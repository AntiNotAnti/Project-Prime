using System;
using MphRead.Runtime.HistoricalCollision;

namespace MphRead.Mods.Network;

/// <summary>
/// Match-owned fixed ring of dynamic collision states. Storage is laid out as
/// registry entry x 32 ticks; recording performs only value writes and never
/// allocates. The live Scene is not rewound while a query reads this ring.
/// </summary>
public sealed class DynamicCollisionHistory
{
    public const int Capacity = 32;

    private readonly HistoricalCollisionRegistry _registry;
    private readonly Entry[] _entries;

    private struct Entry
    {
        public bool Present;
        public uint Tick;
        public HistoricalCollisionState State;
    }

    public DynamicCollisionHistory() : this(new HistoricalCollisionRegistry()) { }

    public DynamicCollisionHistory(HistoricalCollisionRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _entries = new Entry[registry.HardColliderCap * Capacity];
    }

    public int ColliderCount => _registry.Count;
    public long Records { get; private set; }
    public long Queries { get; private set; }
    public long Missing { get; private set; }

    /// <summary>Capture one completed authoritative boundary.</summary>
    public void Record(uint tick)
    {
        for (int index = 0; index < _registry.Count; index++)
        {
            _entries[Index(index, tick)] = new Entry
            {
                Present = true,
                Tick = tick,
                State = _registry.CaptureState(index)
            };
            Records++;
        }
    }

    public bool TryGet(HistoricalColliderId identity, uint tick, out HistoricalCollisionState state)
    {
        Queries++;
        if (_registry.TryGetIndex(identity, out int index)
            && TryGetAt(index, identity, tick, out state)) return true;
        Missing++;
        state = default;
        return false;
    }

    /// <summary>Lookup without metric mutation for bounded diagnostics.</summary>
    public bool TryGetDiagnostic(HistoricalColliderId identity, uint tick,
        out HistoricalCollisionState state)
    {
        if (_registry.TryGetIndex(identity, out int index))
            return TryGetAt(index, identity, tick, out state);
        state = default;
        return false;
    }

    public int CopyDiagnosticSnapshot(uint tick, Span<HistoricalCollisionDiagnostic> destination,
        out bool truncated)
    {
        int count = Math.Min(destination.Length, _registry.Count);
        truncated = destination.Length < _registry.Count;
        Span<int> selected = stackalloc int[count];
        int written = 0;
        ReadOnlySpan<HistoricalColliderKind> prioritizedKinds = stackalloc HistoricalColliderKind[]
        {
            HistoricalColliderKind.Door,
            HistoricalColliderKind.ForceField,
            HistoricalColliderKind.Object,
            HistoricalColliderKind.Platform
        };
        foreach (HistoricalColliderKind kind in prioritizedKinds)
        {
            if (written == count) break;
            for (int index = 0; index < _registry.Count; index++)
            {
                if (_registry.GetKind(index) != kind) continue;
                HistoricalCollisionDiagnostic diagnostic = GetDiagnostic(index, tick);
                if (!diagnostic.State.Active) continue;
                selected[written] = index;
                destination[written++] = diagnostic;
                break;
            }
        }
        for (int index = 0; written < count && index < _registry.Count; index++)
        {
            if (selected[..written].Contains(index)) continue;
            selected[written] = index;
            destination[written++] = GetDiagnostic(index, tick);
        }
        return written;

        HistoricalCollisionDiagnostic GetDiagnostic(int index, uint requestedTick)
        {
            HistoricalColliderId identity = _registry.GetIdentity(index);
            HistoricalCollisionState state = TryGetDiagnostic(identity, requestedTick,
                out HistoricalCollisionState historical)
                ? historical : _registry.CaptureState(index);
            return new(identity, _registry.GetKind(index), state);
        }
    }

    public void Clear()
    {
        Array.Clear(_entries);
        Records = Queries = Missing = 0;
    }

    internal void NoteMissing() => Missing++;

    internal bool TryGetAt(int index, HistoricalColliderId identity, uint tick,
        out HistoricalCollisionState state)
    {
        if ((uint)index < (uint)_registry.Count)
        {
            ref readonly Entry entry = ref _entries[Index(index, tick)];
            if (entry.Present && entry.Tick == tick && entry.State.Identity == identity)
            {
                state = entry.State;
                return true;
            }
        }
        state = default;
        return false;
    }

    private static int Index(int colliderIndex, uint tick)
        => colliderIndex * Capacity + (int)(tick & (Capacity - 1));
}
