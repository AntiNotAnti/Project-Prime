using System;

namespace MphRead.Mods.Network;

/// <summary>Per-peer cosmetic delivery: two unreliable attempts, never reliable replay backlog.</summary>
internal sealed class NetLiveImpactOutbox
{
    internal const int Capacity = 64, Copies = 2, RepeatFrames = 3, ExpiryFrames = 12, BudgetPerFrame = 4;
    private struct Entry { internal LiveCombatImpact Value; internal uint Born, Next; internal int Remaining; }
    private readonly Entry[] _entries = new Entry[Capacity];
    private int _cursor;
    private uint _lastPump;
    private bool _pumped;
    internal int Count { get; private set; }
    internal long Dropped { get; private set; }
    internal long Sent { get; private set; }
    internal delegate void Sender(ReadOnlySpan<byte> bytes);
    internal void Add(in LiveCombatImpact value, uint now)
    {
        // Publisher duplicates cannot amplify into extra copies during this bounded window.
        foreach (ref var e in _entries.AsSpan())
            if (e.Remaining > 0 && e.Value.Identity == value.Identity) return;
        for (int i = 0; i < Capacity; i++)
        {
            int at = (_cursor + i) % Capacity;
            ref var e = ref _entries[at];
            if (e.Remaining != 0) continue;
            e = new() { Value = value, Born = now, Next = now, Remaining = Copies };
            Count++; return;
        }
        Dropped++; // preserve older attempts; caller never blocks.
    }
    internal void Pump(uint now, ushort match, ulong epoch, Sender send)
    {
        if (_pumped && _lastPump == now) return;
        _pumped = true; _lastPump = now;
        int budget = BudgetPerFrame, start = _cursor;
        Span<byte> bytes = stackalloc byte[LiveCombatImpactPacket.Size];
        for (int i = 0; i < Capacity; i++)
        {
            int at = (start + i) % Capacity; ref var e = ref _entries[at];
            if (e.Remaining == 0) continue;
            if (now - e.Born > ExpiryFrames || e.Value.Fact.MatchId != match || e.Value.Fact.AuthorityEpoch != epoch)
            { e = default; Count--; Dropped++; continue; }
            if (budget == 0 || (int)(now - e.Next) < 0) continue;
            LiveCombatImpactPacket.Write(e.Value, bytes); send(bytes); Sent++; budget--;
            e.Next = now + RepeatFrames;
            if (--e.Remaining == 0) { e = default; Count--; }
            _cursor = (at + 1) % Capacity;
        }
    }
    internal void Reset() { Array.Clear(_entries); Count = _cursor = 0; _pumped = false; }
}
