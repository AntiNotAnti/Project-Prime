#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.Gui;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    /// <summary>
    /// Only a completed shell scene teardown may release the bot match Start
    /// latch. Keeping the existing menu controller also retains the choices
    /// made before the previous match.
    /// </summary>
    internal static bool NativeOfflineClosed(RenderWindow window)
    {
        if (!Requested || _disabled || !ReferenceEquals(Shell.Window, window) || window.HasScene)
            return false;
        return TryStart(window) && _controller?.OfflineSessionClosed() == true;
    }
}
#endif
