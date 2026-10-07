using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.StudioRendering;
using ProjectPrime.Studio.Rendering;

namespace ProjectPrime.Studio.Map;

/// <summary>Native world presentation with shared document meshes and canonical authoring overlays.</summary>
public sealed class MapViewportHost : IStudioNativeMapPresentation
{
    private readonly MapDocument _document;
    private readonly StudioNativeViewport _native = new();
    private EditorRenderWorld? _world;
    private StudioRenderSurface? _surface;
    private bool _disposed;
    public MapViewportHost(MapDocument document) { _document=document; }
    public Control NativeControl => _native;
    public bool Active => !_disposed && _native.Surface != null;
    public StudioRenderMetrics? Metrics => _surface?.Metrics;
    private void Prepare()
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        _world ??= StudioGraphicsHost.AcquireWorld(_document);
        _surface ??= StudioGraphicsHost.Device.CreateSurface();
    }
    public void Present(MapRenderFrame frame,StudioViewportImage overlay)
    {
        Prepare();if(_native.Surface is { } native)_surface!.Present(native,_world!,frame,overlay);
        if(_surface?.Metrics is { } metrics)StudioGraphicsHost.Report(_document,metrics);
    }
    /// <summary>Explicit capture; never used by the normal native presentation path.</summary>
    public StudioViewportImage Render(MapRenderFrame frame)
    { Prepare();return _surface!.Render(_world!,frame); }
    public MapPickHit? Pick(MapRenderFrame frame,double x,double y)
    {
        Prepare();var picked=_surface!.Pick(_world!,frame,x,y);
        if(_surface.Metrics is { } metrics)StudioGraphicsHost.Report(_document,metrics);return picked.Surface;
    }
    public StudioPickResult PickElement(MapRenderFrame frame,double x,double y,StudioPickKind kind)
    {
        Prepare();var picked=_surface!.Pick(_world!,frame,x,y,kind);
        if(_surface.Metrics is { } metrics)StudioGraphicsHost.Report(_document,metrics);return picked;
    }
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;
        _surface?.Dispose();_surface=null;
        _native.Dispose();
        if(_world!=null){StudioGraphicsHost.ReleaseWorld(_document);_world=null;}
    }
}
