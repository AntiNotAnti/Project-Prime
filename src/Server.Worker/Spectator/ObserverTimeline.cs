using System;
using System.Collections.Generic;
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
internal sealed record ObserverFrame(uint Tick, uint MatchId, MatchRules Rules,
    byte[]? Snapshot, byte[][]? World, byte[]? Roster, uint RosterRevision,
    bool FreshSnapshot, bool FreshWorld, ObserverEvent[] Events)
{
    public bool Complete => Snapshot != null && World != null && Roster != null;
    // Conservatively charge shared baseline references too. The real retained
    // allocation is lower; eviction never depends on collector behavior.
    public int Bytes
    {
        get
        {
            int size = 128 + (Snapshot?.Length ?? 0) + (Roster?.Length ?? 0);
            if (World != null) foreach (byte[] batch in World) size += batch.Length;
            foreach (ObserverEvent entry in Events) size += entry.Payload.Length + 16;
            return size;
        }
    }
}
/// <summary>Shared, immutable historical state. No observer gets a live fallback.
/// At most60 seconds/3601 frames and64MiB; lagging cursors fail explicitly.</summary>
internal sealed class ObserverTimeline
{
    internal const int MaxBytes = 64 * 1024 * 1024;
    private readonly LinkedList<ObserverFrame> _frames = new();
    private readonly List<ObserverEvent> _events = new();
    private readonly List<byte[]> _worldBatches = new();
    private byte[]? _snapshot, _roster;
    private byte[][]? _world;
    private uint _matchId, _rosterRevision;
    private bool _freshSnapshot;
    private int _bytes;
    private bool _overflow;
    internal int Count => _frames.Count;
    internal int RetainedBytes => _bytes;
    internal void BeginMatch(uint matchId)
    {
        if (_matchId == matchId) return;
        _matchId = matchId; _snapshot = _roster = null; _world = null;
        _events.Clear(); _worldBatches.Clear(); _freshSnapshot = false; _overflow = false;
    }
    internal void Snapshot(ReadOnlySpan<byte> bytes) { _snapshot = bytes.ToArray(); _freshSnapshot = true; }
    internal void WorldBatch(ReadOnlySpan<byte> bytes) => _worldBatches.Add(bytes.ToArray());
    internal void Roster(ReadOnlySpan<byte> bytes, uint revision) { _roster = bytes.ToArray(); _rosterRevision = revision; }
    internal void Event(ReliableEventType type, ReadOnlySpan<byte> bytes)
    {
        // A single authoritative tick has bounded journals; refuse excess instead
        // of silently removing cues from a delayed competitive stream.
        if (_events.Count >= 2048) { _overflow = true; return; }
        _events.Add(new(type, bytes.ToArray()));
    }
    internal ObserverFrame? Commit(uint tick, uint matchId, MatchRules rules)
    {
        if (_matchId != matchId) throw new InvalidOperationException("Observer timeline match was not initialized.");
        if (_overflow)
        {
            // Detach all cursors: overloaded observers close, gameplay continues.
            _frames.Clear(); _bytes = 0; _events.Clear(); _worldBatches.Clear();
            _snapshot = null; _world = null; _overflow = false; _freshSnapshot = false;
            return null;
        }
        bool freshWorld = _worldBatches.Count != 0;
        if (freshWorld) { _world = _worldBatches.ToArray(); _worldBatches.Clear(); }
        var frame = new ObserverFrame(tick, matchId, rules, _snapshot, _world, _roster, _rosterRevision,
            _freshSnapshot, freshWorld, _events.ToArray());
        _frames.AddLast(frame); _bytes += frame.Bytes;
        _events.Clear(); _freshSnapshot = false;
        while (_frames.First != null && (_frames.Count > 3601 || _bytes > MaxBytes
            || unchecked(tick - _frames.First.Value.Tick) > 3600))
        { _bytes -= _frames.First.Value.Bytes; _frames.RemoveFirst(); }
        return frame;
    }
    internal LinkedListNode<ObserverFrame>? Baseline(uint now, uint delayTicks)
    {
        for (var node = _frames.Last; node != null; node = node.Previous)
        {
            uint age = unchecked(now - node.Value.Tick);
            if (age <= Int32.MaxValue && age >= delayTicks && node.Value.Complete) return node;
        }
        return null;
    }
    internal bool Owns(LinkedListNode<ObserverFrame> node) => node.List == _frames;
    internal static bool Due(uint now, uint tick, uint delayTicks)
    { uint age = unchecked(now - tick); return age <= Int32.MaxValue && age >= delayTicks; }
}
