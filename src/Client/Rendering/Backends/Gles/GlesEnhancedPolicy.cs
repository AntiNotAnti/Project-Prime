using System;
using MphRead.Mods;
using MphRead.Mods.Render;

namespace MphRead
{
    /// <summary>
    /// The three GLES scene paths. Enhanced lighting and materials do not
    /// depend on a floating-point target; only tone mapping and color grading
    /// do.
    /// </summary>
    internal enum GlesEnhancedMode : byte
    {
        Legacy,
        EnhancedLdr,
        EnhancedFloatingPoint
    }

    internal enum GlesEnhancedFallbackReason : byte
    {
        None,
        PresetDoesNotRequestEnhanced,
        UnsupportedGlesVersion,
        InsufficientTextureUnits,
        InsufficientFragmentUniformVectors,
        FloatingPointExtensionUnavailable,
        FloatingPointProbeFailed,
        CachedFloatingPointAllocationFailure,
        FloatingPointAllocationFailed,
        EnhancedProgramUnavailable,
        EnhancedTargetAllocationFailed
    }

    /// <summary>
    /// Backend-neutral results of querying one current GLES context. The
    /// renderer supplies the values; this policy deliberately has no OpenGL
    /// dependency so fallback behavior can be tested without a device.
    /// </summary>
    internal readonly record struct GlesEnhancedCapabilities(
        int MajorVersion,
        int MinorVersion,
        int FragmentTextureUnits,
        int FragmentUniformVectors,
        bool FloatingPointColorBufferExtension,
        bool FloatingPointTargetProbeSucceeded);

    internal readonly record struct GlesEnhancedPlan(
        GlesEnhancedMode Mode,
        GlesEnhancedFallbackReason FallbackReason)
    {
        public bool UsesEnhancedLightingAndMaterials
            => Mode is GlesEnhancedMode.EnhancedLdr
                or GlesEnhancedMode.EnhancedFloatingPoint;

        public bool UsesFloatingPointSceneTarget
            => Mode == GlesEnhancedMode.EnhancedFloatingPoint;

        public bool ToneMap => UsesFloatingPointSceneTarget;
        public bool ColorGrade => UsesFloatingPointSceneTarget;
    }

    internal readonly record struct GlesImmutableTextureKey(
        ulong ContextGeneration,
        TextureIdentity Identity,
        long Revision);

    internal enum GlesEnhancedCompositionStage : byte
    {
        SceneLinear,
        ToneMapOrLdrBypass,
        HudScene,
        SceneCaptureTransfer,
        Disruption,
        Overlays,
        Fade,
        FinalOutputTransfer
    }

    internal readonly record struct GlesEnhancedRasterState(
        bool DepthTest,
        bool CullFace,
        bool Blend)
    {
        public static GlesEnhancedRasterState EnterWorld()
            => new(DepthTest: true, CullFace: false, Blend: false);

        public static GlesEnhancedRasterState EnterFullscreen()
            => new(DepthTest: false, CullFace: false, Blend: false);

        public static GlesEnhancedRasterState LeavePresent(bool faceCulling)
            => new(DepthTest: true, CullFace: faceCulling, Blend: false);
    }

    internal static class GlesEnhancedTextureCachePolicy
    {
        public static bool CanAllocate(int cachedCount, int unpinnedCount)
        {
            if (cachedCount < 0) throw new ArgumentOutOfRangeException(nameof(cachedCount));
            if (unpinnedCount < 0 || unpinnedCount > cachedCount)
                throw new ArgumentOutOfRangeException(nameof(unpinnedCount));
            return cachedCount < GlesEnhancedShaderContract.MaximumCachedTextures
                || unpinnedCount > 0;
        }
    }

    internal static class GlesEnhancedColorGradePolicy
    {
        public static bool ShouldEnable(bool requested, bool lutResolved)
            => requested && lutResolved;
    }

    /// <summary>
    /// Stable identity for a floating-point allocation attempt. Context
    /// generation is assigned by the Android owner whenever a new EGL context
    /// becomes current; surface-only recreation must keep the same value.
    /// </summary>
    internal readonly record struct GlesFloatingPointConfiguration
    {
        public GlesFloatingPointConfiguration(ulong contextGeneration, int width, int height)
        {
            if (contextGeneration == 0)
                throw new ArgumentOutOfRangeException(nameof(contextGeneration));
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            ContextGeneration = contextGeneration;
            Width = width;
            Height = height;
        }

        public ulong ContextGeneration { get; }
        public int Width { get; }
        public int Height { get; }
    }

    /// <summary>
    /// Suppresses repeated allocation attempts only for the exact failed
    /// context and dimensions. A resize or replacement EGL context clears the
    /// stale failure and permits one new attempt.
    /// </summary>
    internal sealed class GlesFloatingPointFailureCache
    {
        public GlesFloatingPointConfiguration? FailedConfiguration { get; private set; }

        public bool ShouldAttempt(GlesFloatingPointConfiguration current)
        {
            if (FailedConfiguration is not GlesFloatingPointConfiguration failed)
                return true;
            if (failed == current) return false;
            FailedConfiguration = null;
            return true;
        }

        public void RecordFailure(GlesFloatingPointConfiguration configuration)
            => FailedConfiguration = configuration;

        public void RecordSuccess() => FailedConfiguration = null;
    }

    internal readonly record struct GlesEnhancedTargetConfiguration(
        ulong ContextGeneration,
        int SceneWidth,
        int SceneHeight,
        int DrawableWidth,
        int DrawableHeight);

    /// <summary>Prevents a broken LDR composition allocation from thrashing every frame.</summary>
    internal sealed class GlesEnhancedTargetFailureCache
    {
        public GlesEnhancedTargetConfiguration? FailedConfiguration { get; private set; }

        public bool ShouldAttempt(GlesEnhancedTargetConfiguration current)
        {
            if (FailedConfiguration == current) return false;
            FailedConfiguration = null;
            return true;
        }

        public void RecordFailure(GlesEnhancedTargetConfiguration configuration)
            => FailedConfiguration = configuration;

        public void RecordSuccess() => FailedConfiguration = null;
    }

    internal static class GlesEnhancedPolicy
    {
        public const int MinimumMajorVersion = 3;
        public const int MinimumFragmentTextureUnits = 3;

        // The Enhanced fragment program retains the legacy 32-entry toon table
        // and adds two eight-vec4 light arrays plus per-frame/per-draw values.
        // ES 3.0 guarantees substantially more than this; keeping the explicit
        // floor makes an unexpectedly constrained or misreported context fail
        // deterministically at initialization instead of at shader link time.
        public const int MinimumFragmentUniformVectors = 96;

        public static GlesEnhancedPlan Resolve(GraphicsPreset preset,
            GlesEnhancedCapabilities capabilities,
            GlesFloatingPointConfiguration configuration,
            GlesFloatingPointFailureCache failureCache)
        {
            ArgumentNullException.ThrowIfNull(failureCache);
            if (preset != GraphicsPreset.Enhanced)
            {
                return Legacy(GlesEnhancedFallbackReason.PresetDoesNotRequestEnhanced);
            }
            if (capabilities.MajorVersion < MinimumMajorVersion
                || (capabilities.MajorVersion == MinimumMajorVersion
                    && capabilities.MinorVersion < 0))
            {
                return Legacy(GlesEnhancedFallbackReason.UnsupportedGlesVersion);
            }
            if (capabilities.FragmentTextureUnits < MinimumFragmentTextureUnits)
            {
                return Legacy(GlesEnhancedFallbackReason.InsufficientTextureUnits);
            }
            if (capabilities.FragmentUniformVectors < MinimumFragmentUniformVectors)
            {
                return Legacy(GlesEnhancedFallbackReason.InsufficientFragmentUniformVectors);
            }

            if (!capabilities.FloatingPointColorBufferExtension)
            {
                return EnhancedLdr(
                    GlesEnhancedFallbackReason.FloatingPointExtensionUnavailable);
            }
            if (!capabilities.FloatingPointTargetProbeSucceeded)
            {
                return EnhancedLdr(GlesEnhancedFallbackReason.FloatingPointProbeFailed);
            }
            if (!failureCache.ShouldAttempt(configuration))
            {
                return EnhancedLdr(
                    GlesEnhancedFallbackReason.CachedFloatingPointAllocationFailure);
            }
            return new GlesEnhancedPlan(GlesEnhancedMode.EnhancedFloatingPoint,
                GlesEnhancedFallbackReason.None);
        }

        private static GlesEnhancedPlan Legacy(GlesEnhancedFallbackReason reason)
            => new(GlesEnhancedMode.Legacy, reason);

        private static GlesEnhancedPlan EnhancedLdr(GlesEnhancedFallbackReason reason)
            => new(GlesEnhancedMode.EnhancedLdr, reason);
    }
}
