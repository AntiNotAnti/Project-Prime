using ProjectPrime.Studio.Jobs;

internal static partial class Program
{
    private static async Task CheckJobsAsync()
    {
        await using var jobs = new StudioJobManager();
        int changes = 0;
        jobs.Changed += () => Interlocked.Increment(ref changes);
        Check(await jobs.RunAsync("Complete", (progress, _) =>
        {
            progress.Report(new StudioJobProgress(2, "Finished"));
            return Task.FromResult(42);
        }) == 42 && jobs.Jobs.Single().State == StudioJobState.Completed && jobs.Jobs.Single().Progress.Fraction == 1,
            "job completion and progress are observable and clamped");
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int released = 0;
        Task<int> cancelled = jobs.RunAsync<int>("Cancel", async (_, token) =>
        {
            started.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            finally { Interlocked.Increment(ref released); }
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        StudioJob running = jobs.Jobs.Single(job => job.State == StudioJobState.Running);
        running.Cancel();
        await ExpectAsync<OperationCanceledException>(() => cancelled, "cancel propagates through worker token");
        Check(running.State == StudioJobState.Cancelled && released == 1, "cancelled job releases worker resources");
        await ExpectAsync<IOException>(() => jobs.RunAsync<int>("Fail", (_, _) => Task.FromException<int>(new IOException("expected failure"))),
            "job errors propagate to caller");
        Check(jobs.Jobs.Last().State == StudioJobState.Failed && jobs.Jobs.Last().Error == "expected failure", "failed job retains useful diagnostics");

        var shutdownStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> shutdownJob = jobs.RunAsync<int>("Shutdown", async (_, token) =>
        {
            shutdownStarted.SetResult();
            try { await Task.Delay(Timeout.Infinite, token); return 0; }
            finally { Interlocked.Increment(ref released); }
        });
        await shutdownStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await jobs.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await ExpectAsync<OperationCanceledException>(() => shutdownJob, "clean shutdown cancels owned jobs");
        Check(jobs.Jobs.All(job => job.State != StudioJobState.Running) && released == 2 && changes >= 8,
            "clean shutdown awaits worker release and publishes terminal states");
        await ExpectAsync<ObjectDisposedException>(() => jobs.RunAsync("After shutdown", (_, _) => Task.FromResult(0)),
            "closed job owner accepts no new work");
    }
}
