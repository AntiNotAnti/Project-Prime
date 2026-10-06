using System;

namespace MphRead.Mods.Render;

/// <summary>Logical allocation ownership for an immutable GPU atlas page.</summary>
internal sealed class RetainedAtlasPageOwnership
{
    internal RetainedAtlasPageOwnership(ulong reservedBytes)
    {
        if (reservedBytes == 0) throw new ArgumentOutOfRangeException(nameof(reservedBytes));
        ReservedBytes = reservedBytes;
    }

    internal ulong ReservedBytes { get; }
    internal ulong LiveBytes { get; private set; }
    internal int LiveAllocations { get; private set; }
    internal bool IsEmpty => LiveAllocations == 0;

    internal void Retain(ulong bytes)
    {
        if (bytes == 0 || bytes > ReservedBytes - LiveBytes)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        ulong nextBytes = checked(LiveBytes + bytes);
        int nextAllocations = checked(LiveAllocations + 1);
        LiveBytes = nextBytes;
        LiveAllocations = nextAllocations;
    }

    // The caller removes the corresponding entry before releasing ownership.
    // A partial page must survive: other entries can still reference its offsets.
    internal bool Release(ulong bytes)
    {
        if (bytes == 0 || LiveAllocations == 0 || bytes > LiveBytes
            || (LiveAllocations == 1) != (bytes == LiveBytes))
            throw new InvalidOperationException("Retained atlas allocation ownership underflow.");
        ulong nextBytes = LiveBytes - bytes;
        int nextAllocations = LiveAllocations - 1;
        if (nextAllocations == 0 && nextBytes != 0)
            throw new InvalidOperationException("Retained atlas live bytes disagree with its allocations.");
        LiveBytes = nextBytes;
        LiveAllocations = nextAllocations;
        return IsEmpty;
    }
}
