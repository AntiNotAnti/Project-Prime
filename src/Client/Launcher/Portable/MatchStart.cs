using System;
using MphRead.Entities;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>Loads a server-admitted match or a recorded spectator session.</summary>
    public static class MatchStart
    {
        public static void Launch(MenuSettings settings, LaunchPlan plan)
        {
            plan.Validate();
            if (plan.Kind != LaunchKind.Demo && AuthoritativePlay.Current == null)
            {
                throw new InvalidOperationException("Join an authoritative server before starting a match.");
            }
            if (!GameFiles.Ready)
            {
                Console.WriteLine("[launcher] no game files; nothing to load");
                return;
            }
            GameFiles.ApplyPaths();
            MapGen.MapPreparation.GenerateMissing();
            if (plan.Kind == LaunchKind.Demo)
            {
                LaunchDemo(plan);
                return;
            }
            var room = NetLaunch.ServerRoom()
                ?? throw new InvalidOperationException("The server has not provided a match room.");
            string? unplayable = MapGen.CustomRooms.WhyUnplayable(room.RoomKey);
            if (unplayable != null)
            {
                Console.WriteLine($"[launcher] {unplayable}");
                return;
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
            });
        }

        private static void LaunchDemo(LaunchPlan plan)
        {
            try
            {
                if (!DemoPlayback.Join(plan.DemoPath))
                {
                    Console.WriteLine("[demo] could not open or read the demo file");
                    return;
                }
                (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
                if (room == null)
                {
                    Console.WriteLine("[demo] the demo has no match info");
                    return;
                }
                var scene = new Scene(features: ClientMatchFeatures.Capture());
                using var sdlHost = new SdlGameHost();
                sdlHost.RunScene(scene, presentation =>
                {
                    NetLaunch.BuildPlayers(scene, Hunter.Samus, localRecolor: 0,
                        teamId: -1, localSlot: -1);
                    presentation.AddRoom(room.Value.RoomKey, room.Value.Mode,
                        playerCount: NetLaunch.RoomPlayerCount);
                });
            }
            finally
            {
                DemoPlayback.Stop();
            }
        }

    }
}
