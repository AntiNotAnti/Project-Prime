using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;

namespace MphRead.Mods.Network;

internal static class ReplayFormCompatibilityChecks
{
    internal static void Run(Action<bool, string> require)
    {
        var header = new SnapshotHeader { PlayerCount = 2, MatchId = 1, AuthorityEpoch = 2, Frame = 10 };
        var players = new[] {
            new PlayerState { SlotIndex = 0, SlotGeneration = 1, LifeId = 1, Health = 77, Flags = PlayerState.FlagActive | PlayerState.FlagSpawned },
            new PlayerState { SlotIndex = 1, SlotGeneration = 2, LifeId = 3, Health = 55, Flags = PlayerState.FlagActive | PlayerState.FlagSpawned }
        };
        byte[] tail = new byte[NetMatchTimeSync.Size + NetHealthSync.HeaderSize];
        BinaryPrimitives.WriteUInt16LittleEndian(tail.AsSpan(NetMatchTimeSync.Size), 1);
        byte[] canonical = new byte[1 + SnapshotHeader.Size + 2 * PlayerState.Size + tail.Length];
        canonical[0] = (byte)PacketType.Snapshot; header.Write(canonical.AsSpan(1));
        for (int i = 0; i < players.Length; i++) players[i].Write(canonical.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size));
        tail.CopyTo(canonical, 1 + SnapshotHeader.Size + 2 * PlayerState.Size);
        for (int protocol = 24; protocol <= 48; protocol++)
        {
            int oldSize = protocol < 29 ? PlayerState.LegacySize : PlayerState.Protocol48Size;
            byte[] old = new byte[1 + SnapshotHeader.Size + 2 * oldSize + tail.Length];
            canonical.AsSpan(0, 1 + SnapshotHeader.Size).CopyTo(old);
            for (int i = 0; i < players.Length; i++)
                canonical.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size, oldSize)
                    .CopyTo(old.AsSpan(1 + SnapshotHeader.Size + i * oldSize));
            tail.CopyTo(old, 1 + SnapshotHeader.Size + 2 * oldSize);
            var upgraded = ReplayIdentityCompatibility.Convert(old, protocol);
            require(upgraded.Length == canonical.Length && upgraded[^tail.Length..].SequenceEqual(tail),
                $"protocol {protocol} canonical form expansion preserves exact world tail and row count");
            for (int i = 0; i < players.Length; i++)
            {
                var bytes = upgraded.Slice(1 + SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Size);
                var player = PlayerState.Read(bytes);
                require(player.Health == players[i].Health && player.LifeId == players[i].LifeId
                    && player.Form.Phase == FormPhase.Unknown
                    && bytes[PlayerState.Protocol48Size..].IndexOfAnyExcept((byte)0) < 0,
                    $"protocol {protocol} player {i} remains aligned with unknown default form");
            }
            bool truncated = false;
            try { ReplayIdentityCompatibility.Convert(old.AsSpan(0, 1 + SnapshotHeader.Size + 2 * oldSize - 1), protocol); }
            catch (InvalidDataException) { truncated = true; }
            require(truncated, $"protocol {protocol} truncated canonical player rows fail closed");
        }
        require(ReplayIdentityCompatibility.Convert(canonical, 49).SequenceEqual(canonical),
            "protocol49 canonical snapshot retains authored form width without another expansion");
        byte[] fast = new byte[1 + SnapshotFast.MaximumEncodedSize]; fast[0] = (byte)PacketType.SnapshotFast;
        int fastLength = SnapshotFast.Write(canonical.AsSpan(1), fast.AsSpan(1)) + 1;
        require(ReplayIdentityCompatibility.Convert(fast.AsSpan(0, fastLength), 48)
            .SequenceEqual(fast.AsSpan(0, fastLength)), "protocol48 live fast lane never receives canonical form padding");

        var decoder = new ReplayReplicaState();
        var match = new MatchStatePacket { MatchId = 1, AuthorityEpoch = 2, RoomKey = "MP1 SANCTORUS", Mode = (byte)GameMode.Battle };
        byte[] matchPacket = new byte[1 + MatchStatePacket.Size]; matchPacket[0] = (byte)PacketType.MatchState; match.Write(matchPacket.AsSpan(1));
        decoder.Accept(matchPacket, 0);
        var roster = RosterPacket.Create(); roster.MatchId = 1; roster.AuthorityEpoch = 2; roster.Count = 2;
        roster.Slots[0] = 0; roster.Generations[0] = 1; roster.Slots[1] = 1; roster.Generations[1] = 2;
        byte[] rosterPacket = new byte[1 + RosterPacket.Size]; rosterPacket[0] = (byte)PacketType.Roster;
        roster.Write(rosterPacket.AsSpan(1)); decoder.Accept(rosterPacket, 0); decoder.Accept(canonical, 10);
        byte[] current = decoder.CaptureCheckpoint().Bytes.ToArray();
        int at = 46 + MatchStatePacket.Size + 1 + RosterPacket.Size;
        using var stream = new MemoryStream(); stream.Write(current.AsSpan(0, at));
        for (int i = 0; i < RosterPacket.MaxSlots; i++)
        {
            stream.Write(current.AsSpan(at, 7 + PlayerState.Protocol48Size)); at += 7 + PlayerState.Size;
            // A current (protocol 53) detached checkpoint has the 9-byte native
            // touch appendix on each intent. A protocol-48 checkpoint cannot.
            stream.Write(current.AsSpan(at, 1 + IntentPacket.FullSize + 4));
            at += 1 + IntentPacket.TouchCanonicalSize + 4;
        }
        stream.Write(current.AsSpan(at)); byte[] historical = stream.ToArray(); historical[6] = 48;
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(new(historical));
        require(restored.TryGetPlayer(0, out var restoredPlayer) && restoredPlayer.Health == 77
            && restored.TryGetPlayer(1, out var second) && second.Health == 55,
            "protocol48 detached checkpoint reads old player width without consuming following lifecycle/intent fields");
        byte[] restoredBytes = new byte[PlayerState.Size]; restoredPlayer.Write(restoredBytes);
        require(restoredPlayer.Form.Phase == FormPhase.Unknown
            && restoredBytes.AsSpan(PlayerState.Protocol48Size).IndexOfAnyExcept((byte)0) < 0,
            "protocol48 detached checkpoint upgrades to unknown default form");
    }
}
