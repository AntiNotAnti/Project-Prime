#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.MapEditor;

namespace MphRead.Mods.StudioRendering;

/// <summary>The authoring control supplies presentation data; the desktop host owns graphics.</summary>
public interface IStudioMapPresentation : IDisposable
{
    bool Active { get; }
    StudioViewportImage Render(MapRenderFrame frame);
    MapPickHit? Pick(MapRenderFrame frame, double x, double y);
    StudioPickResult? PickElement(MapRenderFrame frame,double x,double y,StudioPickKind kind) => null;
}

#if MPHREAD_AVALONIA
public interface IStudioNativeMapPresentation : IStudioMapPresentation
{
    Avalonia.Controls.Control NativeControl { get; }
    void Present(MapRenderFrame frame, StudioViewportImage overlay);
}
#endif

public sealed record StudioViewportImage(int Width, int Height, byte[] Rgba);

/// <summary>Installed by the independent desktop application before opening editor controls.</summary>
public static class StudioViewportConfiguration
{
    public static Func<MapDocument, IStudioMapPresentation>? MapPresentationFactory { get; set; }
}
#endif
