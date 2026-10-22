using System;
using MphRead.Mods.Network.Generated;

namespace MphRead.Mods.Network;

internal enum PartyReservationPurpose : byte { LeaderCreate = 1, MemberAttach = 2 }
internal enum PartyReservationWireState : byte { Verifying, Waiting, Activating, Reserved, Admitting, Expired, Rejected, Cancelled }
internal enum PartyReservationWireAction : byte { Accept = 1, DeclineSelf = 2, CancelGroup = 3 }
internal static class PartyReservationWire
{
    internal const byte Version = 1;
    internal const string ZeroId = "00000000-0000-0000-0000-000000000000";
    internal static bool Id(string value, bool zero = false)
        => value is { Length: 36 } && Guid.TryParseExact(value, "D", out Guid id)
            && (zero || id != Guid.Empty) && value == value.ToLowerInvariant();
    internal static bool Ticket(string value)
    {
        if (value is not { Length: >= 20 and <= 768 } || !value.StartsWith("pp1.", StringComparison.Ordinal)) return false;
        foreach (char c in value) if (c < 33 || c > 126) return false;
        return true;
    }
}

[NetPacket(PacketType.PartyReservationRequest, 57)]
internal readonly partial record struct PartyReservationRequestPacket(
    [NetRange(1,1)] byte Version, PartyReservationPurpose Purpose, ulong OperationNonce,
    [NetRange(1,uint.MaxValue)] uint ClientId, ulong QueueNonce,
    [NetString(36)] string RequestId, [NetString(36)] string PartyId,
    [NetRange(0,9007199254740991)] ulong PartyRevision,
    [NetString(36)] string TravelId, [NetRange(1,1000000)] uint TravelRevision,
    [NetRange(1,ushort.MaxValue)] ushort MatchId, ulong AuthorityEpoch,
    [NetString(768)] string CareerTicket)
{
    partial void ValidateSchema(ref bool valid) => valid = OperationNonce != 0 && AuthorityEpoch != 0
        && Purpose is PartyReservationPurpose.LeaderCreate or PartyReservationPurpose.MemberAttach
        && (Purpose == PartyReservationPurpose.LeaderCreate ? QueueNonce == 0 : QueueNonce != 0)
        && PartyReservationWire.Id(RequestId) && PartyReservationWire.Id(PartyId)
        && PartyReservationWire.Id(TravelId) && PartyReservationWire.Ticket(CareerTicket);
    public override string ToString() => "PartyReservationRequest(V1)";
}

[NetPacket(PacketType.PartyReservationState, 57)]
internal readonly partial record struct PartyReservationStatePacket(
    [NetRange(1,1)] byte Version, ulong OperationNonce, [NetRange(1,uint.MaxValue)] uint Revision,
    [NetString(36)] string RequestId, [NetString(36)] string ReservationId,
    [NetRange(0,9007199254740991)] ulong PartyRevision,
    [NetString(36)] string TravelId, [NetRange(1,1000000)] uint TravelRevision,
    [NetRange(1,ushort.MaxValue)] ushort MatchId, ulong AuthorityEpoch,
    PartyReservationWireState State, byte SelfSlot, [NetRange(0,8)] byte ReservedCount,
    [NetRange(0,3600)] uint RemainingTicks, ulong MemberLeaseNonce, ulong GroupQueueId,
    [NetRange(0,256)] ushort Position, [NetRange(0,256)] ushort QueueLength)
{
    partial void ValidateSchema(ref bool valid) => valid = OperationNonce != 0 && AuthorityEpoch != 0
        && State <= PartyReservationWireState.Cancelled && (SelfSlot <= 7 || SelfSlot == byte.MaxValue)
        && PartyReservationWire.Id(RequestId) && PartyReservationWire.Id(ReservationId, zero: true)
        && PartyReservationWire.Id(TravelId) && Position <= QueueLength
        && (State is not (PartyReservationWireState.Reserved or PartyReservationWireState.Admitting)
            || ReservationId != PartyReservationWire.ZeroId && ReservedCount > 0 && RemainingTicks > 0)
        && (SelfSlot == byte.MaxValue || MemberLeaseNonce != 0);
}

[NetPacket(PacketType.PartyReservationAction, 57)]
internal readonly partial record struct PartyReservationActionPacket(
    [NetRange(1,1)] byte Version, PartyReservationWireAction Action, ulong OperationNonce,
    [NetRange(1,uint.MaxValue)] uint ExpectedRevision,
    [NetString(36)] string RequestId, [NetString(36)] string ReservationId,
    ulong MemberLeaseNonce, [NetRange(1,ushort.MaxValue)] ushort MatchId, ulong AuthorityEpoch)
{
    partial void ValidateSchema(ref bool valid) => valid = OperationNonce != 0 && AuthorityEpoch != 0
        && Action is >= PartyReservationWireAction.Accept and <= PartyReservationWireAction.CancelGroup
        && PartyReservationWire.Id(RequestId) && PartyReservationWire.Id(ReservationId, zero: true)
        && (Action != PartyReservationWireAction.Accept || MemberLeaseNonce != 0
            && ReservationId != PartyReservationWire.ZeroId);
}
