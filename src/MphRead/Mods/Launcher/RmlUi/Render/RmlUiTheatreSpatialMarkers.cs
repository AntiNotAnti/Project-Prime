#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using MphRead.Mods.Launcher.Gui;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    internal static void PrepareNativeReplaySpatialOverlays(Scene scene)
    {
        if(!Shell.StudioWindow||_playback is not{IsOpen:true} page||ReplayVideoExporter.Active
            ||!ReferenceEquals(scene,DemoPlayback.PresentationScene)||!DemoPlayback.Owns(scene))return;
        scene.AddNativeReplaySpatialMarkers(page.SpatialWorldMarkers());
        scene.AddNativeStudioCameraPath();
    }
}
#endif
