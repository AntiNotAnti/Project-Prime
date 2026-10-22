using MphRead.Mods.Network.Generated;

// Synthetic test IDs and protocol metadata ONLY. These are not PacketType IDs,
// are not live protocol reservations, and are never compiled into the game.
[NetPacket(201, 1)] internal readonly partial record struct QueueJoin(ulong ClientNonce);
[NetPacket(202, 1)] internal readonly partial record struct QueueLeave(ulong QueueId);
internal enum QueueStatus : byte { Waiting, NextMatch, Offered, Disconnected }
[NetPacket(203, 1)] internal readonly partial record struct QueueState(ulong QueueId,
    [NetRange(1,256)] ushort Position, [NetRange(1,256)] ushort Length, QueueStatus State);
[NetPacket(204, 1)] internal readonly partial record struct QueueOffer(ulong QueueId, ulong OfferId,
    uint MatchId, ulong AuthorityEpoch, [NetRange(1,18000)] uint ExpiresInTicks);
[NetPacket(205, 1)] internal readonly partial record struct QueueAccept(ulong QueueId, ulong OfferId, uint MatchId, ulong AuthorityEpoch);
[NetPacket(206, 1)] internal readonly partial record struct QueueDecline(ulong QueueId, ulong OfferId, uint MatchId, ulong AuthorityEpoch);

// Production reservation schema dependencies only. This fixture does not register
// live NetTransport handlers or replace the group-reservation protocol registry gate.
namespace MphRead.Mods.Network
{
    public enum PacketType : byte
    {
        PartyReservationRequest = 80, PartyReservationState = 81, PartyReservationAction = 82
    }
}
