using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.MapGen;

/// <summary>Keys live only while a holder or waiter owns them.</summary>
internal sealed class MapUploadLocks
{
    private sealed class Entry { internal readonly SemaphoreSlim Gate = new(1, 1); internal int Users; }
    private readonly object _sync = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    internal int Count { get { lock (_sync) return _entries.Count; } }
    private Entry Retain(string key)
    {
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry)) _entries.Add(key, entry = new());
            entry.Users++;
            return entry;
        }
    }
    internal async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellation)
    {
        var entry = Retain(key);
        try { await entry.Gate.WaitAsync(cancellation).ConfigureAwait(false); return new Lease(this, key, entry); }
        catch { Release(key, entry, held: false); throw; }
    }
    internal IDisposable? TryAcquire(string key)
    {
        var entry = Retain(key);
        if (entry.Gate.Wait(0)) return new Lease(this, key, entry);
        Release(key, entry, held: false);
        return null;
    }
    private void Release(string key, Entry entry, bool held)
    {
        if (held) entry.Gate.Release();
        lock (_sync)
        {
            if (--entry.Users != 0) return;
            _entries.Remove(key);
            entry.Gate.Dispose();
        }
    }
    private sealed class Lease(MapUploadLocks owner, string key, Entry entry) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release(key, entry, held: true); }
    }
}
