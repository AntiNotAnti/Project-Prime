using BepuUtilities.Memory;

namespace MphRead.CorpsePhysics;

// Explicit arena ownership enables repeated world-disposal measurements without
// Pool.Clear masking a missed mesh return. Never clear a live world's buffers.
public sealed class CorpsePhysicsArena : IDisposable
{
    internal readonly BufferPool Pool;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private bool _leased, _disposed, _poisoned;
    public long ReservedBytes
    {
        get
        {
            if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Arena owner thread changed.");
            return _disposed ? 0 : checked((long)Pool.GetTotalAllocatedByteCount());
        }
    }
    public CorpsePhysicsArena(int minimumBlockBytes = 131072)
    {
        if (minimumBlockBytes is < 4096 or > 1048576 || (minimumBlockBytes & (minimumBlockBytes - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(minimumBlockBytes));
        Pool = new BufferPool(minimumBlockBytes);
    }
    internal void Acquire()
    {
        if (_disposed || _leased || _poisoned || _owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Arena is disposed, leased or on a different owner thread.");
        _leased = true;
    }
    internal void Release() => _leased = false;
    internal void Poison() => _poisoned = true;
    public void Dispose()
    {
        if (_disposed) return;
        if (_leased || _owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Dispose the owner world before its arena.");
        Pool.Clear(); _disposed = true; GC.SuppressFinalize(this);
    }
}
