using System.Collections.Concurrent;
using FruityPrime.Server.Shared;

namespace FruityPrime.Server.Node.Workers;

/// <summary>Owns authenticated child processes. A failed worker is never silently restarted:
/// its match identities remain interrupted, and a replacement receives a new incarnation.</summary>
public interface IWorkerManager : IAsyncDisposable
{
    IReadOnlyList<WorkerSnapshot> Snapshot();
    Task<ManagedWorker> StartAsync(WorkerLaunchOptions options, CancellationToken cancellationToken = default);
}

public sealed class WorkerManager : IWorkerManager
{
    private readonly ConcurrentDictionary<WorkerId, ManagedWorker> _workers = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private bool _disposed;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private readonly int _maximumWorkers;
    public NodeId NodeId { get; }
    public Guid NodeIncarnation { get; }
    public IReadOnlyList<WorkerSnapshot> Snapshot() => _workers.Values.Select(w => w.Snapshot()).ToArray();

    public WorkerManager(NodeId nodeId, Guid nodeIncarnation, int maximumWorkers = 64)
    {
        if (nodeId.Value == Guid.Empty || nodeIncarnation == Guid.Empty) throw new ArgumentException("Node identity is required.");
        if (maximumWorkers is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumWorkers));
        _maximumWorkers = maximumWorkers;
        NodeId = nodeId; NodeIncarnation = nodeIncarnation;
    }

    public async Task<ManagedWorker> StartAsync(WorkerLaunchOptions options, CancellationToken cancellationToken = default)
    {
        options.Validate();
        options = options with { Arguments = options.Arguments.ToArray() };
        ManagedWorker worker;
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_workers.Count >= _maximumWorkers) throw new InvalidOperationException("Worker pool is full; retire stopped workers before replacement.");
            worker = new ManagedWorker(NodeId, NodeIncarnation, options);
            _workers.TryAdd(worker.Id, worker);
        }
        finally { _lifecycle.Release(); }
        try { await worker.StartAsync(cancellationToken); return worker; }
        catch { await worker.DisposeAsync(); throw; }
    }

    public async Task<bool> RetireAsync(WorkerId workerId)
    {
        ManagedWorker? worker;
        await _lifecycle.WaitAsync();
        try
        {
            if (!_workers.TryGetValue(workerId, out worker) || !worker.Completion.IsCompleted) return false;
            _workers.TryRemove(workerId, out _);
        }
        finally { _lifecycle.Release(); }
        await worker.DisposeAsync();
        return true;
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeGate) return new(_disposeTask ??= DisposeCoreAsync());
    }

    private async Task DisposeCoreAsync()
    {
        await _lifecycle.WaitAsync();
        ManagedWorker[] workers;
        try
        {
            if (_disposed) return;
            _disposed = true; workers = _workers.Values.ToArray();
        }
        finally { _lifecycle.Release(); }
        await Task.WhenAll(workers.Select(async worker => await worker.DisposeAsync()));
    }
}
