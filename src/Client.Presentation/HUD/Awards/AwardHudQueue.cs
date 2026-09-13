using System;
using MphRead;

namespace MphRead.Mods.Hud;

/// <summary>Bounded presentation queue; TripleKill supersedes DoubleKill.</summary>
public sealed class AwardHudQueue
{
    public const int Capacity = 8;
    public const int DedupCapacity = 128;
    private readonly MatchAward[] _queue = new MatchAward[Capacity];
    private readonly uint[] _seen = new uint[DedupCapacity];
    private int _count, _seenCount, _seenHead;

    public int Count => _count;
    public long DuplicateAwards { get; private set; }
    public long DroppedAwards { get; private set; }
    public long SupersededAwards { get; private set; }
    public uint Revision { get; private set; }

    public bool Enqueue(in MatchAward award)
    {
        if (!award.IsValid) return false;
        if (!Remember(award.AwardId)) { DuplicateAwards++; return false; }
        if (award.Kind == MatchAwardKind.TripleKill)
        {
            for (int i = 0; i < _count;)
            {
                if (_queue[i].Kind == MatchAwardKind.DoubleKill && _queue[i].Subject == award.Subject)
                {
                    Remove(i); SupersededAwards++; continue;
                }
                i++;
            }
        }
        if (_count < Capacity) { _queue[_count++] = award; return true; }
        int lowest = 0;
        for (int i = 1; i < _count; i++)
            if (_queue[i].Priority < _queue[lowest].Priority) lowest = i;
        if (award.Priority <= _queue[lowest].Priority) { DroppedAwards++; return false; }
        _queue[lowest] = award; DroppedAwards++; return true;
    }

    public bool TryDequeue(out MatchAward award)
    {
        if (_count == 0) { award = default; return false; }
        int best = 0;
        for (int i = 1; i < _count; i++) if (_queue[i].Priority > _queue[best].Priority) best = i;
        award = _queue[best]; Remove(best); return true;
    }

    public void Reset()
    {
        Array.Clear(_queue); Array.Clear(_seen);
        _count = _seenCount = _seenHead = 0;
        DuplicateAwards = DroppedAwards = SupersededAwards = 0;
        Revision = Revision == uint.MaxValue ? 1 : Revision + 1;
    }

    private void Remove(int index)
    {
        for (int i = index + 1; i < _count; i++) _queue[i - 1] = _queue[i];
        _queue[--_count] = default;
    }

    private bool Remember(uint id)
    {
        for (int i = 0; i < _seenCount; i++)
            if (_seen[(_seenHead - 1 - i + DedupCapacity) % DedupCapacity] == id) return false;
        _seen[_seenHead] = id; _seenHead = (_seenHead + 1) % DedupCapacity;
        if (_seenCount < DedupCapacity) _seenCount++;
        return true;
    }
}
