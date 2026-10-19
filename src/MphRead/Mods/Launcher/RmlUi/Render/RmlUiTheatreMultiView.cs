#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Gui;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    /// <summary>Call after the normal world render and before native UI composition. Never updates a replay clock.</summary>
    internal static bool RenderNativeReplayFourViews(RenderWindow window)
    {
        var page=_playback;var scene=DemoPlayback.PresentationScene;
        if(!Shell.StudioWindow||page is not{IsOpen:true,FourViews:true}||!NativeReplayLeaseCurrent(window)
            ||_pages?.Top!=page.Document||scene==null||!DemoPlayback.Owns(scene))return false;
        var size=window.FramebufferSize;var bounds=NativeReplayViewportBounds(size.X,size.Y);if(bounds==null)return false;
        scene.DrawNativeReplayFourViews(bounds.Value,size);return true;
    }
}
#endif
