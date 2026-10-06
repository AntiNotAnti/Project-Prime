using System.Diagnostics;
using ProjectPrime.Studio.Diagnostics;
using ProjectPrime.Studio.Jobs;

int assertions = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    assertions++;
    Console.WriteLine("DIAGNOSTICS PASS " + description);
}

await using var jobs = new StudioJobManager();
using var collector = new StudioPerformanceCollector(jobs);
var idle = collector.Capture();
Check(idle.ProcessWorkingSetBytes > 0 && idle.ManagedBytes > 0, "memory counters come from the running process and managed heap");
Check(idle.RunningJobs == 0 && idle.Sources.Count == 0, "idle collection owns no synthetic jobs or providers");
int calls = 0;
var registration = collector.Register("measured workload", () =>
{
    calls++;
    return new("measured workload", Render: new(null, null, null, null, null, null, null, null, null, null, null, null, null));
});
var unknown = collector.Capture();
Check(calls == 1 && collector.SourceCount == 1 && unknown.Sources.Count == 1, "registered source is sampled once");
Check(unknown.Sources[0].Render?.GpuMilliseconds == null, "unmeasured GPU time remains unknown");
Check(StudioPerformanceText.Format(unknown).Contains("GPU frame  unavailable"), "HUD explicitly labels unavailable GPU measurements");
registration.Dispose(); registration.Dispose();
collector.Capture();
Check(collector.SourceCount == 0 && calls == 1, "idempotent registration disposal removes callback ownership");
using (collector.Register("closing viewport", () => throw new ObjectDisposedException("viewport")))
{
    var closing = collector.Capture();
    Check(closing.Sources.Single().Render == null && closing.Sources.Single().Detail!.Contains("Unavailable"), "closing resource reports unavailability without stale counters");
}

var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var reported = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var jobTask = jobs.RunAsync("measured checkpoint job", async (progress, cancellation) =>
{
    entered.SetResult(); progress.Report(new(.5, "half complete")); reported.SetResult();
    await finish.Task.WaitAsync(cancellation); return 42;
});
await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));
var running = collector.Capture();
Check(running.RunningJobs == 1 && running.Jobs.Single().Fraction == .5, "running job and progress are measured from the actual manager");
Check(running.Jobs.Single().Elapsed > TimeSpan.Zero, "job elapsed time advances while work is active");
finish.SetResult();
Check(await jobTask == 42, "sampling does not change job completion result");
var completed = collector.Capture();
Check(completed.RunningJobs == 0 && completed.Jobs.Single().State == StudioJobState.Completed, "completed job ceases to count as running");
Check(running.Jobs.Single().State == StudioJobState.Running, "published snapshots remain immutable after completion");

var cancellationEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var cancelledTask = jobs.RunAsync("cancelled workload", async (_, cancellation) =>
{
    cancellationEntered.SetResult(); await Task.Delay(Timeout.Infinite, cancellation); return 0;
});
await cancellationEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
jobs.Jobs.Single(job => job.Title == "cancelled workload").Cancel();
try { await cancelledTask; throw new InvalidOperationException("Cancellation was not observed."); }
catch (OperationCanceledException) { }
Check(collector.Capture().Jobs.Single(job => job.Title == "cancelled workload").State == StudioJobState.Cancelled,
    "cancelled resource is observed after owner cancellation");
for (int index = 0; index < 80; index++) await jobs.RunAsync("short measured job", (_, _) => Task.FromResult(index));
Check(jobs.Jobs.Count <= 50 && collector.Capture().RunningJobs == 0, "completed job history remains bounded after repeated work");

for (int index = 0; index < 1000; index++)
{
    using var source = collector.Register("temporary view", () => null);
}
Check(collector.SourceCount == 0, "repeated viewport registrations release every provider");
long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
var clock = Stopwatch.StartNew();
const int samples = 120;
for (int index = 0; index < samples; index++) collector.Capture();
clock.Stop();
long allocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
Console.WriteLine($"DIAGNOSTICS MEASURE samples={samples} meanCpuMs={clock.Elapsed.TotalMilliseconds / samples:0.###} allocatedBytesPerSample={allocated / samples} workingSetBytes={collector.Capture().ProcessWorkingSetBytes}");
Check(allocated > 0 && clock.ElapsedTicks > 0, "collector overhead is measured rather than assigned a constant");
collector.Dispose();
Check(collector.SourceCount == 0, "collector shutdown releases all source owners");
try { collector.Capture(); throw new InvalidOperationException("Disposed collector accepted a sample."); }
catch (ObjectDisposedException) { assertions++; Console.WriteLine("DIAGNOSTICS PASS disposed collector cannot sample"); }
Console.WriteLine($"Studio diagnostics checks passed: {assertions}.");
