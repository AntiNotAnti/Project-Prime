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
            if (plan.Kind != LaunchKind.Demo
                && AuthoritativePlay.Current!.Client.State == NetConnectionState.Lobby)
            {
                throw new InvalidOperationException("The server has not sent a match transition.");
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
            using var renderer = new RenderWindow();
            ClientSessionCoordinator coordinator = ClientSessionCoordinator.Shared;
            if (coordinator.Session == null)
                coordinator.AttachSession(AuthoritativePlay.Current!);
            coordinator.AttachScene(renderer.Scene);
            try
            {
                NetLaunch.BuildPlayers(renderer.Scene, plan.Hunter, localRecolor: 0);
                renderer.AddRoom(room.RoomKey, room.Mode, playerCount: NetLaunch.RoomPlayerCount);
                renderer.Run();
                if (PauseMenu.LeftServer) coordinator.Leave();
            }
            catch (Exception error)
            {
                if (!coordinator.CanReconnect) coordinator.Fail(error.Message);
                throw;
            }
            finally
            {
                coordinator.DetachScene(renderer.Scene);
            }
        }

        private static void LaunchDemo(LaunchPlan plan)
        {
            PlayerEntity.MaxPlayers = PlayerEntity.SlotCapacity;
            if (!DemoPlayback.Join(plan.DemoPath))
            {
                Console.WriteLine("[demo] could not open or read the demo file");
                return;
            }
            (string RoomKey, GameMode Mode)? room = NetLaunch.ServerRoom();
            if (room == null)
            {
                Console.WriteLine("[demo] the demo has no match info");
                DemoPlayback.Stop();
                return;
            }
            using var renderer = new RenderWindow();
            NetLaunch.BuildPlayers(renderer.Scene, Hunter.Samus, localRecolor: 0, teamId: -1, localSlot: -1);
            renderer.AddRoom(room.Value.RoomKey, room.Value.Mode, playerCount: NetLaunch.RoomPlayerCount);
            renderer.Run();
            DemoPlayback.Stop();
        }

    }
}
