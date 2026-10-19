#if MPHREAD_AVALONIA && !ANDROID
using MphRead.Mods.Launcher.RmlUi.Core;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
    internal static bool CanManageAdventureSaves => Active && _window is { HasScene: false }
        && _front?.CanManageAdventureSaves == true;
    internal static bool RmlUiManageAdventureSave(RmlUiAdventureSaveChange change, out string status)
    {
        status = "SAVE CHANGE REFUSED // FINISH THE ACTIVE OR PENDING SESSION";
        return CanManageAdventureSaves && _front!.RmlUiManageAdventureSave(change, out status);
    }
}
#endif
