using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace MphRead;

[Flags]
public enum CosmeticRenderFeatures : byte
{
    None = 0,
    MaterialOverlay = 1 << 0,
    Particles = 1 << 1,
    Ribbons = 1 << 2,
    Attachments = 1 << 3,
    Distortion = 1 << 4,
    LocalLight = 1 << 5
}

public enum CosmeticPrimitiveKind : byte
{
    Particle,
    Ribbon,
    Attachment,
    Distortion,
    LocalLight
}

/// <summary>
/// Backend-neutral cosmetic draw data. Geometry and texture identity stay in
/// the renderer's existing resource systems; this value only describes one
/// bounded presentation request.
/// </summary>
public readonly record struct CosmeticPrimitiveSubmission
{
    public const int MaximumRibbonSegments = 32;

    public CosmeticPrimitiveSubmission(ulong stableKey, byte playerSlot,
        CosmeticPrimitiveKind kind, Vector3 start, Vector3 end, Vector3 color,
        float intensity, int segmentCount = 1, string? assetKey = null,
        Matrix4? localTransform = null, float size = .1f)
    {
        if (!IsFinite(start) || !IsFinite(end) || !IsFinite(color))
            throw new ArgumentOutOfRangeException(nameof(start));
        if (!float.IsFinite(intensity) || intensity < 0 || intensity > 16)
            throw new ArgumentOutOfRangeException(nameof(intensity));
        if (!float.IsFinite(size) || size < .005f || size > 2)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (segmentCount < 1 || segmentCount > MaximumRibbonSegments)
            throw new ArgumentOutOfRangeException(nameof(segmentCount));
        if (kind != CosmeticPrimitiveKind.Ribbon && segmentCount != 1)
            throw new ArgumentException("Only ribbons may contain multiple segments.", nameof(segmentCount));
        if (assetKey != null && !IsValidAssetKey(assetKey))
            throw new ArgumentException("Cosmetic primitive asset key is invalid.", nameof(assetKey));
        Matrix4 transform = localTransform ?? Matrix4.Identity;
        if (!IsFinite(transform))
            throw new ArgumentOutOfRangeException(nameof(localTransform));

        StableKey = stableKey;
        PlayerSlot = playerSlot;
        Kind = kind;
        Start = start;
        End = end;
        Color = color;
        Intensity = intensity;
        Size = size;
        SegmentCount = segmentCount;
        AssetKey = assetKey;
        LocalTransform = transform;
    }

    public ulong StableKey { get; }
    public byte PlayerSlot { get; }
    public CosmeticPrimitiveKind Kind { get; }
    public Vector3 Start { get; }
    public Vector3 End { get; }
    public Vector3 Color { get; }
    public float Intensity { get; }
    /// <summary>Validated world-space size for particle and ribbon samples.</summary>
    public float Size { get; }
    public int SegmentCount { get; }
    /// <summary>
    /// Stable built-in atlas or mesh identity. The renderer resolves it from
    /// its packaged resources; authored content cannot provide a file path.
    /// </summary>
    public string? AssetKey { get; }
    /// <summary>Bounded authored transform relative to the resolved anchor.</summary>
    public Matrix4 LocalTransform { get; }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Matrix4 value)
        => IsFinite(value.Row0) && IsFinite(value.Row1)
            && IsFinite(value.Row2) && IsFinite(value.Row3);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y)
            && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static bool IsValidAssetKey(string value)
    {
        if (value.Length is < 1 or > 96 || value[0] == '.' || value[^1] == '.')
            return false;
        foreach (char character in value)
        {
            if (character is not (>= 'a' and <= 'z' or >= '0' and <= '9'
                or '.' or '_' or '-')) return false;
        }
        return !value.Contains("..", StringComparison.Ordinal);
    }
}

/// <summary>Hard cosmetic-only limits, stricter than renderer-wide limits.</summary>
public readonly record struct CosmeticBudgetLimits
{
    public static CosmeticBudgetLimits Default { get; } = new(
        particleEmitters: 32, particles: 512, ribbonSystems: 24,
        ribbonSegments: 384, attachmentMeshes: 32, distortionSources: 8,
        localLights: 8);

    public CosmeticBudgetLimits(int particleEmitters, int particles,
        int ribbonSystems, int ribbonSegments, int attachmentMeshes,
        int distortionSources, int localLights)
    {
        Validate(particleEmitters, nameof(particleEmitters));
        Validate(particles, nameof(particles));
        Validate(ribbonSystems, nameof(ribbonSystems));
        Validate(ribbonSegments, nameof(ribbonSegments));
        Validate(attachmentMeshes, nameof(attachmentMeshes));
        Validate(distortionSources, nameof(distortionSources));
        Validate(localLights, nameof(localLights));
        ParticleEmitters = particleEmitters;
        Particles = particles;
        RibbonSystems = ribbonSystems;
        RibbonSegments = ribbonSegments;
        AttachmentMeshes = attachmentMeshes;
        DistortionSources = distortionSources;
        LocalLights = localLights;
    }

    public int ParticleEmitters { get; }
    public int Particles { get; }
    public int RibbonSystems { get; }
    public int RibbonSegments { get; }
    public int AttachmentMeshes { get; }
    public int DistortionSources { get; }
    public int LocalLights { get; }

    private static void Validate(int value, string name)
    {
        if (value < 0 || value > 4096) throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct CosmeticBudgetRequest
{
    public CosmeticBudgetRequest(ulong stableKey, byte playerSlot,
        bool localPlayer, bool focusTarget, float distanceSquared,
        int particleEmitters, int particles, int ribbonSystems,
        int ribbonSegments, int attachmentMeshes, int distortionSources,
        int localLights)
    {
        if (!float.IsFinite(distanceSquared) || distanceSquared < 0)
            throw new ArgumentOutOfRangeException(nameof(distanceSquared));
        Validate(particleEmitters, nameof(particleEmitters));
        Validate(particles, nameof(particles));
        Validate(ribbonSystems, nameof(ribbonSystems));
        Validate(ribbonSegments, nameof(ribbonSegments));
        Validate(attachmentMeshes, nameof(attachmentMeshes));
        Validate(distortionSources, nameof(distortionSources));
        Validate(localLights, nameof(localLights));
        if (ribbonSystems == 0 && ribbonSegments != 0)
            throw new ArgumentException("Ribbon segments require a ribbon system.", nameof(ribbonSegments));

        StableKey = stableKey;
        PlayerSlot = playerSlot;
        LocalPlayer = localPlayer;
        FocusTarget = focusTarget;
        DistanceSquared = distanceSquared;
        ParticleEmitters = particleEmitters;
        Particles = particles;
        RibbonSystems = ribbonSystems;
        RibbonSegments = ribbonSegments;
        AttachmentMeshes = attachmentMeshes;
        DistortionSources = distortionSources;
        LocalLights = localLights;
    }

    public ulong StableKey { get; }
    public byte PlayerSlot { get; }
    public bool LocalPlayer { get; }
    public bool FocusTarget { get; }
    public float DistanceSquared { get; }
    public int ParticleEmitters { get; }
    public int Particles { get; }
    public int RibbonSystems { get; }
    public int RibbonSegments { get; }
    public int AttachmentMeshes { get; }
    public int DistortionSources { get; }
    public int LocalLights { get; }

    private static void Validate(int value, string name)
    {
        if (value < 0 || value > 4096) throw new ArgumentOutOfRangeException(name);
    }
}

public readonly record struct CosmeticBudgetAllowance(
    ulong StableKey,
    byte PlayerSlot,
    int ParticleEmitters,
    int Particles,
    int RibbonSystems,
    int RibbonSegments,
    int AttachmentMeshes,
    int DistortionSources,
    int LocalLights)
{
    public CosmeticRenderFeatures Features
    {
        get
        {
            CosmeticRenderFeatures features = CosmeticRenderFeatures.None;
            if (ParticleEmitters != 0 && Particles != 0) features |= CosmeticRenderFeatures.Particles;
            if (RibbonSystems != 0 && RibbonSegments != 0) features |= CosmeticRenderFeatures.Ribbons;
            if (AttachmentMeshes != 0) features |= CosmeticRenderFeatures.Attachments;
            if (DistortionSources != 0) features |= CosmeticRenderFeatures.Distortion;
            if (LocalLights != 0) features |= CosmeticRenderFeatures.LocalLight;
            return features;
        }
    }
}

/// <summary>
/// Deterministic admission for one frame. Local player, focus target, distance,
/// slot, and stable request key form the complete ordering. Input traversal
/// order never changes which requests receive the bounded resources.
/// </summary>
public static class CosmeticBudgetArbiter
{
    public const int MaximumRequests = 128;

    public static IReadOnlyList<CosmeticBudgetAllowance> Admit(
        IReadOnlyList<CosmeticBudgetRequest> requests,
        CosmeticBudgetLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count > MaximumRequests)
            throw new ArgumentOutOfRangeException(nameof(requests));

        CosmeticBudgetLimits available = limits ?? CosmeticBudgetLimits.Default;
        var ordered = new CosmeticBudgetRequest[requests.Count];
        var stableKeys = new HashSet<ulong>();
        for (int i = 0; i < requests.Count; i++)
        {
            ordered[i] = requests[i];
            if (!stableKeys.Add(ordered[i].StableKey))
                throw new ArgumentException(
                    $"Duplicate cosmetic budget key {ordered[i].StableKey}.",
                    nameof(requests));
        }
        Array.Sort(ordered, Compare);

        var result = new CosmeticBudgetAllowance[ordered.Length];
        int emitters = available.ParticleEmitters;
        int particles = available.Particles;
        int ribbons = available.RibbonSystems;
        int segments = available.RibbonSegments;
        int attachments = available.AttachmentMeshes;
        int distortion = available.DistortionSources;
        int lights = available.LocalLights;
        for (int i = 0; i < ordered.Length; i++)
        {
            CosmeticBudgetRequest request = ordered[i];
            int grantedEmitters = Math.Min(request.ParticleEmitters, emitters);
            int grantedParticles = grantedEmitters == 0
                ? 0 : Math.Min(request.Particles, particles);
            int grantedRibbons = Math.Min(request.RibbonSystems, ribbons);
            int grantedSegments = grantedRibbons == 0
                ? 0 : Math.Min(request.RibbonSegments, segments);
            int grantedAttachments = Math.Min(request.AttachmentMeshes, attachments);
            int grantedDistortion = Math.Min(request.DistortionSources, distortion);
            int grantedLights = Math.Min(request.LocalLights, lights);
            result[i] = new CosmeticBudgetAllowance(request.StableKey,
                request.PlayerSlot, grantedEmitters, grantedParticles,
                grantedRibbons, grantedSegments, grantedAttachments,
                grantedDistortion, grantedLights);
            emitters -= grantedEmitters;
            particles -= grantedParticles;
            ribbons -= grantedRibbons;
            segments -= grantedSegments;
            attachments -= grantedAttachments;
            distortion -= grantedDistortion;
            lights -= grantedLights;
        }
        return Array.AsReadOnly(result);
    }

    /// <summary>Allocation-free frame-loop overload. The request span is
    /// reordered into deterministic priority order.</summary>
    public static int Admit(Span<CosmeticBudgetRequest> requests,
        Span<CosmeticBudgetAllowance> result,
        CosmeticBudgetLimits? limits = null)
    {
        if (requests.Length > MaximumRequests || result.Length < requests.Length)
            throw new ArgumentOutOfRangeException(nameof(requests));
        for (int i = 0; i < requests.Length; i++)
        {
            for (int duplicate = 0; duplicate < i; duplicate++)
            {
                if (requests[duplicate].StableKey == requests[i].StableKey)
                    throw new ArgumentException(
                        $"Duplicate cosmetic budget key {requests[i].StableKey}.",
                        nameof(requests));
            }
            CosmeticBudgetRequest value = requests[i];
            int insertion = i;
            while (insertion > 0 && Compare(value, requests[insertion - 1]) < 0)
            {
                requests[insertion] = requests[insertion - 1];
                insertion--;
            }
            requests[insertion] = value;
        }

        CosmeticBudgetLimits available = limits ?? CosmeticBudgetLimits.Default;
        int emitters = available.ParticleEmitters;
        int particles = available.Particles;
        int ribbons = available.RibbonSystems;
        int segments = available.RibbonSegments;
        int attachments = available.AttachmentMeshes;
        int distortion = available.DistortionSources;
        int lights = available.LocalLights;
        for (int i = 0; i < requests.Length; i++)
        {
            CosmeticBudgetRequest request = requests[i];
            int grantedEmitters = Math.Min(request.ParticleEmitters, emitters);
            int grantedParticles = grantedEmitters == 0
                ? 0 : Math.Min(request.Particles, particles);
            int grantedRibbons = Math.Min(request.RibbonSystems, ribbons);
            int grantedSegments = grantedRibbons == 0
                ? 0 : Math.Min(request.RibbonSegments, segments);
            int grantedAttachments = Math.Min(request.AttachmentMeshes, attachments);
            int grantedDistortion = Math.Min(request.DistortionSources, distortion);
            int grantedLights = Math.Min(request.LocalLights, lights);
            result[i] = new CosmeticBudgetAllowance(request.StableKey,
                request.PlayerSlot, grantedEmitters, grantedParticles,
                grantedRibbons, grantedSegments, grantedAttachments,
                grantedDistortion, grantedLights);
            emitters -= grantedEmitters;
            particles -= grantedParticles;
            ribbons -= grantedRibbons;
            segments -= grantedSegments;
            attachments -= grantedAttachments;
            distortion -= grantedDistortion;
            lights -= grantedLights;
        }
        return requests.Length;
    }

    private static int Compare(CosmeticBudgetRequest left,
        CosmeticBudgetRequest right)
    {
        int comparison = right.LocalPlayer.CompareTo(left.LocalPlayer);
        if (comparison != 0) return comparison;
        comparison = right.FocusTarget.CompareTo(left.FocusTarget);
        if (comparison != 0) return comparison;
        comparison = left.DistanceSquared.CompareTo(right.DistanceSquared);
        if (comparison != 0) return comparison;
        comparison = left.PlayerSlot.CompareTo(right.PlayerSlot);
        if (comparison != 0) return comparison;
        return left.StableKey.CompareTo(right.StableKey);
    }
}

/// <summary>
/// Reusable single-producer staging buffer for already budgeted cosmetic
/// primitives. It seals in stable-key order so backend output does not depend
/// on player traversal order.
/// </summary>
public sealed class CosmeticPrimitiveSubmissionBuffer
{
    // Request admission and expanded primitive staging are separate limits.
    // One ribbon remains one staged primitive and expands only while flushing.
    public const int MaximumCapacity = 512 + 24 + 32 + 8;

    private readonly List<CosmeticPrimitiveSubmission> _items;
    private readonly IReadOnlyList<CosmeticPrimitiveSubmission> _view;
    private readonly CosmeticPrimitiveSubmission[] _sortScratch;
    private bool _sealed;

    public CosmeticPrimitiveSubmissionBuffer(int capacity = MaximumCapacity)
    {
        if (capacity < 1 || capacity > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
        _items = new List<CosmeticPrimitiveSubmission>(capacity);
        _view = _items.AsReadOnly();
        _sortScratch = new CosmeticPrimitiveSubmission[capacity];
    }

    public int Capacity { get; }
    public int Count => _items.Count;

    public bool TryAdd(CosmeticPrimitiveSubmission submission)
    {
        if (_sealed)
            throw new InvalidOperationException("Cosmetic primitive buffer is sealed.");
        if (_items.Count == Capacity) return false;
        _items.Add(submission);
        return true;
    }

    public IReadOnlyList<CosmeticPrimitiveSubmission> Seal()
    {
        if (!_sealed)
        {
            SortStableByKey();
            int write = 0;
            for (int read = 0; read < _items.Count; read++)
            {
                CosmeticPrimitiveSubmission value = _items[read];
                if (write != 0 && _items[write - 1].StableKey == value.StableKey)
                {
                    if (_items[write - 1] == value) continue;
                    throw new InvalidOperationException(
                        $"Cosmetic primitive key {value.StableKey} identifies conflicting submissions.");
                }
                _items[write++] = value;
            }
            if (write != _items.Count)
                _items.RemoveRange(write, _items.Count - write);
            _sealed = true;
        }
        return _view;
    }

    public void Clear()
    {
        _items.Clear();
        _sealed = false;
    }

    private void SortStableByKey()
    {
        int count = _items.Count;
        for (int width = 1; width < count; width *= 2)
        {
            for (int start = 0; start < count; start += width * 2)
            {
                int middle = Math.Min(start + width, count);
                int end = Math.Min(start + width * 2, count);
                int left = start;
                int right = middle;
                for (int output = start; output < end; output++)
                {
                    if (right >= end || left < middle
                        && _items[left].StableKey <= _items[right].StableKey)
                    {
                        _sortScratch[output] = _items[left++];
                    }
                    else
                    {
                        _sortScratch[output] = _items[right++];
                    }
                }
            }
            for (int i = 0; i < count; i++) _items[i] = _sortScratch[i];
        }
    }
}
