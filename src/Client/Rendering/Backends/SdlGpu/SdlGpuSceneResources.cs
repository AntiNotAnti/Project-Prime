using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using MphRead.Mods;
using OpenTK.Mathematics;
using SDL;

namespace MphRead
{
    internal static class SdlGpuMappedMemoryCopy
    {
        public static void Copy(ReadOnlyMemory<byte> source, IntPtr destination)
        {
            if (source.IsEmpty)
            {
                return;
            }
            if (destination == IntPtr.Zero)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (MemoryMarshal.TryGetArray(source, out ArraySegment<byte> segment))
            {
                Marshal.Copy(segment.Array!, segment.Offset, destination, segment.Count);
                return;
            }
            byte[] copy = source.ToArray();
            Marshal.Copy(copy, 0, destination, copy.Length);
        }
    }

    internal static class SdlGpuMatrixAbi
    {
        // OpenTK's Matrix4 storage is row-vector oriented. The canonical HLSL
        // uses row_major matrices with mul(matrix, columnVector), while the
        // legacy GL path uploaded the same OpenTK values with transpose=false.
        // Transpose once here so all shader backends observe the legacy math.
        public static Matrix4 Upload(Matrix4 value) => Matrix4.Transpose(value);

        public static void CopyStack(IReadOnlyList<float> source, int matrixCount, Span<float> destination)
        {
            int count = Math.Min(matrixCount, destination.Length / 16);
            for (int matrix = 0; matrix < count; matrix++)
            {
                int offset = matrix * 16;
                for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    destination[offset + row * 4 + column] = source[offset + column * 4 + row];
            }
        }
    }

    internal static class SdlGpuAlphaPredicate
    {
        public static bool Opaque(float alpha) => alpha == 1f;
        public static bool Transparent(float alpha) => alpha < 1f;
    }

    internal static class SdlGpuCelSurface
    {
        public static int BandCount(RenderFrameOptions options)
            => options.CelShading ? options.CelBands : 0;

        public static bool TryGetFlatColor(RenderFrameOptions options,
            IReadOnlyDictionary<TextureIdentity, RenderTexturePixels> textures,
            RenderMaterial material, out Vector3 color)
        {
            if (options.CelShading && options.ShowTextures && material.Textured
                && material.Texture is TextureIdentity identity
                && textures.TryGetValue(identity, out RenderTexturePixels? pixels))
            {
                color = pixels.AlphaWeightedFlatColor;
                return true;
            }
            color = Vector3.One;
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct SdlGpuSceneVertexFrameConstants
    {
        internal const int VisualLightFloatCount = RenderFrame.MaximumVisualLights * 4;
        internal static int AbiByteSize => sizeof(SdlGpuSceneVertexFrameConstants);

        public Matrix4 View;
        public Matrix4 Projection;
        public Vector4 Options;
        public fixed float VisualLightPositionRadius[VisualLightFloatCount];
        public fixed float VisualLightColorIntensity[VisualLightFloatCount];
        public Vector4 VisualLightOptions;

        internal int VisualLightCount => (int)VisualLightOptions.X;

        internal Vector4 GetPositionRadius(int index)
        {
            if ((uint)index >= RenderFrame.MaximumVisualLights)
                throw new ArgumentOutOfRangeException(nameof(index));
            fixed (float* values = VisualLightPositionRadius)
            {
                int offset = index * 4;
                return new Vector4(values[offset], values[offset + 1],
                    values[offset + 2], values[offset + 3]);
            }
        }

        internal Vector4 GetColorIntensity(int index)
        {
            if ((uint)index >= RenderFrame.MaximumVisualLights)
                throw new ArgumentOutOfRangeException(nameof(index));
            fixed (float* values = VisualLightColorIntensity)
            {
                int offset = index * 4;
                return new Vector4(values[offset], values[offset + 1],
                    values[offset + 2], values[offset + 3]);
            }
        }

        public static SdlGpuSceneVertexFrameConstants Create(Matrix4 view,
            Matrix4 projection, Vector4 options,
            IReadOnlyList<RenderVisualLight>? visualLights, bool enabled)
        {
            SdlGpuSceneVertexFrameConstants result = default;
            result.View = SdlGpuMatrixAbi.Upload(view);
            result.Projection = SdlGpuMatrixAbi.Upload(projection);
            result.Options = options;
            int count = enabled && visualLights != null
                ? Math.Min(visualLights.Count, RenderFrame.MaximumVisualLights) : 0;
            float* positions = result.VisualLightPositionRadius;
            float* colors = result.VisualLightColorIntensity;
            for (int i = 0; i < count; i++)
            {
                RenderVisualLight light = visualLights![i];
                int offset = i * 4;
                positions[offset] = light.Position.X;
                positions[offset + 1] = light.Position.Y;
                positions[offset + 2] = light.Position.Z;
                positions[offset + 3] = light.Radius;
                colors[offset] = light.Color.X;
                colors[offset + 1] = light.Color.Y;
                colors[offset + 2] = light.Color.Z;
                colors[offset + 3] = light.Intensity;
            }
            result.VisualLightOptions = new Vector4(count, 0, 0, 0);
            return result;
        }
    }

    /// <summary>
    /// Device-owned translation of a sealed <see cref="RenderFrame"/>. Static
    /// model meshes, decoded textures, samplers, and immutable pipelines are
    /// cached for the device lifetime. Dynamic primitives use one bounded
    /// resource bucket per frame-in-flight slot and are recycled only after
    /// that slot's fence has completed.
    /// </summary>
    internal unsafe sealed class SdlGpuSceneResources : IDisposable
    {
        private const int MaximumDraws = RenderFrame.DefaultMaximumCapacity * 6;
        private readonly SDL_GPUTextureFormat _depthFormat;
        private readonly bool _depthSampleable;

        private readonly SdlGpuDevice _device;
        private readonly Dictionary<object, GpuMesh> _staticMeshes = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, long> _staticMeshLastUsed = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _expiredStaticMeshes = new();
        private readonly Dictionary<TextureIdentity, GpuTexture> _textures = new();
        private readonly Dictionary<TextureIdentity, long> _textureLastUsed = new();
        private readonly List<TextureIdentity> _expiredTextures = new();
        private readonly Dictionary<SamplerKey, nint> _samplers = new();
        private readonly Dictionary<SamplerKey, SamplerKey> _effectiveSamplerKeys = new();
        private readonly HashSet<(AnisotropyLevel Requested, AnisotropyLevel Effective)> _reportedSamplerNegotiations = new();
        private readonly Dictionary<ScenePipelineKey, nint> _pipelines = new();
        private readonly List<GpuMesh>[] _dynamicSlots;
        private readonly int[] _dynamicSlotUsed;
        private readonly List<GpuTexture>[] _retiredTextureSlots;
        private readonly List<GpuMesh>[] _retiredMeshSlots;
        private readonly List<IUploadResource>[] _uploadSlots;
        private SDL_GPUShader* _vertexShader;
        private SDL_GPUShader* _fragmentShader;
        private SDL_GPUTexture* _sceneColor;
        private SDL_GPUTexture* _sceneMultisampleColor;
        private SDL_GPUTexture* _sceneDepth;
        private SDL_GPUTexture* _bloomColor;
        private SDL_GPUTexture* _bloomMultisampleColor;
        private GpuTexture? _whiteTexture;
        private SdlGpuPostResources? _postResources;
        private uint _targetWidth;
        private uint _targetHeight;
        private int _targetSampleCount = 1;
        private uint _bloomTargetWidth;
        private uint _bloomTargetHeight;
        private int _bloomTargetSampleCount = 1;
        private readonly bool _colorSupports2;
        private readonly bool _colorSupports4;
        private readonly bool _depthSupports2;
        private readonly bool _depthSupports4;
        private readonly HashSet<SdlGpuSampleNegotiation> _reportedSampleNegotiations = new();
        private bool _disposed;
        private long _frameSerial;

        private SdlGpuSceneResources(SdlGpuDevice device)
        {
            _device = device;
            _dynamicSlots = new List<GpuMesh>[device.FrameResources.SlotCount];
            _dynamicSlotUsed = new int[device.FrameResources.SlotCount];
            _retiredTextureSlots = new List<GpuTexture>[device.FrameResources.SlotCount];
            _retiredMeshSlots = new List<GpuMesh>[device.FrameResources.SlotCount];
            _uploadSlots = new List<IUploadResource>[device.FrameResources.SlotCount];
            for (int i = 0; i < _dynamicSlots.Length; i++)
            {
                _dynamicSlots[i] = new List<GpuMesh>();
                _retiredTextureSlots[i] = new List<GpuTexture>();
                _retiredMeshSlots[i] = new List<GpuMesh>();
                _uploadSlots[i] = new List<IUploadResource>();
            }

            ShaderArtifactManifest.ValidateSceneFresh();
            SDL_GPUTextureUsageFlags sampledDepthUsage = SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET
                | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER;
            SDL_GPUTextureFormat d24 = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D24_UNORM_S8_UINT;
            SDL_GPUTextureFormat d32 = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_D32_FLOAT_S8_UINT;
            if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d24,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, sampledDepthUsage))
            {
                _depthFormat = d24;
                _depthSampleable = true;
            }
            else if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d32,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, sampledDepthUsage))
            {
                _depthFormat = d32;
                _depthSampleable = true;
            }
            else if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d24,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET))
            {
                _depthFormat = d24;
            }
            else if (SDL3.SDL_GPUTextureSupportsFormat(device.Handle, d32,
                SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET))
            {
                _depthFormat = d32;
            }
            else
            {
                throw new PlatformNotSupportedException("SDL GPU device supports neither D24S8 nor D32S8 scene targets.");
            }
            _colorSupports2 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                device.SwapchainFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2);
            _colorSupports4 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                device.SwapchainFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4);
            _depthSupports2 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                _depthFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2);
            _depthSupports4 = SDL3.SDL_GPUTextureSupportsSampleCount(device.Handle,
                _depthFormat, SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4);

            (SDL_GPUShaderFormat format, string vertex, string fragment) = SelectArtifacts(device);
            _vertexShader = CreateShader(device.Handle, format, vertex, "main_vs",
                SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_VERTEX, samplers: 0, uniforms: 2);
            try
            {
                _fragmentShader = CreateShader(device.Handle, format, fragment, "main_ps",
                    SDL_GPUShaderStage.SDL_GPU_SHADERSTAGE_FRAGMENT, samplers: 1, uniforms: 2);
                var whiteIdentity = new TextureIdentity(this, variant: "white-fallback");
                _whiteTexture = GpuTexture.Create(_device, new RenderTexturePixels(
                    whiteIdentity, 1, 1, new byte[] { 255, 255, 255, 255 }, onlyOpaque: true),
                    mipmapped: false);
                _postResources = SdlGpuPostResources.Create(device);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static SdlGpuSceneResources Create(SdlGpuDevice device)
            => new(device ?? throw new ArgumentNullException(nameof(device)));

        /// <summary>
        /// The scene color target is exposed only to the SDL backend's
        /// readback translator. It is the exact target after the six scene
        /// passes and before the post/composite chain; no native handle crosses
        /// the backend-neutral frame contracts.
        /// </summary>
        public SDL_GPUTexture* SceneColor => _sceneColor;
        private SDL_GPUTexture* SceneRenderColor
            => _sceneMultisampleColor != null ? _sceneMultisampleColor : _sceneColor;
        public uint SceneTargetWidth => _targetWidth;
        public uint SceneTargetHeight => _targetHeight;

        public void Encode(SDL_GPUCommandBuffer* commandBuffer, SDL_GPUTexture* finalComposite,
            RenderFrame frame, uint finalWidth, uint finalHeight)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (commandBuffer == null || finalComposite == null) throw new ArgumentNullException(nameof(commandBuffer));
            if (!frame.IsSealed) throw new InvalidOperationException("Scene encoding requires a sealed frame.");
            if (frame.Count > RenderFrame.DefaultMaximumCapacity)
                throw new InvalidOperationException("Scene draw count exceeds the bounded frame contract.");

            uint width = checked((uint)Math.Max(1, frame.SceneTargetSize.X));
            uint height = checked((uint)Math.Max(1, frame.SceneTargetSize.Y));
            bool celDepthSampling = frame.CelState.Enabled && frame.CelState.Outline > 0
                && _depthSampleable;
            SdlGpuSampleNegotiation sampleNegotiation = SdlGpuMsaaPolicy.Resolve(
                frame.Options.Quality.MsaaSampleCount, celDepthSampling,
                _colorSupports2, _colorSupports4, _depthSupports2, _depthSupports4);
            ReportSampleNegotiation(sampleNegotiation);
            SdlGpuSceneTargetPlan targetPlan = SdlGpuSceneTargetPlan.From(sampleNegotiation);
            EnsureTargets(width, height, targetPlan);
            SdlGpuBloomPlan bloomPlan = SdlGpuBloomPlan.Create(frame, width, height,
                targetPlan.RenderColorSamples);
            if (bloomPlan.Enabled) EnsureBloomTargets(width, height, bloomPlan);
            ResetDynamicSlot();
            _frameSerial++;

            // Uploads must be encoded before the first render pass. Resolve
            // every resource up front so no copy pass can be opened mid-pass.
            var meshes = new Dictionary<DrawSubmission, GpuMesh>(ReferenceEqualityComparer.Instance);
            var hudMeshes = new Dictionary<RenderHudSceneSubmission, GpuMesh>(ReferenceEqualityComparer.Instance);
            foreach (DrawSubmission draw in frame.Submissions)
            {
                if (!TryResolveMesh(frame, draw, commandBuffer, out GpuMesh? mesh))
                {
                    throw new InvalidOperationException(
                        $"Sealed scene frame is missing {draw.Primitive} geometry for submission polygon {draw.PolygonId}.");
                }
                meshes.Add(draw, mesh!);
                ResolveTexture(frame, draw, commandBuffer);
            }
            foreach (RenderHudSceneSubmission hud in frame.HudSceneItems)
            {
                CpuMesh mesh = hud.InlineMesh
                    ?? (hud.GeometryIdentity != null && frame.MeshResources.TryGetValue(hud.GeometryIdentity, out CpuMesh? captured)
                        ? captured : throw new InvalidOperationException($"Sealed scene frame is missing HUD geometry for polygon {hud.PolygonId}."));
                object identity = hud.InlineMesh != null ? hud : hud.GeometryIdentity!;
                hudMeshes.Add(hud, ResolveGpuMesh(identity, mesh, hud.InlineMesh == null, commandBuffer));
                ResolveTexture(frame, hud.Material.Textured, hud.TextureIdentity, hud.PolygonId, commandBuffer);
            }
            foreach (RenderOverlayCommand overlay in frame.OverlayCommands)
            {
                if (overlay.UseTexture) ResolveTexture(frame, true, overlay.Texture, -1, commandBuffer);
                if (overlay.UseMask) ResolveTexture(frame, true, overlay.MaskTexture, -1, commandBuffer);
            }
            RetireUnusedStaticMeshes();
            RetireUnusedTextures();
            if (_whiteTexture!.EnsureUploaded(commandBuffer)) TrackUpload(_whiteTexture);

            SdlGpuSceneVertexFrameConstants vertexFrame = SdlGpuSceneVertexFrameConstants.Create(
                frame.ViewMatrix, frame.ProjectionMatrix,
                new Vector4(frame.Options.Lighting ? 1 : 0,
                    frame.Options.ShowColors ? 1 : 0,
                    frame.Options.ShowTextures ? 1 : 0, 0),
                frame.VisualLights, frame.Options.Quality.DynamicVisualLights);
            float fogMin = frame.FogOffset / (float)0x7FFF;
            float fogMax = (frame.FogOffset + 32 * (0x400 >> frame.FogSlope)) / (float)0x7FFF;
            FrameFragmentConstants fragmentFrame = new()
            {
                FogColor = frame.FogColor,
                Options = new Vector4(frame.HasFog && frame.Options.Fog ? 1 : 0,
                    SdlGpuCelSurface.BandCount(frame.Options), 0, 0),
                FogRange = new Vector4(fogMin, fogMax, 0, 0)
            };

            int encoded = 0;
            EncodePass(commandBuffer, frame, meshes, frame.OpaqueItems, RenderPassKind.Opaque,
                clearColor: true, clearDepth: true, clearStencil: true, resolveColor: false,
                ref encoded, vertexFrame, fragmentFrame);
            EncodePass(commandBuffer, frame, meshes, frame.DecalItems, RenderPassKind.Decal,
                false, false, false, false, ref encoded, vertexFrame, fragmentFrame);
            EncodePass(commandBuffer, frame, meshes, frame.TransparentItems, RenderPassKind.TransparentStencil,
                false, false, false, false, ref encoded, vertexFrame, fragmentFrame);
            EncodePass(commandBuffer, frame, meshes, frame.OpaqueItems, RenderPassKind.DepthRebuild,
                false, true, false, false, ref encoded, vertexFrame, fragmentFrame);
            EncodePass(commandBuffer, frame, meshes, frame.TransparentItems, RenderPassKind.TransparentBehind,
                false, false, false, false, ref encoded, vertexFrame, fragmentFrame);
            EncodePass(commandBuffer, frame, meshes, frame.TransparentItems, RenderPassKind.TransparentFront,
                false, false, false, resolveColor: frame.HudSceneItems.Count == 0,
                ref encoded, vertexFrame, fragmentFrame);
            if (bloomPlan.Enabled)
                EncodeBloom(commandBuffer, frame, meshes, bloomPlan, vertexFrame, fragmentFrame);
            EncodeHudScene(commandBuffer, frame, hudMeshes);
            _postResources!.Encode(commandBuffer, frame, _sceneColor, _sceneDepth,
                bloomPlan.Enabled ? _bloomColor : null, bloomPlan,
                finalComposite, finalWidth, finalHeight, this);
        }

        private void EncodePass(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            Dictionary<DrawSubmission, GpuMesh> meshes, IReadOnlyList<DrawSubmission> draws,
            RenderPassKind passKind, bool clearColor, bool clearDepth, bool clearStencil,
            bool resolveColor, ref int encoded, SdlGpuSceneVertexFrameConstants vertexFrame,
            FrameFragmentConstants fragmentFrame)
        {
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = SceneRenderColor,
                clear_color = new SDL_FColor { r = frame.ClearColor.X, g = frame.ClearColor.Y,
                    b = frame.ClearColor.Z, a = frame.ClearColor.W },
                load_op = clearColor ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = resolveColor && _targetSampleCount > 1
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = resolveColor && _targetSampleCount > 1 ? _sceneColor : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depthTarget = new()
            {
                texture = _sceneDepth,
                clear_depth = 1,
                clear_stencil = 0,
                load_op = clearDepth ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = clearStencil ? SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR : SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer, &colorTarget, 1, &depthTarget);
            if (pass == null) throw new InvalidOperationException($"SDL scene pass {passKind} failed: {SDL3.SDL_GetError()}");
            try
            {
                SDL_GPUViewport viewport = new() { x = 0, y = 0, w = _targetWidth, h = _targetHeight, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                PushVertex(commandBuffer, 0, vertexFrame);
                PushFragment(commandBuffer, 0, fragmentFrame);
                foreach (DrawSubmission draw in draws)
                {
                    if (++encoded > MaximumDraws) throw new InvalidOperationException("Encoded scene draw bound exceeded.");
                    if (!meshes.TryGetValue(draw, out GpuMesh? mesh)) continue;
                    Draw(commandBuffer, pass, frame, draw, mesh, passKind, RenderTopology.Triangles);
                    if (draw.Primitive == RenderPrimitive.Ngon && mesh.LineIndexCount > 0
                        && frame.Options.VolumeEdges != 2 && !draw.NoLines)
                    {
                        Draw(commandBuffer, pass, frame, draw, mesh, passKind, RenderTopology.Lines);
                    }
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void EncodeHudScene(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            IReadOnlyDictionary<RenderHudSceneSubmission, GpuMesh> meshes)
        {
            if (frame.HudSceneItems.Count == 0) return;
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = SceneRenderColor,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = _targetSampleCount > 1
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = _targetSampleCount > 1 ? _sceneColor : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depthTarget = new()
            {
                texture = _sceneDepth,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer, &colorTarget, 1, &depthTarget);
            if (pass == null) throw new InvalidOperationException($"SDL HUD scene pass failed: {SDL3.SDL_GetError()}");
            try
            {
                SDL_GPUViewport viewport = new() { w = _targetWidth, h = _targetHeight, min_depth = 0, max_depth = 1 };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                FrameFragmentConstants fragmentFrame = new()
                {
                    FogColor = Vector4.Zero,
                    Options = Vector4.Zero,
                    FogRange = Vector4.Zero
                };
                PushFragment(commandBuffer, 0, fragmentFrame);
                foreach (RenderHudSceneSubmission hud in frame.HudSceneItems)
                {
                    if (!meshes.TryGetValue(hud, out GpuMesh? mesh) || mesh.TriangleIndexCount == 0) continue;
                    RenderMaterial material = hud.Material;
                    PipelineKey baseKey = CreateHudPipelineKey(material,
                        _device.SwapchainFormat.ToString(), _targetSampleCount);
                    SDL3.SDL_BindGPUGraphicsPipeline(pass,
                        GetPipeline(new ScenePipelineKey(baseKey,
                            frame.Options.Wireframe || material.Wireframe, frame.Options.FaceCulling)));

                    SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer };
                    SDL_GPUBufferBinding index = new() { buffer = mesh.TriangleIndexBuffer };
                    SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
                    SDL3.SDL_BindGPUIndexBuffer(pass, &index, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
                    SDL_GPUTexture* texture = material.Textured
                        ? (SDL_GPUTexture*)ResolveTextureHandle(frame, hud.TextureIdentity,
                            $"HUD scene polygon {hud.PolygonId}")
                        : _whiteTexture!.Handle;
                    SDL_GPUSampler* sampler = GetSampler(new SamplerKey(
                        RenderFilterMode.Nearest, RepeatMode.Clamp, RepeatMode.Clamp));
                    SDL_GPUTextureSamplerBinding binding = new() { texture = texture, sampler = sampler };
                    SDL3.SDL_BindGPUFragmentSamplers(pass, 0, &binding, 1);

                    SdlGpuSceneVertexFrameConstants vertexFrame
                        = SdlGpuSceneVertexFrameConstants.Create(
                            hud.ViewMatrix, hud.ProjectionMatrix,
                            new Vector4(frame.Options.Lighting ? 1 : 0,
                                frame.Options.ShowColors ? 1 : 0,
                                frame.Options.ShowTextures ? 1 : 0, 0),
                            visualLights: null, enabled: false);
                    PushVertex(commandBuffer, 0, vertexFrame);
                    DrawConstants constants = BuildHudDrawConstants(frame, hud);
                    PushVertex(commandBuffer, 1, constants);
                    PushFragment(commandBuffer, 1, constants);
                    SDL3.SDL_DrawGPUIndexedPrimitives(pass, checked((uint)mesh.TriangleIndexCount), 1, 0, 0, 0);
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void EncodeBloom(SDL_GPUCommandBuffer* commandBuffer, RenderFrame frame,
            IReadOnlyDictionary<DrawSubmission, GpuMesh> meshes, SdlGpuBloomPlan plan,
            SdlGpuSceneVertexFrameConstants vertexFrame,
            FrameFragmentConstants fragmentFrame)
        {
            if (!plan.Enabled || _bloomColor == null)
                throw new InvalidOperationException("SDL bloom pass requires allocated targets.");
            SDL_GPUTexture* renderColor = _bloomMultisampleColor != null
                ? _bloomMultisampleColor : _bloomColor;
            SDL_GPUColorTargetInfo colorTarget = new()
            {
                texture = renderColor,
                clear_color = new SDL_FColor { r = 0, g = 0, b = 0, a = 0 },
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_CLEAR,
                store_op = plan.UsesResolve
                    ? SDL_GPUStoreOp.SDL_GPU_STOREOP_RESOLVE
                    : SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                resolve_texture = plan.UsesResolve ? _bloomColor : null,
                cycle = false
            };
            SDL_GPUDepthStencilTargetInfo depthTarget = new()
            {
                texture = _sceneDepth,
                load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                stencil_load_op = SDL_GPULoadOp.SDL_GPU_LOADOP_LOAD,
                stencil_store_op = SDL_GPUStoreOp.SDL_GPU_STOREOP_STORE,
                cycle = false
            };
            SDL_GPURenderPass* pass = SDL3.SDL_BeginGPURenderPass(commandBuffer,
                &colorTarget, 1, &depthTarget);
            if (pass == null)
                throw new InvalidOperationException($"SDL bloom emission pass failed: {SDL3.SDL_GetError()}");
            try
            {
                SDL_GPUViewport viewport = new()
                {
                    w = _targetWidth, h = _targetHeight, min_depth = 0, max_depth = 1
                };
                SDL3.SDL_SetGPUViewport(pass, &viewport);
                PushVertex(commandBuffer, 0, vertexFrame);
                PushFragment(commandBuffer, 0, fragmentFrame);
                int emitted = 0;
                foreach (DrawSubmission draw in frame.Submissions)
                {
                    if (!SdlGpuBloomPlan.IsEligible(draw.Material)) continue;
                    if (++emitted > RenderFrame.DefaultMaximumCapacity)
                        throw new InvalidOperationException("Bloom draw count exceeds the bounded frame contract.");
                    if (!meshes.TryGetValue(draw, out GpuMesh? mesh))
                        throw new InvalidOperationException(
                            $"Bloom mesh was not resolved for polygon {draw.PolygonId}.");
                    DrawBloom(commandBuffer, pass, frame, draw, mesh);
                }
            }
            finally
            {
                SDL3.SDL_EndGPURenderPass(pass);
            }
        }

        private void DrawBloom(SDL_GPUCommandBuffer* commandBuffer, SDL_GPURenderPass* pass,
            RenderFrame frame, DrawSubmission draw, GpuMesh mesh)
        {
            if (mesh.TriangleIndexCount == 0
                || (draw.Primitive == RenderPrimitive.Ngon && frame.Options.VolumeEdges == 1)) return;
            PipelineKey baseKey = new(RenderShaderVariant.Scene, RenderBlendMode.Opaque,
                draw.Material.CullingMode switch
                {
                    CullingMode.Front => RenderCullMode.Front,
                    CullingMode.Back => RenderCullMode.Back,
                    _ => RenderCullMode.None
                }, RenderDepthMode.LessOrEqual, depthWrite: false,
                RenderStencilMode.Disabled, RenderTopology.Triangles,
                _targetSampleCount, RenderAlphaTestMode.Disabled,
                RenderColorWriteMask.All, decalDepthBias: false,
                _device.SwapchainFormat.ToString());
            SDL3.SDL_BindGPUGraphicsPipeline(pass, GetPipeline(new ScenePipelineKey(
                baseKey, Wireframe: false, FaceCulling: frame.Options.FaceCulling,
                BloomEmission: true)));

            SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer };
            SDL_GPUBufferBinding index = new() { buffer = mesh.TriangleIndexBuffer };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
            SDL3.SDL_BindGPUIndexBuffer(pass, &index,
                SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);
            GpuTexture texture = ResolveBoundTexture(frame, draw);
            SDL_GPUSampler* sampler = GetSampler(SamplerKey.From(draw.Material,
                frame.Options.Quality));
            SDL_GPUTextureSamplerBinding binding = new()
            {
                texture = texture.Handle, sampler = sampler
            };
            SDL3.SDL_BindGPUFragmentSamplers(pass, 0, &binding, 1);

            DrawConstants constants = BuildDrawConstants(frame, draw, draw.Pass,
                RenderTopology.Triangles, SdlGpuBloomPlan.Strength(draw.Material));
            PushVertex(commandBuffer, 1, constants);
            PushFragment(commandBuffer, 1, constants);
            SDL3.SDL_DrawGPUIndexedPrimitives(pass,
                checked((uint)mesh.TriangleIndexCount), 1, 0, 0, 0);
        }

        internal static PipelineKey CreateHudPipelineKey(RenderMaterial material, string targetFormat,
            int sampleCount = 1)
            => new(RenderShaderVariant.Scene, RenderBlendMode.Alpha,
                material.CullingMode switch
                {
                    CullingMode.Front => RenderCullMode.Front,
                    CullingMode.Back => RenderCullMode.Back,
                    _ => RenderCullMode.None
                }, RenderDepthMode.Disabled, depthWrite: false,
                RenderStencilMode.Disabled, RenderTopology.Triangles, sampleCount,
                RenderAlphaTestMode.Disabled, RenderColorWriteMask.All,
                decalDepthBias: false, targetFormat);

        private static DrawConstants BuildHudDrawConstants(RenderFrame frame, RenderHudSceneSubmission hud)
        {
            RenderMaterial material = hud.Material;
            DrawConstants constants = default;
            constants.Transform = SdlGpuMatrixAbi.Upload(hud.Transform);
            constants.Billboard = SdlGpuMatrixAbi.Upload(Matrix4.Identity);
            constants.TextureMatrix = SdlGpuMatrixAbi.Upload(material.TextureMatrix);
            constants.Diffuse = hud.CurrentColor;
            constants.Ambient = new Vector4(material.Ambient, 1);
            constants.Specular = new Vector4(material.Specular, 1);
            constants.Emission = new Vector4(material.Emission, 1);
            constants.OverrideColor = material.ColorOverride ?? Vector4.One;
            constants.PaletteOverride = material.PaletteOverride ?? Vector4.One;
            constants.Light1Vector = new Vector4(hud.LightInfo.Light1Vector, 0);
            constants.Light1Color = new Vector4(hud.LightInfo.Light1Color, 1);
            constants.Light2Vector = new Vector4(hud.LightInfo.Light2Vector, 0);
            constants.Light2Color = new Vector4(hud.LightInfo.Light2Color, 1);
            constants.DrawOptions = new Vector4(material.Textured && frame.Options.ShowTextures ? 1 : 0,
                material.ColorOverride.HasValue ? 1 : 0, material.PaletteOverride.HasValue ? 1 : 0,
                hud.MatrixStackCount > 0 ? 1 : 0);
            constants.MaterialOptions = new Vector4(hud.Alpha, (float)material.PolygonMode,
                (float)material.TexgenMode, material.Lighting ? 1 : 0);
            constants.RenderOptions = Vector4.Zero;
            constants.FlatColor = Vector4.One;
            float* destination = constants.MatrixStack;
            SdlGpuMatrixAbi.CopyStack(hud.MatrixStack, hud.MatrixStackCount,
                new Span<float>(destination, RenderFrame.MatrixStackFloats));
            return constants;
        }

        private void Draw(SDL_GPUCommandBuffer* commandBuffer, SDL_GPURenderPass* pass,
            RenderFrame frame, DrawSubmission draw, GpuMesh mesh, RenderPassKind passKind,
            RenderTopology topology)
        {
            int indexCount = topology == RenderTopology.Lines ? mesh.LineIndexCount : mesh.TriangleIndexCount;
            if (indexCount == 0 || (draw.Primitive == RenderPrimitive.Ngon && topology == RenderTopology.Triangles
                && frame.Options.VolumeEdges == 1)) return;

            bool wireframe = topology == RenderTopology.Triangles && (frame.Options.Wireframe || draw.Material.Wireframe);
            PipelineKey baseKey = PipelineKey.From(draw.Material, draw.Primitive, passKind,
                _targetSampleCount,
                _device.SwapchainFormat.ToString()).WithTopology(topology);
            ScenePipelineKey key = new(baseKey, wireframe, frame.Options.FaceCulling);
            SDL_GPUGraphicsPipeline* pipeline = GetPipeline(key);
            SDL3.SDL_BindGPUGraphicsPipeline(pass, pipeline);
            SDL3.SDL_SetGPUStencilReference(pass, checked((byte)Math.Clamp(draw.PolygonId, 0, 255)));

            SDL_GPUBufferBinding vertex = new() { buffer = mesh.VertexBuffer, offset = 0 };
            SDL_GPUBufferBinding index = new()
            {
                buffer = topology == RenderTopology.Lines ? mesh.LineIndexBuffer : mesh.TriangleIndexBuffer,
                offset = 0
            };
            SDL3.SDL_BindGPUVertexBuffers(pass, 0, &vertex, 1);
            SDL3.SDL_BindGPUIndexBuffer(pass, &index, SDL_GPUIndexElementSize.SDL_GPU_INDEXELEMENTSIZE_32BIT);

            GpuTexture texture = ResolveBoundTexture(frame, draw);
            SDL_GPUSampler* sampler = GetSampler(SamplerKey.From(draw.Material,
                frame.Options.Quality));
            SDL_GPUTextureSamplerBinding textureBinding = new() { texture = texture.Handle, sampler = sampler };
            SDL3.SDL_BindGPUFragmentSamplers(pass, 0, &textureBinding, 1);

            DrawConstants constants = BuildDrawConstants(frame, draw, passKind, topology);
            PushVertex(commandBuffer, 1, constants);
            PushFragment(commandBuffer, 1, constants);
            SDL3.SDL_DrawGPUIndexedPrimitives(pass, checked((uint)indexCount), 1, 0, 0, 0);
        }

        private DrawConstants BuildDrawConstants(RenderFrame frame, DrawSubmission draw,
            RenderPassKind passKind, RenderTopology topology, float bloomStrength = 0)
        {
            DrawConstants constants = default;
            constants.Transform = SdlGpuMatrixAbi.Upload(draw.Transform);
            constants.Billboard = draw.Material.BillboardMode switch
            {
                BillboardMode.Sphere => SdlGpuMatrixAbi.Upload(frame.ViewInverseRotation),
                BillboardMode.Cylinder => SdlGpuMatrixAbi.Upload(frame.ViewInverseRotationY),
                _ => SdlGpuMatrixAbi.Upload(Matrix4.Identity)
            };
            constants.TextureMatrix = SdlGpuMatrixAbi.Upload(draw.Material.TextureMatrix);
            constants.Diffuse = new Vector4(draw.Material.Diffuse, 1);
            constants.Ambient = new Vector4(draw.Material.Ambient, 1);
            constants.Specular = new Vector4(draw.Material.Specular, 1);
            constants.Emission = new Vector4(draw.Material.Emission, 1);
            constants.OverrideColor = draw.Material.ColorOverride ?? Vector4.One;
            constants.PaletteOverride = draw.Material.PaletteOverride ?? Vector4.One;
            constants.Light1Vector = new Vector4(draw.LightInfo.Light1Vector, 0);
            constants.Light1Color = new Vector4(draw.LightInfo.Light1Color, 1);
            constants.Light2Vector = new Vector4(draw.LightInfo.Light2Vector, 0);
            constants.Light2Color = new Vector4(draw.LightInfo.Light2Color, 1);
            constants.DrawOptions = new Vector4(draw.Material.Textured && frame.Options.ShowTextures ? 1 : 0,
                draw.Material.ColorOverride.HasValue ? 1 : 0,
                draw.Material.PaletteOverride.HasValue ? 1 : 0,
                draw.MatrixStackCount > 0 ? 1 : 0);
            if (draw.Primitive == RenderPrimitive.Ngon && topology == RenderTopology.Lines)
            {
                constants.OverrideColor = draw.FrozenEdgeColor;
                constants.DrawOptions.Y = 1;
            }
            constants.MaterialOptions = new Vector4(draw.Material.Alpha, (float)draw.Material.PolygonMode,
                (float)draw.Material.TexgenMode, draw.Material.Lighting ? 1 : 0);
            constants.RenderOptions = new Vector4(passKind switch
            {
                RenderPassKind.Opaque or RenderPassKind.DepthRebuild => 1,
                RenderPassKind.TransparentStencil or RenderPassKind.TransparentBehind or RenderPassKind.TransparentFront => 2,
                _ => 0
            }, 0, bloomStrength, 0);
            if (SdlGpuCelSurface.TryGetFlatColor(frame.Options, frame.TextureResources,
                draw.Material, out Vector3 flatColor))
            {
                constants.RenderOptions.Y = 1;
                constants.FlatColor = new Vector4(flatColor, 1);
            }
            float* destination = constants.MatrixStack;
            SdlGpuMatrixAbi.CopyStack(draw.MatrixStack, draw.MatrixStackCount,
                new Span<float>(destination, RenderFrame.MatrixStackFloats));
            return constants;
        }

        private bool TryResolveMesh(RenderFrame frame, DrawSubmission draw, SDL_GPUCommandBuffer* commandBuffer,
            out GpuMesh? result)
        {
            result = null;
            object? identity = draw.Primitive == RenderPrimitive.Mesh ? draw.GeometryIdentity : draw;
            if (identity == null || !frame.MeshResources.TryGetValue(identity, out CpuMesh? mesh)) return false;
            result = ResolveGpuMesh(identity, mesh, draw.Primitive == RenderPrimitive.Mesh, commandBuffer);
            return true;
        }

        private GpuMesh ResolveGpuMesh(object identity, CpuMesh mesh, bool isStatic,
            SDL_GPUCommandBuffer* commandBuffer)
        {
            GpuMesh result;
            if (isStatic)
            {
                if (!_staticMeshes.TryGetValue(identity, out result!))
                {
                    result = GpuMesh.Create(_device, mesh);
                    _staticMeshes.Add(identity, result);
                }
                _staticMeshLastUsed[identity] = _frameSerial;
            }
            else
            {
                int slotIndex = _device.FrameResources.CurrentSlotIndex;
                List<GpuMesh> slot = _dynamicSlots[slotIndex];
                int resourceIndex = _dynamicSlotUsed[slotIndex]++;
                if (resourceIndex == slot.Count)
                {
                    result = GpuMesh.Create(_device, mesh);
                    slot.Add(result);
                }
                else if (slot[resourceIndex].CanHold(mesh))
                {
                    result = slot[resourceIndex];
                    result.Reset(mesh);
                }
                else
                {
                    GpuMesh replacement = GpuMesh.Create(_device, mesh);
                    slot[resourceIndex].Dispose();
                    slot[resourceIndex] = replacement;
                    result = replacement;
                }
            }
            if (result.EnsureUploaded(commandBuffer) && isStatic) TrackUpload(result);
            return result;
        }

        private void ResolveTexture(RenderFrame frame, DrawSubmission draw, SDL_GPUCommandBuffer* commandBuffer)
            => ResolveTexture(frame, draw.Material.Textured, draw.Material.Texture, draw.PolygonId, commandBuffer);

        private void ResolveTexture(RenderFrame frame, bool textured, TextureIdentity? requested,
            int polygonId, SDL_GPUCommandBuffer* commandBuffer)
        {
            if (!textured) return;
            if (requested is not TextureIdentity identity)
                throw new InvalidOperationException($"Textured scene submission polygon {polygonId} has no texture identity.");
            if (!frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels))
                throw new InvalidOperationException($"Sealed scene frame is missing texture pixels for {identity}.");
            if (!_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
            {
                GpuTexture replacement = GpuTexture.Create(_device, pixels,
                    frame.Options.Quality.UsesMipmaps);
                if (texture != null)
                {
                    // Keep the replaced generation alive through the fence
                    // of the command buffer that establishes the replacement.
                    // Queue ordering then proves every earlier reader is done.
                    _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex].Add(texture);
                }
                texture = replacement;
                _textures[identity] = texture;
            }
            if (texture.EnsureUploaded(commandBuffer)) TrackUpload(texture);
            _textureLastUsed[identity] = _frameSerial;
        }

        private GpuTexture ResolveBoundTexture(RenderFrame frame, DrawSubmission draw)
        {
            if (draw.Material.Textured && draw.Material.Texture is TextureIdentity identity
                && frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels)
                && _textures.TryGetValue(identity, out GpuTexture? texture)
                && texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
            {
                return texture;
            }
            if (draw.Material.Textured)
                throw new InvalidOperationException($"Scene texture upload was not resolved for polygon {draw.PolygonId}.");
            return _whiteTexture!;
        }

        internal nint ResolveTextureHandle(RenderFrame frame, TextureIdentity? requested, string label)
        {
            if (requested is not TextureIdentity identity)
                throw new InvalidOperationException($"Textured {label} has no texture identity.");
            if (!frame.TextureResources.TryGetValue(identity, out RenderTexturePixels? pixels))
                throw new InvalidOperationException($"Sealed scene frame is missing texture pixels for {label} ({identity}).");
            if (!_textures.TryGetValue(identity, out GpuTexture? texture)
                || !texture.Matches(pixels, frame.Options.Quality.UsesMipmaps))
                throw new InvalidOperationException($"Texture upload was not resolved for {label} ({identity}).");
            return (nint)texture.Handle;
        }

        internal nint WhiteTextureHandle => (nint)_whiteTexture!.Handle;
        internal bool DepthSampleable => _depthSampleable;
        internal nint NearestClampSamplerHandle => (nint)GetSampler(new SamplerKey(
            RenderFilterMode.Nearest, RepeatMode.Clamp, RepeatMode.Clamp));
        internal nint LinearClampSamplerHandle => (nint)GetSampler(new SamplerKey(
            RenderFilterMode.Linear, RepeatMode.Clamp, RepeatMode.Clamp));

        private void ResetDynamicSlot()
        {
            int slotIndex = _device.FrameResources.CurrentSlotIndex;
            foreach (IUploadResource resource in _uploadSlots[_device.FrameResources.CurrentSlotIndex])
                resource.ReleaseUploadStorage();
            _uploadSlots[_device.FrameResources.CurrentSlotIndex].Clear();
            _dynamicSlotUsed[slotIndex] = 0;
            foreach (GpuTexture texture in _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex]) texture.Dispose();
            _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex].Clear();
            foreach (GpuMesh mesh in _retiredMeshSlots[slotIndex]) mesh.Dispose();
            _retiredMeshSlots[slotIndex].Clear();
        }

        private void RetireUnusedStaticMeshes()
        {
            // A hidden/unloaded model gets a short grace period so ordinary
            // visibility changes do not churn uploads. Expired handles are
            // released only when the current frame-slot fence completes.
            const int graceFrames = 60;
            if (_staticMeshes.Count == 0) return;
            _expiredStaticMeshes.Clear();
            foreach (KeyValuePair<object, long> entry in _staticMeshLastUsed)
                if (_frameSerial - entry.Value > graceFrames) _expiredStaticMeshes.Add(entry.Key);
            foreach (object identity in _expiredStaticMeshes)
            {
                _retiredMeshSlots[_device.FrameResources.CurrentSlotIndex].Add(_staticMeshes[identity]);
                _staticMeshes.Remove(identity);
                _staticMeshLastUsed.Remove(identity);
            }
        }

        private void RetireUnusedTextures()
        {
            const int graceFrames = 60;
            _expiredTextures.Clear();
            foreach (KeyValuePair<TextureIdentity, long> entry in _textureLastUsed)
            {
                // Value-type variants are immutable per-revision snapshots
                // used by rewritten HUD surfaces. Keep all revisions needed
                // by this frame distinct, then retire them through a fenced
                // frame slot as soon as a later frame stops referencing them.
                int identityGrace = entry.Key.Variant?.GetType().IsValueType == true ? 0 : graceFrames;
                if (_frameSerial - entry.Value > identityGrace) _expiredTextures.Add(entry.Key);
            }
            foreach (TextureIdentity identity in _expiredTextures)
            {
                _retiredTextureSlots[_device.FrameResources.CurrentSlotIndex].Add(_textures[identity]);
                _textures.Remove(identity);
                _textureLastUsed.Remove(identity);
            }
        }

        private void TrackUpload(IUploadResource resource)
            => _uploadSlots[_device.FrameResources.CurrentSlotIndex].Add(resource);

        public void InvalidatePendingUploads()
        {
            int slotIndex = _device.FrameResources.CurrentSlotIndex;
            foreach (IUploadResource resource in _uploadSlots[slotIndex])
                resource.InvalidateUpload();
            for (int i = 0; i < _dynamicSlotUsed[slotIndex]; i++)
                _dynamicSlots[slotIndex][i].InvalidateUpload();
            _postResources?.InvalidatePendingUploads();
        }

        private SDL_GPUSampler* GetSampler(SamplerKey key)
        {
            if (_effectiveSamplerKeys.TryGetValue(key, out SamplerKey effective)
                && _samplers.TryGetValue(effective, out nint mapped))
            {
                return (SDL_GPUSampler*)mapped;
            }

            if (key.Filter != RenderFilterMode.Anisotropic
                || key.Anisotropy == AnisotropyLevel.Off)
            {
                SDL_GPUSampler* direct = GetOrCreateSampler(key);
                if (direct == null)
                    throw new InvalidOperationException($"SDL scene sampler creation failed: {SDL3.SDL_GetError()}");
                _effectiveSamplerKeys[key] = key;
                return direct;
            }

            string lastError = string.Empty;
            for (int index = 0; SdlGpuSamplerPolicy.TryGetAnisotropyAttempt(
                key.Anisotropy, index, out AnisotropyLevel attempt); index++)
            {
                effective = key.WithAnisotropy(attempt);
                SDL_GPUSampler* sampler = GetOrCreateSampler(effective);
                if (sampler != null)
                {
                    _effectiveSamplerKeys[key] = effective;
                    ReportSamplerNegotiation(key.Anisotropy, attempt);
                    return sampler;
                }
                lastError = SDL3.SDL_GetError() ?? string.Empty;
            }
            throw new InvalidOperationException(
                $"SDL enhanced sampler creation failed after anisotropy fallback: {lastError}");
        }

        private SDL_GPUSampler* GetOrCreateSampler(SamplerKey key)
        {
            if (_samplers.TryGetValue(key, out nint cached)) return (SDL_GPUSampler*)cached;
            SdlGpuSamplerDescription description = SdlGpuSamplerPolicy.Describe(key);
            SDL_GPUFilter minFilter = description.MinFilter == SdlGpuSamplerFilter.Nearest
                ? SDL_GPUFilter.SDL_GPU_FILTER_NEAREST : SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
            SDL_GPUFilter magFilter = description.MagFilter == SdlGpuSamplerFilter.Nearest
                ? SDL_GPUFilter.SDL_GPU_FILTER_NEAREST : SDL_GPUFilter.SDL_GPU_FILTER_LINEAR;
            SDL_GPUSamplerCreateInfo info = new()
            {
                min_filter = minFilter,
                mag_filter = magFilter,
                mipmap_mode = description.MipFilter == SdlGpuSamplerMipFilter.Linear
                    ? SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_LINEAR
                    : SDL_GPUSamplerMipmapMode.SDL_GPU_SAMPLERMIPMAPMODE_NEAREST,
                address_mode_u = Address(key.WrapX),
                address_mode_v = Address(key.WrapY),
                address_mode_w = SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE,
                min_lod = description.MinLod,
                max_lod = description.MaxLod,
                enable_anisotropy = description.EnableAnisotropy,
                max_anisotropy = description.MaxAnisotropy
            };
            SDL_GPUSampler* sampler = SDL3.SDL_CreateGPUSampler(_device.Handle, &info);
            if (sampler != null) _samplers.Add(key, (nint)sampler);
            return sampler;
        }

        private void ReportSamplerNegotiation(AnisotropyLevel requested,
            AnisotropyLevel effective)
        {
            if (_reportedSamplerNegotiations.Add((requested, effective)))
            {
                Console.WriteLine($"[render] SDL GPU anisotropy requested={(int)requested}x "
                    + $"effective={(effective == AnisotropyLevel.Off ? "off" : $"{(int)effective}x")}");
            }
        }

        private SDL_GPUGraphicsPipeline* GetPipeline(ScenePipelineKey key)
        {
            if (key.Key.SampleCount != _targetSampleCount)
            {
                throw new InvalidOperationException(
                    $"SDL scene pipeline sample count {key.Key.SampleCount} does not match target {_targetSampleCount}.");
            }
            if (_pipelines.TryGetValue(key, out nint cached)) return (SDL_GPUGraphicsPipeline*)cached;
            SDL_GPUGraphicsPipeline* pipeline = CreatePipeline(key);
            _pipelines.Add(key, (nint)pipeline);
            return pipeline;
        }

        private SDL_GPUGraphicsPipeline* CreatePipeline(ScenePipelineKey sceneKey)
        {
            PipelineKey key = sceneKey.Key;
            SDL_GPUVertexBufferDescription vertexDescription = new()
            {
                slot = 0, pitch = (uint)sizeof(GpuVertex),
                input_rate = SDL_GPUVertexInputRate.SDL_GPU_VERTEXINPUTRATE_VERTEX
            };
            SDL_GPUVertexAttribute* attributes = stackalloc SDL_GPUVertexAttribute[6];
            attributes[0] = Attribute(0, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 0);
            attributes[1] = Attribute(1, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT4, 12);
            attributes[2] = Attribute(2, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT3, 28);
            attributes[3] = Attribute(3, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2, 40);
            attributes[4] = Attribute(4, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 48);
            attributes[5] = Attribute(5, SDL_GPUVertexElementFormat.SDL_GPU_VERTEXELEMENTFORMAT_UINT, 52);
            SDL_GPUVertexInputState vertexInput = new()
            {
                vertex_buffer_descriptions = &vertexDescription, num_vertex_buffers = 1,
                vertex_attributes = attributes, num_vertex_attributes = 6
            };
            bool blend = sceneKey.BloomEmission || key.BlendMode == RenderBlendMode.Alpha;
            bool additive = sceneKey.BloomEmission;
            RenderColorWriteMask writes = key.StencilMode is RenderStencilMode.MarkTransparent
                or RenderStencilMode.Preserve ? RenderColorWriteMask.None : key.ColorWriteMask;
            SDL_GPUColorTargetDescription colorTarget = new()
            {
                format = _device.SwapchainFormat,
                blend_state = new SDL_GPUColorTargetBlendState
                {
                    src_color_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_color_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    color_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    src_alpha_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE,
                    dst_alpha_blendfactor = additive ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE
                        : blend ? SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA
                        : SDL_GPUBlendFactor.SDL_GPU_BLENDFACTOR_ZERO,
                    alpha_blend_op = SDL_GPUBlendOp.SDL_GPU_BLENDOP_ADD,
                    color_write_mask = ColorMask(writes),
                    enable_blend = blend,
                    enable_color_write_mask = true
                }
            };
            SDL_GPUDepthStencilState depthStencil = CreateDepthStencil(key);
            SDL_GPUGraphicsPipelineCreateInfo info = new()
            {
                vertex_shader = _vertexShader,
                fragment_shader = _fragmentShader,
                vertex_input_state = vertexInput,
                primitive_type = key.Topology == RenderTopology.Lines
                    ? SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_LINELIST
                    : SDL_GPUPrimitiveType.SDL_GPU_PRIMITIVETYPE_TRIANGLELIST,
                rasterizer_state = new SDL_GPURasterizerState
                {
                    fill_mode = sceneKey.Wireframe ? SDL_GPUFillMode.SDL_GPU_FILLMODE_LINE : SDL_GPUFillMode.SDL_GPU_FILLMODE_FILL,
                    cull_mode = !sceneKey.FaceCulling ? SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE : key.CullMode switch
                    {
                        RenderCullMode.Front => SDL_GPUCullMode.SDL_GPU_CULLMODE_FRONT,
                        RenderCullMode.Back => SDL_GPUCullMode.SDL_GPU_CULLMODE_BACK,
                        _ => SDL_GPUCullMode.SDL_GPU_CULLMODE_NONE
                    },
                    front_face = SDL_GPUFrontFace.SDL_GPU_FRONTFACE_COUNTER_CLOCKWISE,
                    depth_bias_constant_factor = key.DecalDepthBias ? -1 : 0,
                    depth_bias_slope_factor = key.DecalDepthBias ? -1 : 0,
                    enable_depth_bias = key.DecalDepthBias,
                    enable_depth_clip = true
                },
                multisample_state = new SDL_GPUMultisampleState
                {
                    sample_count = SampleCount(key.SampleCount)
                },
                depth_stencil_state = depthStencil,
                target_info = new SDL_GPUGraphicsPipelineTargetInfo
                {
                    color_target_descriptions = &colorTarget, num_color_targets = 1,
                    depth_stencil_format = _depthFormat, has_depth_stencil_target = true
                }
            };
            SDL_GPUGraphicsPipeline* pipeline = SDL3.SDL_CreateGPUGraphicsPipeline(_device.Handle, &info);
            if (pipeline == null) throw new InvalidOperationException($"SDL scene pipeline creation failed ({key.StencilMode}/{key.Topology}): {SDL3.SDL_GetError()}");
            return pipeline;
        }

        private static SDL_GPUDepthStencilState CreateDepthStencil(PipelineKey key)
        {
            SDL_GPUStencilOpState stencil = new()
            {
                fail_op = key.StencilMode == RenderStencilMode.Clear ? SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO : SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP,
                depth_fail_op = key.StencilMode == RenderStencilMode.Clear ? SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO : SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP,
                pass_op = key.StencilMode switch
                {
                    RenderStencilMode.Clear => SDL_GPUStencilOp.SDL_GPU_STENCILOP_ZERO,
                    RenderStencilMode.MarkTransparent => SDL_GPUStencilOp.SDL_GPU_STENCILOP_REPLACE,
                    _ => SDL_GPUStencilOp.SDL_GPU_STENCILOP_KEEP
                },
                compare_op = key.StencilMode switch
                {
                    RenderStencilMode.MarkTransparent => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_GREATER,
                    RenderStencilMode.EqualPolygon => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_EQUAL,
                    RenderStencilMode.NotEqualPolygon => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_NOT_EQUAL,
                    _ => SDL_GPUCompareOp.SDL_GPU_COMPAREOP_ALWAYS
                }
            };
            return new SDL_GPUDepthStencilState
            {
                compare_op = key.DepthMode == RenderDepthMode.Less
                    ? SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS : SDL_GPUCompareOp.SDL_GPU_COMPAREOP_LESS_OR_EQUAL,
                enable_depth_test = key.DepthMode != RenderDepthMode.Disabled,
                enable_depth_write = key.DepthWrite,
                enable_stencil_test = key.StencilMode != RenderStencilMode.Disabled,
                front_stencil_state = stencil,
                back_stencil_state = stencil,
                compare_mask = 0xFF,
                write_mask = 0xFF
            };
        }

        private void EnsureTargets(uint width, uint height, SdlGpuSceneTargetPlan plan)
        {
            if (_sceneColor != null && width == _targetWidth && height == _targetHeight
                && plan.RenderColorSamples == _targetSampleCount) return;
            SDL_GPUTexture* color = null;
            SDL_GPUTexture* multisampleColor = null;
            SDL_GPUTexture* depth = null;
            try
            {
                color = CreateTarget(_device.SwapchainFormat,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                    width, height, plan.ResolveColorSamples);
                if (plan.UsesResolve)
                {
                    multisampleColor = CreateTarget(_device.SwapchainFormat,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
                        width, height, plan.RenderColorSamples);
                }
                depth = CreateTarget(_depthFormat,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_DEPTH_STENCIL_TARGET
                        | (_depthSampleable && plan.DepthSamples == 1
                            ? SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER : 0),
                    width, height, plan.DepthSamples);
            }
            catch
            {
                if (depth != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, depth);
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                if (color != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, depth);
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw new InvalidOperationException($"SDL scene target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_sceneColor != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneColor);
            if (_sceneMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneMultisampleColor);
            if (_sceneDepth != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneDepth);
            _sceneColor = color;
            _sceneMultisampleColor = multisampleColor;
            _sceneDepth = depth;
            _targetWidth = width;
            _targetHeight = height;
            _targetSampleCount = plan.RenderColorSamples;
        }

        private void EnsureBloomTargets(uint width, uint height, SdlGpuBloomPlan plan)
        {
            if (!plan.Enabled) return;
            if (_bloomColor != null && width == _bloomTargetWidth
                && height == _bloomTargetHeight
                && plan.RenderSamples == _bloomTargetSampleCount) return;
            SDL_GPUTexture* color = null;
            SDL_GPUTexture* multisampleColor = null;
            try
            {
                color = CreateTarget(_device.SwapchainFormat,
                    SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET
                        | SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                    width, height, plan.ResolveSamples);
                if (plan.UsesResolve)
                {
                    multisampleColor = CreateTarget(_device.SwapchainFormat,
                        SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_COLOR_TARGET,
                        width, height, plan.RenderSamples);
                }
            }
            catch
            {
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                if (color != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw;
            }
            if (!SDL3.SDL_WaitForGPUIdle(_device.Handle))
            {
                if (multisampleColor != null)
                    SDL3.SDL_ReleaseGPUTexture(_device.Handle, multisampleColor);
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, color);
                throw new InvalidOperationException(
                    $"SDL bloom target resize wait failed: {SDL3.SDL_GetError()}");
            }
            if (_bloomMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomMultisampleColor);
            if (_bloomColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomColor);
            _bloomColor = color;
            _bloomMultisampleColor = multisampleColor;
            _bloomTargetWidth = width;
            _bloomTargetHeight = height;
            _bloomTargetSampleCount = plan.RenderSamples;
        }

        private SDL_GPUTexture* CreateTarget(SDL_GPUTextureFormat format,
            SDL_GPUTextureUsageFlags usage, uint width, uint height, int sampleCount)
        {
            SDL_GPUTextureCreateInfo info = new()
            {
                type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D, format = format, usage = usage,
                width = width, height = height, layer_count_or_depth = 1, num_levels = 1,
                sample_count = SampleCount(sampleCount)
            };
            SDL_GPUTexture* texture = SDL3.SDL_CreateGPUTexture(_device.Handle, &info);
            if (texture == null) throw new InvalidOperationException($"SDL scene target creation failed: {SDL3.SDL_GetError()}");
            return texture;
        }

        private static SDL_GPUBlitRegion Region(SDL_GPUTexture* texture, uint width, uint height)
            => new() { texture = texture, mip_level = 0, layer_or_depth_plane = 0, w = width, h = height };

        private static SDL_GPUVertexAttribute Attribute(uint location, SDL_GPUVertexElementFormat format, uint offset)
            => new() { location = location, buffer_slot = 0, format = format, offset = offset };

        private static SDL_GPUColorComponentFlags ColorMask(RenderColorWriteMask mask)
        {
            SDL_GPUColorComponentFlags value = 0;
            if ((mask & RenderColorWriteMask.Red) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_R;
            if ((mask & RenderColorWriteMask.Green) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_G;
            if ((mask & RenderColorWriteMask.Blue) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_B;
            if ((mask & RenderColorWriteMask.Alpha) != 0) value |= SDL_GPUColorComponentFlags.SDL_GPU_COLORCOMPONENT_A;
            return value;
        }

        private static SDL_GPUSamplerAddressMode Address(RepeatMode mode) => mode switch
        {
            RepeatMode.Repeat => SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_REPEAT,
            RepeatMode.Mirror => SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_MIRRORED_REPEAT,
            _ => SDL_GPUSamplerAddressMode.SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE
        };

        private static SDL_GPUSampleCount SampleCount(int count) => count switch
        {
            4 => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_4,
            2 => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_2,
            _ => SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
        };

        private void ReportSampleNegotiation(SdlGpuSampleNegotiation negotiation)
        {
            if (negotiation.Requested <= 1 || !_reportedSampleNegotiations.Add(negotiation)) return;
            string reason = negotiation.Reason switch
            {
                SdlGpuMsaaFallbackReason.CelDepthSampling
                    => "cel outline depth sampling requires a single-sample depth target",
                SdlGpuMsaaFallbackReason.UnsupportedColorOrDepthFormat
                    => "color/depth format sample-count support",
                _ => "supported"
            };
            Console.WriteLine($"[render] SDL GPU MSAA requested={negotiation.Requested}x "
                + $"effective={negotiation.Effective}x reason={reason}");
        }

        private static void PushVertex<T>(SDL_GPUCommandBuffer* commandBuffer, uint slot, T value) where T : unmanaged
            => SDL3.SDL_PushGPUVertexUniformData(commandBuffer, slot, (IntPtr)(&value), (uint)sizeof(T));

        private static void PushFragment<T>(SDL_GPUCommandBuffer* commandBuffer, uint slot, T value) where T : unmanaged
            => SDL3.SDL_PushGPUFragmentUniformData(commandBuffer, slot, (IntPtr)(&value), (uint)sizeof(T));

        private static SDL_GPUShader* CreateShader(SDL_GPUDevice* device, SDL_GPUShaderFormat format,
            string path, string entrypoint, SDL_GPUShaderStage stage, uint samplers, uint uniforms)
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
                SDL_GPUShader* shader = SDL3.SDL_CreateGPUShader(device, &info);
                if (shader == null) throw new InvalidOperationException($"SDL scene shader creation failed for {path}: {SDL3.SDL_GetError()}");
                return shader;
            }
        }

        private static (SDL_GPUShaderFormat, string, string) SelectArtifacts(SdlGpuDevice device)
        {
            string directory = ShaderArtifactManifest.Directory;
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_DXIL, Path.Combine(directory, "scene.vert.dxil"), Path.Combine(directory, "scene.frag.dxil"));
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_MSL, Path.Combine(directory, "scene.vert.msl"), Path.Combine(directory, "scene.frag.msl"));
            if ((device.ShaderFormats & SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV) != 0)
                return (SDL_GPUShaderFormat.SDL_GPU_SHADERFORMAT_SPIRV, Path.Combine(directory, "scene.vert.spv"), Path.Combine(directory, "scene.frag.spv"));
            throw new PlatformNotSupportedException($"SDL GPU shader formats {SdlGpuDevice.DescribeShaderFormats(device.ShaderFormats)} have no scene artifact.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SDL3.SDL_WaitForGPUIdle(_device.Handle);
            foreach (List<GpuMesh> slot in _dynamicSlots) foreach (GpuMesh mesh in slot) mesh.Dispose();
            foreach (List<GpuTexture> slot in _retiredTextureSlots) foreach (GpuTexture texture in slot) texture.Dispose();
            foreach (List<GpuMesh> slot in _retiredMeshSlots) foreach (GpuMesh mesh in slot) mesh.Dispose();
            foreach (GpuMesh mesh in _staticMeshes.Values) mesh.Dispose();
            foreach (GpuTexture texture in _textures.Values) texture.Dispose();
            _postResources?.Dispose();
            _whiteTexture?.Dispose();
            foreach (nint sampler in _samplers.Values) SDL3.SDL_ReleaseGPUSampler(_device.Handle, (SDL_GPUSampler*)sampler);
            foreach (nint pipeline in _pipelines.Values) SDL3.SDL_ReleaseGPUGraphicsPipeline(_device.Handle, (SDL_GPUGraphicsPipeline*)pipeline);
            if (_sceneDepth != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneDepth);
            if (_bloomMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomMultisampleColor);
            if (_bloomColor != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _bloomColor);
            if (_sceneMultisampleColor != null)
                SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneMultisampleColor);
            if (_sceneColor != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, _sceneColor);
            if (_fragmentShader != null) SDL3.SDL_ReleaseGPUShader(_device.Handle, _fragmentShader);
            if (_vertexShader != null) SDL3.SDL_ReleaseGPUShader(_device.Handle, _vertexShader);
        }

        private readonly record struct ScenePipelineKey(PipelineKey Key, bool Wireframe,
            bool FaceCulling, bool BloomEmission = false);

        private interface IUploadResource
        {
            void ReleaseUploadStorage();
            void InvalidateUpload();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FrameFragmentConstants { public Vector4 FogColor; public Vector4 Options; public Vector4 FogRange; }

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct DrawConstants
        {
            public Matrix4 Transform;
            public Matrix4 Billboard;
            public fixed float MatrixStack[RenderFrame.MatrixStackFloats];
            public Matrix4 TextureMatrix;
            public Vector4 Diffuse, Ambient, Specular, Emission, OverrideColor, PaletteOverride;
            public Vector4 Light1Vector, Light1Color, Light2Vector, Light2Color;
            public Vector4 DrawOptions, MaterialOptions, RenderOptions, FlatColor;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct GpuVertex
        {
            public float Px, Py, Pz, Cr, Cg, Cb, Ca, Nx, Ny, Nz, U, V;
            public uint MatrixIndex, Flags;
            public GpuVertex(RenderVertex vertex)
            {
                Px = vertex.Position.X; Py = vertex.Position.Y; Pz = vertex.Position.Z;
                Cr = vertex.Color.X; Cg = vertex.Color.Y; Cb = vertex.Color.Z; Ca = vertex.Color.W;
                Nx = vertex.Normal.X; Ny = vertex.Normal.Y; Nz = vertex.Normal.Z;
                U = vertex.TexCoord.X; V = vertex.TexCoord.Y;
                MatrixIndex = vertex.MatrixIndex; Flags = (uint)vertex.Flags;
            }
        }

        private sealed class GpuMesh : IDisposable, IUploadResource
        {
            private readonly SdlGpuDevice _device;
            private CpuMesh _mesh;
            private SDL_GPUTransferBuffer* _transfer;
            private bool _uploaded;
            private readonly uint _vertexCapacity;
            private readonly uint _triangleCapacity;
            private readonly uint _lineCapacity;
            private readonly uint _transferCapacity;
            public SDL_GPUBuffer* VertexBuffer { get; private set; }
            public SDL_GPUBuffer* TriangleIndexBuffer { get; private set; }
            public SDL_GPUBuffer* LineIndexBuffer { get; private set; }
            public int TriangleIndexCount => _mesh.TriangleIndexCount;
            public int LineIndexCount => _mesh.LineIndexCount;

            private GpuMesh(SdlGpuDevice device, CpuMesh mesh)
            {
                _device = device; _mesh = mesh;
                _vertexCapacity = checked((uint)Math.Max(sizeof(GpuVertex), mesh.VertexCount * sizeof(GpuVertex)));
                _triangleCapacity = checked((uint)Math.Max(sizeof(uint), mesh.TriangleIndexCount * sizeof(uint)));
                _lineCapacity = checked((uint)Math.Max(sizeof(uint), mesh.LineIndexCount * sizeof(uint)));
                _transferCapacity = checked((uint)Math.Max(4, mesh.VertexCount * sizeof(GpuVertex)
                    + (mesh.TriangleIndexCount + mesh.LineIndexCount) * sizeof(uint)));
                try
                {
                    VertexBuffer = Buffer(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_VERTEX, _vertexCapacity);
                    TriangleIndexBuffer = Buffer(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, _triangleCapacity);
                    LineIndexBuffer = Buffer(SDL_GPUBufferUsageFlags.SDL_GPU_BUFFERUSAGE_INDEX, _lineCapacity);
                    SDL_GPUTransferBufferCreateInfo transferInfo = new() { usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD, size = _transferCapacity };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(device.Handle, &transferInfo);
                    if (VertexBuffer == null || TriangleIndexBuffer == null || LineIndexBuffer == null || _transfer == null)
                        throw new InvalidOperationException($"SDL scene mesh allocation failed: {SDL3.SDL_GetError()}");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public static GpuMesh Create(SdlGpuDevice device, CpuMesh mesh) => new(device, mesh);

            public bool CanHold(CpuMesh mesh)
                => mesh.VertexCount * sizeof(GpuVertex) <= _vertexCapacity
                    && mesh.TriangleIndexCount * sizeof(uint) <= _triangleCapacity
                    && mesh.LineIndexCount * sizeof(uint) <= _lineCapacity
                    && mesh.VertexCount * sizeof(GpuVertex)
                        + (mesh.TriangleIndexCount + mesh.LineIndexCount) * sizeof(uint) <= _transferCapacity;

            public void Reset(CpuMesh mesh)
            {
                if (!CanHold(mesh)) throw new ArgumentException("Dynamic mesh exceeds its retained slot capacity.", nameof(mesh));
                _mesh = mesh;
                _uploaded = false;
            }

            public bool EnsureUploaded(SDL_GPUCommandBuffer* commandBuffer)
            {
                if (_uploaded) return false;
                if (_transfer == null)
                {
                    SDL_GPUTransferBufferCreateInfo info = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = _transferCapacity
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &info);
                    if (_transfer == null) throw new InvalidOperationException($"SDL scene mesh upload allocation failed: {SDL3.SDL_GetError()}");
                }
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _transfer, false);
                if (memory == IntPtr.Zero) throw new InvalidOperationException($"SDL scene mesh map failed: {SDL3.SDL_GetError()}");
                uint vertexBytes = checked((uint)(_mesh.VertexCount * sizeof(GpuVertex)));
                uint triangleBytes = checked((uint)(_mesh.TriangleIndexCount * sizeof(uint)));
                try
                {
                    GpuVertex* vertices = (GpuVertex*)memory;
                    for (int i = 0; i < _mesh.VertexCount; i++) vertices[i] = new GpuVertex(_mesh.Vertices[i]);
                    if (_mesh.TriangleIndexCount > 0) Marshal.Copy(_mesh.TriangleIndices, 0, memory + (int)vertexBytes, _mesh.TriangleIndexCount);
                    if (_mesh.LineIndexCount > 0) Marshal.Copy(_mesh.LineIndices, 0, memory + (int)(vertexBytes + triangleBytes), _mesh.LineIndexCount);
                }
                finally { SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transfer); }
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null) throw new InvalidOperationException($"SDL scene mesh copy pass failed: {SDL3.SDL_GetError()}");
                Upload(copy, VertexBuffer, 0, 0, vertexBytes);
                if (triangleBytes > 0) Upload(copy, TriangleIndexBuffer, vertexBytes, 0, triangleBytes);
                uint lineBytes = checked((uint)(_mesh.LineIndexCount * sizeof(uint)));
                if (lineBytes > 0) Upload(copy, LineIndexBuffer, vertexBytes + triangleBytes, 0, lineBytes);
                SDL3.SDL_EndGPUCopyPass(copy);
                _uploaded = true;
                return true;
            }

            public void ReleaseUploadStorage()
            {
                if (_transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                _transfer = null;
            }

            public void InvalidateUpload() => _uploaded = false;

            private void Upload(SDL_GPUCopyPass* copy, SDL_GPUBuffer* destination, uint sourceOffset, uint destinationOffset, uint size)
            {
                SDL_GPUTransferBufferLocation source = new() { transfer_buffer = _transfer, offset = sourceOffset };
                SDL_GPUBufferRegion target = new() { buffer = destination, offset = destinationOffset, size = size };
                SDL3.SDL_UploadToGPUBuffer(copy, &source, &target, false);
            }

            private SDL_GPUBuffer* Buffer(SDL_GPUBufferUsageFlags usage, uint size)
            {
                SDL_GPUBufferCreateInfo info = new() { usage = usage, size = Math.Max(4, size) };
                return SDL3.SDL_CreateGPUBuffer(_device.Handle, &info);
            }

            public void Dispose()
            {
                ReleaseUploadStorage();
                if (LineIndexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, LineIndexBuffer);
                if (TriangleIndexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, TriangleIndexBuffer);
                if (VertexBuffer != null) SDL3.SDL_ReleaseGPUBuffer(_device.Handle, VertexBuffer);
                _transfer = null; LineIndexBuffer = null; TriangleIndexBuffer = null; VertexBuffer = null;
            }
        }

        private sealed class GpuTexture : IDisposable, IUploadResource
        {
            private readonly SdlGpuDevice _device;
            private readonly RenderTexturePixels _pixels;
            private readonly uint _mipLevelCount;
            private SDL_GPUTransferBuffer* _transfer;
            private bool _uploaded;
            public SDL_GPUTexture* Handle { get; private set; }
            public bool Matches(RenderTexturePixels pixels, bool mipmapped)
                => _pixels.Revision == pixels.Revision
                    && _pixels.Width == pixels.Width
                    && _pixels.Height == pixels.Height
                    && _mipLevelCount == SdlGpuTextureQuality.MipLevelCount(
                        pixels.Width, pixels.Height, mipmapped);

            private GpuTexture(SdlGpuDevice device, RenderTexturePixels pixels,
                bool mipmapped)
            {
                _device = device;
                _pixels = pixels;
                _mipLevelCount = SdlGpuTextureQuality.MipLevelCount(
                    pixels.Width, pixels.Height, mipmapped);
                try
                {
                    SDL_GPUTextureCreateInfo textureInfo = new()
                    {
                        type = SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                        format = SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
                        usage = SdlGpuTextureQuality.SceneTextureUsage(mipmapped),
                        width = checked((uint)pixels.Width), height = checked((uint)pixels.Height),
                        layer_count_or_depth = 1, num_levels = _mipLevelCount,
                        sample_count = SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1
                    };
                    Handle = SDL3.SDL_CreateGPUTexture(device.Handle, &textureInfo);
                    SDL_GPUTransferBufferCreateInfo transferInfo = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = checked((uint)pixels.Rgba8.Length)
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(device.Handle, &transferInfo);
                    if (Handle == null || _transfer == null) throw new InvalidOperationException($"SDL scene texture allocation failed: {SDL3.SDL_GetError()}");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public static GpuTexture Create(SdlGpuDevice device, RenderTexturePixels pixels,
                bool mipmapped) => new(device, pixels, mipmapped);

            public bool EnsureUploaded(SDL_GPUCommandBuffer* commandBuffer)
            {
                if (_uploaded) return false;
                if (_transfer == null)
                {
                    SDL_GPUTransferBufferCreateInfo info = new()
                    {
                        usage = SDL_GPUTransferBufferUsage.SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD,
                        size = checked((uint)_pixels.Rgba8.Length)
                    };
                    _transfer = SDL3.SDL_CreateGPUTransferBuffer(_device.Handle, &info);
                    if (_transfer == null) throw new InvalidOperationException($"SDL scene texture upload allocation failed: {SDL3.SDL_GetError()}");
                }
                IntPtr memory = SDL3.SDL_MapGPUTransferBuffer(_device.Handle, _transfer, false);
                if (memory == IntPtr.Zero) throw new InvalidOperationException($"SDL scene texture map failed: {SDL3.SDL_GetError()}");
                try
                {
                    SdlGpuMappedMemoryCopy.Copy(_pixels.Rgba8, memory);
                }
                finally { SDL3.SDL_UnmapGPUTransferBuffer(_device.Handle, _transfer); }
                SDL_GPUCopyPass* copy = SDL3.SDL_BeginGPUCopyPass(commandBuffer);
                if (copy == null) throw new InvalidOperationException($"SDL scene texture copy pass failed: {SDL3.SDL_GetError()}");
                SDL_GPUTextureTransferInfo sourceInfo = new()
                {
                    transfer_buffer = _transfer, offset = 0,
                    pixels_per_row = checked((uint)_pixels.Width), rows_per_layer = checked((uint)_pixels.Height)
                };
                SDL_GPUTextureRegion target = new()
                {
                    texture = Handle, mip_level = 0, layer = 0, x = 0, y = 0, z = 0,
                    w = checked((uint)_pixels.Width), h = checked((uint)_pixels.Height), d = 1
                };
                SDL3.SDL_UploadToGPUTexture(copy, &sourceInfo, &target, false);
                SDL3.SDL_EndGPUCopyPass(copy);
                if (_mipLevelCount > 1)
                {
                    // SDL requires mip generation outside copy/render passes.
                    // _uploaded makes this exactly once per successful texture
                    // revision submission, and failed submissions invalidate it.
                    SDL3.SDL_GenerateMipmapsForGPUTexture(commandBuffer, Handle);
                }
                _uploaded = true;
                return true;
            }

            public void ReleaseUploadStorage()
            {
                if (_transfer != null) SDL3.SDL_ReleaseGPUTransferBuffer(_device.Handle, _transfer);
                _transfer = null;
            }

            public void InvalidateUpload() => _uploaded = false;

            public void Dispose()
            {
                ReleaseUploadStorage();
                if (Handle != null) SDL3.SDL_ReleaseGPUTexture(_device.Handle, Handle);
                _transfer = null; Handle = null;
            }
        }
    }
}
