using System;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>Loads a server-admitted match or a recorded spectator session.</summary>
    public static class MatchStart
    {
        public static MatchRunResult Run(MenuSettings settings, LaunchPlan plan, Action? started = null,
            Func<MatchResultsSnapshot?, Func<bool>, MatchResultsPresentationResult>? presentResults = null)
        {
            bool didStart = false;
            MatchResultsSnapshot? results = null;
            MatchResultsPresentationResult? presentationResult = null;
            var play = AuthoritativePlay.Current;
            Guid? matchId = play?.NodeMatchId ?? NodeSessions.Current?.State.JoinedMatchId;
            try
            {
                RunCore(settings, plan, () => { didStart = true; started?.Invoke(); }, (value, pump) =>
                {
                    results = value;
                    // Present over the still-live SDL scene before shell return.
                    // A missing terminal UDP result is explicitly represented by null.
                    if (play?.State == AuthoritativePlay.TerminalState.Completed)
                        presentationResult = presentResults?.Invoke(value, pump);
                });
                if (presentationResult?.QuitApplication == true)
                    return new MatchRunResult(MatchExitReason.QuitApplication, matchId, Results: results);
                if (presentationResult?.Failure is { } failure)
                    return new MatchRunResult(MatchExitReason.Disconnected, matchId, failure, results);
                return new MatchRunResult(MatchRunResult.Classify(didStart, PauseMenu.QuitProgram || presentationResult?.QuitApplication == true, PauseMenu.LeftMatch,
                    play?.State == AuthoritativePlay.TerminalState.Completed || plan.Kind == LaunchKind.Demo,
                    play?.Interrupted == true, play?.Client.Failure != null, false), matchId,
                    play?.Interrupted == true ? "The server interrupted the match. Return to your lobby and try again." : null, results);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("match", ex);
                return new MatchRunResult(MatchRunResult.Classify(didStart, PauseMenu.QuitProgram || presentationResult?.QuitApplication == true, PauseMenu.LeftMatch,
                    false, play?.Interrupted == true, play?.Client.Failure != null, true), matchId, ex.Message, results);
            }
        }

        private static void RunCore(MenuSettings settings, LaunchPlan plan, Action started, Action<MatchResultsSnapshot?, Func<bool>> capture)
        {
            plan.Validate();
            if (plan.Kind != LaunchKind.Demo && AuthoritativePlay.Current == null)
            {
                throw new InvalidOperationException("Join an authoritative server before starting a match.");
            }
            if (!GameFiles.Ready)
            {
                throw new InvalidOperationException("Install game files before starting a match.");
            }
            IDisposable? playLease = MphRead.Mods.Update.UpdateCoordinator.Shared.AcquirePlayLease();
            if (playLease == null)
            {
                throw new InvalidOperationException(
                    "An update is restarting; try again after it finishes.");
            }
            MphRead.Mods.Update.UpdateCoordinator.Shared.SetSafeToRestart(false);
            try
            {
                GameFiles.ApplyPaths();
                MapGen.MapPreparation.GenerateMissing();
                if (plan.Kind == LaunchKind.Demo)
                {
                    LaunchDemo(plan, started, capture);
                    return;
                }
                var room = NetLaunch.ServerRoom()
                    ?? throw new InvalidOperationException("The server has not provided a match room.");
                string? unplayable = MapGen.CustomRooms.WhyUnplayable(room.RoomKey);
                if (unplayable != null)
                {
                    throw new InvalidOperationException(unplayable);
                }
                settings.RoomKey = room.RoomKey;
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                using var sdlHost = new SdlGameHost();
                sdlHost.RunScene(scene, presentation =>
                {
                    // RunScene creates ScenePresentation before this callback,
                    // preserving the setup order while the SDL host owns the
                    // native window and renderer.
                    NetLaunch.BuildPlayers(scene, plan.Hunter, localRecolor: 0);
                    presentation.AddRoom(room.RoomKey, room.Mode,
                        playerCount: NetLaunch.RoomPlayerCount);
                }, () => capture(scene.Match.Result is { } result ? new(room.RoomKey, room.Mode, result) : null, sdlHost.PumpResultsEvents), started);
            }
            finally
            {
                playLease.Dispose();
                MphRead.Mods.Update.UpdateCoordinator.Shared.SetSafeToRestart(true);
            }
        }

        private static void LaunchDemo(LaunchPlan plan, Action started, Action<MatchResultsSnapshot?, Func<bool>> capture)
        {
            try
            {
                if (!DemoPlayback.Join(plan.DemoPath))
                {
                    throw new InvalidOperationException("Could not open or read the demo file.");
                }
                (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
                if (room == null)
                {
                    throw new InvalidOperationException("The demo has no match info.");
                }
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                using var sdlHost = new SdlGameHost();
                sdlHost.RunScene(scene, presentation =>
                {
                    NetLaunch.BuildPlayers(scene, Hunter.Samus, localRecolor: 0,
                        teamId: -1, localSlot: -1);
                    presentation.AddRoom(room.Value.RoomKey, room.Value.Mode,
                        playerCount: NetLaunch.RoomPlayerCount);
                }, () => capture(scene.Match.Result is { } result ? new(room.Value.RoomKey, room.Value.Mode, result) : null, sdlHost.PumpResultsEvents), started);
            }
            finally
            {
                DemoPlayback.Stop();
            }
        }

    }
}
