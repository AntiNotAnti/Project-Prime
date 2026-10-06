using System;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Replay;

/// <summary>Identity deduplication for the retained presentation window. A seek
/// discards the cursor and reconstructs the window; no whole-match keys survive.</summary>
internal sealed class ReplayHistorySet<T> where T : notnull
{
    private readonly Dictionary<T, uint> _frames = new();
    private readonly Queue<(T Key, uint Frame)> _order = new();
    private readonly uint _history;
    private readonly int _capacity;
    internal int Count => _frames.Count;
    internal ReplayHistorySet(uint history, int capacity) { _history = history; _capacity = capacity; }
    internal bool Contains(T key) => _frames.ContainsKey(key);
    internal bool Add(T key, uint frame)
    {
        if (_frames.ContainsKey(key)) return false;
        if (_frames.Count >= _capacity) throw new InvalidDataException("Replay presentation identities exceed their retained-window budget.");
        _frames.Add(key, frame); _order.Enqueue((key, frame));
        return true;
    }
    internal void Prune(uint frame)
    {
        // Source records arrive in recording order, including future lookahead.
        while (_order.TryPeek(out var item) && (ulong)item.Frame + _history < frame)
        { _order.Dequeue(); _frames.Remove(item.Key); }
    }
    internal void Clear() { _frames.Clear(); _order.Clear(); }
}
