using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using MphRead.Mods;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
    internal readonly record struct SdlGpuSkyConfiguration(
        EnhancedSkyAssetKey Key, SDL_GPUTextureFormat ColorFormat, int SampleCount);

    internal static class SdlGpuSkyPolicy
    {
        public static bool IsEligible(RenderSkyState? sky, GraphicsPreset preset)
            => sky is { Enabled: true }
                && preset == GraphicsPreset.Enhanced
                && sky.BaseKind is EnhancedSkyBaseKind.Cubemap
                    or EnhancedSkyBaseKind.Background2D;

        public static bool OpaqueClearsColor(bool skyEncoded) => !skyEncoded;
    }

    /// <summary>
    /// Optional device-local VE22 shader/pipeline owner. Preparation failures
    /// are cached per immutable sky/target configuration; command-pass failures
    /// are intentionally allowed to propagate as fatal encoder failures.
    /// </summary>
    internal unsafe sealed class SdlGpuSkyResources : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SkyConstants
        {
            public Matrix4 InverseProjection;
            public Matrix4 InverseViewRotation;
            public Vector4 Options;
            public Vector4 Motion;
        }

        private readonly record struct PreparedLayer(
            nint Texture, RenderSkyLayer Layer);

        private sealed class PreparedSky
        {
            public required nint[] BaseTextures { get; init; }
            public required PreparedLayer[] Layers { get; init; }
        }

        private readonly SdlGpuDevice _device;
        private readonly SdlGpuConfigurationFailureCache<SdlGpuSkyConfiguration> _failure = new();
        private SdlGpuSkyConfiguration? _resourceConfiguration;
        private SdlGpuSkyConfiguration? _reportedFailure;
        private SDL_GPUShader* _vertexShader;
        private SDL_GPUShader* _fragmentShader;
        private SDL_GPUGraphicsPipeline* _opaquePipeline;
        private SDL_GPUGraphicsPipeline* _alphaPipeline;
        private bool _disposed;

        public SdlGpuSkyResources(SdlGpuDevice device)
            => _device = device ?? throw new ArgumentNullException(nameof(device));

        public bool TryEncode(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            SDL_GPUTexture* target, SDL_GPUTextureFormat colorFormat, int sampleCount,
            SDL_FColor clearColor, nint sampler,
            Action<TextureIdentity> prepareTexture,
            Action<TextureIdentity> encodeTextureUpload,
            Func<TextureIdentity, nint> resolveTexture)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!SdlGpuSkyPolicy.IsEligible(frame.Sky,
                    frame.Options.Quality.GraphicsPreset)) return false;
            RenderSkyState sky = frame.Sky!;
            var configuration = new SdlGpuSkyConfiguration(sky.Key, colorFormat,
                sampleCount);
            if (!_failure.ShouldAttempt(configuration)) return false;

            PreparedSky prepared;
            try
            {
                foreach (TextureIdentity identity in sky.TextureIdentities)
                    prepareTexture(identity);
                EnsurePipelines(configuration, sky.Composition.Count != 0);
                prepared = PrepareTextures(sky, resolveTexture);
                _failure.RecordSuccess();
                _reportedFailure = null;
            }
            catch (Exception error) when (error is not OutOfMemoryException
                and not StackOverflowException)
            {
                _failure.RecordFailure(configuration);
                if (_reportedFailure != configuration)
                {
                    _reportedFailure = configuration;
                    Console.Error.WriteLine("[render] Enhanced sky disabled for "
                        + $"'{sky.Key}' and {colorFormat}/{sampleCount}x: {error.Message}");
                }
                return false;
            }

            // Copy-pass encoding is deliberately outside the fail-soft preparation
            // boundary. A command-buffer failure must retain the backend's fatal
            // submit behavior rather than being mistaken for a bad optional pack.
            foreach (TextureIdentity identity in sky.TextureIdentities)
                encodeTextureUpload(identity);
            EncodePrepared(commandBuffer, frame, sky, prepared, target,
                clearColor, sampler);
            return true;
        }

        private PreparedSky PrepareTextures(RenderSkyState sky,
            Func<TextureIdentity, nint> resolveTexture)
        {
            nint[] baseTextures = new nint[6];
            if (sky.BaseKind == EnhancedSkyBaseKind.Cubemap)
            {
                RenderSkyCubemap cubemap = sky.Cubemap
                    ?? throw new InvalidOperationException("Sky cubemap state is incomplete.");
                for (int face = 0; face < cubemap.Faces.Count; face++)
                    baseTextures[face] = Resolve(cubemap.Faces[face], resolveTexture);
            }
            else
            {
                TextureIdentity background = sky.Background
                    ?? throw new InvalidOperationException("Sky background state is incomplete.");
                nint texture = Resolve(background, resolveTexture);
                Array.Fill(baseTextures, texture);
            }

            var layers = new PreparedLayer[sky.Composition.Count];
            for (int i = 0; i < layers.Length; i++)
            {
                RenderSkyLayer layer = sky.Composition[i];
                if (layer.Blend != EnhancedSkyOverlayBlend.StraightAlpha
                    || layer.SelectiveBloom)
                {
                    throw new InvalidOperationException(
                        "Sky layer violates the VE22 composition contract.");
                }
                layers[i] = new PreparedLayer(
                    Resolve(layer.Texture, resolveTexture), layer);
            }
            return new PreparedSky { BaseTextures = baseTextures, Layers = layers };
        }

        private static nint Resolve(TextureIdentity identity,
            Func<TextureIdentity, nint> resolveTexture)
        {
            nint handle = resolveTexture(identity);
            if (handle == 0) throw new InvalidOperationException(
                $"Sky texture {identity} has no uploaded SDL resource.");
            return handle;
        }

        private void EncodePrepared(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, RenderSkyState sky, PreparedSky prepared,
            SDL_GPUTexture* target, SDL_FColor clearColor, nint samplerHandle)
        {
            if (commandBuffer == null || target == null || _opaquePipeline == null
                || _vertexShader == null || _fragmentShader == null)
            {
                throw new InvalidOperationException("SDL sky encoder resources are incomplete.");
            }
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = target,
                clear_color = clearColor,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer,
                &colorTarget, 1, null);
            if (pass == null) throw new InvalidOperationException(
                $"SDL sky pass failed: {SDL3.SDL_GetError()}");
            SdlGpuTelemetryContext.RenderPass();
            try
            {
                SDL_GPUViewport viewport = new()
                {
                    w = checked((uint)Math.Max(1, frame.SceneTargetSize.X)),
                    h = checked((uint)Math.Max(1, frame.SceneTargetSize.Y)),
                    min_depth = 0,
                    max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                SDL_GPUSampler* sampler = (SDL_GPUSampler*)samplerHandle;
                BindTextures(pass, prepared.BaseTextures, sampler);
                PushConstants(commandBuffer, frame, sky,
                    sky.BaseKind == EnhancedSkyBaseKind.Cubemap ? 1 : 0,
                    opacity: 1, intensity: 1, default);
                SDL3.SDL_BindGPUGraphicsPipeline(pass, _opaquePipeline);
                SdlGpuTelemetryContext.PipelineBind();
                SDL3.SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
                SdlGpuTelemetryContext.PrimitiveDraw();

                if (prepared.Layers.Length != 0)
                {
                    if (_alphaPipeline == null) throw new InvalidOperationException(
                        "SDL sky alpha pipeline is unavailable.");
                    SDL3.SDL_BindGPUGraphicsPipeline(pass, _alphaPipeline);
                    SdlGpuTelemetryContext.PipelineBind();
                    nint[] layerTextures = new nint[6];
                    foreach (PreparedLayer preparedLayer in prepared.Layers)
                    {
                        Array.Fill(layerTextures, preparedLayer.Texture);
                        BindTextures(pass, layerTextures, sampler);
                        RenderSkyLayer layer = preparedLayer.Layer;
                        PushConstants(commandBuffer, frame, sky, 2, layer.Opacity,
                            layer.Intensity, new Vector4(layer.ScrollU, layer.ScrollV,
                                layer.RotationSpeed, layer.TwinkleRate));
                        SDL3.SDL_DrawGPUPrimitives(pass, 3, 1, 0, 0);
                        SdlGpuTelemetryContext.PrimitiveDraw();
                    }
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void BindTextures(SDL_GPURenderPass* pass,
            IReadOnlyList<nint> textures, SDL_GPUSampler* sampler)
        {
            int bindingCount = SdlGpuSamplerBindingAbi.BindingCountForDriver(
                _device.Driver, 6);
            SDL_GPUTextureSamplerBinding* bindings
                = stackalloc SDL_GPUTextureSamplerBinding[bindingCount];
            for (int i = 0; i < 6; i++)
            {
                bindings[i] = new SDL_GPUTextureSamplerBinding
                    { texture = (SDL_GPUTexture*)textures[i], sampler = sampler };
            }
            SdlGpuSamplerBindingAbi.Pad(bindings, 6, bindingCount,
                (SDL_GPUTexture*)textures[0], sampler);
            SDL3.SDL_BindGPUFragmentSamplers(pass, 0, bindings,
                checked((uint)bindingCount));
            SdlGpuTelemetryContext.SamplerBind(bindingCount);
        }

        private static void PushConstants(SDL_GPUCommandBuffer* commandBuffer,
            RenderFrame frame, RenderSkyState sky, float mode, float opacity,
            float intensity, Vector4 motion)
        {
            SkyConstants constants = new()
            {
                InverseProjection = SdlGpuMatrixAbi.Upload(
                    frame.ProjectionMatrix.Inverted()),
                // The captured inverse rotation has no camera translation.
                InverseViewRotation = SdlGpuMatrixAbi.Upload(
                    frame.ViewInverseRotation),
                Options = new Vector4(mode, sky.TimeSeconds, opacity, intensity),
                Motion = motion
            };
            SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, 0,
                (IntPtr)(&constants), (uint)sizeof(SkyConstants));
            SdlGpuTelemetryContext.FragmentUniform(sizeof(SkyConstants));
        }

        private void EnsurePipelines(SdlGpuSkyConfiguration configuration,
            bool needsAlpha)
        {
            if (_resourceConfiguration == configuration && _opaquePipeline != null
                && (!needsAlpha || _alphaPipeline != null)) return;
            ReleaseResources();
            try
            {
                ShaderArtifactManifest.ValidateSkyFresh();
                (SDL_GPUShaderFormat format, string suffix) = SelectFormat();
                string directory = ShaderArtifactManifest.Directory;
                _vertexShader = CreateShader(format, Path.Combine(directory,
                    $"sky.vert.{suffix}"), SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX,
                    samplers: 0, uniforms: 0);
                _fragmentShader = CreateShader(format, Path.Combine(directory,
                    $"sky.frag.{suffix}"), SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT,
                    samplers: checked((uint)
                        SdlGpuSamplerBindingAbi.BindingCountForDriver(
                            _device.Driver, 6)), uniforms: 1);
                _opaquePipeline = CreatePipeline(configuration, blend: false);
                if (needsAlpha) _alphaPipeline = CreatePipeline(configuration, blend: true);
                _resourceConfiguration = configuration;
            }
            catch
            {
                ReleaseResources();
                throw;
            }
        }

        private SDL_GPUGraphicsPipeline* CreatePipeline(
            SdlGpuSkyConfiguration configuration, bool blend)
        {
            SDL_GPUColorTargetDescription color = new()
            {
                format = configuration.ColorFormat,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    src_color_blendfactor = blend
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = blend
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    src_alpha_blendfactor = blend
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_alpha_blendfactor = blend
                        ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    color_write_mask = SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_B
                        | SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_A,
                    enable_blend = blend,
                    enable_color_write_mask = true
                }
            };
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = _vertexShader,
                fragment_shader = _fragmentShader,
                vertex_input_state = default,
                primitive_type = SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE,
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = configuration.SampleCount switch
                    {
                        4 => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4,
                        2 => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2,
                        _ => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
                    }
                },
                depth_stencil_state = default,
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &color,
                    num_color_targets = 1,
                    depth_stencil_format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_INVALID,
                    has_depth_stencil_target = false
                }
            };
            SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(
                _device.Handle, &info);
            if (pipeline == null) throw new InvalidOperationException(
                $"SDL sky pipeline creation failed: {SDL3.SDL_GetError()}");
            return pipeline;
        }

        private SDL_GPUShader* CreateShader(SDL_GPUShaderFormat format,
            string path, SDL_GPUShaderStage stage, uint samplers, uint uniforms)
        {
            byte[] code = File.ReadAllBytes(path);
            byte[] entrypoint = Encoding.UTF8.GetBytes(
                (stage == SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX
                    ? "main_vs" : "main_ps") + "\0");
            fixed (byte* codePointer = code)
            fixed (byte* entrypointPointer = entrypoint)
            {
                SDL_GPUShaderCreateInfo info = new()
                {
                    code_size = (UIntPtr)code.Length,
                    code = codePointer,
                    entrypoint = entrypointPointer,
                    format = format,
                    stage = stage,
                    num_samplers = samplers,
                    num_uniform_buffers = uniforms
                };
                SDL_GPUShader* shader = SDL3.SDL_CreateGPUShader(_device.Handle,
                    &info);
                if (shader == null) throw new InvalidOperationException(
                    $"SDL sky shader creation failed for {path}: {SDL3.SDL_GetError()}");
                return shader;
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
                "No generated sky shader format is supported.");
        }

        private void ReleaseResources()
        {
            if (_alphaPipeline != null)
                SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, _alphaPipeline);
            if (_opaquePipeline != null)
                SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, _opaquePipeline);
            if (_fragmentShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _fragmentShader);
            if (_vertexShader != null)
                SDL3.SDL_ReleaseGPUShader(_device.Handle, _vertexShader);
            _alphaPipeline = null;
            _opaquePipeline = null;
            _fragmentShader = null;
            _vertexShader = null;
            _resourceConfiguration = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseResources();
        }
    }
}
