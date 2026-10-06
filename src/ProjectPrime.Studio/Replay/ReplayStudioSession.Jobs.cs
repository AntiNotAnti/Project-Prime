namespace ProjectPrime.Studio.Replay;

public sealed partial class ReplayStudioSession
{
    private readonly List<Task> _transportJobs = [];
    private CancellationTokenSource _transportLifetime = new();
    private bool _acceptingTransportJobs = true;
    public bool HasPendingTransportJobs => _transportJobs.Any(task => !task.IsCompleted);
    public string? TransportJobError { get; private set; }

    /// <summary>Observe an owner-frame seek. The job never advances simulation.</summary>
    public Task SeekAsync(uint frame, bool resume = false, CancellationToken cancellation = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_acceptingTransportJobs) throw new InvalidOperationException("This replay is saving or closing.");
        cancellation.ThrowIfCancellationRequested();
        if (!Player.Status.Ready && Player.AppliedSeekRequestId == 0)
            throw new InvalidOperationException("Wait for replay preparation before seeking.");
        _transportJobs.RemoveAll(task => task.IsCompleted);
        long request = Player.RequestSeek(frame, resume);
        TransportJobError = null;
        Task task = ObserveAsync();
        _transportJobs.Add(task);
        return task;

        async Task ObserveAsync()
        {
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_transportLifetime.Token, cancellation);
            try
            {
                if (JobRunner is { } runner)
                    await runner("Seek replay to frame " + frame, ObserveOwnerStatusAsync, lifetime.Token);
                else await ObserveOwnerStatusAsync(lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                // Queue cancellation on the same owner as seek and bounded Update.
                // Request identity rejects old-job cancellation after another scrub.
                if (!_disposed) Player.CancelSeek(request);
                throw;
            }

            async Task ObserveOwnerStatusAsync(CancellationToken token)
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (Player.SeekRequestId != request)
                    {
                        lifetime.Cancel();
                        token.ThrowIfCancellationRequested();
                    }
                    var status = Player.Status;
                    if (status.State == "Error") throw new IOException(status.Error ?? "Replay seeking failed.");
                    if (Player.SettledSeekRequestId >= request && status.Ready) return;
                    await Task.Delay(16, token);
                }
            }
        }
    }

    public void Seek(uint frame, bool resume = false)
    {
        try { _ = ObserveCommandAsync(SeekAsync(frame, resume)); }
        catch (Exception error) { if (!_disposed) { TransportJobError = error.Message; Changed?.Invoke(); } }
    }
    private async Task ObserveCommandAsync(Task command)
    {
        try { await command; }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!_disposed) { TransportJobError = error.Message; Changed?.Invoke(); } }
    }
    internal async Task PauseTransportJobsAsync(CancellationToken cancellation)
    {
        _acceptingTransportJobs = false;
        _transportLifetime.Cancel();
        try { await Task.WhenAll(_transportJobs.ToArray()).WaitAsync(cancellation); }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { }
        catch (Exception) when (!cancellation.IsCancellationRequested) { /* Results remain visible in Jobs. */ }
        cancellation.ThrowIfCancellationRequested();
    }
    internal void ResumeTransportJobs()
    {
        if (_disposed || _acceptingTransportJobs) return;
        _transportLifetime.Dispose();
        _transportLifetime = new();
        _acceptingTransportJobs = true;
    }
    private void DisposeTransportJobs()
    {
        _acceptingTransportJobs = false;
        _transportLifetime.Cancel();
        _transportLifetime.Dispose();
    }
}
