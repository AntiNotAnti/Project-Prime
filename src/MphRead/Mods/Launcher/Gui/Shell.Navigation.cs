#if MPHREAD_SHELL && !ANDROID
using MphRead.Mods.Launcher.Core;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    private static LauncherRouter? _applicationRouter;

    // Created and mutated on the game's thread. Both presenters consume the same history/lifetime.
    internal static LauncherRouter ApplicationRouter => _applicationRouter ??= new();

    private static void ReleaseApplicationRouter()
    {
        _applicationRouter?.Dispose();
        _applicationRouter = null;
    }
}
#endif
