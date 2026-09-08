using System;
using System.Diagnostics;
using MphRead.Entities;

namespace MphRead.Mods.Network
{
    public enum NetRole { Offline, Client }

    /// <summary>
    /// Passive recorded-match state. Live connections belong to AuthoritativePlay;
    /// this adapter never opens a socket, sends input, or acquires simulation authority.
    /// </summary>
    public static class NetSession
    {
        public static bool Active { get; private set; }
        public static NetRole Role => Active ? NetRole.Client : NetRole.Offline;
        public static int LocalSlot => Active ? -1 : 0;
        public static uint NetFrame { get; private set; }
        public static uint LastSnapshotFrame => _lastSnapshotFrame;
        public static string PlayerName { get; set; } = "Player";
        public static MatchStatePacket? ServerMatch { get; private set; }
        public static readonly PlayerState[] RemoteStates = new PlayerState[PlayerEntity.SlotCapacity];
        public static readonly bool[] RemoteStateValid = new bool[PlayerEntity.SlotCapacity];
        public static readonly IntentPacket[] RemoteIntents = new IntentPacket[PlayerEntity.SlotCapacity];
        public static readonly bool[] RemoteIntentValid = new bool[PlayerEntity.SlotCapacity];
        public static readonly Hunter[] SlotHunter = new Hunter[PlayerEntity.SlotCapacity];
        public static readonly int[] SlotPing = new int[PlayerEntity.SlotCapacity];
        public static readonly bool[] SlotOccupied = new bool[PlayerEntity.SlotCapacity];
        private static readonly uint[] _lastSlotIntentFrame = new uint[PlayerEntity.SlotCapacity];
        private static uint _lastSnapshotFrame;
        private static (uint Rng1, uint Rng2)? _pendingRandom;
        internal static void ApplyRandom(Scene scene)
        {
            if (_pendingRandom is not { } state) return;
            scene.Random.SetRng1(state.Rng1);
            scene.Random.SetRng2(state.Rng2);
            _pendingRandom = null;
        }
        private static int _lateSnapshotRun;
        private const uint SnapshotResetGap = 600;
        private const uint IntentResetGap = 600;
        private const int LateSnapshotsBeforeReset = 12;
        public static long SnapshotsReceived { get; private set; }
        public static long StatesApplied { get; private set; }
        public static long IntentsReceived { get; private set; }
        public static long SnapshotsOutOfOrder { get; private set; }
        public static long IntentsOutOfOrder { get; private set; }
        public static int SnapshotStreamResets { get; private set; }
        public static NetMetrics Metrics { get; private set; } = new();
        public static NetTrafficMetrics? TrafficMetrics => null;

        public static void NoteStatesApplied() => StatesApplied++;
        public static void StartPlayback()
        {
            Stop();
            Active = true;
        }

        public static void RewindPlayback()
        {
            _pendingRandom = null;
            NetFrame = 0;
            Metrics = new NetMetrics();
            _lastSnapshotFrame = 0;
            _lateSnapshotRun = 0;
            Array.Clear(_lastSlotIntentFrame);
            Array.Clear(RemoteStateValid);
            Array.Clear(RemoteIntentValid);
            SnapshotsReceived = StatesApplied = IntentsReceived = 0;
            SnapshotsOutOfOrder = IntentsOutOfOrder = 0;
            SnapshotStreamResets = 0;
            NetPlayerBridge.Reset();
            NetDamage.Reset();
        }

        /// <summary>Decode a file record synchronously, without a socket or receive queue.</summary>
        public static void InjectPlaybackPacket(byte[] data, int length)
        {
            if (!Active) { return; }
            if (length < 1 || length > data.Length || length > NetConfig.MaxPacketSize)
            {
                Metrics.Reject();
                return;
            }
            ReadOnlySpan<byte> payload = data.AsSpan(1, length - 1);
            switch ((PacketType)data[0])
            {
                case PacketType.SlotIntent: HandleSlotIntent(payload); break;
                case PacketType.Snapshot: HandleSnapshot(payload); break;
                case PacketType.Roster: HandleRoster(payload); break;
                case PacketType.MatchState: HandleMatchState(payload, false); break;
                case PacketType.MapChange: HandleMatchState(payload, true); break;
                case PacketType.Chat:
                    if (payload.Length == ChatPacket.Size) { Chat.ChatBox.Receive(ChatPacket.Read(payload)); }
                    else { Metrics.Reject(); }
                    break;
                // Connection, admission and historical authority records are inert.
                // A recording never assigns a local player or changes networking roles.
            }
        }

        public static void Update(double time)
        {
            if (!Active) { return; }
            NetFrame++;
            Metrics.BeginWork(Stopwatch.GetTimestamp());
        }

        public static void Stop()
        {
            _pendingRandom = null;
            AuthoritativePlay.Current?.Dispose();
            DemoPlayback.CloseFile();
            DemoRecorder.Stop();
            NetPlayerSetup.Reset();
            SpectatorMode.Reset();
            NetMatchSync.Reset();
            NetSlotManager.Reset();
            NetRoomChange.Reset();
            Chat.ChatBox.Clear();
            RewindPlayback();
            Array.Clear(SlotOccupied);
            Array.Clear(SlotHunter);
            Array.Clear(SlotPing);
            _playbackRoster = null;
            ServerMatch = null;
            Active = false;
        }

        public static void ForgetSlot(int slot)
        {
            if ((uint)slot >= PlayerEntity.SlotCapacity) { return; }
            _lastSlotIntentFrame[slot] = 0;
            RemoteIntentValid[slot] = false;
            RemoteIntents[slot] = default;
            RemoteStateValid[slot] = false;
            RemoteStates[slot] = default;
        }

        internal static void SetPlaybackMatch(in MatchTransitionPacket match)
        {
            ServerMatch = new MatchStatePacket { Mode = (byte)match.Mode,
                MatchId = unchecked((ushort)match.MatchId), RoomKey = match.Room,
                NextRoomKey = string.Empty, Flags = MatchStatePacket.FlagInProgress };
        }

        private static void HandleSlotIntent(ReadOnlySpan<byte> payload)
        {
            if (payload.Length < 1 + IntentPacket.Size)
            {
                return;
            }
            int slot = payload[0];
            if (slot < 0 || slot >= RemoteIntents.Length || slot == LocalSlot)
            {
                return;
            }
            if (!NetPacketReader.TryReadIntent(payload[1..], out IntentPacket intent))
            {
                Metrics.Reject();
                return;
            }
            if (_lastSlotIntentFrame[slot] != 0 && intent.Frame <= _lastSlotIntentFrame[slot]
                && _lastSlotIntentFrame[slot] - intent.Frame < IntentResetGap)
            {
                Metrics.LateInput(intent.Frame == _lastSlotIntentFrame[slot]);
                IntentsOutOfOrder++;
                return;
            }
            _lastSlotIntentFrame[slot] = intent.Frame;
            RemoteIntents[slot] = intent;
            RemoteIntentValid[slot] = true;
            IntentsReceived++;
        }

        private static RosterPacket? _playbackRoster;

        internal static void ApplyRoster(Scene scene)
        {
            if (_playbackRoster is not RosterPacket roster) return;
            for (int i = 0; i < roster.Count; i++)
                if ((uint)roster.Slots[i] < (uint)scene.Roster.Nicknames.Length)
                    scene.Roster.Nicknames[roster.Slots[i]] = roster.Names[i];
        }

        private static void HandleRoster(ReadOnlySpan<byte> payload)
        {
            if (!NetPacketReader.TryReadRoster(payload, out RosterPacket roster))
            {
                Metrics.Reject();
                return;
            }
            _playbackRoster = roster;
            Array.Clear(SlotOccupied);
            for (int i = 0; i < roster.Count; i++)
            {
                int slot = roster.Slots[i];
                if (slot < 0 || slot >= SlotOccupied.Length)
                {
                    continue;
                }
                SlotOccupied[slot] = true;
                if (Enum.IsDefined(typeof(Hunter), roster.Hunters[i]))
                {
                    SlotHunter[slot] = (Hunter)roster.Hunters[i];
                }
                SlotPing[slot] = roster.Pings[i];
            }
        }

        private static void HandleMatchState(ReadOnlySpan<byte> payload, bool rotated)
        {
            if (!NetPacketReader.TryReadMatchState(payload, out MatchStatePacket state))
            {
                Metrics.Reject();
                return;
            }
            string? previous = ServerMatch?.RoomKey;
            ServerMatch = state;
            if (rotated || previous == null || previous != state.RoomKey)
            {
                Console.WriteLine($"[net] server map: {state.RoomKey} "
                    + $"({(GameMode)state.Mode}, {state.TimeRemaining:0} s left)");
            }
        }

        private static void HandleSnapshot(ReadOnlySpan<byte> payload)
        {
            if (!NetPacketReader.TryReadSnapshot(payload, out SnapshotHeader header))
            {
                Metrics.Reject();
                return;
            }
            if (_lastSnapshotFrame != 0 && header.Frame <= _lastSnapshotFrame
                && _lastSnapshotFrame - header.Frame < SnapshotResetGap)
            {
                Metrics.LateSnapshot(header.Frame == _lastSnapshotFrame);
                SnapshotsOutOfOrder++;
                if (++_lateSnapshotRun < LateSnapshotsBeforeReset)
                {
                    return;
                }
                NetLog.Event($"snapshot stream re-based: {_lateSnapshotRun} in a row "
                    + $"older than {_lastSnapshotFrame} (now {header.Frame})");
                SnapshotStreamResets++;
            }
            _lateSnapshotRun = 0;
            _lastSnapshotFrame = header.Frame;
            SnapshotsReceived++;
            Metrics.Snapshot(Stopwatch.GetTimestamp());
            _pendingRandom = (header.Rng1, header.Rng2);
            int offset = SnapshotHeader.Size;
            Array.Clear(RemoteStateValid);
            for (int i = 0; i < header.PlayerCount; i++)
            {
                if (offset + PlayerState.Size > payload.Length)
                {
                    break;
                }
                PlayerState state = PlayerState.Read(payload[offset..]);
                offset += PlayerState.Size;
                if (state.SlotIndex < RemoteStates.Length)
                {
                    RemoteStates[state.SlotIndex] = state;
                    RemoteStateValid[state.SlotIndex] = true;
                }
            }
        }

    }
}
