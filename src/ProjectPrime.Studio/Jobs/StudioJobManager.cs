using System.Diagnostics;

namespace ProjectPrime.Studio.Jobs;

public enum StudioJobState { Running, Completed, Cancelled, Failed }
public readonly record struct StudioJobProgress(double Fraction, string? Detail = null);
public sealed class StudioCancellationScope : IDisposable
{
    private readonly CancellationTokenSource _source;
    public StudioCancellationScope(CancellationToken parent = default) => _source = CancellationTokenSource.CreateLinkedTokenSource(parent);
    public CancellationToken Token => _source.Token;
    public void Cancel() { try { _source.Cancel(); } catch (ObjectDisposedException) { } }
    public void Dispose() => _source.Dispose();
}
public sealed class StudioJob
{
    internal StudioJob(string title, CancellationToken parent, bool continueOnShutdown) { Title = title; Cancellation = new(parent); ContinueOnShutdown = continueOnShutdown; }
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; }
    public bool ContinueOnShutdown { get; }
    private readonly object _stateGate = new();
    private StudioJobState _state = StudioJobState.Running;
    private StudioJobProgress _progress;
    private string? _error;
    public StudioJobState State { get { lock (_stateGate) return _state; } internal set { lock (_stateGate) _state = value; } }
    public StudioJobProgress Progress { get { lock (_stateGate) return _progress; } internal set { lock (_stateGate) _progress = value; } }
    public TimeSpan Elapsed { get { lock (_stateGate) return _clock.Elapsed; } }
    public string? Error { get { lock (_stateGate) return _error; } internal set { lock (_stateGate) _error = value; } }
    public void Cancel() { if (State == StudioJobState.Running) Cancellation.Cancel(); }
    internal StudioCancellationScope Cancellation { get; }
    internal Task? Task { get; set; }
    internal void Stop() { lock (_stateGate) _clock.Stop(); }
    private readonly Stopwatch _clock = Stopwatch.StartNew();
}

/// <summary>One owner for cancellable desktop jobs; all work starts away from the UI thread.</summary>
public sealed class StudioJobManager : IAsyncDisposable
{
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<StudioJob> _jobs = [];
    private readonly object _gate = new();
    public event Action? Changed;
    public IReadOnlyList<StudioJob> Jobs { get { lock (_gate) return _jobs.ToArray(); } }
    public async Task<T> RunAsync<T>(string title, Func<IProgress<StudioJobProgress>, CancellationToken, Task<T>> work, CancellationToken cancellationToken = default,
        bool continueOnShutdown = false)
    {
        CancellationTokenSource parent;
        StudioJob job;
        Task<T> task;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            parent = CancellationTokenSource.CreateLinkedTokenSource(continueOnShutdown ? CancellationToken.None : _shutdown.Token, cancellationToken);
            job = new(title, parent.Token, continueOnShutdown);
            _jobs.RemoveAll(item => item.State != StudioJobState.Running && _jobs.Count >= 50);
            _jobs.Add(job);
            IProgress<StudioJobProgress> progress = new JobProgress(job, this);
            task = Task.Run(async () =>
            {
                try
                {
                    T result = await work(progress, job.Cancellation.Token).ConfigureAwait(false);
                    job.Cancellation.Token.ThrowIfCancellationRequested();
                    job.State = StudioJobState.Completed;
                    return result;
                }
                catch (OperationCanceledException) when (job.Cancellation.Token.IsCancellationRequested || continueOnShutdown) { job.State = StudioJobState.Cancelled; throw; }
                catch (Exception ex) { job.Error = ex.Message; job.State = StudioJobState.Failed; throw; }
                finally { job.Stop(); NotifyChanged(); }
            }, CancellationToken.None);
            job.Task = task;
        }
        NotifyChanged();
        try { return await task.ConfigureAwait(false); }
        finally { job.Cancellation.Dispose(); parent.Dispose(); }
    }
    public void CancelAll() { foreach (var job in Jobs) job.Cancel(); }
    private bool _disposed;
    public async ValueTask DisposeAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            tasks = _jobs.Where(job => !job.ContinueOnShutdown).Select(job => job.Task).OfType<Task>().ToArray();
        }
        _shutdown.Cancel();
        try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch (Exception) { /* Job errors are already observable on each job. */ }
        _shutdown.Dispose();
    }
    private void NotifyChanged()
    {
        if (Changed is not { } subscribers) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
            try { subscriber(); } catch (Exception) { /* Observers cannot take ownership of or interrupt a job. */ }
    }
    private sealed class JobProgress(StudioJob job, StudioJobManager manager) : IProgress<StudioJobProgress>
    {
        public void Report(StudioJobProgress value) { job.Progress = value with { Fraction = double.IsFinite(value.Fraction) ? Math.Clamp(value.Fraction, 0, 1) : 0 }; manager.NotifyChanged(); }
    }
}
