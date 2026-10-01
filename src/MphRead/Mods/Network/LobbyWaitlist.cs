using System;
using System.Collections.Generic;
using System.Net;

namespace MphRead.Mods.Network;

/// <summary>Established transport identity supplied by the server, never a client claim.
/// Endpoint and connection incarnation both fence queue operations. Queue/offer IDs
/// and client nonces are correlation values, not authentication credentials.</summary>
internal readonly record struct LobbyQueueConnection(string Address, int Port, ulong ConnectionId)
{
    internal static LobbyQueueConnection FromEstablished(IPEndPoint endpoint, ulong connectionId)
        => new(endpoint.Address.ToString(), endpoint.Port, connectionId);
    internal bool IsValid => !string.IsNullOrEmpty(Address) && Port > 0 && Port <= ushort.MaxValue && ConnectionId != 0;
}

internal enum LobbyWaitlistState : byte { Waiting, NextMatch, Offered, Disconnected }
internal readonly record struct LobbySeatOffer(ulong OfferId, uint MatchId, ulong AuthorityEpoch, byte Slot, double ExpiresAt);
internal readonly record struct LobbyWaitlistEntry(ulong QueueId, int Position, int QueueLength,
    LobbyWaitlistState State, LobbySeatOffer? Offer);
internal readonly record struct LobbyWaitlistMetrics(ulong Joined, ulong Left, ulong OffersCreated,
    ulong Accepted, ulong Declined, ulong Expired, ulong DisconnectedExpired, double MeanWaitSeconds, int MaximumQueueLength);

/// <summary>Bounded FIFO policy and capacity reservations. All calls belong to one
/// lobby control thread. DedicatedServer supplies established queue-only transport
/// identities and performs ordinary admission after reserving an available seat.</summary>
internal sealed class LobbyWaitlist
{
    internal const int DefaultCapacity = 64, HardMaximumCapacity = 256;
    private sealed class Entry
    {
        internal LobbyQueueConnection Owner;
        internal ulong QueueId;
        internal double JoinedAt;
        internal double? DisconnectedAt;
        internal LobbySeatOffer? Offer;
    }
    private readonly List<Entry> _entries = new();
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly int _capacity;
    private readonly double _offerLifetime, _resumeGrace;
    private double _now;
    private ulong _nextQueueId, _nextOfferId;
    private bool _allowAdmission, _inAdmission;
    private int _playerCapacity;
    private ushort _occupied;
    private uint _matchId;
    private ulong _epoch;
    private LobbyWaitlistMetrics _metrics;

    internal LobbyWaitlist(int capacity = DefaultCapacity, double offerLifetimeSeconds = 15, double resumeGraceSeconds = 10)
    {
        if (capacity < 1 || capacity > HardMaximumCapacity) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!double.IsFinite(offerLifetimeSeconds) || offerLifetimeSeconds <= 0 || offerLifetimeSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(offerLifetimeSeconds));
        if (!double.IsFinite(resumeGraceSeconds) || resumeGraceSeconds <= 0 || resumeGraceSeconds > 300)
            throw new ArgumentOutOfRangeException(nameof(resumeGraceSeconds));
        _capacity = capacity; _offerLifetime = offerLifetimeSeconds; _resumeGrace = resumeGraceSeconds;
    }

    private void CheckOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread || _inAdmission)
            throw new InvalidOperationException("Waitlist operations must run on the lobby owner and cannot reenter admission.");
    }
    private void Advance(double now)
    {
        CheckOwner();
        if (!double.IsFinite(now) || now < _now || now > double.MaxValue - 300)
            throw new ArgumentOutOfRangeException(nameof(now), "Use finite monotonic server time.");
        _now = now;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var entry = _entries[i];
            if (entry.DisconnectedAt is double disconnected && now - disconnected >= _resumeGrace)
            {
                _entries.RemoveAt(i);
                _metrics = _metrics with { DisconnectedExpired = Increment(_metrics.DisconnectedExpired) };
            }
            else if (entry.Offer is LobbySeatOffer offer && now >= offer.ExpiresAt)
            {
                _entries.RemoveAt(i);
                _metrics = _metrics with { Expired = Increment(_metrics.Expired) };
            }
        }
    }
    private static ulong Increment(ulong value) => value == ulong.MaxValue ? value : value + 1;
    private static ulong Next(ref ulong value)
    {
        if (value == ulong.MaxValue) throw new InvalidOperationException("Waitlist identifier space exhausted.");
        return ++value;
    }
    private Entry? Find(LobbyQueueConnection owner, ulong queueId)
        => _entries.Find(entry => entry.Owner == owner && entry.QueueId == queueId);

    /// <summary>Publish authoritative occupancy (humans AND bots), lifecycle and JIP
    /// admission policy, then expire reservations and offer currently free seats.</summary>
    internal void Update(double now, int playerCapacity, ushort occupiedSlots, bool allowAdmission, uint matchId, ulong authorityEpoch)
    {
        CheckOwner();
        if (playerCapacity < 1 || playerCapacity > 8 || (occupiedSlots >> playerCapacity) != 0
            || matchId == 0 || authorityEpoch == 0) throw new ArgumentException("Invalid authoritative admission state.");
        Advance(now);
        bool changedEpoch = _matchId != matchId || _epoch != authorityEpoch;
        _playerCapacity = playerCapacity; _occupied = occupiedSlots; _allowAdmission = allowAdmission;
        _matchId = matchId; _epoch = authorityEpoch;
        foreach (var entry in _entries)
            if (entry.Offer is LobbySeatOffer offer && (changedEpoch || !allowAdmission
                || offer.Slot >= playerCapacity || (occupiedSlots & (1 << offer.Slot)) != 0)) entry.Offer = null;
        OfferAvailable();
    }

    private void OfferAvailable()
    {
        if (!_allowAdmission) return;
        ushort unavailable = (ushort)(_occupied | ReservedSlotsCore());
        foreach (var entry in _entries)
        {
            if (entry.Offer != null) continue;
            // Retained disconnected entries keep FIFO precedence for their bounded grace.
            if (entry.DisconnectedAt != null) break;
            int slot = 0;
            while (slot < _playerCapacity && (unavailable & (1 << slot)) != 0) slot++;
            if (slot == _playerCapacity) break;
            entry.Offer = new(Next(ref _nextOfferId), _matchId, _epoch, (byte)slot, _now + _offerLifetime);
            unavailable |= (ushort)(1 << slot);
            _metrics = _metrics with { OffersCreated = Increment(_metrics.OffersCreated) };
        }
    }
    private ushort ReservedSlotsCore()
    {
        ushort result = 0;
        foreach (var entry in _entries) if (entry.Offer is LobbySeatOffer offer) result |= (ushort)(1 << offer.Slot);
        return result;
    }
    internal ushort ReservedSlots { get { CheckOwner(); return ReservedSlotsCore(); } }
    internal int Count { get { CheckOwner(); return _entries.Count; } }
    internal LobbyWaitlistMetrics Metrics { get { CheckOwner(); return _metrics; } }
    internal bool CanDirectJoin(int slot)
    {
        CheckOwner();
        return _allowAdmission && slot >= 0 && slot < _playerCapacity
            && !_entries.Exists(entry => entry.Offer == null)
            && ((_occupied | ReservedSlotsCore()) & (1 << slot)) == 0;
    }

    internal bool TryJoin(LobbyQueueConnection owner, ulong clientNonce, double now, out LobbyWaitlistEntry state)
    {
        Advance(now); state = default;
        if (!owner.IsValid || clientNonce == 0 || _entries.Count >= _capacity
            || _entries.Exists(entry => entry.Owner == owner || entry.Owner.ConnectionId == owner.ConnectionId
                || entry.Owner.Address == owner.Address && entry.Owner.Port == owner.Port)) return false;
        _entries.Add(new Entry { Owner = owner, QueueId = Next(ref _nextQueueId), JoinedAt = now });
        _metrics = _metrics with { Joined = Increment(_metrics.Joined), MaximumQueueLength = Math.Max(_metrics.MaximumQueueLength, _entries.Count) };
        OfferAvailable();
        state = Snapshot(_entries[^1]);
        return true;
    }
    private LobbyWaitlistEntry Snapshot(Entry entry)
        => new(entry.QueueId, _entries.IndexOf(entry) + 1, _entries.Count,
            entry.DisconnectedAt != null ? LobbyWaitlistState.Disconnected : entry.Offer != null ? LobbyWaitlistState.Offered
            : _allowAdmission ? LobbyWaitlistState.Waiting : LobbyWaitlistState.NextMatch, entry.Offer);
    internal bool TryGetState(LobbyQueueConnection owner, ulong queueId, out LobbyWaitlistEntry state)
    {
        CheckOwner(); var entry = Find(owner, queueId);
        state = entry == null ? default : Snapshot(entry);
        return entry != null;
    }
    internal bool Leave(LobbyQueueConnection owner, ulong queueId, double now)
    {
        Advance(now); var entry = Find(owner, queueId);
        if (entry == null) return false;
        _entries.Remove(entry); _metrics = _metrics with { Left = Increment(_metrics.Left) }; OfferAvailable(); return true;
    }
    internal bool Disconnect(LobbyQueueConnection owner, ulong queueId, double now)
    {
        Advance(now); var entry = Find(owner, queueId);
        if (entry == null) return false;
        entry.DisconnectedAt ??= now; return true;
    }
    /// <summary>Resume the SAME established transport incarnation only. Rebinding to
    /// a new endpoint/connection requires a future authenticated transport contract.</summary>
    internal bool Resume(LobbyQueueConnection owner, ulong queueId, double now)
    {
        Advance(now); var entry = Find(owner, queueId);
        if (entry == null || entry.DisconnectedAt == null) return false;
        entry.DisconnectedAt = null; OfferAvailable(); return true;
    }
    private Entry? MatchOffer(LobbyQueueConnection owner, ulong queueId, ulong offerId, uint matchId, ulong epoch)
    {
        var entry = Find(owner, queueId);
        return _allowAdmission && entry?.DisconnectedAt == null && entry?.Offer is LobbySeatOffer offer
            && offer.OfferId == offerId && offer.MatchId == matchId && offer.AuthorityEpoch == epoch
            && offer.ExpiresAt > _now && (_occupied & (1 << offer.Slot)) == 0 ? entry : null;
    }
    internal bool Decline(LobbyQueueConnection owner, ulong queueId, ulong offerId, uint matchId, ulong epoch, double now)
    {
        Advance(now); var entry = MatchOffer(owner, queueId, offerId, matchId, epoch);
        if (entry == null) return false;
        _entries.Remove(entry); _metrics = _metrics with { Declined = Increment(_metrics.Declined) }; OfferAvailable(); return true;
    }
    /// <summary>The callback must perform normal server admission validation AND
    /// atomically occupy the reserved seat before returning true. A false return or
    /// exception retains the reservation. It cannot reenter this queue.</summary>
    internal bool TryAccept(LobbyQueueConnection owner, ulong queueId, ulong offerId, uint matchId, ulong epoch,
        double now, Func<int, bool> admit)
    {
        if (admit == null) throw new ArgumentNullException(nameof(admit));
        Advance(now); var entry = MatchOffer(owner, queueId, offerId, matchId, epoch);
        if (entry == null) return false;
        int slot = entry.Offer!.Value.Slot;
        bool accepted;
        _inAdmission = true;
        try { accepted = admit(slot); }
        finally { _inAdmission = false; }
        if (!accepted) return false;
        _occupied |= (ushort)(1 << slot);
        _entries.Remove(entry);
        ulong count = Increment(_metrics.Accepted);
        _metrics = _metrics with { Accepted = count,
            MeanWaitSeconds = _metrics.MeanWaitSeconds + (now - entry.JoinedAt - _metrics.MeanWaitSeconds) / count };
        OfferAvailable(); return true;
    }
    internal void Clear()
    {
        CheckOwner(); _entries.Clear(); _occupied = 0; _allowAdmission = false;
        // Never reuse queue or offer IDs while this owner lives.
    }
}
