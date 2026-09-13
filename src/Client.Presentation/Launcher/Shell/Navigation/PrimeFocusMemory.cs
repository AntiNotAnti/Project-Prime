using System;
using System.Collections.Generic;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Bounded LRU memory for the last stable focus ID in each route/subsection
/// scope. It stores IDs only; stale visuals are validated by the caller when a
/// scope is entered again.
/// </summary>
public sealed class PrimeFocusMemory
{
    private readonly int _capacity;
    private readonly Dictionary<PrimeFocusScope, Entry> _entries = new();
    private readonly LinkedList<PrimeFocusScope> _recency = new();

    public PrimeFocusMemory(int capacity = 64)
    {
        if (capacity is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(capacity),
                "Focus memory must remain bounded between 1 and 1024 scopes.");
        _capacity = capacity;
    }

    public int Capacity => _capacity;
    public int Count => _entries.Count;

    public void Remember(PrimeFocusScope scope, string focusId)
    {
        if (string.IsNullOrWhiteSpace(focusId))
            throw new ArgumentException("Focus memory requires a stable focus ID.",
                nameof(focusId));

        if (_entries.TryGetValue(scope, out Entry? existing))
        {
            existing.FocusId = focusId;
            Touch(existing.Node);
            return;
        }

        var node = new LinkedListNode<PrimeFocusScope>(scope);
        _recency.AddLast(node);
        _entries.Add(scope, new Entry(focusId, node));
        EvictIfNeeded();
    }

    public bool TryGet(PrimeFocusScope scope, out string focusId)
    {
        if (_entries.TryGetValue(scope, out Entry? entry))
        {
            Touch(entry.Node);
            focusId = entry.FocusId;
            return true;
        }

        focusId = "";
        return false;
    }

    public bool Forget(PrimeFocusScope scope)
    {
        if (!_entries.Remove(scope, out Entry? entry)) return false;
        _recency.Remove(entry.Node);
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _recency.Clear();
    }

    public IReadOnlyList<PrimeFocusMemoryEntry> Snapshot()
    {
        var result = new List<PrimeFocusMemoryEntry>(_entries.Count);
        // Oldest first makes eviction and diagnostics observable without
        // exposing mutable dictionary/linked-list internals.
        for (LinkedListNode<PrimeFocusScope>? node = _recency.First;
            node is not null; node = node.Next)
        {
            Entry entry = _entries[node.Value];
            result.Add(new(node.Value, entry.FocusId));
        }
        return result;
    }

    private void Touch(LinkedListNode<PrimeFocusScope> node)
    {
        if (node.List == _recency && node != _recency.Last)
        {
            _recency.Remove(node);
            _recency.AddLast(node);
        }
    }

    private void EvictIfNeeded()
    {
        while (_entries.Count > _capacity)
        {
            LinkedListNode<PrimeFocusScope> oldest = _recency.First
                ?? throw new InvalidOperationException("Focus memory recency is inconsistent.");
            _recency.RemoveFirst();
            _entries.Remove(oldest.Value);
        }
    }

    private sealed class Entry
    {
        public Entry(string focusId, LinkedListNode<PrimeFocusScope> node)
        {
            FocusId = focusId;
            Node = node;
        }

        public string FocusId { get; set; }
        public LinkedListNode<PrimeFocusScope> Node { get; }
    }
}

public readonly record struct PrimeFocusMemoryEntry(PrimeFocusScope Scope,
    string FocusId);
