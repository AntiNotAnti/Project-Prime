using FruityPrime.Server.Shared;

namespace FruityPrime.Server.Node.Lobbies.Queue;

/// <summary>Descriptive compatibility name for callers that prefer the full
/// waitlist entry type. It carries the same state as <see cref="Entry"/>.</summary>
public sealed class LobbyWaitlistEntry : Entry
{
    internal LobbyWaitlistEntry(Guid sessionId, HumanIdentityKey identity, string displayName,
        LobbyQueueRequestedRole requestedRole, byte? requestedTeam, long queueSequence, DateTimeOffset enqueuedAt)
        : base(sessionId, identity, displayName, requestedRole, requestedTeam, queueSequence, enqueuedAt) { }
}
