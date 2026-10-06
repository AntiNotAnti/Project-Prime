#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using GpuTexture = Silk.NET.WebGPU.Texture;
using GpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.StudioRendering;

public sealed unsafe partial class StudioRenderDevice
{
    private ShaderModule* _overlayShader;
    private RenderPipeline* _rgbaOverlay, _bgraOverlay;
    private const string OverlayShader = """
@group(0) @binding(0) var image: texture_2d<f32>;
@group(0) @binding(1) var sample_image: sampler;
struct Output { @builtin(position) position: vec4<f32>, @location(0) uv: vec2<f32> };
@vertex fn vertex(@builtin(vertex_index) index: u32) -> Output {
 var points = array<vec2<f32>,3>(vec2<f32>(-1.0,-1.0),vec2<f32>(3.0,-1.0),vec2<f32>(-1.0,3.0));
 let point = points[index]; var o: Output; o.position=vec4<f32>(point,0.0,1.0);
 o.uv=vec2<f32>((point.x+1.0)*0.5, (1.0-point.y)*0.5); return o;
}
@fragment fn fragment(input: Output) -> @location(0) vec4<f32> { return textureSample(image,sample_image,input.uv); }
""";
    internal void Present(StudioRenderSurface target, StudioNativeSurface native, EditorRenderWorld world,
        MapEditor.MapRenderFrame frame, StudioViewportImage overlay)
    {
        var watch = Stopwatch.StartNew();
        Prepare(target,world,frame);
        if (_bgraFillPipeline == null)
        { _bgraFillPipeline = Pipeline(PrimitiveTopology.TriangleList,GpuTextureFormat.Bgra8Unorm,"fragment"); _bgraEdgePipeline = Pipeline(PrimitiveTopology.LineList,GpuTextureFormat.Bgra8Unorm,"fragment"); }
        var view = native.Acquire(frame.Layout);
        if (view == null) return;
        var old = target.ColorView; var oldFormat = target.TargetFormat;
        try
        {
            target.ColorView=view; target.TargetFormat=native.Format;
            int draws=Draw(target,world,frame,false);
            DrawOverlay(target,native.Format,overlay);
            native.Present();
            target.Metrics = new(world.MeshUploads,world.ResidentMeshes,world.ResidentTextures,world.TextureBytes,
                draws+1,watch.Elapsed.TotalMilliseconds,0,Generation,world.GeometryBytes,target.Metrics?.PickReadbackBytes ?? 0,
                BatchCount:draws+1,VisiblePrimitives:_lastVisiblePrimitives+1,GeometryUploadBytes:world.GeometryUploadBytes);
        }
        finally { target.ColorView=old; target.TargetFormat=oldFormat; }
    }
    private RenderPipeline* OverlayPipeline(GpuTextureFormat format)
    {
        if (_overlayShader == null)
        {
            nint code=SilkMarshal.StringToPtr(OverlayShader);
            try
            {
                var wgsl=new ShaderModuleWGSLDescriptor { Code=(byte*)code, Chain=new ChainedStruct { SType=SType.ShaderModuleWgslDescriptor } };
                var sd=new ShaderModuleDescriptor { NextInChain=(ChainedStruct*)&wgsl };
                _overlayShader=Api.DeviceCreateShaderModule(Device.Device,&sd);
            }
            finally { SilkMarshal.Free(code); }
        }
        var existing=format==GpuTextureFormat.Bgra8Unorm ? _bgraOverlay : _rgbaOverlay;
        if(existing != null)return existing;
        nint vertex=SilkMarshal.StringToPtr("vertex"), fragment=SilkMarshal.StringToPtr("fragment");
        try
        {
            var blend = new BlendState {
                Color = new BlendComponent { Operation=BlendOperation.Add, SrcFactor=BlendFactor.One, DstFactor=BlendFactor.OneMinusSrcAlpha },
                Alpha = new BlendComponent { Operation=BlendOperation.Add, SrcFactor=BlendFactor.One, DstFactor=BlendFactor.OneMinusSrcAlpha } };
            var color=new ColorTargetState { Format=format, WriteMask=ColorWriteMask.All, Blend=&blend };
            var fs=new FragmentState { Module=_overlayShader,EntryPoint=(byte*)fragment,TargetCount=1,Targets=&color };
            var pd=new RenderPipelineDescriptor { Vertex=new VertexState { Module=_overlayShader,EntryPoint=(byte*)vertex }, Fragment=&fs,
                Primitive=new PrimitiveState { Topology=PrimitiveTopology.TriangleList, FrontFace=FrontFace.Ccw, CullMode=CullMode.None }, Multisample=new MultisampleState { Count=1,Mask=uint.MaxValue } };
            var pipeline=Api.DeviceCreateRenderPipeline(Device.Device,&pd);
            if(format==GpuTextureFormat.Bgra8Unorm)_bgraOverlay=pipeline;else _rgbaOverlay=pipeline;
            return pipeline;
        }
        finally { SilkMarshal.Free(vertex);SilkMarshal.Free(fragment); }
    }
    private void DrawOverlay(StudioRenderSurface target,GpuTextureFormat format,StudioViewportImage overlay)
    {
        // Overlay bytes are CPU-authoritative. Upload only when MapViewport is
        // invalidated; there is no GPU→CPU transfer on a normal frame.
        var api=Api; var queue=api.DeviceGetQueue(Device.Device);
        GpuTexture* texture=null; TextureView* view=null; Sampler* sampler=null; BindGroup* binding=null; BindGroupLayout* layout=null;
        CommandEncoder* encoder=null; RenderPassEncoder* pass=null; CommandBuffer* commands=null;
        try
        {
            texture=api.DeviceCreateTexture(Device.Device,new TextureDescriptor { Size=new((uint)overlay.Width,(uint)overlay.Height,1),Format=GpuTextureFormat.Rgba8Unorm,
                Usage=TextureUsage.TextureBinding|TextureUsage.CopyDst,Dimension=TextureDimension.Dimension2D,MipLevelCount=1,SampleCount=1 });
            var destination=new ImageCopyTexture { Texture=texture,Aspect=TextureAspect.All };
            var dataLayout=new TextureDataLayout { BytesPerRow=(uint)overlay.Width*4,RowsPerImage=(uint)overlay.Height };
            var extent=new Extent3D((uint)overlay.Width,(uint)overlay.Height,1);
            fixed(byte* data=overlay.Rgba)api.QueueWriteTexture(queue,&destination,data,(nuint)overlay.Rgba.Length,&dataLayout,&extent);
            view=api.TextureCreateView(texture,null);
            sampler=api.DeviceCreateSampler(Device.Device,new SamplerDescriptor { MinFilter=FilterMode.Linear,MagFilter=FilterMode.Linear,
                MipmapFilter=MipmapFilterMode.Nearest,AddressModeU=AddressMode.ClampToEdge,AddressModeV=AddressMode.ClampToEdge,
                AddressModeW=AddressMode.ClampToEdge,MaxAnisotropy=1 });
            var pipeline=OverlayPipeline(format);layout=api.RenderPipelineGetBindGroupLayout(pipeline,0);
            var entries=stackalloc BindGroupEntry[2];entries[0]=new() { Binding=0,TextureView=view };entries[1]=new() { Binding=1,Sampler=sampler };
            var bd=new BindGroupDescriptor { Layout=layout,EntryCount=2,Entries=entries };binding=api.DeviceCreateBindGroup(Device.Device,&bd);
            encoder=api.DeviceCreateCommandEncoder(Device.Device,new CommandEncoderDescriptor());
            var color=new RenderPassColorAttachment { DepthSlice=uint.MaxValue,View=target.ColorView,LoadOp=LoadOp.Load,StoreOp=StoreOp.Store };
            var pd=new RenderPassDescriptor { ColorAttachments=&color,ColorAttachmentCount=1 };pass=api.CommandEncoderBeginRenderPass(encoder,&pd);
            api.RenderPassEncoderSetPipeline(pass,pipeline);api.RenderPassEncoderSetBindGroup(pass,0,binding,0,null);
            api.RenderPassEncoderDraw(pass,3,1,0,0);api.RenderPassEncoderEnd(pass);
            commands=api.CommandEncoderFinish(encoder,new CommandBufferDescriptor());
            Device.ObserveNativeResult(MphRead.Mods.Render.ModernGraphicsNativeBridge.Submit(queue,1,&commands),"Studio overlay submit");
        }
        finally
        {
            if(commands!=null)api.CommandBufferRelease(commands);if(pass!=null)api.RenderPassEncoderRelease(pass);if(encoder!=null)api.CommandEncoderRelease(encoder);
            if(binding!=null)api.BindGroupRelease(binding);if(layout!=null)api.BindGroupLayoutRelease(layout);
            if(sampler!=null)api.SamplerRelease(sampler);if(view!=null)api.TextureViewRelease(view);if(texture!=null)api.TextureRelease(texture);api.QueueRelease(queue);
        }
    }
    private void ReleaseOverlayGraph()
    {
        if(_rgbaOverlay!=null)Api.RenderPipelineRelease(_rgbaOverlay);if(_bgraOverlay!=null)Api.RenderPipelineRelease(_bgraOverlay);
        if(_overlayShader!=null)Api.ShaderModuleRelease(_overlayShader);_rgbaOverlay=_bgraOverlay=null;_overlayShader=null;
    }
}
#endif
