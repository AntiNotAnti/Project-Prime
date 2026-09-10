using System.Collections.Concurrent;
using System.Diagnostics;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Worker.Simulation;

public sealed record LaneMetrics(int LaneId, int Matches, long Ticks, long CatchUpTicks, long DroppedTicks,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaxMilliseconds,
    double P999Milliseconds = 0, long DeadlineMisses = 0, long CommandQueueHighWater = 0);

/// <summary>One dedicated writer for the complete lifetime of every assigned match.</summary>
public sealed class SimulationLane : IDisposable
{
    internal const int MaximumCommandsPerPass = 64;
    internal const double CommandWallBudgetMilliseconds = 0.5;
    internal const double DeadlineGuardMilliseconds = 1;
    private sealed record Entry(MatchInstance Match, Action<MatchInstance> Terminal, Action<MatchInstanceStatus>? Snapshot);
    private readonly object _admission = new();
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly List<Entry> _matches = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly int _capacity;
    private readonly BoundedPercentileSampler _durations = new(600);
    private int _queued, _stopping, _commandQueueObservedHighWater;
    private int _telemetrySequence;
    private int _matchCount;
    private long _ticks;
    private long _catchUpTicks;
    private long _droppedTicks;
    private long _deadlineMisses;
    private long _commandQueueHighWater;
    private LaneMetrics _metrics;
    public int Id { get; }
    public int OwnerThreadId => _thread.ManagedThreadId;
    public LaneMetrics Metrics
    {
        get
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int sequence = Volatile.Read(ref _telemetrySequence);
                if ((sequence & 1) != 0) continue;
                int matches = Volatile.Read(ref _matchCount);
                long ticks = Volatile.Read(ref _ticks);
                long catchUpTicks = Volatile.Read(ref _catchUpTicks);
                long droppedTicks = Volatile.Read(ref _droppedTicks);
                long deadlineMisses = Volatile.Read(ref _deadlineMisses);
                long commandQueueHighWater = Volatile.Read(ref _commandQueueHighWater);
                if (!_durations.TrySnapshot(out BoundedPercentileSnapshot durations)) continue;
                Thread.MemoryBarrier();
                int completed = Volatile.Read(ref _telemetrySequence);
                if (sequence != completed || (completed & 1) != 0) continue;
                var metrics = new LaneMetrics(Id, matches, ticks, catchUpTicks, droppedTicks,
                    durations.P50, durations.P95, durations.P99, durations.Max,
                    durations.P999, deadlineMisses, commandQueueHighWater);
                Volatile.Write(ref _metrics, metrics);
                return metrics;
            }
            return Volatile.Read(ref _metrics);
        }
    }
    public Exception? LastCallbackFailure { get; private set; }

    public SimulationLane(int id, int commandCapacity)
    {
        if (commandCapacity < 1) throw new ArgumentOutOfRangeException(nameof(commandCapacity));
        Id = id; _capacity = commandCapacity;
        _metrics = new(id, 0, 0, 0, 0, 0, 0, 0, 0);
        _thread = new Thread(Run) { IsBackground = true, Name = $"simulation-lane-{id}" };
        _thread.Start();
    }

    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_admission)
        {
            if (_stopping != 0) return Task.FromException<T>(new ObjectDisposedException(nameof(SimulationLane)));
            if (_queued >= _capacity) return Task.FromException<T>(new InvalidOperationException("Lane command queue is full."));
            _queued++;
            if (_queued > _commandQueueObservedHighWater) _commandQueueObservedHighWater = _queued;
            _commands.Enqueue(() =>
            {
                if (cancellationToken.IsCancellationRequested) { result.TrySetCanceled(cancellationToken); return; }
                try { result.TrySetResult(action()); }
                catch (Exception error) { result.TrySetException(error); }
            });
            _wake.Set();
        }
        return result.Task;
    }

    internal void Add(MatchInstance match, Action<MatchInstance> terminal, Action<MatchInstanceStatus>? snapshot = null)
    {
        RequireOwner();
        if (_matches.Any(entry => entry.Match.MatchId == match.MatchId)) throw new InvalidOperationException("Duplicate lane match.");
        _matches.Add(new(match, terminal, snapshot));
    }

    internal void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != OwnerThreadId) throw new InvalidOperationException("Match mutation belongs to its simulation lane.");
    }

    private void Run()
    {
        var scheduler = new FixedTickScheduler();
        while (Volatile.Read(ref _stopping) == 0)
        {
            DrainCommands(scheduler);
            int due = scheduler.TakeDue(Stopwatch.GetTimestamp());
            for (int step = 0; step < due; step++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int i = _matches.Count - 1; i >= 0; i--)
                {
                    Entry entry = _matches[i];
                    try { entry.Match.Tick(); }
                    catch (Exception) when (entry.Match.State == MatchInstanceState.Failed) { /* Match captured failure; terminate only this entry. */ }
                    try { entry.Snapshot?.Invoke(entry.Match.Status); } catch (Exception error) { LastCallbackFailure = error; }
                    if (entry.Match.State != MatchInstanceState.Running)
                    {
                        _matches.RemoveAt(i);
                        Complete(entry);
                    }
                }
                RecordTelemetry(scheduler, Stopwatch.GetTimestamp() - start);
            }
            WaitForWork(scheduler);
        }
        DrainCommandsForShutdown();
        foreach (Entry entry in _matches)
        {
            try { entry.Match.RequestStop(MatchStopReason.HostShutdown); }
            catch (Exception error) { LastCallbackFailure = error; }
            Complete(entry);
        }
        _matches.Clear();
        PublishScalars(scheduler);
    }

    private void Complete(Entry entry)
    {
        try { entry.Terminal(entry.Match); }
        catch (Exception error)
        {
            LastCallbackFailure = error;
            try { entry.Match.Dispose(); } catch (Exception cleanup) { LastCallbackFailure = new AggregateException(error, cleanup); }
        }
    }

    private void DrainCommands(FixedTickScheduler scheduler)
    {
        long started = Stopwatch.GetTimestamp();
        long wallBudget = Math.Max(1,
            (long)(Stopwatch.Frequency * (CommandWallBudgetMilliseconds / 1000.0)));
        long deadlineGuard = Math.Max(1,
            (long)(Stopwatch.Frequency * (DeadlineGuardMilliseconds / 1000.0)));
        for (int count = 0; count < Math.Min(_capacity, MaximumCommandsPerPass); count++)
        {
            long now = Stopwatch.GetTimestamp();
            // Clock/scheduler handoff can consume the sub-millisecond wall budget
            // before the first command is inspected. Admit one command whenever
            // the simulation deadline still has its guard, then enforce the wall
            // budget for every additional command in this pass.
            if (scheduler.RemainingTicks(now) <= deadlineGuard
                || count > 0 && now - started >= wallBudget)
                break;
            if (!_commands.TryDequeue(out Action? action)) break;
            Interlocked.Decrement(ref _queued);
            action();
        }
    }

    private void DrainCommandsForShutdown()
    {
        while (_commands.TryDequeue(out Action? action))
        {
            Interlocked.Decrement(ref _queued);
            action();
        }
    }

    private void WaitForWork(FixedTickScheduler scheduler)
    {
        if (Volatile.Read(ref _queued) > 0) return;
        long remaining = scheduler.RemainingTicks(Stopwatch.GetTimestamp());
        if (remaining <= 0) return;
        double remainingMilliseconds = remaining * (1000.0 / Stopwatch.Frequency);
        if (remainingMilliseconds >= 2)
        {
            _wake.WaitOne(Math.Max(1, (int)Math.Floor(remainingMilliseconds) - 1));
        }
        else if (remainingMilliseconds >= 0.25)
        {
            Thread.Yield();
        }
        else
        {
            Thread.SpinWait(32);
        }
    }

    private void RecordTelemetry(FixedTickScheduler scheduler, long elapsedTicks)
    {
        int sequence = Interlocked.Increment(ref _telemetrySequence);
        double elapsedMilliseconds = elapsedTicks * (1000.0 / Stopwatch.Frequency);
        _durations.Record(elapsedMilliseconds);
        if (elapsedMilliseconds > 1000.0 / 60.0) _deadlineMisses++;
        _ticks++;
        _matchCount = _matches.Count;
        _catchUpTicks = scheduler.CatchUpTicks;
        _droppedTicks = scheduler.DroppedTicks;
        long observedCommandQueueHighWater = Volatile.Read(ref _commandQueueObservedHighWater);
        if (observedCommandQueueHighWater > _commandQueueHighWater)
            _commandQueueHighWater = observedCommandQueueHighWater;
        Volatile.Write(ref _telemetrySequence, unchecked(sequence + 1));
    }

    private void PublishScalars(FixedTickScheduler scheduler)
    {
        int sequence = Interlocked.Increment(ref _telemetrySequence);
        _matchCount = _matches.Count;
        _catchUpTicks = scheduler.CatchUpTicks;
        _droppedTicks = scheduler.DroppedTicks;
        long observedCommandQueueHighWater = Volatile.Read(ref _commandQueueObservedHighWater);
        if (observedCommandQueueHighWater > _commandQueueHighWater)
            _commandQueueHighWater = observedCommandQueueHighWater;
        Volatile.Write(ref _telemetrySequence, unchecked(sequence + 1));
    }

    public void Dispose()
    {
        if (Environment.CurrentManagedThreadId == OwnerThreadId) throw new InvalidOperationException("Lane disposal must be requested by its host.");
        lock (_admission)
        {
            if (_stopping != 0) return;
            Volatile.Write(ref _stopping, 1);
            _wake.Set();
        }
        if (!_thread.Join(TimeSpan.FromSeconds(35))) throw new TimeoutException("Simulation lane did not stop within its shutdown deadline.");
        _wake.Dispose();
    }
}
