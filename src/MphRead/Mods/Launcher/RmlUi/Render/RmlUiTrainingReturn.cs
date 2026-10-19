#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.Gui;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    internal static bool HandleNativeTrainingFailure(RenderWindow window, string error)
    {
        if (!Requested) return false;
        if (Shell.Active && ReferenceEquals(Shell.Window, window) && !window.HasScene)
        {
            NativeTrainingClosed(window);
            _controller?.ReportStudioLaunch(false, "TRAINING COULD NOT START // " + error);
        }
        Mods.DebugLog.Line("rmlui", "Training launch failed: " + error);
        return true;
    }

    internal static bool NativeTrainingClosed(RenderWindow window)
    {
        if (!Requested || _disabled || !ReferenceEquals(Shell.Window, window) || window.HasScene)
            return false;
        return TryStart(window) && _controller?.TrainingSessionClosed() == true;
    }
}
#endif
