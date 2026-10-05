using System;
using System.IO;
using System.Buffers.Binary;

namespace MphRead.Mods.Network;

/// <summary>
/// Upgrades recorded packets at playback boundaries, never on a live connection.
/// Historical replays are deliberately best-effort: missing fields get historical
/// defaults and optional packet families may be ignored, while live peers still
/// have to match <see cref="NetConfig.ProtocolVersion"/> exactly.
/// </summary>
internal static class ReplayIdentityCompatibility
{
    internal const int OldestReplayProtocol = 4;
    internal static bool Supports(int protocol)
        => protocol >= OldestReplayProtocol && protocol <= NetConfig.ProtocolVersion;
    internal static bool BestEffort(int protocol)
        => protocol >= OldestReplayProtocol && protocol < NetConfig.ProtocolVersion;

    internal static ReadOnlySpan<byte> Convert(ReadOnlySpan<byte> packet, int protocol)
    {
        if (packet.IsEmpty) return packet;
        if (!Supports(protocol)) throw new InvalidDataException("Unsupported replay protocol.");

        var converted = ConvertIdentity(packet, protocol);
        if (converted.IsEmpty) return converted;

        PacketType type = (PacketType)converted[0];
        if (protocol < 35 && type is PacketType.MatchSemanticEvent or PacketType.MatchAward) return ReadOnlySpan<byte>.Empty;
        if (type == PacketType.MapChange)
        {
            // Historical live clients fed MapChange through the same MatchState
            // decoder with a "rotated" flag. The detached replay replica has no
            // connection lifecycle, so canonicalize it to the state packet it is.
            byte[] state = converted.ToArray();
            state[0] = (byte)PacketType.MatchState;
            converted = state;
            type = PacketType.MatchState;
        }
        if (protocol < 24 && type is PacketType.Intent or PacketType.SlotIntent)
        {
            converted = ConvertLegacyIntent(converted, protocol);
            type = (PacketType)converted[0];
        }

        if (protocol < 33 && type == PacketType.Roster
            && converted.Length == 1 + RosterPacket.LegacySize)
        {
            byte[] expanded = New(PacketType.Roster, RosterPacket.Size);
            converted.Slice(1, RosterPacket.HeaderSize).CopyTo(expanded.AsSpan(1));
            for (int i = 0; i < RosterPacket.MaxSlots; i++)
            {
                converted.Slice(1 + RosterPacket.HeaderSize + i * RosterPacket.LegacyEntrySize,
                        RosterPacket.LegacyEntrySize)
                    .CopyTo(expanded.AsSpan(1 + RosterPacket.HeaderSize + i * RosterPacket.EntrySize));
            }
            converted = expanded; // new handicap byte remains zero for legacy recordings
        }

        if (protocol < 30 && type is PacketType.Intent or PacketType.SlotIntent)
        {
            int prefix = type == PacketType.SlotIntent ? 2 : 1;
            if (converted.Length == prefix + IntentPacket.LegacyFullSize)
            {
                byte[] expanded = new byte[prefix + IntentPacket.FullSize];
                converted.CopyTo(expanded);
                converted = expanded;
            }
            else
            {
                Require(converted.Length == prefix + IntentPacket.FullSize);
            }
        }
        else if (protocol < 39 && type is PacketType.Intent or PacketType.SlotIntent)
        {
            int prefix = type == PacketType.SlotIntent ? 2 : 1;
            Require(converted.Length == prefix + IntentPacket.Protocol38FullSize);
            byte[] expanded = new byte[prefix + IntentPacket.FullSize];
            converted[..(prefix + IntentPacket.LegacyFullSize)].CopyTo(expanded);
            byte count = converted[prefix + IntentPacket.LegacyFullSize];
            expanded[prefix + IntentPacket.LegacyFullSize] = count;
            int copyCount = Math.Min((int)count, NetFireEvents.Capacity);
            for (int i = 0; i < copyCount; i++)
            {
                int oldAt = prefix + IntentPacket.LegacyFullSize + 1 + i * FireEvent.LegacySize;
                int newAt = prefix + IntentPacket.LegacyFullSize + 1 + i * FireEvent.Size;
                converted.Slice(oldAt, FireEvent.LegacySize).CopyTo(expanded.AsSpan(newAt));
                // PoseFlags/origin/direction remain zero: old recordings keep
                // slice-2 timing but cannot claim an exact source shot pose.
            }
            converted = expanded;
        }

        if (protocol < 29 && type == PacketType.MatchState
            && converted.Length != 1 + MatchStatePacket.Size)
        {
            Require(converted.Length is 96 or 104);
            byte[] expanded = new byte[1 + MatchStatePacket.Size];
            converted.CopyTo(expanded);
            // Protocols before 12 had no stream identity on roster/snapshots/intents.
            // Normalize the whole replay to the zero identity instead of inventing one.
            if (protocol < 12)
                BinaryPrimitives.WriteUInt16LittleEndian(expanded.AsSpan(14), 0);
            converted = expanded;
        }

        if (protocol < 28 && type == PacketType.MatchState)
        {
            Require(converted.Length == 1 + MatchStatePacket.Size);
            byte[] result = converted.ToArray();
            // Historical bits were negative ("disable X"). Current replay state stores
            // the positive rule so a zero-filled old packet retains the old defaults.
            result[11] ^= MatchStatePacket.FlagShadowFreeze | MatchStatePacket.FlagSpawnProtection;
            converted = result;
        }

        if (protocol is >= 24 and < 31 && type == PacketType.SessionState)
        {
            Require(converted.Length == 1 + SessionStatePacket.Protocol28Size);
            byte[] result = new byte[1 + SessionStatePacket.Size];
            converted.CopyTo(result);
            ushort old = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(15));
            ushort lobby = (ushort)(((old & 8) >> 3) | ((old & 16) >> 3) | ((old & 32) >> 3) | ((old & 64) >> 3));
            uint modifiers = (uint)((old & 7) | ((old & 128) >> 4) | ((old & 256) >> 4)
                | ((old & 512) >> 4) | ((old & 1024) >> 4) | ((old & 2048) >> 4) | ((old & 4096) >> 4) | ((old & 8192) >> 3));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(15), lobby);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(1 + SessionStatePacket.Protocol28Size), modifiers);
            return result;
        }

        if (protocol is >= 27 and < 31 && type == PacketType.PostMatchReport)
        {
            Require(converted.Length == 1 + PostMatchReportPacket.HeaderSize + 8 * PostMatchReportPacket.LegacyEntrySize);
            byte[] result = New(PacketType.PostMatchReport, PostMatchReportPacket.Size);
            converted.Slice(1, 3).CopyTo(result.AsSpan(1));
            for (int i = 0; i < 8; i++)
                converted.Slice(4 + i * PostMatchReportPacket.LegacyEntrySize, PostMatchReportPacket.LegacyEntrySize)
                    .CopyTo(result.AsSpan(4 + i * PostMatchReportPacket.EntrySize));
            return result;
        }

        if (protocol < 24 && type == PacketType.Snapshot)
            return ConvertLegacySnapshot(converted, protocol);

        if (protocol is >= 24 and < 29 && type is PacketType.Snapshot or PacketType.SnapshotFast)
        {
            var body = converted[1..];
            Require(body.Length >= SnapshotHeader.Size);
            var header = SnapshotHeader.Read(body);
            Require(header.PlayerCount <= 8);
            bool fast = type == PacketType.SnapshotFast;
            int oldSize = PlayerState.LegacySize - (fast ? 7 : 0);
            int newSize = oldSize + Mods.EnhancedHunters.EnhancedHunterNetState.Size;
            int oldTail = SnapshotHeader.Size + header.PlayerCount * oldSize;
            Require(body.Length >= oldTail);
            byte[] expanded = new byte[converted.Length + header.PlayerCount * (newSize - oldSize)];
            converted[..(1 + SnapshotHeader.Size)].CopyTo(expanded);
            for (int i = 0; i < header.PlayerCount; i++)
            {
                int offset = 1 + SnapshotHeader.Size + i * newSize;
                body.Slice(SnapshotHeader.Size + i * oldSize, oldSize).CopyTo(expanded.AsSpan(offset));
                expanded[offset + oldSize + 1] = byte.MaxValue;
            }
            body[oldTail..].CopyTo(expanded.AsSpan(1 + SnapshotHeader.Size + header.PlayerCount * newSize));
            return expanded;
        }

        if (protocol is 29 or 30 && type == PacketType.MatchState)
        {
            Require(converted.Length == 1 + MatchStatePacket.Size);
            byte[] result = converted.ToArray();
            ushort old = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(104));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(104), (ushort)((old & 8192) >> 3));
            return result;
        }
        return converted;
    }

    private static ReadOnlySpan<byte> ConvertIdentity(ReadOnlySpan<byte> packet, int protocol)
    {
        if (protocol >= 27 || packet.IsEmpty) return packet;
        PacketType type = (PacketType)packet[0];
        ReadOnlySpan<byte> source = packet[1..];

        if (type == PacketType.Roster)
        {
            if (protocol >= 13)
            {
                int header = protocol >= 26 ? 18 : 17;
                int entry = protocol >= 26 ? 27 : 25;
                Require(source.Length == header + RosterPacket.MaxSlots * entry);
                byte[] result = New(type, RosterPacket.Size);
                source[..header].CopyTo(result.AsSpan(1));
                for (int i = 0; i < RosterPacket.MaxSlots; i++)
                {
                    var old = source.Slice(header + i * entry, entry);
                    var row = result.AsSpan(1 + RosterPacket.HeaderSize + i * RosterPacket.EntrySize);
                    old[..5].CopyTo(row);
                    CopyName(old.Slice(5, 16), row.Slice(5, PlayerNameCodec.MaxWireBytes));
                    old[21..].CopyTo(row[(5 + PlayerNameCodec.MaxWireBytes)..]);
                }
                return result;
            }

            if (protocol == 12)
            {
                const int header = 15, entry = 23;
                Require(source.Length == header + RosterPacket.MaxSlots * entry);
                byte[] result = New(type, RosterPacket.Size);
                source[..header].CopyTo(result.AsSpan(1));
                for (int i = 0; i < RosterPacket.MaxSlots; i++)
                {
                    var old = source.Slice(header + i * entry, entry);
                    var row = result.AsSpan(1 + RosterPacket.HeaderSize + i * RosterPacket.EntrySize);
                    old[..5].CopyTo(row);
                    CopyName(old.Slice(5, 16), row.Slice(5, PlayerNameCodec.MaxWireBytes));
                    old.Slice(21, 2).CopyTo(row[(5 + PlayerNameCodec.MaxWireBytes)..]);
                }
                return result;
            }

            int oldEntry = protocol >= 6 ? 21 : 20;
            Require(source.Length == 1 + RosterPacket.MaxSlots * oldEntry);
            byte[] legacy = New(type, RosterPacket.Size);
            byte count = Math.Min(source[0], (byte)RosterPacket.MaxSlots);
            legacy[1] = count;
            for (int i = 0; i < RosterPacket.MaxSlots; i++)
            {
                var old = source.Slice(1 + i * oldEntry, oldEntry);
                var row = legacy.AsSpan(1 + RosterPacket.HeaderSize + i * RosterPacket.EntrySize);
                row[0] = old[0];
                row[1] = old[1];
                int pingAt, nameAt;
                if (protocol >= 6)
                {
                    row[2] = old[2];
                    pingAt = 3; nameAt = 5;
                }
                else
                {
                    pingAt = 2; nameAt = 4;
                }
                old.Slice(pingAt, 2).CopyTo(row[3..]);
                CopyName(old.Slice(nameAt, 16), row.Slice(5, PlayerNameCodec.MaxWireBytes));
                if (i < count)
                    BinaryPrimitives.WriteUInt16LittleEndian(row[(5 + PlayerNameCodec.MaxWireBytes)..], 1);
            }
            return legacy;
        }

        if (type == PacketType.Chat)
        {
            if (source.Length != 2 + 16 + ChatPacket.MaxTextBytes)
                return protocol < 24 ? ReadOnlySpan<byte>.Empty : throw Malformed();
            return Expand(type, source, 2, ChatPacket.MaxTextBytes);
        }
        if (type == PacketType.VoteState)
        {
            if (source.Length != 1 + VoteStatePacket.MaxRoomBytes + 16 + 6)
                return protocol < 24 ? ReadOnlySpan<byte>.Empty : throw Malformed();
            return Expand(type, source, 1 + VoteStatePacket.MaxRoomBytes, 6);
        }
        if (type == PacketType.SessionState && protocol < 24)
        {
            // SessionState evolved substantially before protocol 24. The replay's
            // core world is still reconstructible from MatchState/Roster/Snapshot,
            // so treat the old lobby/config packet as optional rather than parsing
            // future fields out of historical bytes.
            return ReadOnlySpan<byte>.Empty;
        }
        if (type == PacketType.PostMatchReport)
        {
            if (source.Length != 3 + PostMatchReportPacket.MaxEntries * 44)
                return protocol < 24 ? ReadOnlySpan<byte>.Empty : throw Malformed();
            byte[] result = New(type, PostMatchReportPacket.Size);
            source[..3].CopyTo(result.AsSpan(1));
            for (int i = 0; i < PostMatchReportPacket.MaxEntries; i++)
            {
                var old = source.Slice(3 + i * 44, 44);
                var row = result.AsSpan(1 + 3 + i * PostMatchReportPacket.EntrySize);
                old[..28].CopyTo(row);
                CopyName(old[28..], row.Slice(28, PostMatchReportPacket.MaxNameBytes));
            }
            return result;
        }
        if (type == PacketType.SessionState && protocol == 24)
        {
            Require(source.Length == 41 + HostRequestPacket.MaxRoomBytes);
            byte[] result = New(type, SessionStatePacket.Protocol28Size);
            source.CopyTo(result.AsSpan(1));
            return result;
        }
        return packet;
    }

    private static ReadOnlySpan<byte> ConvertLegacyIntent(ReadOnlySpan<byte> packet, int protocol)
    {
        PacketType type = (PacketType)packet[0];
        int prefix = type == PacketType.SlotIntent ? 2 : 1;
        Require(packet.Length >= prefix);
        ReadOnlySpan<byte> source = packet[prefix..];

        int oldCore = protocol <= 4 ? 69 : protocol <= 6 ? 73 : protocol <= 11 ? 74 : 88;
        int oldState = protocol <= 6 ? 0 : protocol <= 18 ? 4 : protocol == 19 ? 8 : protocol == 20 ? 10 : 14;
        Require(source.Length == oldCore || source.Length == oldCore + oldState);

        byte[] result = new byte[prefix + IntentPacket.LegacyFullSize];
        packet[..prefix].CopyTo(result);
        Span<byte> destination = result.AsSpan(prefix);
        source[..oldCore].CopyTo(destination);

        if (protocol < 12)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination[74..], 0);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[76..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[84..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[86..], 1);
            if (source.Length > oldCore)
                source[oldCore..].CopyTo(destination[IntentPacket.Size..]);
        }
        else if (source.Length > oldCore)
        {
            source[oldCore..].CopyTo(destination[IntentPacket.Size..]);
        }

        // Protocol 18 replaced eight uint press masks with sixteen sequenced
        // ushort edges in the same 32 bytes. Interpreting the older masks as
        // edge records can create phantom repeated shots, so keep level buttons
        // and omit those optional historical edges.
        if (protocol < 18)
            destination.Slice(21, IntentPacket.EdgeHistoryBytes).Clear();

        return result;
    }

    private static ReadOnlySpan<byte> ConvertLegacySnapshot(ReadOnlySpan<byte> packet, int protocol)
    {
        ReadOnlySpan<byte> body = packet[1..];
        int oldHeader = protocol < 12 ? 13 : SnapshotHeader.Size;
        Require(body.Length >= oldHeader);
        int count = protocol < 12 ? body[12] : SnapshotHeader.Read(body).PlayerCount;
        Require(count is >= 0 and <= RosterPacket.MaxSlots);

        int oldPlayer = protocol < 12 ? 64 : protocol == 12 ? 130 : protocol < 19 ? 114 : 117;
        int oldTail = oldHeader + count * oldPlayer;
        Require(body.Length >= oldTail);

        int tailBytes = protocol < 12
            ? NetMatchTimeSync.Size + NetHealthSync.HeaderSize
            : body.Length - oldTail;
        if (protocol >= 12)
            Require(tailBytes >= NetMatchTimeSync.Size + NetHealthSync.HeaderSize);

        byte[] result = new byte[1 + SnapshotHeader.Size + count * PlayerState.Size + tailBytes];
        result[0] = (byte)PacketType.Snapshot;
        if (protocol < 12)
        {
            body[..oldHeader].CopyTo(result.AsSpan(1));
            // Stream identity did not exist yet. Zero is shared with the
            // converted match/roster/intent packets for this replay.
            result.AsSpan(1 + 13, SnapshotHeader.Size - 13).Clear();
        }
        else
        {
            body[..SnapshotHeader.Size].CopyTo(result.AsSpan(1));
        }

        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> old = body.Slice(oldHeader + i * oldPlayer, oldPlayer);
            Span<byte> current = result.AsSpan(1 + SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Size);
            ConvertLegacyPlayer(old, current, protocol);
        }

        Span<byte> tail = result.AsSpan(1 + SnapshotHeader.Size + count * PlayerState.Size);
        if (protocol < 12)
        {
            tail.Clear();
            // Empty health state for normalized match identity zero.
            BinaryPrimitives.WriteUInt16LittleEndian(tail[NetMatchTimeSync.Size..], 0);
        }
        else
        {
            body[oldTail..].CopyTo(tail);
        }
        return result;
    }

    private static void ConvertLegacyPlayer(ReadOnlySpan<byte> source, Span<byte> destination, int protocol)
    {
        destination.Clear();
        // Enhanced Hunter state did not exist. Its target slot uses FF as "none".
        destination[PlayerState.LegacySize + 1] = byte.MaxValue;

        if (protocol < 12)
        {
            Require(source.Length == 64);
            source[..42].CopyTo(destination);
            source.Slice(58, 6).CopyTo(destination[42..]);
            BinaryPrimitives.WriteUInt16LittleEndian(destination[48..], 1);
            bool spawned = (source[1] & PlayerState.FlagSpawned) != 0;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[50..], spawned ? (ushort)1 : (ushort)0);
            // Old single damage metadata has no generation/damage amount and
            // cannot be reconstructed safely into the modern event history.
            return;
        }

        if (protocol == 12)
        {
            Require(source.Length == 130);
            source[..42].CopyTo(destination);
            source.Slice(58, 6).CopyTo(destination[42..]);
            source.Slice(64, 6).CopyTo(destination[48..]);
            source.Slice(70, 4 * DamageEvent.Size).CopyTo(destination[54..]);
            return;
        }

        if (protocol < 19)
        {
            Require(source.Length == 114);
            source.CopyTo(destination);
            return;
        }

        Require(source.Length == 117);
        source.CopyTo(destination);
    }

    private static byte[] Expand(PacketType type, ReadOnlySpan<byte> source, int before, int after)
    {
        Require(source.Length == before + 16 + after);
        byte[] result = New(type, before + PlayerNameCodec.MaxWireBytes + after);
        source[..before].CopyTo(result.AsSpan(1));
        CopyName(source.Slice(before, 16), result.AsSpan(1 + before, PlayerNameCodec.MaxWireBytes));
        source[(before + 16)..].CopyTo(result.AsSpan(1 + before + PlayerNameCodec.MaxWireBytes));
        return result;
    }

    private static void CopyName(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        string name = ChatPacket.ReadAscii(source);
        PlayerNameCodec.TryEncode(PlayerNameCodec.Clamp(name), destination, out _);
    }

    private static byte[] New(PacketType type, int size)
    {
        var result = new byte[size + 1];
        result[0] = (byte)type;
        return result;
    }

    private static InvalidDataException Malformed()
        => new("Malformed legacy replay packet.");
    private static void Require(bool valid)
    {
        if (!valid) throw Malformed();
    }
}
