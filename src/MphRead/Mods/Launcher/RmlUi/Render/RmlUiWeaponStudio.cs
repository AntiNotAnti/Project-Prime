#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.StudioIntegration;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeWeaponStudioPage? _weaponStudio;
    private static void OpenNativeWeaponStudio(string? path = null)
    {
        if(Environment.GetEnvironmentVariable("PROJECT_PRIME_WEAPON_CREATOR")!="1")
        { if(_studio==null)OpenNativeStudio();_studio?.ShowStatus("WEAPON CREATOR IS DISABLED // Enable the experimental creator feature to use this workspace."); return; }
        if(!Shell.StudioWindow)
        {
            bool opened=StudioApplicationLauncher.TryOpenWeapon(path,out string? error);
            _studio?.ShowStatus(opened?"WEAPON STUDIO OPENED IN A SEPARATE APP":error??"WEAPON STUDIO COULD NOT OPEN");
            return;
        }
        if(_host==null||_pages==null||_weaponStudio is { CanNavigateAway:false })return;
        CloseNativeStudio(); _launcherPages?.Suspend();
        var next=new NativeWeaponStudioPage(_host,_pages);
        try { next.Open(path); _weaponStudio=next; }
        catch { next.Dispose(); throw; }
    }
    private static void CloseNativeWeaponStudio()
    { var closing=_weaponStudio;_weaponStudio=null;closing?.Dispose(); }
}
#endif
