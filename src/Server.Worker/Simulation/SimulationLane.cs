using System.Collections.Concurrent;
using System.Diagnostics;
using MphRead;
using MphRead.Mods.Network;

namespace ProjectPrime.Server.Worker.Simulation;

public sealed record LaneMetrics(int LaneId, int Matches, long Ticks, long CatchUpTicks, long DroppedTicks,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaxMilliseconds,
    double P999Milliseconds = 0, long DeadlineMisses = 0, long CommandQueueHighWater = 0);

/// <summary>Atomic per-match status-publication measurements for heartbeat diagnostics.</summary>
internal readonly record struct StatusPublicationMetrics(long Count, uint FirstTick, uint LastTick)
{
    internal double CadenceHz
    {
        get
        {
            if (Count < 2) return 0;
            uint elapsedTicks = unchecked(LastTick - FirstTick);
            return elapsedTicks == 0
                ? 0
                : (Count - 1) * FixedTickScheduler.Rate / (double)elapsedTicks;
        }
    }
}

/// <summary>One dedicated writer for the complete lifetime of every assigned match.</summary>
public sealed class SimulationLane : IDisposable
{
    internal const int MaximumCommandsPerPass = 64;
    internal const double CommandWallBudgetMilliseconds = 0.5;
    internal const double DeadlineGuardMilliseconds = 1;
    // Status is an operational/control-plane view. Constructing it at the
    // authoritative cadence was needlessly allocating one record per match
    // per tick. Keep the admission predicate on the lane, but publish status
    // only at a stable 10 Hz cadence or when its lifecycle identity changes.
    internal const int StatusSnapshotRateHz = 10;
    internal static readonly SimDuration StatusSnapshotInterval = SimDuration.FromMilliseconds(100);

    private sealed class Entry
    {
        public MatchInstance Match { get; }
        public Action<MatchInstance> Terminal { get; }
        public Action<MatchInstance>? AdmissionCheck { get; }
        public Action<MatchInstanceStatus>? Snapshot { get; }
        public MatchInstanceState PublishedState { get; set; }
        public MatchPhase PublishedPhase { get; set; }
        public SimTick PublishedAt { get; set; }
        public int StatusTelemetrySequence;
        public long StatusPublicationCount;
        public uint FirstStatusTick;
        public uint LastStatusTick;

        public Entry(MatchInstance match, Action<MatchInstance> terminal,
            Action<MatchInstance>? admissionCheck, Action<MatchInstanceStatus>? snapshot)
        {
            Match = match;
            Terminal = terminal;
            AdmissionCheck = admissionCheck;
            Snapshot = snapshot;
            PublishedState = match.State;
            PublishedPhase = match.Simulation.Scene.Match.Phase;
            PublishedAt = new(match.NextTick);
        }
    }
    private readonly object _admission = new();
    private readonly ConcurrentQueue<Action> _commands = new();
    // The simulation list is lane-owned and intentionally remains a List for
    // deterministic tick iteration. Heartbeat readers use this separate
    // concurrent index so status telemetry never enumerates a mutating list.
    private readonly ConcurrentDictionary<Guid, Entry> _statusEntries = new();
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
    private long _statusSnapshots;
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
    internal long StatusSnapshots => Volatile.Read(ref _statusSnapshots);

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
                try
                {
                    T value = action();
                    // Commands can perform lifecycle work without a
                    // simulation step (cancel, admin end, or a phase change).
                    // Publish those transitions immediately while retaining
                    // the lower-rate cadence for ordinary tick progress.
                    PublishChangedStatuses();
                    result.TrySetResult(value);
                }
                catch (Exception error) { result.TrySetException(error); }
            });
            _wake.Set();
        }
        return result.Task;
    }

    internal void Add(MatchInstance match, Action<MatchInstance> terminal,
        Action<MatchInstance>? admissionCheck = null, Action<MatchInstanceStatus>? snapshot = null)
    {
        RequireOwner();
        if (_matches.Any(entry => entry.Match.MatchId == match.MatchId)) throw new InvalidOperationException("Duplicate lane match.");
        Entry entry = new(match, terminal, admissionCheck, snapshot);
        _matches.Add(entry);
        _statusEntries[match.MatchId] = entry;
    }

    internal void RequireOwner()
    {
        if (Environment.CurrentManagedThreadId != OwnerThreadId) throw new InvalidOperationException("Match mutation belongs to its simulation lane.");
    }

    internal bool TryGetStatusPublication(Guid matchId, out StatusPublicationMetrics metrics)
    {
        if (_statusEntries.TryGetValue(matchId, out Entry? entry))
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int sequence = Volatile.Read(ref entry.StatusTelemetrySequence);
                if ((sequence & 1) != 0) continue;
                long count = Volatile.Read(ref entry.StatusPublicationCount);
                uint firstTick = Volatile.Read(ref entry.FirstStatusTick);
                uint lastTick = Volatile.Read(ref entry.LastStatusTick);
                Thread.MemoryBarrier();
                int completed = Volatile.Read(ref entry.StatusTelemetrySequence);
                if (sequence == completed && (completed & 1) == 0)
                {
                    metrics = new(count, firstTick, lastTick);
                    return true;
                }
            }
        }
        metrics = default;
        return false;
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
                    // Admission remains a lane-owned decision and is evaluated
                    // on every authoritative tick. It must not be moved to a
                    // timer/status thread: a join can become valid or invalid
                    // between status publications.
                    try { entry.AdmissionCheck?.Invoke(entry.Match); } catch (Exception error) { LastCallbackFailure = error; }
                    PublishStatusIfDue(entry);
                    if (entry.Match.State != MatchInstanceState.Running)
                    {
                        _matches.RemoveAt(i);
                        _statusEntries.TryRemove(entry.Match.MatchId, out _);
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
            PublishStatusIfDue(entry);
            Complete(entry);
        }
        _matches.Clear();
        _statusEntries.Clear();
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

    private void PublishStatusIfDue(Entry entry)
    {
        MatchInstance match = entry.Match;
        MatchInstanceState state = match.State;
        MatchPhase phase = match.Simulation.Scene.Match.Phase;
        SimTick tick = new(match.NextTick);
        bool lifecycleChanged = state != entry.PublishedState || phase != entry.PublishedPhase;
        bool cadenceDue = tick.HasElapsedSince(entry.PublishedAt, StatusSnapshotInterval);
        if (!lifecycleChanged && !cadenceDue) return;

        try
        {
            if (entry.Snapshot is { } snapshot)
            {
                snapshot(match.Status);
                RecordStatusPublication(entry, tick.Value);
            }
            entry.PublishedState = state;
            entry.PublishedPhase = phase;
            entry.PublishedAt = tick;
            Interlocked.Increment(ref _statusSnapshots);
        }
        catch (Exception error)
        {
            LastCallbackFailure = error;
        }
    }

    private static void RecordStatusPublication(Entry entry, uint tick)
    {
        int sequence = Interlocked.Increment(ref entry.StatusTelemetrySequence);
        long count = entry.StatusPublicationCount + 1;
        entry.StatusPublicationCount = count;
        if (count == 1) entry.FirstStatusTick = tick;
        entry.LastStatusTick = tick;
        Volatile.Write(ref entry.StatusTelemetrySequence, unchecked(sequence + 1));
    }

    private void PublishChangedStatuses()
    {
        for (int i = _matches.Count - 1; i >= 0; i--)
        {
            Entry entry = _matches[i];
            MatchInstanceState state = entry.Match.State;
            MatchPhase phase = entry.Match.Simulation.Scene.Match.Phase;
            if (state != entry.PublishedState || phase != entry.PublishedPhase)
                PublishStatusIfDue(entry);
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
