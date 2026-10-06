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
    private RenderPipeline* _shadowPipeline,_overdrawPipeline,_bgraOverdrawPipeline;
    private BindGroupLayout* _materialLayout;
    private ShaderModule* _shader;
    private long _lastVisiblePrimitives;
    private const string Shader = """
struct Params { mvp: mat4x4<f32>, model: mat4x4<f32>, view: mat4x4<f32>, uv: vec4<f32>, tint: vec4<f32>, features: vec4<f32>,
 light1vec: vec4<f32>, light2vec: vec4<f32>, light1col: vec4<f32>, light2col: vec4<f32>, fogColor: vec4<f32>, settings: vec4<f32>,
 materialColor: vec4<f32>, fogRange: vec4<f32>, shadowView: mat4x4<f32>, shadowProjection: mat4x4<f32> };
@group(0) @binding(0) var<uniform> p: Params;
@group(0) @binding(1) var image: texture_2d<f32>;
@group(0) @binding(2) var sample_image: sampler;
@group(0) @binding(3) var normal_image: texture_2d<f32>;
@group(0) @binding(4) var normal_sampler: sampler;
@group(0) @binding(5) var specular_image: texture_2d<f32>;
@group(0) @binding(6) var specular_sampler: sampler;
@group(0) @binding(7) var emissive_image: texture_2d<f32>;
@group(0) @binding(8) var emissive_sampler: sampler;
@group(0) @binding(9) var shadow_image: texture_depth_2d;
var<private> surface_position_1: vec3<f32>;
var<private> surface_normal_1: vec3<f32>;
var<private> texcoord_1: vec2<f32>;
fn prime_sample_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(image,sample_image,uv); }
fn prime_sample_normal_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(normal_image,normal_sampler,uv); }
fn prime_sample_specular_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(specular_image,specular_sampler,uv); }
fn prime_sample_emissive_tex(uv: vec2<f32>) -> vec4<f32> { return textureSample(emissive_image,emissive_sampler,uv); }
fn prime_sample_shadow_tex(uv: vec2<f32>) -> vec4<f32> {
 let dims=vec2<i32>(textureDimensions(shadow_image));
 let point=clamp(vec2<i32>(vec2<f32>(uv.x,1f-uv.y)*vec2<f32>(dims)),vec2<i32>(0),dims-vec2<i32>(1));
 return vec4<f32>(textureLoad(shadow_image,point,0));
}
struct Input { @location(0) position: vec3<f32>, @location(1) normal: vec3<f32>, @location(2) uv: vec2<f32>, @location(3) shade: f32, @location(4) id: u32, @location(5) terrain: u32 };
struct Output { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32>, @location(1) shade: f32, @location(2) @interpolate(flat) id: u32,
 @location(3) world: vec3<f32>, @location(4) normal: vec3<f32>, @location(5) @interpolate(flat) terrain: u32 };
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
 o.uv = input.uv * p.uv.xy; o.shade = input.shade; o.id = input.id;o.terrain=input.terrain;
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
 apply_material_lighting(&color);
 let texel_uv=input.uv*vec2<f32>(textureDimensions(image));let dx=dpdx(texel_uv);let dy=dpdy(texel_uv);
 let density=sqrt(abs(dx.x*dy.y-dx.y*dy.x)/max(length(cross(dpdx(input.world),dpdy(input.world))),0.000001f));
 if(p.settings.z>0.5f && p.settings.z<1.5f){color=vec4<f32>(terrain_color(input.terrain),1f);}
 if(p.settings.z>2.5f && p.settings.z<3.5f){color=vec4<f32>(density_color(density),1f);}
 if(p.settings.z>3.5f){color=p.materialColor;}
 if(p.settings.z<0.5f){
   if(color.a<=0.0039f){discard;}
   color=vec4<f32>(color.rgb*directional_shadow(input.world,input.normal),color.a);
   color=creator_fog(color,input.position.z);
 }
 return color * p.tint;
}
@fragment fn pick(input: Output) -> @location(0) u32 { return input.id; }
@fragment fn overdraw(input: Output) -> @location(0) vec4<f32> { return vec4<f32>(0.12f,0.025f,0.004f,1f); }
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
            _shadowPipeline = Pipeline(PrimitiveTopology.TriangleList,GpuTextureFormat.Undefined,null);
            _overdrawPipeline = Pipeline(PrimitiveTopology.TriangleList,GpuTextureFormat.Rgba8Unorm,"overdraw");
            _materialLayout = Api.RenderPipelineGetBindGroupLayout(_fillPipeline, 0);
            Device.ThrowIfFailed();
        }
        finally { SilkMarshal.Free(source); }
    }
    private RenderPipeline* Pipeline(PrimitiveTopology topology, GpuTextureFormat format, string? fragmentName)
    {
        var attributes = stackalloc VertexAttribute[6];
        attributes[0] = new() { Format = VertexFormat.Float32x3, Offset = 0, ShaderLocation = 0 };
        attributes[1] = new() { Format = VertexFormat.Float32x3, Offset = 12, ShaderLocation = 1 };
        attributes[2] = new() { Format = VertexFormat.Float32x2, Offset = 24, ShaderLocation = 2 };
        attributes[3] = new() { Format = VertexFormat.Float32, Offset = 32, ShaderLocation = 3 };
        attributes[4] = new() { Format = VertexFormat.Uint32, Offset = 36, ShaderLocation = 4 };
        attributes[5] = new() { Format = VertexFormat.Uint32, Offset = 40, ShaderLocation = 5 };
        var layout = new VertexBufferLayout { Attributes = attributes, AttributeCount = 6, ArrayStride = MeshResource.VertexStride, StepMode = VertexStepMode.Vertex };
        var color = new ColorTargetState { Format = format, WriteMask = ColorWriteMask.All };
        bool overdraw=fragmentName=="overdraw";
        var blend = new BlendState {Color=new BlendComponent {Operation=BlendOperation.Add,SrcFactor=overdraw ? BlendFactor.One : BlendFactor.SrcAlpha,DstFactor=overdraw ? BlendFactor.One : BlendFactor.OneMinusSrcAlpha},
            Alpha=new BlendComponent {Operation=BlendOperation.Add,SrcFactor=BlendFactor.One,DstFactor=overdraw ? BlendFactor.Zero : BlendFactor.OneMinusSrcAlpha}};
        if(fragmentName is "fragment" or "overdraw")color.Blend=&blend;
        var depth = new DepthStencilState { Format = GpuTextureFormat.Depth32float, DepthWriteEnabled = !overdraw && topology == PrimitiveTopology.TriangleList,
            DepthCompare = overdraw ? CompareFunction.Always : CompareFunction.LessEqual, StencilReadMask = 0, StencilWriteMask = 0,
            StencilFront = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep },
            StencilBack = new StencilFaceState { Compare = CompareFunction.Always, FailOp = StencilOperation.Keep, DepthFailOp = StencilOperation.Keep, PassOp = StencilOperation.Keep } };
        nint vs = SilkMarshal.StringToPtr("vertex"), fs = fragmentName==null ? 0 : SilkMarshal.StringToPtr(fragmentName);
        try
        {
            var fragment = new FragmentState { Module = _shader, EntryPoint = (byte*)fs, TargetCount = 1, Targets = &color };
            var descriptor = new RenderPipelineDescriptor {
                Vertex = new VertexState { Module = _shader, EntryPoint = (byte*)vs, Buffers = &layout, BufferCount = 1 },
                Primitive = new PrimitiveState { Topology = topology, FrontFace = FrontFace.Ccw, CullMode = CullMode.None },
                Multisample = new MultisampleState { Count = 1, Mask = uint.MaxValue }, Fragment = fragmentName==null ? null : &fragment, DepthStencil = &depth };
            var pipeline = Api.DeviceCreateRenderPipeline(Device.Device, &descriptor);
            if (pipeline == null) throw new InvalidOperationException("Studio viewport pipeline creation failed.");
            return pipeline;
        }
        finally { SilkMarshal.Free(vs); SilkMarshal.Free(fs); }
    }
    internal StudioViewportImage Render(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame)
    {
        var watch = Stopwatch.StartNew();
        var metrics=SubmitForDiagnostics(target,world,frame);
        byte[] rgba = ReadTexture(target.Color, 0, 0, target.Width, target.Height);
        target.Metrics=metrics with {CpuMilliseconds=watch.Elapsed.TotalMilliseconds,ReadbackBytes=rgba.LongLength};
        return new((int)target.Width, (int)target.Height, rgba);
    }
    internal StudioRenderMetrics SubmitForDiagnostics(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame)
    {
        var watch=Stopwatch.StartNew();
        Prepare(target, world, frame);
        int shadowDraws=frame.ShadowPreview ? Draw(target,world,frame with {Meshes=frame.ResidentMeshes},false,shadow:true) : 0;
        int draws = Draw(target, world, frame, false)+shadowDraws;
        target.Metrics = new(world.MeshUploads, world.ResidentMeshes, world.ResidentTextures, world.TextureBytes,
            draws, watch.Elapsed.TotalMilliseconds, 0, Generation,world.GeometryBytes,
            BatchCount:draws,VisiblePrimitives:_lastVisiblePrimitives,GeometryUploadBytes:world.GeometryUploadBytes,PixelWidth:(int)target.Width,PixelHeight:(int)target.Height);
        return target.Metrics;
    }
    internal StudioPickResult Pick(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame, double x, double y,StudioPickKind kind)
    {
        var cpu = EditorPickPass.Cpu(frame, x, y,kind);
        try
        {
            Prepare(target, world, frame);
            if(target.PickFrame?.Matches(world,frame,kind)!=true)
            {
                Draw(target, world, frame, true,kind);target.PickPassSubmissions++;
                target.PickFrame=new(world,frame,kind);
            }
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
        target.EnsureShadow(frame);
        world.Synchronize(frame.ShadowPreview ? frame with {Meshes=frame.ResidentMeshes} : frame);
    }
    private int Draw(StudioRenderSurface target, EditorRenderWorld world, MapRenderFrame frame, bool picking,StudioPickKind pickKind=StudioPickKind.Face,bool shadow=false)
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
                ClearValue = picking ? new Silk.NET.WebGPU.Color(0,0,0,0) : frame.DiagnosticMode==MapViewportDiagnosticMode.Overdraw ? new Silk.NET.WebGPU.Color(0,0,0,1) : new Silk.NET.WebGPU.Color(20d/255,28d/255,37d/255,1) };
            var depth = new RenderPassDepthStencilAttachment { View = shadow ? target.ShadowView : target.DepthView, DepthLoadOp = LoadOp.Clear, DepthStoreOp = StoreOp.Store,
                DepthClearValue = 1, StencilLoadOp = LoadOp.Undefined, StencilStoreOp = StoreOp.Undefined, StencilReadOnly = true };
            var descriptor = new RenderPassDescriptor { ColorAttachments = shadow ? null : &attachment, ColorAttachmentCount = shadow ? 0u : 1u, DepthStencilAttachment = &depth };
            pass = api.CommandEncoderBeginRenderPass(encoder, &descriptor);
            api.RenderPassEncoderSetViewport(pass,0,0,shadow ? target.ShadowSize : target.Width,shadow ? target.ShadowSize : target.Height,0,1);
            var basis = frame.Camera.Basis();
            var view = shadow ? target.ShadowCameraView : Matrix4x4.CreateLookAt(frame.Camera.Position, frame.Camera.Target, basis.Up);
            // Canonical camera projection is GL clip depth. Convert to WebGPU 0..1.
            var clipDepth = Matrix4x4.Identity; clipDepth.M33 = .5f; clipDepth.M43 = .5f;
            var vp = view * (shadow ? target.ShadowCameraProjection : frame.Camera.Projection(frame.Layout)) * clipDepth;
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
                    bool overdraw=!picking && !shadow && !edge && frame.DiagnosticMode==MapViewportDiagnosticMode.Overdraw;
                    var pipeline = shadow ? _shadowPipeline : picking ? _pickPipeline : overdraw
                        ? (target.TargetFormat==GpuTextureFormat.Bgra8Unorm ? _bgraOverdrawPipeline : _overdrawPipeline) : target.TargetFormat == GpuTextureFormat.Bgra8Unorm
                        ? (edge ? _bgraEdgePipeline : _bgraFillPipeline) : edge ? _edgePipeline : _fillPipeline;
                    api.RenderPassEncoderSetPipeline(pass, pipeline);
                    var mvp = transform * vp;
                    float uvX = source == null ? 1 : 1f / (source.CoordinateWidth > 0 ? source.CoordinateWidth : source.Width);
                    float uvY = source == null ? 1 : 1f / (source.CoordinateHeight > 0 ? source.CoordinateHeight : source.Height);
                    var tint = edge ? (selected ? new Vector4(1,.8f,.15f,1) : new Vector4(.45f,.6f,.7f,1))
                        : frame.Collision ? new Vector4(.2f,.6f,.4f,1) : Vector4.One;
                    float* words = stackalloc float[124];
                    *(Matrix4x4*)words = mvp; *(Matrix4x4*)(words+16) = transform; *(Matrix4x4*)(words+32) = view;
                    int mode = !picking ? 0 : part.PickKind == StudioPickKind.Vertex ? 1 : part.PickKind == StudioPickKind.Edge ? 2 : 0;
                    *(Vector4*)(words+48) = mode==0 ? new(uvX,uvY,source == null ? 0 : 1,0)
                        : new(18f/target.Width,18f/target.Height,0,mode); *(Vector4*)(words+52) = tint;
                    *(Vector4*)(words+56) = new(!edge && (material.Enhanced || frame.LightingPreview) ? 1 : 0,material.Normal != 0 ? 1 : 0,material.Specular != 0 ? 1 : 0,material.Emissive != 0 ? 1 : 0);
                    *(Vector4*)(words+60)=new(frame.Light1Vector,0);*(Vector4*)(words+64)=new(frame.Light2Vector,0);
                    *(Vector4*)(words+68)=new(frame.Light1Color,0);*(Vector4*)(words+72)=new(frame.Light2Color,0);*(Vector4*)(words+76)=new(frame.FogColor,1);
                    *(Vector4*)(words+80)=new(frame.FogPreview && frame.FogEnabled && !edge ? 1 : 0,frame.ShadowPreview && !edge ? 1 : 0,edge ? 0 : (int)frame.DiagnosticMode,0);
                    *(Vector4*)(words+84)=new(MapViewportDiagnostics.MaterialColor(part.Material.Imported,part.Material.Index),1);
                    var fog=MphRead.Mods.Render.GraphicsEnvironmentMath.FogRange(frame.FogOffset,frame.FogSlope);
                    *(Vector4*)(words+88)=new(fog.Minimum,fog.Maximum,1f/target.ShadowSize,0);
                    *(Matrix4x4*)(words+92)=target.ShadowCameraView;*(Matrix4x4*)(words+108)=target.ShadowCameraProjection;
                    api.QueueWriteBuffer(queue, part.Uniform, 0, words, MeshResource.UniformBytes);
                    var bindingLayout = api.RenderPipelineGetBindGroupLayout(pipeline, 0);
                    frameLayouts.Add((nint)bindingLayout);
                    var entries = stackalloc BindGroupEntry[10];
                    entries[0] = new() { Binding = 0, Buffer = part.Uniform, Size = MeshResource.UniformBytes };
                    entries[1] = new() { Binding = 1, TextureView = material.View };
                    entries[2] = new() { Binding = 2, Sampler = material.Sampler };
                    int[] companions = {material.Normal,material.Specular,material.Emissive};
                    for(int channel=0;channel<3;channel++)
                    {
                        var companion=companions[channel] == 0 ? world.White : world.Textures[companions[channel]];
                        entries[3+channel*2]=new() { Binding=(uint)(3+channel*2),TextureView=companion.View };
                        entries[4+channel*2]=new() { Binding=(uint)(4+channel*2),Sampler=companion.Sampler };
                    }
                    entries[9]=new() {Binding=9,TextureView=target.ShadowView};
                    var bindingDescriptor = new BindGroupDescriptor { Layout = bindingLayout, EntryCount = picking || shadow || overdraw ? 1u : 10u, Entries = entries };
                    var binding = api.DeviceCreateBindGroup(Device.Device, &bindingDescriptor);
                    frameBindings.Add((nint)binding);
                    // wgpu resolves recorded binding IDs when the pass ends.
                    // Keep every binding alive through command submission.
                    api.RenderPassEncoderSetBindGroup(pass, 0, binding, 0, null);
                    api.RenderPassEncoderSetVertexBuffer(pass, 0, part.Vertices, 0, (ulong)(part.Count*MeshResource.VertexStride));
                    api.RenderPassEncoderDraw(pass, (uint)part.Count, 1, 0, 0); drawCalls++;
                    _lastVisiblePrimitives+=part.Count/(edge ? 2 : 3);
                }
                if (shadow || picking || !frame.Wireframe) foreach (var part in resource.Parts) Submit(part, false);
                if(picking && pickKind==StudioPickKind.Vertex)foreach(var part in resource.PickVertices)Submit(part,false);
                if(picking && pickKind==StudioPickKind.Edge)foreach(var part in resource.PickEdges)Submit(part,false);
                if (!picking && !shadow && (frame.Wireframe || selected)) foreach (var part in resource.Edges) Submit(part, true);
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
        if (_shadowPipeline!=null)Api.RenderPipelineRelease(_shadowPipeline);
        if (_overdrawPipeline!=null)Api.RenderPipelineRelease(_overdrawPipeline);
        if (_bgraOverdrawPipeline!=null)Api.RenderPipelineRelease(_bgraOverdrawPipeline);
        if (_shader != null) Api.ShaderModuleRelease(_shader);
        _materialLayout = null; _fillPipeline = _edgePipeline = _pickPipeline = null; _shader = null;
        _bgraFillPipeline = _bgraEdgePipeline = null;
        _shadowPipeline=_overdrawPipeline=_bgraOverdrawPipeline=null;
        ReleaseOverlayGraph();
    }
}
#endif
