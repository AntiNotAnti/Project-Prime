using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Droid;

/// <summary>
/// Process-wide ownership of the shared renderer/world, including offscreen
/// previews. Stopping requests cancellation; completion is published only
/// after the owner has released its scene, native context and ownership lease.
/// </summary>
internal sealed class AndroidRenderLifetime
{
    private static readonly SemaphoreSlim Ownership = new(1, 1);
    private static Exception? _retirementFailure;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _surfaceGeneration;
    private int _completionPublished;

    internal CancellationToken Cancellation => _stop.Token;
    internal Task Completion => _completed.Task;
    internal bool StopRequested => _stop.IsCancellationRequested;
    internal long SurfaceGeneration => Interlocked.Read(ref _surfaceGeneration);
    internal long AdvanceSurfaceGeneration() => Interlocked.Increment(ref _surfaceGeneration);
    internal bool IsCurrentSurface(long generation) => generation == SurfaceGeneration;

    internal Task RequestStop()
    {
        _stop.Cancel();
        return Completion;
    }

    internal IDisposable Enter()
    {
        Ownership.Wait(Cancellation);
        if (Volatile.Read(ref _retirementFailure) is Exception failure)
        {
            Ownership.Release();
            throw new InvalidOperationException(
                "The previous Android renderer did not finish teardown. Restart the app before rendering again.", failure);
        }
        return new Lease();
    }

    internal void Complete(IDisposable? ownership, Exception? retirementFailure = null)
    {
        if (Interlocked.Exchange(ref _completionPublished, 1) != 0) return;
        // Failed teardown is recorded while we still hold the lease. A waiting
        // renderer must observe it before it can touch global graphics state.
        if (retirementFailure != null)
            Interlocked.CompareExchange(ref _retirementFailure, retirementFailure, null);
        try { ownership?.Dispose(); }
        catch (Exception ex)
        {
            retirementFailure ??= ex;
            Interlocked.CompareExchange(ref _retirementFailure, ex, null);
        }
        // Completion means there is no remaining renderer ownership, not just
        // that cancellation was requested or a wait timed out.
        if (retirementFailure == null) _completed.TrySetResult();
        else _completed.TrySetException(retirementFailure);
    }

    private sealed class Lease : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                Ownership.Release();
        }
    }
}
