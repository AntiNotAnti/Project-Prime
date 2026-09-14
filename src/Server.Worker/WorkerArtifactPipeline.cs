using System.Diagnostics;
using System.Threading.Channels;
using MphRead.Replay;
using MphRead.Telemetry;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;

namespace ProjectPrime.Server.Worker;

/// <summary>Immutable facts captured at the gameplay terminal. The pipeline
/// never reaches back into a disposed MatchInstance.</summary>
internal sealed record WorkerArtifactFacts(
    MatchSpec Spec,
    uint WireMatchId,
    MatchCompletion? Completion,
    Guid? ReplayId,
    ServerReplaySession? Replay,
    bool ReportExpected,
    Guid ReportId,
    WorkerArtifactDisposition? ReportDisposition = null);

internal sealed record WorkerArtifactResult(
    MatchReportReady? ReportReady,
    ArtifactFailureCode? ReportFailure,
    int FailedOperations);

/// <summary>Test-only/host-internal seam for deterministic artifact failures.
/// Production leaves the delegates null and uses the normal writers.</summary>
internal sealed record WorkerArtifactOperations(
    Func<WorkerArtifactFacts, CancellationToken, Task<MatchReportReady?>>? WriteReport = null,
    Func<WorkerArtifactFacts, CancellationToken, Task>? WriteTelemetry = null,
    Func<WorkerArtifactFacts, CancellationToken, Task>? AwaitReplay = null,
    Action<string>? Lifecycle = null);

/// <summary>One atomic report disposition claim for one terminal job. The
/// Worker owns the claim even when the underlying operation completes after a
/// deadline or throws outside the normal process callback.</summary>
internal sealed class WorkerArtifactDisposition
{
    private int _claimed;
    public bool IsClaimed => Volatile.Read(ref _claimed) != 0;
    public bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;
}

public sealed record WorkerArtifactSnapshot(
    PersistenceHealth Health,
    int Active,
    int Queued,
    int Executing,
    long Failures,
    long Completed,
    double DurationAverageMilliseconds);

/// <summary>Bounded, independent artifact owner. It deliberately has no
/// simulation references and does not turn persistence failures into gameplay
/// failures.</summary>
internal sealed class WorkerArtifactPipeline : IAsyncDisposable
{
    private readonly Channel<WorkerArtifactFacts> _queue;
    private readonly Func<WorkerArtifactFacts, CancellationToken, Task<WorkerArtifactResult>> _process;
    private readonly Action<WorkerArtifactFacts, WorkerArtifactResult>? _completedCallback;
    private readonly Action<WorkerArtifactFacts, MatchReportReady?, ArtifactFailureCode?>? _reportDisposition;
    private readonly Task[] _workers;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private int _queued;
    private int _executing;
    private int _active;
    private long _failures;
    private long _completed;
    private long _durationCount;
    private long _durationMilliseconds;
    private int _health = (int)PersistenceHealth.Healthy;
    private int _disposed;

    public WorkerArtifactPipeline(int concurrency, int capacity,
        Func<WorkerArtifactFacts, CancellationToken, Task<WorkerArtifactResult>> process,
        Action<WorkerArtifactFacts, WorkerArtifactResult>? completedCallback = null,
        Action<WorkerArtifactFacts, MatchReportReady?, ArtifactFailureCode?>? reportDisposition = null)
    {
        if (concurrency is < 1 or > 8 || capacity is < 1 or > 4096)
            throw new ArgumentOutOfRangeException();
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _completedCallback = completedCallback;
        _reportDisposition = reportDisposition;
        _queue = Channel.CreateBounded<WorkerArtifactFacts>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        _workers = Enumerable.Range(0, concurrency).Select(_ => RunAsync()).ToArray();
    }

    public WorkerArtifactSnapshot Snapshot
        => new((PersistenceHealth)Volatile.Read(ref _health),
            Math.Max(0, Volatile.Read(ref _active)),
            Math.Max(0, Volatile.Read(ref _queued)),
            Math.Max(0, Volatile.Read(ref _executing)),
            Math.Max(0, Interlocked.Read(ref _failures)),
            Math.Max(0, Interlocked.Read(ref _completed)),
            DurationAverageMilliseconds());

    public bool TryEnqueue(WorkerArtifactFacts facts)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        // Production terminal facts always carry this owner. Keep the
        // pipeline safe for host/test seams too: a report-expected job must
        // have an independent exactly-once disposition even when a caller
        // omitted the optional tail.
        if (facts.ReportExpected && facts.ReportDisposition is null)
            facts = facts with { ReportDisposition = new WorkerArtifactDisposition() };
        Interlocked.Increment(ref _queued);
        Interlocked.Increment(ref _active);
        bool accepted;
        try { accepted = _queue.Writer.TryWrite(facts); }
        catch (ChannelClosedException) { accepted = false; }
        if (!accepted)
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Decrement(ref _active);
            Interlocked.Increment(ref _failures);
            SetHealth(PersistenceHealth.Unavailable);
            return false;
        }
        return true;
    }

    public void MarkUnavailable()
    {
        Interlocked.Increment(ref _failures);
        SetHealth(PersistenceHealth.Unavailable);
    }

    /// <summary>Fence new required-artifact admissions without counting a
    /// second failure when the disposition event itself already accounts for
    /// the operation.</summary>
    public void FenceUnavailable() => SetHealth(PersistenceHealth.Unavailable);

    private async Task RunAsync()
    {
        await foreach (WorkerArtifactFacts facts in _queue.Reader.ReadAllAsync())
        {
            Interlocked.Decrement(ref _queued);
            Interlocked.Increment(ref _executing);
            long started = Stopwatch.GetTimestamp();
            try
            {
                WorkerArtifactResult result;
                try
                {
                    result = await _process(facts, _stop.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    result = new(null, facts.ReportExpected ? ArtifactFailureCode.Shutdown : null, 1);
                }
                catch (Exception)
                {
                    result = new(null, facts.ReportExpected ? ArtifactFailureCode.PersistenceFailed : null, 1);
                }
                if (facts.ReportExpected && facts.ReportDisposition is not { IsClaimed: true })
                {
                    MatchReportReady? ready = result.ReportReady;
                    ArtifactFailureCode? failure = result.ReportFailure;
                    // A malformed producer result cannot publish both sides of
                    // the report disposition. Resolve that conflict to one
                    // bounded failure and leave gameplay terminal ownership
                    // untouched.
                    if (ready != null && failure != null)
                    {
                        ready = null;
                        failure = ArtifactFailureCode.PersistenceFailed;
                    }
                    if (ready == null && failure == null)
                        failure = ArtifactFailureCode.PersistenceFailed;
                    PublishDisposition(facts, ready, failure);
                }
                try { _completedCallback?.Invoke(facts, result); }
                catch (Exception) { /* Diagnostics callbacks never own pipeline lifetime. */ }
                if (result.FailedOperations > 0)
                {
                    Interlocked.Add(ref _failures, result.FailedOperations);
                    SetHealth(result.ReportFailure.HasValue
                        ? PersistenceHealth.Unavailable : PersistenceHealth.Degraded);
                }
                Interlocked.Increment(ref _completed);
            }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - started;
                long milliseconds = Math.Max(0, (long)(elapsed * 1000.0 / Stopwatch.Frequency));
                Interlocked.Increment(ref _durationCount);
                Interlocked.Add(ref _durationMilliseconds, milliseconds);
                Interlocked.Decrement(ref _executing);
                Interlocked.Decrement(ref _active);
            }
        }
    }

    private void SetHealth(PersistenceHealth health)
    {
        while (true)
        {
            PersistenceHealth current = (PersistenceHealth)Volatile.Read(ref _health);
            if (current >= health || Interlocked.CompareExchange(ref _health, (int)health, (int)current) == (int)current)
                return;
        }
    }

    /// <summary>Claims and publishes the one report disposition for a job.
    /// The pipeline owns the claim; the runtime callback is emit-only. This
    /// is also the seam used by the runtime while an uncancellable operation
    /// is still running after its deadline.</summary>
    internal bool PublishDisposition(WorkerArtifactFacts facts, MatchReportReady? ready,
        ArtifactFailureCode? failure)
    {
        if (!facts.ReportExpected || facts.ReportDisposition is not { } disposition
            || !disposition.TryClaim()) return false;
        try { _reportDisposition?.Invoke(facts, ready, failure); }
        catch (Exception) { /* Event publication cannot own pipeline lifetime. */ }
        return true;
    }

    private double DurationAverageMilliseconds()
    {
        long count = Interlocked.Read(ref _durationCount);
        return count == 0 ? 0 : Interlocked.Read(ref _durationMilliseconds) / (double)count;
    }

    public async ValueTask DisposeAsync()
    {
        Task task;
        lock (_disposeGate)
        {
            if (_disposeTask == null)
            {
                Interlocked.Exchange(ref _disposed, 1);
                _disposeTask = DisposeCoreAsync();
            }
            task = _disposeTask;
        }
        await task.ConfigureAwait(false);
    }

    internal Task ShutdownTask
    {
        get { lock (_disposeGate) return _disposeTask ?? Task.CompletedTask; }
    }

    private async Task DisposeCoreAsync()
    {
        // Signal cancellable telemetry and the owned operation boundary first.
        // The reader still drains accepted jobs; uncancellable report/replay
        // work remains owned and keeps this task incomplete until it really
        // finishes.
        _stop.Cancel();
        _queue.Writer.TryComplete();
        await Task.WhenAll(_workers).ConfigureAwait(false);
        _stop.Dispose();
    }
}
