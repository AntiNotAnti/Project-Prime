using System;
using System.Collections.Generic;
using MphRead.Mods;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
    internal static class SdlGpuTextureQuality
    {
        // RenderTexturePixels dimensions are positive Int32 values, so a full
        // chain can never exceed 31 levels (2^30 through 1x1).
        public const uint MaximumMipLevels = 31;

        public static uint MipLevelCount(int width, int height, bool mipmapped)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (!mipmapped) return 1;

            uint dimension = checked((uint)Math.Max(width, height));
            uint levels = 1;
            while (dimension > 1)
            {
                dimension >>= 1;
                levels++;
            }
            if (levels > MaximumMipLevels)
                throw new InvalidOperationException("Texture mip chain exceeds the bounded Int32 dimension policy.");
            return levels;
        }

        public static SDL_GPUTextureUsageFlags SceneTextureUsage(bool mipmapped)
        {
            SDL_GPUTextureUsageFlags usage
                = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            // SDL_GenerateMipmapsForGPUTexture renders each generated level,
            // so its target must also have COLOR_TARGET usage.
            if (mipmapped)
            {
                usage |= SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET;
            }
            return usage;
        }
    }

    internal enum SdlGpuSamplerFilter : byte
    {
        Nearest,
        Linear
    }

    internal enum SdlGpuSamplerMipFilter : byte
    {
        Nearest,
        Linear
    }

    internal readonly record struct SdlGpuSamplerDescription(
        SdlGpuSamplerFilter MinFilter,
        SdlGpuSamplerFilter MagFilter,
        SdlGpuSamplerMipFilter MipFilter,
        float MinLod,
        float MaxLod,
        AnisotropyLevel Anisotropy)
    {
        public bool EnableAnisotropy => Anisotropy != AnisotropyLevel.Off;
        public float MaxAnisotropy => EnableAnisotropy ? (float)Anisotropy : 1f;
    }

    internal static class SdlGpuSamplerPolicy
    {
        // SDL samplers are not texture-specific. A high finite LOD ceiling
        // covers every bounded Int32 texture chain without using infinity.
        public const float MipmappedMaxLod = SdlGpuTextureQuality.MaximumMipLevels - 1;

        public static SdlGpuSamplerDescription Describe(SamplerKey key)
        {
            if (key.Filter == RenderFilterMode.Nearest)
            {
                return new SdlGpuSamplerDescription(SdlGpuSamplerFilter.Nearest,
                    SdlGpuSamplerFilter.Nearest, SdlGpuSamplerMipFilter.Nearest,
                    0, 0, AnisotropyLevel.Off);
            }
            return new SdlGpuSamplerDescription(SdlGpuSamplerFilter.Linear,
                SdlGpuSamplerFilter.Linear,
                key.Mipmapped ? SdlGpuSamplerMipFilter.Linear : SdlGpuSamplerMipFilter.Nearest,
                0, key.Mipmapped ? MipmappedMaxLod : 0,
                key.Mipmapped && key.Filter == RenderFilterMode.Anisotropic
                    ? key.Anisotropy : AnisotropyLevel.Off);
        }

        /// <summary>
        /// Deterministic descending attempts for SDL, whose portable API has
        /// no anisotropy-limit query. The final Off attempt is the ordinary
        /// enhanced linear-mipmap sampler.
        /// </summary>
        public static bool TryGetAnisotropyAttempt(AnisotropyLevel requested, int index,
            out AnisotropyLevel attempt)
        {
            int requestedValue = requested switch
            {
                AnisotropyLevel.X16 => 16,
                AnisotropyLevel.X8 => 8,
                AnisotropyLevel.X4 => 4,
                AnisotropyLevel.X2 => 2,
                _ => 0
            };
            if (index < 0)
            {
                attempt = AnisotropyLevel.Off;
                return false;
            }
            int currentIndex = 0;
            for (int value = requestedValue; value >= 2; value /= 2)
            {
                if (currentIndex++ == index)
                {
                    attempt = (AnisotropyLevel)value;
                    return true;
                }
            }
            if (currentIndex == index)
            {
                attempt = AnisotropyLevel.Off;
                return true;
            }
            attempt = AnisotropyLevel.Off;
            return false;
        }
    }

    internal enum SdlGpuMsaaFallbackReason : byte
    {
        None,
        CelDepthSampling,
        UnsupportedColorOrDepthFormat
    }

    internal readonly record struct SdlGpuSampleNegotiation(
        int Requested, int Effective, SdlGpuMsaaFallbackReason Reason);

    internal readonly record struct SdlGpuSceneTargetPlan(
        int RenderColorSamples, int DepthSamples, int ResolveColorSamples)
    {
        public bool UsesResolve => RenderColorSamples > 1;

        public static SdlGpuSceneTargetPlan From(SdlGpuSampleNegotiation negotiation)
            => new(negotiation.Effective, negotiation.Effective, 1);

        public bool MatchesPipeline(PipelineKey key) => key.SampleCount == RenderColorSamples;
    }

    internal static class SdlGpuMsaaPolicy
    {
        public static SdlGpuSampleNegotiation Resolve(int requested, bool celDepthSampling,
            bool colorSupports2, bool colorSupports4, bool depthSupports2, bool depthSupports4)
        {
            requested = requested >= 4 ? 4 : requested >= 2 ? 2 : 1;
            if (requested == 1)
                return new SdlGpuSampleNegotiation(1, 1, SdlGpuMsaaFallbackReason.None);
            if (celDepthSampling)
                return new SdlGpuSampleNegotiation(requested, 1,
                    SdlGpuMsaaFallbackReason.CelDepthSampling);

            if (requested >= 4 && colorSupports4 && depthSupports4)
                return new SdlGpuSampleNegotiation(requested, 4, SdlGpuMsaaFallbackReason.None);
            if (colorSupports2 && depthSupports2)
                return new SdlGpuSampleNegotiation(requested, 2,
                    requested == 2 ? SdlGpuMsaaFallbackReason.None
                        : SdlGpuMsaaFallbackReason.UnsupportedColorOrDepthFormat);
            return new SdlGpuSampleNegotiation(requested, 1,
                SdlGpuMsaaFallbackReason.UnsupportedColorOrDepthFormat);
        }
    }

    internal static class SdlGpuVisualLightPolicy
    {
        public const int MaximumLights = RenderFrame.MaximumVisualLights;

        public static float DistanceAttenuation(float distance, float radius)
        {
            if (!float.IsFinite(distance) || !float.IsFinite(radius) || radius <= 0)
                return 0;
            float linear = Math.Clamp(1f - Math.Max(0, distance) / radius, 0, 1);
            return linear * linear;
        }
    }

    internal readonly record struct SdlGpuReconstructionPlan(
        bool Enabled, uint SourceWidth, uint SourceHeight,
        uint DestinationWidth, uint DestinationHeight, float Sharpness)
    {
        public static SdlGpuReconstructionPlan Create(GraphicsPreset preset,
            uint sourceWidth, uint sourceHeight, uint destinationWidth,
            uint destinationHeight)
        {
            if (sourceWidth == 0 || sourceHeight == 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            if (destinationWidth == 0 || destinationHeight == 0)
                throw new ArgumentOutOfRangeException(nameof(destinationWidth));
            bool enabled = preset == GraphicsPreset.Enhanced
                && (sourceWidth != destinationWidth
                    || sourceHeight != destinationHeight);
            // A conservative contrast-adaptive amount avoids halos around the
            // high-contrast silhouettes that matter for competitive play.
            return new SdlGpuReconstructionPlan(enabled, sourceWidth,
                sourceHeight, destinationWidth, destinationHeight,
                enabled ? 0.22f : 0);
        }
    }

    internal enum SdlGpuHdrFallbackReason : byte
    {
        None,
        PresetDoesNotRequestHdr,
        UnsupportedRenderTarget,
        CachedAllocationFailure
    }

    internal readonly record struct SdlGpuSceneColorPlan(
        SDL_GPUTextureFormat Format,
        bool UsesHdr,
        SdlGpuHdrFallbackReason FallbackReason);

    /// <summary>
    /// Selects a distinct internal scene format. The final composite remains
    /// the device's single-sample byte-format SDR target in every mode.
    /// </summary>
    internal static class SdlGpuHdrPolicy
    {
        public const SDL_GPUTextureFormat HdrFormat
            = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16G16B16A16_FLOAT;

        public static SdlGpuSceneColorPlan Resolve(GraphicsPreset preset,
            SDL_GPUTextureFormat ldrFormat, bool hdrColorTargetAndSamplerSupported)
        {
            if (preset != GraphicsPreset.Enhanced)
            {
                return new SdlGpuSceneColorPlan(ldrFormat, UsesHdr: false,
                    SdlGpuHdrFallbackReason.PresetDoesNotRequestHdr);
            }
            if (!hdrColorTargetAndSamplerSupported)
            {
                return new SdlGpuSceneColorPlan(ldrFormat, UsesHdr: false,
                    SdlGpuHdrFallbackReason.UnsupportedRenderTarget);
            }
            return new SdlGpuSceneColorPlan(HdrFormat, UsesHdr: true,
                SdlGpuHdrFallbackReason.None);
        }
    }

    internal readonly record struct SdlGpuHdrConfiguration(
        GraphicsPreset Preset,
        uint SceneWidth,
        uint SceneHeight,
        uint CompositeWidth,
        uint CompositeHeight,
        int RequestedSamples,
        bool CelDepthSampling,
        bool BloomRequested);

    /// <summary>
    /// An allocation failure is sticky only for the exact resource
    /// configuration. SdlGpuSceneResources is device-owned, so its lifetime is
    /// also the device-generation boundary. Any size, quality, or feature
    /// change invalidates the cached failure and permits one new HDR attempt.
    /// </summary>
    internal static class SdlGpuHdrFailurePolicy
    {
        public static bool ShouldAttemptHdr(SdlGpuHdrConfiguration? failed,
            SdlGpuHdrConfiguration current)
            => failed == null || failed.Value != current;
    }

    internal readonly record struct SdlGpuOutputTransferPolicy(
        bool ToneMap,
        bool ShaderConvertsLinearToSrgb,
        bool DisplayAssetsConvertSrgbToLinear)
    {
        public static SdlGpuOutputTransferPolicy Resolve(GraphicsPreset preset,
            bool swapchainIsSrgb)
        {
            bool enhanced = preset == GraphicsPreset.Enhanced;
            return new SdlGpuOutputTransferPolicy(
                ToneMap: enhanced,
                ShaderConvertsLinearToSrgb: enhanced && !swapchainIsSrgb,
                DisplayAssetsConvertSrgbToLinear: enhanced);
        }
    }

    internal enum SdlGpuColorPipelineStep : byte
    {
        SceneEffects,
        ToneMap,
        ColorGrade,
        SceneCapture,
        Visor,
        HudScene,
        Overlays,
        OutputTransfer,
        FinalCapture
    }

    internal static class SdlGpuColorPipelinePlan
    {
        private static readonly SdlGpuColorPipelineStep[] _enhancedSteps =
        {
            SdlGpuColorPipelineStep.SceneEffects,
            SdlGpuColorPipelineStep.ToneMap,
            SdlGpuColorPipelineStep.ColorGrade,
            // SceneTarget branches here and intentionally excludes the
            // viewer-local visor just like the later 2D overlay layers.
            SdlGpuColorPipelineStep.SceneCapture,
            SdlGpuColorPipelineStep.Visor,
            SdlGpuColorPipelineStep.HudScene,
            SdlGpuColorPipelineStep.Overlays,
            SdlGpuColorPipelineStep.OutputTransfer,
            SdlGpuColorPipelineStep.FinalCapture
        };

        public static IReadOnlyList<SdlGpuColorPipelineStep> EnhancedSteps
            => _enhancedSteps;
    }

    /// <summary>
    /// Readback textures always use the final SDR byte format. Enhanced scene
    /// captures therefore require the same explicit tone-map/transfer pass as
    /// presentation; the float scene target is never interpreted as bytes.
    /// </summary>
    internal static class SdlGpuCaptureColorPolicy
    {
        public static bool RequiresSdrSceneConversion(GraphicsPreset preset,
            CaptureTargetKind target)
            => preset == GraphicsPreset.Enhanced
                && target is CaptureTargetKind.SceneTarget
                    or CaptureTargetKind.ThumbnailTarget;

        public static SDL_GPUTextureFormat ReadbackFormat(
            SDL_GPUTextureFormat swapchainFormat)
            => swapchainFormat;
    }

    /// <summary>CPU reference for the Enhanced display-linear blend contract.</summary>
    internal static class SdlGpuDisplayCompositionMath
    {
        public static Vector3 BlendAuthoredSrgb(Vector3 destinationLinear,
            Vector3 authoredSrgb, float alpha)
        {
            if (!float.IsFinite(alpha) || alpha < 0 || alpha > 1)
                throw new ArgumentOutOfRangeException(nameof(alpha));
            Vector3 sourceLinear = EnhancedColorMath.SrgbToLinear(authoredSrgb);
            return destinationLinear * (1 - alpha) + sourceLinear * alpha;
        }

        public static Vector3 ShaderOutput(Vector3 displayLinear,
            bool swapchainIsSrgb)
            => swapchainIsSrgb
                ? displayLinear
                : EnhancedColorMath.LinearToSrgb(displayLinear);
    }

    internal readonly record struct SdlGpuBloomPlan(
        bool Enabled,
        uint BlurWidth,
        uint BlurHeight,
        int RenderSamples,
        int ResolveSamples)
    {
        public bool UsesResolve => Enabled && RenderSamples > 1;
        public int StepIndex(SdlGpuBloomStep step) => step switch
        {
            SdlGpuBloomStep.Cel => 0,
            SdlGpuBloomStep.BloomComposite => Enabled ? 1 : -1,
            SdlGpuBloomStep.Disruption => 2,
            SdlGpuBloomStep.SceneComposite => 3,
            SdlGpuBloomStep.Overlays => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(step))
        };
        public bool MatchesPipeline(PipelineKey key)
            => !Enabled || key.SampleCount == RenderSamples;

        public static SdlGpuBloomPlan Create(RenderFrame frame, uint sceneWidth,
            uint sceneHeight, int effectiveSamples)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            bool enabled = frame.Options.Quality.Bloom
                && HasEligibleSubmission(frame.Submissions,
                    frame.Options.Quality.GraphicsPreset);
            return new SdlGpuBloomPlan(enabled,
                enabled ? QuarterDimension(sceneWidth) : 0,
                enabled ? QuarterDimension(sceneHeight) : 0,
                enabled ? NormalizeSamples(effectiveSamples) : 1,
                1);
        }

        public static bool IsEligible(RenderMaterial material)
            => material.BloomEligible && float.IsFinite(material.BloomStrength)
                && material.BloomStrength > 0;

        public static bool IsEligible(RenderMaterial material,
            GraphicsPreset preset)
        {
            if (preset == GraphicsPreset.Enhanced
                && material.Enhanced?.Emissive is TextureIdentity)
            {
                return SdlGpuEmissionPolicy.HasMappedEmission(material);
            }
            return IsEligible(material);
        }

        public static float Strength(RenderMaterial material)
            => IsEligible(material) ? Math.Clamp(material.BloomStrength, 0, 1) : 0;

        public static float Strength(RenderMaterial material,
            GraphicsPreset preset)
            => preset == GraphicsPreset.Enhanced
                && material.Enhanced?.Emissive is TextureIdentity
                    ? SdlGpuEmissionPolicy.HasMappedEmission(material) ? 1 : 0
                    : Strength(material);

        public static Vector3 ApplyFogVisibility(Vector3 emission, float fogDensity)
            => emission * (1f - Math.Clamp(fogDensity, 0, 1));

        public static bool HasEligibleSubmission(IReadOnlyList<DrawSubmission> submissions,
            GraphicsPreset preset)
        {
            for (int i = 0; i < submissions.Count; i++)
                if (IsEligible(submissions[i].Material, preset)) return true;
            return false;
        }

        public static uint QuarterDimension(uint dimension)
            => Math.Max(1u, checked((dimension + 3u) / 4u));

        private static int NormalizeSamples(int samples)
            => samples >= 4 ? 4 : samples >= 2 ? 2 : 1;
    }

    internal static class SdlGpuEmissionPolicy
    {
        public static bool HasMappedEmission(RenderMaterial material)
            => material.Enhanced is EnhancedMaterial enhanced
                && enhanced.Emissive is TextureIdentity
                && float.IsFinite(enhanced.EmissionStrength)
                && enhanced.EmissionStrength > 0;

        public static Vector3 Map(Vector3 emissiveSrgb, Vector3 tint,
            float strength)
        {
            if (!float.IsFinite(strength) || strength <= 0) return Vector3.Zero;
            return EnhancedColorMath.SrgbToLinear(emissiveSrgb) * tint * strength;
        }
    }

    internal enum SdlGpuBloomStep : byte
    {
        Cel,
        BloomComposite,
        Disruption,
        SceneComposite,
        Overlays
    }
}
