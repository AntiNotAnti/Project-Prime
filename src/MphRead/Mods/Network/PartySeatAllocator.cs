using System;

namespace MphRead.Mods.Network;

/// <summary>
/// Pure all-or-nothing selector for party seats. The caller supplies every
/// slot already unavailable to ordinary admission. No partial result escapes.
/// </summary>
internal static class PartySeatAllocator
{
    internal static bool TryAllocate(
        int playerCapacity,
        ushort unavailable,
        int requested,
        out byte[] slots,
        out ushort reservedMask)
    {
        slots = Array.Empty<byte>();
        reservedMask = 0;

        if (playerCapacity is < 1 or > 8
            || requested is < 1 or > 8
            || requested > playerCapacity
            || (unavailable >> playerCapacity) != 0)
            return false;

        var candidate = new byte[requested];
        ushort mask = 0;
        int count = 0;
        for (int slot = 0; slot < playerCapacity && count < requested; slot++)
        {
            if ((unavailable & (1 << slot)) != 0)
                continue;
            candidate[count++] = (byte)slot;
            mask |= (ushort)(1 << slot);
        }

        if (count != requested)
            return false;

        slots = candidate;
        reservedMask = mask;
        return true;
    }
}
