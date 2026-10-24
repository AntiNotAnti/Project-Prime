#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Gui;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    /// <summary>Only auxiliary pictures are optional. A failed tile must not close the playback owner.</summary>
    internal static bool RenderNativeReplayFourViews(RenderWindow window)
    {
        var page = _playback;
        var scene = DemoPlayback.PresentationScene;
        if (!Shell.StudioWindow || page is not { IsOpen: true, FourViews: true }
            || !NativeReplayLeaseCurrent(window) || _pages?.Top != page.Document
            || scene == null || !DemoPlayback.Owns(scene)) return false;
        var size = window.FramebufferSize;
        var bounds = NativeReplayViewportBounds(size.X, size.Y);
        if (bounds == null) return false;
        try { return scene.DrawNativeReplayFourViews(bounds.Value, size, page.AuxiliaryPreviewInteracting); }
        catch (Exception error) when (error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            Mods.DebugLog.Line("replay", "Four-view auxiliary render failed; returning to single view: " + error);
            page.ReportFourViewFailure(error);
            return false;
        }
    }
}
#endif
