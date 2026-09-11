using System;
using System.Collections.Generic;

namespace MphRead.Mods.Network;

internal readonly record struct NodeHealthObservation(
    TimeSpan? LastSuccessfulRtt,
    DateTimeOffset? LastSuccessfulAt,
    DateTimeOffset? LastFailedConnection,
    int FailureCount);

/// <summary>
/// Process-local observations used as a bounded health hint for automatic
/// Node selection. The Backend directory and Node admission handshake remain
/// authoritative; a recent failure is a penalty, never a ban.
/// </summary>
internal sealed class NodeHealthCache
{
    internal const int MaximumEntries = 256;
    internal const int MaximumFailureCount = 8;
    internal static readonly TimeSpan ObservationDuration = TimeSpan.FromSeconds(60);
    internal static NodeHealthCache Shared { get; } = new();

    private readonly object _gate = new();
    private readonly int _maximumEntries;
    private readonly TimeSpan _observationDuration;
    private readonly Dictionary<Guid, Entry> _entries = new();
    private long _sequence;

    internal NodeHealthCache(int maximumEntries = MaximumEntries,
        TimeSpan? observationDuration = null)
    {
        if (maximumEntries is < 1 or > MaximumEntries)
            throw new ArgumentOutOfRangeException(nameof(maximumEntries));
        if (observationDuration is not null && observationDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(observationDuration));
        _maximumEntries = maximumEntries;
        _observationDuration = observationDuration ?? ObservationDuration;
    }

    internal int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    internal bool IsUnhealthy(Guid nodeId, DateTimeOffset now)
    {
        if (nodeId == Guid.Empty) return false;
        lock (_gate)
        {
            PruneExpired(now);
            return _entries.TryGetValue(nodeId, out Entry entry)
                && entry.HasRecentFailure(now, _observationDuration);
        }
    }

    internal bool TryGetRecentRtt(Guid nodeId, DateTimeOffset now, out TimeSpan rtt)
    {
        if (nodeId == Guid.Empty)
        {
            rtt = default;
            return false;
        }
        lock (_gate)
        {
            PruneExpired(now);
            if (_entries.TryGetValue(nodeId, out Entry entry)
                && entry.LastSuccessfulRtt is { } measured
                && entry.LastSuccessfulAt is { } at
                && now >= at && now - at < _observationDuration)
            {
                rtt = measured;
                return true;
            }
        }
        rtt = default;
        return false;
    }

    internal bool TryGetObservation(Guid nodeId, DateTimeOffset now,
        out NodeHealthObservation observation)
    {
        if (nodeId == Guid.Empty)
        {
            observation = default;
            return false;
        }
        lock (_gate)
        {
            PruneExpired(now);
            if (_entries.TryGetValue(nodeId, out Entry entry))
            {
                observation = new(entry.LastSuccessfulRtt, entry.LastSuccessfulAt,
                    entry.LastFailedAt, entry.FailureCount);
                return true;
            }
        }
        observation = default;
        return false;
    }

    internal void RecordTransientFailure(Guid nodeId, DateTimeOffset now)
    {
        if (nodeId == Guid.Empty) return;
        lock (_gate)
        {
            PruneExpired(now);
            int failureCount = 1;
            if (_entries.TryGetValue(nodeId, out Entry previous)
                && previous.HasRecentFailure(now, _observationDuration))
            {
                failureCount = Math.Min(MaximumFailureCount, previous.FailureCount + 1);
            }
            _entries[nodeId] = previous with
            {
                LastFailedAt = now,
                FailureCount = failureCount,
                Sequence = ++_sequence
            };
            TrimToBound();
        }
    }

    internal void RecordSuccess(Guid nodeId, DateTimeOffset now,
        TimeSpan? roundTripTime = null)
    {
        if (nodeId == Guid.Empty) return;
        lock (_gate)
        {
            PruneExpired(now);
            Entry previous = _entries.TryGetValue(nodeId, out Entry existing)
                ? existing : default;
            _entries[nodeId] = previous with
            {
                LastSuccessfulAt = now,
                LastSuccessfulRtt = roundTripTime ?? previous.LastSuccessfulRtt,
                Sequence = ++_sequence
            };
            TrimToBound();
        }
    }

    private void PruneExpired(DateTimeOffset now)
    {
        if (_entries.Count == 0) return;
        List<Guid>? expired = null;
        foreach (KeyValuePair<Guid, Entry> pair in _entries)
        {
            if (pair.Value.IsExpired(now, _observationDuration))
                (expired ??= new List<Guid>()).Add(pair.Key);
        }
        if (expired is null) return;
        foreach (Guid nodeId in expired) _entries.Remove(nodeId);
    }

    private void TrimToBound()
    {
        while (_entries.Count > _maximumEntries)
        {
            Guid evict = Guid.Empty;
            Entry oldest = default;
            bool found = false;
            foreach (KeyValuePair<Guid, Entry> pair in _entries)
            {
                if (!found || pair.Value.Sequence < oldest.Sequence
                    || pair.Value.Sequence == oldest.Sequence
                        && pair.Key.CompareTo(evict) < 0)
                {
                    evict = pair.Key;
                    oldest = pair.Value;
                    found = true;
                }
            }
            if (!found) return;
            _entries.Remove(evict);
        }
    }

    private readonly record struct Entry(
        DateTimeOffset? LastSuccessfulAt = null,
        TimeSpan? LastSuccessfulRtt = null,
        DateTimeOffset? LastFailedAt = null,
        int FailureCount = 0,
        long Sequence = 0)
    {
        internal bool HasRecentFailure(DateTimeOffset now, TimeSpan duration)
            => LastFailedAt is { } failed
                && (LastSuccessfulAt is not { } succeeded || failed > succeeded)
                && now >= failed && now - failed < duration;

        internal bool IsExpired(DateTimeOffset now, TimeSpan duration)
            => (LastSuccessfulAt is not { } succeeded || now - succeeded >= duration)
                && (LastFailedAt is not { } failed || now - failed >= duration);
    }
}
