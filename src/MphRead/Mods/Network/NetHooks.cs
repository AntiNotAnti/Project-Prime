using System.Diagnostics;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>Game-thread entry points for authoritative play and passive demo presentation.</summary>
    public static class NetHooks
    {
        public static int LocalSlot => AuthoritativePlay.Current?.LocalSlot
            ?? (DemoPlayback.IsActive ? -1 : 0);

        public static bool KeepSlotAlive(PlayerEntity player) => NetSession.Active || AuthoritativePlay.Active;

        public static bool TryApplyRemoteInput(PlayerEntity player, int slot)
        {
            if (AuthoritativePlay.Active && slot != LocalSlot)
            {
                player.Controls.ClearAll();
                return true;
            }
            if (!NetSession.Active) { return false; }
            // Legacy demos may contain recorded controls for animation.
            // They have no socket and cannot submit gameplay state.
            if (player.LoadFlags.TestFlag(LoadFlags.Active) && NetSession.RemoteIntentValid[slot])
            {
                NetPlayerBridge.ApplyIntent(player, NetSession.RemoteIntents[slot]);
            }
            else { player.Controls.ClearAll(); }
            return true;
        }

        public static bool ForceSpawn(PlayerEntity player) => MapAudit.ForceEveryone;

        public static void AfterInput(Scene scene)
        {
            if (AuthoritativePlay.Current is { } play)
            {
                play.BeforeSimulation(scene);
                return;
            }
            if (DemoPlayback.IsModern)
            {
                DemoPlayback.BeforeSimulation(scene);
                return;
            }
            if (!NetSession.Active) { return; }
            NetRoomChange.Sync(scene);
            NetDiagnostics.Report(scene, NetSession.NetFrame / 60.0);
            NetPlayerSetup.ApplyOnce();
            NetMatchEnd.Sync(scene);
            NetMatchSync.Apply(scene);
            NetSlotManager.Sync(scene);
            NetLog.Snapshot(NetSession.NetFrame / 60.0, scene);
        }

        public static void AfterSimulation(Scene scene)
        {
            if (AuthoritativePlay.Current is { } play)
            {
                play.AfterSimulation();
                return;
            }
            if (DemoPlayback.IsModern)
            {
                DemoPlayback.AfterSimulation();
                return;
            }
            if (!NetSession.Active) { return; }
            for (int slot = 0; slot < PlayerEntity.Players.Count; slot++)
            {
                if (!NetSession.RemoteStateValid[slot]) { continue; }
                PlayerEntity player = PlayerEntity.Players[slot];
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
