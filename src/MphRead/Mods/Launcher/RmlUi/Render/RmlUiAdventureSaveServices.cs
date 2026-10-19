#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Core;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal sealed partial class RmlUiLiveMenuServices
{
    public bool CanManageAdventureSaves => Shell.CanManageAdventureSaves;
    public bool ManageAdventureSave(RmlUiAdventureSaveChange change, out string status)
        => Shell.RmlUiManageAdventureSave(change, out status);
}
#endif
