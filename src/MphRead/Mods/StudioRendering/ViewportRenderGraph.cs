#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using MphRead.Mods.MapEditor;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using GpuTexture = Silk.NET.WebGPU.Texture;
using GpuTextureFormat = Silk.NET.WebGPU.TextureFormat;
using GpuBuffer = Silk.NET.WebGPU.Buffer;

namespace MphRead.Mods.StudioRendering;

/// <summary>Direct retained creator graph: geometry, selection edges, integer picking and explicit capture.</summary>
public sealed unsafe partial class StudioRenderDevice
{
    private RenderPipeline* _fillPipeline, _edgePipeline, _pickPipeline;
    private RenderPipeline* _bgraFillPipeline, _bgraEdgePipeline;
    private BindGroupLayout* _materialLayout;
    private ShaderModule* _shader;
    private long _lastVisiblePrimitives;
    private const string Shader = """
struct Params { mvp: mat4x4<f32>, model: mat4x4<f32>, view: mat4x4<f32>, uv: vec4<f32>, tint: vec4<f32>, features: vec4<f32> };
@group(0) @binding(0) var<uniform> p: Params;
@group(0) @binding(1) var image: texture_2d<f32>;
@group(0) @binding(2) var sample_image: sampler;
@group(0) @binding(3) var normal_image: texture_2d<f32>;
@group(0) @binding(4) var normal_sampler: sampler;
@group(0) @binding(5) var specular_image: texture_2d<f32>;
@group(0) @binding(6) var specular_sampler: sampler;
@group(0) @binding(7) var emissive_image: texture_2d<f32>;
@group(0) @binding(8) var emissive_sampler: sampler;
var<private> surface_position_1: vec3<f32>;
var<private> surface_normal_1: vec3<f32>;
var<private> texcoord_1: vec2<f32>;
fn prime_sample_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(image,sample_image,uv); }
fn prime_sample_normal_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(normal_image,normal_sampler,uv); }
fn prime_sample_specular_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(specular_image,specular_sampler,uv); }
fn prime_sample_emissive_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(emissive_image,emissive_sampler,uv); }
struct Input { @location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) shade: f32, @location(4) id: u32 };
struct Output { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>, @location(1) shade: f32, @location(2) @interpolate(flat) id: u32,
 @location(3) world: vec3<f32>, @location(4) normal: vec3<f32> };
@vertex fn vertex(input: Input) -> Output {
 var o: Output; o.position = p.mvp * vec4<f32>(input.position,1.0);
 if(p.uv.w > 0.5 && p.uv.w < 1.5) {
   o.position=vec4<f32>(o.position.xy+input.uv*p.uv.xy*o.position.w,o.position.z-0.00001*o.position.w,o.position.w);
 }
 if(p.uv.w > 1.5) {
   let end=p.mvp*vec4<f32>(input.normal,1.0);
   let direction=normalize((end.xy/end.w-o.position.xy/o.position.w)/p.uv.xy);
   let perpendicular=vec2<f32>(-direction.y,direction.x);
   let point=mix(o.position,end,input.uv.x);
   o.position=vec4<f32>(point.xy+perpendicular*input.uv.y*p.uv.xy*point.w,point.z-0.00001*point.w,point.w);
 }
 o.uv = input.uv * p.uv.xy; o.shade = input.shade; o.id = input.id;
 o.world=(p.model*vec4<f32>(input.position,1.0)).xyz;
 let cofactor=mat3x3<f32>(cross(p.model[1].xyz,p.model[2].xyz),cross(p.model[2].xyz,p.model[0].xyz),cross(p.model[0].xyz,p.model[1].xyz));
 let determinant=dot(p.model[0].xyz,cofactor[0]);
 if(abs(determinant)>0.0000001){o.normal=normalize((cofactor*input.normal)/determinant);}
 else{o.normal=normalize(input.normal);}
 return o;
}
@fragment fn fragment(input: Output) -> @location(0) vec4<f32> {
 let tex = textureSample(image, sample_image, input.uv);
 var color = select(vec4<f32>(vec3<f32>(input.shade), 1.0), tex, p.uv.z > 0.5);
 surface_position_1=input.world;surface_normal_1=input.normal;texcoord_1=input.uv;
 apply_material_lighting(&color); return color * p.tint;
}
@fragment fn pick(input: Output) -> @location(0) u32 { return input.id; }
""";

    private void PrepareGraph()
    {
        if (_fillPipeline != null) return;
        nint source = SilkMarshal.StringToPtr(Shader + CreatorMaterialShader.Functions);
        try
        {
            var wgsl = new ShaderModuleWGSLDescriptor { Code = (byte*)source, Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor } };
            var descriptor = new ShaderModuleDescriptor { NextInChain = (ChainedStruct*)&wgsl };
            _shader = Api.DeviceCreateShaderModule(Device.Device, &descriptor);
            _fillPipeline = Pipeline(PrimitiveTopology.TriangleList, GpuTextureFormat.Rgba8Unorm, "fragment");
            _edgePipeline = Pipeline(PrimitiveTopology.LineList, GpuTextureFormat.Rgba8Unorm, "fragment");
            _pickPipeline = Pipeline(PrimitiveTopology.TriangleList, GpuTextureFormat.R32Uint, "pick");
            _materialLayout = Api.RenderPipelineGetBindGroupLayout(_fillPipeline, 0);
            Device.ThrowIfFailed();
        }
        finally { SilkMarshal.Free(source); }
    }
    private RenderPipeline* Pipeline(PrimitiveTopology topology, GpuTextureFormat format, string fragmentName)
    {
        var attributes = stackalloc VertexAttribute[5];
        attributes[0] = new() { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new() { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new() { Format = VertexFormat.Float32x2, Offset = 24, ShaderLocation = 2 };
        attributes[3] = new() { Format = VertexFormat.Float32, Offset = 32, ShaderLocation = 3 };
        attributes[4] = new() { Format = VertexFormat.Uint32, Offset = 36, ShaderLocation = 4 };
        var layout = new VertexBufferLayout { Attributes = attributes, AttributeCount = 5, ArrayStride = 40, StepMode = VertexStepMode.Vertex };
        var color = new ColorTargetState { Format = format, WriteMask = ColorWriteMask.All };
        var depth = new DepthStencilState { Format = GpuTextureFormat.Depth32float, DepthWriteEnabled = topology == PrimitiveTopology.TriangleList,
            DepthCompare = CompareFunction.LessEqual, StencilReadMask = 0, StencilWriteMask = 0,
            StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
            StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep } };
        nint vs = SilkMarshal.StringToPtr("vertex"), fs = SilkMarshal.StringToPtr(fragmentName);
        try
        {
            var fragment = new FragmentState { Module = _shader, EntryPoint = (byte*)fs, TargetCount = 1, Targets = &color };
            var descriptor = new RenderPipelineDescriptor {
                Vertex = new VertexState { Module = _shader, EntryPoint = (byte*)vs, Buffers = &layout, BufferCount = 1 },
                Primitive = new PrimitiveState { Topology = topology, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue }, Fragment = &fragment, DepthStencil = &depth };
            var pipeline = Api.DeviceCreateRenderPipeline(Device.Device, &descriptor);
            if (pipeline == null) throw new InvalidOperationException("Studio viewport pipeline creation failed.");
            return pipeline;
        }
        finally { SilkMarshal.Free(vs); SilkMarshal.Free(fs); }
    }
    internal StudioViewportImage Render(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame)
    {
        var watch = Stopwatch.StartNew();
        Prepare(target, world, frame);
        int draws = Draw(target, world, frame, false);
        byte[] rgba = ReadTexture(target.Color, 0, 0, target.Width, target.Height);
        target.Metrics = new(world.MeshUploads, world.ResidentMeshes, world.ResidentTextures, world.TextureBytes,
            draws, watch.Elapsed.TotalMilliseconds, rgba.LongLength, Generation,world.GeometryBytes,
            BatchCount:draws,VisiblePrimitives:_lastVisiblePrimitives,GeometryUploadBytes:world.GeometryUploadBytes);
        return new((int)target.Width, (int)target.Height, rgba);
    }
    internal StudioPickResult Pick(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame, double x, double y,StudioPickKind kind)
    {
        var cpu = EditorPickPass.Cpu(frame, x, y,kind);
        try
        {
            Prepare(target, world, frame); Draw(target, world, frame, true,kind);
            uint px = (uint)Math.Clamp((int)Math.Floor(x * frame.Layout.RenderScale), 0, (int)target.Width-1);
            uint py = (uint)Math.Clamp((int)Math.Floor(y * frame.Layout.RenderScale), 0, (int)target.Height-1);
            byte[] pixel = ReadTexture(target.Ids, px, py, 1, 1);
            if(target.Metrics is { } metrics)target.Metrics=metrics with {PickReadbackBytes=metrics.PickReadbackBytes+4};
            uint id = BitConverter.ToUInt32(pixel);
            StudioPickElement? element = world.Picks.TryGetValue(id, out var found) ? found : null;
            if(kind==StudioPickKind.Object && element is { } objectElement)element=objectElement with {Kind=StudioPickKind.Object};
            bool parity = element?.ObjectId == cpu.Element?.ObjectId && element?.Face == cpu.Element?.Face
                && element?.Kind == cpu.Element?.Kind && (kind is StudioPickKind.Face or StudioPickKind.Object || element?.A==cpu.Element?.A);
            // CPU oracle wins disagreements, preserving near-plane and editor
            // modeling semantics instead of silently selecting a wrong object.
            return parity ? new(element, cpu.Surface, true, true) : cpu with { GpuUsed = true, MatchesCpu = false };
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        { return cpu; }
    }
    private void Prepare(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame)
    {
        RequireOwner();
        if (!ReferenceEquals(world.Owner, this) || !ReferenceEquals(target.Owner, this))
            throw new ArgumentException("World and surface must belong to the same Studio graphics device.");
        if (!frame.Layout.IsValid) throw new ArgumentException("Viewport pixel bounds are invalid.");
        RecoverIfLost(); PrepareGraph();
        target.Resize((uint)frame.Layout.PixelWidth, (uint)frame.Layout.PixelHeight);
        world.Synchronize(frame);
    }
    private int Draw(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame, bool picking,StudioPickKind pickKind=StudioPickKind.Face)
    {
        var api = Api;
        var queue = api.DeviceGetQueue(Device.Device);
        CommandEncoder* encoder = null; RenderPassEncoder* pass = null; CommandBuffer* commands = null;
        var frameBindings=new List<nint>();var frameLayouts=new List<nint>();
        int drawCalls = 0;
        _lastVisiblePrimitives=0;
        try
        {
            encoder = api.DeviceCreateCommandEncoder(Device.Device, new CommandEncoderDescriptor());
            var attachment = new RenderPassColorAttachment { DepthSlice = uint.MaxValue,
                View = picking ? target.IdView : target.ColorView, LoadOp = LoadOp.Clear, StoreOp = StoreOp.Store,
                ClearValue = picking ? new Silk.NET.WebGPU.Color(0,0,0,0) : new Silk.NET.WebGPU.Color(20d/255,28d/255,37d/255,1) };
            var depth = new RenderPassDepthStencilAttachment { View = target.DepthView, DepthLoadOp = LoadOp.Clear, DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1, StencilLoadOp = LoadOp.Undefined, StencilStoreOp = StoreOp.Undefined, StencilReadOnly = true };
            var descriptor = new RenderPassDescriptor { ColorAttachments = &attachment, ColorAttachmentCount = 1, DepthStencilAttachment = &depth };
            pass = api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            var basis = frame.Camera.Basis();
            var view = Matrix4x4.CreateLookAt(frame.Camera.Position, frame.Camera.Target, basis.Up);
            // Canonical camera projection is GL clip depth. Convert to WebGPU 0..1.
            var clipDepth = Matrix4x4.Identity; clipDepth.M33 = .5f; clipDepth.M43 = .5f;
            var vp = view * frame.Camera.Projection(frame.Layout) * clipDepth;
            foreach (var mesh in frame.Meshes)
            {
                var resource = world.Meshes[mesh.ObjectId];
                var transform = frame.PreviewTransforms.TryGetValue(mesh.ObjectId, out var preview) ? preview : Matrix4x4.Identity;
                bool selected = frame.Selection.Contains(mesh.ObjectId);
                void Submit(MeshResource.Part part, bool edge)
                {
                    if (frame.Collision ? !part.Solid || part.SurfaceOnly : part.CollisionOnly) return;
                    MapViewportMaterial? source = null;
                    if (!frame.Collision && !edge)
                    { if (frame.UvChecker) source = MapViewportMaterials.Checker; else frame.Materials.TryGetValue(part.Material, out source); }
                    var material = source == null ? world.White : world.Material(source);
                    // Pick pipeline's automatically inferred layout excludes
                    // texture bindings. Give it its own layout-compatible group.
                    var pipeline = picking ? _pickPipeline : target.TargetFormat == GpuTextureFormat.Bgra8Unorm
                        ? (edge ? _bgraEdgePipeline : _bgraFillPipeline) : edge ? _edgePipeline : _fillPipeline;
                    api.RenderPassEncoderSetPipeline(pass, pipeline);
                    var mvp = transform * vp;
                    float uvX = source == null ? 1 : 1f / (source.CoordinateWidth > 0 ? source.CoordinateWidth : source.Width);
                    float uvY = source == null ? 1 : 1f / (source.CoordinateHeight > 0 ? source.CoordinateHeight : source.Height);
                    var tint = edge ? (selected ? new Vector4(1,.8f,.15f,1) : new Vector4(.45f,.6f,.7f,1))
                        : frame.Collision ? new Vector4(.2f,.6f,.4f,1) : Vector4.One;
                    float* words = stackalloc float[60];
                    *(Matrix4x4*)words = mvp; *(Matrix4x4*)(words+16) = transform; *(Matrix4x4*)(words+32) = view;
                    int mode = !picking ? 0 : part.PickKind == StudioPickKind.Vertex ? 1 : part.PickKind == StudioPickKind.Edge ? 2 : 0;
                    *(Vector4*)(words+48) = mode==0 ? new(uvX,uvY,source == null ? 0 : 1,0)
                        : new(18f/target.Width,18f/target.Height,0,mode); *(Vector4*)(words+52) = tint;
                    *(Vector4*)(words+56) = new(material.Enhanced ? 1 : 0,material.Normal != 0 ? 1 : 0,material.Specular != 0 ? 1 : 0,material.Emissive != 0 ? 1 : 0);
                    api.QueueWriteBuffer(queue, part.Uniform, 0, words, 240);
                    var bindingLayout = api.RenderPipelineGetBindGroupLayout(pipeline, 0);
                    frameLayouts.Add((nint)bindingLayout);
                    var entries = stackalloc BindGroupEntry[9];
                    entries[0] = new() { Binding = 0, Buffer = part.Uniform, Size = 240 };
                    entries[1] = new() { Binding = 1, TextureView = material.View };
                    entries[2] = new() { Binding = 2, Sampler = material.Sampler };
                    int[] companions = {material.Normal,material.Specular,material.Emissive};
                    for(int channel=0;channel<3;channel++)
                    {
                        var companion=companions[channel] == 0 ? world.White : world.Textures[companions[channel]];
                        entries[3+channel*2]=new() { Binding=(uint)(3+channel*2),TextureView=companion.View };
                        entries[4+channel*2]=new() { Binding=(uint)(4+channel*2),Sampler=companion.Sampler };
                    }
                    var bindingDescriptor = new BindGroupDescriptor { Layout = bindingLayout, EntryCount = picking ? 1u : 9u, Entries = entries };
                    var binding = api.DeviceCreateBindGroup(Device.Device, &bindingDescriptor);
                    frameBindings.Add((nint)binding);
                    // wgpu resolves recorded binding IDs when the pass ends.
                    // Keep every binding alive through command submission.
                    api.RenderPassEncoderSetBindGroup(pass, 0, binding, 0, null);
                    api.RenderPassEncoderSetVertexBuffer(pass, 0, part.Vertices, 0, (ulong)(part.Count*40));
                    api.RenderPassEncoderDraw(pass, (uint)part.Count, 1, 0, 0); drawCalls++;
                    _lastVisiblePrimitives+=part.Count/(edge ? 2 : 3);
                }
                if (picking || !frame.Wireframe) foreach (var part in resource.Parts) Submit(part, false);
                if(picking && pickKind==StudioPickKind.Vertex)foreach(var part in resource.PickVertices)Submit(part,false);
                if(picking && pickKind==StudioPickKind.Edge)foreach(var part in resource.PickEdges)Submit(part,false);
                if (!picking && (frame.Wireframe || selected)) foreach (var part in resource.Edges) Submit(part, true);
            }
            api.RenderPassEncoderEnd(pass);
            commands = api.CommandEncoderFinish(encoder, new CommandBufferDescriptor());
            Device.ObserveNativeResult(MphRead.Mods.Render.ModernGraphicsNativeBridge.Submit(queue,1,&commands), "Studio submit");
            Device.ThrowIfFailed(); return drawCalls;
        }
        finally
        {
            if (commands != null) api.CommandBufferRelease(commands);
            if (pass != null) api.RenderPassEncoderRelease(pass);
            if (encoder != null) api.CommandEncoderRelease(encoder);
            foreach(var binding in frameBindings)if(binding!=0)api.BindGroupRelease((BindGroup*)binding);
            foreach(var layout in frameLayouts)if(layout!=0)api.BindGroupLayoutRelease((BindGroupLayout*)layout);
            api.QueueRelease(queue);
        }
    }
    private byte[] ReadTexture(GpuTexture* texture, uint x, uint y, uint width, uint height)
    {
        uint rowBytes = width*4, paddedRow = (rowBytes+255)&~255u;
        nuint bytes = (nuint)(paddedRow*height);
        var api = Api;
        var readback = api.DeviceCreateBuffer(Device.Device, new BufferDescriptor { Size = (ulong)bytes, Usage = BufferUsage.CopyDst | BufferUsage.MapRead });
        CommandEncoder* encoder = null; CommandBuffer* commands = null; Queue* queue = null;
        bool mapped = false;
        try
        {
            encoder = api.DeviceCreateCommandEncoder(Device.Device, new CommandEncoderDescriptor());
            var source = new ImageCopyTexture { Texture = texture, Origin = new(x,y,0), Aspect = TextureAspect.All };
            var destination = new ImageCopyBuffer { Buffer = readback, Layout = new TextureDataLayout { BytesPerRow = paddedRow, RowsPerImage = height } };
            var extent = new Extent3D(width,height,1); api.CommandEncoderCopyTextureToBuffer(encoder,&source,&destination,&extent);
            commands = api.CommandEncoderFinish(encoder, new CommandBufferDescriptor()); queue = api.DeviceGetQueue(Device.Device);
            Device.ObserveNativeResult(MphRead.Mods.Render.ModernGraphicsNativeBridge.Submit(queue,1,&commands), "Studio explicit readback");
            BufferMapAsyncStatus status = BufferMapAsyncStatus.Unknown;
            BufferMapCallback callbackFunction = (value,_) => status = value;
            var callback = new PfnBufferMapCallback(callbackFunction);
            api.BufferMapAsync(readback, MapMode.Read, 0, bytes, callback, null);
            Device.Native.DevicePoll(Device.Device,true,null);
            if (status != BufferMapAsyncStatus.Success) throw new InvalidOperationException("Studio readback failed: " + status);
            mapped = true;
            var pixels = (byte*)api.BufferGetConstMappedRange(readback,0,bytes);
            byte[] result = new byte[checked(width*height*4)];
            for (int row=0;row<height;row++) Marshal.Copy((nint)(pixels+row*paddedRow),result,(int)(row*rowBytes),(int)rowBytes);
            GC.KeepAlive(callbackFunction); return result;
        }
        finally
        {
            if (mapped) api.BufferUnmap(readback);
            if (queue != null) api.QueueRelease(queue);
            if (commands != null) api.CommandBufferRelease(commands);
            if (encoder != null) api.CommandEncoderRelease(encoder);
            api.BufferRelease(readback);
        }
    }
    private void ReleaseGraph()
    {
        if (_materialLayout != null) Api.BindGroupLayoutRelease(_materialLayout);
        if (_fillPipeline != null) Api.RenderPipelineRelease(_fillPipeline);
        if (_edgePipeline != null) Api.RenderPipelineRelease(_edgePipeline);
        if (_pickPipeline != null) Api.RenderPipelineRelease(_pickPipeline);
        if (_bgraFillPipeline != null) Api.RenderPipelineRelease(_bgraFillPipeline);
        if (_bgraEdgePipeline != null) Api.RenderPipelineRelease(_bgraEdgePipeline);
        if (_shader != null) Api.ShaderModuleRelease(_shader);
        _materialLayout = null; _fillPipeline = _edgePipeline = _pickPipeline = null; _shader = null;
        _bgraFillPipeline = _bgraEdgePipeline = null;
        ReleaseOverlayGraph();
    }
}
#endif
