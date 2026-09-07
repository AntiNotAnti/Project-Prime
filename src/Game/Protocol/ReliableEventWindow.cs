using System;

namespace MphRead.Mods.Network;

/// <summary>Application-event deduplication, independent of the 32-bit packet ACK mask.
/// A 2KiB ring covers 30 seconds at eight sends per 60Hz tick (14,400 attempts).
/// Sequence half-range ambiguity is rejected; storage and clearing are bounded.</summary>
internal sealed class ReliableEventWindow
{
    public const int Capacity = 16384;
    private readonly ulong[] _bits = new ulong[Capacity / 64];
    private bool _initialized;
    private uint _newest;

    public ReceiveResult Record(uint id)
    {
        if (!_initialized)
        { _initialized = true; _newest = id; Mark(id); return ReceiveResult.Newest; }
        uint forward = unchecked(id - _newest);
        if (forward == 0) return ReceiveResult.Duplicate;
        if (forward == 0x80000000u) return ReceiveResult.TooOld;
        if (forward < 0x80000000u)
        {
            if (forward >= Capacity) Array.Clear(_bits);
            else
                for (uint offset = 1; offset <= forward; offset++)
                {
                    uint index = unchecked(_newest + offset) & (Capacity - 1);
                    _bits[index >> 6] &= ~(1ul << (int)(index & 63));
                }
            _newest = id; Mark(id); return ReceiveResult.Newest;
        }
        if (unchecked(_newest - id) >= Capacity) return ReceiveResult.TooOld;
        uint slot = id & (Capacity - 1);
        ulong bit = 1ul << (int)(slot & 63);
        if ((_bits[slot >> 6] & bit) != 0) return ReceiveResult.Duplicate;
        _bits[slot >> 6] |= bit;
        return ReceiveResult.OutOfOrder;
    }

    private void Mark(uint id)
    { uint slot = id & (Capacity - 1); _bits[slot >> 6] |= 1ul << (int)(slot & 63); }
}
