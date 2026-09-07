using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.UI.State;

/// <summary>Small LRU cache with bounded concurrent loading and no render-loop work.</summary>
public sealed class BoundedAsyncCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly SemaphoreSlim _loadGate;
    private readonly object _gate = new();
    private readonly Dictionary<TKey, Entry> _entries = [];
    private readonly Dictionary<TKey, Task<TValue>> _inflight = [];
    private readonly LinkedList<TKey> _lru = [];

    public BoundedAsyncCache(int capacity, int maximumConcurrentLoads)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumConcurrentLoads <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentLoads));
        _capacity = capacity;
        _loadGate = new SemaphoreSlim(maximumConcurrentLoads, maximumConcurrentLoads);
    }

    public int Count { get { lock (_gate) return _entries.Count; } }

    public Task<TValue> GetAsync(TKey key, Func<CancellationToken, Task<TValue>> loader,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loader);
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out Entry? entry))
            {
                Touch(entry);
                return Task.FromResult(entry.Value);
            }
            if (_inflight.TryGetValue(key, out Task<TValue>? active))
                return active.WaitAsync(cancellationToken);
            Task<TValue> created = LoadAndPublishAsync(key, loader, cancellationToken);
            _inflight.Add(key, created);
            return created;
        }
    }

    private async Task<TValue> LoadAndPublishAsync(TKey key,
        Func<CancellationToken, Task<TValue>> loader, CancellationToken cancellationToken)
    {
        // Ensure the task is registered before a synchronously completed loader can remove it.
        await Task.Yield();
        bool entered = false;
        try
        {
            await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            entered = true;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? entry))
                {
                    Touch(entry);
                    return entry.Value;
                }
            }

            TValue value = await loader(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out Entry? existing))
                {
                    Touch(existing);
                    return existing.Value;
                }
                LinkedListNode<TKey> node = _lru.AddFirst(key);
                _entries.Add(key, new Entry(value, node));
                while (_entries.Count > _capacity)
                {
                    LinkedListNode<TKey> last = _lru.Last!;
                    _lru.RemoveLast();
                    _entries.Remove(last.Value);
                }
            }
            return value;
        }
        finally
        {
            if (entered) _loadGate.Release();
            lock (_gate) _inflight.Remove(key);
        }
    }

    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_entries.Remove(key, out Entry? entry)) return false;
            _lru.Remove(entry.Node);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _lru.Clear();
        }
    }

    private void Touch(Entry entry)
    {
        _lru.Remove(entry.Node);
        _lru.AddFirst(entry.Node);
    }

    private sealed record Entry(TValue Value, LinkedListNode<TKey> Node);
}
