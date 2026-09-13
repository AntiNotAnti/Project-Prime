using System.Diagnostics;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>Game-thread entry points for authoritative play and passive replay presentation.</summary>
    public static class NetHooks
    {
        private static AuthoritativePlay? ResolvePlay(AuthoritativePlay? play)
            => play ?? ClientOnlineRuntime.Current?.Match?.Play;

        public static int LocalSlot => GetLocalSlot();

        public static int GetLocalSlot(AuthoritativePlay? play = null) => ResolvePlay(play)?.LocalSlot
            ?? (ReplayPlayback.IsActive ? -1 : 0);

        public static bool KeepSlotAlive(PlayerEntity player, AuthoritativePlay? play = null)
            => NetSession.Active || ResolvePlay(play) != null;

        public static bool TryApplyRemoteInput(PlayerEntity player, int slot,
            AuthoritativePlay? play = null)
        {
            AuthoritativePlay? activePlay = ResolvePlay(play);
            int localSlot = GetLocalSlot(activePlay);
            if (activePlay != null && slot != localSlot)
            {
                player.Controls.ClearAll();
                return true;
            }
            if (!NetSession.Active) { return false; }
            // Legacy replays may contain recorded controls for animation.
            // They have no socket and cannot submit gameplay state.
            if (player.LoadFlags.TestFlag(LoadFlags.Active) && NetSession.RemoteIntentValid[slot])
            {
                NetPlayerBridge.ApplyIntent(player, NetSession.RemoteIntents[slot]);
            }
            else { player.Controls.ClearAll(); }
            return true;
        }

        public static bool ForceSpawn(PlayerEntity player) => false;

        public static void AfterInput(Scene scene, AuthoritativePlay? play = null)
        {
            if (ResolvePlay(play) is { } activePlay)
            {
                activePlay.BeforeSimulation(scene);
                return;
            }
            if (ReplayPlayback.IsModern)
            {
                ReplayPlayback.BeforeSimulation(scene);
                return;
            }
            if (!NetSession.Active) { return; }
            NetSession.ApplyRoster(scene);
            NetSession.ApplyRandom(scene);
            NetRoomChange.Sync(scene);
            NetDiagnostics.Report(scene, NetSession.NetFrame / 60.0);
            NetPlayerSetup.ApplyOnce(scene);
            NetMatchEnd.Sync(scene);
            NetMatchSync.Apply(scene);
            NetSlotManager.Sync(scene);
            NetLog.Snapshot(NetSession.NetFrame / 60.0, scene);
        }

        public static void AfterSimulation(Scene scene, AuthoritativePlay? play = null)
        {
            if (ResolvePlay(play) is { } activePlay)
            {
                activePlay.AfterSimulation();
                return;
            }
            if (ReplayPlayback.IsModern)
            {
                ReplayPlayback.AfterSimulation(scene);
                return;
            }
            if (!NetSession.Active) { return; }
            for (int slot = 0; slot < scene.Players.Count; slot++)
            {
                if (!NetSession.RemoteStateValid[slot]) { continue; }
                PlayerEntity player = scene.Players[slot];
                if (player.LoadFlags.TestFlag(LoadFlags.Active))
                {
                    NetPlayerBridge.ApplyState(scene, player, NetSession.RemoteStates[slot]);
                }
            }
            NetSession.NoteStatesApplied();
            NetSession.Metrics.EndWork(Stopwatch.GetTimestamp());
        }
    }
}
