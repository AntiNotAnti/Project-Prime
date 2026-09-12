using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// One presentation-only distortion surface. Geometry identity is an opaque
/// renderer resource key; none of these values has gameplay authority.
/// </summary>
internal readonly record struct EnhancedDistortionSubmission
{
    public const int MaximumCount = 128;
    public const float MaximumStrength = ForceFieldVisualProfile.MaximumDistortionStrength;
    public const float MaximumFalloff = ForceFieldVisualProfile.MaximumFresnelPower;

    public EnhancedDistortionSubmission(ulong stableKey, object geometryIdentity,
        Matrix4 transform, float strength, float falloff, float presentationPhase,
        CullingMode cullingMode = CullingMode.Neither)
    {
        ArgumentNullException.ThrowIfNull(geometryIdentity);
        if (!IsFinite(transform))
            throw new ArgumentOutOfRangeException(nameof(transform));
        ValidateRange(strength, float.Epsilon, MaximumStrength, nameof(strength));
        ValidateRange(falloff, float.Epsilon, MaximumFalloff, nameof(falloff));
        if (!float.IsFinite(presentationPhase)
            || presentationPhase < 0 || presentationPhase >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(presentationPhase),
                "Presentation phase must be finite and in [0, 1).");
        }

        StableKey = stableKey;
        GeometryIdentity = geometryIdentity;
        Transform = transform;
        Strength = strength;
        Falloff = falloff;
        PresentationPhase = presentationPhase;
        CullingMode = cullingMode;
        SourceDraw = null;
    }

    public ulong StableKey { get; }
    public object GeometryIdentity { get; }
    public Matrix4 Transform { get; }
    public float Strength { get; }
    public float Falloff { get; }
    public float PresentationPhase { get; }
    public CullingMode CullingMode { get; }
    internal DrawSubmission? SourceDraw { get; private init; }

    internal static EnhancedDistortionSubmission FromDraw(ulong stableKey,
        DrawSubmission draw, float strength, float falloff,
        float presentationPhase)
    {
        ArgumentNullException.ThrowIfNull(draw);
        if (draw.GeometryIdentity is null)
            throw new ArgumentException(
                "Distortion draw requires captured geometry.", nameof(draw));
        return new EnhancedDistortionSubmission(stableKey,
            draw.GeometryIdentity, draw.Transform, strength, falloff,
            presentationPhase, draw.Material.CullingMode)
        {
            SourceDraw = draw
        };
    }

    /// <summary>
    /// Materializes the existing force-field profile at the caller's captured
    /// presentation time. Fresnel power is also the conservative edge falloff
    /// for the distortion mask; collision and active state remain elsewhere.
    /// </summary>
    public static EnhancedDistortionSubmission FromForceField(ulong stableKey,
        object geometryIdentity, Matrix4 transform, ForceFieldVisualProfile profile,
        TimeSpan presentationTime)
    {
        ForceFieldVisualSample sample = profile.Sample(presentationTime, stableKey);
        return new EnhancedDistortionSubmission(stableKey, geometryIdentity, transform,
            profile.DistortionStrength, profile.FresnelPower, sample.NoisePhase);
    }

    internal bool SameSource(in EnhancedDistortionSubmission other)
        => StableKey == other.StableKey
            && ReferenceEquals(GeometryIdentity, other.GeometryIdentity)
            && Transform == other.Transform
            && Strength.Equals(other.Strength)
            && Falloff.Equals(other.Falloff)
            && PresentationPhase.Equals(other.PresentationPhase)
            && CullingMode == other.CullingMode
            && ReferenceEquals(SourceDraw, other.SourceDraw);

    private static void ValidateRange(float value, float minimum, float maximum,
        string parameterName)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static bool IsFinite(Matrix4 value)
        => IsFinite(value.Row0) && IsFinite(value.Row1)
            && IsFinite(value.Row2) && IsFinite(value.Row3);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);
}

/// <summary>
/// Mutable single-producer staging storage. It always retains the lowest 128
/// stable keys in ascending order, so capacity admission is independent of
/// producer traversal order. A conflicting duplicate key is an authoring bug.
/// </summary>
internal sealed class EnhancedDistortionSubmissionBuffer
{
    private readonly List<EnhancedDistortionSubmission> _items;

    public EnhancedDistortionSubmissionBuffer(int capacity = EnhancedDistortionSubmission.MaximumCount)
    {
        if (capacity < 1 || capacity > EnhancedDistortionSubmission.MaximumCount)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _items = new List<EnhancedDistortionSubmission>(capacity);
    }

    public int Capacity { get; }
    public int Count => _items.Count;

    public bool TryAdd(EnhancedDistortionSubmission submission)
    {
        if (submission.GeometryIdentity is null)
            throw new ArgumentException("Distortion submission must be initialized.", nameof(submission));

        int index = FindInsertionIndex(submission.StableKey);
        if (index < _items.Count && _items[index].StableKey == submission.StableKey)
        {
            if (_items[index].SameSource(submission)) return false;
            throw new InvalidOperationException(
                $"Distortion stable key {submission.StableKey} identifies conflicting sources.");
        }
        if (_items.Count == Capacity && index == _items.Count) return false;

        _items.Insert(index, submission);
        if (_items.Count > Capacity) _items.RemoveAt(Capacity);
        return index < Capacity;
    }

    public EnhancedDistortionSubmissionBatch Seal()
        => _items.Count == 0
            ? EnhancedDistortionSubmissionBatch.Empty
            : new EnhancedDistortionSubmissionBatch(_items.ToArray());

    public void Clear() => _items.Clear();

    private int FindInsertionIndex(ulong stableKey)
    {
        int low = 0;
        int high = _items.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (_items[middle].StableKey < stableKey) low = middle + 1;
            else high = middle;
        }
        return low;
    }
}

/// <summary>An immutable, deterministically ordered distortion frame snapshot.</summary>
internal sealed class EnhancedDistortionSubmissionBatch
{
    private readonly EnhancedDistortionSubmission[] _items;
    private readonly IReadOnlyList<EnhancedDistortionSubmission> _view;

    internal EnhancedDistortionSubmissionBatch(EnhancedDistortionSubmission[] items)
    {
        _items = (EnhancedDistortionSubmission[])items.Clone();
        _view = Array.AsReadOnly(_items);
    }

    public static EnhancedDistortionSubmissionBatch Empty { get; }
        = new(Array.Empty<EnhancedDistortionSubmission>());

    public int Count => _items.Length;
    public bool HasSources => _items.Length != 0;
    public IReadOnlyList<EnhancedDistortionSubmission> Items => _view;
}

[Flags]
internal enum EnhancedDistortionSampleCounts : byte
{
    None = 0,
    One = 1 << 0,
    Two = 1 << 1,
    Four = 1 << 2,
    Eight = 1 << 3
}

internal readonly record struct EnhancedDistortionFormatCapabilities(
    bool ColorTarget,
    bool Sampled,
    EnhancedDistortionSampleCounts SampleCounts)
{
    private const EnhancedDistortionSampleCounts All
        = EnhancedDistortionSampleCounts.One | EnhancedDistortionSampleCounts.Two
            | EnhancedDistortionSampleCounts.Four | EnhancedDistortionSampleCounts.Eight;

    public bool Supports(int sampleCount)
    {
        if ((SampleCounts & ~All) != 0)
            throw new ArgumentOutOfRangeException(nameof(SampleCounts));
        EnhancedDistortionSampleCounts flag = sampleCount switch
        {
            1 => EnhancedDistortionSampleCounts.One,
            2 => EnhancedDistortionSampleCounts.Two,
            4 => EnhancedDistortionSampleCounts.Four,
            8 => EnhancedDistortionSampleCounts.Eight,
            _ => throw new ArgumentOutOfRangeException(nameof(sampleCount))
        };
        return ColorTarget && Sampled
            && SampleCounts.HasFlag(EnhancedDistortionSampleCounts.One)
            && SampleCounts.HasFlag(flag);
    }
}

internal readonly record struct EnhancedDistortionCapabilities(
    EnhancedDistortionFormatCapabilities Rg16Float,
    EnhancedDistortionFormatCapabilities Rgba16Float);

internal enum EnhancedDistortionTargetFormat : byte
{
    None,
    Rg16Float,
    Rgba16Float
}

internal enum EnhancedDistortionDisabledReason : byte
{
    None,
    NotRequested,
    NoSources,
    UnsupportedFormatOrSampleCount,
    CachedAllocationFailure
}

internal readonly record struct EnhancedDistortionAllocationScope(
    uint Width,
    uint Height,
    int RenderSampleCount);

internal readonly record struct EnhancedDistortionTargetConfiguration(
    uint Width,
    uint Height,
    EnhancedDistortionTargetFormat Format,
    int RenderSampleCount,
    int ResolveSampleCount)
{
    public EnhancedDistortionAllocationScope Scope
        => new(Width, Height, RenderSampleCount);
    public bool RequiresResolve => RenderSampleCount > 1;
}

/// <summary>
/// Pure target selection. The vector render target always matches scene-depth
/// samples; when that is multisampled it resolves to one sample for the warp.
/// </summary>
internal readonly record struct EnhancedDistortionTargetPlan(
    bool Enabled,
    EnhancedDistortionDisabledReason DisabledReason,
    EnhancedDistortionTargetConfiguration? Configuration)
{
    public static Vector2 NeutralOffset => Vector2.Zero;

    public static Vector2 ClampCompositeOffset(Vector2 accumulatedOffset)
        => EnhancedDistortionMath.ClampOffset(
            accumulatedOffset,
            EnhancedDistortionSubmission.MaximumStrength);

    public static EnhancedDistortionTargetPlan Create(uint width, uint height,
        int sceneDepthSampleCount, bool requested, int sourceCount,
        EnhancedDistortionCapabilities capabilities,
        EnhancedDistortionFailureCache? failureCache = null)
    {
        if (width == 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height == 0) throw new ArgumentOutOfRangeException(nameof(height));
        ValidateSampleCount(sceneDepthSampleCount);
        if (sourceCount < 0 || sourceCount > EnhancedDistortionSubmission.MaximumCount)
            throw new ArgumentOutOfRangeException(nameof(sourceCount));
        if (!requested) return Disabled(EnhancedDistortionDisabledReason.NotRequested);
        if (sourceCount == 0) return Disabled(EnhancedDistortionDisabledReason.NoSources);

        bool supported = false;
        bool cached = false;
        foreach ((EnhancedDistortionTargetFormat format,
            EnhancedDistortionFormatCapabilities formatCapabilities) in Candidates(capabilities))
        {
            if (!formatCapabilities.Supports(sceneDepthSampleCount)) continue;
            supported = true;
            var configuration = new EnhancedDistortionTargetConfiguration(width, height,
                format, sceneDepthSampleCount, ResolveSampleCount: 1);
            if (failureCache != null
                && !failureCache.ShouldAttempt(configuration, requested, hasSources: true))
            {
                cached = true;
                continue;
            }
            return new EnhancedDistortionTargetPlan(true,
                EnhancedDistortionDisabledReason.None, configuration);
        }

        return Disabled(supported && cached
            ? EnhancedDistortionDisabledReason.CachedAllocationFailure
            : EnhancedDistortionDisabledReason.UnsupportedFormatOrSampleCount);
    }

    private static EnhancedDistortionTargetPlan Disabled(
        EnhancedDistortionDisabledReason reason)
        => new(false, reason, null);

    private static IEnumerable<(EnhancedDistortionTargetFormat,
        EnhancedDistortionFormatCapabilities)> Candidates(
        EnhancedDistortionCapabilities capabilities)
    {
        yield return (EnhancedDistortionTargetFormat.Rg16Float, capabilities.Rg16Float);
        yield return (EnhancedDistortionTargetFormat.Rgba16Float, capabilities.Rgba16Float);
    }

    private static void ValidateSampleCount(int sampleCount)
    {
        if (sampleCount is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
    }
}

/// <summary>
/// Device-owned and distortion-local. At most the two candidate formats for
/// one dimensions/sample scope are remembered; resize/sample changes retry.
/// HDR, bloom, and per-frame source eligibility never enter this cache.
/// </summary>
internal sealed class EnhancedDistortionFailureCache
{
    private readonly HashSet<EnhancedDistortionTargetConfiguration> _failed = new();
    private EnhancedDistortionAllocationScope? _scope;

    public int FailureCount => _failed.Count;

    public bool ShouldAttempt(EnhancedDistortionTargetConfiguration configuration,
        bool requested, bool hasSources)
    {
        SynchronizeScope(configuration.Scope);
        return requested && hasSources && !_failed.Contains(configuration);
    }

    public void RecordFailure(EnhancedDistortionTargetConfiguration configuration)
    {
        if (configuration.Format == EnhancedDistortionTargetFormat.None)
            throw new ArgumentOutOfRangeException(nameof(configuration));
        SynchronizeScope(configuration.Scope);
        _failed.Add(configuration);
    }

    public void RecordSuccess(EnhancedDistortionTargetConfiguration configuration)
    {
        SynchronizeScope(configuration.Scope);
        _failed.Remove(configuration);
    }

    public bool Contains(EnhancedDistortionTargetConfiguration configuration)
        => _scope == configuration.Scope && _failed.Contains(configuration);

    public void ResetForDeviceLoss()
    {
        _failed.Clear();
        _scope = null;
    }

    private void SynchronizeScope(EnhancedDistortionAllocationScope scope)
    {
        if (_scope.HasValue && _scope.Value == scope) return;
        _failed.Clear();
        _scope = scope;
    }
}

internal enum EnhancedDistortionPassKind : byte
{
    VectorRaster,
    SceneWarp,
    Bloom,
    ToneMap
}

internal static class SdlGpuDistortionTransformPolicy
{
    public static bool UsesMatrixStack(int matrixStackCount)
    {
        if (matrixStackCount < 0 || matrixStackCount > 31)
            throw new ArgumentOutOfRangeException(nameof(matrixStackCount));
        return matrixStackCount > 0;
    }
}

/// <summary>
/// Fixed VE17 ordering contract, not a render graph. A neutral/no-source frame
/// bypasses both distortion passes; an enabled frame must warp before bloom
/// and tone mapping.
/// </summary>
internal sealed class EnhancedDistortionPassPlan
{
    private readonly IReadOnlyList<EnhancedDistortionPassKind> _passes;

    private EnhancedDistortionPassPlan(EnhancedDistortionPassKind[] passes)
        => _passes = Array.AsReadOnly(passes);

    public IReadOnlyList<EnhancedDistortionPassKind> Passes => _passes;
    public bool UsesDistortion => _passes.Count > 2;

    public static EnhancedDistortionPassPlan Create(bool distortionEnabled)
    {
        EnhancedDistortionPassKind[] passes = distortionEnabled
            ? [EnhancedDistortionPassKind.VectorRaster,
                EnhancedDistortionPassKind.SceneWarp,
                EnhancedDistortionPassKind.Bloom,
                EnhancedDistortionPassKind.ToneMap]
            : [EnhancedDistortionPassKind.Bloom, EnhancedDistortionPassKind.ToneMap];
        Validate(passes);
        return new EnhancedDistortionPassPlan(passes);
    }

    public static void Validate(IReadOnlyList<EnhancedDistortionPassKind> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);
        int vectors = IndexOf(passes, EnhancedDistortionPassKind.VectorRaster);
        int warp = IndexOf(passes, EnhancedDistortionPassKind.SceneWarp);
        int bloom = IndexOf(passes, EnhancedDistortionPassKind.Bloom);
        int toneMap = IndexOf(passes, EnhancedDistortionPassKind.ToneMap);
        if (bloom < 0 || toneMap < 0 || bloom >= toneMap)
            throw new InvalidOperationException("Bloom must precede tone mapping.");
        if ((vectors < 0) != (warp < 0))
            throw new InvalidOperationException(
                "Distortion vector raster and scene warp must be enabled together.");
        if (vectors >= 0 && !(vectors < warp && warp < bloom && warp < toneMap))
            throw new InvalidOperationException(
                "Distortion must raster vectors, then warp before bloom and tone mapping.");
        if (HasDuplicate(passes))
            throw new InvalidOperationException("Distortion pass plan contains a duplicate stage.");
    }

    private static int IndexOf(IReadOnlyList<EnhancedDistortionPassKind> passes,
        EnhancedDistortionPassKind value)
    {
        for (int i = 0; i < passes.Count; i++)
            if (passes[i] == value) return i;
        return -1;
    }

    private static bool HasDuplicate(IReadOnlyList<EnhancedDistortionPassKind> passes)
    {
        for (int i = 0; i < passes.Count; i++)
            for (int j = i + 1; j < passes.Count; j++)
                if (passes[i] == passes[j]) return true;
        return false;
    }
}
