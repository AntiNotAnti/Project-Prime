#if MPHREAD_RMLUI_POC && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using MphRead.Mods.Launcher.RmlUi.Render;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using WgpuBuffer = Silk.NET.WebGPU.Buffer;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render;

internal sealed unsafe partial class ModernGraphicsCompat
{
    internal readonly record struct RmlUiResourceSample(int Geometry,int Buffers,long BufferCapacityBytes,int Textures,long TextureCapacityBytes,int StencilTextures,long EstimatedStencilBytes,int Pipelines,int ShaderModules);
    internal static RmlUiResourceSample RmlUiResources
    {
        get
        {
            var s=Current;long buffers=0,textures=0;int bufferCount=0;
            foreach(var item in s._rmlGeometry.Values)
            {
                if(item.Native.Vertex!=null){bufferCount++;buffers=checked(buffers+(long)item.Native.VertexCapacity);}
                if(item.Native.Index!=null){bufferCount++;buffers=checked(buffers+(long)item.Native.IndexCapacity);}
            }
            foreach(var item in s._rmlTextures.Values)textures=checked(textures+(long)item.Source.Width*item.Source.Height*4);
            return new(s._rmlGeometry.Count,bufferCount,buffers,s._rmlTextures.Count,textures,s._rmlStencil==null?0:1,
                s._rmlStencil==null?0:(long)s._width*s._height*4,s._rmlPipelines.Count,s._rmlShader==null?0:1);
        }
    }
    private enum RmlPipelineMode { Draw, DrawClipped, MaskSet, MaskIntersect }
    private readonly record struct RmlPipelineKey(WgpuTextureFormat Format, RmlPipelineMode Mode);
    private sealed class RmlGeometryResource
    {
        internal RmlUiDrawGeometry Source = null!;
        internal NativeGeometry Native = new();
    }
    private sealed class RmlTextureResource
    {
        internal RmlUiDrawTexture Source = null!;
        internal NativeTexture Native = new();
    }
    private readonly Dictionary<ulong, RmlGeometryResource> _rmlGeometry = new();
    private readonly Dictionary<ulong, RmlTextureResource> _rmlTextures = new();
    private readonly Dictionary<RmlPipelineKey, PipelineRecord> _rmlPipelines = new();
    private ShaderModule* _rmlShader;
    private NativeRenderbuffer? _rmlStencil;
    private ulong _rmlGeneration;
    private RmlUiDrawListFrame? _rmlPreparedFrame;

    private const string RmlShader = @"
struct Input {
    @location(0) position: vec3<f32>,
    @location(1) color: vec4<f32>,
    @location(3) uv: vec3<f32>,
};
struct Output {
    @builtin(position) position: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) uv: vec2<f32>,
};
struct Parameters {
    transform: mat4x4<f32>,
    projection: vec4<f32>,
    translation: vec4<f32>,
};
@group(0) @binding(0) var picture: texture_2d<f32>;
@group(0) @binding(1) var picture_sampler: sampler;
@group(0) @binding(2) var<uniform> parameters: Parameters;
@vertex
fn vs_main(input: Input) -> Output {
    var output: Output;
    let p = parameters.transform * vec4<f32>(input.position.xy + parameters.translation.xy, 0.0, 1.0);
    // RmlUi's orthographic depth range is [-10000, 10000]. WebGPU uses [0, 1].
    output.position = vec4<f32>(p.xy * parameters.projection.xy + parameters.projection.zw * p.w,
        p.z * 0.00005 + 0.5 * p.w, p.w);
    output.color = input.color;
    output.uv = input.uv.xy;
    return output;
}
@fragment
fn fs_main(input: Output) -> @location(0) vec4<f32> {
    return textureSample(picture, picture_sampler, input.uv) * input.color;
}
@fragment
fn fs_srgb(input: Output) -> @location(0) vec4<f32> {
    let c = textureSample(picture, picture_sampler, input.uv) * input.color;
    let straight = c.rgb / max(c.a, 0.00001);
    let linear = select(pow((straight + vec3<f32>(0.055)) / 1.055, vec3<f32>(2.4)),
        straight / 12.92, straight <= vec3<f32>(0.04045));
    return vec4<f32>(linear * c.a, c.a);
}
";

    internal static void DrawRmlUi(RmlUiDrawListFrame frame, int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        Current.DrawRmlUiCore(frame, width, height);
    }

    internal static void ReleaseRmlUiFrames()
    {
        // UI shutdown cannot reconstruct a lost device or release identifiers
        // against a replacement owner. Renderer teardown handles lost owners.
        ModernGraphicsCompat? owner = _current;
        if (owner == null || owner._disposed || owner._device.IsLost || _deviceRecoveryFailure != null) return;
        owner.ReleaseRmlFrameResources();
        owner._rmlGeneration = 0;
    }

    private void DrawRmlUiCore(RmlUiDrawListFrame frame, int width, int height)
    {
        if (!AcquireSurfaceTexture()) return;
        if (_width != width || _height != height)
            throw new InvalidOperationException("RmlUi compositor dimensions must match the engine framebuffer.");
        if (_rmlGeneration != frame.Generation)
        {
            ReleaseRmlFrameResources();
            _rmlGeneration = frame.Generation;
        }
        PrepareRmlResources(frame);
        TextureView* stencilView = EnsureRmlStencil();
        var attachment = new RenderPassColorAttachment
        {
            DepthSlice = uint.MaxValue, View = _surfaceView,
            LoadOp = LoadOp.Load, StoreOp = StoreOp.Store
        };
        var stencil = new RenderPassDepthStencilAttachment
        {
            View = stencilView,
            DepthLoadOp = LoadOp.Clear, DepthStoreOp = StoreOp.Discard, DepthClearValue = 1,
            StencilLoadOp = LoadOp.Clear, StencilStoreOp = StoreOp.Discard, StencilClearValue = 0
        };
        var descriptor = new RenderPassDescriptor
        {
            ColorAttachments = &attachment, ColorAttachmentCount = 1, DepthStencilAttachment = &stencil
        };
        RenderPassEncoder* pass = _api.CommandEncoderBeginRenderPass(BeginCommands(), descriptor);
        if (pass == null) throw new InvalidOperationException("Could not begin RmlUi terminal composite pass.");
        bool scissorEnabled = false, clipEnabled = false;
        int left = 0, top = 0, scissorWidth = width, scissorHeight = height;
        uint clipReference = 1;
        Span<float> parameters = stackalloc float[24];
        parameters.Clear();
        SetRmlIdentity(parameters);
        parameters[16] = 2f / width; parameters[17] = -2f / height;
        parameters[18] = -1; parameters[19] = 1;
        _api.RenderPassEncoderSetViewport(pass, 0, 0, _width, _height, 0, 1);
        try
        {
            foreach (RmlUiDrawCommand command in frame.Commands)
            {
                switch (command.Kind)
                {
                    case RmlUiDrawCommandKind.EnableScissor: scissorEnabled = command.Enabled != 0; continue;
                    case RmlUiDrawCommandKind.Scissor:
                        left = command.X; top = command.Y; scissorWidth = command.Width; scissorHeight = command.Height; continue;
                    case RmlUiDrawCommandKind.EnableClipMask: clipEnabled = command.Enabled != 0; continue;
                    case RmlUiDrawCommandKind.Transform:
                        if (command.Enabled == 0) SetRmlIdentity(parameters);
                        else for (int i = 0; i < 16; i++) parameters[i] = command.Transform[i];
                        continue;
                    case RmlUiDrawCommandKind.Geometry:
                    case RmlUiDrawCommandKind.ClipMask:
                        break;
                    default: throw new InvalidOperationException("Unknown RmlUi draw command.");
                }
                var clip = RmlScissor(scissorEnabled ? left : 0, scissorEnabled ? top : 0,
                    scissorEnabled ? scissorWidth : width, scissorEnabled ? scissorHeight : height, width, height);
                bool visible = clip.Width != 0 && clip.Height != 0;
                if (visible) _api.RenderPassEncoderSetScissorRect(pass, clip.X, clip.Y, clip.Width, clip.Height);
                RmlPipelineMode mode;
                uint reference;
                ulong texture = command.Texture;
                if (command.Kind == RmlUiDrawCommandKind.ClipMask)
                {
                    texture = 0;
                    if (command.Operation == RmlUiClipOperation.Intersect)
                    {
                        if (clipReference >= 255)
                            throw new NotSupportedException("RmlUi clip nesting exceeds the eight-bit stencil mask.");
                        reference = clipReference;
                        clipReference++;
                        mode = RmlPipelineMode.MaskIntersect;
                    }
                    else
                    {
                        bool inverse = command.Operation == RmlUiClipOperation.SetInverse;
                        if (visible) ClearRmlClip(pass, parameters, width, height, inverse ? 1u : 0u);
                        clipReference = 1;
                        reference = inverse ? 0u : 1u;
                        mode = RmlPipelineMode.MaskSet;
                    }
                }
                else
                {
                    mode = clipEnabled ? RmlPipelineMode.DrawClipped : RmlPipelineMode.Draw;
                    reference = clipReference;
                }
                if (!visible) continue;
                if (!_rmlGeometry.TryGetValue(command.Geometry, out RmlGeometryResource? geometry))
                    throw new InvalidOperationException("RmlUi referenced missing compiled geometry.");
                parameters[20] = command.TranslationX; parameters[21] = command.TranslationY;
                DrawRmlGeometry(pass, geometry.Native, geometry.Source.Indices.Length,
                    texture, mode, reference, parameters);
            }
        }
        finally
        {
            _api.RenderPassEncoderEnd(pass);
            _api.RenderPassEncoderRelease(pass);
            EndCommands();
        }
    }

    internal static (uint X, uint Y, uint Width, uint Height) RmlScissor(
        int x, int y, int width, int height, int targetWidth, int targetHeight)
    {
        long left = Math.Clamp((long)x, 0, targetWidth);
        long right = Math.Clamp((long)x + Math.Max(0, width), 0, targetWidth);
        long top = Math.Clamp((long)y, 0, targetHeight);
        long bottom = Math.Clamp((long)y + Math.Max(0, height), 0, targetHeight);
        return ((uint)left, (uint)top, (uint)(right - left), (uint)(bottom - top));
    }

    private static void SetRmlIdentity(Span<float> parameters)
    {
        parameters[..16].Clear();
        parameters[0] = parameters[5] = parameters[10] = parameters[15] = 1;
    }

    private TextureView* EnsureRmlStencil()
    {
        if (_rmlStencil != null && _rmlStencil.Width == _width && _rmlStencil.Height == _height)
            return _rmlStencil.View;
        if (_rmlStencil != null) ReleaseNativeRenderbuffer(_rmlStencil);
        _rmlStencil = null;
        var texture = _api.DeviceCreateTexture(_device.Device, new TextureDescriptor
        {
            Size = new Extent3D(_width, _height, 1), Format = WgpuTextureFormat.Depth24PlusStencil8,
            Usage = TextureUsage.RenderAttachment, MipLevelCount = 1, SampleCount = 1,
            Dimension = TextureDimension.Dimension2D
        });
        if (texture == null) throw new InvalidOperationException("Could not allocate RmlUi stencil attachment.");
        _rmlStencil = new NativeRenderbuffer
        {
            Texture = texture, View = _api.TextureCreateView(texture, null),
            Width = (int)_width, Height = (int)_height
        };
        return _rmlStencil.View;
    }

    private void PrepareRmlResources(RmlUiDrawListFrame frame)
    {
        if (ReferenceEquals(_rmlPreparedFrame, frame)) return;
        PruneRmlResources(_rmlGeometry, frame.Geometry, resource => ReleaseGeometry(resource.Native));
        PruneRmlResources(_rmlTextures, frame.Textures, resource => ReleaseNativeTexture(resource.Native));
        foreach (var item in frame.Geometry)
        {
            if (_rmlGeometry.TryGetValue(item.Key, out RmlGeometryResource? existing))
            {
                if (ReferenceEquals(existing.Source, item.Value)) continue;
                ReleaseGeometry(existing.Native);
            }
            float[] vertices = RmlVertices(item.Value.Vertices);
            var resource = new RmlGeometryResource { Source = item.Value };
            ulong vertexBytes = checked((ulong)vertices.Length * sizeof(float));
            ulong indexBytes = checked((ulong)item.Value.Indices.Length * sizeof(int));
            GrowBuffer(ref resource.Native.Vertex, ref resource.Native.VertexCapacity, vertexBytes, BufferUsage.Vertex);
            GrowBuffer(ref resource.Native.Index, ref resource.Native.IndexCapacity, indexBytes, BufferUsage.Index);
            fixed (float* pointer = vertices)
                WriteProfiledBuffer(resource.Native.Vertex, 0, pointer, checked((nuint)vertexBytes));
            fixed (int* pointer = item.Value.Indices)
                WriteProfiledBuffer(resource.Native.Index, 0, pointer, checked((nuint)indexBytes));
            _rmlGeometry[item.Key] = resource;
        }
        foreach (var item in frame.Textures)
        {
            if (_rmlTextures.TryGetValue(item.Key, out RmlTextureResource? existing))
            {
                if (ReferenceEquals(existing.Source, item.Value)) continue;
                ReleaseNativeTexture(existing.Native);
            }
            RmlUiDrawTexture source = item.Value;
            if ((uint)source.Width > _device.MaxTextureDimension2D || (uint)source.Height > _device.MaxTextureDimension2D)
                throw new InvalidOperationException("RmlUi font atlas exceeds the graphics device texture limit.");
            var native = new NativeTexture
            {
                Width = source.Width, Height = source.Height, MipCount = 1, Format = WgpuTextureFormat.Rgba8Unorm,
                Texture = _api.DeviceCreateTexture(_device.Device, new TextureDescriptor
                {
                    Size = new Extent3D((uint)source.Width, (uint)source.Height, 1), Format = WgpuTextureFormat.Rgba8Unorm,
                    Usage = TextureUsage.CopyDst | TextureUsage.TextureBinding,
                    MipLevelCount = 1, SampleCount = 1, Dimension = TextureDimension.Dimension2D
                })
            };
            if (native.Texture == null) throw new InvalidOperationException("Could not allocate RmlUi texture.");
            native.View = native.SampleView = _api.TextureCreateView(native.Texture, null);
            native.Sampler = _api.DeviceCreateSampler(_device.Device, new SamplerDescriptor
            {
                MinFilter = FilterMode.Linear, MagFilter = FilterMode.Linear, MipmapFilter = MipmapFilterMode.Nearest,
                AddressModeU = AddressMode.ClampToEdge, AddressModeV = AddressMode.ClampToEdge,
                AddressModeW = AddressMode.ClampToEdge, MaxAnisotropy = 1
            });
            _rmlTextures[item.Key] = new RmlTextureResource { Source = source, Native = native };
            UploadTexture(native, source.Pixels);
        }
        _rmlPreparedFrame = frame;
    }

    private static void PruneRmlResources<T, TSource>(Dictionary<ulong, T> resources,
        IReadOnlyDictionary<ulong, TSource> active, Action<T> release)
    {
        List<ulong>? removed = null;
        foreach (var item in resources)
            if (!active.ContainsKey(item.Key)) (removed ??= new()).Add(item.Key);
        if (removed != null) foreach (ulong key in removed) { release(resources[key]); resources.Remove(key); }
    }

    private static float[] RmlVertices(RmlUiVertex[] source)
    {
        var result = new float[checked(source.Length * LegacyGeometryBatch.FloatsPerVertex)];
        for (int i = 0; i < source.Length; i++)
        {
            RmlUiVertex vertex = source[i]; int at = i * LegacyGeometryBatch.FloatsPerVertex;
            result[at] = vertex.X; result[at + 1] = vertex.Y;
            result[at + 3] = (vertex.Color & 255) / 255f;
            result[at + 4] = ((vertex.Color >> 8) & 255) / 255f;
            result[at + 5] = ((vertex.Color >> 16) & 255) / 255f;
            result[at + 6] = (vertex.Color >> 24) / 255f;
            result[at + 10] = vertex.U; result[at + 11] = vertex.V;
        }
        return result;
    }

    private void ClearRmlClip(RenderPassEncoder* pass, ReadOnlySpan<float> saved, int width, int height, uint value)
    {
        Span<float> parameters = stackalloc float[24];
        saved.CopyTo(parameters); SetRmlIdentity(parameters);
        parameters[20] = parameters[21] = 0;
        Span<float> vertices = stackalloc float[LegacyGeometryBatch.FloatsPerVertex * 3];
        vertices.Clear();
        vertices[LegacyGeometryBatch.FloatsPerVertex] = width * 2f;
        vertices[LegacyGeometryBatch.FloatsPerVertex * 2 + 1] = height * 2f;
        Span<int> indices = stackalloc int[3] { 0, 1, 2 };
        DrawRmlGeometry(pass, PrepareGeometry(vertices, indices), 3, 0,
            RmlPipelineMode.MaskSet, value, parameters);
    }

    private void DrawRmlGeometry(RenderPassEncoder* pass, NativeGeometry geometry, int indexCount,
        ulong texture, RmlPipelineMode mode, uint reference, ReadOnlySpan<float> parameters)
    {
        PipelineRecord pipeline = RmlPipeline(mode);
        TextureView* view = _whiteView; Sampler* sampler = _whiteSampler;
        if (texture != 0)
        {
            if (!_rmlTextures.TryGetValue(texture, out RmlTextureResource? resource))
                throw new InvalidOperationException("RmlUi referenced a released texture.");
            view = resource.Native.SampleView; sampler = resource.Native.Sampler;
        }
        UniformAllocation uniform = RentUniformBuffer(96);
        fixed (float* pointer = parameters) WriteUniformBuffer(uniform, pointer, 96);
        var entries = stackalloc BindGroupEntry[3];
        entries[0] = new BindGroupEntry { Binding = 0, TextureView = view };
        entries[1] = new BindGroupEntry { Binding = 1, Sampler = sampler };
        entries[2] = new BindGroupEntry { Binding = 2, Buffer = (WgpuBuffer*)uniform.Buffer, Offset = uniform.Offset, Size = 96 };
        Span<nint> resources = stackalloc nint[5]
        {
            (nint)pipeline.Layout, (nint)view, (nint)sampler, uniform.Buffer, (nint)uniform.Offset
        };
        BindGroup* group = FrameBindGroup(new BindGroupDescriptor
        {
            Layout = pipeline.Layout, Entries = entries, EntryCount = 3
        }, resources);
        _api.RenderPassEncoderSetPipeline(pass, pipeline.Pipeline);
        _api.RenderPassEncoderSetBindGroup(pass, 0, group, 0, null);
        _api.RenderPassEncoderSetStencilReference(pass, reference);
        _api.RenderPassEncoderSetVertexBuffer(pass, 0, geometry.Vertex, geometry.VertexOffset, geometry.VertexCapacity);
        _api.RenderPassEncoderSetIndexBuffer(pass, geometry.Index, IndexFormat.Uint32, geometry.IndexOffset,
            checked((ulong)indexCount * sizeof(int)));
        _api.RenderPassEncoderDrawIndexed(pass, (uint)indexCount, 1, 0, 0, 0);
    }

    private PipelineRecord RmlPipeline(RmlPipelineMode mode)
    {
        var key = new RmlPipelineKey(_surfaceFormat, mode);
        if (_rmlPipelines.TryGetValue(key, out PipelineRecord? existing)) return existing;
        if (_rmlShader == null)
        {
            nint code = SilkMarshal.StringToPtr(RmlShader);
            try
            {
                var wgsl = new ShaderModuleWGSLDescriptor
                {
                    Code = (byte*)code, Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor }
                };
                _rmlShader = _api.DeviceCreateShaderModule(_device.Device, new ShaderModuleDescriptor
                {
                    NextInChain = (ChainedStruct*)&wgsl
                });
                if (_rmlShader == null) throw new InvalidOperationException("Could not compile RmlUi WebGPU shader.");
            }
            finally { SilkMarshal.Free(code); }
        }
        var attributes = stackalloc VertexAttribute[3];
        attributes[0] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new VertexAttribute { Format = VertexFormat.Float32x4, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new VertexAttribute { Format = VertexFormat.Float32x3, Offset = 40, ShaderLocation = 3 };
        var layout = new VertexBufferLayout
        {
            Attributes = attributes, AttributeCount = 3, StepMode = VertexStepMode.Vertex,
            ArrayStride = LegacyGeometryBatch.FloatsPerVertex * sizeof(float)
        };
        bool mask = mode is RmlPipelineMode.MaskSet or RmlPipelineMode.MaskIntersect;
        BlendState blend = new()
        {
            Color = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add },
            Alpha = new BlendComponent { SrcFactor = BlendFactor.One, DstFactor = BlendFactor.OneMinusSrcAlpha, Operation = BlendOperation.Add }
        };
        var target = new ColorTargetState { Format = _surfaceFormat, Blend = mask ? null : &blend, WriteMask = mask ? ColorWriteMask.None : ColorWriteMask.All };
        var stencil = new StencilFaceState
        {
            Compare = mode is RmlPipelineMode.DrawClipped or RmlPipelineMode.MaskIntersect
                ? CompareFunction.Equal : CompareFunction.Always,
            FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep,
            PassOp = mode == RmlPipelineMode.MaskSet ? StencilOperation.Replace
                : mode == RmlPipelineMode.MaskIntersect ? StencilOperation.IncrementClamp : StencilOperation.Keep
        };
        var depth = new DepthStencilState
        {
            Format = WgpuTextureFormat.Depth24PlusStencil8, DepthCompare = CompareFunction.Always,
            DepthWriteEnabled = false, StencilFront = stencil, StencilBack = stencil,
            StencilReadMask = 255, StencilWriteMask = mask ? 255u : 0
        };
        nint vs = SilkMarshal.StringToPtr("vs_main");
        nint fs = SilkMarshal.StringToPtr(_surfaceFormat is WgpuTextureFormat.Rgba8UnormSrgb or WgpuTextureFormat.Bgra8UnormSrgb ? "fs_srgb" : "fs_main");
        try
        {
            var fragment = new FragmentState { Module = _rmlShader, EntryPoint = (byte*)fs, Targets = &target, TargetCount = 1 };
            var descriptor = new RenderPipelineDescriptor
            {
                Vertex = new VertexState { Module = _rmlShader, EntryPoint = (byte*)vs, Buffers = &layout, BufferCount = 1 },
                Primitive = new PrimitiveState { Topology = PrimitiveTopology.TriangleList, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                DepthStencil = &depth,
                Multisample = new MultisampleState { Count = 1, Mask = ~0u }, Fragment = &fragment
            };
            RenderPipeline* pipeline = _api.DeviceCreateRenderPipeline(_device.Device, descriptor);
            if (pipeline == null) throw new InvalidOperationException("Could not create RmlUi GPU pipeline.");
            var result = new PipelineRecord { Pipeline = pipeline, Layout = _api.RenderPipelineGetBindGroupLayout(pipeline, 0) };
            _rmlPipelines.Add(key, result); return result;
        }
        finally { SilkMarshal.Free(vs); SilkMarshal.Free(fs); }
    }

    private void ReleaseRmlFrameResources()
    {
        _rmlPreparedFrame = null;
        foreach (var resource in _rmlGeometry.Values) ReleaseGeometry(resource.Native);
        foreach (var resource in _rmlTextures.Values) ReleaseNativeTexture(resource.Native);
        _rmlGeometry.Clear(); _rmlTextures.Clear();
    }

    private void DisposeRmlUiResources()
    {
        ReleaseRmlFrameResources();
        foreach (PipelineRecord pipeline in _rmlPipelines.Values)
        {
            if (pipeline.Layout != null) _api.BindGroupLayoutRelease(pipeline.Layout);
            if (pipeline.Pipeline != null) _api.RenderPipelineRelease(pipeline.Pipeline);
        }
        _rmlPipelines.Clear();
        if (_rmlStencil != null) ReleaseNativeRenderbuffer(_rmlStencil);
        _rmlStencil = null;
        if (_rmlShader != null) _api.ShaderModuleRelease(_rmlShader);
        _rmlShader = null;
    }
}
#endif
