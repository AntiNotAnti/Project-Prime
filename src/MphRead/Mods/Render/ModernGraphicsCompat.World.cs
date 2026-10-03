#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using Silk.NET.Core;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuColor = Silk.NET.WebGPU.Color;
using WgpuTexture = Silk.NET.WebGPU.Texture;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        private sealed class NativeRenderbuffer
        {
            internal WgpuTexture* Texture;
            internal TextureView* View;
        }

        private readonly record struct CorePipelineKey(
            ModernProgramKind Program,
            PrimitiveTopology Topology,
            WgpuTextureFormat ColorFormat,
            int ColorTargetCount,
            bool HasDepth,
            WgpuTextureFormat DepthFormat,
            bool DepthTest,
            bool DepthWrite,
            DepthFunction DepthFunction,
            bool StencilTest,
            StencilFunction StencilFunction,
            OpenTK.Graphics.OpenGL.StencilOp StencilFail,
            OpenTK.Graphics.OpenGL.StencilOp StencilDepthFail,
            OpenTK.Graphics.OpenGL.StencilOp StencilPass,
            int StencilReadMask,
            int StencilWriteMask,
            bool Cull,
            TriangleFace CullFace,
            bool Blend,
            BlendingFactor BlendSource,
            BlendingFactor BlendDestination,
            BlendEquationMode BlendEquation,
            ColorWriteMask WriteMask,
            bool PolygonOffset,
            float PolygonOffsetFactor,
            float PolygonOffsetUnits);

        private sealed class CorePipelineRecord
        {
            internal RenderPipeline* Pipeline;
            internal BindGroupLayout* Layout;
            internal bool MaskTexture;
            internal bool DepthTexture;
        }

        private readonly struct CoreTarget
        {
            internal CoreTarget(WgpuTexture* colorTexture, TextureView* colorView,
                WgpuTextureFormat colorFormat, TextureView* depthView, int width, int height,
                WgpuTextureFormat depthFormat = WgpuTextureFormat.Depth24PlusStencil8)
                : this(colorTexture, colorView, colorFormat, depthView, width, height,
                    depthFormat, null, null, null, null)
            {
            }

            internal CoreTarget(WgpuTexture* colorTexture, TextureView* colorView,
                WgpuTextureFormat colorFormat, TextureView* depthView, int width, int height,
                WgpuTextureFormat depthFormat,
                WgpuTexture* colorTexture1, TextureView* colorView1,
                WgpuTexture* colorTexture2, TextureView* colorView2)
            {
                ColorTexture = colorTexture;
                ColorView = colorView;
                ColorTexture1 = colorTexture1;
                ColorView1 = colorView1;
                ColorTexture2 = colorTexture2;
                ColorView2 = colorView2;
                ColorFormat = colorFormat;
                DepthView = depthView;
                DepthFormat = depthFormat;
                Width = width;
                Height = height;
            }

            internal WgpuTexture* ColorTexture { get; }
            internal TextureView* ColorView { get; }
            internal WgpuTexture* ColorTexture1 { get; }
            internal TextureView* ColorView1 { get; }
            internal WgpuTexture* ColorTexture2 { get; }
            internal TextureView* ColorView2 { get; }
            internal WgpuTextureFormat ColorFormat { get; }
            internal TextureView* DepthView { get; }
            internal WgpuTextureFormat DepthFormat { get; }
            internal bool HasStencil => HasDepth && DepthFormat == WgpuTextureFormat.Depth24PlusStencil8;
            internal int Width { get; }
            internal int Height { get; }
            internal bool HasDepth => DepthView != null;
            internal int ColorTargetCount => ColorView2 != null ? 3 : ColorView1 != null ? 2 : 1;

            internal WgpuTexture* ColorTextureAt(int index) => index switch
            {
                0 => ColorTexture,
                1 => ColorTexture1,
                2 => ColorTexture2,
                _ => null
            };

            internal TextureView* ColorViewAt(int index) => index switch
            {
                0 => ColorView,
                1 => ColorView1,
                2 => ColorView2,
                _ => null
            };
        }

        private readonly Dictionary<int, NativeRenderbuffer> _nativeRenderbuffers = new();
        private readonly Dictionary<CorePipelineKey, CorePipelineRecord> _corePipelines = new();
        private readonly Dictionary<WgpuTextureFormat, PipelineRecord> _blitPipelines = new();
        private Silk.NET.WebGPU.Sampler* _blitNearestSampler;
        private Silk.NET.WebGPU.Sampler* _blitLinearSampler;

        private ShaderModule* _worldShader;
        private ShaderModule* _rttShader;
        private ShaderModule* _shiftShader;
        private ShaderModule* _celShader;
        private ShaderModule* _playerOutlineShader;
        private ShaderModule* _toneMapShader;
        private WgpuBuffer* _uniformBuffer;
        private ulong _uniformBufferOffset;
        private RenderPassEncoder* _activeCorePass;
        private TextureView* _activeCoreColorView;
        private TextureView* _activeCoreColorView1;
        private TextureView* _activeCoreColorView2;
        private TextureView* _activeCoreDepthView;
        private int _activeCoreColorTargetCount;

        private bool _depthWrite = true;
        private DepthFunction _depthFunction = DepthFunction.Less;
        private TriangleFace _cullFace = TriangleFace.Back;
        private AlphaFunction _alphaFunction = AlphaFunction.Always;
        private float _alphaReference;
        private BlendEquationMode _blendEquation = BlendEquationMode.FuncAdd;
        private int _clearStencil;
        private StencilFunction _stencilFunction = StencilFunction.Always;
        private int _stencilReference;
        private int _stencilReadMask = unchecked((int)0xFFFFFFFF);
        private int _stencilWriteMask = unchecked((int)0xFFFFFFFF);
        private OpenTK.Graphics.OpenGL.StencilOp _stencilFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
        private OpenTK.Graphics.OpenGL.StencilOp _stencilDepthFail = OpenTK.Graphics.OpenGL.StencilOp.Keep;
        private OpenTK.Graphics.OpenGL.StencilOp _stencilPass = OpenTK.Graphics.OpenGL.StencilOp.Keep;
        private float _polygonOffsetFactor;
        private float _polygonOffsetUnits;

        private void CreateCoreShaders()
        {
            _worldShader = CreateViewportShader(ModernGraphicsShaders.World);
            _rttShader = CreateViewportShader(ModernGraphicsShaders.Rtt);
            _shiftShader = CreateViewportShader(ModernGraphicsShaders.Shift);
            _celShader = CreateViewportShader(ModernGraphicsShaders.Cel);
            _playerOutlineShader = CreateViewportShader(ModernGraphicsShaders.PlayerOutline);
            _toneMapShader = CreateViewportShader(ModernGraphicsShaders.ToneMap);

        }

        private void DisposeCoreShaders()
        {
            foreach (NativeRenderbuffer renderbuffer in _nativeRenderbuffers.Values)
                ReleaseNativeRenderbuffer(renderbuffer);
            _nativeRenderbuffers.Clear();

            foreach (CorePipelineRecord pipeline in _corePipelines.Values)
            {
                if (pipeline.Layout != null) _api.BindGroupLayoutRelease(pipeline.Layout);
                if (pipeline.Pipeline != null) _api.RenderPipelineRelease(pipeline.Pipeline);
            }
            _corePipelines.Clear();

            foreach (PipelineRecord pipeline in _blitPipelines.Values)
            {
                if (pipeline.Layout != null) _api.BindGroupLayoutRelease(pipeline.Layout);
                if (pipeline.Pipeline != null) _api.RenderPipelineRelease(pipeline.Pipeline);
            }
            _blitPipelines.Clear();
            if (_blitNearestSampler != null)
            {
                _api.SamplerRelease(_blitNearestSampler);
                _blitNearestSampler = null;
            }
            if (_blitLinearSampler != null)
            {
                _api.SamplerRelease(_blitLinearSampler);
                _blitLinearSampler = null;
            }

            if (_toneMapShader != null)
            {
                _api.ShaderModuleRelease(_toneMapShader);
                _toneMapShader = null;
            }
            if (_playerOutlineShader != null)
            {
                _api.ShaderModuleRelease(_playerOutlineShader);
                _playerOutlineShader = null;
            }
            if (_celShader != null)
            {
                _api.ShaderModuleRelease(_celShader);
                _celShader = null;
            }
            if (_shiftShader != null)
            {
                _api.ShaderModuleRelease(_shiftShader);
                _shiftShader = null;
            }
            if (_rttShader != null)
            {
                _api.ShaderModuleRelease(_rttShader);
                _rttShader = null;
            }
            if (_worldShader != null)
            {
                _api.ShaderModuleRelease(_worldShader);
                _worldShader = null;
            }
        }

        private OpenTK.Mathematics.Vector4 ViewportTransform(int width, int height) => new(
            _viewportWidth / (float)width, _viewportHeight / (float)height,
            (2f * _viewportX + _viewportWidth) / width - 1f,
            (2f * _viewportY + _viewportHeight) / height - 1f);

        private ShaderModule* CreateViewportShader(string source) => CreateWgslModule(source.Replace(
            "return output;", "let vp = bitcast<vec4<f32>>(u.data[319]); "
            + "output.position = vec4<f32>(output.position.xy * vp.xy + vp.zw * output.position.w, output.position.zw); return output;"));

        private ShaderModule* CreateWgslModule(string source)
        {
            nint code = SilkMarshal.StringToPtr(source);
            try
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Code = (byte*)code,
                    Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor }
                };
                var descriptor = new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)&wgsl
                };
                ShaderModule* shader = _api.DeviceCreateShaderModule(_device.Device, descriptor);
                if (shader == null) throw new InvalidOperationException("Could not create WGSL shader module.");
                return shader;
            }
            finally
            {
                SilkMarshal.Free(code);
            }
        }

        private void EndActiveCorePass()
        {
            if (_activeCorePass == null) return;
            _api.RenderPassEncoderEnd(_activeCorePass);
            _api.RenderPassEncoderRelease(_activeCorePass);
            _activeCorePass = null;
            _activeCoreColorView = null;
            _activeCoreColorView1 = null;
            _activeCoreColorView2 = null;
            _activeCoreDepthView = null;
            _activeCoreColorTargetCount = 0;
        }

        private RenderPassEncoder* CoreRenderPass(CoreTarget target, int colorTargetCount)
        {
            if (colorTargetCount < 1 || target.ColorTargetCount < colorTargetCount)
                throw new InvalidOperationException(
                    $"Render pass requires {colorTargetCount} color targets; framebuffer has {target.ColorTargetCount}.");

            TextureView* colorView1 = colorTargetCount > 1 ? target.ColorView1 : null;
            TextureView* colorView2 = colorTargetCount > 2 ? target.ColorView2 : null;
            if (_activeCorePass != null
                && _activeCoreColorTargetCount == colorTargetCount
                && _activeCoreColorView == target.ColorView
                && _activeCoreColorView1 == colorView1
                && _activeCoreColorView2 == colorView2
                && _activeCoreDepthView == target.DepthView)
            {
                return _activeCorePass;
            }

            EndActiveCorePass();
            CommandEncoder* encoder = BeginCommandEncoder();
            var colors = stackalloc RenderPassColorAttachment[colorTargetCount];
            for (int colorIndex = 0; colorIndex < colorTargetCount; colorIndex++)
            {
                colors[colorIndex] = new RenderPassColorAttachment
                {
                    DepthSlice = uint.MaxValue,
                    View = target.ColorViewAt(colorIndex),
                    ResolveTarget = null,
                    LoadOp = LoadOp.Load,
                    StoreOp = StoreOp.Store
                };
            }
            RenderPassDepthStencilAttachment depth = default;
            RenderPassDepthStencilAttachment* depthPtr = null;
            if (target.HasDepth)
            {
                depth = new RenderPassDepthStencilAttachment
                {
                    View = target.DepthView,
                    DepthLoadOp = LoadOp.Load,
                    DepthStoreOp = StoreOp.Store,
                    DepthReadOnly = false,
                    StencilLoadOp = target.HasStencil ? LoadOp.Load : LoadOp.Undefined,
                    StencilStoreOp = target.HasStencil ? StoreOp.Store : StoreOp.Undefined,
                    StencilReadOnly = !target.HasStencil
                };
                depthPtr = &depth;
            }
            var descriptor = new RenderPassDescriptor
            {
                ColorAttachments = colors,
                ColorAttachmentCount = (uint)colorTargetCount,
                DepthStencilAttachment = depthPtr
            };
            _activeCorePass = _api.CommandEncoderBeginRenderPass(encoder, descriptor);
            if (_measurePerformance) _coreRenderPasses++;
            if (_activeCorePass == null)
                throw new InvalidOperationException("Could not begin coalesced WebGPU render pass.");
            _activeCoreColorView = target.ColorView;
            _activeCoreColorView1 = colorView1;
            _activeCoreColorView2 = colorView2;
            _activeCoreDepthView = target.DepthView;
            _activeCoreColorTargetCount = colorTargetCount;
            return _activeCorePass;
        }

        private ModernProgramKind CurrentProgramKind()
        {
            int program = _programs.CurrentProgram;
            if (program == 0) return ModernProgramKind.Unknown;
            return _programs.Program(program).Kind;
        }

        private CoreTarget ResolveDrawTarget()
        {
            return ResolveTarget(_resources.DrawFramebuffer, forDraw: true);
        }

        private CoreTarget ResolveReadTarget()
        {
            return ResolveTarget(_resources.ReadFramebuffer, forDraw: false);
        }

        private CoreTarget ResolveTarget(int framebufferId, bool forDraw)
        {
            if (framebufferId == 0)
            {
                if (!AcquireSurfaceTexture())
                    throw new InvalidOperationException("No WebGPU surface texture is available.");
                return new CoreTarget(_surfaceTexture.Texture, _surfaceView, _surfaceFormat,
                    EnsureSurfaceDepth(), (int)_width, (int)_height);
            }

            ModernGraphicsResourceState.FramebufferRecord framebuffer = _resources.Framebuffer(framebufferId);
            if (framebuffer.ColorTexture == 0)
                throw new InvalidOperationException($"Framebuffer {framebufferId} has no color attachment.");

            NativeTexture color = EnsureTexture(framebuffer.ColorTexture);
            ModernGraphicsResourceState.TextureRecord colorRecord = _resources.Texture(framebuffer.ColorTexture);
            WgpuTextureFormat colorFormat = ColorFormat(colorRecord);
            NativeTexture? color1 = null;
            NativeTexture? color2 = null;
            if (framebuffer.ColorTexture1 != 0)
            {
                color1 = EnsureTexture(framebuffer.ColorTexture1);
                ModernGraphicsResourceState.TextureRecord record1 = _resources.Texture(framebuffer.ColorTexture1);
                if (record1.Width != colorRecord.Width || record1.Height != colorRecord.Height
                    || ColorFormat(record1) != colorFormat)
                    throw new InvalidOperationException("Framebuffer color attachment 1 does not match attachment 0.");
            }
            if (framebuffer.ColorTexture2 != 0)
            {
                if (color1 == null)
                    throw new InvalidOperationException("Framebuffer color attachment 2 requires attachment 1.");
                color2 = EnsureTexture(framebuffer.ColorTexture2);
                ModernGraphicsResourceState.TextureRecord record2 = _resources.Texture(framebuffer.ColorTexture2);
                if (record2.Width != colorRecord.Width || record2.Height != colorRecord.Height
                    || ColorFormat(record2) != colorFormat)
                    throw new InvalidOperationException("Framebuffer color attachment 2 does not match attachment 0.");
            }
            TextureView* depth = null;
            WgpuTextureFormat depthFormat = WgpuTextureFormat.Depth24PlusStencil8;

            int depthTexture = framebuffer.DepthStencilTexture != 0
                ? framebuffer.DepthStencilTexture : framebuffer.DepthTexture;
            if (depthTexture != 0)
            {
                NativeTexture nativeDepth = EnsureTexture(depthTexture);
                depth = nativeDepth.View;
                depthFormat = nativeDepth.Format;
            }
            else
            {
                int depthRenderbuffer = framebuffer.DepthStencilRenderbuffer != 0
                    ? framebuffer.DepthStencilRenderbuffer : framebuffer.DepthRenderbuffer;
                if (depthRenderbuffer != 0)
                {
                    depth = EnsureRenderbuffer(depthRenderbuffer).View;
                }
            }

            return new CoreTarget(color.Texture, color.View, colorFormat, depth,
                Math.Max(1, colorRecord.Width), Math.Max(1, colorRecord.Height), depthFormat,
                color1 == null ? null : color1.Texture, color1 == null ? null : color1.View,
                color2 == null ? null : color2.Texture, color2 == null ? null : color2.View);
        }

        private NativeRenderbuffer EnsureRenderbuffer(int id)
        {
            ModernGraphicsResourceState.RenderbufferRecord record = _resources.Renderbuffer(id);
            if (_nativeRenderbuffers.TryGetValue(id, out NativeRenderbuffer? existing) && !record.Dirty)
                return existing;
            if (existing != null)
            {
                ReleaseNativeRenderbuffer(existing);
                _nativeRenderbuffers.Remove(id);
            }

            WgpuTextureFormat format = WgpuTextureFormat.Depth24PlusStencil8;
            var descriptor = new TextureDescriptor
            {
                Size = new Extent3D((uint)Math.Max(1, record.Width),
                    (uint)Math.Max(1, record.Height), 1),
                Format = format,
                Usage = TextureUsage.RenderAttachment,
                MipLevelCount = 1,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D
            };
            var native = new NativeRenderbuffer
            {
                Texture = _api.DeviceCreateTexture(_device.Device, descriptor)
            };
            if (native.Texture == null)
                throw new InvalidOperationException($"Could not allocate modern renderbuffer {id}.");
            native.View = _api.TextureCreateView(native.Texture, null);
            if (native.View == null)
            {
                _api.TextureRelease(native.Texture);
                throw new InvalidOperationException($"Could not create modern renderbuffer view {id}.");
            }
            record.Dirty = false;
            _nativeRenderbuffers[id] = native;
            return native;
        }

        private void ReleaseNativeRenderbuffer(NativeRenderbuffer renderbuffer)
        {
            EndActiveCorePass();
            if (renderbuffer.View != null) _api.TextureViewRelease(renderbuffer.View);
            if (renderbuffer.Texture != null) _api.TextureRelease(renderbuffer.Texture);
        }

        private static WgpuTextureFormat ColorFormat(ModernGraphicsResourceState.TextureRecord record)
        {
            return NativeTextureFormat(record);
        }

        // Validate before allocating draw resources or joining a coalesced
        // render pass: inactive GL bindings must
        // never enter a WebGPU usage scope, even behind a uniform shader branch.
        private int ValidateRenderPassResources(int unit, bool required, CoreTarget target)
        {
            int id = required ? _resources.BoundTexture(unit) : 0;
            if (id == 0) return 0;
            NativeTexture texture = EnsureTexture(id);
            string? attachment = texture.Texture == target.ColorTexture
                || texture.Texture == target.ColorTexture1
                || texture.Texture == target.ColorTexture2 ? "ColorAttachment" : null;
            if (_resources.DrawFramebuffer != 0)
            {
                var framebuffer = _resources.Framebuffer(_resources.DrawFramebuffer);
                if (id == framebuffer.DepthTexture || id == framebuffer.DepthStencilTexture)
                    attachment = "DepthAttachment";
            }
            if (attachment != null)
            {
                string message = $"WebGPU feedback hazard: texture={id} native=0x{(nuint)texture.Texture:x} "
                    + $"framebuffer={_resources.DrawFramebuffer} program={_programs.CurrentProgram} "
                    + $"unit={unit} usage=SampledTexture attachment={attachment}";
                Mods.DebugLog.Line("render", message);
                throw new InvalidOperationException(message);
            }
            return id;
        }

        private void DrawCoreIndexed(ReadOnlySpan<float> vertices, ReadOnlySpan<int> indices,
            PrimitiveTopology topology, ModernProgramKind kind,
            float[]? persistentVertices = null, int[]? persistentIndices = null,
            NativeGeometry? retainedGeometry = null)
        {
            if (_resources.DrawFramebuffer == 0 && !AcquireSurfaceTexture()) return;
            _device.ThrowIfFailed();
            CoreTarget target = ResolveDrawTarget();
            ModernProgramKind effective = kind switch
            {
                ModernProgramKind.Clear => ModernProgramKind.Clear,
                ModernProgramKind.DeferredPbr => ModernProgramKind.DeferredPbr,
                ModernProgramKind.DeferredPbrMrt => ModernProgramKind.DeferredPbrMrt,
                ModernProgramKind.PostProcess => ModernProgramKind.PostProcess,
                ModernProgramKind.World => ModernProgramKind.World,
                ModernProgramKind.Shift => ModernProgramKind.Shift,
                ModernProgramKind.Cel => ModernProgramKind.Cel,
                ModernProgramKind.PlayerOutline => ModernProgramKind.PlayerOutline,
                ModernProgramKind.ToneMap => ModernProgramKind.ToneMap,
                ModernProgramKind.Rtt => ModernProgramKind.Rtt,
                _ => _resources.DrawFramebuffer != 0
                    ? ModernProgramKind.FixedFunction : ModernProgramKind.Rtt
            };

            var program = _programs.CurrentProgram == 0 ? null : _programs.Program(_programs.CurrentProgram);
            if (effective == ModernProgramKind.DeferredPbr
                && program != null && Int(program, "gbuffer_mode") == 0)
            {
                effective = ModernProgramKind.DeferredPbrMrt;
            }
            bool generated = UsesGeneratedShader(effective);
            if (generated) PrepareGeneratedResources(effective, target);
            int texture0 = ValidateRenderPassResources(0,
                !generated && effective != ModernProgramKind.Clear
                    && (effective == ModernProgramKind.FixedFunction ? _enabled.Contains(EnableCap.Texture2D)
                        : effective != ModernProgramKind.World || (program != null && Int(program, "use_texture") != 0)), target);
            int texture1 = ValidateRenderPassResources(1,
                effective == ModernProgramKind.Cel
                    || (effective == ModernProgramKind.Rtt && program != null && Int(program, "use_mask") != 0), target);
            if (effective == ModernProgramKind.Cel && texture1 == 0)
                throw new InvalidOperationException("Cel pass requires the depth texture on unit 1.");

            CorePipelineRecord pipeline = CorePipeline(effective, topology, target);
            if (!generated) WriteCompatibilityUniforms(effective, target);

            ulong vertexBytes = (ulong)(vertices.Length * sizeof(float));
            ulong indexBytes = (ulong)(indices.Length * sizeof(int));
            NativeGeometry geometryBuffers = retainedGeometry
                ?? (persistentVertices != null && persistentIndices != null
                    ? PrepareGeometry(persistentVertices, persistentIndices)
                    : PrepareGeometry(vertices, indices));
            WgpuBuffer* vertex = geometryBuffers.Vertex;
            WgpuBuffer* index = geometryBuffers.Index;

            TextureView* baseView = _whiteView;
            Silk.NET.WebGPU.Sampler* baseSampler = _whiteSampler;
            if (texture0 != 0)
            {
                NativeTexture baseTexture = EnsureTexture(texture0);
                baseView = baseTexture.SampleView;
                baseSampler = baseTexture.Sampler;
            }

            BindGroup* bindGroup;
            if (generated) bindGroup = GeneratedBindGroup(effective, pipeline.Layout);
            else if (pipeline.DepthTexture)
            {
                if (texture1 == 0)
                    throw new InvalidOperationException("Cel pass requires the depth texture on unit 1.");
                NativeTexture depthTexture = EnsureTexture(texture1);
                if (depthTexture.Format != WgpuTextureFormat.Depth24PlusStencil8
                    && depthTexture.Format != WgpuTextureFormat.Depth24Plus)
                {
                    throw new InvalidOperationException(
                        $"Cel depth binding {texture1} is {depthTexture.Format}, not a depth texture.");
                }

                var entries = stackalloc BindGroupEntry[4];
                entries[0] = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = _uniformBuffer,
                    Offset = _uniformBufferOffset,
                    Size = (ulong)(ModernGraphicsShaders.UniformSlots * 4 * sizeof(uint))
                };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = baseView };
                entries[2] = new BindGroupEntry { Binding = 2, Sampler = baseSampler };
                entries[3] = new BindGroupEntry { Binding = 3, TextureView = depthTexture.SampleView };
                Span<nint> resources = stackalloc nint[6]
                {
                    (nint)pipeline.Layout, (nint)_uniformBuffer, (nint)_uniformBufferOffset,
                    (nint)baseView, (nint)baseSampler, (nint)depthTexture.SampleView
                };
                bindGroup = FrameBindGroup(new BindGroupDescriptor
                {
                    Layout = pipeline.Layout,
                    Entries = entries,
                    EntryCount = 4
                }, resources);
            }
            else if (pipeline.MaskTexture)
            {
                TextureView* maskView = _whiteView;
                if (texture1 != 0) maskView = EnsureTexture(texture1).SampleView;
                var entries = stackalloc BindGroupEntry[4];
                entries[0] = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = _uniformBuffer,
                    Offset = _uniformBufferOffset,
                    Size = (ulong)(ModernGraphicsShaders.UniformSlots * 4 * sizeof(uint))
                };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = baseView };
                entries[2] = new BindGroupEntry { Binding = 2, Sampler = baseSampler };
                entries[3] = new BindGroupEntry { Binding = 3, TextureView = maskView };
                Span<nint> resources = stackalloc nint[6]
                {
                    (nint)pipeline.Layout, (nint)_uniformBuffer, (nint)_uniformBufferOffset,
                    (nint)baseView, (nint)baseSampler, (nint)maskView
                };
                bindGroup = FrameBindGroup(new BindGroupDescriptor
                {
                    Layout = pipeline.Layout,
                    Entries = entries,
                    EntryCount = 4
                }, resources);
            }
            else
            {
                var entries = stackalloc BindGroupEntry[3];
                entries[0] = new BindGroupEntry
                {
                    Binding = 0,
                    Buffer = _uniformBuffer,
                    Offset = _uniformBufferOffset,
                    Size = (ulong)(ModernGraphicsShaders.UniformSlots * 4 * sizeof(uint))
                };
                entries[1] = new BindGroupEntry { Binding = 1, TextureView = baseView };
                entries[2] = new BindGroupEntry { Binding = 2, Sampler = baseSampler };
                Span<nint> resources = stackalloc nint[5]
                {
                    (nint)pipeline.Layout, (nint)_uniformBuffer, (nint)_uniformBufferOffset,
                    (nint)baseView, (nint)baseSampler
                };
                bindGroup = FrameBindGroup(new BindGroupDescriptor
                {
                    Layout = pipeline.Layout,
                    Entries = entries,
                    EntryCount = 3
                }, resources);
            }

            int colorTargetCount = effective == ModernProgramKind.DeferredPbrMrt ? 3 : 1;
            RenderPassEncoder* pass = CoreRenderPass(target, colorTargetCount);
            _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderSetVertexBuffer(pass, 0, vertex,
                geometryBuffers.VertexOffset, vertexBytes);
            _api.RenderPassEncoderSetIndexBuffer(pass, index, IndexFormat.Uint32,
                geometryBuffers.IndexOffset, indexBytes);
            _api.RenderPassEncoderSetViewport(pass, 0, 0, target.Width, target.Height, 0, 1);
            ApplyScissor(pass, target.Width, target.Height);
            if (_enabled.Contains(EnableCap.StencilTest) && target.HasDepth)
                _api.RenderPassEncoderSetStencilReference(pass, (uint)_stencilReference);
            _api.RenderPassEncoderDrawIndexed(pass, (uint)indices.Length, 1, 0, 0, 0);
            if (_measurePerformance) _coreDraws++;
            RecordCommandOperation();
        }

        private CorePipelineRecord CorePipeline(ModernProgramKind program,
            PrimitiveTopology topology, CoreTarget target)
        {
            int colorTargetCount = program == ModernProgramKind.DeferredPbrMrt ? 3 : 1;
            if (target.ColorTargetCount < colorTargetCount)
                throw new InvalidOperationException(
                    $"Program {program} requires {colorTargetCount} color targets; framebuffer has {target.ColorTargetCount}.");
            var key = new CorePipelineKey(program, topology, target.ColorFormat, colorTargetCount,
                target.HasDepth, target.DepthFormat,
                _enabled.Contains(EnableCap.DepthTest), _depthWrite, _depthFunction,
                _enabled.Contains(EnableCap.StencilTest), _stencilFunction,
                _stencilFail, _stencilDepthFail, _stencilPass, _stencilReadMask, _stencilWriteMask,
                _enabled.Contains(EnableCap.CullFace), _cullFace,
                _enabled.Contains(EnableCap.Blend), _blendSource, _blendDestination, _blendEquation,
                CurrentWriteMask(), _enabled.Contains(EnableCap.PolygonOffsetFill),
                _polygonOffsetFactor, _polygonOffsetUnits);
            if (_corePipelines.TryGetValue(key, out CorePipelineRecord? cached)) return cached;
            long pipelineStart = PerformanceStart();

            GeneratedProgram? generated = UsesGeneratedShader(program) ? GeneratedShader(program) : null;
            ShaderModule* shader = program switch
            {
                ModernProgramKind.Clear => _clearShader != null ? _clearShader : (_clearShader = CreateWgslModule(ModernGraphicsShaders.Clear)),
                ModernProgramKind.World or ModernProgramKind.FixedFunction => _worldShader,
                ModernProgramKind.Shift => _shiftShader,
                ModernProgramKind.Cel => _celShader,
                ModernProgramKind.PlayerOutline => _playerOutlineShader,
                ModernProgramKind.ToneMap => _toneMapShader,
                _ => _rttShader
            };
            var attributes = stackalloc VertexAttribute[6];
            attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
            attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 3u * sizeof(float), ShaderLocation = 1 };
            attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 7u * sizeof(float), ShaderLocation = 2 };
            attributes[3] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 10u * sizeof(float), ShaderLocation = 3 };
            attributes[4] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 13u * sizeof(float), ShaderLocation = 4 };
            attributes[5] = new VertexAttribute { Format = VertexFormat.Float32, Offset = 14u * sizeof(float), ShaderLocation = 5 };
            var vertexLayout = new VertexBufferLayout
            {
                Attributes = attributes,
                AttributeCount = 6,
                StepMode = VertexStepMode.Vertex,
                ArrayStride = (ulong)(LegacyGeometryBatch.FloatsPerVertex * sizeof(float))
            };

            BlendState blend = default;
            BlendState* blendPtr = null;
            if (key.Blend)
            {
                blend = new BlendState
                {
                    Color = new BlendComponent
                    {
                        SrcFactor = ToBlend(key.BlendSource),
                        DstFactor = ToBlend(key.BlendDestination),
                        Operation = ToBlendOperation(key.BlendEquation)
                    },
                    Alpha = new BlendComponent
                    {
                        SrcFactor = ToBlend(key.BlendSource),
                        DstFactor = ToBlend(key.BlendDestination),
                        Operation = ToBlendOperation(key.BlendEquation)
                    }
                };
                blendPtr = &blend;
            }

            var targetStates = stackalloc ColorTargetState[colorTargetCount];
            for (int colorIndex = 0; colorIndex < colorTargetCount; colorIndex++)
            {
                targetStates[colorIndex] = new ColorTargetState
                {
                    Format = target.ColorFormat,
                    Blend = blendPtr,
                    WriteMask = key.WriteMask
                };
            }

            DepthStencilState depthStencil = default;
            DepthStencilState* depthPtr = null;
            if (target.HasDepth)
            {
                StencilFaceState stencil = new StencilFaceState
                {
                    Compare = key.StencilTest ? ToCompare(key.StencilFunction) : CompareFunction.Always,
                    FailOp = key.StencilTest ? ToStencil(key.StencilFail) : StencilOperation.Keep,
                    DepthFailOp = key.StencilTest ? ToStencil(key.StencilDepthFail) : StencilOperation.Keep,
                    PassOp = key.StencilTest ? ToStencil(key.StencilPass) : StencilOperation.Keep
                };
                depthStencil = new DepthStencilState
                {
                    Format = key.DepthFormat,
                    DepthWriteEnabled = key.DepthTest && key.DepthWrite,
                    DepthCompare = key.DepthTest ? ToCompare(key.DepthFunction) : CompareFunction.Always,
                    StencilFront = stencil,
                    StencilBack = stencil,
                    StencilReadMask = unchecked((uint)key.StencilReadMask),
                    StencilWriteMask = unchecked((uint)key.StencilWriteMask),
                    DepthBias = key.PolygonOffset ? (int)MathF.Round(key.PolygonOffsetUnits) : 0,
                    DepthBiasSlopeScale = key.PolygonOffset ? key.PolygonOffsetFactor : 0,
                    DepthBiasClamp = 0
                };
                depthPtr = &depthStencil;
            }

            nint vs = SilkMarshal.StringToPtr(generated != null ? "main" : "vs_main");
            nint fs = SilkMarshal.StringToPtr(generated != null ? "main" : "fs_main");
            try
            {
                var fragment = new FragmentState
                {
                    Module = generated != null ? generated.Fragment : shader,
                    EntryPoint = (byte*)fs,
                    Targets = targetStates,
                    TargetCount = (uint)colorTargetCount
                };
                var descriptor = new RenderPipelineDescriptor
                {
                    Vertex = new VertexState
                    {
                        Module = generated != null ? generated.Vertex : shader,
                        EntryPoint = (byte*)vs,
                        Buffers = &vertexLayout,
                        BufferCount = 1
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = topology,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = key.Cull ? ToCull(key.CullFace) : CullMode.None
                    },
                    DepthStencil = depthPtr,
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = ~0u,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragment
                };
                RenderPipeline* pipeline = _api.DeviceCreateRenderPipeline(_device.Device, descriptor);
                if (pipeline == null)
                    throw new InvalidOperationException($"Could not create modern {program} render pipeline.");
                BindGroupLayout* layout = _api.RenderPipelineGetBindGroupLayout(pipeline, 0);
                var result = new CorePipelineRecord
                {
                    Pipeline = pipeline,
                    Layout = layout,
                    MaskTexture = program == ModernProgramKind.Rtt,
                    DepthTexture = program == ModernProgramKind.Cel
                };
                _corePipelines.Add(key, result);
                RecordPipelineCreation(pipelineStart);
                return result;
            }
            finally
            {
                SilkMarshal.Free(vs);
                SilkMarshal.Free(fs);
            }
        }

        private readonly uint[] _compatibilityWords = new uint[ModernGraphicsShaders.UniformSlots * 4];

        private void WriteCompatibilityUniforms(ModernProgramKind kind, CoreTarget target)
        {
            uint[] words = _compatibilityWords;
            Array.Clear(words);
            var viewport = ViewportTransform(target.Width, target.Height);
            WriteFloat(words, 319, 0, viewport.X);
            WriteFloat(words, 319, 1, viewport.Y);
            WriteFloat(words, 319, 2, viewport.Z);
            WriteFloat(words, 319, 3, viewport.W);
            WriteIdentity(words, ModernGraphicsShaders.Projection);
            WriteIdentity(words, ModernGraphicsShaders.View);
            WriteIdentity(words, ModernGraphicsShaders.ViewInverse);
            WriteIdentity(words, ModernGraphicsShaders.TextureMatrix);
            for (int i = 0; i < 32; i++)
                WriteIdentity(words, ModernGraphicsShaders.MatrixStack + i * 4);

            WriteFloat(words, ModernGraphicsShaders.ImmColor, 0, _currentColor.X);
            WriteFloat(words, ModernGraphicsShaders.ImmColor, 1, _currentColor.Y);
            WriteFloat(words, ModernGraphicsShaders.ImmColor, 2, _currentColor.Z);
            WriteFloat(words, ModernGraphicsShaders.ImmColor, 3, _currentColor.W);
            WriteFloat(words, ModernGraphicsShaders.FragmentFlags0, 0, 1f);
            WriteFloat(words, ModernGraphicsShaders.RttScalars, 0, 1f);
            WriteFloat(words, ModernGraphicsShaders.RttScalars, 2, _width);
            WriteFloat(words, ModernGraphicsShaders.RttScalars, 3, _height);

            int programId = _programs.CurrentProgram;
            ModernGraphicsCompatState.ProgramRecord? program =
                programId == 0 ? null : _programs.Program(programId);

            if (kind == ModernProgramKind.FixedFunction)
            {
                WriteBool(words, ModernGraphicsShaders.Flags0, 1, _enabled.Contains(EnableCap.Texture2D));
                WriteBool(words, ModernGraphicsShaders.Flags0, 2, true);
            }
            else if (kind == ModernProgramKind.World && program != null)
            {
                WriteBool(words, ModernGraphicsShaders.Flags0, 0, Int(program, "use_light") != 0);
                WriteBool(words, ModernGraphicsShaders.Flags0, 1, Int(program, "use_texture") != 0);
                WriteBool(words, ModernGraphicsShaders.Flags0, 2, Int(program, "show_colors") != 0);
                WriteBool(words, ModernGraphicsShaders.Flags0, 3, Int(program, "fog_enable") != 0);
                WriteVec(words, ModernGraphicsShaders.Light1Vector, Data(program, "light1vec"));
                WriteVec(words, ModernGraphicsShaders.Light1Color, Data(program, "light1col"));
                WriteVec(words, ModernGraphicsShaders.Light2Vector, Data(program, "light2vec"));
                WriteVec(words, ModernGraphicsShaders.Light2Color, Data(program, "light2col"));
                WriteVec(words, ModernGraphicsShaders.Diffuse, Data(program, "diffuse"));
                WriteVec(words, ModernGraphicsShaders.Ambient, Data(program, "ambient"));
                WriteVec(words, ModernGraphicsShaders.Specular, Data(program, "specular"));
                WriteVec(words, ModernGraphicsShaders.Emission, Data(program, "emission"));
                WriteVec(words, ModernGraphicsShaders.FogColor, Data(program, "fog_color"));
                WriteFloat(words, ModernGraphicsShaders.Scalars0, 0, Float(program, "far_plane"));
                WriteFloat(words, ModernGraphicsShaders.Scalars0, 1, Float(program, "fog_min"));
                WriteFloat(words, ModernGraphicsShaders.Scalars0, 2, Float(program, "fog_max"));
                WriteInt(words, ModernGraphicsShaders.Scalars0, 3, Int(program, "texgen_mode"));

                WriteMatrix(words, ModernGraphicsShaders.Projection, Data(program, "proj_mtx"));
                WriteMatrix(words, ModernGraphicsShaders.View, Data(program, "view_mtx"));
                WriteMatrix(words, ModernGraphicsShaders.ViewInverse, Data(program, "view_inv_mtx"));
                WriteMatrix(words, ModernGraphicsShaders.TextureMatrix, Data(program, "tex_mtx"));
                WriteMatrices(words, ModernGraphicsShaders.MatrixStack, Data(program, "mtx_stack"), 32);

                WriteFloat(words, ModernGraphicsShaders.FragmentFlags0, 0,
                    Float(program, "mat_alpha", 1f));
                WriteInt(words, ModernGraphicsShaders.FragmentFlags0, 1, Int(program, "mat_mode"));
                WriteBool(words, ModernGraphicsShaders.FragmentFlags0, 2,
                    Int(program, "use_override") != 0);
                WriteBool(words, ModernGraphicsShaders.FragmentFlags0, 3,
                    Int(program, "use_pal_override") != 0);
                WriteVec(words, ModernGraphicsShaders.OverrideColor, Data(program, "override_color"));
                WriteVec(words, ModernGraphicsShaders.PaletteOverrideColor, Data(program, "pal_override_color"));

                int alphaTest = 0;
                if (_enabled.Contains(EnableCap.AlphaTest))
                {
                    if (_alphaFunction == AlphaFunction.Equal && _alphaReference >= 0.999f) alphaTest = 1;
                    else if (_alphaFunction == AlphaFunction.Less && _alphaReference >= 0.999f) alphaTest = 2;
                }
                WriteInt(words, ModernGraphicsShaders.FragmentFlags1, 0, alphaTest);
                WriteInt(words, ModernGraphicsShaders.FragmentFlags1, 1, Int(program, "cel_bands"));
                WriteBool(words, ModernGraphicsShaders.FragmentFlags1, 2, Int(program, "use_flat") != 0);
                WriteInt(words, ModernGraphicsShaders.FragmentFlags1, 3, Int(program, "textured_player_skin"));
                WriteVec(words, ModernGraphicsShaders.FlatColor, Data(program, "flat_color"));
                WriteVec(words, ModernGraphicsShaders.PlayerOutlineColor, Data(program, "player_outline_color"));
                WriteBool(words, ModernGraphicsShaders.FragmentFlags2, 0,
                    Int(program, "player_outline_mask") != 0);
                WriteVecArray(words, ModernGraphicsShaders.ToonTable, Data(program, "toon_table"), 32, 3);
            }
            else if (kind == ModernProgramKind.Shift && program != null)
            {
                WriteMatrices(words, ModernGraphicsShaders.ShiftTable,
                    Data(program, "shift_table"), 4);
                WriteMatrices(words, ModernGraphicsShaders.WhiteTable,
                    Data(program, "white_table"), 12);
                WriteInt(words, ModernGraphicsShaders.ShiftParams, 0,
                    Int(program, "shift_idx"));
                WriteFloat(words, ModernGraphicsShaders.ShiftParams, 1,
                    Float(program, "shift_fac"));
                WriteFloat(words, ModernGraphicsShaders.ShiftParams, 2,
                    Float(program, "lerp_fac"));
                WriteFloat(words, ModernGraphicsShaders.ShiftParams, 3,
                    Float(program, "white_fac"));
                WriteBool(words, ModernGraphicsShaders.FragmentFlags3, 0,
                    _resources.IsFramebufferTexture(_resources.BoundTexture(0)));
            }
            else if (kind == ModernProgramKind.Cel && program != null)
            {
                WriteFloat(words, ModernGraphicsShaders.CelParams0, 0,
                    Float(program, "texel_w"));
                WriteFloat(words, ModernGraphicsShaders.CelParams0, 1,
                    Float(program, "texel_h"));
                WriteFloat(words, ModernGraphicsShaders.CelParams0, 2,
                    Float(program, "outline"));
                WriteFloat(words, ModernGraphicsShaders.CelParams0, 3,
                    Float(program, "near_plane"));
                WriteFloat(words, ModernGraphicsShaders.CelParams1, 0,
                    Float(program, "far_plane", 10000f));
                WriteFloat(words, ModernGraphicsShaders.CelParams1, 1,
                    Float(program, "depth_quantum"));
                WriteInt(words, ModernGraphicsShaders.CelParams1, 2,
                    Int(program, "probe"));
            }
            else if (kind == ModernProgramKind.PlayerOutline && program != null)
            {
                WriteFloat(words, ModernGraphicsShaders.OutlineParams, 0,
                    Float(program, "outline_step_x"));
                WriteFloat(words, ModernGraphicsShaders.OutlineParams, 1,
                    Float(program, "outline_step_y"));
                WriteBool(words, ModernGraphicsShaders.FragmentFlags3, 0,
                    _resources.IsFramebufferTexture(_resources.BoundTexture(0)));
            }
            else if (kind == ModernProgramKind.ToneMap && program != null)
            {
                WriteBool(words, ModernGraphicsShaders.FragmentFlags3, 0,
                    _resources.IsFramebufferTexture(_resources.BoundTexture(0)));
            }
            else if (program != null)
            {
                WriteFloat(words, ModernGraphicsShaders.RttScalars, 0, Float(program, "alpha", 1f));
                WriteBool(words, ModernGraphicsShaders.RttScalars, 1, Int(program, "use_mask") != 0);
                WriteFloat(words, ModernGraphicsShaders.RttScalars, 2,
                    Float(program, "view_width", _width));
                WriteFloat(words, ModernGraphicsShaders.RttScalars, 3,
                    Float(program, "view_height", _height));
                WriteVec(words, ModernGraphicsShaders.FadeColor, Data(program, "fade_color"));
                WriteBool(words, ModernGraphicsShaders.FragmentFlags3, 0,
                    _resources.IsFramebufferTexture(_resources.BoundTexture(0)));
            }

            UniformAllocation allocation = RentUniformBuffer((ulong)(words.Length * sizeof(uint)));
            _uniformBuffer = (WgpuBuffer*)allocation.Buffer;
            _uniformBufferOffset = allocation.Offset;
            fixed (uint* ptr = words)
            {
                WriteUniformBuffer(allocation, ptr, (nuint)(words.Length * sizeof(uint)));
            }
        }

        private void ClearOffscreenCore(ClearBufferMask mask)
        {
            if (_resources.DrawFramebuffer == 0 && !AcquireSurfaceTexture()) return;
            CoreTarget target = ResolveDrawTarget();
            bool clearColor = (mask & ClearBufferMask.ColorBufferBit) != 0;
            bool clearDepth = (mask & ClearBufferMask.DepthBufferBit) != 0;
            bool clearStencil = (mask & ClearBufferMask.StencilBufferBit) != 0;

            CommandEncoder* encoder = BeginCommands();
            int colorTargetCount = target.ColorTargetCount;
            var colors = stackalloc RenderPassColorAttachment[colorTargetCount];
            for (int colorIndex = 0; colorIndex < colorTargetCount; colorIndex++)
            {
                colors[colorIndex] = new RenderPassColorAttachment
                {
                    DepthSlice = uint.MaxValue, // WGPU_DEPTH_SLICE_UNDEFINED: this is a 2D view.
                    View = target.ColorViewAt(colorIndex),
                    ResolveTarget = null,
                    LoadOp = clearColor ? LoadOp.Clear : LoadOp.Load,
                    StoreOp = StoreOp.Store,
                    ClearValue = new WgpuColor
                    {
                        R = _clearColor.X,
                        G = _clearColor.Y,
                        B = _clearColor.Z,
                        A = _clearColor.W
                    }
                };
            }
            RenderPassDepthStencilAttachment depth = default;
            RenderPassDepthStencilAttachment* depthPtr = null;
            if (target.HasDepth)
            {
                depth = new RenderPassDepthStencilAttachment
                {
                    View = target.DepthView,
                    DepthLoadOp = clearDepth ? LoadOp.Clear : LoadOp.Load,
                    DepthStoreOp = StoreOp.Store,
                    DepthClearValue = 1f,
                    DepthReadOnly = false,
                    StencilLoadOp = target.HasStencil ? (clearStencil ? LoadOp.Clear : LoadOp.Load) : LoadOp.Undefined,
                    StencilStoreOp = target.HasStencil ? StoreOp.Store : StoreOp.Undefined,
                    StencilClearValue = unchecked((uint)_clearStencil),
                    StencilReadOnly = !target.HasStencil
                };
                depthPtr = &depth;
            }
            var descriptor = new RenderPassDescriptor
            {
                ColorAttachments = colors,
                ColorAttachmentCount = (uint)colorTargetCount,
                DepthStencilAttachment = depthPtr
            };
            RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(encoder, descriptor);
            _api.RenderPassEncoderEnd(pass);
            EndCommands();
            _api.RenderPassEncoderRelease(pass);

        }

        private void ReadOffscreenPixelsCore<T>(int x, int y, int width, int height,
            PixelFormat format, PixelType type, T[] pixels) where T : struct
        {
            if (type != PixelType.UnsignedByte || (format != PixelFormat.Rgb && format != PixelFormat.Rgba))
                throw new NotSupportedException("Modern offscreen readback supports RGB/RGBA unsigned-byte.");
            if (typeof(T) != typeof(byte))
                throw new NotSupportedException("Modern offscreen readback currently targets byte arrays.");

            CoreTarget target = ResolveReadTarget();
            ReadTexturePixels(target.ColorTexture, target.ColorFormat, target.Height,
                x, y, width, height, format, (byte[])(object)pixels);
        }

        private void ReadTexturePixels(WgpuTexture* texture, WgpuTextureFormat textureFormat,
            int textureHeight, int x, int y, int width, int height, PixelFormat format, byte[] pixels)
        {
            uint copyWidth = (uint)Math.Max(0, width);
            uint copyHeight = (uint)Math.Max(0, height);
            bool halfFloat = textureFormat == WgpuTextureFormat.Rgba16float;
            int sourcePixelBytes = halfFloat ? 8 : 4;
            uint rowBytes = copyWidth * (uint)sourcePixelBytes;
            uint paddedRow = (rowBytes + 255u) & ~255u;
            ulong total = (ulong)paddedRow * copyHeight;
            WgpuBuffer* readback = _api.DeviceCreateBuffer(_device.Device, new BufferDescriptor
            {
                Size = total,
                Usage = BufferUsage.CopyDst | BufferUsage.MapRead
            });
            CommandEncoder* encoder = BeginCommands();
            var source = new ImageCopyTexture
            {
                Texture = texture,
                MipLevel = 0,
                Origin = new Origin3D((uint)Math.Max(0, x),
                    (uint)Math.Max(0, textureHeight - y - height), 0),
                Aspect = TextureAspect.All
            };
            var destination = new ImageCopyBuffer
            {
                Buffer = readback,
                Layout = new TextureDataLayout
                {
                    BytesPerRow = paddedRow,
                    RowsPerImage = copyHeight
                }
            };
            var extent = new Extent3D(copyWidth, copyHeight, 1);
            _api.CommandEncoderCopyTextureToBuffer(encoder, &source, &destination, &extent);
            EndCommands();

            bool mappedSuccessfully = false;
            try
            {
                FlushCommands();
                ResetFrameBuffers();
                _mapStatus = BufferMapAsyncStatus.Unknown;
                _api.BufferMapAsync(readback, MapMode.Read, 0, (nuint)total,
                    new PfnBufferMapCallback((status, _) => _mapStatus = status), null);
                _device.Native.DevicePoll(_device.Device, true, null);
                if (_mapStatus != BufferMapAsyncStatus.Success)
                    throw new InvalidOperationException($"Modern offscreen readback map failed: {_mapStatus}.");

                mappedSuccessfully = true;
                byte* mapped = (byte*)_api.BufferGetConstMappedRange(readback, 0, (nuint)total);
                int components = format == PixelFormat.Rgb ? 3 : 4;
                bool bgra = textureFormat == WgpuTextureFormat.Bgra8Unorm
                    || textureFormat == WgpuTextureFormat.Bgra8UnormSrgb;
                int outputLength = Math.Min(pixels.Length, checked(width * height * components));
                int written = 0;
                for (int row = 0; row < height && written < outputLength; row++)
                {
                    byte* src = mapped + (height - 1 - row) * paddedRow;
                    for (int col = 0; col < width && written < outputLength; col++)
                    {
                        byte* texel = src + col * sourcePixelBytes;
                        byte b0 = halfFloat ? ReadHalfByte(texel) : texel[0];
                        byte b1 = halfFloat ? ReadHalfByte(texel + 2) : texel[1];
                        byte b2 = halfFloat ? ReadHalfByte(texel + 4) : texel[2];
                        byte b3 = halfFloat ? ReadHalfByte(texel + 6) : texel[3];
                        pixels[written++] = bgra ? b2 : b0;
                        if (written < outputLength) pixels[written++] = b1;
                        if (written < outputLength) pixels[written++] = bgra ? b0 : b2;
                        if (components == 4 && written < outputLength) pixels[written++] = b3;
                    }
                }
            }
            finally
            {
                if (mappedSuccessfully) _api.BufferUnmap(readback);

                _api.BufferRelease(readback);
            }
        }

        private static byte ReadHalfByte(byte* source)
        {
            float value = (float)BitConverter.UInt16BitsToHalf(*(ushort*)source);
            return float.IsNaN(value) ? (byte)0 : (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);
        }

        private void BlitFramebufferCore(int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            ClearBufferMask mask, BlitFramebufferFilter filter)
        {
            if ((_resources.ReadFramebuffer == 0 || _resources.DrawFramebuffer == 0)
                && !AcquireSurfaceTexture()) return;
            if ((mask & ClearBufferMask.ColorBufferBit) == 0)
            {
                return;
            }
            if ((mask & ~ClearBufferMask.ColorBufferBit) != 0)
            {
                throw new NotSupportedException(
                    "Modern framebuffer blits currently support the color buffer; Project Prime replay preview uses color only.");
            }

            CoreTarget sourceTarget = ResolveReadTarget();
            CoreTarget destinationTarget = ResolveDrawTarget();
            if (sourceTarget.ColorTexture == destinationTarget.ColorTexture)
                throw new InvalidOperationException("WebGPU blit source and destination must be different textures.");
            WgpuTexture* staging = null;
            TextureView* stagingView = null;
            try
            {
                if (_resources.ReadFramebuffer == 0)
                    sourceTarget = StageCopySource(sourceTarget, 0, 0, sourceTarget.Width, sourceTarget.Height,
                        out staging, out stagingView);
                BlitTargets(sourceTarget, destinationTarget, sourceX0, sourceY0, sourceX1, sourceY1,
                    destinationX0, destinationY0, destinationX1, destinationY1, filter, applyScissor: true);
            }
            finally
            {
                if (stagingView != null) _api.TextureViewRelease(stagingView);
                if (staging != null) _api.TextureRelease(staging);
            }
        }

        private void BlitTargets(CoreTarget sourceTarget, CoreTarget destinationTarget,
            int sourceX0, int sourceY0, int sourceX1, int sourceY1,
            int destinationX0, int destinationY0, int destinationX1, int destinationY1,
            BlitFramebufferFilter filter, bool applyScissor = false)
        {
            PipelineRecord pipeline = BlitPipeline(destinationTarget.ColorFormat);

            float dx0 = destinationX0 / (float)destinationTarget.Width * 2f - 1f;
            float dx1 = destinationX1 / (float)destinationTarget.Width * 2f - 1f;
            float dy0 = destinationY0 / (float)destinationTarget.Height * 2f - 1f;
            float dy1 = destinationY1 / (float)destinationTarget.Height * 2f - 1f;

            float u0 = sourceX0 / (float)sourceTarget.Width;
            float u1 = sourceX1 / (float)sourceTarget.Width;
            // GL source rectangles are bottom-origin; WebGPU texture rows are
            // top-origin. Convert the logical source rectangle explicitly.
            float v0 = 1f - sourceY0 / (float)sourceTarget.Height;
            float v1 = 1f - sourceY1 / (float)sourceTarget.Height;

            Span<float> vertices = stackalloc float[LegacyGeometryBatch.FloatsPerVertex * 4];
            Span<int> indices = stackalloc int[6] { 0, 1, 2, 0, 2, 3 };
            WriteBlitVertex(vertices, 0, dx0, dy0, u0, v0);
            WriteBlitVertex(vertices, 1, dx1, dy0, u1, v0);
            WriteBlitVertex(vertices, 2, dx1, dy1, u1, v1);
            WriteBlitVertex(vertices, 3, dx0, dy1, u0, v1);

            ulong vertexBytes = (ulong)(vertices.Length * sizeof(float));
            ulong indexBytes = (ulong)(indices.Length * sizeof(int));
            NativeGeometry geometryBuffers = PrepareGeometry(vertices, indices);
            WgpuBuffer* vertex = geometryBuffers.Vertex;
            WgpuBuffer* index = geometryBuffers.Index;

            FilterMode sampleFilter = filter == BlitFramebufferFilter.Linear
                ? FilterMode.Linear : FilterMode.Nearest;
            Silk.NET.WebGPU.Sampler* sampler = BlitSampler(sampleFilter);

            UniformAllocation uiUniform = RentUniformBuffer(16);
            _uiViewportBuffer = (WgpuBuffer*)uiUniform.Buffer;
            _uiViewportOffset = uiUniform.Offset;
            var viewport = new OpenTK.Mathematics.Vector4(1, 1, 0, 0);
            WriteUniformBuffer(uiUniform, &viewport, 16);
            var entries = stackalloc BindGroupEntry[3];
            entries[0] = new BindGroupEntry { Binding = 0, TextureView = sourceTarget.ColorView };
            entries[1] = new BindGroupEntry { Binding = 1, Sampler = sampler };
            entries[2] = new BindGroupEntry
            {
                Binding = 2, Buffer = _uiViewportBuffer, Offset = _uiViewportOffset, Size = 16
            };
            Span<nint> resources = stackalloc nint[5]
            {
                (nint)pipeline.Layout, (nint)sourceTarget.ColorView, (nint)sampler,
                (nint)_uiViewportBuffer, (nint)_uiViewportOffset
            };
            BindGroup* bindGroup = FrameBindGroup(
                new BindGroupDescriptor
                {
                    Layout = pipeline.Layout,
                    Entries = entries,
                    EntryCount = 3
                }, resources);

            CommandEncoder* encoder = BeginCommands();
            var color = new RenderPassColorAttachment
            {
                DepthSlice = uint.MaxValue, // WGPU_DEPTH_SLICE_UNDEFINED: this is a 2D view.
                View = destinationTarget.ColorView,
                ResolveTarget = null,
                LoadOp = LoadOp.Load,
                StoreOp = StoreOp.Store
            };
            var passDescriptor = new RenderPassDescriptor
            {
                ColorAttachments = &color,
                ColorAttachmentCount = 1
            };
            RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(encoder, passDescriptor);
            _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
            _api.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, null);
            _api.RenderPassEncoderSetVertexBuffer(pass, 0, vertex, geometryBuffers.VertexOffset, vertexBytes);
            _api.RenderPassEncoderSetIndexBuffer(pass, index, IndexFormat.Uint32, geometryBuffers.IndexOffset, indexBytes);
            _api.RenderPassEncoderSetViewport(pass, 0, 0,
                destinationTarget.Width, destinationTarget.Height, 0, 1);
            if (applyScissor) ApplyScissor(pass, destinationTarget.Width, destinationTarget.Height);
            _api.RenderPassEncoderDrawIndexed(pass, (uint)indices.Length, 1, 0, 0, 0);
            _api.RenderPassEncoderEnd(pass);
            EndCommands();

            _api.RenderPassEncoderRelease(pass);
        }

        private static void WriteBlitVertex(Span<float> vertices, int vertex,
            float x, float y, float u, float v)
        {
            int at = vertex * LegacyGeometryBatch.FloatsPerVertex;
            vertices[at + 0] = x;
            vertices[at + 1] = y;
            vertices[at + 2] = 0;
            vertices[at + 3] = 1;
            vertices[at + 4] = 1;
            vertices[at + 5] = 1;
            vertices[at + 6] = 1;
            vertices[at + 7] = 0;
            vertices[at + 8] = 0;
            vertices[at + 9] = 1;
            vertices[at + 10] = u;
            vertices[at + 11] = v;
            vertices[at + 12] = 0;
            vertices[at + 13] = 1;
            vertices[at + 14] = 1;
        }

        private Silk.NET.WebGPU.Sampler* BlitSampler(FilterMode filter)
        {
            if (filter == FilterMode.Linear)
            {
                if (_blitLinearSampler == null)
                    _blitLinearSampler = CreateBlitSampler(FilterMode.Linear);
                return _blitLinearSampler;
            }

            if (_blitNearestSampler == null)
                _blitNearestSampler = CreateBlitSampler(FilterMode.Nearest);
            return _blitNearestSampler;
        }

        private Silk.NET.WebGPU.Sampler* CreateBlitSampler(FilterMode filter) =>
            _api.DeviceCreateSampler(_device.Device, new SamplerDescriptor
            {
                MinFilter = filter,
                MagFilter = filter,
                MipmapFilter = MipmapFilterMode.Nearest,
                AddressModeU = AddressMode.ClampToEdge,
                AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge,
                MaxAnisotropy = 1
            });

        private PipelineRecord BlitPipeline(WgpuTextureFormat format)
        {
            if (_blitPipelines.TryGetValue(format, out PipelineRecord? cached)) return cached;
            long pipelineStart = PerformanceStart();

            var attributes = stackalloc VertexAttribute[3];
            attributes[0] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 0,
                ShaderLocation = 0
            };
            attributes[1] = new VertexAttribute
            {
                Format = VertexFormat.Float32x4,
                Offset = 3u * sizeof(float),
                ShaderLocation = 1
            };
            attributes[2] = new VertexAttribute
            {
                Format = VertexFormat.Float32x3,
                Offset = 10u * sizeof(float),
                ShaderLocation = 3
            };
            var vertexLayout = new VertexBufferLayout
            {
                Attributes = attributes,
                AttributeCount = 3,
                StepMode = VertexStepMode.Vertex,
                ArrayStride = (ulong)(LegacyGeometryBatch.FloatsPerVertex * sizeof(float))
            };
            var target = new ColorTargetState
            {
                Format = format,
                Blend = null,
                WriteMask = ColorWriteMask.All
            };
            nint vs = SilkMarshal.StringToPtr("vs_main");
            nint fs = SilkMarshal.StringToPtr("fs_main");
            try
            {
                var fragment = new FragmentState
                {
                    Module = _uiShader,
                    EntryPoint = (byte*)fs,
                    Targets = &target,
                    TargetCount = 1
                };
                var descriptor = new RenderPipelineDescriptor
                {
                    Vertex = new VertexState
                    {
                        Module = _uiShader,
                        EntryPoint = (byte*)vs,
                        Buffers = &vertexLayout,
                        BufferCount = 1
                    },
                    Primitive = new PrimitiveState
                    {
                        Topology = PrimitiveTopology.TriangleList,
                        StripIndexFormat = IndexFormat.Undefined,
                        FrontFace = FrontFace.Ccw,
                        CullMode = CullMode.None
                    },
                    Multisample = new MultisampleState
                    {
                        Count = 1,
                        Mask = ~0u,
                        AlphaToCoverageEnabled = false
                    },
                    Fragment = &fragment
                };
                RenderPipeline* native = _api.DeviceCreateRenderPipeline(_device.Device, descriptor);
                if (native == null)
                    throw new InvalidOperationException("Could not create modern framebuffer blit pipeline.");
                BindGroupLayout* layout = _api.RenderPipelineGetBindGroupLayout(native, 0);
                var result = new PipelineRecord { Pipeline = native, Layout = layout };
                _blitPipelines.Add(format, result);
                RecordPipelineCreation(pipelineStart);
                return result;
            }
            finally
            {
                SilkMarshal.Free(vs);
                SilkMarshal.Free(fs);
            }
        }

        private void CopyTexSubImage2DCore(TextureTarget target, int level, int xoffset, int yoffset,
            int x, int y, int width, int height)
        {
            if (_resources.ReadFramebuffer == 0 && !AcquireSurfaceTexture()) return;
            if (target != TextureTarget.Texture2D || level != 0)
                throw new NotSupportedException("Modern copy-to-texture supports base-level Texture2D.");

            int destinationId = _resources.BoundTexture(_resources.ActiveTextureUnit);
            if (destinationId == 0) throw new InvalidOperationException("No destination texture is bound.");
            NativeTexture destination = EnsureTexture(destinationId);
            CoreTarget sourceTarget = ResolveReadTarget();
            if (sourceTarget.ColorTexture == destination.Texture)
                throw new InvalidOperationException("WebGPU copy source and destination must be different textures.");

            if (width < 0 || height < 0 || x < 0 || y < 0 || xoffset < 0 || yoffset < 0
                || (long)x + width > sourceTarget.Width || (long)y + height > sourceTarget.Height
                || (long)xoffset + width > destination.Width || (long)yoffset + height > destination.Height)
                throw new ArgumentOutOfRangeException(nameof(width), "Copy rectangle is outside its source or destination.");
            if (width == 0 || height == 0) return;
            _resources.Texture(destinationId).FramebufferOrigin = true;
            if (sourceTarget.ColorFormat != destination.Format)
            {
                CopyConvertedColor(sourceTarget, destination, x, y, xoffset, yoffset, width, height);
                return;
            }
            var source = new ImageCopyTexture
            {
                Texture = sourceTarget.ColorTexture,
                MipLevel = 0,
                Origin = new Origin3D((uint)x,
                    (uint)Math.Max(0, sourceTarget.Height - y - height), 0),
                Aspect = TextureAspect.All
            };
            var dest = new ImageCopyTexture
            {
                Texture = destination.Texture,
                MipLevel = 0,
                Origin = new Origin3D((uint)xoffset, (uint)(destination.Height - yoffset - height), 0),
                Aspect = TextureAspect.All
            };
            var extent = new Extent3D((uint)width, (uint)height, 1);
            CommandEncoder* encoder = BeginCommands();
            _api.CommandEncoderCopyTextureToTexture(encoder, &source, &dest, &extent);
            EndCommands();

        }

        private void CopyConvertedColor(CoreTarget source, NativeTexture destination,
            int x, int y, int xoffset, int yoffset, int width, int height)
        {
            WgpuTexture* staging = null;
            TextureView* stagingView = null;
            try
            {
                if (_resources.ReadFramebuffer == 0)
                {
                    source = StageCopySource(source, x, y, width, height, out staging, out stagingView);
                    x = y = 0;
                }
                var target = new CoreTarget(destination.Texture, destination.View, destination.Format,
                    null, destination.Width, destination.Height);
                BlitTargets(source, target, x, y, x + width, y + height,
                    xoffset, yoffset, xoffset + width, yoffset + height, BlitFramebufferFilter.Nearest);
            }
            finally
            {
                if (stagingView != null) _api.TextureViewRelease(stagingView);
                if (staging != null) _api.TextureRelease(staging);
            }
        }

        private CoreTarget StageCopySource(CoreTarget source, int x, int y, int width, int height,
            out WgpuTexture* staging, out TextureView* view)
        {
            // Swapchain textures need not support sampling. The intermediate
            // preserves their format; the render pass performs any conversion.
            staging = _api.DeviceCreateTexture(_device.Device, new TextureDescriptor
            {
                Size = new Extent3D((uint)width, (uint)height, 1),
                Format = source.ColorFormat, Dimension = TextureDimension.Dimension2D,
                MipLevelCount = 1, SampleCount = 1,
                Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding
            });
            view = _api.TextureCreateView(staging, null);
            var from = new ImageCopyTexture
            {
                Texture = source.ColorTexture, Aspect = TextureAspect.All,
                Origin = new Origin3D((uint)x, (uint)(source.Height - y - height), 0)
            };
            var to = new ImageCopyTexture { Texture = staging, Aspect = TextureAspect.All };
            var size = new Extent3D((uint)width, (uint)height, 1);
            _api.CommandEncoderCopyTextureToTexture(BeginCommands(), &from, &to, &size);
            EndCommands();
            return new CoreTarget(staging, view, source.ColorFormat, null, width, height);
        }

        private static int Int(ModernGraphicsCompatState.ProgramRecord program, string name, int fallback = 0)
        {
            if (!program.Uniforms.TryGetValue(name, out ModernGraphicsCompatState.UniformValue value))
                return fallback;
            if (value.IsInteger) return value.IntValue;
            return value.Data is { Length: > 0 } data ? (int)data[0] : fallback;
        }

        private static float Float(ModernGraphicsCompatState.ProgramRecord program,
            string name, float fallback = 0)
        {
            if (!program.Uniforms.TryGetValue(name, out ModernGraphicsCompatState.UniformValue value))
                return fallback;
            if (value.IsInteger) return value.IntValue;
            return value.Data is { Length: > 0 } data ? data[0] : fallback;
        }

        private static float[]? Data(ModernGraphicsCompatState.ProgramRecord program, string name)
        {
            if (!program.Uniforms.TryGetValue(name, out ModernGraphicsCompatState.UniformValue value))
                return null;
            if (value.IsInteger) return new[] { (float)value.IntValue };
            return value.Data;
        }

        private static void WriteBool(uint[] words, int slot, int component, bool value)
            => WriteInt(words, slot, component, value ? 1 : 0);

        private static void WriteInt(uint[] words, int slot, int component, int value)
            => words[slot * 4 + component] = unchecked((uint)value);

        private static void WriteFloat(uint[] words, int slot, int component, float value)
            => words[slot * 4 + component] = BitConverter.SingleToUInt32Bits(value);

        private static void WriteVec(uint[] words, int slot, float[]? values)
        {
            if (values == null) return;
            for (int i = 0; i < Math.Min(4, values.Length); i++)
                WriteFloat(words, slot, i, values[i]);
        }

        private static void WriteMatrix(uint[] words, int slot, float[]? values)
        {
            if (values == null || values.Length < 16) return;
            for (int i = 0; i < 16; i++)
                WriteFloat(words, slot + i / 4, i % 4, values[i]);
        }

        private static void WriteMatrices(uint[] words, int slot, float[]? values, int matrices)
        {
            if (values == null) return;
            int count = Math.Min(values.Length, matrices * 16);
            for (int i = 0; i < count; i++)
                WriteFloat(words, slot + i / 4, i % 4, values[i]);
        }

        private static void WriteVecArray(uint[] words, int slot, float[]? values,
            int vectors, int components)
        {
            if (values == null) return;
            int count = Math.Min(vectors, values.Length / components);
            for (int i = 0; i < count; i++)
                for (int c = 0; c < components; c++)
                    WriteFloat(words, slot + i, c, values[i * components + c]);
        }

        private static void WriteIdentity(uint[] words, int slot)
        {
            for (int i = 0; i < 4; i++) WriteFloat(words, slot + i, i, 1f);
        }

        private static CompareFunction ToCompare(DepthFunction value)
        {
            return value switch
            {
                DepthFunction.Never => CompareFunction.Never,
                DepthFunction.Less => CompareFunction.Less,
                DepthFunction.Equal => CompareFunction.Equal,
                DepthFunction.Lequal => CompareFunction.LessEqual,
                DepthFunction.Greater => CompareFunction.Greater,
                DepthFunction.Notequal => CompareFunction.NotEqual,
                DepthFunction.Gequal => CompareFunction.GreaterEqual,
                _ => CompareFunction.Always
            };
        }

        private static CompareFunction ToCompare(StencilFunction value)
        {
            return value switch
            {
                StencilFunction.Never => CompareFunction.Never,
                StencilFunction.Less => CompareFunction.Less,
                StencilFunction.Equal => CompareFunction.Equal,
                StencilFunction.Lequal => CompareFunction.LessEqual,
                StencilFunction.Greater => CompareFunction.Greater,
                StencilFunction.Notequal => CompareFunction.NotEqual,
                StencilFunction.Gequal => CompareFunction.GreaterEqual,
                _ => CompareFunction.Always
            };
        }

        private static StencilOperation ToStencil(OpenTK.Graphics.OpenGL.StencilOp value)
        {
            return value switch
            {
                OpenTK.Graphics.OpenGL.StencilOp.Zero => StencilOperation.Zero,
                OpenTK.Graphics.OpenGL.StencilOp.Replace => StencilOperation.Replace,
                OpenTK.Graphics.OpenGL.StencilOp.Incr => StencilOperation.IncrementClamp,
                OpenTK.Graphics.OpenGL.StencilOp.Decr => StencilOperation.DecrementClamp,
                OpenTK.Graphics.OpenGL.StencilOp.Invert => StencilOperation.Invert,
                OpenTK.Graphics.OpenGL.StencilOp.IncrWrap => StencilOperation.IncrementWrap,
                OpenTK.Graphics.OpenGL.StencilOp.DecrWrap => StencilOperation.DecrementWrap,
                _ => StencilOperation.Keep
            };
        }

        private static CullMode ToCull(TriangleFace value)
        {
            return value switch
            {
                TriangleFace.Front => CullMode.Front,
                TriangleFace.Back => CullMode.Back,
                _ => CullMode.None
            };
        }

        private static BlendOperation ToBlendOperation(BlendEquationMode value)
        {
            return value switch
            {
                BlendEquationMode.FuncSubtract => BlendOperation.Subtract,
                BlendEquationMode.FuncReverseSubtract => BlendOperation.ReverseSubtract,
                _ => BlendOperation.Add
            };
        }
    }
}
#endif
