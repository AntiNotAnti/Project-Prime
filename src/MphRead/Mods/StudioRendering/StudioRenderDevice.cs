#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapEditor;
using MphRead.Mods.Render;
using Silk.NET.WebGPU;
using GpuTexture = Silk.NET.WebGPU.Texture;
using GpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.StudioRendering;

/// <summary>One creator device and owner thread. Document worlds share retained resources across views.</summary>
public sealed unsafe partial class StudioRenderDevice : IDisposable
{
    private ModernGraphicsDevice _device;
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly HashSet<EditorRenderWorld> _worlds = new();
    private readonly HashSet<StudioRenderSurface> _surfaces = new();
    private readonly HashSet<StudioNativeSurface> _nativeSurfaces = new();
    private StudioNativeSurface? _activeReplaySurface;
    private bool _disposed;
    internal ModernGraphicsDevice Device => _device;
    internal WebGPU Api => _device.Api;
    public int Generation { get; private set; } = 1;
    public string Backend => _device.Backend.ToString();
    public string Adapter => _device.AdapterName;
    public int ResidentWorldCount => _worlds.Count;
    public int ViewportSurfaceCount => _surfaces.Count;
    public int NativeSurfaceCount => _nativeSurfaces.Count;
    /// <summary>Actual native-handle lifetime, including constructors which fail before registration.</summary>
    public long NativeSurfaceHandlesCreated { get; private set; }
    public long NativeSurfaceHandlesReleased { get; private set; }
    public long LiveNativeSurfaceHandles => NativeSurfaceHandlesCreated - NativeSurfaceHandlesReleased;
    private bool _failNextNativeSurfaceAdmission;
    internal void NativeSurfaceHandleCreated() => NativeSurfaceHandlesCreated++;
    internal void NativeSurfaceHandleReleased() => NativeSurfaceHandlesReleased++;
    /// <summary>Reject one admission after creating and querying a real native surface.</summary>
    public void FailNextNativeSurfaceAdmissionForDiagnostics()
    { RequireOwner(); _failNextNativeSurfaceAdmission = true; }
    internal bool ConsumeNativeSurfaceAdmissionFailureForDiagnostics()
    {
        if (!_failNextNativeSurfaceAdmission) return false;
        _failNextNativeSurfaceAdmission = false; return true;
    }
    public bool NativeResourcesAvailable => !_disposed && !_device.IsLost;
    public long GeometryMemoryBudgetBytes { get; set; } = 512L * 1024 * 1024;
    internal void RequireGeometryAdmission(long bytes)
    {
        if(bytes<0 || _worlds.Sum(world=>world.GeometryBytes)+bytes>GeometryMemoryBudgetBytes)
            throw new InvalidOperationException("Studio geometry residency budget reached. The document remains available through the CPU viewport.");
    }
    public StudioRenderDevice(GraphicsBackend backend = GraphicsBackend.Auto) { _device = ModernGraphicsDevice.Create(backend); }
    /// <summary>Explicit diagnostic injection; document models stay outside device lifetime.</summary>
    public void SimulateDeviceLossForDiagnostics() { RequireOwner(); _device.DestroyForCheck(); }

    internal void RequireOwner()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Studio graphics operations require their graphics owner thread.");
    }

    public EditorRenderWorld CreateWorld()
    { RequireOwner(); var world = new EditorRenderWorld(this); _worlds.Add(world); return world; }
    public StudioRenderSurface CreateSurface()
    { RequireOwner(); var surface = new StudioRenderSurface(this); _surfaces.Add(surface); return surface; }
    internal void Forget(EditorRenderWorld world) => _worlds.Remove(world);
    internal void Forget(StudioRenderSurface surface) => _surfaces.Remove(surface);
    public StudioNativeSurface CreateNativeSurface(nint handle, string kind, nint display = 0)
    { RequireOwner(); var surface = new StudioNativeSurface(this,handle,kind,display); _nativeSurfaces.Add(surface); return surface; }
    internal void Forget(StudioNativeSurface surface)
    {
        ModernGraphicsCompat.DetachStudioSurface(_device,surface.Surface);
        if (ReferenceEquals(_activeReplaySurface,surface))
            _activeReplaySurface=null;
        _nativeSurfaces.Remove(surface);
    }
    public void RecreateNativeSurface(StudioNativeSurface surface)
    {
        RequireOwner();
        if(!ReferenceEquals(surface.Owner,this))throw new ArgumentException("Native surface belongs to another device.");
        if(_device.IsLost){RecoverIfLost();return;}
        ModernGraphicsCompat.DetachStudioSurface(_device,surface.Surface);
        if(ReferenceEquals(_activeReplaySurface,surface))_activeReplaySurface=null;
        surface.Recreate();
    }
    public void BeginReplayFrame(StudioNativeSurface surface, int width, int height)
    {
        RequireOwner(); RecoverIfLost();
        if (!ReferenceEquals(surface.Owner,this)) throw new ArgumentException("Replay surface belongs to another device.");
        ModernGraphicsCompat.BeginStudioSurface(_device,surface.Surface,width,height); _activeReplaySurface=surface;
    }
    public void PresentReplayFrame()
    { RequireOwner(); ModernGraphicsCompat.Present(); }

    public void RecoverIfLost()
    {
        RequireOwner();
        if (!_device.IsLost) return;
        // Keep document CPU data, recreate only native state, then lazily promote
        // each world/surface on its next successful render boundary.
        foreach (var surface in _surfaces) surface.ReleaseNative();
        _device.ClearBorrowedStudioSurface();
        foreach (var surface in _nativeSurfaces) surface.ReleaseNative();
        foreach (var world in _worlds) world.ReleaseNative();
        ReleaseGraph();
        var old = _device;
        _device = old.CreateReplacement();
        foreach (var surface in _nativeSurfaces) surface.Recreate();
        if (_activeReplaySurface != null) _device.BorrowStudioSurface(_activeReplaySurface.Surface);
        ModernGraphicsCompat.ReplaceStudioDevice(_device);
        old.Dispose(); Generation++;
    }

    public void Dispose()
    {
        if (_disposed) return;
        RequireOwner();
        ModernGraphicsCompat.ShutdownStudio(); _device.ClearBorrowedStudioSurface(); _activeReplaySurface=null;
        foreach (var surface in new List<StudioRenderSurface>(_surfaces)) surface.Dispose();
        foreach (var surface in new List<StudioNativeSurface>(_nativeSurfaces)) surface.Dispose();
        foreach (var world in new List<EditorRenderWorld>(_worlds)) world.Dispose();
        ReleaseGraph(); _device.Dispose(); _disposed = true;
    }
}

public sealed record StudioRenderMetrics(long MeshUploads, int ResidentMeshes, int ResidentTextures,
    long TextureBytes, int DrawCalls, double CpuMilliseconds, long ReadbackBytes, int DeviceGeneration,
    long GeometryBytes = 0, long PickReadbackBytes = 0, double? GpuMilliseconds = null,
    int? BatchCount = null, long? VisiblePrimitives = null, long GeometryUploadBytes = 0, int PixelWidth = 0, int PixelHeight = 0);

/// <summary>Independent viewport targets/camera; meshes/materials live in its shared document world.</summary>
public sealed unsafe class StudioRenderSurface : IDisposable
{
    internal readonly StudioRenderDevice Owner;
    internal GpuTexture* Color;
    internal TextureView* ColorView;
    internal GpuTexture* Depth;
    internal TextureView* DepthView;
    internal GpuTexture* Ids;
    internal TextureView* IdView;
    internal GpuTexture* ShadowDepth;
    internal TextureView* ShadowView;
    internal uint ShadowSize;
    internal System.Numerics.Matrix4x4 ShadowCameraView=System.Numerics.Matrix4x4.Identity;
    internal System.Numerics.Matrix4x4 ShadowCameraProjection=System.Numerics.Matrix4x4.Identity;
    internal uint Width, Height;
    internal GpuTextureFormat TargetFormat = GpuTextureFormat.Rgba8Unorm;
    private bool _disposed;
    internal StudioRenderSurface(StudioRenderDevice owner) { Owner = owner; }
    public StudioRenderMetrics? Metrics { get; internal set; }
    public long PickPassSubmissions { get; internal set; }
    internal StudioPickFrameSnapshot? PickFrame;
    internal void Resize(uint width, uint height)
    {
        Owner.RequireOwner();
        if (Width == width && Height == height && Color != null) return;
        ReleaseNative(); Width = width; Height = height;
        Color = Create(GpuTextureFormat.Rgba8Unorm, TextureUsage.RenderAttachment | TextureUsage.CopySrc);
        ColorView = Owner.Api.TextureCreateView(Color, null);
        Depth = Create(GpuTextureFormat.Depth32float, TextureUsage.RenderAttachment);
        DepthView = Owner.Api.TextureCreateView(Depth, null);
        Ids = Create(GpuTextureFormat.R32Uint, TextureUsage.RenderAttachment | TextureUsage.CopySrc);
        IdView = Owner.Api.TextureCreateView(Ids, null);
    }
    private GpuTexture* Create(GpuTextureFormat format, TextureUsage usage) => Owner.Api.DeviceCreateTexture(Owner.Device.Device,
        new TextureDescriptor { Size = new(Width, Height, 1), Dimension = TextureDimension.Dimension2D,
            Format = format, Usage = usage, MipLevelCount = 1, SampleCount = 1 });
    internal void EnsureShadow(MapRenderFrame frame)
    {
        uint wanted=frame.ShadowPreview ? 1024u : 1u;
        if(ShadowDepth==null || ShadowSize!=wanted)
        {
            if(ShadowView!=null)Owner.Api.TextureViewRelease(ShadowView);
            if(ShadowDepth!=null)Owner.Api.TextureRelease(ShadowDepth);
            ShadowSize=wanted;
            ShadowDepth=Owner.Api.DeviceCreateTexture(Owner.Device.Device,new TextureDescriptor {Size=new(wanted,wanted,1),Dimension=TextureDimension.Dimension2D,
                Format=GpuTextureFormat.Depth32float,Usage=TextureUsage.RenderAttachment|TextureUsage.TextureBinding,MipLevelCount=1,SampleCount=1});
            ShadowView=Owner.Api.TextureCreateView(ShadowDepth,null);
        }
        var camera=GraphicsEnvironmentMath.DirectionalShadowCamera(frame.Camera.Position,frame.Camera.Basis().Forward,frame.Light1Vector,(int)wanted,lowQuality:true);
        ShadowCameraView=camera.View;ShadowCameraProjection=camera.Projection;
    }
    internal void ReleaseNative()
    {
        var api = Owner.Api;
        if (ColorView != null) api.TextureViewRelease(ColorView);
        if (DepthView != null) api.TextureViewRelease(DepthView);
        if (IdView != null) api.TextureViewRelease(IdView);
        if (ShadowView != null)api.TextureViewRelease(ShadowView);
        if (Color != null) api.TextureRelease(Color);
        if (Depth != null) api.TextureRelease(Depth);
        if (Ids != null) api.TextureRelease(Ids);
        if (ShadowDepth != null)api.TextureRelease(ShadowDepth);
        Color = Depth = Ids = null; ColorView = DepthView = IdView = null;
        ShadowDepth=null;ShadowView=null;ShadowSize=0;
        PickFrame=null;
    }
    public StudioViewportImage Render(EditorRenderWorld world, MapRenderFrame frame)
    { ObjectDisposedException.ThrowIf(_disposed, this); return Owner.Render(this, world, frame); }
    /// <summary>Diagnostic frame submission without GPU readback or a native window. CPU timing measures submission, not GPU completion.</summary>
    public StudioRenderMetrics SubmitForDiagnostics(EditorRenderWorld world, MapRenderFrame frame)
    { ObjectDisposedException.ThrowIf(_disposed,this);return Owner.SubmitForDiagnostics(this,world,frame); }
    /// <summary>Counts actual GPU pixel reads and full-scene CPU queries separately.
    /// Winning-face triangle tests never include unrelated document geometry.</summary>
    public StudioPickDiagnostics PickDiagnostics { get; internal set; }
    private bool _failNextPickReadback;
    /// <summary>One-shot diagnostic fault injection through the normal read-error fallback.</summary>
    public void FailNextPickReadbackForDiagnostics()
    { ObjectDisposedException.ThrowIf(_disposed, this); Owner.RequireOwner(); _failNextPickReadback = true; }
    internal void CheckPickReadbackForDiagnostics()
    {
        if (!_failNextPickReadback) return;
        _failNextPickReadback = false;
        throw new InvalidOperationException("Injected Studio pick readback failure.");
    }
    public StudioPickResult Pick(EditorRenderWorld world, MapRenderFrame frame, double x, double y,
        StudioPickKind kind = StudioPickKind.Face, bool verifyCpuParity = false)
    { ObjectDisposedException.ThrowIf(_disposed, this); return Owner.Pick(this, world, frame, x, y, kind, verifyCpuParity); }
    public void Present(StudioNativeSurface native, EditorRenderWorld world, MapRenderFrame frame, StudioViewportImage overlay)
    { ObjectDisposedException.ThrowIf(_disposed, this); Owner.Present(this, native, world, frame, overlay); }
    public void Dispose()
    { if (_disposed) return; Owner.RequireOwner(); ReleaseNative(); Owner.Forget(this); _disposed = true; }
}
#endif
