using System;
using MphRead.Entities;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using ProjectPrime.Server.Shared;
using MapPreparation = MphRead.Mods.MapGen.MapPreparation;

namespace MphRead.Droid
{
    /// <summary>
    /// Builds an Android client scene from an admitted server session or a replay.
    /// <see cref="GameView"/> owns the GL thread and scene lifetime.
    /// </summary>
    internal static class AndroidMatch
    {
        /// <summary>Runs on the GL thread: everything below it touches GL.</summary>
        public static Scene Build(AndroidInput input, Vector2i size, LaunchPlan plan, Action close,
            FrameTiming timing)
        {
            plan.Validate();
            ClientOnlineRuntime? runtime = ClientOnlineRuntime.Current;
            if (plan.Kind != LaunchKind.Replay && runtime?.Match?.Play == null)
            {
                throw new ProgramException("Join a server before starting a match.");
            }
            GameFiles.ApplyPaths();
            AndroidMaps.RefreshCatalog();
            if (plan.Kind == LaunchKind.Replay)
            {
                return BuildReplay(input, size, plan, close, runtime, timing);
            }
            (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
            if (room == null || String.IsNullOrWhiteSpace(room.Value.RoomKey))
            {
                throw new ProgramException("The server did not say which map it is running.");
            }
            MapRequirement? requiredMap = runtime?.Node?.Round?.Lobby.RequiredMap
                ?? runtime?.Node?.Lobby?.RequiredMap;
            RoomContentPreparationResult preparation =
                MapPreparation.PrepareRoomAsync(new RoomContentRequest(
                    room.Value.RoomKey, requiredMap?.ToRoomContentRequirement(),
                    GameplayContentIdentity.Current(
                        BuildIdentity.Display, NetHeader.Version),
                    RoomContentPurpose.Match),
                    System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();
            MapPreparation.RequirePreparedRoom(preparation);
            var scene = new Scene(features: ClientMatchFeatures.Capture());
            var presentation = new ScenePresentation(scene, size, input.Keyboard, input.Mouse,
                _ => { }, close, timing);
            bool teamPlay = room.Value.Mode.IsTeamMode();
            NetLaunch.BuildPlayers(scene, plan.Hunter, localRecolor: 0,
                teamId: teamPlay ? 0 : -1);
            presentation.AddRoom(room.Value.RoomKey, room.Value.Mode, playerCount: NetLaunch.RoomPlayerCount);
            return scene;
        }

        /// <summary>
        /// A recorded match, played back -- the half of
        /// <c>MatchStart.LaunchReplay</c> that is not a window.
        ///
        /// The file is fed to <see cref="NetSession"/> exactly as a live
        /// connection would be, so every packet handler, room transition and
        /// match-end sequence runs unchanged; what makes it a replay rather
        /// than a match is that there is no local slot (-1) and so no player
        /// to spawn as. <c>SpectatorMode</c> takes the camera on the
        /// first frame anybody recorded becomes available.
        ///
        /// The room comes from the recording itself: a replay carries the
        /// server's own MatchState, which is what <see cref="NetLaunch.ServerRoom"/>
        /// reads. <see cref="ReplayPlayback.Prepare"/> may already have been called
        /// by the screen that picked the file -- it reports a bad file there,
        /// where there is still something to report on. Consume that prepared
        /// reader when the path still matches; otherwise Join rewinds the file
        /// for the run about to start.
        /// </summary>
        private static Scene BuildReplay(AndroidInput input, Vector2i size,
            LaunchPlan plan, Action close, ClientOnlineRuntime? runtime, FrameTiming timing)
        {
            if (!ReplayLaunchCoordinator.TryStart(plan, out string? replayError))
            {
                throw new ProgramException(replayError
                    ?? "That file could not be read as a replay.");
            }
            (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
            if (room == null)
            {
                ReplayPlayback.Stop();
                throw new ProgramException("The replay has no match info in it.");
            }
            ReplayMapIdentity? replayMap = ReplayPlayback.MapIdentity;
            if (replayMap != null && !replayMap.RoomKey.Equals(
                room.Value.RoomKey, StringComparison.OrdinalIgnoreCase))
                throw new ProgramException(
                    "MAP-RUN-008: Replay map identity does not match its recorded room.");
            RoomContentPreparationResult preparation =
                MapPreparation.PrepareRoomAsync(new RoomContentRequest(
                    room.Value.RoomKey,
                    replayMap?.ToRoomContentRequirement(),
                    GameplayContentIdentity.Current(
                        BuildIdentity.Display, NetHeader.Version),
                    RoomContentPurpose.Replay),
                    System.Threading.CancellationToken.None)
                .GetAwaiter().GetResult();
            MapPreparation.RequirePreparedRoom(preparation);
            var scene = new Scene(features: ClientMatchFeatures.Capture());
            var presentation = new ScenePresentation(scene, size, input.Keyboard, input.Mouse,
                _ => { }, close, timing,
                runtime?.Match is { } match ? new ClientSceneServices(match, runtime.Node) : null);
            NetLaunch.BuildPlayers(scene, Hunter.Samus, localRecolor: 0, teamId: -1, localSlot: -1);
            presentation.AddRoom(room.Value.RoomKey, room.Value.Mode, playerCount: NetLaunch.RoomPlayerCount);
            Console.WriteLine($"[match] replay, {room.Value.RoomKey}");
            return scene;
        }
    }
}
