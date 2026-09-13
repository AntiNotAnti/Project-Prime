using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Formats.Sound;

namespace MphRead.Sound;

/// <summary>
/// Owns serial music decode work without building a chain of workers that
/// synchronously wait for one another.  There is one active request and one
/// replaceable pending request.  A newer request fences both older requests;
/// a decoder that cannot observe cancellation still cannot publish stale data.
/// </summary>
internal sealed class MusicLoadCoordinator : IDisposable
{
    internal readonly record struct LoadRequest(
        long Generation, SeqId Sequence, ushort Tracks, float Volume);

    internal enum CompletionStatus
    {
        Published,
        Superseded,
        Canceled,
        Failed,
        Shutdown
    }

    internal sealed record Completion(
        LoadRequest Request,
        CompletionStatus Status,
        TimeSpan Duration,
        Exception? Error);

    internal sealed class RequestHandle
    {
        internal RequestHandle(LoadRequest request, Task<Completion> completion)
        {
            Request = request;
            Completion = completion;
        }

        internal LoadRequest Request { get; }
        internal Task<Completion> Completion { get; }
    }

    internal readonly record struct Diagnostics(
        int CurrentDepth,
        int MaxDepth,
        bool Active,
        long Cancellations,
        long Superseded,
        long Failures,
        long Published,
        TimeSpan LastDuration,
        TimeSpan TotalDuration);

    private sealed class WorkItem
    {
        internal WorkItem(LoadRequest request)
        {
            Request = request;
            Cancellation = new CancellationTokenSource();
            Completion = new TaskCompletionSource<Completion>(
                TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal WorkItem(LoadRequest request, Action<Completion>? observer)
            : this(request)
        {
            Observer = observer;
        }

        internal LoadRequest Request { get; }
        internal CancellationTokenSource Cancellation { get; }
        internal TaskCompletionSource<Completion> Completion { get; }
        internal Action<Completion>? Observer { get; }
        internal bool Finished { get; set; }
        internal bool Superseded { get; set; }
        internal bool Canceled { get; set; }
    }

    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Func<LoadRequest, CancellationToken, Task<IDisposable>> _loadAsync;
    private readonly Func<LoadRequest, IDisposable, bool> _publish;
    private readonly Task _consumer;

    private WorkItem? _active;
    private WorkItem? _pending;
    private Task? _drainTask;
    private long _generation;
    private bool _signalSet;
    private bool _stopping;
    private bool _disposed;
    private int _maxDepth;
    private long _cancellations;
    private long _superseded;
    private long _failures;
    private long _published;
    private TimeSpan _lastDuration;
    private TimeSpan _totalDuration;

    internal MusicLoadCoordinator(
        Func<LoadRequest, CancellationToken, Task<IDisposable>> loadAsync,
        Func<LoadRequest, IDisposable, bool> publish)
    {
        _loadAsync = loadAsync ?? throw new ArgumentNullException(nameof(loadAsync));
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
        _consumer = Task.Run(ConsumeAsync);
    }

    internal bool IsBusy
    {
        get
        {
            lock (_gate) return _active != null || _pending != null;
        }
    }

    internal Diagnostics Snapshot
    {
        get
        {
            lock (_gate)
            {
                return new(
                    CurrentDepth: _pending == null ? 0 : 1,
                    MaxDepth: _maxDepth,
                    Active: _active != null,
                    Cancellations: _cancellations,
                    Superseded: _superseded,
                    Failures: _failures,
                    Published: _published,
                    LastDuration: _lastDuration,
                    TotalDuration: _totalDuration);
            }
        }
    }

    internal RequestHandle Enqueue(SeqId sequence, ushort tracks, float volume = 1,
        Action<Completion>? observer = null)
    {
        WorkItem? replaced = null;
        WorkItem item;
        bool signal = false;

        lock (_gate)
        {
            if (_stopping)
            {
                LoadRequest request = new(_generation, sequence, tracks, volume);
                TaskCompletionSource<Completion> completion =
                    new(TaskCreationOptions.RunContinuationsAsynchronously);
                completion.SetResult(new(request, CompletionStatus.Shutdown,
                    TimeSpan.Zero, null));
                return new RequestHandle(request, completion.Task);
            }

            LoadRequest next = new(++_generation, sequence, tracks, volume);
            item = new WorkItem(next, observer);
            if (_pending != null)
            {
                replaced = _pending;
                replaced.Superseded = true;
                CancelLocked(replaced);
                _pending = null;
            }
            if (_active != null)
            {
                _active.Superseded = true;
                CancelLocked(_active);
            }
            _pending = item;
            _maxDepth = Math.Max(_maxDepth, 1);
            if (!_signalSet)
            {
                _signalSet = true;
                signal = true;
            }
        }

        if (replaced != null) Complete(replaced, CompletionStatus.Superseded,
            TimeSpan.Zero, null);
        if (signal) _signal.Release();
        return new RequestHandle(item.Request, item.Completion.Task);
    }

    /// <summary>
    /// Cancels the active and pending request without waiting for an
    /// uncooperative decoder.  Any result eventually returned by that decoder
    /// is disposed and cannot be published.
    /// </summary>
    internal void Cancel()
    {
        WorkItem? pending;
        Completion? completion = null;
        lock (_gate)
        {
            if (_stopping) return;
            ++_generation;
            if (_active != null)
            {
                _active.Canceled = true;
                CancelLocked(_active);
            }
            pending = _pending;
            _pending = null;
            if (pending != null) CompleteLocked(pending, CompletionStatus.Canceled,
                TimeSpan.Zero, null, out completion);
        }
        pending?.Cancellation.Dispose();
        Notify(pending?.Observer, completion);
    }

    /// <summary>
    /// Stops admission and drains the one consumer.  This is reserved for
    /// process-owned audio shutdown; normal scene release uses <see cref="Cancel"/>.
    /// </summary>
    internal Task DrainAsync()
    {
        WorkItem? pending;
        Completion? completion = null;
        Task drainTask;
        lock (_gate)
        {
            if (_drainTask != null) return _drainTask;
            _stopping = true;
            ++_generation;
            if (_active != null) CancelLocked(_active);
            pending = _pending;
            _pending = null;
            if (pending != null) CompleteLocked(pending, CompletionStatus.Shutdown,
                TimeSpan.Zero, null, out completion);
            _shutdown.Cancel();
            _drainTask = DrainCoreAsync();
            drainTask = _drainTask;
        }
        pending?.Cancellation.Dispose();
        Notify(pending?.Observer, completion);
        return drainTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        DrainAsync().GetAwaiter().GetResult();
    }

    private async Task DrainCoreAsync()
    {
        try
        {
            await _consumer.ConfigureAwait(false);
        }
        finally
        {
            _signal.Dispose();
            _shutdown.Dispose();
        }
    }

    private async Task ConsumeAsync()
    {
        while (true)
        {
            try
            {
                await _signal.WaitAsync(_shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            WorkItem? item;
            bool stopping;
            lock (_gate)
            {
                _signalSet = false;
                item = _pending;
                _pending = null;
                if (item != null) _active = item;
                stopping = _stopping;
            }

            if (item == null)
            {
                if (stopping) break;
                continue;
            }

            await ProcessAsync(item).ConfigureAwait(false);
        }
    }

    private async Task ProcessAsync(WorkItem item)
    {
        long start = Stopwatch.GetTimestamp();
        IDisposable? loaded = null;
        CompletionStatus status = CompletionStatus.Failed;
        Exception? error = null;
        bool published = false;

        try
        {
            loaded = await _loadAsync(item.Request, item.Cancellation.Token)
                .ConfigureAwait(false);
            if (loaded == null)
            {
                throw new InvalidOperationException("Music loader returned no result.");
            }

            lock (_gate)
            {
                if (IsStaleLocked(item))
                {
                    status = GetStaleStatusLocked(item);
                }
                else
                {
                    // Keep the generation fence and publication atomic with
                    // respect to replacement/cancellation. The callback must
                    // only perform the short native publication transaction.
                    try
                    {
                        published = _publish(item.Request, loaded);
                        status = published
                            ? CompletionStatus.Published
                            : CompletionStatus.Canceled;
                    }
                    catch (Exception publishError) when
                        (publishError is not OutOfMemoryException)
                    {
                        error = publishError;
                        status = CompletionStatus.Failed;
                    }
                }
            }
        }
        catch (OperationCanceledException cancel) when
            (item.Cancellation.IsCancellationRequested)
        {
            error = cancel;
            status = GetStaleStatus(item);
        }
        catch (Exception loadError) when (loadError is not OutOfMemoryException)
        {
            error = loadError;
            status = CompletionStatus.Failed;
        }
        finally
        {
            if (!published) DisposeResult(loaded);
            lock (_gate)
            {
                // Clear the active slot before disposing its CTS. A request
                // arriving between completion and cleanup must not observe a
                // disposed active item and attempt to cancel it again.
                if (ReferenceEquals(_active, item)) _active = null;
            }
            TimeSpan duration = Stopwatch.GetElapsedTime(start);
            Complete(item, status, duration, error);
        }
    }

    private bool IsStaleLocked(WorkItem item)
        => _stopping || item.Superseded || item.Cancellation.IsCancellationRequested
            || item.Request.Generation != _generation;

    private CompletionStatus GetStaleStatus(WorkItem item)
    {
        lock (_gate) return GetStaleStatusLocked(item);
    }

    private CompletionStatus GetStaleStatusLocked(WorkItem item)
        => _stopping ? CompletionStatus.Shutdown
            : item.Superseded || item.Request.Generation != _generation && !item.Canceled
                ? CompletionStatus.Superseded : CompletionStatus.Canceled;

    private void CancelLocked(WorkItem item)
    {
        if (!item.Cancellation.IsCancellationRequested)
        {
            item.Cancellation.Cancel();
            ++_cancellations;
        }
    }

    private void Complete(
        WorkItem item, CompletionStatus status, TimeSpan duration, Exception? error)
    {
        Completion? completion;
        lock (_gate) CompleteLocked(item, status, duration, error, out completion);
        item.Cancellation.Dispose();
        Notify(item.Observer, completion);
    }

    private void CompleteLocked(
        WorkItem item, CompletionStatus status, TimeSpan duration, Exception? error,
        out Completion? completion)
    {
        if (item.Finished)
        {
            completion = null;
            return;
        }
        item.Finished = true;
        _lastDuration = duration;
        _totalDuration += duration;
        switch (status)
        {
            case CompletionStatus.Published:
                ++_published;
                break;
            case CompletionStatus.Superseded:
                ++_superseded;
                break;
            case CompletionStatus.Failed:
                ++_failures;
                break;
        }
        completion = new(item.Request, status, duration, error);
        item.Completion.TrySetResult(completion);
    }

    private static void Notify(Action<Completion>? observer, Completion? completion)
    {
        if (observer == null || completion == null) return;
        try
        {
            observer(completion);
        }
        catch (Exception error)
        {
            try
            {
                Console.WriteLine($"[sound] music load observation failed: {error.Message}");
            }
            catch (Exception)
            {
                // Logging is best effort during process teardown.
            }
        }
    }

    private static void DisposeResult(IDisposable? result)
    {
        if (result == null) return;
        try { result.Dispose(); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { Console.WriteLine($"[sound] stale music result disposal failed: {error.Message}"); }
    }
}
