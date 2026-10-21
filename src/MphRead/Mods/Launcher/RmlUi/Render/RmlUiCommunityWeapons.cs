#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Community;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeCommunityWeaponsPage? _communityWeapons;
    private static bool _weaponsReturnToMaps;
    private static void OpenNativeCommunityWeapons(Guid? requiredId=null,string? requiredArchive=null)
    {
        if(_host==null||_pages==null)return;
        bool maps=_community!=null;
        CloseNativeCommunity();_launcherPages?.Suspend();
        var next=new NativeCommunityWeaponsPage(_host,_pages);
        try
        {
            next.Open();_communityWeapons=next;_weaponsReturnToMaps=maps;
            if(requiredId is {} id&&requiredArchive!=null)next.RequireRevision(id,requiredArchive);
        }
        catch {next.Dispose();throw;}
    }
    private static bool HandleNativeCommunityWeapons(in RmlUiIntent intent,bool admitted)
    {
        if(_communityWeapons is {} page)
        {
            bool handled=page.Handle(intent);
            if(page.Closed)ReturnFromNativeCommunityWeapons();
            return handled;
        }
        if(intent.Kind!=RmlUiIntentKind.WeaponCommunityAction||intent.Argument!=0
            ||_pages?.Top!=intent.Document||_host?.CurrentInputDocument!=intent.Document
            ||!admitted&&_pages?.Accept(intent)!=true)return false;
        if(NetSession.TryRequiredWeaponPackage(out var id,out var archive))OpenNativeCommunityWeapons(id,archive);
        else OpenNativeCommunityWeapons();
        return true;
    }
    private static void CloseNativeCommunityWeapons()
    {var closing=_communityWeapons;_communityWeapons=null;closing?.Dispose();}
    private static void ReturnFromNativeCommunityWeapons()
    {
        bool maps=_weaponsReturnToMaps;CloseNativeCommunityWeapons();_weaponsReturnToMaps=false;
        if(maps&&_host!=null)OpenNativeCommunity(_host);else _launcherPages?.Resume();
    }
}
#endif
