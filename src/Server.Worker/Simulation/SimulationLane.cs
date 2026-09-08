using System.Collections.Concurrent;
using System.Diagnostics;
using MphRead.Mods.Network;

namespace FruityPrime.Server.Worker.Simulation;

public sealed record LaneMetrics(int LaneId, int Matches, long Ticks, long CatchUpTicks, long DroppedTicks,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaxMilliseconds);

/// <summary>One dedicated writer for the complete lifetime of every assigned match.</summary>
public sealed class SimulationLane : IDisposable
{
    private sealed record Entry(MatchInstance Match, Action<MatchInstance> Terminal, Action<MatchInstanceStatus>? Snapshot);
    private readonly object _admission = new();
    private readonly ConcurrentQueue<Action> _commands = new();
    private readonly List<Entry> _matches = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private readonly int _capacity;
    private readonly double[] _durations = new double[600];
    private int _queued, _stopping, _samples;
    private long _ticks;
    private LaneMetrics _metrics;
    public int Id { get; }
    public int OwnerThreadId => _thread.ManagedThreadId;
    public LaneMetrics Metrics => Volatile.Read(ref _metrics);
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
            DrainCommands();
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
                _durations[(int)(_ticks % _durations.Length)] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                _samples = Math.Min(_samples + 1, _durations.Length);
                _ticks++;
            }
            if (due > 0 && _ticks % 60 < due) Publish(scheduler);
            _wake.WaitOne(1);
        }
        DrainCommands();
        foreach (Entry entry in _matches)
        {
            try { entry.Match.RequestStop(MatchStopReason.HostShutdown); }
            catch (Exception error) { LastCallbackFailure = error; }
            Complete(entry);
        }
        _matches.Clear();
        Publish(scheduler);
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

    private void DrainCommands()
    {
        for (int count = 0; count < _capacity && _commands.TryDequeue(out var action); count++)
        { Interlocked.Decrement(ref _queued); action(); }
    }

    private void Publish(FixedTickScheduler scheduler)
    {
        var sorted = _durations.Take(Math.Min(_samples, _durations.Length)).Order().ToArray();
        double At(double fraction) => sorted.Length == 0 ? 0 : sorted[(int)((sorted.Length - 1) * fraction)];
        Volatile.Write(ref _metrics, new(Id, _matches.Count, _ticks, scheduler.CatchUpTicks, scheduler.DroppedTicks,
            At(.5), At(.95), At(.99), At(1)));
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
