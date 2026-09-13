using System.Collections.Immutable;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Node.Lobbies.Queue;

/// <summary>Bounded FIFO state for one lobby. The owning LobbyManager performs
/// all synchronization and lifecycle transitions; this class has no thread or
/// timer of its own.</summary>
public sealed class LobbyWaitlist
{
    public const int DefaultMaximumEntries = 64;
    public const int MaximumEntriesLimit = 1024;
    public const int MaxWaitlistPerLobby = MaximumEntriesLimit;

    private readonly int _maximumEntries;
    private readonly List<Entry> _entries = [];
    private long _nextSequence;
    private long _offersAccepted;
    private long _offersExpired;
    private long _offersDeclined;
    private long _totalWaitMilliseconds;
    private long _waitSamples;

    public LobbyWaitlist(int maximumEntries = DefaultMaximumEntries)
    {
        if (maximumEntries is < 1 or > MaximumEntriesLimit) throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        _maximumEntries = maximumEntries;
    }

    public int MaximumEntries => _maximumEntries;
    public int Count => _entries.Count(e => e.State is LobbyQueueEntryState.Queued or LobbyQueueEntryState.SeatOffered);
    public long OffersAccepted => _offersAccepted;
    public long OffersExpired => _offersExpired;
    public long OffersDeclined => _offersDeclined;
    public long TotalWaitMilliseconds => _totalWaitMilliseconds;
    public long WaitSamples => _waitSamples;
    public IReadOnlyList<Entry> Entries => _entries;

    public Entry? Find(HumanIdentityKey identity)
        => _entries.LastOrDefault(e => e.Identity == identity
            && e.State is (LobbyQueueEntryState.Queued or LobbyQueueEntryState.SeatOffered));

    public Entry? Find(Guid sessionId)
        => _entries.LastOrDefault(e => e.SessionId == sessionId
            && e.State is (LobbyQueueEntryState.Queued or LobbyQueueEntryState.SeatOffered));

    public Entry Enqueue(Guid sessionId, HumanIdentityKey identity, string displayName,
        LobbyQueueRequestedRole requestedRole, byte? requestedTeam, DateTimeOffset now)
    {
        if (sessionId == Guid.Empty) throw new ArgumentException("Session identity is required.", nameof(sessionId));
        if (identity.Value == Guid.Empty) throw new ArgumentException("Human identity is required.", nameof(identity));
        if (!Enum.IsDefined(requestedRole)
            || requestedTeam is >= MphRead.MatchRules.MaximumTeamCount)
            throw new ArgumentException("Invalid queue request.");
        if (Find(identity) is not null) throw new InvalidOperationException("Identity is already queued.");
        if (Count >= _maximumEntries) throw new InvalidOperationException("Waitlist capacity reached.");
        if (_nextSequence == long.MaxValue)
            throw new InvalidOperationException("Waitlist queue sequence exhausted.");
        _nextSequence++;
        var entry = new LobbyWaitlistEntry(sessionId, identity, displayName, requestedRole, requestedTeam, _nextSequence, now);
        _entries.Add(entry);
        return entry;
    }

    public IEnumerable<Entry> ActiveEntries()
        => _entries.Where(e => e.State is LobbyQueueEntryState.Queued or LobbyQueueEntryState.SeatOffered)
            .OrderByDescending(e => e.PriorityClass)
            .ThenBy(e => e.QueueSequence);

    public IEnumerable<Entry> QueuedEntries()
        => _entries.Where(e => e.State == LobbyQueueEntryState.Queued)
            .OrderByDescending(e => e.PriorityClass)
            .ThenBy(e => e.QueueSequence);

    public int Position(Entry entry)
    {
        int position = 0;
        foreach (Entry item in ActiveEntries())
        {
            position++;
            if (ReferenceEquals(item, entry)) return position;
        }
        return 0;
    }

    public void RecordAccepted(Entry entry, DateTimeOffset now)
    {
        entry.Transition(LobbyQueueEntryState.Promoted);
        _offersAccepted++;
        RecordWait(entry, now);
    }

    public void RecordExpired(Entry entry, DateTimeOffset now)
    {
        entry.Transition(LobbyQueueEntryState.Expired);
        _offersExpired++;
        RecordWait(entry, now);
    }

    public void RecordDeclined(Entry entry, DateTimeOffset now)
    {
        entry.Transition(LobbyQueueEntryState.Cancelled);
        _offersDeclined++;
        RecordWait(entry, now);
    }

    public void RecordCancelled(Entry entry, DateTimeOffset now)
    {
        entry.Transition(LobbyQueueEntryState.Cancelled);
        RecordWait(entry, now);
    }

    public void DeferOffers()
    {
        foreach (Entry entry in _entries.Where(e => e.State == LobbyQueueEntryState.SeatOffered)) entry.Requeue();
    }

    public int ExpireOffers(DateTimeOffset now)
    {
        int expired = 0;
        foreach (Entry entry in _entries.Where(e => e.State == LobbyQueueEntryState.SeatOffered && e.Reservation!.IsExpired(now)).ToArray())
        {
            RecordExpired(entry, now);
            expired++;
        }
        return expired;
    }

    public void EvictTerminalEntries()
    {
        int limit = _maximumEntries * 2;
        if (_entries.Count <= limit) return;
        int remove = _entries.Count - limit;
        _entries.RemoveAll(entry => remove > 0
            && entry.State is (LobbyQueueEntryState.Promoted or LobbyQueueEntryState.Expired or LobbyQueueEntryState.Cancelled)
            && remove-- > 0);
    }

    public LobbyWaitlistMetrics Metrics() => new(Count, _offersAccepted, _offersExpired, _offersDeclined,
        _totalWaitMilliseconds, _waitSamples);

    public LobbyWaitlistSnapshot Snapshot(HumanIdentityKey? self = null)
    {
        var active = ActiveEntries().ToArray();
        var summaries = active.Select((entry, index) => new LobbyQueueEntrySummary(index + 1, entry.DisplayName, entry.State, entry.QueueSequence)).ToImmutableArray();
        Entry? mine = self is { } identity ? Find(identity) : null;
        return new(Count, summaries, mine is not null, mine?.State, mine?.QueueSequence,
            mine?.Reservation is { } reservation ? new(reservation.OfferId, reservation.ExpiresAt, reservation.Policy) : null);
    }

    private void RecordWait(Entry entry, DateTimeOffset now)
    {
        TimeSpan waited = entry.WaitedAt(now);
        _totalWaitMilliseconds = checked(_totalWaitMilliseconds + Math.Max(0, (long)waited.TotalMilliseconds));
        _waitSamples++;
    }
}

public sealed record LobbyWaitlistMetrics(int Size, long OffersAccepted, long OffersExpired, long OffersDeclined,
    long TotalWaitMilliseconds, long WaitSamples)
{
    public double TotalWaitSeconds => TotalWaitMilliseconds / 1000d;
    public double AverageWaitSeconds => WaitSamples == 0 ? 0 : TotalWaitMilliseconds / 1000d / WaitSamples;
}
