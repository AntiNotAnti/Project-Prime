#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static NativeCampaignEndingPage? _campaignEnding;

    internal static bool TryOpenNativeCampaignEnding(RenderWindow window, CampaignEndingSummary receipt)
    {
        if (!Requested || _disabled || window.HasScene || !Shell.Active
            || MatchStart.RecentCampaignEnding != receipt) return false;
        if (_campaignEnding is { } current && current.Receipt == receipt) return true;
        if (!TryStart(window) || _host == null || _pages == null) return false;
        try
        {
            CloseNativeCampaignEnding(); CloseNativeCampaignShip();
            CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense(); CloseNativeCommunity();
            CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
            _launcherPages?.Suspend();
            var page = new NativeCampaignEndingPage(_host, _pages, receipt);
            _campaignEnding = page;
            page.Open(window.IsFocused && window.FramebufferSize.X > 0 && window.FramebufferSize.Y > 0);
            _shown = true;
            return true;
        }
        catch (Exception error) when (Recoverable(error)) { Fail(error); return false; }
    }

    private static void ApplyCampaignEndingReturn()
    {
        if (_campaignEnding is not { } page || !page.TryTakeReturn(out var receipt)) return;
        // Clear only the value displayed by this page. A replacement ending
        // receipt belongs to its own journey and cannot be consumed here.
        if (MatchStart.RecentCampaignEnding == receipt) MatchStart.ClearRecentCampaignEnding();
        CloseNativeCampaignEnding();
        ReleaseNativeInput();
        _controller?.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure);
    }

    private static void CloseNativeCampaignEnding()
    {
        var page = _campaignEnding; _campaignEnding = null;
        page?.Dispose();
    }
}
#endif
