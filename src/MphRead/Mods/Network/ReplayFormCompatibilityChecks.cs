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
        for (int protocol = 49; protocol <= 80; protocol++)
        {
            byte[] old = new byte[1 + SnapshotHeader.Size + players.Length * PlayerState.Protocol80Size + tail.Length];
            canonical.AsSpan(0, 1 + SnapshotHeader.Size).CopyTo(old);
            for (int i = 0; i < players.Length; i++)
                canonical.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Protocol80Size)
                    .CopyTo(old.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Protocol80Size));
            tail.CopyTo(old, 1 + SnapshotHeader.Size + players.Length * PlayerState.Protocol80Size);
            require(ReplayIdentityCompatibility.Convert(old, protocol).SequenceEqual(canonical),
                $"protocol{protocol} actual173-byte form rows expand only absent grapple state");
        }
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
        byte[] current = ReplayCheckpointCheckFixture.Protocol80(decoder.CaptureCheckpoint().Bytes, require);
        int matchAt = ReplayCheckpointCheckFixture.MatchOffset(current);
        int at = matchAt + MatchStatePacket.Size + 1 + RosterPacket.Size;
        // The fixture targets protocol 48, whose MatchState has the pre-60
        // ushort rules width. Do not relabel a current uint-rules body as old.
        require(current.AsSpan(matchAt + MatchStatePacket.Protocol59Size,
                MatchStatePacket.Size - MatchStatePacket.Protocol59Size).IndexOfAnyExcept((byte)0) < 0,
            "historical match fixture discards only zero high rule bits");
        using var stream = new MemoryStream();
        stream.Write(current.AsSpan(0, 7)); // Historical header has no feature byte.
        stream.Write(current.AsSpan(8, matchAt - 8));
        stream.Write(current.AsSpan(matchAt, MatchStatePacket.Protocol59Size));
        stream.Write(current.AsSpan(matchAt + MatchStatePacket.Size,
            at - matchAt - MatchStatePacket.Size));
        for (int i = 0; i < RosterPacket.MaxSlots; i++)
        {
            stream.Write(current.AsSpan(at, 7 + PlayerState.Protocol48Size)); at += 7 + PlayerState.Protocol80Size;
            // A current flagged detached checkpoint has the 9-byte native
            // touch appendix on each intent. A protocol-48 checkpoint cannot.
            stream.Write(current.AsSpan(at, 1 + IntentPacket.FullSize));
            at += 1 + IntentPacket.Protocol80TouchSize;
            stream.Write(current.AsSpan(at, 4)); at += 4;
        }
        // The historical player-width fixture uses schema 9, which predates
        // the schema 10 pickup room and three data counts. Strip only empty data.
        const int grantTailBytes = 87;
        const int pickupTailBytes = sizeof(uint) + 3 * sizeof(int) + grantTailBytes;
        require(current.AsSpan(current.Length - pickupTailBytes).ToArray()
                .Select((value, index) => value == (index == pickupTailBytes - grantTailBytes ? 83 : 0)).All(value => value),
            "historical player-width fixture strips only empty pickup data");
        stream.Write(current.AsSpan(at, current.Length - at - pickupTailBytes));
        byte[] historical = stream.ToArray(); historical[4] = 9; historical[5] = 0; historical[6] = 48;
        require(historical.Length == current.Length - 1
            - (MatchStatePacket.Size - MatchStatePacket.Protocol59Size)
            - RosterPacket.MaxSlots * (PlayerState.Protocol80Size - PlayerState.Protocol48Size
                + IntentPacket.Protocol80TouchSize - IntentPacket.FullSize)
            - pickupTailBytes,
            "protocol48 fixture removes exactly feature, match, player, intent and empty pickup extensions");
        var restored = new ReplayReplicaState(); restored.RestoreCheckpoint(new(historical));
        require(restored.Match is { } historicalMatch && historicalMatch.MatchId == match.MatchId
            && historicalMatch.AuthorityEpoch == match.AuthorityEpoch && historicalMatch.RuleBits == match.RuleBits,
            "protocol48 detached checkpoint preserves historical match identity and rules");
        require(restored.TryGetPlayer(0, out var restoredPlayer) && restoredPlayer.Health == 77
            && restored.TryGetPlayer(1, out var second) && second.Health == 55,
            "protocol48 detached checkpoint reads old player width without consuming following lifecycle/intent fields");
        byte[] restoredBytes = new byte[PlayerState.Size]; restoredPlayer.Write(restoredBytes);
        require(restoredPlayer.Form.Phase == FormPhase.Unknown
            && restoredBytes.AsSpan(PlayerState.Protocol48Size).IndexOfAnyExcept((byte)0) < 0,
            "protocol48 detached checkpoint upgrades to unknown default form");
    }
}

// Check-only fixture projection. Never use this to downgrade production replays:
// these three historical checks have no configuration or grapple.
// Preserve actual historical173/1296 row widths before their older-schema cuts.
internal static class ReplayCheckpointCheckFixture
{
    internal static int MatchOffset(ReadOnlySpan<byte> bytes)
    {
        // Header8, clocks/counters36, snapshot flag1, roster-revision flag1,
        // optional revision4, then present-match flag1. Roster fixtures may
        // carry a revision even when its numeric value is zero.
        if (bytes.Length < 47 || bytes[45] > 1)
            throw new InvalidOperationException("Historical check has an invalid fixed checkpoint header.");
        int matchFlag = 46 + (bytes[45] == 1 ? 4 : 0);
        if (bytes.Length <= matchFlag || bytes[matchFlag] != 1)
            throw new InvalidOperationException("Historical check requires a present match.");
        return matchFlag + 1;
    }

    internal static byte[] Protocol80(ReadOnlySpan<byte> current, Action<bool, string> require)
    {
        require(PlayerState.Protocol80Size == 173 && IntentPacket.Protocol80TouchSize == 1296,
            "historical fixture uses frozen protocol80 player/intent widths");
        int matchAt = MatchOffset(current);
        int firstPlayer = matchAt + MatchStatePacket.Size + 1 + RosterPacket.Size;
        var layout = ReplayReplicaState.InspectCheckpointLayout(new(current));
        if (layout.Version != 14 || layout.Protocol != 82 || layout.HeaderBytes != 8
            || !layout.Has(ReplayReplicaCheckpointFeatures.Grapple)
            || current[matchAt + MatchStatePacket.Size] != 0)
            throw new InvalidOperationException("Historical check requires a v14/protocol82 match-only fixture.");
        using var stream = new MemoryStream();
        stream.Write(current[..firstPlayer]);
        int at = firstPlayer;
        for (int slot = 0; slot < RosterPacket.MaxSlots; slot++)
        {
            if (current.Slice(at + 7 + PlayerState.Protocol80Size,
                    PlayerState.Size - PlayerState.Protocol80Size).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidOperationException("Historical fixture cannot discard populated accepted grapple state.");
            stream.Write(current.Slice(at, 7 + PlayerState.Protocol80Size));
            at += 7 + PlayerState.Size;
            stream.Write(current.Slice(at, 1)); at++;
            if (current.Slice(at + IntentPacket.Protocol80TouchSize,
                    IntentPacket.TouchCanonicalSize - IntentPacket.Protocol80TouchSize).IndexOfAnyExcept((byte)0) >= 0)
                throw new InvalidOperationException("Historical fixture cannot discard a populated grapple request nonce.");
            if ((BinaryPrimitives.ReadUInt32LittleEndian(current[(at + 4)..]) & (uint)IntentButtons.Grapple) != 0)
                throw new InvalidOperationException("Historical fixture cannot relabel a held grapple input.");
            stream.Write(current.Slice(at, IntentPacket.Protocol80TouchSize));
            at += IntentPacket.TouchCanonicalSize;
            stream.Write(current.Slice(at, 4)); at += 4;
        }
        stream.Write(current[at..]);
        byte[] historical = stream.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(historical.AsSpan(4),
            layout.Has(ReplayReplicaCheckpointFeatures.TimedShots) ? (ushort)13 : (ushort)12);
        historical[6] = 80;
        historical[7] &= unchecked((byte)~(byte)ReplayReplicaCheckpointFeatures.Grapple);
        require(historical.Length == current.Length - RosterPacket.MaxSlots
            * (PlayerState.Size - PlayerState.Protocol80Size + IntentPacket.TouchCanonicalSize - IntentPacket.Protocol80TouchSize),
            "protocol80 fixture removes exactly19 accepted-state bytes and2 nonce bytes per slot plus v14 capability");
        var historicalLayout = ReplayReplicaState.InspectCheckpointLayout(new(historical));
        require(historicalLayout.Protocol == 80 && historicalLayout.PlayerBytes == 173
            && historicalLayout.IntentBytes == 1296 && !historicalLayout.Has(ReplayReplicaCheckpointFeatures.Grapple),
            "projected protocol80 fixture decodes under the actual historical layout");
        return historical;
    }
}
