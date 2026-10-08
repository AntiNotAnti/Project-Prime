#if !ANDROID && !MPHREAD_SERVER
using MphRead.Mods.Network;
using ProjectPrime.Studio.Protocol;

namespace MphRead.Mods.Launcher.Gui;

internal static partial class Shell
{
#if MPHREAD_RMLUI_POC
    private sealed record DeferredStudioPlaytest(string Room, RmlUi.Settings.SettingsPagePresenter Owner);
    private static DeferredStudioPlaytest? _deferredStudioPlaytest;
    private static void CancelDeferredStudioPlaytest() => _deferredStudioPlaytest = null;
#endif
    internal static bool ExternalStudioLaunchPending => _pending != null
#if MPHREAD_RMLUI_POC
        || _deferredStudioPlaytest is { } deferred && ReferenceEquals(_nativeSettings, deferred.Owner)
            && deferred.Owner.HasPendingLeave
#endif
        ;
    internal static bool IsExternalStudioPlaytestRunning(string room)
        => _window?.HasScene == true && _played is { IsPlaytest: true } played && played.RoomKey == room;
    internal static bool QueueExternalStudioPlaytest(string room, StudioPlaytestOptions options)
    {
        if (!Active || _window?.HasScene == true || _pending != null || NetSession.Active) return false;
#if MPHREAD_RMLUI_POC
        if (RmlUiPrototype.Active && NativeSetupBlocksNavigation) return false;
        if (RmlUiPrototype.Active && NativeSettingsForeground)
        {
            var request = new DeferredStudioPlaytest(room, _nativeSettings!);
            _deferredStudioPlaytest = request;
            request.Owner.RequestLeave(() =>
            {
                if (ReferenceEquals(_deferredStudioPlaytest, request))
                {
                    CancelDeferredStudioPlaytest();
                    QueueExternalStudioPlaytest(room, options);
                }
                else if (ReferenceEquals(_nativeSettings, request.Owner) && request.Owner.Document == default)
                {
                    NavigateNativeRoute(RmlUi.Host.RmlUiRouteArgument.Home);
                }
            });
            return true;
        }
        if (RmlUiPrototype.Active)
        {
            if (ApplicationRouter.Navigate(new(Core.LauncherPage.Home)) == Core.LauncherNavigationOutcome.Blocked)
                return false;
            _rmlMultiplayer?.Cancel();
            RetireNativePages();
            RmlUiPrototype.Pages?.ShowBaseline(RmlUi.Host.RmlUiMenuPage.Home);
            RmlUiPrototype.Show();
        }
#endif
        _settings.RoomKey = room;
        _pending = new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = room, IsPlaytest = true,
            Hunter = (Hunter)options.Hunter, Mode = GameMode.Battle, Bots = options.Bots,
            BotLevel = options.BotLevel, PlayerName = "Map author" };
        return true;
    }
    internal static void StopExternalStudioPlaytest(string room)
    {
#if MPHREAD_RMLUI_POC
        if (_deferredStudioPlaytest?.Room == room) CancelDeferredStudioPlaytest();
#endif
        if (_pending is { IsPlaytest: true } pending && pending.RoomKey == room) _pending = null;
        if (IsExternalStudioPlaytestRunning(room)) RequestEndMatch();
    }
    internal static bool OpenExternalStudioHosting(string room, string? communityAddress = null)
    {
        if (!Active || _window?.HasScene == true || _pending != null || NetSession.Active) return false;
#if MPHREAD_RMLUI_POC
        if (RmlUiPrototype.Active && NativeSetupBlocksNavigation) return false;
        CancelDeferredStudioPlaytest();
        if (RmlUiPrototype.Active && NativeSettingsForeground)
        {
            _nativeSettings!.RequestLeave(() => OpenExternalStudioHosting(room, communityAddress));
            return true;
        }
        if (RmlUiPrototype.Active && _nativeSetup != null && !_nativeSetup.Controller.CanLeave) return false;
#endif
#if MPHREAD_AVALONIA
        if (
#if MPHREAD_RMLUI_POC
            !RmlUiPrototype.Active &&
#endif
            _front == null) return false;
#else
        if (!RmlUiPrototype.Active) return false;
#endif
        if (communityAddress != null)
        {
            using var validation = new MphRead.Mods.MapGen.MapCommunityClient(communityAddress);
            // Preserve the selected source through the canonical host/lobby advertisement path.
            System.IO.Directory.CreateDirectory(LauncherPrefs.Directory);
            System.IO.File.WriteAllText(System.IO.Path.Combine(LauncherPrefs.Directory, "map-community.txt"), communityAddress.Trim());
            System.Environment.SetEnvironmentVariable("PROJECT_PRIME_MAP_COMMUNITY", communityAddress.Trim());
        }
#if MPHREAD_RMLUI_POC
        if (RmlUiPrototype.Active)
        {
            if (ApplicationRouter.Navigate(new(Core.LauncherPage.Play)) == Core.LauncherNavigationOutcome.Blocked) return false;
            RetireNativePages();
            RmlUiPrototype.Pages?.ShowBaseline(RmlUi.Host.RmlUiMenuPage.Play);
            bool available = EnsureRmlMultiplayer().OpenCreateForRoom(room);
            RmlUiPrototype.Show();
            return available;
        }
#endif
#if MPHREAD_AVALONIA
        _front!.OpenStudioHosting(room);
        return true;
#else
        return false;
#endif
    }
}
#endif
