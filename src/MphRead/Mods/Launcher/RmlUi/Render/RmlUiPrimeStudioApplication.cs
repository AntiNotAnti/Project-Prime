#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.StudioIntegration;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private static RmlUiDocumentToken _applicationHeader;
    internal delegate bool StudioWorkspaceLaunch(bool replay, out string? error);
    internal static StudioWorkspaceLaunch? StudioWorkspaceLaunchOverride { get; set; }

    private static void OpenPrimeStudioDestination()
    {
        CloseNativeSocial(); CloseNativeStudio(); CloseNativeLicense();
        CloseNativeCommunity(); CloseNativeHunter(); CloseNativeQuickSettings(); CloseNativeNews();
        OpenNativeStudio();
    }

    internal static bool TryOpenPrimeStudioHub()
    {
        if (!Requested || _disabled || !Shell.Active || Shell.Window is not { HasScene: false } window
            || _controller?.Joining == true || _services?.Lobby().Active == true
            || !PrepareFullSettingsNavigation()
            || _quickSettings is { } settings && !settings.PrepareNavigation()
            || _hunterChoice is { } hunter && !hunter.PrepareNavigation() || !TryStart(window)) return false;
        OpenPrimeStudioDestination();
        return true;
    }

    private static void LaunchPrimeStudioWorkspace(NativeStudioEntryPage page, bool replay)
    {
        if (Shell.StudioWindow) return;
        bool accepted = (StudioWorkspaceLaunchOverride ?? StudioApplicationLauncher.TryOpenWorkspace)(replay, out string? error);
        string status = accepted ? replay ? "REPLAY STUDIO OPENED IN A SEPARATE APP"
            : "MAP STUDIO // COMING SOON IN PRIME STUDIO" : error ?? "PRIME STUDIO COULD NOT OPEN";
        page.ShowStatus(status);
        _controller?.ReportStudioLaunch(accepted, status);
    }

    private static void PumpPrimeStudioApplication(RenderWindow window)
    {
        if (!Shell.StudioWindow || Shell.NativeStartupPending || window.HasScene || _controller == null || _controller.Busy
            || _quickSettings is { CanNavigateAway: false }
            || _fullSettings is { CanNavigateAway: false } || _hudEditor != null) return;
        if (PumpNativeStudioProjectOpen(window)) return;
        // Requests wait while an editor owns playback. They never replace a
        // running scene or silently discard a camera/timeline draft.
        Shell.StudioApplication?.Pump(request =>
        {
            if (request.Kind == StudioOpenKind.Home) OpenPrimeStudioDestination();
            else if (request.Kind == StudioOpenKind.ReplayLibrary)
            {
                CloseNativeStudio(); CloseNativeQuickSettings(); CloseNativeNews();
                _controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre);
            }
            else
            {
                CloseNativeStudio(); CloseNativeQuickSettings(); CloseNativeNews();
                _controller.OpenStudioReplayPath(request.Path!, request.Recover);
            }
            try { window.Focus(); } catch (InvalidOperationException) { }
        }, maximum: 1);
    }

    private static void PresentApplicationHeader(RmlUiHost host)
    {
        if (_pages is not { } pages || pages.Page == default) return;
        if (_applicationHeader != pages.Page)
        {
            host.Update();
            _applicationHeader = pages.Page;
            host.SetBool(pages.Page, "class:@document:native-page-enter", true);
        }
        RmlUiHeaderTab active = _studio != null ? RmlUiHeaderTab.Studio
            : _hunterChoice != null || _license is { IsOverlay: false } ? RmlUiHeaderTab.Hunters
            : _community != null ? RmlUiHeaderTab.Community
            : _quickSettings != null || _fullSettings != null || _hudEditor != null || _social != null || _party != null ? RmlUiHeaderTab.None
            : _news != null ? RmlUiHeaderTab.Play
            : _launcherPages?.Page == RmlUiMenuPage.Theatre ? RmlUiHeaderTab.Theatre
            : RmlUiHeaderTab.Play;
        RmlUiApplicationHeader.Apply(host, pages.Page, Shell.StudioWindow, active);
    }
}
#endif
