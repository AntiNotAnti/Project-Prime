#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeCampaignShipPage? _campaignShip;

    internal static bool TryOpenNativeCampaignShip(RenderWindow window, CampaignDockReceipt receipt, Hunter hunter)
    {
        if (!Requested || _disabled || window.HasScene || !Shell.Active || (uint)hunter >= 7
            || MphRead.Mods.Network.NetSession.Active || MatchStart.RecentShipDock != receipt) return false;
        if (_campaignShip is { } current && current.Dock == new NativeCampaignShipDock(receipt.Slot, (int)receipt.Planet))
            return true;
        if (!TryStart(window) || _host == null || _pages == null) return false;
        try
        {
            CloseNativeCampaignEnding(); CloseNativeCampaignShip();
            CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense(); CloseNativeCommunity();
            CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
            _launcherPages?.Suspend();
            var controller = new NativeCampaignShipController<LaunchPlan>(new CurrentNativeCampaignShipBackend(),
                new(receipt.Slot, (int)receipt.Planet), (int)hunter);
            _campaignShip = new(_host, _pages, controller);
            _campaignShip.Open();
            _shown = true;
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); return false; }
    }

    private static void ApplyCampaignShipEffect()
    {
        if (_campaignShip is not { } page || !page.TryTakeEffect(out var effect)) return;
        var receipt = new CampaignDockReceipt(effect.Dock.Slot, (CampaignPlanet)effect.Dock.Planet);
        CloseNativeCampaignShip();
        ReleaseNativeInput();
        if (effect.Launch && MatchStart.RecentShipDock == receipt
            && Shell.RmlUiLaunchCampaignShip(effect.Plan, receipt, effect.Fingerprint)) return;
        _controller?.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure);
    }

    private static void CloseNativeCampaignShip()
    {
        var page = _campaignShip; _campaignShip = null;
        page?.Dispose();
    }
}
#endif
