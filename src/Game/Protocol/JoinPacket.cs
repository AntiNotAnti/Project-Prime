using System;
using System.Buffers.Binary;

namespace MphRead.Mods.Network
{
    public readonly record struct JoinPacket(byte Protocol, ulong Nonce, Hunter Hunter, string Name,
        ulong PreviousConnectionId = 0, string Ticket = "", bool Observer = false, uint WireMatchId = 0,
        Guid AdmissionId = default)
    {
        public const int Size = 1 + 8 + 1 + RosterPacket.MaxNameBytes + 8;
        // Joins are authenticated in the production path, so reserve the
        // 16-byte tag even while parsing the disabled legacy seam. This keeps
        // one exact payload budget for every 1,024-byte gameplay datagram.
        // Keep this a compile-time bound so packet writers can size stack
        // buffers without depending on a runtime property. Authenticated
        // payloads are 1,024 - header - tag bytes.
        public const int MaxPayloadBytes = NetConfig.MaxPacketSize - NetHeader.Size - NetAuthentication.TagSize;
        public const int MaxTicketBytes = MaxPayloadBytes - Size - 3;
        // Routed/authenticated joins carry both the public wire match and the
        // 16-byte admission identity in addition to the extension header.
        public const int MaxRoutedTicketBytes = MaxTicketBytes - 4 - 16;
        private const byte ObserverFlag = 1;
        private const byte WireMatchFlag = 2;
        private const byte AdmissionFlag = 4;
        public override string ToString() => $"JoinPacket {{ Protocol = {Protocol}, Hunter = {Hunter}, HasTicket = {!string.IsNullOrEmpty(Ticket)}, AdmissionId = {AdmissionId} }}";
        public int EncodedSize => Size + (string.IsNullOrEmpty(Ticket) && !Observer && WireMatchId == 0 && AdmissionId == Guid.Empty
            ? 0 : 3 + (WireMatchId == 0 ? 0 : 4) + (AdmissionId == Guid.Empty ? 0 : 16) + (Ticket?.Length ?? 0));
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
            if (destination.Length < EncodedSize || EncodedSize > MaxPayloadBytes
                || (WireMatchId != 0 || AdmissionId != Guid.Empty) && (Ticket?.Length ?? 0) > MaxRoutedTicketBytes
                || (!string.IsNullOrEmpty(Ticket) && !ValidTicketText(Ticket)))
                throw new ArgumentException("Invalid join credential or output buffer.");
            destination[0] = Protocol;
            BinaryPrimitives.WriteUInt64LittleEndian(destination[1..], Nonce);
            destination[9] = (byte)Hunter;
            NetText.Write(destination.Slice(10, RosterPacket.MaxNameBytes), Name);
            BinaryPrimitives.WriteUInt64LittleEndian(destination[26..], PreviousConnectionId);
            if (!string.IsNullOrEmpty(Ticket) || Observer || WireMatchId != 0 || AdmissionId != Guid.Empty)
            {
                destination[Size] = (byte)((Observer ? ObserverFlag : 0)
                    | (WireMatchId != 0 ? WireMatchFlag : 0)
                    | (AdmissionId != Guid.Empty ? AdmissionFlag : 0));
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(Size + 1)..], checked((ushort)(Ticket?.Length ?? 0)));
                int extensionOffset = Size + 3;
                if (WireMatchId != 0)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(destination[extensionOffset..], WireMatchId);
                    extensionOffset += 4;
                }
                if (AdmissionId != Guid.Empty)
                {
                    if (!AdmissionId.TryWriteBytes(destination[extensionOffset..(extensionOffset + 16)]))
                        throw new ArgumentException("Invalid admission identity.");
                    extensionOffset += 16;
                }
                if (!string.IsNullOrEmpty(Ticket)) System.Text.Encoding.ASCII.GetBytes(Ticket.AsSpan(), destination[extensionOffset..]);
            }
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out JoinPacket packet)
        {
            packet = default;
            if (source.Length < Size || source.Length > MaxPayloadBytes || source[9] > (byte)Hunter.Guardian
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
            uint wireMatchId = 0;
            bool observer = false;
            Guid admissionId = Guid.Empty;
            if (source.Length != Size)
            {
                byte flags = source[Size];
                if ((flags & ~(ObserverFlag | WireMatchFlag | AdmissionFlag)) != 0) return false;
                observer = (flags & ObserverFlag) != 0;
                bool routed = (flags & WireMatchFlag) != 0;
                bool hasAdmission = (flags & AdmissionFlag) != 0;
                int ticketOffset = Size + 3 + (routed ? 4 : 0) + (hasAdmission ? 16 : 0);
                if (source.Length < ticketOffset || BinaryPrimitives.ReadUInt16LittleEndian(source[(Size + 1)..]) != source.Length - ticketOffset) return false;
                if (routed)
                {
                    wireMatchId = BinaryPrimitives.ReadUInt32LittleEndian(source[(Size + 3)..]);
                    if (wireMatchId == 0) return false;
                }
                if (hasAdmission)
                {
                    int admissionOffset = Size + 3 + (routed ? 4 : 0);
                    admissionId = new Guid(source.Slice(admissionOffset, 16));
                    if (admissionId == Guid.Empty) return false;
                }
                int ticketLength = source.Length - ticketOffset;
                if ((routed || hasAdmission) && ticketLength > MaxRoutedTicketBytes || ticketLength > MaxTicketBytes) return false;
                foreach (byte value in source[ticketOffset..]) if (value > 127) return false;
                ticket = System.Text.Encoding.ASCII.GetString(source[ticketOffset..]);
                if (ticket.Length == 0 ? !observer && !routed : !ValidTicketText(ticket)) return false;
            }
            packet = new JoinPacket(source[0], BinaryPrimitives.ReadUInt64LittleEndian(source[1..]),
                (Hunter)source[9], NetText.Read(source.Slice(10, RosterPacket.MaxNameBytes)),
                BinaryPrimitives.ReadUInt64LittleEndian(source[26..]), ticket, observer, wireMatchId, admissionId);
            return packet.Name.Length > 0;
        }

        /// <summary>
        /// Reads only the bounded public admission route from an untrusted join
        /// payload. The MAC is verified by the owning match before the full
        /// packet is parsed or any ticket work is queued.
        /// </summary>
        public static bool TryReadAdmissionId(ReadOnlySpan<byte> source, out Guid admissionId)
        {
            admissionId = Guid.Empty;
            if (source.Length < Size + 3 || source.Length > MaxPayloadBytes) return false;
            byte flags = source[Size];
            if ((flags & ~(ObserverFlag | WireMatchFlag | AdmissionFlag)) != 0
                || (flags & AdmissionFlag) == 0) return false;
            bool routed = (flags & WireMatchFlag) != 0;
            int offset = Size + 3 + (routed ? 4 : 0);
            if (source.Length < offset + 16) return false;
            if (routed && BinaryPrimitives.ReadUInt32LittleEndian(source[(Size + 3)..]) == 0) return false;
            admissionId = new Guid(source.Slice(offset, 16));
            return admissionId != Guid.Empty;
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
                || source[4] < (byte)ReliableEventType.Welcome || source[4] > (byte)ReliableEventType.TimingProfileApplied
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
