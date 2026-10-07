using System;
using System.Buffers.Binary;
using System.Text;

namespace MphRead.Mods.Network;

internal enum PartyReservationWireState : byte
{
    Validating,
    Reserved,
    Rejected,
    Expired,
    Cancelled
}

internal readonly record struct PartyReserveClaimPacket(
    Guid RequestId,
    uint ClientId,
    string CareerTicket)
{
    internal const int HeaderSize = 22;
    internal const int MaxTicketBytes = 768;
    internal int Size => HeaderSize + Encoding.ASCII.GetByteCount(CareerTicket);

    internal int Write(Span<byte> destination)
    {
        int ticketBytes = Encoding.ASCII.GetByteCount(CareerTicket);
        if (RequestId == Guid.Empty || ClientId == 0
            || ticketBytes is < 20 or > MaxTicketBytes
            || destination.Length < HeaderSize + ticketBytes)
            throw new ArgumentException("Invalid party reservation claim.");

        RequestId.TryWriteBytes(destination[..16]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], ClientId);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[20..], (ushort)ticketBytes);
        Encoding.ASCII.GetBytes(CareerTicket.AsSpan(), destination.Slice(HeaderSize, ticketBytes));
        return HeaderSize + ticketBytes;
    }

    internal static bool TryRead(
        ReadOnlySpan<byte> source, out PartyReserveClaimPacket packet)
    {
        packet = default;
        if (source.Length < HeaderSize)
            return false;

        Guid requestId = new(source[..16]);
        uint clientId = BinaryPrimitives.ReadUInt32LittleEndian(source[16..]);
        int ticketBytes = BinaryPrimitives.ReadUInt16LittleEndian(source[20..]);
        if (requestId == Guid.Empty || clientId == 0
            || ticketBytes is < 20 or > MaxTicketBytes
            || source.Length != HeaderSize + ticketBytes)
            return false;

        ReadOnlySpan<byte> ticketSpan = source.Slice(HeaderSize, ticketBytes);
        for (int i = 0; i < ticketSpan.Length; i++)
        {
            if (ticketSpan[i] is < 0x21 or > 0x7e)
                return false;
        }
        string ticket = Encoding.ASCII.GetString(ticketSpan);
        if (!ticket.StartsWith("pp1.", StringComparison.Ordinal))
            return false;

        packet = new PartyReserveClaimPacket(requestId, clientId, ticket);
        return true;
    }
}

internal readonly record struct PartyReserveStatePacket(
    Guid RequestId,
    Guid ReservationId,
    PartyReservationWireState State,
    byte Slot,
    byte RequiredCount,
    byte AdmittedCount,
    ulong AuthorityEpoch,
    uint ExpiresInTicks)
{
    internal const byte NoSlot = byte.MaxValue;
    internal const int Size = 48;

    internal void Write(Span<byte> destination)
    {
        if (destination.Length < Size
            || RequestId == Guid.Empty
            || (State == PartyReservationWireState.Reserved
                && (ReservationId == Guid.Empty || Slot > 7
                    || RequiredCount is < 1 or > 8
                    || AdmittedCount > RequiredCount
                    || AuthorityEpoch == 0 || ExpiresInTicks == 0)))
            throw new ArgumentException("Invalid party reservation state.");

        destination[..Size].Clear();
        RequestId.TryWriteBytes(destination[..16]);
        ReservationId.TryWriteBytes(destination.Slice(16, 16));
        destination[32] = (byte)State;
        destination[33] = Slot;
        destination[34] = RequiredCount;
        destination[35] = AdmittedCount;
        BinaryPrimitives.WriteUInt64LittleEndian(destination[36..], AuthorityEpoch);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[44..], ExpiresInTicks);
    }

    internal static bool TryRead(
        ReadOnlySpan<byte> source, out PartyReserveStatePacket packet)
    {
        packet = default;
        if (source.Length != Size
            || source[32] > (byte)PartyReservationWireState.Cancelled)
            return false;

        Guid requestId = new(source[..16]);
        Guid reservationId = new(source.Slice(16, 16));
        var state = (PartyReservationWireState)source[32];
        byte slot = source[33];
        byte required = source[34];
        byte admitted = source[35];
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(source[36..]);
        uint expires = BinaryPrimitives.ReadUInt32LittleEndian(source[44..]);

        if (requestId == Guid.Empty)
            return false;

        if (state == PartyReservationWireState.Reserved)
        {
            if (reservationId == Guid.Empty || slot > 7
                || required is < 1 or > 8 || admitted > required
                || epoch == 0 || expires == 0)
                return false;
        }
        else if (slot != NoSlot && slot > 7)
        {
            return false;
        }

        packet = new PartyReserveStatePacket(
            requestId, reservationId, state, slot, required,
            admitted, epoch, expires);
        return true;
    }
}

internal readonly record struct PartyReserveAcceptPacket(
    Guid RequestId,
    Guid ReservationId,
    ulong AuthorityEpoch)
{
    internal const int Size = 40;

    internal void Write(Span<byte> destination)
    {
        if (destination.Length < Size
            || RequestId == Guid.Empty
            || ReservationId == Guid.Empty
            || AuthorityEpoch == 0)
            throw new ArgumentException("Invalid party reservation acceptance.");

        RequestId.TryWriteBytes(destination[..16]);
        ReservationId.TryWriteBytes(destination.Slice(16, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(destination[32..], AuthorityEpoch);
    }

    internal static bool TryRead(
        ReadOnlySpan<byte> source, out PartyReserveAcceptPacket packet)
    {
        packet = default;
        if (source.Length != Size)
            return false;

        Guid requestId = new(source[..16]);
        Guid reservationId = new(source.Slice(16, 16));
        ulong epoch = BinaryPrimitives.ReadUInt64LittleEndian(source[32..]);
        if (requestId == Guid.Empty || reservationId == Guid.Empty || epoch == 0)
            return false;

        packet = new PartyReserveAcceptPacket(requestId, reservationId, epoch);
        return true;
    }
}
