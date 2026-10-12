using System.Collections.Generic;

namespace MphRead.Mods.Network;

internal enum PickupItemAcceptance { Unchanged, Accepted, Conflict, Capacity }

/// <summary>Live incarnations plus bounded recent tombstones. Discarding old
/// tombstones cannot resurrect them: incoming facts older than the retained
/// authority watermark are ignored. Late-join baselines refresh current items'
/// authority frame. The 1024-step horizon exceeds reliable delivery's 15s TTL.</summary>
internal static class PickupItemHistory
{
    internal const int Capacity = 2048;
    internal const uint Retention = 1024;
    internal static PickupItemAcceptance Accept(Dictionary<PowerUpSpawnKey, PickupItemFact> items, PickupItemFact fact)
    {
        bool hasFrame = false; uint latest = 0;
        foreach (var item in items.Values)
            if (!hasFrame || NetLifecycleTracker.Newer(item.AuthorityFrame, latest))
            { latest = item.AuthorityFrame; hasFrame = true; }
        if (hasFrame && unchecked((int)(latest - fact.AuthorityFrame)) > Retention)
            return PickupItemAcceptance.Unchanged;
        bool exists = items.TryGetValue(fact.Spawn, out var previous);
        if (exists)
        {
            if (previous == fact) return PickupItemAcceptance.Unchanged;
            bool newer = NetLifecycleTracker.Newer(fact.AuthorityFrame, previous.AuthorityFrame);
            if (!newer && fact.AuthorityFrame != previous.AuthorityFrame) return PickupItemAcceptance.Unchanged;
            if (previous.Type != fact.Type) return PickupItemAcceptance.Conflict;
            if (!previous.Available && fact.Available)
                return newer ? PickupItemAcceptance.Conflict : PickupItemAcceptance.Unchanged;
            // Native consumption and deferred removal can both publish a
            // tombstone in one frame, with a parent moving the item between
            // them. Its consumed position no longer affects availability.
            if (!newer && !previous.Available && !fact.Available)
                return PickupItemAcceptance.Unchanged;
            if (!newer && previous.Available == fact.Available)
                return PickupItemAcceptance.Conflict;
        }
        if (!hasFrame || NetLifecycleTracker.Newer(fact.AuthorityFrame, latest)) latest = fact.AuthorityFrame;
        var expired = new List<PowerUpSpawnKey>();
        foreach (var pair in items)
            if (!pair.Value.Available && unchecked((int)(latest - pair.Value.AuthorityFrame)) > Retention)
                expired.Add(pair.Key);
        foreach (var key in expired) items.Remove(key);
        if (!exists && items.Count >= Capacity) return PickupItemAcceptance.Capacity;
        items[fact.Spawn] = fact;
        return PickupItemAcceptance.Accepted;
    }
}
