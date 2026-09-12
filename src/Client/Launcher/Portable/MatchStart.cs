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
            Action<ulong>? windowPrepared = null,
            ClientOnlineRuntime? onlineRuntime = null)
        {
            SdlGameHost? ownedHost = null;
            bool persistentHost = host != null;
            bool didStart = false;
            MatchResultsSnapshot? results = null;
            MatchResultsPresentationResult? presentationResult = null;
            ClientOnlineRuntime? runtime = onlineRuntime ?? ClientOnlineRuntime.Current;
            AuthoritativePlay? play = runtime?.Match?.Play;
            Guid? matchId = play?.NodeMatchId ?? runtime?.Node?.State.JoinedMatchId;
            try
            {
                progress?.Invoke(new(MatchTransitionStage.Preparing,
                    "Validating the match request."));
                host ??= ownedHost = new SdlGameHost(showWindow: false);
                if (host.CloseRequested) return new MatchRunResult(MatchExitReason.QuitApplication, matchId);
                RunCore(settings, plan, host, () => { didStart = true; started?.Invoke(); }, runtime, (value, pump) =>
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
                if (PauseMenu.LeftMatch)
                    return new MatchRunResult(MatchExitReason.LeftMatch, matchId,
                        Results: null);
                if (play?.State == AuthoritativePlay.TerminalState.Transitioning)
                    return new MatchRunResult(MatchExitReason.Transitioning, matchId,
                        Results: null);
                if (presentationResult?.Failure is { } failure)
                    return new MatchRunResult(MatchExitReason.Disconnected, matchId, failure, results);
                return new MatchRunResult(MatchRunResult.Classify(didStart, PauseMenu.QuitProgram || host?.CloseRequested == true || presentationResult?.QuitApplication == true, PauseMenu.LeftMatch,
                    play?.State == AuthoritativePlay.TerminalState.Completed || plan.Kind == LaunchKind.Replay,
                    play?.Interrupted == true, play?.Client.Failure != null, false,
                    transitioning: play?.State == AuthoritativePlay.TerminalState.Transitioning), matchId,
                    play?.Interrupted == true && play.State != AuthoritativePlay.TerminalState.Transitioning
                        ? "The server interrupted the match. Return to your lobby and try again." : null, results);
            }
            catch (Exception ex)
            {
                DebugLog.Exception("match", ex);
                if (play?.State == AuthoritativePlay.TerminalState.Transitioning)
                    return new MatchRunResult(MatchExitReason.Transitioning, matchId,
                        Results: null);
                return new MatchRunResult(MatchRunResult.Classify(didStart, PauseMenu.QuitProgram || host?.CloseRequested == true || presentationResult?.QuitApplication == true, PauseMenu.LeftMatch,
                    false, play?.Interrupted == true, play?.Client.Failure != null, true,
                    transitioning: false), matchId, ex.Message, results);
            }
            finally { ownedHost?.Dispose(); }
        }

        private static void RunCore(MenuSettings settings, LaunchPlan plan, SdlGameHost host,
            Action started, ClientOnlineRuntime? runtime,
            Action<MatchResultsSnapshot?, Func<bool>> capture,
            SceneExitPresentation exitPresentation, ulong transitionGeneration,
            Action<ulong>? firstFramePresented, Action<MatchLoadStatus>? progress,
            Action<ulong>? windowPrepared)
        {
            progress?.Invoke(new(MatchTransitionStage.Connecting,
                "Confirming the assigned gameplay session."));
            plan.Validate();
            if (plan.Kind != LaunchKind.Replay && runtime?.Match?.Play == null)
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
                MapRequirement? requiredMap = runtime?.Node?.Round?.Lobby.RequiredMap
                    ?? runtime?.Node?.Lobby?.RequiredMap;
                RoomContentPreparationResult preparation =
                    MapGen.MapPreparation.PrepareRoomAsync(new RoomContentRequest(
                        room.RoomKey, requiredMap?.ToRoomContentRequirement(),
                        GameplayContentIdentity.Current(
                            BuildIdentity.Display, NetHeader.Version),
                        RoomContentPurpose.Match), CancellationToken.None)
                    .GetAwaiter().GetResult();
                MapGen.MapPreparation.RequirePreparedRoom(preparation);
                settings.RoomKey = room.RoomKey;
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                var sdlHost = host;
                AuthoritativePlay? play = runtime?.Match?.Play;
                ClientSceneServices? sceneServices = runtime?.Match is { } match
                    ? new ClientSceneServices(match, runtime.Node) : null;
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
                }, () => capture(CaptureResults(room.RoomKey, room.Mode, scene.Match.Result, play, runtime?.Node), sdlHost.PumpResultsEvents), started,
                    () => play != null && AuthoritativePlayDesktop.PumpSceneCompletion(play, scene, sdlHost),
                    exitPresentation, transitionGeneration, firstFramePresented,
                    windowPrepared, sceneServices);
            }
            finally
            {
                ContentEnvironment.UnmountMap();
                playLease.Dispose();
                MphRead.Mods.Update.UpdateCoordinator.Shared.SetSafeToRestart(true);
            }
        }

        private static MatchResultsSnapshot? CaptureResults(string mapKey, GameMode mode,
            MatchResult? replicated, AuthoritativePlay? play, NodeControlClient? node)
        {
            if (replicated != null) return new(mapKey, mode, replicated);
            if (play?.CompletionSummary is not { } completion) return null;
            NodeSessionSnapshot? session = node?.Session;
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
                if (!ReplayLaunchCoordinator.TryStart(plan, out string? replayError))
                {
                    throw new InvalidOperationException(replayError
                        ?? "Could not open or read the replay file.");
                }
                (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
                if (room == null)
                {
                    throw new InvalidOperationException("The replay has no match info.");
                }
                progress?.Invoke(new(MatchTransitionStage.LoadingArena,
                    "Loading the recorded arena.", room.Value.RoomKey));
                ReplayMapIdentity? replayMap = ReplayPlayback.MapIdentity;
                if (replayMap != null && !replayMap.RoomKey.Equals(
                    room.Value.RoomKey, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "MAP-RUN-008: Replay map identity does not match its recorded room.");
                RoomContentPreparationResult preparation =
                    MapGen.MapPreparation.PrepareRoomAsync(new RoomContentRequest(
                        room.Value.RoomKey,
                        replayMap?.ToRoomContentRequirement(),
                        GameplayContentIdentity.Current(
                            BuildIdentity.Display, NetHeader.Version),
                        RoomContentPurpose.Replay), CancellationToken.None)
                    .GetAwaiter().GetResult();
                MapGen.MapPreparation.RequirePreparedRoom(preparation);
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
