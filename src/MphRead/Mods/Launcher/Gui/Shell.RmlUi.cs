#if MPHREAD_RMLUI_POC
using System;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui
{
    internal static partial class Shell
    {
        private static LobbySessionController? _rmlLobby;
        private static LaunchPlan _rmlLobbyPlan;
        private static long _rmlLobbyFrame;
        private static (Guid Lifetime, ushort Match, ulong Epoch, uint Start)? _rmlPendingStart;
        private static LobbySessionController? _pendingLobbyController;

        private static void OpenRmlLobby(LaunchPlan plan)
        {
            RetireNativePages();
            WireNativePages();
            ReleaseRmlLobby();
            _rmlLobbyRules?.ResetSession();
            SpectatorMode.SetSessionPreference(plan.Spectate);
            _rmlLobbyPlan = plan;
            var controller = new LobbySessionController(plan.Lobby);
            _rmlLobby = controller;
            _rmlLobbyFrame = 0;
            controller.MatchRequested += RmlMatchRequested;
            controller.Closed += RmlLobbyClosed;
            controller.TransferPumpOwnership(LobbyPumpOwner.Native);
            ApplicationRouter.Reset(new(LauncherPage.Lobby));
            RmlUiPrototype.PresentLobby(controller.Snapshot());
            if (_window == null || !RmlUiPrototype.EnterLobby(_window,
                    plan.Lobby?.ServerName, plan.Lobby?.Endpoint))
            {
                OpenLegacyRmlLobby();
                return;
            }
            // Wiring precedes TickUi's first pump. Opening a lobby here must not
            // add a second pump to the same engine frame.
        }

        private static void PumpRmlLobby()
        {
            if (_rmlLobby is not { } controller) return;
            controller.PumpOnce(LobbyPumpOwner.Native, ++_rmlLobbyFrame);
            if (ReferenceEquals(controller, _rmlLobby))
                RmlUiPrototype.PresentLobby(controller.Snapshot());
        }

        private static void DispatchRmlLobby(LobbyIntentKind kind)
        {
            if (_rmlLobby is not { } controller) return;
            LobbyActionResult result = controller.Dispatch(controller.Intent(kind));
            if (!result.Accepted)
                RmlUiPrototype.SetMenuText("lobby_status", result.Message);
            if (ReferenceEquals(controller, _rmlLobby))
                RmlUiPrototype.PresentLobby(controller.Snapshot());
        }

        private static void RmlMatchRequested(object? sender, LaunchPlan plan)
        {
            if (!ReferenceEquals(sender, _rmlLobby)) return;
            RetireNativePages();
            _rmlLobbyRules?.ResetSession();
            LobbySnapshot snapshot = _rmlLobby!.Snapshot();
            _rmlPendingStart = (snapshot.Lifetime, snapshot.MatchId,
                snapshot.AuthorityEpoch, snapshot.StartGeneration);
            Decided(plan);
        }

        private static bool ValidateRmlPendingPlan()
        {
            if (_nativeLaunchFailure != null && !RmlUiPrototype.Runtime.IsAlive(_nativeLaunchDocument))
            {
                _nativeLaunchFailure = null;
                _nativeLaunchDocument = default;
                return false;
            }
            if (_rmlPendingStart is not { } expected) return true;
            if (_pendingLobbyController is not { } controller) return false;
            LobbySnapshot current = controller.Snapshot();
            bool valid = current.Active && !current.Closed && current.ShouldLoadMatch
                && current.Lifetime == expected.Lifetime && current.MatchId == expected.Match
                && current.AuthorityEpoch == expected.Epoch && current.StartGeneration == expected.Start;
            if (!valid) _rmlPendingStart = null;
            return valid;
        }

        private static void RmlLobbyClosed(object? sender, string reason)
        {
            if (!ReferenceEquals(sender, _rmlLobby)) return;
            // A disconnect or Leave can occur while the scene is prewarming.
            // Invalidate the queued handoff before the next BeforeFrame loads it.
            _pending = null;
            _rmlPendingStart = null;
            ReleaseRmlLobby();
            _rmlLobbyRules?.ResetSession();
            RmlUiPrototype.ExitLobby();
            ApplicationRouter.Reset(new(LauncherPage.Home));
            if (_window?.HasScene == true) _endMatch = true;
            if (!String.IsNullOrWhiteSpace(reason))
                RmlUiPrototype.SetMenuText("system_status", reason);
        }

        private static void ReleaseRmlLobby(bool retirePages = true)
        {
            if (retirePages) RetireNativePages();
            LobbySessionController? controller = _rmlLobby;
            _rmlLobby = null;
            _rmlPendingStart = null;
            _pendingLobbyController = null;
            if (controller == null) return;
            controller.MatchRequested -= RmlMatchRequested;
            controller.Closed -= RmlLobbyClosed;
            controller.Dispose();
            RmlUiPrototype.ClearLobbySnapshot();
        }

#if MPHREAD_AVALONIA
        private static void OpenLegacyRmlLobby()
        {
            if (_rmlLobby is not { } controller) return;
            // An explicit rollback transfers the same authority, never creates
            // another controller, connection, or independently advancing clock.
            if (!GuiLauncher.EnsureSetup() || UiSurface.Ensure() == null)
            {
                ReleaseRmlLobby();
                RequestQuit();
                return;
            }
            RmlUiPrototype.Shutdown();
            ShowFrontScreen();
            if (_front == null)
            {
                ReleaseRmlLobby();
                RequestQuit();
                return;
            }
            _rmlLobby = null;
            controller.MatchRequested -= RmlMatchRequested;
            controller.Closed -= RmlLobbyClosed;
            controller.TransferPumpOwnership(LobbyPumpOwner.Legacy);
            _front.OpenConnectedFromRml(_rmlLobbyPlan, controller);
        }

        private static bool RestoreLegacyRmlPresentation()
        {
            if (_rmlLobby != null)
            {
                OpenLegacyRmlLobby();
                return _front != null && !_quit;
            }
            RmlUiPrototype.Shutdown();
            if (!GuiLauncher.EnsureSetup() || UiSurface.Ensure() == null)
            {
                RequestQuit();
                return false;
            }
            ShowFrontScreen();
            return _front != null;
        }
#endif

        private static bool TryRestoreRmlHome(RenderWindow window)
        {
            if (!RmlUiPrototype.Requested || RmlUiPrototype.Failed) return false;
            if (!RmlUiPrototype.Active && !RmlUiPrototype.TryActivate(window)) return false;
            RetireNativePages();
            WireNativePages();
            // Retire any prior rollback presenter before another session can
            // connect; its backend must never stop or hydrate the new lobby.
#if MPHREAD_AVALONIA
            _front?.RetireLobby();
#endif
            _pendingLobbyController = null;
            _rmlPendingStart = null;
            _settings = GameState.LoadSettings();
            Mods.GameSettings.Apply(_settings);
            LauncherPrefs.Load();
            Hunters.Reroll();
            RmlUiPrototype.ExitLobby();
            RmlUiPrototype.Show();
            ApplicationRouter.Reset(new(LauncherPage.Home));
            window.Title = Mods.Branding.Name;
            return true;
        }
    }
}
#endif
