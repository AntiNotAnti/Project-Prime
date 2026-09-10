using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;
using MphRead.Identity;

namespace MphRead.Mods.Launcher
{
    /// <summary>Loads a server-admitted match or a recorded spectator session.</summary>
    public static class MatchStart
    {
        public static MatchRunResult Run(MenuSettings settings, LaunchPlan plan, Action? started = null,
            Func<MatchResultsSnapshot?, Func<bool>, MatchResultsPresentationResult>? presentResults = null, SdlGameHost? host = null)
        {
            SdlGameHost? ownedHost = null;
            bool didStart = false;
            MatchResultsSnapshot? results = null;
            MatchResultsPresentationResult? presentationResult = null;
            var play = AuthoritativePlay.Current;
            Guid? matchId = play?.NodeMatchId ?? NodeSessions.Current?.State.JoinedMatchId;
            try
            {
                host ??= ownedHost = new SdlGameHost(showWindow: false);
                if (host.CloseRequested) return new MatchRunResult(MatchExitReason.QuitApplication, matchId);
                RunCore(settings, plan, host, () => { didStart = true; started?.Invoke(); }, (value, pump) =>
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
                return new MatchRunResult(MatchRunResult.Classify(didStart, PauseMenu.QuitProgram || host?.CloseRequested == true || presentationResult?.QuitApplication == true, PauseMenu.LeftMatch,
                    play?.State == AuthoritativePlay.TerminalState.Completed || plan.Kind == LaunchKind.Replay,
                    play?.Interrupted == true, play?.Client.Failure != null, false), matchId,
                    play?.Interrupted == true ? "The server interrupted the match. Return to your lobby and try again." : null, results);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("match", ex);
                return new MatchRunResult(MatchRunResult.Classify(didStart, PauseMenu.QuitProgram || host?.CloseRequested == true || presentationResult?.QuitApplication == true, PauseMenu.LeftMatch,
                    false, play?.Interrupted == true, play?.Client.Failure != null, true), matchId, ex.Message, results);
            }
            finally { ownedHost?.Dispose(); }
        }

        private static void RunCore(MenuSettings settings, LaunchPlan plan, SdlGameHost host, Action started, Action<MatchResultsSnapshot?, Func<bool>> capture)
        {
            plan.Validate();
            if (plan.Kind != LaunchKind.Replay && AuthoritativePlay.Current == null)
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
                if (plan.Kind == LaunchKind.Replay)
                {
                    LaunchReplay(plan, host, started, capture);
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
                var sdlHost = host;
                sdlHost.RunScene(scene, presentation =>
                {
                    // RunScene creates ScenePresentation before this callback,
                    // preserving the setup order while the SDL host owns the
                    // native window and renderer.
                    NetLaunch.BuildPlayers(scene, plan.Hunter, localRecolor: 0);
                    presentation.AddRoom(room.RoomKey, room.Mode,
                        playerCount: NetLaunch.RoomPlayerCount);
                }, () => capture(CaptureResults(room.RoomKey, room.Mode, scene.Match.Result, AuthoritativePlay.Current), sdlHost.PumpResultsEvents), started,
                    () => AuthoritativePlay.Current?.PumpSceneCompletion(scene, sdlHost) == true);
            }
            finally
            {
                playLease.Dispose();
                MphRead.Mods.Update.UpdateCoordinator.Shared.SetSafeToRestart(true);
            }
        }

        private static MatchResultsSnapshot? CaptureResults(string mapKey, GameMode mode,
            MatchResult? replicated, AuthoritativePlay? play)
        {
            if (replicated != null) return new(mapKey, mode, replicated);
            if (play?.CompletionSummary is not { } completion) return null;
            NodeSessionSnapshot? session = NodeSessions.Current?.Session;
            PlayerId? playerId = session?.PlayerId is Guid id ? new PlayerId(id) : null;
            return new(mapKey, mode, null, completion, playerId, session?.GuestSessionId);
        }

        private static void LaunchReplay(LaunchPlan plan, SdlGameHost host, Action started, Action<MatchResultsSnapshot?, Func<bool>> capture)
        {
            try
            {
                if (!ReplayPlayback.Join(plan.ReplayPath))
                {
                    throw new InvalidOperationException("Could not open or read the replay file.");
                }
                if (plan.ReplayHighlights is { Count: > 0 } highlights)
                    ReplayPlayback.ConfigureHighlights(highlights);
                (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
                if (room == null)
                {
                    throw new InvalidOperationException("The replay has no match info.");
                }
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                var sdlHost = host;
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
                ReplayPlayback.Stop();
            }
        }

    }
}
