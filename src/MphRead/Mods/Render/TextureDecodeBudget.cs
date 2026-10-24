using System;
using System.Threading;
using System.Threading.Tasks;

namespace MphRead.Mods.Render;

// Reservations cover owned decode/prepared data, not opaque codec-private
// scratch or process RSS. Shared across Scenes, including retired map jobs.
internal sealed class TextureDecodeBudget
{
    internal static readonly TextureDecodeBudget Shared = new(
        OperatingSystem.IsAndroid() ? 64L * 1024 * 1024 : 256L * 1024 * 1024,
        OperatingSystem.IsAndroid() ? 1 : 2);
    private readonly object _admission = new();
    private long _reserved, _peak;
    private int _active;
    internal long LimitBytes { get; }
    internal int LimitJobs { get; }
    internal long ReservedBytes => Interlocked.Read(ref _reserved);
    internal long PeakReservedBytes => Interlocked.Read(ref _peak);
    internal int ActiveJobs => Volatile.Read(ref _active);

    internal TextureDecodeBudget(long bytes, int jobs)
    {
        if (bytes <= 0 || jobs <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        LimitBytes = bytes; LimitJobs = jobs;
    }
    internal bool TryAcquire(long bytes, out Reservation reservation)
    {
        reservation = null!;
        if (bytes <= 0 || bytes > LimitBytes || !Monitor.TryEnter(_admission)) return false;
        try
        {
            if (ActiveJobs >= LimitJobs || bytes > LimitBytes - ReservedBytes) return false;
            reservation = new(this, bytes);
            Interlocked.Increment(ref _active);
            long current = Interlocked.Add(ref _reserved, bytes);
            long peak;
            do { peak = PeakReservedBytes; }
            while (current > peak && Interlocked.CompareExchange(ref _peak, current, peak) != peak);
            return true;
        }
        finally { Monitor.Exit(_admission); }
    }
    internal sealed class Reservation : IDisposable
    {
        private TextureDecodeBudget? _owner;
        private readonly long _bytes;
        internal Reservation(TextureDecodeBudget owner, long bytes) { _owner = owner; _bytes = bytes; }
        public void Dispose()
        {
            TextureDecodeBudget? owner = Interlocked.Exchange(ref _owner, null);
            if (owner == null) return;
            Interlocked.Add(ref owner._reserved, -_bytes);
            Interlocked.Decrement(ref owner._active);
        }
    }
}

// Completion carries no decoded result. A retired job clears its owned result
// even when a non-interruptible codec finishes after the Scene was unloaded.
internal sealed class TextureDecodeJob<T> : IDisposable where T : class
{
    private readonly object _state = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TextureDecodeBudget.Reservation _reservation;
    private T? _prepared;
    private bool _retired, _taken;
    internal Task Completion { get; }
    internal TextureDecodeJob(TextureDecodeBudget.Reservation reservation, Func<CancellationToken,T?> decode)
    {
        _reservation = reservation;
        CancellationToken token = _cancellation.Token;
        try
        {
            Completion = Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                T? result = decode(token); // Native codec runs outside the tiny publication lock.
                lock (_state)
                    if (!_retired && !token.IsCancellationRequested) _prepared = result;
            });
            _ = Completion.ContinueWith(completed =>
            {
                // Observe retired faults; no renderer may remain to await them.
                _ = completed.Exception;
                lock (_state)
                    if (_retired || completed.IsFaulted || completed.IsCanceled || (_prepared == null && !_taken))
                        _reservation.Dispose();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        catch { reservation.Dispose(); _cancellation.Dispose(); throw; }
    }
    internal T? TakeCompleted()
    {
        if (!Completion.IsCompleted) throw new InvalidOperationException("Decode is still pending.");
        Completion.GetAwaiter().GetResult(); // Already completed: never waits on the render owner.
        lock (_state)
        {
            T? result = _prepared; _prepared = null;
            _taken = result != null;
            return _retired ? null : result;
        }
    }
    public void Dispose()
    {
        lock (_state)
        {
            if (_retired) return;
            _retired = true; _prepared = null;
            // An active codec retains its reservation until Completion. Never
            // wait for that worker, cancellation callbacks or a queue slot here.
            if (Completion.IsCompleted) _reservation.Dispose();
        }
        _ = CancelAndDisposeAsync();
    }
    private async Task CancelAndDisposeAsync()
    {
        try { await _cancellation.CancelAsync().ConfigureAwait(false); }
        catch { /* Retirement must also survive a failing cancellation callback. */ }
        finally
        {
            try { await Completion.ConfigureAwait(false); }
            catch { /* Completion fault is observed and retired above. */ }
            _cancellation.Dispose();
        }
    }
}
