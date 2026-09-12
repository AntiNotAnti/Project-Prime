using System;

namespace MphRead;

[Flags]
public enum RenderGraphResource : uint
{
    None = 0,
    FrameData = 1 << 0,
    Geometry = 1 << 1,
    Textures = 1 << 2,
    ShadowMap = 1 << 3,
    SurfaceData = 1 << 4,
    AmbientOcclusion = 1 << 5,
    SceneColor = 1 << 6,
    SceneDepth = 1 << 7,
    SceneStencil = 1 << 8,
    DistortionVectors = 1 << 9,
    BloomEmission = 1 << 10,
    DisplayLinear = 1 << 11,
    SceneCaptureLinear = 1 << 12,
    CaptureOutput = 1 << 13,
    FinalComposite = 1 << 14,
    ReadbackCommands = 1 << 15,
    ReconstructionSource = 1 << 16
}

public enum RenderGraphPassKind : byte
{
    DirectionalShadow,
    SurfaceData,
    AmbientOcclusion,
    Sky,
    Opaque,
    Decal,
    TransparentStencil,
    DepthRebuild,
    TransparentBehind,
    TransparentFront,
    DistortionVectors,
    BloomEmission,
    OriginalHud,
    ScenePostProcess,
    Reconstruction,
    SceneCaptureBase,
    Visor,
    EnhancedHud,
    SceneCaptureTransfer,
    Overlay,
    FinalTransfer,
    SceneReadback,
    FinalReadback
}

public readonly record struct RenderGraphPass(
    RenderGraphPassKind Kind,
    RenderGraphResource Reads,
    RenderGraphResource Writes);

/// <summary>
/// Backend-resolved predicates for one sealed frame. These values describe
/// resources and passes that were already negotiated by the backend; they do
/// not expose mutable presentation or gameplay state to the executor.
/// </summary>
public readonly record struct RenderGraphFeatures(
    bool DirectionalShadow,
    bool SurfaceData,
    bool AmbientOcclusion,
    bool Sky,
    bool Distortion,
    bool Bloom,
    bool OriginalHud,
    bool EnhancedOutput,
    bool SceneCapture,
    bool Visor,
    bool EnhancedHud,
    bool SceneReadback = false,
    bool FinalReadback = false,
    bool Reconstruction = false);

/// <summary>
/// Small reusable execution plan for Project Prime's fixed renderer. The
/// backing storage is allocated once; rebuilding a frame performs no heap
/// allocation and clears unused slots so optional passes cannot leak forward.
/// </summary>
public sealed class RenderExecutionPlan
{
    public const int MaximumPasses = 24;

    private readonly RenderGraphPass[] _passes = new RenderGraphPass[MaximumPasses];
    private int _count;

    public int Count => _count;
    public ReadOnlySpan<RenderGraphPass> Passes => _passes.AsSpan(0, _count);
    public RenderGraphPass this[int index]
        => index >= 0 && index < _count
            ? _passes[index]
            : throw new ArgumentOutOfRangeException(nameof(index));

    internal void Reset() => _count = 0;

    internal void Add(RenderGraphPassKind kind, RenderGraphResource reads,
        RenderGraphResource writes)
    {
        if (_count == _passes.Length)
            throw new InvalidOperationException("Render graph exceeded its bounded pass capacity.");
        _passes[_count++] = new RenderGraphPass(kind, reads, writes);
    }
}

/// <summary>
/// Builds and validates the fixed Project Prime pass graph. This is purposely
/// not a general scheduling framework: order is explicit, deterministic, and
/// preserves the six MPH world passes and both capture tap points.
/// </summary>
public static class RenderGraphLite
{
    public const RenderGraphResource ImportedResources
        = RenderGraphResource.FrameData | RenderGraphResource.Geometry
            | RenderGraphResource.Textures;

    public static void Build(RenderExecutionPlan plan, RenderGraphFeatures features)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ValidateFeatures(features);
        plan.Reset();

        RenderGraphResource lighting = RenderGraphResource.None;
        if (features.DirectionalShadow)
        {
            plan.Add(RenderGraphPassKind.DirectionalShadow,
                RenderGraphResource.FrameData | RenderGraphResource.Geometry
                    | RenderGraphResource.Textures,
                RenderGraphResource.ShadowMap);
            lighting |= RenderGraphResource.ShadowMap;
        }
        if (features.SurfaceData)
        {
            plan.Add(RenderGraphPassKind.SurfaceData,
                RenderGraphResource.FrameData | RenderGraphResource.Geometry
                    | RenderGraphResource.Textures,
                RenderGraphResource.SurfaceData);
            if (features.AmbientOcclusion)
            {
                plan.Add(RenderGraphPassKind.AmbientOcclusion,
                    RenderGraphResource.SurfaceData,
                    RenderGraphResource.AmbientOcclusion);
                lighting |= RenderGraphResource.AmbientOcclusion;
            }
        }
        if (features.Sky)
        {
            plan.Add(RenderGraphPassKind.Sky,
                RenderGraphResource.FrameData | RenderGraphResource.Textures,
                RenderGraphResource.SceneColor);
        }

        RenderGraphResource worldInputs = RenderGraphResource.FrameData
            | RenderGraphResource.Geometry | RenderGraphResource.Textures | lighting;
        if (features.SurfaceData) worldInputs |= RenderGraphResource.SurfaceData;
        plan.Add(RenderGraphPassKind.Opaque,
            worldInputs | (features.Sky ? RenderGraphResource.SceneColor
                : RenderGraphResource.None),
            RenderGraphResource.SceneColor | RenderGraphResource.SceneDepth
                | RenderGraphResource.SceneStencil);
        plan.Add(RenderGraphPassKind.Decal,
            worldInputs | RenderGraphResource.SceneColor
                | RenderGraphResource.SceneDepth,
            RenderGraphResource.SceneColor);
        plan.Add(RenderGraphPassKind.TransparentStencil,
            worldInputs | RenderGraphResource.SceneDepth,
            RenderGraphResource.SceneStencil);
        plan.Add(RenderGraphPassKind.DepthRebuild,
            worldInputs | RenderGraphResource.SceneStencil,
            RenderGraphResource.SceneDepth);
        AddTransparentColorPass(plan, RenderGraphPassKind.TransparentBehind,
            worldInputs);
        AddTransparentColorPass(plan, RenderGraphPassKind.TransparentFront,
            worldInputs);

        if (features.Distortion)
        {
            plan.Add(RenderGraphPassKind.DistortionVectors,
                worldInputs | RenderGraphResource.SceneDepth,
                RenderGraphResource.DistortionVectors);
        }
        if (features.Bloom)
        {
            plan.Add(RenderGraphPassKind.BloomEmission,
                worldInputs | RenderGraphResource.SceneDepth,
                RenderGraphResource.BloomEmission);
        }
        if (features.OriginalHud)
        {
            plan.Add(RenderGraphPassKind.OriginalHud,
                RenderGraphResource.FrameData | RenderGraphResource.Geometry
                    | RenderGraphResource.Textures | RenderGraphResource.SceneColor
                    | RenderGraphResource.SceneDepth,
                RenderGraphResource.SceneColor | RenderGraphResource.SceneDepth);
        }

        RenderGraphResource postReads = RenderGraphResource.SceneColor
            | RenderGraphResource.SceneDepth;
        if (features.Distortion) postReads |= RenderGraphResource.DistortionVectors;
        if (features.Bloom) postReads |= RenderGraphResource.BloomEmission;
        plan.Add(RenderGraphPassKind.ScenePostProcess, postReads,
            features.Reconstruction ? RenderGraphResource.ReconstructionSource
                : features.EnhancedOutput ? RenderGraphResource.DisplayLinear
                : RenderGraphResource.FinalComposite);

        if (features.Reconstruction)
        {
            plan.Add(RenderGraphPassKind.Reconstruction,
                RenderGraphResource.ReconstructionSource,
                RenderGraphResource.DisplayLinear);
        }

        if (features.EnhancedOutput)
        {
            if (features.SceneCapture)
            {
                plan.Add(RenderGraphPassKind.SceneCaptureBase,
                    RenderGraphResource.SceneColor,
                    RenderGraphResource.SceneCaptureLinear);
            }
            if (features.Visor)
            {
                plan.Add(RenderGraphPassKind.Visor,
                    RenderGraphResource.DisplayLinear,
                    RenderGraphResource.DisplayLinear);
            }
            if (features.EnhancedHud)
            {
                RenderGraphResource hudTargets = RenderGraphResource.DisplayLinear;
                if (features.SceneCapture)
                    hudTargets |= RenderGraphResource.SceneCaptureLinear;
                plan.Add(RenderGraphPassKind.EnhancedHud,
                    RenderGraphResource.FrameData | RenderGraphResource.Geometry
                        | RenderGraphResource.Textures | hudTargets,
                    hudTargets);
            }
            if (features.SceneCapture)
            {
                plan.Add(RenderGraphPassKind.SceneCaptureTransfer,
                    RenderGraphResource.SceneCaptureLinear,
                    RenderGraphResource.CaptureOutput);
            }
        }

        RenderGraphResource overlayTarget = features.EnhancedOutput
            ? RenderGraphResource.DisplayLinear : RenderGraphResource.FinalComposite;
        plan.Add(RenderGraphPassKind.Overlay,
            RenderGraphResource.FrameData | RenderGraphResource.Textures | overlayTarget,
            overlayTarget);
        if (features.EnhancedOutput)
        {
            plan.Add(RenderGraphPassKind.FinalTransfer,
                RenderGraphResource.DisplayLinear,
                RenderGraphResource.FinalComposite);
        }
        if (features.SceneReadback)
        {
            plan.Add(RenderGraphPassKind.SceneReadback,
                features.EnhancedOutput ? RenderGraphResource.CaptureOutput
                    : RenderGraphResource.SceneColor,
                RenderGraphResource.ReadbackCommands);
        }
        if (features.FinalReadback)
        {
            plan.Add(RenderGraphPassKind.FinalReadback,
                RenderGraphResource.FinalComposite,
                RenderGraphResource.ReadbackCommands);
        }

        Validate(plan.Passes, ImportedResources);
    }

    public static void Validate(ReadOnlySpan<RenderGraphPass> passes,
        RenderGraphResource importedResources)
    {
        RenderGraphResource available = importedResources;
        int worldIndex = 0;
        ReadOnlySpan<RenderGraphPassKind> worldOrder =
        [
            RenderGraphPassKind.Opaque,
            RenderGraphPassKind.Decal,
            RenderGraphPassKind.TransparentStencil,
            RenderGraphPassKind.DepthRebuild,
            RenderGraphPassKind.TransparentBehind,
            RenderGraphPassKind.TransparentFront
        ];
        for (int i = 0; i < passes.Length; i++)
        {
            RenderGraphPass pass = passes[i];
            RenderGraphResource missing = pass.Reads & ~available;
            if (missing != RenderGraphResource.None)
            {
                throw new InvalidOperationException(
                    $"Render pass {pass.Kind} reads unavailable resources {missing}.");
            }
            if (pass.Writes == RenderGraphResource.None)
                throw new InvalidOperationException($"Render pass {pass.Kind} has no output.");
            available |= pass.Writes;

            if (worldIndex < worldOrder.Length && pass.Kind == worldOrder[worldIndex])
                worldIndex++;
            else if (IsWorldPass(pass.Kind))
                throw new InvalidOperationException("The six MPH world passes are out of order.");
        }
        if (worldIndex != worldOrder.Length)
            throw new InvalidOperationException("The render graph is missing an MPH world pass.");
    }

    private static void ValidateFeatures(RenderGraphFeatures features)
    {
        if (features.AmbientOcclusion && !features.SurfaceData)
            throw new ArgumentException("Ambient occlusion requires surface data.", nameof(features));
        if (!features.EnhancedOutput
            && (features.SceneCapture || features.Visor || features.EnhancedHud))
        {
            throw new ArgumentException(
                "Enhanced capture, visor, and HUD passes require Enhanced output.",
                nameof(features));
        }
        if (features.EnhancedOutput && features.OriginalHud)
            throw new ArgumentException("Original HUD composition cannot target Enhanced output.", nameof(features));
        if (features.Reconstruction && !features.EnhancedOutput)
            throw new ArgumentException(
                "Spatial reconstruction requires Enhanced output.",
                nameof(features));
        if (features.EnhancedOutput && features.SceneReadback
            && !features.SceneCapture)
            throw new ArgumentException(
                "Enhanced scene readback requires the SDR scene-capture branch.",
                nameof(features));
    }

    private static void AddTransparentColorPass(RenderExecutionPlan plan,
        RenderGraphPassKind kind, RenderGraphResource worldInputs)
        => plan.Add(kind,
            worldInputs | RenderGraphResource.SceneColor
                | RenderGraphResource.SceneDepth | RenderGraphResource.SceneStencil,
            RenderGraphResource.SceneColor);

    private static bool IsWorldPass(RenderGraphPassKind kind)
        => kind is RenderGraphPassKind.Opaque or RenderGraphPassKind.Decal
            or RenderGraphPassKind.TransparentStencil or RenderGraphPassKind.DepthRebuild
            or RenderGraphPassKind.TransparentBehind or RenderGraphPassKind.TransparentFront;
}
