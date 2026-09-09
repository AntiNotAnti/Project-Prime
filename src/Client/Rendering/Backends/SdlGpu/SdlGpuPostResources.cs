using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using SDL;

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

    /// <summary>Fixed R7 post/HUD command sequence; deliberately not a render graph.</summary>
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
        private SDL_GPUTexture* _bloomA;
        private SDL_GPUTexture* _bloomB;
        private uint _sceneWidth;
        private uint _sceneHeight;
        private uint _bloomWidth;
        private uint _bloomHeight;
        private bool _reportedUnsampleableDepth;
        private bool _disposed;

        private SdlGpuPostResources(SdlGpuDevice device)
        {
            _device = device;
            _overlaySlots = new OverlaySlot[device.FrameResources.SlotCount];
            try
            {
                ShaderArtifactManifest.ValidatePostFresh();
                for (int i = 0; i < _overlaySlots.Length; i++) _overlaySlots[i] = new OverlaySlot(device);
                foreach (PostShader shader in Enum.GetValues<PostShader>()) _shaders.Add(shader, CreateShaders(shader));
                CreateQuad();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static SdlGpuPostResources Create(SdlGpuDevice device) => new(device);

        public void Encode(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* sceneColor, SDL_GPUTexture* sceneDepth, SDL_GPUTexture* bloomColor,
            SdlGpuBloomPlan bloomPlan, SDL_GPUTexture* finalComposite,
            uint finalWidth, uint finalHeight, SdlGpuSceneResources sceneResources)
        {
            SdlGpuPresentationOrder.Validate(frame.OverlayCommands);
            EnsureQuadUploaded(commandBuffer);
            EnsureIntermediates(checked((uint)Math.Max(1, frame.SceneTargetSize.X)),
                checked((uint)Math.Max(1, frame.SceneTargetSize.Y)));

            SDL_GPUTexture* current = sceneColor;
            if (frame.CelState.Enabled && frame.CelState.Outline > 0 && sceneResources.DepthSampleable)
            {
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeCel(commandBuffer, frame, current, sceneDepth, target, sceneResources);
                current = target;
            }
            else if (frame.CelState.Enabled && frame.CelState.Outline > 0 && !_reportedUnsampleableDepth)
            {
                _reportedUnsampleableDepth = true;
                Console.Error.WriteLine("[render] cel outline disabled: SDL GPU depth-stencil targets are not sampleable on this device.");
            }
            if (bloomColor != null)
            {
                if (!bloomPlan.Enabled)
                    throw new InvalidOperationException("SDL bloom texture has no enabled bloom plan.");
                EnsureBloomIntermediates(bloomPlan.BlurWidth, bloomPlan.BlurHeight);
                EncodeBloomBlur(commandBuffer, bloomColor, _bloomA,
                    bloomPlan.BlurWidth, bloomPlan.BlurHeight,
                    _sceneWidth, _sceneHeight, horizontal: true, sceneResources);
                EncodeBloomBlur(commandBuffer, _bloomA, _bloomB,
                    bloomPlan.BlurWidth, bloomPlan.BlurHeight,
                    bloomPlan.BlurWidth, bloomPlan.BlurHeight,
                    horizontal: false, sceneResources);

                // Bloom belongs to the scene-effects chain so disruption and
                // whiteout affect the composited result. Copy the current
                // scene into the other full-size intermediate, then add the
                // blurred emission without sampling and rendering one texture.
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeFullscreen(commandBuffer, current, sceneResources.WhiteTextureHandle,
                    target, _sceneWidth, _sceneHeight, PostShader.Fullscreen,
                    operation: 0, alpha: 1, color: Vector4.One,
                    blend: false, RenderCompositeFilter.Nearest, clear: true, sceneResources);
                EncodeBloomComposite(commandBuffer, _bloomB, target,
                    _sceneWidth, _sceneHeight, sceneResources);
                current = target;
            }
            else if (bloomPlan.Enabled)
            {
                throw new InvalidOperationException("SDL enabled bloom plan has no resolved emission texture.");
            }

            if (frame.Disruption.Enabled)
            {
                SDL_GPUTexture* target = NextIntermediate(current);
                EncodeDisruption(commandBuffer, frame, current, target, sceneResources);
                current = target;
            }

            EncodeFullscreen(commandBuffer, current, sceneResources.WhiteTextureHandle,
                finalComposite, finalWidth, finalHeight, PostShader.Fullscreen,
                operation: 0, alpha: 1, color: Vector4.One,
                blend: false, frame.Composite.Filter, clear: frame.Composite.ClearDestination,
                sceneResources);
            EncodeOverlays(commandBuffer, frame, finalComposite, finalWidth, finalHeight, sceneResources);
        }

        private SDL_GPUTexture* NextIntermediate(SDL_GPUTexture* current)
            => current == _intermediateA ? _intermediateB : _intermediateA;

        private void EncodeBloomBlur(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPUTexture* source, SDL_GPUTexture* target, uint targetWidth,
            uint targetHeight, uint sourceWidth, uint sourceHeight, bool horizontal,
            SdlGpuSceneResources resources)
        {
            BloomConstants constants = new()
            {
                Options = new Vector4(horizontal ? 1 : 0, horizontal ? 0 : 1, 0, 0),
                TexelSize = new Vector4(1f / sourceWidth, 1f / sourceHeight, 0, 0)
            };
            BeginFullscreenPass(commandBuffer, target, targetWidth, targetHeight,
                PostShader.Bloom, blend: false, additive: false, clear: true,
                source, (SDL_GPUTexture*)resources.WhiteTextureHandle,
                resources.LinearClampSamplerHandle, resources.NearestClampSamplerHandle,
                &constants, (uint)sizeof(BloomConstants));
        }

        private void EncodeBloomComposite(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPUTexture* source, SDL_GPUTexture* target, uint width, uint height,
            SdlGpuSceneResources resources)
        {
            BloomConstants constants = new()
            {
                Options = new Vector4(0, 0, BloomCompositeStrength, 0),
                TexelSize = Vector4.Zero
            };
            BeginFullscreenPass(commandBuffer, target, width, height,
                PostShader.Bloom, blend: true, additive: true, clear: false,
                source, (SDL_GPUTexture*)resources.WhiteTextureHandle,
                resources.LinearClampSamplerHandle, resources.NearestClampSamplerHandle,
                &constants, (uint)sizeof(BloomConstants));
        }

        private void EncodeCel(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* source, SDL_GPUTexture* depth, SDL_GPUTexture* target,
            SdlGpuSceneResources resources)
        {
            CelConstants constants = new()
            {
                CelOptions = new Vector4(frame.CelState.Outline, frame.CelState.NearPlane,
                    frame.CelState.FarPlane, frame.CelState.DepthQuantum),
                TexelOptions = new Vector4(frame.CelState.TexelSize.X, frame.CelState.TexelSize.Y, 0, 0)
            };
            BeginFullscreenPass(commandBuffer, target, _sceneWidth, _sceneHeight, PostShader.Cel,
                blend: false, additive: false, clear: true, source, depth,
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
                blend: false, additive: false, clear: true, source,
                (SDL_GPUTexture*)resources.WhiteTextureHandle,
                resources.LinearClampSamplerHandle, resources.NearestClampSamplerHandle,
                &constants, (uint)sizeof(DisruptionConstants));
        }

        private void EncodeOverlays(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* target, uint width, uint height, SdlGpuSceneResources resources)
        {
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
            try
            {
                SDL_GPUViewport viewport = new() { w = width, h = height, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                uint firstVertex = 0;
                SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
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
                            operation: 1, alpha: command.Alpha, color: command.Color,
                            blend: true, RenderCompositeFilter.Nearest, clear: false, resources);
                        continue;
                    }

                    SDL_GPUGraphicsPipeline* pipeline = Pipeline(new PostPipelineKey(PostShader.Hud, true,
                        SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP));
                    SDL3.SDL_BindGPUGraphicsPipeline(pass, pipeline);
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
                    SDL3.SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
                    HudConstants constants = new()
                    {
                        Options = new Vector4(command.Alpha, command.UseTexture ? 1 : 0, command.UseMask ? 1 : 0, 0),
                        Viewport = new Vector4(width, height, 0, 0)
                    };
                    SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, 0, (IntPtr)(&constants), (uint)sizeof(HudConstants));
                    SDL3.SDL_DrawGPUPrimitives(pass, vertexCount, 1, firstVertex, 0);
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
            float operation, float alpha, Vector4 color, bool blend, RenderCompositeFilter filter, bool clear,
            SdlGpuSceneResources resources)
        {
            FullscreenConstants constants = new()
            {
                Operation = new Vector4(operation, 0, 0, 0),
                FadeColor = new Vector4(color.X, color.Y, color.Z, alpha),
                Viewport = new Vector4(width, height, _sceneWidth, _sceneHeight),
                OverlayOptions = new Vector4(alpha, 0, 0, 0)
            };
            nint sampler = filter == RenderCompositeFilter.Linear
                ? resources.LinearClampSamplerHandle : resources.NearestClampSamplerHandle;
            BeginFullscreenPass(commandBuffer, target, width, height, shader,
                blend, additive: false, clear, source, (SDL_GPUTexture*)maskHandle,
                sampler, sampler,
                &constants, (uint)sizeof(FullscreenConstants));
        }

        private void BeginFullscreenPass(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture* target,
            uint width, uint height, PostShader shader, bool blend, bool additive, bool clear,
            SDL_GPUTexture* source, SDL_GPUTexture* mask, nint samplerOne, nint samplerTwo,
            void* constants, uint constantsSize)
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
            try
            {
                SDL_GPUViewport viewport = new() { w = width, h = height, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                SDL3.SDL_BindGPUGraphicsPipeline(pass, Pipeline(new PostPipelineKey(shader, blend,
                    SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP, additive)));
                SDL_GPUBufferBinding vertex = new() { buffer = _quadBuffer, offset = 0 };
                SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
                int count = shader is PostShader.Fullscreen or PostShader.Cel ? 2 : 1;
                SDL_GPUTextureSamplerBinding* bindings = stackalloc SDL_GPUTextureSamplerBinding[2];
                bindings[0] = new SDL_GPUTextureSamplerBinding { texture = source, sampler = (SDL_GPUSampler*)samplerOne };
                bindings[1] = new SDL_GPUTextureSamplerBinding { texture = mask, sampler = (SDL_GPUSampler*)samplerTwo };
                SDL3.SDL_BindGPUFragmentSamplers(pass, 0, bindings, checked((uint)count));
                SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, 0, (IntPtr)constants, constantsSize);
                SDL3.SDL_DrawGPUPrimitives(pass, 4, 1, 0, 0);
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
                format = _device.SwapchainFormat,
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
            string stem = shader.ToString().ToLowerInvariant();
            (SDL_GPUShaderFormat format, string suffix) = SelectFormat();
            string directory = ShaderArtifactManifest.Directory;
            uint samplers = shader switch
            {
                PostShader.Disruption or PostShader.Bloom => 1,
                _ => 2
            };
            SDL_GPUShader* vertex = CreateShader(format, Path.Combine(directory, $"{stem}.vert.{suffix}"),
                "main_vs", SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
            try
            {
                return new ShaderPair(vertex,
                    CreateShader(format, Path.Combine(directory, $"{stem}.frag.{suffix}"), "main_ps",
                        SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers, 1));
            }
            catch
            {
                SDL3.SDL_ReleaseGPUShader(_device.Handle, vertex);
                throw;
            }
        }

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
            SDL3.SDL_EndGPUCopyPass(copy);
            _quadUploaded = true;
        }

        private void EnsureIntermediates(uint width, uint height)
        {
            if (_intermediateA != null && width == _sceneWidth && height == _sceneHeight) return;
            SDL_GPUTexture* first = null;
            SDL_GPUTexture* second = null;
            try
            {
                first = CreateIntermediate(width, height);
                second = CreateIntermediate(width, height);
            }
            catch
            {
                if (second != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, second);
                if (first != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, first);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, second);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, first);
                throw new InvalidOperationException($"SDL post target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_intermediateA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateA);
            if (_intermediateB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateB);
            _intermediateA = first; _intermediateB = second; _sceneWidth = width; _sceneHeight = height;
        }

        private SDL_GPUTexture* CreateIntermediate(uint width, uint height)
        {
            SDL_GPUTextureCreateInfo info = new()
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = _device.SwapchainFormat,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                width = width, height = height, layer_count_or_depth = 1, num_levels = 1,
                sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
            };
            SDL_GPUTexture* result = SDL3.SDL_CreateGPUTexture(_device.Handle, &info);
            if (result == null) throw new InvalidOperationException($"SDL post target allocation failed: {SDL3.SDL_GetError()}");
            return result;
        }

        private void EnsureBloomIntermediates(uint width, uint height)
        {
            if (_bloomA != null && width == _bloomWidth && height == _bloomHeight) return;
            SDL_GPUTexture* first = null;
            SDL_GPUTexture* second = null;
            try
            {
                first = CreateIntermediate(width, height);
                second = CreateIntermediate(width, height);
            }
            catch
            {
                if (second != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, second);
                if (first != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, first);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, second);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, first);
                throw new InvalidOperationException(
                    $"SDL bloom blur target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_bloomB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomB);
            if (_bloomA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomA);
            _bloomA = first;
            _bloomB = second;
            _bloomWidth = width;
            _bloomHeight = height;
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
            if (_bloomB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomB);
            if (_bloomA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomA);
            if (_intermediateB != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateB);
            if (_intermediateA != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _intermediateA);
            if (_quadTransfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _quadTransfer);
            if (_quadBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, _quadBuffer);
        }

        private enum PostShader : byte { Fullscreen, Hud, Disruption, Cel, Bloom }
        private readonly record struct PostPipelineKey(PostShader Shader, bool Blend,
            SDL_GPUPrimitiveType Primitive, bool Additive = false);
        private struct FullscreenConstants { public Vector4 Operation, FadeColor, Viewport, OverlayOptions; }
        private struct HudConstants { public Vector4 Options, Viewport; }
        private struct CelConstants { public Vector4 CelOptions, TexelOptions; }
        private struct BloomConstants { public Vector4 Options, TexelSize; }
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
