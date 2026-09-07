using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public readonly record struct JoinPacket(byte Protocol, ulong Nonce, Hunter Hunter, string Name,
        ulong PreviousConnectionId = 0)
    {
        public const int Size = 1 + 8 + 1 + RosterPacket.MaxNameBytes + 8;

        public void Write(Span<byte> destination)
        {
            destination[0] = Protocol;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[1..], Nonce);
            destination[9] = (byte)Hunter;
            NetText.Write(destination.Slice(10, RosterPacket.MaxNameBytes), Name);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[26..], PreviousConnectionId);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out JoinPacket packet)
        {
            packet = default;
            if (source.Length != Size || source[9] > (byte)Hunter.Guardian
                || BinaryPrimitives.ReadUInt64LittleEndian(source[1..]) == 0)
            {
                return false;
            }
            for (int i = 10; i < 26; i++)
            {
                if (source[i] != 0 && (source[i] < 32 || source[i] > 126))
                {
                    return false;
                }
            }
            packet = new JoinPacket(source[0], BinaryPrimitives.ReadUInt64LittleEndian(source[1..]),
                (Hunter)source[9], NetText.Read(source.Slice(10, RosterPacket.MaxNameBytes)),
                BinaryPrimitives.ReadUInt64LittleEndian(source[26..]));
            return packet.Name.Length > 0;
        }
    }

    public readonly record struct JoinAcceptedPacket(ulong ClientNonce, byte Slot, uint MatchId,
        uint ServerTick, byte TickRate, MatchRules Rules)
    {
        public GameMode Mode => Rules?.Mode.ToLegacyMode() ?? GameMode.None;
        public string Room => Rules?.RoomKey ?? "";
        public const int Size = 18 + MatchRulesWire.Size;
        public JoinAcceptedPacket(ulong nonce, byte slot, uint matchId, uint tick, byte rate, GameMode mode, string room)
            : this(nonce, slot, matchId, tick, rate, MatchRules.CreateDefault(mode.ToMatchMode(), room)) { }

        public void Write(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, ClientNonce);
            destination[8] = Slot;
            BinaryPrimitives.WriteUInt32LittleEndian(destination[9..], MatchId);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[13..], ServerTick);
            destination[17] = TickRate;
            MatchRulesWire.Write(destination[18..], Rules);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out JoinAcceptedPacket packet)
        {
            packet = default;
            if (source.Length != Size || source[8] >= RosterPacket.MaxSlots || source[17] != 60
                || BinaryPrimitives.ReadUInt64LittleEndian(source) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[9..]) == 0
                || !MatchRulesWire.TryRead(source[18..], out MatchRules rules)
                || source[8] >= rules.MaxPlayers) { return false; }
            packet = new(BinaryPrimitives.ReadUInt64LittleEndian(source), source[8],
                BinaryPrimitives.ReadUInt32LittleEndian(source[9..]),
                BinaryPrimitives.ReadUInt32LittleEndian(source[13..]), source[17], rules);
            return true;
        }
    }

    public static class ReliableEventPacket
    {
        public const int HeaderSize = 7;

        public static bool TryRead(ReadOnlySpan<byte> source, out uint id,
            out ReliableEventType type, out ReadOnlySpan<byte> payload)
        {
            id = default;
            type = default;
            payload = default;
            if (source.Length < HeaderSize || source.Length > HeaderSize + ReliableChannel.MaxPayloadSize
                || source[4] < (byte)ReliableEventType.Welcome || source[4] > (byte)ReliableEventType.ChatRequest
                || BinaryPrimitives.ReadUInt16LittleEndian(source[5..]) != source.Length - HeaderSize)
            {
                return false;
            }
            id = BinaryPrimitives.ReadUInt32LittleEndian(source);
            type = (ReliableEventType)source[4];
            payload = source[HeaderSize..];
            return true;
        }

        public static int Write(Span<byte> destination, uint id, ReliableEventType type,
            ReadOnlySpan<byte> payload)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, id);
            destination[4] = (byte)type;
            BinaryPrimitives.WriteUInt16LittleEndian(destination[5..], checked((ushort)payload.Length));
            payload.CopyTo(destination[HeaderSize..]);
            return HeaderSize + payload.Length;
        }
    }
}
