using System;

namespace MphRead.Mods.Network;

internal readonly record struct LobbyGroupEntry(ulong QueueId, int Position, int QueueLength,
    ushort Slots, double ExpiresAt, bool Activated);

// Group seats are entries in the existing FIFO and use its one reservation mask.
internal sealed partial class LobbyWaitlist
{
    private sealed class GroupHold
    {
        internal string Request = "";
        internal int Count;
        internal ushort Slots;
        internal double ExpiresAt;
        internal bool Activated;
    }

    internal bool TryJoinGroup(LobbyQueueConnection owner, string request, int count,
        uint match, ulong epoch, double deadline, double now, out LobbyGroupEntry state)
    {
        Advance(now); state = default;
        if (!owner.IsValid || !PartyReservationWire.Id(request) || count is < 1 or > 8
            || match != _matchId || epoch != _epoch || !_allowAdmission || _entries.Count >= _capacity
            || !double.IsFinite(deadline) || deadline <= now || deadline > now + 60
            || _entries.Exists(e => e.Owner == owner || e.Group?.Request == request)) return false;
        var entry = new Entry { Owner = owner, QueueId = Next(ref _nextQueueId), JoinedAt = now,
            Group = new() { Request = request, Count = count, ExpiresAt = Math.Min(deadline, now + 20) } };
        _entries.Add(entry);
        _metrics = _metrics with { Joined = Increment(_metrics.Joined),
            MaximumQueueLength = Math.Max(_metrics.MaximumQueueLength, _entries.Count) };
        OfferAvailable(); state = GroupSnapshot(entry); return true;
    }
    private LobbyGroupEntry GroupSnapshot(Entry entry)
        => new(entry.QueueId, _entries.IndexOf(entry) + 1, _entries.Count,
            entry.Group!.Slots, entry.Group.ExpiresAt, entry.Group.Activated);
    internal bool TryGetGroup(ulong queueId, out LobbyGroupEntry state)
    {
        CheckOwner(); Entry? entry = _entries.Find(e => e.QueueId == queueId && e.Group != null);
        state = entry == null ? default : GroupSnapshot(entry); return entry != null;
    }
    internal bool ActivateGroup(ulong queueId, uint match, ulong epoch, double deadline, double now)
    {
        Advance(now); Entry? entry = _entries.Find(e => e.QueueId == queueId && e.Group != null);
        if (entry?.Group is not { Slots: > 0 } group || match != _matchId || epoch != _epoch
            || !_allowAdmission || deadline <= now || !double.IsFinite(deadline)) return false;
        group.ExpiresAt = Math.Min(group.ExpiresAt, deadline);
        group.Activated = true; return true;
    }
    internal bool CancelGroup(ulong queueId, double now)
    {
        Advance(now); int removed = _entries.RemoveAll(e => e.QueueId == queueId && e.Group != null);
        if (removed != 0) OfferAvailable(); return removed != 0;
    }
    internal bool ReleaseGroupSlot(ulong queueId, int slot, double now)
    {
        Advance(now); Entry? entry = _entries.Find(e => e.QueueId == queueId && e.Group != null);
        if (entry?.Group is not { } group || slot is < 0 or > 7 || (group.Slots & (1 << slot)) == 0) return false;
        group.Slots &= (ushort)~(1 << slot);
        --group.Count;
        if (group.Count == 0) _entries.Remove(entry);
        OfferAvailable(); return true;
    }
    internal bool TryAcceptGroup(ulong queueId, int slot, uint match, ulong epoch, double now, Func<int,bool> admit)
    {
        Advance(now); Entry? entry = _entries.Find(e => e.QueueId == queueId && e.Group != null);
        if (entry?.Group is not { Activated: true } group || !_allowAdmission || match != _matchId || epoch != _epoch
            || slot is < 0 or > 7 || (group.Slots & (1 << slot)) == 0 || (_occupied & (1 << slot)) != 0) return false;
        bool accepted;
        _inAdmission = true;
        try { accepted = admit(slot); } finally { _inAdmission = false; }
        if (!accepted) return false;
        _occupied |= (ushort)(1 << slot);
        group.Slots &= (ushort)~(1 << slot);
        if (--group.Count == 0) _entries.Remove(entry);
        _metrics = _metrics with { Accepted = Increment(_metrics.Accepted) };
        OfferAvailable(); return true;
    }
}
