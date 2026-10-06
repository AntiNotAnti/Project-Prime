using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

/// <summary>Render-owner range allocator; freed spans coalesce without moving live draw offsets.</summary>
internal sealed class RetainedAtlasRanges
{
    private readonly List<(uint Start, uint Count)> _free = new();
    private readonly uint _capacity;
    internal uint LiveUnits { get; private set; }
    internal RetainedAtlasRanges(uint capacity)
    {
        _capacity = capacity;
        _free.Add((0, capacity));
    }

    internal bool CanRent(uint count) => _free.Exists(range => range.Count >= count);
    internal uint Rent(uint count)
    {
        if (count == 0) throw new ArgumentOutOfRangeException(nameof(count));
        for (int i = 0; i < _free.Count; i++)
        {
            var range = _free[i];
            if (range.Count < count) continue;
            if (range.Count == count) _free.RemoveAt(i);
            else _free[i] = (checked(range.Start + count), range.Count - count);
            LiveUnits = checked(LiveUnits + count);
            return range.Start;
        }
        throw new InvalidOperationException("Retained atlas has no fitting free range.");
    }

    internal void Return(uint start, uint count)
    {
        uint end = checked(start + count);
        if (count == 0 || count > LiveUnits || end > _capacity) throw new ArgumentOutOfRangeException(nameof(count));
        int i = 0;
        while (i < _free.Count && _free[i].Start < start) i++;
        if ((i > 0 && _free[i - 1].Start + _free[i - 1].Count > start)
            || (i < _free.Count && end > _free[i].Start))
            throw new InvalidOperationException("Retained atlas range was returned twice or overlaps free storage.");
        _free.Insert(i, (start, count));
        LiveUnits -= count;
        if (i > 0 && _free[i - 1].Start + _free[i - 1].Count == start)
        {
            _free[i - 1] = (_free[i - 1].Start, checked(_free[i - 1].Count + count));
            _free.RemoveAt(i--);
        }
        if (i + 1 < _free.Count && _free[i].Start + _free[i].Count == _free[i + 1].Start)
        {
            _free[i] = (_free[i].Start, checked(_free[i].Count + _free[i + 1].Count));
            _free.RemoveAt(i + 1);
        }
    }
}
