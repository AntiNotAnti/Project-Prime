using FruityPrime.Server.Shared;

namespace FruityPrime.Server.Node.Lobbies.Queue;

/// <summary>
/// Server-owned queue priority. The public surface currently has one normal
/// class; keeping it explicit leaves room for an authenticated policy to add a
/// class later without accepting client timestamps or client-chosen ordering.
/// </summary>
public enum LobbyQueuePriorityClass
{
    Standard = 0,
    Normal = Standard
}

/// <summary>Mutable state for one waitlist identity. It is only accessed while
/// the owning LobbyManager serialization lock is held.</summary>
public class Entry
{
    internal Entry(Guid sessionId, HumanIdentityKey identity, string displayName,
        LobbyQueueRequestedRole requestedRole, byte? requestedTeam, long queueSequence,
        DateTimeOffset enqueuedAt, LobbyQueuePriorityClass priorityClass = LobbyQueuePriorityClass.Standard)
    {
        SessionId = sessionId;
        Identity = identity;
        DisplayName = displayName;
        RequestedRole = requestedRole;
        RequestedTeam = requestedTeam;
        QueueSequence = queueSequence;
        EnqueuedAt = enqueuedAt;
        PriorityClass = priorityClass;
        State = LobbyQueueEntryState.Queued;
    }

    public Guid SessionId { get; }
    public HumanIdentityKey Identity { get; }
    public string DisplayName { get; }
    public LobbyQueueRequestedRole RequestedRole { get; }
    public byte? RequestedTeam { get; }
    public long QueueSequence { get; }
    public DateTimeOffset EnqueuedAt { get; }
    public LobbyQueuePriorityClass PriorityClass { get; }
    public LobbyQueuePriorityClass Priority => PriorityClass;
    public LobbyQueueEntryState State { get; private set; }
    public SeatReservation? Reservation { get; private set; }

    public Guid? OfferId => Reservation?.OfferId;
    public DateTimeOffset? OfferExpiresAt => Reservation?.ExpiresAt;

    public TimeSpan WaitedAt(DateTimeOffset now) => now <= EnqueuedAt ? TimeSpan.Zero : now - EnqueuedAt;

    internal void Offer(SeatReservation reservation)
    {
        if (State != LobbyQueueEntryState.Queued) throw new InvalidOperationException("Only queued entries can be offered.");
        if (reservation.Identity != Identity || reservation.SessionId != SessionId || reservation.QueueSequence != QueueSequence)
            throw new ArgumentException("Reservation does not belong to the entry.");
        Reservation = reservation;
        State = LobbyQueueEntryState.SeatOffered;
    }

    internal SeatReservation RequireOffer(Guid offerId)
    {
        if (State != LobbyQueueEntryState.SeatOffered || Reservation is not { } reservation || reservation.OfferId != offerId)
            throw new InvalidOperationException("The seat offer is stale.");
        return reservation;
    }

    internal void Transition(LobbyQueueEntryState state)
    {
        if (state is not (LobbyQueueEntryState.Promoted or LobbyQueueEntryState.Expired or LobbyQueueEntryState.Cancelled))
            throw new ArgumentOutOfRangeException(nameof(state));
        if (State is LobbyQueueEntryState.Promoted or LobbyQueueEntryState.Expired or LobbyQueueEntryState.Cancelled)
            return;
        State = state;
        Reservation = null;
    }

    internal void Requeue()
    {
        if (State != LobbyQueueEntryState.SeatOffered) throw new InvalidOperationException("Only offered entries can be requeued.");
        Reservation = null;
        State = LobbyQueueEntryState.Queued;
    }
}
