using System;
using System.Linq;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods.Launcher.Gui;

namespace MphRead.Mods.Launcher
{
    public readonly record struct MatchLoadStatus(
        MatchTransitionStage Stage,
        string? Detail = null,
        string? MapName = null);

    /// <summary>Loads a server-admitted match or a recorded spectator session.</summary>
    public static class MatchStart
    {
        public static MatchRunResult Run(MenuSettings settings, LaunchPlan plan, Action? started = null,
            Func<MatchResultsSnapshot?, Func<bool>, MatchResultsPresentationResult>? presentResults = null,
            SdlGameHost? host = null, ulong transitionGeneration = 0,
            Action<ulong>? firstFramePresented = null,
            Action<MatchLoadStatus>? progress = null,
            Action<ulong>? windowPrepared = null)
        {
            SdlGameHost? ownedHost = null;
            bool persistentHost = host != null;
            bool didStart = false;
            MatchResultsSnapshot? results = null;
            MatchResultsPresentationResult? presentationResult = null;
            var play = AuthoritativePlay.Current;
            Guid? matchId = play?.NodeMatchId ?? NodeSessions.Current?.State.JoinedMatchId;
            try
            {
                progress?.Invoke(new(MatchTransitionStage.Preparing,
                    "Validating the match request."));
                host ??= ownedHost = new SdlGameHost(showWindow: false);
                if (host.CloseRequested) return new MatchRunResult(MatchExitReason.QuitApplication, matchId);
                RunCore(settings, plan, host, () => { didStart = true; started?.Invoke(); }, (value, pump) =>
                {
                    results = value;
                    // Present over the still-live SDL scene before shell return.
                    // A missing terminal UDP result is explicitly represented by null.
                    if (play?.State == AuthoritativePlay.TerminalState.Completed)
                        presentationResult = presentResults?.Invoke(value, pump);
                }, persistentHost ? SceneExitPresentation.KeepWindowVisible
                    : SceneExitPresentation.HideWindow, transitionGeneration,
                    firstFramePresented, progress, windowPrepared);
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

        private static void RunCore(MenuSettings settings, LaunchPlan plan, SdlGameHost host, Action started,
            Action<MatchResultsSnapshot?, Func<bool>> capture,
            SceneExitPresentation exitPresentation, ulong transitionGeneration,
            Action<ulong>? firstFramePresented, Action<MatchLoadStatus>? progress,
            Action<ulong>? windowPrepared)
        {
            progress?.Invoke(new(MatchTransitionStage.Connecting,
                "Confirming the assigned gameplay session."));
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
                progress?.Invoke(new(MatchTransitionStage.PreparingContent,
                    "Applying the selected content paths."));
                GameFiles.ApplyPaths();
                if (plan.Kind == LaunchKind.Replay)
                {
                    LaunchReplay(plan, host, started, capture, exitPresentation,
                        transitionGeneration, firstFramePresented, progress,
                        windowPrepared);
                    return;
                }
                var room = NetLaunch.ServerRoom()
                    ?? throw new InvalidOperationException("The server has not provided a match room.");
                progress?.Invoke(new(MatchTransitionStage.LoadingArena,
                    "Preparing the selected arena.", room.RoomKey));
                MapGen.MapPreparation.CompileAndMountRoomAsync(room.RoomKey,
                    gameplayIdentity: NetHeader.Version.ToString(), CancellationToken.None)
                    .GetAwaiter().GetResult();
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
                    progress?.Invoke(new(MatchTransitionStage.PreparingPlayers,
                        "Building the frozen player roster.", room.RoomKey));
                    // RunScene creates ScenePresentation before this callback,
                    // preserving the setup order while the SDL host owns the
                    // native window and renderer.
                    NetLaunch.BuildPlayers(scene, plan.Hunter, localRecolor: 0);
                    presentation.AddRoom(room.RoomKey, room.Mode,
                        playerCount: NetLaunch.RoomPlayerCount);
                    progress?.Invoke(new(MatchTransitionStage.Finalizing,
                        "Finalizing scene presentation.", room.RoomKey));
                }, () => capture(CaptureResults(room.RoomKey, room.Mode, scene.Match.Result, AuthoritativePlay.Current), sdlHost.PumpResultsEvents), started,
                    () => AuthoritativePlay.Current?.PumpSceneCompletion(scene, sdlHost) == true,
                    exitPresentation, transitionGeneration, firstFramePresented,
                    windowPrepared);
            }
            finally
            {
                ContentEnvironment.UnmountMap();
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

        private static void LaunchReplay(LaunchPlan plan, SdlGameHost host, Action started,
            Action<MatchResultsSnapshot?, Func<bool>> capture,
            SceneExitPresentation exitPresentation, ulong transitionGeneration,
            Action<ulong>? firstFramePresented, Action<MatchLoadStatus>? progress,
            Action<ulong>? windowPrepared)
        {
            try
            {
                if (!ReplayPlayback.ConsumePrepared(plan.ReplayPath)
                    && !ReplayPlayback.Join(plan.ReplayPath))
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
                progress?.Invoke(new(MatchTransitionStage.LoadingArena,
                    "Loading the recorded arena.", room.Value.RoomKey));
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                var sdlHost = host;
                sdlHost.RunScene(scene, presentation =>
                {
                    progress?.Invoke(new(MatchTransitionStage.PreparingPlayers,
                        "Restoring the recorded roster.", room.Value.RoomKey));
                    NetLaunch.BuildPlayers(scene, Hunter.Samus, localRecolor: 0,
                        teamId: -1, localSlot: -1);
                    presentation.AddRoom(room.Value.RoomKey, room.Value.Mode,
                        playerCount: NetLaunch.RoomPlayerCount);
                    progress?.Invoke(new(MatchTransitionStage.Finalizing,
                        "Finalizing replay presentation.", room.Value.RoomKey));
                }, () => capture(scene.Match.Result is { } result ? new(room.Value.RoomKey, room.Value.Mode, result) : null, sdlHost.PumpResultsEvents), started,
                    exitPresentation: exitPresentation, transitionGeneration: transitionGeneration,
                    firstFramePresented: firstFramePresented,
                    windowPrepared: windowPrepared);
            }
            finally
            {
                ReplayPlayback.Stop();
            }
        }

    }
}
