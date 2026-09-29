using System;
using System.IO;
using System.Buffers.Binary;

namespace MphRead.Mods.Network;

/// <summary>Upgrade recorded packets at playback boundaries, never on a live connection.</summary>
internal static class ReplayIdentityCompatibility
{
    internal static bool Supports(int protocol) => protocol >= 24 && protocol <= NetConfig.ProtocolVersion;

    internal static ReadOnlySpan<byte> Convert(ReadOnlySpan<byte> packet, int protocol)
    {
        var converted = ConvertIdentity(packet, protocol);
        if (protocol < 28 && !converted.IsEmpty && (PacketType)converted[0] == PacketType.MatchState)
        {
            Require(converted.Length == 1 + MatchStatePacket.Size);
            byte[] result = converted.ToArray();
            result[11] ^= MatchStatePacket.FlagShadowFreeze | MatchStatePacket.FlagSpawnProtection;
            return result;
        }
        if (protocol < 29 && !converted.IsEmpty && (PacketType)converted[0] == PacketType.SessionState)
        {
            Require(converted.Length == 1 + SessionStatePacket.Protocol28Size);
            byte[] result = new byte[1 + SessionStatePacket.Size];
            converted.CopyTo(result);
            ushort old = BinaryPrimitives.ReadUInt16LittleEndian(result.AsSpan(15));
            ushort lobby = (ushort)(((old & 8) >> 3) | ((old & 16) >> 3) | ((old & 32) >> 3) | ((old & 64) >> 3));
            uint modifiers = (uint)((old & 7) | ((old & 128) >> 4) | ((old & 256) >> 4)
                | ((old & 512) >> 4) | ((old & 1024) >> 4) | ((old & 2048) >> 4) | ((old & 4096) >> 4));
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(15), lobby);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(1 + SessionStatePacket.Protocol28Size), modifiers);
            return result;
        }
        if (protocol is 27 or 28 && !converted.IsEmpty && (PacketType)converted[0] == PacketType.PostMatchReport)
        {
            Require(converted.Length == 1 + PostMatchReportPacket.HeaderSize + 8 * PostMatchReportPacket.LegacyEntrySize);
            byte[] result = New(PacketType.PostMatchReport, PostMatchReportPacket.Size);
            converted.Slice(1, 3).CopyTo(result.AsSpan(1));
            for (int i = 0; i < 8; i++)
                converted.Slice(4 + i * PostMatchReportPacket.LegacyEntrySize, PostMatchReportPacket.LegacyEntrySize)
                    .CopyTo(result.AsSpan(4 + i * PostMatchReportPacket.EntrySize));
            return result;
        }
        return converted;
    }

    private static ReadOnlySpan<byte> ConvertIdentity(ReadOnlySpan<byte> packet, int protocol)
    {
        if (protocol >= 27 || packet.IsEmpty) return packet;
        if (!Supports(protocol)) throw new InvalidDataException("Unsupported replay protocol.");
        PacketType type = (PacketType)packet[0];
        ReadOnlySpan<byte> source = packet[1..];
        if (type == PacketType.Roster)
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
        if (type == PacketType.Chat)
            return Expand(type, source, 2, ChatPacket.MaxTextBytes);
        if (type == PacketType.VoteState)
            return Expand(type, source, 1 + VoteStatePacket.MaxRoomBytes, 6);
        if (type == PacketType.PostMatchReport)
        {
            Require(source.Length == 3 + PostMatchReportPacket.MaxEntries * 44);
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
        // Legacy fields were ASCII with zero padding, independent of the native glyph codec.
        string name = ChatPacket.ReadAscii(source);
        PlayerNameCodec.TryEncode(PlayerNameCodec.Clamp(name), destination, out _);
    }
    private static byte[] New(PacketType type, int size) { var result = new byte[size + 1]; result[0] = (byte)type; return result; }
    private static void Require(bool valid) { if (!valid) throw new InvalidDataException("Malformed legacy identity packet."); }
}
