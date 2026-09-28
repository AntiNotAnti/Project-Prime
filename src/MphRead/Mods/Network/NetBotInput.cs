using System;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    /// <summary>Per-bot input capture. Only the dedicated simulation authors these facts.</summary>
    internal static class NetBotInput
    {
        private static readonly PlayerReplicationBridge?[] Bridges = new PlayerReplicationBridge[PlayerEntity.SlotCapacity];
        private static readonly IntentPacket[] Pending = new IntentPacket[PlayerEntity.SlotCapacity];
        private static readonly bool[] Ready = new bool[PlayerEntity.SlotCapacity];
        internal static Action<int, IntentPacket>? Sink;

        internal static void Reset()
        {
            Array.Clear(Bridges); Array.Clear(Ready); Sink = null;
        }
        internal static void ForgetSlot(int slot)
        {
            Bridges[slot] = null; Ready[slot] = false;
        }
        internal static void Capture(PlayerEntity player)
        {
            if (!NetSession.IsAuthority || player.SceneServices.IsReplica || !player.IsBot) return;
            int slot = player.SlotIndex;
            var bridge = Bridges[slot] ??= new PlayerReplicationBridge(LivePlayerReplicationHost.Instance);
            bridge.RecordPresses(player);
            player.ModContinuousNetworkTarget = NetTargetIdentity.None;
            player.ModContinuousFireTick = 0;
            if (NetSession.NetFrame % NetConfig.IntentSendInterval != 0) return;
            var intent = bridge.CaptureIntent(player);
            intent.Frame = NetSession.NetFrame;
            intent.MatchId = NetSession.CurrentMatchId;
            intent.AuthorityEpoch = NetSession.AuthorityEpoch;
            intent.SlotGeneration = NetPlayerLifecycle.Generation(slot);
            intent.LifeId = NetPlayerLifecycle.Get(slot);
            intent.AckFrame = 0; intent.AckSubFrame = 0; // Authority decisions never rewind.
            Pending[slot] = intent; Ready[slot] = true;
        }
        internal static void CaptureContinuousShot(PlayerEntity player)
        {
            int slot = player.SlotIndex;
            if (!NetSession.IsAuthority || !player.IsBot || !Ready[slot]
                || Pending[slot].Frame != NetSession.NetFrame) return;
            Pending[slot].Aim = player.ModGunVector;
            Pending[slot].Position = player.Position;
            Pending[slot].ContinuousFireTick = player.ModContinuousFireTick;
        }
        internal static void Flush()
        {
            if (!NetSession.IsAuthority) return;
            for (int slot = 0; slot < Ready.Length; slot++)
            {
                if (!Ready[slot]) continue;
                Ready[slot] = false;
                var intent = Pending[slot];
                if (intent.Frame != NetSession.NetFrame || !NetSession.SlotIsBot[slot] || slot >= PlayerEntity.Players.Count
                    || !NetPlayerLifecycle.Matches(slot, intent.SlotGeneration, intent.LifeId)) continue;
                var player = PlayerEntity.Players[slot];
                intent.Target = player.CurrentWeapon == BeamType.ShockCoil ? player.ModContinuousNetworkTarget : intent.Target;
                intent.ContinuousFireTick = player.ModContinuousFireTick;
                // Keep the pre-step firing pose and release charge; snapshots carry the resulting movement.
                ReplayCapture.AcceptedIntent(slot, intent);
                Sink?.Invoke(slot, intent);
            }
        }
    }
}
