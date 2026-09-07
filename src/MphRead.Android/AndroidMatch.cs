using System;
using MphRead.Entities;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Droid
{
    /// <summary>
    /// Builds an Android client scene from an admitted server session or a demo.
    /// <see cref="GameView"/> owns the GL thread and scene lifetime.
    /// </summary>
    internal static class AndroidMatch
    {
        /// <summary>Runs on the GL thread: everything below it touches GL.</summary>
        public static Scene Build(AndroidInput input, Vector2i size, LaunchPlan plan, Action close)
        {
            plan.Validate();
            if (plan.Kind != LaunchKind.Demo && AuthoritativePlay.Current == null)
            {
                throw new ProgramException("Join a server before starting a match.");
            }
            GameFiles.ApplyPaths();
            // Build missing custom maps before any room is loaded.
            AndroidMaps.EnsureBuilt();
            if (plan.Kind == LaunchKind.Demo)
            {
                return BuildDemo(input, size, plan, close);
            }
            (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
            if (room == null || String.IsNullOrWhiteSpace(room.Value.RoomKey))
            {
                throw new ProgramException("The server did not say which map it is running.");
            }
            Menu.SaveSlot = 0;
            var scene = new Scene(size, input.Keyboard, input.Mouse, _ => { }, close);
            bool teamPlay = GameState.IsTeamMode(room.Value.Mode);
            NetLaunch.BuildPlayers(scene, plan.Hunter, localRecolor: 0,
                teamId: teamPlay ? 0 : -1);
            scene.AddRoom(room.Value.RoomKey, room.Value.Mode, playerCount: NetLaunch.RoomPlayerCount);
            return scene;
        }

        /// <summary>
        /// A recorded match, played back -- the half of
        /// <c>MatchStart.LaunchDemo</c> that is not a window.
        ///
        /// The file is fed to <see cref="NetSession"/> exactly as a live
        /// connection would be, so every packet handler, room transition and
        /// match-end sequence runs unchanged; what makes it a replay rather
        /// than a match is that there is no local slot (-1) and so no player
        /// to spawn as. <see cref="SpectatorMode"/> takes the camera on the
        /// first frame anybody recorded becomes available.
        ///
        /// The room comes from the recording itself: a demo carries the
        /// server's own MatchState, which is what <see cref="NetLaunch.ServerRoom"/>
        /// reads. <see cref="DemoPlayback.Join"/> has already been called by
        /// the screen that picked the file -- it reports a bad file there,
        /// where there is still something to report on -- and calling it again
        /// here is what re-winds the reader for the run about to start.
        /// </summary>
        private static Scene BuildDemo(AndroidInput input, Vector2i size,
            LaunchPlan plan, Action close)
        {
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            if (!DemoPlayback.Join(plan.DemoPath))
            {
                throw new ProgramException(DemoPlayback.LastError
                    ?? "That file could not be read as a demo.");
            }
            (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
            if (room == null)
            {
                DemoPlayback.Stop();
                throw new ProgramException("The demo has no match info in it.");
            }
            Menu.SaveSlot = 0;
            var scene = new Scene(size, input.Keyboard, input.Mouse, _ => { }, close);
            NetLaunch.BuildPlayers(scene, Hunter.Samus, localRecolor: 0, teamId: -1, localSlot: -1);
            scene.AddRoom(room.Value.RoomKey, room.Value.Mode, playerCount: NetLaunch.RoomPlayerCount);
            Console.WriteLine($"[match] demo, {room.Value.RoomKey}");
            return scene;
        }
    }
}
