using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using SDL;
using MphRead.Mods;

namespace MphRead
{
    internal static class SdlGpuMaskCoordinates
    {
        public static Vector2 FromTopOrigin(Vector2 pixelPosition, Vector2 viewport)
        {
            float legacyY = viewport.Y - pixelPosition.Y;
            float maskY = legacyY + (viewport.X - viewport.Y) / 2f;
            return new Vector2(pixelPosition.X / viewport.X, 1f - maskY / viewport.X);
        }
    }

    internal static class SdlGpuPresentationOrder
    {
        private static readonly RenderPresentationStage[] _stages =
        {
            RenderPresentationStage.HudScene,
            RenderPresentationStage.Cel,
            RenderPresentationStage.SceneComposite,
            RenderPresentationStage.HudOverlay,
            RenderPresentationStage.SpectatorOverlay,
            RenderPresentationStage.ReplayOverlay,
            RenderPresentationStage.Fade
        };

        public static void Validate(IReadOnlyList<RenderOverlayCommand> commands)
        {
            int marker = 0;
            RenderPresentationStage? current = null;
            bool sawFade = false;
            foreach (RenderOverlayCommand command in commands)
            {
                if (command.Kind == RenderOverlayKind.StageMarker)
                {
                    if (marker >= _stages.Length || command.Stage != _stages[marker])
                        throw new InvalidOperationException($"Unexpected render stage marker {command.Stage}; expected {(_stages.Length > marker ? _stages[marker] : (RenderPresentationStage?)null)}.");
                    current = command.Stage;
                    marker++;
                }
                else if (current == null || command.Stage != current)
                {
                    throw new InvalidOperationException($"Overlay {command.Kind} belongs to {command.Stage} but follows {current?.ToString() ?? "no stage marker"}.");
                }
                else if ((command.Kind == RenderOverlayKind.Fade) != (command.Stage == RenderPresentationStage.Fade))
                {
                    throw new InvalidOperationException($"Overlay {command.Kind} is invalid in render stage {command.Stage}.");
                }
                else if (sawFade)
                {
                    throw new InvalidOperationException("No render command may follow the final fade.");
                }
                if (command.Kind == RenderOverlayKind.Fade) sawFade = true;
            }
            if (marker != _stages.Length)
                throw new InvalidOperationException($"Render frame has {marker} stage markers; expected {_stages.Length}.");
        }
    }

    /// <summary>SDL resources and encoders invoked by the validated RenderGraphLite plan.</summary>
    internal unsafe sealed class SdlGpuPostResources : IDisposable
    {
        private const int MaximumOverlayVertices = 65536;
        internal const float BloomCompositeStrength = 0.65f;
        private readonly SdlGpuDevice _device;
        private readonly OverlaySlot[] _overlaySlots;
        private readonly Dictionary<PostPipelineKey, nint> _pipelines = new();
        private readonly Dictionary<PostShader, ShaderPair> _shaders = new();
        private SDL_GPUBuffer* _quadBuffer;
        private SDL_GPUTransferBuffer* _quadTransfer;
        private bool _quadUploaded;
        private SDL_GPUTexture* _intermediateA;
        private SDL_GPUTexture* _intermediateB;
        private SDL_GPUTexture* _distortedBloom;
        private SDL_GPUTexture* _bloomQuarterA;
        private SDL_GPUTexture* _bloomQuarterB;
        private SDL_GPUTexture* _bloomEighthA;
        private SDL_GPUTexture* _bloomEighthB;
        private SDL_GPUTexture* _bloomSixteenthA;
        private SDL_GPUTexture* _bloomSixteenthB;
        private SDL_GPUTexture* _displayLinearA;
        private SDL_GPUTexture* _displayLinearB;
        private SDL_GPUTexture* _displayLinearForFrame;
        private SDL_GPUTexture* _reconstructionSourceForFrame;
        private SdlGpuReconstructionPlan _reconstructionPlan;
        private SDL_GPUTexture* _captureSceneSdr;
        private SDL_GPUTexture* _captureSceneForFrame;
        private uint _sceneWidth;
        private uint _sceneHeight;
        private uint _captureWidth;
        private uint _captureHeight;
        private uint _displayWidth;
        private uint _displayHeight;
        private SDL_GPUTextureFormat _sceneFormat;
        private SDL_GPUTextureFormat _displayFormat;
        private BloomPyramidPlan? _bloomPyramid;
        private readonly BloomPyramidFailureCache _bloomFailureCache = new();
        private BloomPyramidConfiguration? _reportedBloomFailure;
        private bool _bloomEnabledForFrame;
        private bool _reportedUnsampleableDepth;
        private SDL_GPUTextureFormat? _failedDistortionWarpFormat;
        private SDL_GPUTextureFormat? _reportedDistortionWarpFailure;
        private SDL_GPUTextureFormat? _failedVisorFormat;
        private SDL_GPUTextureFormat? _reportedVisorFailure;
        private bool _disposed;

        private SdlGpuPostResources(SdlGpuDevice device)
        {
            _device = device;
            _overlaySlots = new OverlaySlot[device.FrameResources.SlotCount];
            try
            {
                ShaderArtifactManifest.ValidatePostFresh();
                for (int i = 0; i < _overlaySlots.Length; i++) _overlaySlots[i] = new OverlaySlot(device);
                foreach (PostShader shader in Enum.GetValues<PostShader>())
                {
                    if (shader is not PostShader.DistortionWarp and not PostShader.Visor)
                        _shaders.Add(shader, CreateShaders(shader));
                }
                CreateQuad();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static SdlGpuPostResources Create(SdlGpuDevice device) => new(device);

        public SDL_GPUTexture* CaptureSceneColor => _captureSceneForFrame;
        public SDL_GPUTexture* DisplayLinearColor => _displayLinearForFrame;

        public bool TryPrepareDistortionWarp(SDL_GPUTextureFormat targetFormat)
        {
            if (_failedDistortionWarpFormat == targetFormat) return false;
            try
            {
                ShaderArtifactManifest.ValidateDistortionWarpFresh();
                if (!_shaders.ContainsKey(PostShader.DistortionWarp))
                {
                    _shaders.Add(PostShader.DistortionWarp,
                        CreateShaders(PostShader.DistortionWarp));
                }
                _ = Pipeline(new PostPipelineKey(PostShader.DistortionWarp,
                    Blend: false,
                    SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP,
                    targetFormat));
                _failedDistortionWarpFormat = null;
                _reportedDistortionWarpFailure = null;
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException
                or IOException or PlatformNotSupportedException)
            {
                _failedDistortionWarpFormat = targetFormat;
                if (_reportedDistortionWarpFailure != targetFormat)
                {
                    _reportedDistortionWarpFailure = targetFormat;
                    Console.Error.WriteLine(
                        $"[render] Enhanced distortion warp disabled for {targetFormat}: {error.Message}");
                }
                return false;
            }
        }

        public bool TryPrepareVisor(SDL_GPUTextureFormat targetFormat)
        {
            if (_failedVisorFormat == targetFormat) return false;
            PostPipelineKey key = new(PostShader.Visor, Blend: false,
                SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP,
                targetFormat);
            if (_pipelines.ContainsKey(key)) return true;
            try
            {
                if (!_shaders.ContainsKey(PostShader.Visor))
                {
                    ShaderArtifactManifest.ValidateVisorFresh();
                    _shaders.Add(PostShader.Visor, CreateShaders(PostShader.Visor));
                }
                _ = Pipeline(key);
                _failedVisorFormat = null;
                _reportedVisorFailure = null;
                return true;
            }
            catch (Exception error) when (error is InvalidOperationException
                or IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException or PlatformNotSupportedException)
            {
                _failedVisorFormat = targetFormat;
                if (_reportedVisorFailure != targetFormat)
                {
                    _reportedVisorFailure = targetFormat;
                    Console.Error.WriteLine(
                        $"[render] Enhanced visor disabled for {targetFormat}: {error.Message}");
                }
                return false;
            }
        }

        public void PrepareSceneTargets(uint width, uint height,
            uint compositeWidth, uint compositeHeight,
            SDL_GPUTextureFormat sceneFormat, SdlGpuBloomPlan bloomPlan,
            bool enhancedOutput)
        {
            EnsureIntermediates(width, height, sceneFormat);
            _bloomEnabledForFrame = bloomPlan.Enabled
                && TryEnsureBloomPyramid(width, height, sceneFormat);
            if (enhancedOutput)
                EnsureDisplayLinear(compositeWidth, compositeHeight, sceneFormat);
        }

        public void EncodeScene(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* sceneColor, SDL_GPUTexture* sceneDepth, SDL_GPUTexture* bloomColor,
            SdlGpuBloomPlan bloomPlan, SDL_GPUTexture* finalComposite,
            uint finalWidth, uint finalHeight, SDL_GPUTextureFormat sceneFormat,
            bool enhancedOutput, SdlGpuSceneResources sceneResources,
            SDL_GPUTexture* distortionTexture)
        {
            SdlGpuPresentationOrder.Validate(frame.OverlayCommands);
            EnsureQuadUploaded(commandBuffer);
            PrepareSceneTargets(checked((uint)Math.Max(1, frame.SceneTargetSize.X)),
                checked((uint)Math.Max(1, frame.SceneTargetSize.Y)), finalWidth,
                finalHeight, sceneFormat, bloomPlan, enhancedOutput);

            _captureSceneForFrame = enhancedOutput ? null : sceneColor;
            _reconstructionSourceForFrame = null;
            _reconstructionPlan = default;

            SDL_GPUTexture* current = sceneColor;
            SDL_GPUTexture* effectiveBloom = bloomColor;
            SDL_GPUTexture* surfaceData = sceneResources.SurfaceDataTexture;
            SDL_GPUTexture* celGeometry = surfaceData != null
                ? surfaceData : sceneResources.DepthSampleable ? sceneDepth : null;
            if (frame.CelState.Enabled && frame.CelState.Outline > 0
                && celGeometry != null)
            {
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeCel(commandBuffer, frame, current, celGeometry, target,
                    sceneResources, surfaceData != null);
                current = target;
            }
            else if (frame.CelState.Enabled && frame.CelState.Outline > 0 && !_reportedUnsampleableDepth)
            {
                _reportedUnsampleableDepth = true;
                Console.Error.WriteLine("[render] cel outline disabled: SDL GPU depth-stencil targets are not sampleable on this device.");
            }
            // Cel evaluates against the unwarped surface/depth buffers. Warp
            // the completed cel scene and its selective emission together so
            // later bloom follows the same displaced geometry.
            if (enhancedOutput && distortionTexture != null)
            {
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeFullscreen(commandBuffer, current,
                    (nint)distortionTexture, target, _sceneWidth, _sceneHeight,
                    PostShader.DistortionWarp, sceneFormat,
                    operation: EnhancedDistortionSubmission.MaximumStrength,
                    alpha: 1, color: Vector4.One, blend: false,
                    RenderCompositeFilter.Linear, clear: true, sceneResources);
                current = target;
                if (bloomColor != null && _bloomEnabledForFrame)
                {
                    EncodeFullscreen(commandBuffer, bloomColor,
                        (nint)distortionTexture, _distortedBloom,
                        _sceneWidth, _sceneHeight,
                        PostShader.DistortionWarp, sceneFormat,
                        operation: EnhancedDistortionSubmission.MaximumStrength,
                        alpha: 1, color: Vector4.One, blend: false,
                        RenderCompositeFilter.Linear, clear: true,
                        sceneResources);
                    effectiveBloom = _distortedBloom;
                }
            }
            if (effectiveBloom != null && _bloomEnabledForFrame)
            {
                if (!bloomPlan.Enabled)
                    throw new InvalidOperationException("SDL bloom texture has no enabled bloom plan.");
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeBloomPyramid(commandBuffer, effectiveBloom, current, target,
                    sceneResources);
                current = target;
            }
            else if (effectiveBloom == null && _bloomEnabledForFrame)
            {
                throw new InvalidOperationException("SDL enabled bloom plan has no resolved emission texture.");
            }

            if (frame.Disruption.Enabled)
            {
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeDisruption(commandBuffer, frame, current, target, sceneResources);
                current = target;
            }

            if (enhancedOutput)
            {
                _reconstructionPlan = SdlGpuReconstructionPlan.Create(
                    frame.Options.Quality.GraphicsPreset, _sceneWidth,
                    _sceneHeight, finalWidth, finalHeight);
                if (_reconstructionPlan.Enabled)
                {
                    // Tone map and grade at native scene resolution. A single
                    // spatial pass then reconstructs into the full-size
                    // display target before visor/HUD/overlay composition.
                    SDL_GPUTexture* toneTarget = NextIntermediate(current);
                    EncodeColorTransform(commandBuffer, current, toneTarget,
                        _sceneWidth, _sceneHeight, frame.Exposure,
                        toneMap: true, shaderConvertsLinearToSrgb: false,
                        RenderCompositeFilter.Nearest, sceneFormat,
                        sceneResources, clear: true);
                    current = toneTarget;
                    if (frame.ColorGrade.Enabled)
                    {
                        SDL_GPUTexture* gradeTarget = NextIntermediate(current);
                        EncodeColorGrade(commandBuffer, frame, current,
                            gradeTarget, _sceneWidth, _sceneHeight, sceneFormat,
                            sceneResources);
                        current = gradeTarget;
                    }
                    _reconstructionSourceForFrame = current;
                    _displayLinearForFrame = null;
                }
                else
                {
                    EncodeColorTransform(commandBuffer, current, _displayLinearA,
                        finalWidth, finalHeight, frame.Exposure,
                        toneMap: true, shaderConvertsLinearToSrgb: false,
                        frame.Composite.Filter, sceneFormat, sceneResources,
                        clear: true,
                        destinationViewport: frame.Composite.DestinationViewport);
                    if (frame.ColorGrade.Enabled)
                    {
                        EncodeColorGrade(commandBuffer, frame, _displayLinearA,
                            _displayLinearB, finalWidth, finalHeight, sceneFormat,
                            sceneResources);
                        _displayLinearForFrame = _displayLinearB;
                    }
                    else
                    {
                        _displayLinearForFrame = _displayLinearA;
                    }
                }
            }
            else
            {
                _displayLinearForFrame = null;
                EncodeFullscreen(commandBuffer, current, sceneResources.WhiteTextureHandle,
                    finalComposite, finalWidth, finalHeight, PostShader.Fullscreen,
                    _device.SwapchainFormat, operation: 0, alpha: 1,
                    color: Vector4.One, blend: false, frame.Composite.Filter,
                    clear: frame.Composite.ClearDestination, sceneResources,
                    displayAssetsToLinear: false,
                    destinationViewport: frame.Composite.DestinationViewport);
            }
        }

        public void EncodeReconstruction(SDL_GPUCommandBuffer* commandBuffer,
            SdlGpuSceneResources resources,
            RenderDestinationViewport? destinationViewport)
        {
            if (!_reconstructionPlan.Enabled) return;
            if (_reconstructionSourceForFrame == null || _displayLinearA == null)
                throw new InvalidOperationException(
                    "SDL reconstruction resources are unavailable.");
            uint reconstructionWidth = _reconstructionPlan.DestinationWidth;
            uint reconstructionHeight = _reconstructionPlan.DestinationHeight;
            if (destinationViewport is { IsValid: true } destination
                && destination.X + destination.Width <= reconstructionWidth
                && destination.Y + destination.Height <= reconstructionHeight)
            {
                reconstructionWidth = checked((uint)destination.Width);
                reconstructionHeight = checked((uint)destination.Height);
            }
            ReconstructionConstants constants = new()
            {
                SourceAndDestination = new Vector4(
                    _reconstructionPlan.SourceWidth,
                    _reconstructionPlan.SourceHeight,
                    reconstructionWidth,
                    reconstructionHeight),
                Options = new Vector4(_reconstructionPlan.Sharpness, 1, 0, 0)
            };
            BeginFullscreenPass(commandBuffer, _displayLinearA,
                _reconstructionPlan.DestinationWidth,
                _reconstructionPlan.DestinationHeight,
                PostShader.Reconstruction, _displayFormat, blend: false,
                additive: false, clear: true, _reconstructionSourceForFrame,
                (SDL_GPUTexture*)resources.WhiteTextureHandle,
                resources.LinearClampSamplerHandle,
                resources.LinearClampSamplerHandle, &constants,
                (uint)sizeof(ReconstructionConstants), destinationViewport);
            _displayLinearForFrame = _displayLinearA;
            _reconstructionSourceForFrame = null;
        }

        public bool RequiresSceneCapture(GraphicsPreset preset,
            IReadOnlyList<RenderCaptureRequest> requests)
        {
            for (int i = 0; i < requests.Count; i++)
            {
                if (SdlGpuCaptureColorPolicy.RequiresSdrSceneConversion(
                    preset, requests[i].Target)) return true;
            }
            return false;
        }

        public SDL_GPUTexture* EncodeSceneCaptureBase(
            SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* sceneColor,
            SdlGpuSceneResources resources)
        {
            if (!RequiresSceneCapture(frame.Options.Quality.GraphicsPreset,
                frame.CaptureRequests)) return null;
            // Preserve SceneTarget semantics: branch from the resolved scene
            // after the six world passes, not from the later post-effects
            // chain. Reuse a full-size intermediate after the main scene has
            // already been tone-mapped to its separate composition target.
            SDL_GPUTexture* captureLinear = NextIntermediate(sceneColor);
            EncodeColorTransform(commandBuffer, sceneColor, captureLinear,
                _sceneWidth, _sceneHeight, frame.Exposure, toneMap: true,
                shaderConvertsLinearToSrgb: false, frame.Composite.Filter,
                _sceneFormat, resources, clear: true);
            if (frame.ColorGrade.Enabled)
            {
                SDL_GPUTexture* gradedCapture = NextIntermediate(captureLinear);
                EncodeColorGrade(commandBuffer, frame, captureLinear,
                    gradedCapture, _sceneWidth, _sceneHeight, _sceneFormat,
                    resources);
                captureLinear = gradedCapture;
            }
            return captureLinear;
        }

        /// <summary>
        /// Apply the viewer-local visor to the main display-linear branch. The
        /// overlay-free SceneTarget branch is intentionally captured before this.
        /// </summary>
        public SDL_GPUTexture* EncodeVisor(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, SDL_GPUTexture* source, uint width, uint height,
            SDL_GPUTextureFormat targetFormat, SdlGpuSceneResources resources)
        {
            if (!frame.Visor.Enabled || !TryPrepareVisor(targetFormat)) return source;
            SDL_GPUTexture* target = NextDisplayLinear(source);
            RenderVisorState state = frame.Visor;
            VisorConstants constants = new()
            {
                Combat = new Vector4(state.Combat.CenterClearRadius,
                    state.Combat.EdgeVignette,
                    state.Combat.ChromaticSeparation,
                    state.Combat.Distortion),
                Damage = new Vector4(state.Damage.Direction.X,
                    state.Damage.Direction.Y, state.Damage.EdgeOpacity,
                    state.Damage.Distortion),
                DamageColor = new Vector4(state.Damage.ColorImpulse,
                    state.Damage.ScanlineInterference),
                LowHealth = new Vector4(state.LowHealth.EdgeOpacity,
                    state.LowHealth.Interference,
                    state.LowHealth.CenterClearRadius,
                    state.Combat.HelmetReflection),
                Phases = new Vector4(state.DistortionPhase,
                    state.InterferencePhase, state.Damage.CenterClearRadius, 0)
            };
            BeginFullscreenPass(commandBuffer, target, width, height,
                PostShader.Visor, targetFormat, blend: false, additive: false,
                clear: true, source,
                (SDL_GPUTexture*)resources.WhiteTextureHandle,
                resources.LinearClampSamplerHandle,
                resources.LinearClampSamplerHandle,
                &constants, (uint)sizeof(VisorConstants));
            _displayLinearForFrame = target;
            return target;
        }

        public void EncodeSceneCaptureTransfer(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, SDL_GPUTexture* captureLinear,
            SdlGpuSceneResources resources)
        {
            EnsureCaptureScene(_sceneWidth, _sceneHeight);
            SdlGpuOutputTransferPolicy transfer = SdlGpuOutputTransferPolicy.Resolve(
                GraphicsPreset.Enhanced, _device.SwapchainIsSrgb);
            EncodeColorTransform(commandBuffer, captureLinear, _captureSceneSdr,
                _sceneWidth, _sceneHeight, exposure: 1, toneMap: false,
                transfer.ShaderConvertsLinearToSrgb, frame.Composite.Filter,
                _device.SwapchainFormat, resources, clear: true);
            _captureSceneForFrame = _captureSceneSdr;
        }

        public void EncodeFinalTransfer(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, SDL_GPUTexture* displayLinear,
            SDL_GPUTexture* finalComposite, uint width, uint height,
            SdlGpuSceneResources resources)
        {
            SdlGpuOutputTransferPolicy transfer = SdlGpuOutputTransferPolicy.Resolve(
                GraphicsPreset.Enhanced, _device.SwapchainIsSrgb);
            EncodeColorTransform(commandBuffer, displayLinear, finalComposite,
                width, height, exposure: 1, toneMap: false,
                transfer.ShaderConvertsLinearToSrgb, RenderCompositeFilter.Nearest,
                _device.SwapchainFormat, resources, clear: true);
        }

        private SDL_GPUTexture* NextIntermediate(SDL_GPUTexture* current)
            => current == _intermediateA ? _intermediateB : _intermediateA;

        private SDL_GPUTexture* NextDisplayLinear(SDL_GPUTexture* current)
        {
            if (current == _displayLinearA) return _displayLinearB;
            if (current == _displayLinearB) return _displayLinearA;
            throw new InvalidOperationException(
                "SDL visor source is not a display-linear ping-pong target.");
        }

        private void EncodeColorTransform(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPUTexture* source, SDL_GPUTexture* target, uint width, uint height,
            float exposure, bool toneMap, bool shaderConvertsLinearToSrgb,
            RenderCompositeFilter filter, SDL_GPUTextureFormat targetFormat,
            SdlGpuSceneResources resources, bool clear,
            RenderDestinationViewport? destinationViewport = null)
        {
            ToneMapConstants constants = new()
            {
                Options = new Vector4(exposure,
                    shaderConvertsLinearToSrgb ? 1 : 0,
                    toneMap ? 1 : 0, 0)
            };
            nint sampler = filter == RenderCompositeFilter.Linear
                ? resources.LinearClampSamplerHandle : resources.NearestClampSamplerHandle;
            BeginFullscreenPass(commandBuffer, target, width, height,
                PostShader.ToneMap, targetFormat,
                blend: false, additive: false, clear, source,
                (SDL_GPUTexture*)resources.WhiteTextureHandle, sampler, sampler,
                &constants, (uint)sizeof(ToneMapConstants), destinationViewport);
        }

        private void EncodeColorGrade(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, SDL_GPUTexture* source, SDL_GPUTexture* target,
            uint width, uint height, SDL_GPUTextureFormat targetFormat,
            SdlGpuSceneResources resources)
        {
            ColorGradeConstants constants = new()
            {
                Options = new Vector4(frame.ColorGrade.Strength, 0, 0, 0)
            };
            SDL_GPUTexture* lut = (SDL_GPUTexture*)resources.ResolveUnmippedTextureHandle(
                frame, frame.ColorGrade.LutTexture, "color-grade LUT");
            BeginFullscreenPass(commandBuffer, target, width, height,
                PostShader.ColorGrade, targetFormat,
                blend: false, additive: false, clear: true, source, lut,
                resources.LinearClampSamplerHandle,
                resources.LinearClampSamplerHandle,
                &constants, (uint)sizeof(ColorGradeConstants));
        }

        private void EncodeBloomPyramid(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPUTexture* emissionSource, SDL_GPUTexture* sceneInput,
            SDL_GPUTexture* sceneOutput, SdlGpuSceneResources resources)
        {
            BloomPyramidPlan plan = _bloomPyramid
                ?? throw new InvalidOperationException("SDL bloom pyramid resources are unavailable.");
            foreach (BloomPyramidPassDescriptor pass in plan.Passes)
            {
                SDL_GPUTexture* inputA = BloomResource(pass.InputA,
                    emissionSource, sceneInput, sceneOutput);
                SDL_GPUTexture* inputB = pass.InputB is { } second
                    ? BloomResource(second, emissionSource, sceneInput, sceneOutput)
                    : (SDL_GPUTexture*)resources.WhiteTextureHandle;
                SDL_GPUTexture* output = BloomResource(pass.Output,
                    emissionSource, sceneInput, sceneOutput);
                if (inputA == output || inputB == output)
                    throw new InvalidOperationException("SDL bloom pass cannot sample its render target.");

                (uint inputWidth, uint inputHeight) = BloomResourceSize(pass.InputA, plan);
                BloomConstants constants = BloomConstantsFor(pass, inputWidth, inputHeight);
                BeginFullscreenPass(commandBuffer, output, pass.OutputWidth, pass.OutputHeight,
                    PostShader.Bloom, _sceneFormat,
                    blend: false, additive: false, clear: true,
                    inputA, inputB,
                    resources.LinearClampSamplerHandle, resources.LinearClampSamplerHandle,
                    &constants, (uint)sizeof(BloomConstants));
            }
        }

        private static BloomConstants BloomConstantsFor(BloomPyramidPassDescriptor pass,
            uint inputWidth, uint inputHeight)
        {
            Vector4 options = pass.Kind switch
            {
                BloomPyramidPassKind.Downsample => new Vector4(0, 0, 0, 1),
                BloomPyramidPassKind.BlurHorizontal => new Vector4(1, 0, 0, 0),
                BloomPyramidPassKind.BlurVertical => new Vector4(0, 1, 0, 0),
                BloomPyramidPassKind.UpsampleCombine => new Vector4(
                    pass.InputAWeight, pass.InputBWeight, 0, 2),
                BloomPyramidPassKind.SceneComposite => new Vector4(
                    pass.InputAWeight, BloomCompositeStrength * pass.InputBWeight, 0, 3),
                _ => throw new ArgumentOutOfRangeException(nameof(pass))
            };
            return new BloomConstants
            {
                Options = options,
                TexelSize = new Vector4(1f / inputWidth, 1f / inputHeight, 0, 0)
            };
        }

        private SDL_GPUTexture* BloomResource(BloomPyramidResource resource,
            SDL_GPUTexture* emissionSource, SDL_GPUTexture* sceneInput,
            SDL_GPUTexture* sceneOutput) => resource switch
        {
            BloomPyramidResource.EmissionSource => emissionSource,
            BloomPyramidResource.QuarterA => _bloomQuarterA,
            BloomPyramidResource.QuarterB => _bloomQuarterB,
            BloomPyramidResource.EighthA => _bloomEighthA,
            BloomPyramidResource.EighthB => _bloomEighthB,
            BloomPyramidResource.SixteenthA => _bloomSixteenthA,
            BloomPyramidResource.SixteenthB => _bloomSixteenthB,
            BloomPyramidResource.SceneInput => sceneInput,
            BloomPyramidResource.SceneOutput => sceneOutput,
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };

        private static (uint Width, uint Height) BloomResourceSize(
            BloomPyramidResource resource, BloomPyramidPlan plan) => resource switch
        {
            BloomPyramidResource.EmissionSource or BloomPyramidResource.SceneInput
                or BloomPyramidResource.SceneOutput
                => (plan.Configuration.SceneWidth, plan.Configuration.SceneHeight),
            BloomPyramidResource.QuarterA or BloomPyramidResource.QuarterB
                => (plan.Levels[0].Width, plan.Levels[0].Height),
            BloomPyramidResource.EighthA or BloomPyramidResource.EighthB
                => (plan.Levels[1].Width, plan.Levels[1].Height),
            BloomPyramidResource.SixteenthA or BloomPyramidResource.SixteenthB
                => (plan.Levels[2].Width, plan.Levels[2].Height),
            _ => throw new ArgumentOutOfRangeException(nameof(resource))
        };

        private void EncodeCel(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* source, SDL_GPUTexture* depth, SDL_GPUTexture* target,
            SdlGpuSceneResources resources, bool usesSurfaceData)
        {
            CelConstants constants = new()
            {
                CelOptions = new Vector4(frame.CelState.Outline, frame.CelState.NearPlane,
                    frame.CelState.FarPlane, frame.CelState.DepthQuantum),
                TexelOptions = new Vector4(frame.CelState.TexelSize.X,
                    frame.CelState.TexelSize.Y, usesSurfaceData ? 1 : 0, 0)
            };
            BeginFullscreenPass(commandBuffer, target, _sceneWidth, _sceneHeight, PostShader.Cel,
                _sceneFormat, blend: false, additive: false, clear: true, source, depth,
                resources.NearestClampSamplerHandle, resources.NearestClampSamplerHandle,
                &constants, (uint)sizeof(CelConstants));
        }

        private void EncodeDisruption(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* source, SDL_GPUTexture* target, SdlGpuSceneResources resources)
        {
            DisruptionConstants constants = default;
            constants.Options = new Vector4(frame.Disruption.ShiftFactor, frame.Disruption.ShiftIndex,
                frame.Disruption.LerpFactor, frame.Disruption.WhiteoutFactor);
            float* shift = constants.ShiftTable;
            float* whiteout = constants.WhiteoutTable;
            for (int i = 0; i < 64; i++) shift[i] = frame.Disruption.ShiftTable[i];
            for (int i = 0; i < 192; i++) whiteout[i] = frame.Disruption.WhiteoutTable[i];
            BeginFullscreenPass(commandBuffer, target, _sceneWidth, _sceneHeight, PostShader.Disruption,
                _sceneFormat, blend: false, additive: false, clear: true, source,
                (SDL_GPUTexture*)resources.WhiteTextureHandle,
                resources.LinearClampSamplerHandle, resources.NearestClampSamplerHandle,
                &constants, (uint)sizeof(DisruptionConstants));
        }

        public void EncodeOverlays(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* target, uint width, uint height,
            SDL_GPUTextureFormat targetFormat, bool displayLinearComposition,
            SdlGpuSceneResources resources)
        {
            bool displayAssetsToLinear = displayLinearComposition;
            OverlaySlot slot = _overlaySlots[_device.FrameResources.CurrentSlotIndex];
            slot.Prepare(frame.OverlayCommands, commandBuffer);
            SDL_GPUColorTargetInfo targetInfo = new()
            {
                texture = target,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer, &targetInfo, 1, null);
            if (pass == null) throw new InvalidOperationException($"SDL overlay pass failed: {SDL3.SDL_GetError()}");
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new() { w = width, h = height, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                uint firstVertex = 0;
                const int populatedBindingCount = 2;
                int bindingCount = SdlGpuSamplerBindingAbi.BindingCountForDriver(
                    _device.Driver, populatedBindingCount);
                SDL_GPUTextureSamplerBinding* bindings
                    = stackalloc SDL_GPUTextureSamplerBinding[bindingCount];
                foreach (RenderOverlayCommand command in frame.OverlayCommands)
                {
                    if (command.Kind == RenderOverlayKind.StageMarker) continue;
                    uint vertexCount = checked((uint)command.Vertices.Count);
                    if (vertexCount < 3) throw new InvalidOperationException($"Overlay {command.Kind} has fewer than three vertices.");
                    if (command.Kind == RenderOverlayKind.Fade)
                    {
                        SDL3.SDL_EndGPURenderPass(pass);
                        pass = null;
                        EncodeFullscreen(commandBuffer, (SDL_GPUTexture*)resources.WhiteTextureHandle,
                            resources.WhiteTextureHandle, target, width, height, PostShader.Fullscreen,
                            targetFormat, operation: 1, alpha: command.Alpha,
                            color: command.Color, blend: true,
                            RenderCompositeFilter.Nearest, clear: false, resources,
                            displayAssetsToLinear);
                        continue;
                    }

                    SDL_GPUGraphicsPipeline* pipeline = Pipeline(new PostPipelineKey(PostShader.Hud, true,
                        SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP,
                        targetFormat));
                    SDL3.SDL_BindGPUGraphicsPipeline(pass, pipeline);
                    SdlGpuTelemetryContext.PipelineBind();
                    SDL_GPUBufferBinding vertex = new() { buffer = slot.Buffer, offset = 0 };
                    SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
                    SDL_GPUTexture* source = command.UseTexture
                        ? (SDL_GPUTexture*)resources.ResolveTextureHandle(frame, command.Texture, command.Kind.ToString())
                        : (SDL_GPUTexture*)resources.WhiteTextureHandle;
                    SDL_GPUTexture* mask = command.UseMask
                        ? (SDL_GPUTexture*)resources.ResolveTextureHandle(frame, command.MaskTexture, command.Kind + " mask")
                        : (SDL_GPUTexture*)resources.WhiteTextureHandle;
                    // Legacy HUD layers/objects always force nearest/clamp;
                    // scene texture filtering is a separate option.
                    SDL_GPUSampler* sampler = (SDL_GPUSampler*)resources.NearestClampSamplerHandle;
                    bindings[0] = new SDL_GPUTextureSamplerBinding { texture = source, sampler = sampler };
                    bindings[1] = new SDL_GPUTextureSamplerBinding { texture = mask, sampler = (SDL_GPUSampler*)resources.NearestClampSamplerHandle };
                    SdlGpuSamplerBindingAbi.Pad(bindings,
                        populatedBindingCount, bindingCount, source, sampler);
                    SDL3.SDL_BindGPUFragmentSamplers(pass, 0, bindings,
                        checked((uint)bindingCount));
                    SdlGpuTelemetryContext.SamplerBind(bindingCount);
                    HudConstants constants = new()
                    {
                        Options = new Vector4(command.Alpha, command.UseTexture ? 1 : 0,
                            command.UseMask ? 1 : 0,
                            displayAssetsToLinear ? 1 : 0),
                        Viewport = new Vector4(width, height, 0, 0)
                    };
                    SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, 0, (IntPtr)(&constants), (uint)sizeof(HudConstants));
                    SdlGpuTelemetryContext.FragmentUniform(sizeof(HudConstants));
                    SDL3.SDL_DrawGPUPrimitives(pass, vertexCount, 1, firstVertex, 0);
                    SdlGpuTelemetryContext.PrimitiveDraw();
                    firstVertex += vertexCount;
                }
            }
            finally
            {
                if (pass != null) SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void EncodeFullscreen(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture* source,
            nint maskHandle, SDL_GPUTexture* target, uint width, uint height, PostShader shader,
            SDL_GPUTextureFormat targetFormat, float operation, float alpha, Vector4 color,
            bool blend, RenderCompositeFilter filter, bool clear,
            SdlGpuSceneResources resources, bool displayAssetsToLinear = false,
            RenderDestinationViewport? destinationViewport = null)
        {
            FullscreenConstants constants = new()
            {
                Operation = new Vector4(operation, 0, 0, 0),
                FadeColor = new Vector4(color.X, color.Y, color.Z, alpha),
                Viewport = new Vector4(width, height, _sceneWidth, _sceneHeight),
                OverlayOptions = new Vector4(alpha, 0,
                    displayAssetsToLinear ? 1 : 0, 0)
            };
            nint sampler = filter == RenderCompositeFilter.Linear
                ? resources.LinearClampSamplerHandle : resources.NearestClampSamplerHandle;
            BeginFullscreenPass(commandBuffer, target, width, height, shader, targetFormat,
                blend, additive: false, clear, source, (SDL_GPUTexture*)maskHandle,
                sampler, sampler,
                &constants, (uint)sizeof(FullscreenConstants), destinationViewport);
        }

        private void BeginFullscreenPass(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture* target,
            uint width, uint height, PostShader shader, SDL_GPUTextureFormat targetFormat,
            bool blend, bool additive, bool clear,
            SDL_GPUTexture* source, SDL_GPUTexture* mask, nint samplerOne, nint samplerTwo,
            void* constants, uint constantsSize,
            RenderDestinationViewport? destinationViewport = null)
        {
            SDL_GPUColorTargetInfo targetInfo = new()
            {
                texture = target,
                clear_color = new SDL_FColor { a = 1 },
                load_op = clear ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer, &targetInfo, 1, null);
            if (pass == null) throw new InvalidOperationException($"SDL {shader} pass failed: {SDL3.SDL_GetError()}");
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                RenderDestinationViewport destination = destinationViewport
                    is { IsValid: true } requested
                    && requested.X + requested.Width <= width
                    && requested.Y + requested.Height <= height
                        ? requested : new(0, 0, checked((int)width), checked((int)height));
                SDL_GPUViewport viewport = new()
                {
                    x = destination.X,
                    y = destination.Y,
                    w = destination.Width,
                    h = destination.Height,
                    min_depth = 0,
                    max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                SDL3.SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PostPipelineKey(shader, blend,
                    SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP,
                    targetFormat, additive)));
                SdlGpuTelemetryContext.PipelineBind();
                SDL_GPUBufferBinding vertex = new() { buffer = _quadBuffer, offset = 0 };
                SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
                int count = SamplerCount(shader);
                int bindingCount = SdlGpuSamplerBindingAbi.BindingCountForDriver(
                    _device.Driver, count);
                SDL_GPUTextureSamplerBinding* bindings
                    = stackalloc SDL_GPUTextureSamplerBinding[bindingCount];
                bindings[0] = new SDL_GPUTextureSamplerBinding { texture = source, sampler = (SDL_GPUSampler*)samplerOne };
                if (count == 2)
                {
                    bindings[1] = new SDL_GPUTextureSamplerBinding
                        { texture = mask, sampler = (SDL_GPUSampler*)samplerTwo };
                }
                SdlGpuSamplerBindingAbi.Pad(bindings, count, bindingCount,
                    source, (SDL_GPUSampler*)samplerOne);
                SDL3.SDL_BindGPUFragmentSamplers(pass, 0, bindings,
                    checked((uint)bindingCount));
                SdlGpuTelemetryContext.SamplerBind(bindingCount);
                SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, 0, (IntPtr)constants, constantsSize);
                SdlGpuTelemetryContext.FragmentUniform(constantsSize);
                SDL3.SDL_DrawGPUPrimitives(pass, 4, 1, 0, 0);
                SdlGpuTelemetryContext.PrimitiveDraw();
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private SDL_GPUGraphicsPipeline* Pipeline(PostPipelineKey key)
        {
            if (_pipelines.TryGetValue(key, out nint handle)) return (SDL_GPUGraphicsPipeline*)handle;
            ShaderPair shaders = _shaders[key.Shader];
            bool hud = key.Shader == PostShader.Hud;
            SDL_GPUVertexBufferDescription description = new()
            {
                slot = 0, pitch = hud ? (uint)sizeof(OverlayVertex) : 5u * sizeof(float),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
            };
            SDL_GPUVertexAttribute* attributes = stackalloc SDL_GPUVertexAttribute[hud ? 3 : 2];
            attributes[0] = Attr(0, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 0);
            if (hud)
            {
                attributes[1] = Attr(1, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, 12);
                attributes[2] = Attr(2, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, 28);
            }
            else attributes[1] = Attr(1, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, 12);
            SDL_GPUVertexInputState input = new()
            {
                vertex_buffer_descriptions = &description, num_vertex_buffers = 1,
                vertex_attributes = attributes, num_vertex_attributes = hud ? 3u : 2u
            };
            SDL_GPUColorTargetDescription color = new()
            {
                format = key.TargetFormat,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    src_color_blendfactor = key.Additive
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : key.Blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = key.Additive
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : key.Blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    src_alpha_blendfactor = key.Additive
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : key.Blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_alpha_blendfactor = key.Additive
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : key.Blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    color_write_mask = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_B | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_A,
                    enable_blend = key.Blend, enable_color_write_mask = true
                }
            };
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = shaders.Vertex, fragment_shader = shaders.Fragment,
                vertex_input_state = input, primitive_type = key.Primitive,
                rasterizer_state = new SDL_GPURasterizerState { fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE, enable_depth_clip = true },
                multisample_state = new SDL_GPUMultisampleState { sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1 },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &color, num_color_targets = 1,
                    depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID,
                    has_depth_stencil_target = false
                }
            };
            SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(_device.Handle, &info);
            if (pipeline == null) throw new InvalidOperationException($"SDL {key.Shader} pipeline failed: {SDL3.SDL_GetError()}");
            _pipelines.Add(key, (nint)pipeline);
            return pipeline;
        }

        private ShaderPair CreateShaders(PostShader shader)
        {
            string stem = shader switch
            {
                PostShader.ToneMap => "tone_map",
                PostShader.ColorGrade => "color_grade",
                PostShader.Reconstruction => "reconstruction",
                PostShader.DistortionWarp => "distortion_warp",
                _ => shader.ToString().ToLowerInvariant()
            };
            (SDL_GPUShaderFormat format, string suffix) = SelectFormat();
            string directory = ShaderArtifactManifest.Directory;
            uint samplers = checked((uint)SamplerCount(shader));
            uint bindingCount = checked((uint)
                SdlGpuSamplerBindingAbi.BindingCountForDriver(_device.Driver,
                    checked((int)samplers)));
            SDL_GPUShader* vertex = CreateShader(format, Path.Combine(directory, $"{stem}.vert.{suffix}"),
                "main_vs", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
            try
            {
                return new ShaderPair(vertex,
                    CreateShader(format, Path.Combine(directory, $"{stem}.frag.{suffix}"), "main_ps",
                        SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                        bindingCount, 1));
            }
            catch
            {
                SDL3.SDL_ReleaseGPUShader(_device.Handle, vertex);
                throw;
            }
        }

        internal static int SamplerCount(PostShader shader)
            => shader switch
            {
                PostShader.Fullscreen or PostShader.Hud or PostShader.Cel
                    or PostShader.Bloom or PostShader.ColorGrade
                    or PostShader.DistortionWarp => 2,
                PostShader.Disruption or PostShader.ToneMap
                    or PostShader.Reconstruction or PostShader.Visor => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(shader))
            };

        private SDL_GPUShader* CreateShader(SDL_GPUShaderFormat format, string path, string entrypoint,
            SDL_GPUShaderStage stage, uint samplers, uint uniforms)
        {
            byte[] code = File.ReadAllBytes(path);
            byte[] name = Encoding.UTF8.GetBytes(entrypoint + "\0");
            fixed (byte* codePtr = code)
            fixed (byte* namePtr = name)
            {
                SDL_GPUShaderCreateInfo info = new()
                {
                    code_size = (UIntPtr)code.Length, code = codePtr, entrypoint = namePtr,
                    format = format, stage = stage, num_samplers = samplers, num_uniform_buffers = uniforms
                };
                SDL_GPUShader* result = SDL3.SDL_CreateGPUShader(_device.Handle, &info);
                if (result == null) throw new InvalidOperationException($"SDL post shader failed for {path}: {SDL3.SDL_GetError()}");
                return result;
            }
        }

        private (SDL_GPUShaderFormat, string) SelectFormat()
        {
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0) return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, "dxil");
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL) != 0) return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, "msl");
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV) != 0) return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, "spv");
            throw new PlatformNotSupportedException("No generated post shader format is supported.");
        }

        private void CreateQuad()
        {
            SDL_GPUBufferCreateInfo buffer = new() { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = 20 * 4 };
            _quadBuffer = SDL3.SDL_CreateGPUBuffer(_device.Handle, &buffer);
            SDL_GPUTransferBufferCreateInfo transfer = new() { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = 20 * 4 };
            _quadTransfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &transfer);
            if (_quadBuffer == null || _quadTransfer == null)
            {
                if (_quadTransfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _quadTransfer);
                if (_quadBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, _quadBuffer);
                _quadTransfer = null;
                _quadBuffer = null;
                throw new InvalidOperationException($"SDL fullscreen quad allocation failed: {SDL3.SDL_GetError()}");
            }
        }

        private void EnsureQuadUploaded(SDL_GPUCommandBuffer* commandBuffer)
        {
            if (_quadUploaded) return;
            float[] vertices = { 1, 1, 0, 1, 0, -1, 1, 0, 0, 0, 1, -1, 0, 1, 1, -1, -1, 0, 0, 1 };
            IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _quadTransfer, false);
            if (memory == IntPtr.Zero) throw new InvalidOperationException($"SDL fullscreen quad map failed: {SDL3.SDL_GetError()}");
            Marshal.Copy(vertices, 0, memory, vertices.Length);
            SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _quadTransfer);
            SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
            if (copy == null) throw new InvalidOperationException($"SDL fullscreen quad copy pass failed: {SDL3.SDL_GetError()}");
            SDL_GPUTransferBufferLocation source = new() { transfer_buffer = _quadTransfer };
            SDL_GPUBufferRegion target = new() { buffer = _quadBuffer, size = 80 };
            SDL3.SDL_UploadToGPUBuffer(copy, &source, &target, false);
            SdlGpuTelemetryContext.UploadScheduled(80);
            SDL3.SDL_EndGPUCopyPass(copy);
            _quadUploaded = true;
        }

        private void EnsureIntermediates(uint width, uint height,
            SDL_GPUTextureFormat format)
        {
            if (_intermediateA != null && width == _sceneWidth && height == _sceneHeight
                && format == _sceneFormat) return;
            SDL_GPUTexture* first = null;
            SDL_GPUTexture* second = null;
            SDL_GPUTexture* distortedBloom = null;
            try
            {
                first = CreateIntermediate(width, height, format);
                second = CreateIntermediate(width, height, format);
                distortedBloom = CreateIntermediate(width, height, format);
            }
            catch
            {
                if (distortedBloom != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, distortedBloom);
                if (second != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, second);
                if (first != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, first);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, second);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, first);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, distortedBloom);
                throw new InvalidOperationException($"SDL post target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_intermediateA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateA);
            if (_intermediateB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateB);
            if (_distortedBloom != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _distortedBloom);
            _intermediateA = first;
            _intermediateB = second;
            _distortedBloom = distortedBloom;
            _sceneWidth = width;
            _sceneHeight = height;
            _sceneFormat = format;
        }

        private SDL_GPUTexture* CreateIntermediate(uint width, uint height,
            SDL_GPUTextureFormat format)
        {
            SDL_GPUTextureCreateInfo info = new()
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = format,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                width = width, height = height, layer_count_or_depth = 1, num_levels = 1,
                sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
            };
            SDL_GPUTexture* result = SDL3.SDL_CreateGPUTexture(_device.Handle, &info);
            if (result == null) throw new InvalidOperationException($"SDL post target allocation failed: {SDL3.SDL_GetError()}");
            return result;
        }

        private bool TryEnsureBloomPyramid(uint width, uint height,
            SDL_GPUTextureFormat format)
        {
            BloomPyramidStorageFormat storageFormat = format
                == SdlGpuHdrPolicy.HdrFormat
                    ? BloomPyramidStorageFormat.Rgba16Float
                    : BloomPyramidStorageFormat.Rgba8Unorm;
            BloomPyramidConfiguration configuration = new(width, height,
                storageFormat, BloomPyramidWeights.Default);
            if (_bloomPyramid?.Configuration == configuration
                && HasBloomPyramidResources()) return true;
            if (!_bloomFailureCache.ShouldAttempt(configuration,
                qualityRequested: true, frameHasEligibleEmission: true)) return false;

            BloomPyramidPlan plan;
            try
            {
                plan = BloomPyramidPlan.Create(width, height, storageFormat);
            }
            catch (ArgumentOutOfRangeException error)
            {
                RecordBloomFailure(configuration, error.Message);
                return false;
            }

            SDL_GPUTexture* quarterA = null;
            SDL_GPUTexture* quarterB = null;
            SDL_GPUTexture* eighthA = null;
            SDL_GPUTexture* eighthB = null;
            SDL_GPUTexture* sixteenthA = null;
            SDL_GPUTexture* sixteenthB = null;
            try
            {
                quarterA = CreateIntermediate(plan.Levels[0].Width, plan.Levels[0].Height, format);
                quarterB = CreateIntermediate(plan.Levels[0].Width, plan.Levels[0].Height, format);
                eighthA = CreateIntermediate(plan.Levels[1].Width, plan.Levels[1].Height, format);
                eighthB = CreateIntermediate(plan.Levels[1].Width, plan.Levels[1].Height, format);
                sixteenthA = CreateIntermediate(plan.Levels[2].Width, plan.Levels[2].Height, format);
                sixteenthB = CreateIntermediate(plan.Levels[2].Width, plan.Levels[2].Height, format);
            }
            catch (InvalidOperationException error)
            {
                ReleaseBloomTextures(quarterA, quarterB, eighthA, eighthB,
                    sixteenthA, sixteenthB);
                RecordBloomFailure(configuration, error.Message);
                return false;
            }
            catch
            {
                ReleaseBloomTextures(quarterA, quarterB, eighthA, eighthB,
                    sixteenthA, sixteenthB);
                throw;
            }

            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                ReleaseBloomTextures(quarterA, quarterB, eighthA, eighthB,
                    sixteenthA, sixteenthB);
                RecordBloomFailure(configuration,
                    $"GPU idle wait failed: {SDL3.SDL_GetError()}");
                return false;
            }

            ReleaseBloomPyramid();
            _bloomQuarterA = quarterA;
            _bloomQuarterB = quarterB;
            _bloomEighthA = eighthA;
            _bloomEighthB = eighthB;
            _bloomSixteenthA = sixteenthA;
            _bloomSixteenthB = sixteenthB;
            _bloomPyramid = plan;
            _bloomFailureCache.RecordSuccess();
            _reportedBloomFailure = null;
            return true;
        }

        private bool HasBloomPyramidResources()
            => _bloomQuarterA != null && _bloomQuarterB != null
                && _bloomEighthA != null && _bloomEighthB != null
                && _bloomSixteenthA != null && _bloomSixteenthB != null;

        private void RecordBloomFailure(BloomPyramidConfiguration configuration,
            string reason)
        {
            _bloomFailureCache.RecordFailure(configuration);
            if (_reportedBloomFailure == configuration) return;
            _reportedBloomFailure = configuration;
            Console.Error.WriteLine(
                $"[render] HDR bloom disabled for this resource configuration: {reason}");
        }

        private void ReleaseBloomPyramid()
        {
            ReleaseBloomTextures(_bloomQuarterA, _bloomQuarterB,
                _bloomEighthA, _bloomEighthB, _bloomSixteenthA, _bloomSixteenthB);
            _bloomQuarterA = null;
            _bloomQuarterB = null;
            _bloomEighthA = null;
            _bloomEighthB = null;
            _bloomSixteenthA = null;
            _bloomSixteenthB = null;
            _bloomPyramid = null;
        }

        private void ReleaseBloomTextures(SDL_GPUTexture* quarterA,
            SDL_GPUTexture* quarterB, SDL_GPUTexture* eighthA,
            SDL_GPUTexture* eighthB, SDL_GPUTexture* sixteenthA,
            SDL_GPUTexture* sixteenthB)
        {
            if (sixteenthB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, sixteenthB);
            if (sixteenthA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, sixteenthA);
            if (eighthB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, eighthB);
            if (eighthA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, eighthA);
            if (quarterB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, quarterB);
            if (quarterA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, quarterA);
        }

        private void EnsureCaptureScene(uint width, uint height)
        {
            if (_captureSceneSdr != null && width == _captureWidth
                && height == _captureHeight) return;
            SDL_GPUTexture* replacement = CreateIntermediate(width, height,
                _device.SwapchainFormat);
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacement);
                throw new InvalidOperationException(
                    $"SDL SDR scene-capture target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_captureSceneSdr != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _captureSceneSdr);
            _captureSceneSdr = replacement;
            _captureWidth = width;
            _captureHeight = height;
        }

        private void EnsureDisplayLinear(uint width, uint height,
            SDL_GPUTextureFormat format)
        {
            if (_displayLinearA != null && _displayLinearB != null
                && width == _displayWidth
                && height == _displayHeight && format == _displayFormat) return;
            SDL_GPUTexture* replacementA = null;
            SDL_GPUTexture* replacementB = null;
            try
            {
                replacementA = CreateIntermediate(width, height, format);
                replacementB = CreateIntermediate(width, height, format);
            }
            catch
            {
                if (replacementB != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementB);
                if (replacementA != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementA);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementB);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementA);
                throw new InvalidOperationException(
                    $"SDL display-linear target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_displayLinearB != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _displayLinearB);
            if (_displayLinearA != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _displayLinearA);
            _displayLinearA = replacementA;
            _displayLinearB = replacementB;
            _displayLinearForFrame = replacementA;
            _displayWidth = width;
            _displayHeight = height;
            _displayFormat = format;
        }

        private static SDL_GPUVertexAttribute Attr(uint location, SDL_GPUVertexElementFormat format, uint offset)
            => new() { location = location, buffer_slot = 0, format = format, offset = offset };

        public void InvalidatePendingUploads() => _quadUploaded = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SDL3.SDL_WaitForGPUIdle(_device.Handle);
            foreach (OverlaySlot? slot in _overlaySlots) slot?.Dispose();
            foreach (nint pipeline in _pipelines.Values) SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, (SDL_GPUGraphicsPipeline*)pipeline);
            foreach (ShaderPair shaders in _shaders.Values) shaders.Dispose(_device.Handle);
            if (_displayLinearB != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _displayLinearB);
            if (_displayLinearA != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _displayLinearA);
            if (_captureSceneSdr != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _captureSceneSdr);
            ReleaseBloomPyramid();
            if (_intermediateB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateB);
            if (_intermediateA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateA);
            if (_distortedBloom != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _distortedBloom);
            if (_quadTransfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _quadTransfer);
            if (_quadBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, _quadBuffer);
        }

        internal enum PostShader : byte
        {
            Fullscreen,
            Hud,
            Disruption,
            Cel,
            Bloom,
            ToneMap,
            ColorGrade,
            Reconstruction,
            DistortionWarp,
            Visor
        }
        private readonly record struct PostPipelineKey(PostShader Shader, bool Blend,
            SDL_GPUPrimitiveType Primitive, SDL_GPUTextureFormat TargetFormat,
            bool Additive = false);
        private struct FullscreenConstants { public Vector4 Operation, FadeColor, Viewport, OverlayOptions; }
        private struct HudConstants { public Vector4 Options, Viewport; }
        private struct CelConstants { public Vector4 CelOptions, TexelOptions; }
        private struct BloomConstants { public Vector4 Options, TexelSize; }
        private struct ToneMapConstants { public Vector4 Options; }
        private struct ColorGradeConstants { public Vector4 Options; }
        private struct ReconstructionConstants
        {
            public Vector4 SourceAndDestination;
            public Vector4 Options;
        }
        private struct VisorConstants
        {
            public Vector4 Combat, Damage, DamageColor, LowHealth, Phases;
        }
        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DisruptionConstants { public Vector4 Options; public fixed float ShiftTable[64]; public fixed float WhiteoutTable[192]; }
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private readonly struct OverlayVertex
        {
            public readonly float X, Y, Z, R, G, B, A, U, V;
            public OverlayVertex(RenderOverlayVertex value)
            { X = value.Position.X; Y = value.Position.Y; Z = value.Position.Z; R = value.Color.X; G = value.Color.Y; B = value.Color.Z; A = value.Color.W; U = value.TexCoord.X; V = value.TexCoord.Y; }
        }

        private sealed class OverlaySlot : IDisposable
        {
            private readonly SdlGpuDevice _device;
            private SDL_GPUTransferBuffer* _transfer;
            private uint _capacity;
            public SDL_GPUBuffer* Buffer { get; private set; }
            public OverlaySlot(SdlGpuDevice device) { _device = device; }
            public void Prepare(IReadOnlyList<RenderOverlayCommand> commands, SDL_GPUCommandBuffer* commandBuffer)
            {
                int count = 0;
                foreach (RenderOverlayCommand command in commands)
                    if (command.Kind is not RenderOverlayKind.StageMarker and not RenderOverlayKind.Fade)
                        count = checked(count + command.Vertices.Count);
                if (count > MaximumOverlayVertices) throw new InvalidOperationException($"Overlay frame exceeded {MaximumOverlayVertices} vertices.");
                if (count == 0) return;
                uint bytes = checked((uint)(count * sizeof(OverlayVertex)));
                EnsureCapacity(bytes);
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _transfer, false);
                if (memory == IntPtr.Zero) throw new InvalidOperationException($"SDL overlay map failed: {SDL3.SDL_GetError()}");
                OverlayVertex* output = (OverlayVertex*)memory;
                int index = 0;
                foreach (RenderOverlayCommand command in commands)
                    if (command.Kind is not RenderOverlayKind.StageMarker and not RenderOverlayKind.Fade)
                        foreach (RenderOverlayVertex vertex in command.Vertices) output[index++] = new OverlayVertex(vertex);
                SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transfer);
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null) throw new InvalidOperationException($"SDL overlay copy pass failed: {SDL3.SDL_GetError()}");
                SDL_GPUTransferBufferLocation source = new() { transfer_buffer = _transfer };
                SDL_GPUBufferRegion target = new() { buffer = Buffer, size = bytes };
                SDL3.SDL_UploadToGPUBuffer(copy, &source, &target, false);
                SdlGpuTelemetryContext.UploadScheduled(bytes);
                SDL3.SDL_EndGPUCopyPass(copy);
            }
            private void EnsureCapacity(uint required)
            {
                if (_capacity >= required) return;
                uint capacity = 256;
                while (capacity < required) capacity = checked(capacity * 2);
                SDL_GPUBufferCreateInfo buffer = new() { usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, size = capacity };
                SDL_GPUTransferBufferCreateInfo transfer = new() { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = capacity };
                SDL_GPUBuffer* replacementBuffer = SDL3.SDL_CreateGPUBuffer(_device.Handle, &buffer);
                SDL_GPUTransferBuffer* replacementTransfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &transfer);
                if (replacementBuffer == null || replacementTransfer == null)
                {
                    if (replacementTransfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, replacementTransfer);
                    if (replacementBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, replacementBuffer);
                    throw new InvalidOperationException($"SDL overlay buffer allocation failed: {SDL3.SDL_GetError()}");
                }
                if (Buffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, Buffer);
                if (_transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                Buffer = replacementBuffer;
                _transfer = replacementTransfer;
                _capacity = capacity;
            }
            public void Dispose()
            {
                if (_transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                if (Buffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, Buffer);
                _transfer = null; Buffer = null;
            }
        }

        private readonly struct ShaderPair
        {
            public readonly SDL_GPUShader* Vertex;
            public readonly SDL_GPUShader* Fragment;
            public ShaderPair(SDL_GPUShader* vertex, SDL_GPUShader* fragment) { Vertex = vertex; Fragment = fragment; }
            public void Dispose(SDL_GPUDevice* device)
            { if (Fragment != null) SDL3.SDL_ReleaseGPUShader(device, Fragment); if (Vertex != null) SDL3.SDL_ReleaseGPUShader(device, Vertex); }
        }
    }
}
