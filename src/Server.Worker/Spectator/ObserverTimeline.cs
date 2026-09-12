using System;
using System.Collections.Generic;
using System.Threading;

namespace MphRead.Mods.Network;

public sealed record ObserverOptions(int MaxSpectators = 4, int DelaySeconds = 0)
{
    public void Validate()
    {
        if (MaxSpectators is < 0 or > 16 || DelaySeconds is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(MaxSpectators), "Observers must be 0..16 and delay 0..30 seconds.");
    }
}

internal readonly record struct ObserverEvent(ReliableEventType Type, byte[] Payload);

/// <summary>
/// A frame owns the packet arrays handed to it. Capture code never writes an
/// array after publishing the frame, which lets the replay writer consume it
/// asynchronously while the spectator ring evicts its own reference.
/// </summary>
internal sealed record ObserverFrame(uint Tick, uint MatchId, MatchRules Rules,
    byte[]? Snapshot, byte[][]? World, byte[]? Roster, uint RosterRevision,
    bool FreshSnapshot, bool FreshWorld, ObserverEvent[] Events)
{
    internal bool CaptureOverflowed { get; init; }
    public bool Complete => Snapshot != null && World != null && Roster != null;

    // Conservatively charge shared baseline references too. The real retained
    // allocation is lower; eviction never depends on collector behavior.
    public int Bytes
    {
        get
        {
            int size = 128 + (Snapshot?.Length ?? 0) + (Roster?.Length ?? 0);
            if (World != null)
                foreach (byte[] batch in World) size += batch.Length;
            foreach (ObserverEvent entry in Events) size += entry.Payload.Length + 16;
            return size;
        }
    }
}

/// <summary>Stable cursor into the bounded historical frame ring.</summary>
internal readonly record struct ObserverCursor(long Sequence)
{
    internal bool IsValid => Sequence > 0;
}

/// <summary>
/// Builds one immutable presentation frame per authoritative tick. This is
/// intentionally separate from <see cref="ObserverTimeline"/>: replay capture
/// must still receive the frame when spectator history is disabled or cannot
/// retain it.
/// </summary>
internal sealed class ObserverFrameBuilder
{
    private readonly List<ObserverEvent> _events = new();
    private readonly List<byte[]> _worldBatches = new();
    private byte[]? _snapshot;
    private byte[]? _roster;
    private byte[][]? _world;
    private uint _matchId;
    private uint _rosterRevision;
    private bool _freshSnapshot;
    private bool _eventOverflow;

    internal void BeginMatch(uint matchId)
    {
        if (_matchId == matchId) return;
        _matchId = matchId;
        _snapshot = null;
        _roster = null;
        _world = null;
        _events.Clear();
        _worldBatches.Clear();
        _freshSnapshot = false;
        _eventOverflow = false;
    }

    internal void Snapshot(ReadOnlySpan<byte> bytes)
    {
        // The source is normally a reusable simulation scratch packet. Take
        // ownership before the caller can reuse that storage.
        _snapshot = bytes.ToArray();
        _freshSnapshot = true;
    }

    internal void WorldBatch(ReadOnlySpan<byte> bytes)
        => _worldBatches.Add(bytes.ToArray());

    internal void Roster(ReadOnlySpan<byte> bytes, uint revision)
    {
        _roster = bytes.ToArray();
        _rosterRevision = revision;
    }

    internal void Event(ReliableEventType type, ReadOnlySpan<byte> bytes)
    {
        // A single authoritative tick has bounded journals. Preserve the
        // explicit capture-failure signal for required replay while keeping
        // the already-owned prefix safe for an observer failure path.
        if (_events.Count >= 2048) { _eventOverflow = true; return; }
        _events.Add(new(type, bytes.ToArray()));
    }

    internal ObserverFrame Produce(uint tick, uint matchId, MatchRules rules)
    {
        if (_matchId != matchId)
            throw new InvalidOperationException("Observer frame builder match was not initialized.");

        bool freshWorld = _worldBatches.Count != 0;
        if (freshWorld)
        {
            // The outer array is a newly-owned immutable envelope. Its inner
            // packet arrays were copied when captured and are never reused.
            _world = _worldBatches.ToArray();
            _worldBatches.Clear();
        }

        ObserverFrame frame = new(tick, matchId, rules, _snapshot, _world, _roster,
            _rosterRevision, _freshSnapshot, freshWorld, _events.ToArray())
        { CaptureOverflowed = _eventOverflow };
        _events.Clear();
        _freshSnapshot = false;
        _eventOverflow = false;
        return frame;
    }
}

/// <summary>
/// Bounded spectator history. The ring is addressed by monotonically
/// increasing sequence IDs rather than object-node references, so cursor
/// validity is deterministic across eviction, match transitions, and uint tick
/// wrap. Historical frames intentionally survive <see cref="BeginMatch"/> so a
/// delayed observer can receive an explicit match transition.
/// </summary>
internal sealed class ObserverTimeline
{
    internal static readonly SimDuration MinimumBaselineRetention = SimDuration.FromSeconds(3);
    internal static readonly SimDuration SafetyMargin = SimDuration.FromSeconds(2);
    internal static readonly SimDuration AbsoluteMaximumRetention = new(3601);
    internal const int MaxBytes = 64 * 1024 * 1024;

    private readonly ObserverFrame?[] _frames;
    private readonly long[] _sequences;
    private readonly int _retentionTicks;
    private readonly ObserverFrameBuilder _compatibilityBuilder = new();
    private long _firstSequence = 1;
    private long _nextSequence = 1;
    private int _count;
    private int _bytes;

    internal ObserverTimeline(ObserverOptions? options = null)
    {
        options ??= new();
        options.Validate();
        _retentionTicks = options.MaxSpectators == 0
            ? 0
            : checked((int)CalculateRetention(options.DelaySeconds).Ticks);
        // Keep a non-empty backing array for the disabled case so all cursor
        // helpers remain allocation-free and branch-safe.
        int capacity = Math.Max(1, _retentionTicks);
        _frames = new ObserverFrame?[capacity];
        _sequences = new long[capacity];
    }

    internal int RetentionTicks => _retentionTicks;
    // Heartbeats read these counters off-lane. The ring remains single-writer,
    // but volatile reads make the published scalar observations explicit.
    internal int Count => Volatile.Read(ref _count);
    internal int RetainedBytes => Volatile.Read(ref _bytes);

    internal static SimDuration CalculateRetention(int delaySeconds)
    {
        if (delaySeconds is < 0 or > 30)
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        SimDuration requested = SimDuration.FromSeconds(delaySeconds) + SafetyMargin;
        return requested.Ticks < MinimumBaselineRetention.Ticks
            ? MinimumBaselineRetention
            : new(Math.Min(AbsoluteMaximumRetention.Ticks, requested.Ticks));
    }

    /// <summary>
    /// Starts a new frame assembly while retaining prior history. The separate
    /// production path in ServerNetwork uses its own builder; this forwarding
    /// method keeps focused timeline tests and legacy callers equivalent.
    /// </summary>
    internal void BeginMatch(uint matchId) => _compatibilityBuilder.BeginMatch(matchId);

    internal void Snapshot(ReadOnlySpan<byte> bytes) => _compatibilityBuilder.Snapshot(bytes);
    internal void WorldBatch(ReadOnlySpan<byte> bytes) => _compatibilityBuilder.WorldBatch(bytes);
    internal void Roster(ReadOnlySpan<byte> bytes, uint revision) => _compatibilityBuilder.Roster(bytes, revision);
    internal void Event(ReliableEventType type, ReadOnlySpan<byte> bytes) => _compatibilityBuilder.Event(type, bytes);

    /// <summary>Compatibility helper that produces and retains one frame.</summary>
    internal ObserverFrame Commit(uint tick, uint matchId, MatchRules rules)
    {
        ObserverFrame frame = _compatibilityBuilder.Produce(tick, matchId, rules);
        Retain(frame, out _);
        return frame;
    }

    /// <summary>
    /// Retains a frame when spectator policy requires it. A frame larger than
    /// the hard byte cap is simply not retained; the direct replay sink still
    /// owns and receives the immutable frame.
    /// </summary>
    internal bool Retain(ObserverFrame frame, out ObserverCursor cursor)
    {
        cursor = default;
        if (_retentionTicks == 0 || frame.Bytes > MaxBytes) return false;

        while (_count >= _retentionTicks
            || _count > 0 && _bytes > MaxBytes - frame.Bytes)
            EvictOldest();

        if (_count >= _retentionTicks) return false;
        long sequence = _nextSequence++;
        int slot = (int)(sequence % _frames.Length);
        // The capacity and eviction checks above make this impossible unless
        // a sequence counter wraps after centuries of runtime.
        if (_sequences[slot] != 0) EvictSlot(slot);
        _frames[slot] = frame;
        _sequences[slot] = sequence;
        if (_count == 0) _firstSequence = sequence;
        Volatile.Write(ref _count, _count + 1);
        Volatile.Write(ref _bytes, _bytes + frame.Bytes);
        cursor = new ObserverCursor(sequence);
        return true;
    }

    /// <summary>Detaches all spectator cursors after a history production fault.</summary>
    internal void Clear()
    {
        for (int i = 0; i < _frames.Length; i++)
        {
            _frames[i] = null;
            _sequences[i] = 0;
        }
        _firstSequence = _nextSequence;
        Volatile.Write(ref _count, 0);
        Volatile.Write(ref _bytes, 0);
    }

    internal ObserverCursor? Baseline(SimTick now, SimDuration delay)
    {
        if (_count == 0) return null;
        for (long sequence = _nextSequence - 1; sequence >= _firstSequence; sequence--)
        {
            if (!TryGet(new ObserverCursor(sequence), out ObserverFrame? frame)) continue;
            if (Due(now, new(frame.Tick), delay) && frame.Complete)
                return new ObserverCursor(sequence);
        }
        return null;
    }

    internal bool TryGet(ObserverCursor cursor, out ObserverFrame frame)
    {
        if (!cursor.IsValid || _count == 0
            || cursor.Sequence < _firstSequence || cursor.Sequence >= _nextSequence)
        {
            frame = null!;
            return false;
        }
        int slot = (int)(cursor.Sequence % _frames.Length);
        if (_sequences[slot] != cursor.Sequence || _frames[slot] is not { } retained)
        {
            frame = null!;
            return false;
        }
        frame = retained;
        return true;
    }

    internal bool TryGetNext(ObserverCursor cursor, out ObserverCursor nextCursor, out ObserverFrame frame)
    {
        long next = cursor.Sequence + 1;
        nextCursor = new ObserverCursor(next);
        if (TryGet(nextCursor, out frame)) return true;
        frame = null!;
        return false;
    }

    internal bool Owns(ObserverCursor cursor) => TryGet(cursor, out _);

    internal static bool Due(SimTick now, SimTick tick, SimDuration delay)
        => now.HasElapsedSince(tick, delay);

    private void EvictOldest()
    {
        if (_count == 0) return;
        int slot = (int)(_firstSequence % _frames.Length);
        EvictSlot(slot);
        _firstSequence++;
    }

    private void EvictSlot(int slot)
    {
        ObserverFrame? frame = _frames[slot];
        if (frame != null)
        {
            Volatile.Write(ref _bytes, _bytes - frame.Bytes);
            _frames[slot] = null; // release retained payload references eagerly
            Volatile.Write(ref _count, _count - 1);
        }
        _sequences[slot] = 0;
    }
}
