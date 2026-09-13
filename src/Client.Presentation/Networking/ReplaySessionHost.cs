using System;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network
{
    internal interface IReplaySessionHost
    {
        bool IsPassive { get; }
        bool AllowsPresentationSideEffects { get; }
        void Start();
        void Stop();
        void Rewind();
        void SetMatch(in MatchTransitionPacket match);
        void SetRoster(ReadOnlySpan<NetRosterEntry> roster);
        void SetSnapshotRoster(ReadOnlySpan<SnapshotPlayer> players);
        void PresentChat(in SessionChatPacket chat);
        bool RestoreChat(ReadOnlySpan<byte> state);
        void ClearChat();
        void RejectRecord();
        void InjectLegacy(byte[] data);
        void AdvanceLegacy(uint frame);
    }

    internal sealed class TheatreReplaySessionHost : IReplaySessionHost
    {
        public bool IsPassive => false;
        public bool AllowsPresentationSideEffects => true;
        public void Start() => NetSession.StartPlayback();
        public void Stop() => NetSession.Stop();
        public void Rewind() => NetSession.RewindPlayback();
        public void SetMatch(in MatchTransitionPacket match) => NetSession.SetPlaybackMatch(match);
        public void SetRoster(ReadOnlySpan<NetRosterEntry> roster)
        {
            Array.Clear(NetSession.SlotOccupied);
            foreach (NetRosterEntry entry in roster)
            {
                NetSession.SlotOccupied[entry.Slot] = true;
                NetSession.SlotHunter[entry.Slot] = entry.Hunter;
                NetSession.SlotPing[entry.Slot] = entry.PingMs;
            }
        }
        public void SetSnapshotRoster(ReadOnlySpan<SnapshotPlayer> players)
        {
            Array.Clear(NetSession.SlotOccupied);
            foreach (SnapshotPlayer player in players)
            {
                NetSession.SlotOccupied[player.Slot] = true;
                NetSession.SlotHunter[player.Slot] = player.Hunter;
            }
        }
        public void PresentChat(in SessionChatPacket chat) => Chat.ChatBox.Receive(
            new ChatPacket { Slot = chat.Slot, Kind = ChatPacket.KindSay, Name = chat.Name, Text = chat.Text });
        public bool RestoreChat(ReadOnlySpan<byte> state) => Chat.ChatBox.RestoreReplay(state.ToArray());
        public void ClearChat() => Chat.ChatBox.Clear();
        public void RejectRecord() => NetSession.Metrics.Reject();
        public void InjectLegacy(byte[] data) => NetSession.InjectPlaybackPacket(data, data.Length);
        public void AdvanceLegacy(uint frame) => NetSession.Update(frame / 60.0);
    }

    internal sealed class PassiveReplaySessionHost : IReplaySessionHost
    {
        public bool IsPassive => true;
        // Scene-local feedback is safe and required by killcam/replay
        // observation. All process-global host effects below remain no-ops.
        public bool AllowsPresentationSideEffects => true;
        public void Start() { }
        public void Stop() { }
        public void Rewind() { }
        public void SetMatch(in MatchTransitionPacket match) { }
        public void SetRoster(ReadOnlySpan<NetRosterEntry> roster) { }
        public void SetSnapshotRoster(ReadOnlySpan<SnapshotPlayer> players) { }
        public void PresentChat(in SessionChatPacket chat) { }
        public bool RestoreChat(ReadOnlySpan<byte> state) => true;
        public void ClearChat() { }
        public void RejectRecord() { }
        public void InjectLegacy(byte[] data) => throw new InvalidOperationException("Legacy replay playback requires the Theatre session.");
        public void AdvanceLegacy(uint frame) { }
    }

    /// <summary>Replica-only policy for a scene driven by one replay session.</summary>
    public sealed class ReplaySceneServices : ISceneServices
    {
        private readonly ReplayPlaybackSession _session;
        internal ReplaySceneServices(ReplayPlaybackSession session) => _session = session;
        public bool IsReplica => true;
        public bool RebuildingRoom => true;
        public int LocalSlot => _session.PerspectiveSlot;
        public uint WorldServerTick => _session.WorldServerTick ?? _session.SnapshotServerTick ?? _session.CurrentFrame;
        public bool MayEndOnScore => false;
        public bool ShouldLeaveAfterMatch => false;
        public bool KeepSlotAlive(PlayerEntity player) => true;
        public bool IsRemoteControlled(int slot) => true;
        public bool TryGetRemoteAim(int slot, out Vector3 aim) { aim = default; return false; }
        public bool DesiredSpectating => LocalSlot < 0;
        public bool SuppressDamage(PlayerEntity victim) => true;
        public PlayerEntity RebuildPlayers(Scene scene, Hunter hunter, int recolor)
            => _session.RebuildPlayers(scene);
        public void AfterRoomRebuild(Scene scene) => _session.AfterRoomRebuild(scene);
        public void AfterInput(Scene scene) => _session.BeforeSimulation(scene);
        public void AfterSimulation(Scene scene) => _session.AfterSimulation(scene);
    }
}
