using FruityPrime.Server.Shared;

namespace FruityPrime.Server.Node.Lobbies.Queue;

/// <summary>A server-owned, single-use reservation for one human player seat.</summary>
public sealed record SeatReservation(Guid OfferId, Guid SessionId, HumanIdentityKey Identity,
    long QueueSequence, DateTimeOffset ExpiresAt, LobbySeatPolicy Policy)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
