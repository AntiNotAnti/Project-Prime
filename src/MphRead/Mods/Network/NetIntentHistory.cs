using System;

namespace MphRead.Mods.Network;

/// <summary>Local redundancy history is larger than any one UDP carrier. New
/// shots go first; a rotating six-event budget repeats older shots fairly.
/// Neither overflow nor expiry is allowed to overwrite an unreported entry.</summary>
internal sealed class NetOutgoingFireHistory
{
    internal const int Capacity = 128, RepeatBudget = 6;
    private readonly FireEvent[] _events = new FireEvent[Capacity];
    private readonly bool[] _sent = new bool[Capacity];
    private int _count, _cursor;
    internal long Overflows { get; private set; }
    internal long UnsentExpired { get; private set; }
    internal int Count => _count;
    internal void Clear() { _count = _cursor = 0; }
    private void Prune(uint frame)
    {
        int count = 0;
        for (int i = 0; i < _count; i++)
        {
            if (unchecked(frame - _events[i].SourceFrame) > NetFireEvents.RetentionFrames)
            { if (!_sent[i]) UnsentExpired++; continue; }
            _events[count] = _events[i]; _sent[count++] = _sent[i];
        }
        // Keep the rotation tied to shot identity when the prefix expires.
        _cursor = Math.Max(0, _cursor - (_count - count));
        _count = count;
        if (_cursor >= _count) _cursor = 0;
    }
    internal bool CanAdd(uint frame)
    {
        Prune(frame);
        if (_count < Capacity) return true;
        Overflows++; return false;
    }
    internal bool Add(in FireEvent fire)
    {
        Prune(fire.SourceFrame);
        if (_count == Capacity) { Overflows++; return false; }
        _events[_count] = fire; _sent[_count++] = false;
        return true;
    }
    internal void Fill(ref IntentPacket intent)
    {
        Prune(intent.Frame);
        intent.HasFireEvents = true; intent.FireEventCount = 0; intent.FireEvents = default;
        Span<int> selected = stackalloc int[NetFireEvents.LiveCapacity];
        int count = 0;
        for (int i = 0; i < _count && count < selected.Length; i++)
            if (!_sent[i]) selected[count++] = i;
        int scans = 0;
        int repeatBudget = Math.Min(NetFireEvents.LiveCapacity, Math.Max(RepeatBudget, (_count + 5) / 6));
        while (count < repeatBudget && scans++ < _count)
        {
            int index = _cursor; _cursor = (_cursor + 1) % _count;
            bool duplicate = false;
            for (int i = 0; i < count; i++) duplicate |= selected[i] == index;
            if (!duplicate) selected[count++] = index;
        }
        // Wire history is always serial-number ordered, even across wrap.
        for (int i = 1; i < count; i++)
        {
            int value = selected[i], j = i;
            while (j > 0 && NetLifecycleTracker.Newer(_events[selected[j - 1]].ShotId, _events[value].ShotId))
            { selected[j] = selected[j - 1]; j--; }
            selected[j] = value;
        }
        intent.FireEventCount = (byte)count;
        for (int i = 0; i < count; i++)
        { intent.FireEvents[i] = _events[selected[i]]; _sent[selected[i]] = true; }
    }
}

/// <summary>Bounded, allocation-free union of carrier events, independent of
/// movement ordering. Consumed identities remain in a serial-number window,
/// so a delayed older shot can be recovered without replaying a newer shot.</summary>
internal sealed class NetIncomingFireHistory
{
    internal const int Capacity = 128, RecoveryFrames = 64;
    private struct Entry { public FireEvent Fire; public bool Present, Consumed; }
    private readonly Entry[] _entries = new Entry[Capacity];
    private bool _seen;
    private uint _latestFrame, _latestId;
    internal int Pending { get; private set; }
    internal long Overflows { get; private set; }
    internal long Conflicts { get; private set; }
    internal long StaleCarriers { get; private set; }
    internal long ExpiredPending { get; private set; }
    internal Action<FireEvent>? OnExpired { get; set; }
    internal void Clear()
    { Array.Clear(_entries); Pending = 0; _seen = false; _latestFrame = _latestId = 0; }
    internal bool Accept(in IntentPacket intent)
    {
        if (!intent.HasFireEvents) return true;
        if (!NetFireEvents.Validate(intent)) return false;
        uint frame = !_seen || NetLifecycleTracker.Newer(intent.Frame, _latestFrame) ? intent.Frame : _latestFrame;
        uint newest = _latestId;
        if (intent.FireEventCount > 0 && (!_seen || newest == 0
            || NetLifecycleTracker.Newer(intent.FireEvents[intent.FireEventCount - 1].ShotId, newest)))
            newest = intent.FireEvents[intent.FireEventCount - 1].ShotId;
        // Validate the whole batch before modifying retained events. A bad
        // duplicate or a full bucket cannot partially admit a carrier.
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var fire = intent.FireEvents[i];
            ref var entry = ref _entries[fire.ShotId % Capacity];
            if (unchecked(frame - fire.SourceFrame) > RecoveryFrames
                || unchecked(newest - fire.ShotId) >= Capacity)
            { StaleCarriers++; return false; }
            if (entry.Present && entry.Fire.ShotId == fire.ShotId)
            { if (entry.Fire != fire) { Conflicts++; return false; } }
            else if (entry.Present && !entry.Consumed
                && unchecked(frame - entry.Fire.SourceFrame) <= RecoveryFrames)
            { Overflows++; return false; }
            // A wrap can map two distinct IDs in the same incoming batch to
            // the same ring cell; reject instead of overwriting either one.
            for (int j = 0; j < i; j++)
                if (intent.FireEvents[j].ShotId % Capacity == fire.ShotId % Capacity)
                { Overflows++; return false; }
        }
        _seen = true; _latestFrame = frame; _latestId = newest;
        for (int i = 0; Pending > 0 && i < Capacity; i++)
            if (_entries[i].Present && !_entries[i].Consumed
                && unchecked(frame - _entries[i].Fire.SourceFrame) > RecoveryFrames)
            { OnExpired?.Invoke(_entries[i].Fire); _entries[i].Consumed = true; Pending--; ExpiredPending++; }
        for (int i = 0; i < intent.FireEventCount; i++)
        {
            var fire = intent.FireEvents[i];
            ref var entry = ref _entries[fire.ShotId % Capacity];
            if (entry.Present && entry.Fire.ShotId == fire.ShotId) continue;
            entry = new() { Fire = fire, Present = true }; Pending++;
        }
        return true;
    }
    internal bool TryPeek(out FireEvent fire)
    {
        fire = default;
        if (Pending == 0) return false;
        bool found = false;
        for (int i = 0; i < Capacity; i++)
        {
            ref var entry = ref _entries[i];
            if (entry.Present && !entry.Consumed
                && (!found || NetLifecycleTracker.Newer(fire.ShotId, entry.Fire.ShotId)))
            { fire = entry.Fire; found = true; }
        }
        return found;
    }
    internal void Consume(uint id)
    {
        ref var entry = ref _entries[id % Capacity];
        if (entry.Present && entry.Fire.ShotId == id && !entry.Consumed)
        { entry.Consumed = true; Pending--; }
    }
}
