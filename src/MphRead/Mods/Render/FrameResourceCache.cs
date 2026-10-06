using System;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

// Frame-slot caches must relinquish entries that the completed frame did not
// use. Native bind groups keep their textures/buffers alive independently of
// the application's owning handles.
internal static class FrameResourceCache
{
    internal static int TrimUnused<T, TState>(List<T> entries, int used,
        TState state, Action<TState, T> release)
    {
        if (used < 0) throw new ArgumentOutOfRangeException(nameof(used));
        int firstUnused = Math.Min(used, entries.Count);
        int removed = entries.Count - firstUnused;
        for (int i = firstUnused; i < entries.Count; i++)
            release(state, entries[i]);
        if (removed != 0) entries.RemoveRange(firstUnused, removed);
        return removed;
    }
}
