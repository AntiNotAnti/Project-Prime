using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MphRead.Mods.Render;

// Completed profiling samples cannot retain memory indefinitely when a consumer
// stops draining. Keep the latest samples; the owner records evictions as drops.
internal sealed class GpuTimingSampleQueue<T>
{
    private readonly int _capacity;
    private readonly Queue<T> _samples;

    internal GpuTimingSampleQueue(int capacity)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _samples = new Queue<T>(capacity);
    }

    internal int Count => _samples.Count;

    internal bool Enqueue(T sample)
    {
        bool evicted = _samples.Count == _capacity;
        if (evicted) _samples.Dequeue();
        _samples.Enqueue(sample);
        return evicted;
    }

    internal bool TryDequeue([MaybeNullWhen(false)] out T sample) => _samples.TryDequeue(out sample);
    internal void Clear() => _samples.Clear();
}
