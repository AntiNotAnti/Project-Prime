using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
    /// <summary>
    /// Feature-local half-resolution AO resources. Allocation and encode
    /// failures are cached per immutable surface configuration and never
    /// affect scene HDR ownership.
    /// </summary>
    internal unsafe sealed class SdlGpuSsaoResources : IDisposable
    {
        private const float SampleRadiusWorld = 0.75f;
        private const float ViewSpaceBias = 0.02f;
        private const float Strength = 0.7f;

        private readonly SdlGpuDevice _device;
        private readonly SdlGpuConfigurationFailureCache<
            SdlGpuEnhancedSurfaceConfiguration> _failure = new();
        private SDL_GPUShader* _vertexShader;
        private SDL_GPUShader* _fragmentShader;
        private SDL_GPUGraphicsPipeline* _pipeline;
        private SDL_GPUBuffer* _quadBuffer;
        private SDL_GPUTransferBuffer* _quadTransfer;
        private SDL_GPUTexture* _rawAndFinal;
        private SDL_GPUTexture* _horizontal;
        private SdlGpuEnhancedSurfacePlan? _plan;
        private SdlGpuEnhancedSurfaceConfiguration? _reportedFailure;
        private bool _quadUploaded;
        private bool _disposed;

        public SdlGpuSsaoResources(SdlGpuDevice device)
        {
            _device = device ?? throw new ArgumentNullException(nameof(device));
        }

        public SDL_GPUTexture* OcclusionTexture
            => _plan.HasValue
                && _failure.ShouldAttempt(_plan.Value.Configuration)
                    ? _rawAndFinal : null;

        public bool TryPrepare(SDL_GPUCommandBuffer* commandBuffer,
            SdlGpuEnhancedSurfacePlan plan)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (commandBuffer == null) throw new ArgumentNullException(nameof(commandBuffer));
            SdlGpuEnhancedSurfaceConfiguration configuration = plan.Configuration;
            if (!_failure.ShouldAttempt(configuration)) return false;
            if (_plan?.Configuration == configuration && _rawAndFinal != null
                && _horizontal != null && _pipeline != null)
            {
                return TryEnsureUploaded(commandBuffer, configuration);
            }

            try
            {
                EnsureShadersAndQuad();
                SDL_GPUTextureUsageFlags usage
                    = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                        | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
                SDL_GPUTextureFormat format
                    = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM;
                if (!SDL3.SDL_GPUTextureSupportsFormat(_device.Handle, format,
                    SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, usage))
                {
                    throw new PlatformNotSupportedException(
                        "SDL GPU device does not support sampleable R8 SSAO targets.");
                }

                SDL_GPUTexture* replacementRaw = null;
                SDL_GPUTexture* replacementHorizontal = null;
                try
                {
                    replacementRaw = CreateTarget(plan.SsaoWidth, plan.SsaoHeight);
                    replacementHorizontal = CreateTarget(plan.SsaoWidth,
                        plan.SsaoHeight);
                }
                catch
                {
                    if (replacementHorizontal != null)
                        SDL3.SDL_ReleaseGPUTexture(_device.Handle,
                            replacementHorizontal);
                    if (replacementRaw != null)
                        SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementRaw);
                    throw;
                }
                if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
                {
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle,
                        replacementHorizontal);
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, replacementRaw);
                    throw new InvalidOperationException(
                        $"SDL SSAO target resize wait failed: {SDL3.SDL_GetError()}");
                }
                if (_horizontal != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, _horizontal);
                if (_rawAndFinal != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, _rawAndFinal);
                _rawAndFinal = replacementRaw;
                _horizontal = replacementHorizontal;
                _plan = plan;
                _failure.RecordSuccess();
                _reportedFailure = null;
                return TryEnsureUploaded(commandBuffer, configuration);
            }
            catch (Exception error) when (error is InvalidOperationException
                or IOException or PlatformNotSupportedException)
            {
                RecordFailure(configuration, error.Message);
                return false;
            }
        }

        public bool TryEncode(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPUTexture* surface, nint linearClampSampler,
            Matrix4 view, Matrix4 projection)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_plan.HasValue || surface == null || _rawAndFinal == null
                || _horizontal == null) return false;
            SdlGpuEnhancedSurfacePlan plan = _plan.Value;
            if (!_failure.ShouldAttempt(plan.Configuration)) return false;
            try
            {
                EncodePass(commandBuffer, surface, surface, _rawAndFinal,
                    plan, SdlGpuSsaoPassKind.Raw, linearClampSampler,
                    view, projection);
                EncodePass(commandBuffer, surface, _rawAndFinal, _horizontal,
                    plan, SdlGpuSsaoPassKind.BilateralHorizontal,
                    linearClampSampler, view, projection);
                EncodePass(commandBuffer, surface, _horizontal, _rawAndFinal,
                    plan, SdlGpuSsaoPassKind.BilateralVertical,
                    linearClampSampler, view, projection);
                return true;
            }
            catch (InvalidOperationException error)
            {
                RecordFailure(plan.Configuration, error.Message);
                return false;
            }
        }

        private void EncodePass(SDL_GPUCommandBuffer* commandBuffer,
            SDL_GPUTexture* surface, SDL_GPUTexture* occlusion,
            SDL_GPUTexture* target, SdlGpuEnhancedSurfacePlan plan,
            SdlGpuSsaoPassKind operation, nint sampler,
            Matrix4 view, Matrix4 projection)
        {
            if (target == surface || target == occlusion)
                throw new InvalidOperationException(
                    "SDL SSAO pass cannot sample its render target.");
            SDL_GPUColorTargetInfo color = new()
            {
                texture = target,
                clear_color = new SDL_FColor { r = 1, g = 1, b = 1, a = 1 },
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(
                commandBuffer, &color, 1, null);
            if (pass == null)
                throw new InvalidOperationException(
                    $"SDL SSAO pass failed: {SDL3.SDL_GetError()}");
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new()
                {
                    w = plan.SsaoWidth, h = plan.SsaoHeight,
                    min_depth = 0, max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                SDL3.SDL_BindGPUGraphicsPipeline(pass, _pipeline);
                SdlGpuTelemetryContext.PipelineBind();
                SDL_GPUBufferBinding vertex = new() { buffer = _quadBuffer };
                SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
                SDL_GPUTextureSamplerBinding* bindings
                    = stackalloc SDL_GPUTextureSamplerBinding[2];
                bindings[0] = new() { texture = surface,
                    sampler = (SDL_GPUSampler*)sampler };
                bindings[1] = new() { texture = occlusion,
                    sampler = (SDL_GPUSampler*)sampler };
                SDL3.SDL_BindGPUFragmentSamplers(pass, 0, bindings, 2);
                SdlGpuTelemetryContext.SamplerBind(2);
                SsaoConstants constants = new()
                {
                    InverseProjection = SdlGpuMatrixAbi.Upload(
                        projection.Inverted()),
                    Projection = SdlGpuMatrixAbi.Upload(projection),
                    View = SdlGpuMatrixAbi.Upload(view),
                    Options = new Vector4((float)operation, SampleRadiusWorld,
                        ViewSpaceBias, Strength),
                    TexelOptions = new Vector4(
                        1f / plan.Configuration.Width,
                        1f / plan.Configuration.Height,
                        1f / plan.SsaoWidth, 1f / plan.SsaoHeight)
                };
                SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, 0,
                    (IntPtr)(&constants), (uint)sizeof(SsaoConstants));
                SdlGpuTelemetryContext.FragmentUniform(sizeof(SsaoConstants));
                SDL3.SDL_DrawGPUPrimitives(pass, 4, 1, 0, 0);
                SdlGpuTelemetryContext.PrimitiveDraw();
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private bool TryEnsureUploaded(SDL_GPUCommandBuffer* commandBuffer,
            SdlGpuEnhancedSurfaceConfiguration configuration)
        {
            if (_quadUploaded) return true;
            try
            {
                float[] vertices =
                {
                    1, 1, 0, 1, 0, -1, 1, 0, 0, 0,
                    1, -1, 0, 1, 1, -1, -1, 0, 0, 1
                };
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle,
                    _quadTransfer, false);
                if (memory == IntPtr.Zero)
                    throw new InvalidOperationException(
                        $"SDL SSAO quad map failed: {SDL3.SDL_GetError()}");
                Marshal.Copy(vertices, 0, memory, vertices.Length);
                SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _quadTransfer);
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null)
                    throw new InvalidOperationException(
                        $"SDL SSAO quad copy pass failed: {SDL3.SDL_GetError()}");
                SDL_GPUTransferBufferLocation source = new()
                {
                    transfer_buffer = _quadTransfer
                };
                SDL_GPUBufferRegion target = new()
                {
                    buffer = _quadBuffer, size = 20 * sizeof(float)
                };
                SDL3.SDL_UploadToGPUBuffer(copy, &source, &target, false);
                SdlGpuTelemetryContext.UploadScheduled(20 * sizeof(float));
                SDL3.SDL_EndGPUCopyPass(copy);
                _quadUploaded = true;
                return true;
            }
            catch (InvalidOperationException error)
            {
                RecordFailure(configuration, error.Message);
                return false;
            }
        }

        private void EnsureShadersAndQuad()
        {
            if (_pipeline != null && _quadBuffer != null
                && _quadTransfer != null) return;
            ShaderArtifactManifest.ValidateSsaoFresh();
            (SDL_GPUShaderFormat format, string suffix) = SelectFormat();
            string directory = ShaderArtifactManifest.Directory;
            if (_vertexShader == null)
            {
                _vertexShader = CreateShader(format,
                    Path.Combine(directory, $"ssao.vert.{suffix}"), "main_vs",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, 0, 0);
            }
            if (_fragmentShader == null)
            {
                _fragmentShader = CreateShader(format,
                    Path.Combine(directory, $"ssao.frag.{suffix}"), "main_ps",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, 2, 1);
            }
            if (_quadBuffer == null)
            {
                SDL_GPUBufferCreateInfo buffer = new()
                {
                    usage = SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX,
                    size = 20 * sizeof(float)
                };
                _quadBuffer = SDL3.SDL_CreateGPUBuffer(_device.Handle, &buffer);
            }
            if (_quadTransfer == null)
            {
                SDL_GPUTransferBufferCreateInfo transfer = new()
                {
                    usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                    size = 20 * sizeof(float)
                };
                _quadTransfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle,
                    &transfer);
            }
            if (_quadBuffer == null || _quadTransfer == null)
                throw new InvalidOperationException(
                    $"SDL SSAO quad allocation failed: {SDL3.SDL_GetError()}");
            if (_pipeline == null) _pipeline = CreatePipeline();
        }

        private SDL_GPUTexture* CreateTarget(uint width, uint height)
        {
            SDL_GPUTextureCreateInfo info = new()
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM,
                usage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                    | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                width = width, height = height, layer_count_or_depth = 1,
                num_levels = 1,
                sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
            };
            SDL_GPUTexture* result = SDL3.SDL_CreateGPUTexture(_device.Handle,
                &info);
            if (result == null)
                throw new InvalidOperationException(
                    $"SDL SSAO target allocation failed: {SDL3.SDL_GetError()}");
            return result;
        }

        private SDL_GPUGraphicsPipeline* CreatePipeline()
        {
            SDL_GPUVertexBufferDescription description = new()
            {
                slot = 0, pitch = 5 * sizeof(float),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
            };
            SDL_GPUVertexAttribute* attributes = stackalloc SDL_GPUVertexAttribute[2];
            attributes[0] = new() { location = 0, buffer_slot = 0,
                format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3,
                offset = 0 };
            attributes[1] = new() { location = 1, buffer_slot = 0,
                format = SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2,
                offset = 3 * sizeof(float) };
            SDL_GPUVertexInputState input = new()
            {
                vertex_buffer_descriptions = &description,
                num_vertex_buffers = 1, vertex_attributes = attributes,
                num_vertex_attributes = 2
            };
            SDL_GPUColorTargetDescription color = new()
            {
                format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    color_write_mask = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R,
                    enable_blend = false, enable_color_write_mask = true
                }
            };
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = _vertexShader, fragment_shader = _fragmentShader,
                vertex_input_state = input,
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLESTRIP,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                    enable_depth_clip = true
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
                },
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &color, num_color_targets = 1,
                    depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID,
                    has_depth_stencil_target = false
                }
            };
            SDL_GPUGraphicsPipeline* result = SDL3.SDL_CreateGPUGraphicsPipeline(
                _device.Handle, &info);
            if (result == null)
                throw new InvalidOperationException(
                    $"SDL SSAO pipeline failed: {SDL3.SDL_GetError()}");
            return result;
        }

        private SDL_GPUShader* CreateShader(SDL_GPUShaderFormat format,
            string path, string entrypoint, SDL_GPUShaderStage stage,
            uint samplers, uint uniforms)
        {
            byte[] code = File.ReadAllBytes(path);
            byte[] name = Encoding.UTF8.GetBytes(entrypoint + "\0");
            fixed (byte* codePointer = code)
            fixed (byte* namePointer = name)
            {
                SDL_GPUShaderCreateInfo info = new()
                {
                    code_size = (UIntPtr)code.Length, code = codePointer,
                    entrypoint = namePointer, format = format, stage = stage,
                    num_samplers = samplers, num_uniform_buffers = uniforms
                };
                SDL_GPUShader* result = SDL3.SDL_CreateGPUShader(_device.Handle,
                    &info);
                if (result == null)
                    throw new InvalidOperationException(
                        $"SDL SSAO shader failed for {path}: {SDL3.SDL_GetError()}");
                return result;
            }
        }

        private (SDL_GPUShaderFormat Format, string Suffix) SelectFormat()
        {
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, "dxil");
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, "msl");
            if ((_device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, "spv");
            throw new PlatformNotSupportedException(
                "No generated SSAO shader format is supported.");
        }

        private void RecordFailure(SdlGpuEnhancedSurfaceConfiguration configuration,
            string reason)
        {
            _failure.RecordFailure(configuration);
            if (_reportedFailure == configuration) return;
            _reportedFailure = configuration;
            Console.Error.WriteLine(
                $"[render] Enhanced SSAO disabled for this resource configuration: {reason}");
        }

        public void InvalidatePendingUploads() => _quadUploaded = false;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SDL3.SDL_WaitForGPUIdle(_device.Handle);
            if (_horizontal != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _horizontal);
            if (_rawAndFinal != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _rawAndFinal);
            if (_pipeline != null)
                SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, _pipeline);
            if (_quadTransfer != null)
                SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _quadTransfer);
            if (_quadBuffer != null)
                SDL3.SDL_ReleaseGPUBuffer(_device.Handle, _quadBuffer);
            if (_fragmentShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _fragmentShader);
            if (_vertexShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _vertexShader);
        }

        private struct SsaoConstants
        {
            public Matrix4 InverseProjection;
            public Matrix4 Projection;
            public Matrix4 View;
            public Vector4 Options;
            public Vector4 TexelOptions;
        }
    }
}
