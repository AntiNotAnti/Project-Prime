using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public readonly record struct JoinPacket(byte Protocol, ulong Nonce, Hunter Hunter, string Name,
        ulong PreviousConnectionId = 0, string Ticket = "", bool Observer = false)
    {
        public const int Size = 1 + 8 + 1 + RosterPacket.MaxNameBytes + 8;
        public const int MaxTicketBytes = 963;
        public override string ToString() => $"JoinPacket {{ Protocol = {Protocol}, Hunter = {Hunter}, HasTicket = {!string.IsNullOrEmpty(Ticket)} }}";
        public int EncodedSize => Size + (string.IsNullOrEmpty(Ticket) && !Observer ? 0 : 3 + (Ticket?.Length ?? 0));
        public static bool ValidTicketText(string ticket)
        {
            if (ticket.Length == 0 || ticket.Length > MaxTicketBytes) return false;
            int dots = 0;
            foreach (char c in ticket)
                if (c == '.') dots++;
                else if (!(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return false;
            return dots == 2 && ticket[0] != '.' && ticket[^1] != '.' && !ticket.Contains("..");
        }

        public void Write(Span<byte> destination)
        {
            if (destination.Length < EncodedSize || (!string.IsNullOrEmpty(Ticket) && !ValidTicketText(Ticket)))
                throw new ArgumentException("Invalid join credential or output buffer.");
            destination[0] = Protocol;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[1..], Nonce);
            destination[9] = (byte)Hunter;
            NetText.Write(destination.Slice(10, RosterPacket.MaxNameBytes), Name);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[26..], PreviousConnectionId);
            if (!string.IsNullOrEmpty(Ticket) || Observer)
            {
                destination[Size] = Observer ? (byte)1 : (byte)0;
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(Size + 1)..], checked((ushort)(Ticket?.Length ?? 0)));
                if (!string.IsNullOrEmpty(Ticket)) System.Text.Encoding.ASCII.GetBytes(Ticket, destination.Slice(Size + 3, Ticket.Length));
            }
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out JoinPacket packet)
        {
            packet = default;
            if ((source.Length != Size && (source.Length < Size + 3 || source.Length > Size + 3 + MaxTicketBytes)) || source[9] > (byte)Hunter.Guardian
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
            string ticket = "";
            if (source.Length != Size)
            {
                if (source[Size] > 1 || BinaryPrimitives.ReadUInt16LittleEndian(source[(Size + 1)..]) != source.Length - Size - 3) return false;
                foreach (byte b in source[(Size + 3)..]) if (b > 127) return false;
                ticket = System.Text.Encoding.ASCII.GetString(source[(Size + 3)..]);
                if (ticket.Length == 0 ? source[Size] != 1 : !ValidTicketText(ticket)) return false;
            }
            packet = new JoinPacket(source[0], BinaryPrimitives.ReadUInt64LittleEndian(source[1..]),
                (Hunter)source[9], NetText.Read(source.Slice(10, RosterPacket.MaxNameBytes)),
                BinaryPrimitives.ReadUInt64LittleEndian(source[26..]), ticket, source.Length != Size && source[Size] == 1);
            return packet.Name.Length > 0;
        }
    }

    public readonly record struct JoinAcceptedPacket(ulong ClientNonce, byte Slot, uint MatchId,
        uint ServerTick, byte TickRate, MatchRules Rules)
    {
        public bool IsObserver => Slot == byte.MaxValue;
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
            MatchRulesWire.Write(destination.Slice(18, MatchRulesWire.Size), Rules);
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out JoinAcceptedPacket packet)
        {
            packet = default;
            if (source.Length != Size || (source[8] >= RosterPacket.MaxSlots && source[8] != byte.MaxValue) || source[17] != 60
                || BinaryPrimitives.ReadUInt64LittleEndian(source) == 0
                || BinaryPrimitives.ReadUInt32LittleEndian(source[9..]) == 0
                || !MatchRulesWire.TryRead(source[18..], out MatchRules rules)
                || source[8] != byte.MaxValue && source[8] >= rules.MaxPlayers) { return false; }
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
                || source[4] < (byte)ReliableEventType.Welcome || source[4] > (byte)ReliableEventType.IntermissionVote
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
