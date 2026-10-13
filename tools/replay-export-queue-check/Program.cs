using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Replay;

const int capacity = 8;
int checks = 0, failures = 0;
void Check(bool condition, string message)
{
    checks++;
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} {message}");
    if (!condition) failures++;
}
ReplayVideoExportManifest Job(int id) => new(3, "queue-fixture.ppdemo", 1, 120,
    1920, 1080, 60, true, false, true, $"frames-{id}/frame_%08d.png",
    $"output-{id}.mp4", "unused-encoder-arguments", new[]
    {
        new ReplayVideoSegment(1, 60, "First person", ReplaySegmentCamera.FirstPerson),
        new ReplayVideoSegment(60, 120, "Chase", ReplaySegmentCamera.Chase)
    }, PresetName: $"Job {id}");
T Private<T>(string name) => (T)typeof(ReplayExportQueue)
    .GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
ReplayVideoExportManifest[] Snapshot() => Private<Queue<ReplayVideoExportManifest>>("Pending").ToArray();

ReplayExportQueue.ClearPending();
try
{
    var accepted = Enumerable.Range(0, capacity).Select(Job).ToArray();
    accepted[0] = accepted[0] with { Version = 2 };
    Check(accepted.All(ReplayExportQueue.Enqueue), "first eight pending jobs are accepted");
    Check(Snapshot().SequenceEqual(accepted), "accepted job identities, segment order and FIFO are unchanged");
    Check(ReplayExportQueue.MaximumPending == capacity && !ReplayExportQueue.CheckCapacity(),
        "producer preflight rejects full queue before output preparation");
    var rejected = Job(100);
    Check(!ReplayExportQueue.Enqueue(rejected) && ReplayExportQueue.PendingCount == capacity,
        "ninth job is explicitly rejected without growing pending ownership");
    Check(ReferenceEquals(Private<ReplayVideoExportManifest>("_last"), accepted[^1]),
        "rejected export cannot replace the last accepted retry target");
    Check(!ReplayExportQueue.RetryLast() && ReplayExportQueue.PendingCount == capacity,
        "retry cannot bypass a full pending queue");
    Check(ReplayExportQueue.Status.Contains("full", StringComparison.OrdinalIgnoreCase),
        "full queue reports useful rejection feedback");
    int admittedOverflow = 0;
    for (int id = 0; id < 1000; id++)
        if (ReplayExportQueue.Enqueue(Job(200 + id))) admittedOverflow++;
    Check(admittedOverflow == 0 && Snapshot().SequenceEqual(accepted),
        "one thousand repeated queue requests retain only the original eight jobs");
    ReplayExportQueue.CancelActive();
    Check(Snapshot().SequenceEqual(accepted), "cancelling an idle exporter preserves pending FIFO");
    ReplayExportQueue.ClearPending();
    Check(ReplayExportQueue.PendingCount == 0 && ReplayExportQueue.CheckCapacity(),
        "clear pending releases every queued entry and reopens producer admission");
    Check(ReplayExportQueue.RetryLast() && Snapshot().Length == 1
        && ReferenceEquals(Snapshot()[0], accepted[^1]),
        "clear still permits retry of the last accepted export");
    ReplayExportQueue.ClearPending();
    var failed = Job(2000);
    for (int i = 0; i < 100; i++) ReplayExportQueue.NoteFailure(failed, new string('x', 2000));
    Check(ReplayExportQueue.RecentFailures.Count == 5
        && ReplayExportQueue.RecentFailures.All(message => message.Length < 200)
        && ReferenceEquals(Private<ReplayVideoExportManifest>("_failed"), failed),
        "adjacent failure history remains five short messages and one failed job");
    Check(ReplayExportQueue.RetryLast() && ReferenceEquals(Snapshot()[0], failed),
        "failed-job retry keeps its existing priority and manifest identity");
    for (int i = 1; i < capacity; i++) ReplayExportQueue.Enqueue(Job(3000 + i));
    Check(!ReplayExportQueue.RetryLast() && ReplayExportQueue.PendingCount == capacity,
        "failed-job retry obeys the same pending capacity");
    ReplayExportQueue.ClearPending();
    int admitted = 0;
    Parallel.For(0, 256, id =>
    {
        // Preflight is advisory; actual admission must remain atomic even if
        // several hypothetical producers prepare in parallel.
        ReplayExportQueue.CheckCapacity();
        if (ReplayExportQueue.Enqueue(Job(4000 + id))) Interlocked.Increment(ref admitted);
    });
    Check(admitted == capacity && ReplayExportQueue.PendingCount == capacity
        && Snapshot().Distinct().Count() == capacity,
        "parallel count-and-enqueue admission cannot exceed eight pending jobs");
    string outputDirectory = Path.Combine(Path.GetTempPath(), "prime-queue-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(outputDirectory);
    try
    {
        string ownedOutput = Path.Combine(outputDirectory, "existing-user-video.mp4");
        File.WriteAllText(ownedOutput, "existing user output");
        Check(!ReplayExportQueue.Enqueue(Job(5000) with { SuggestedOutput = ownedOutput })
            && File.ReadAllText(ownedOutput) == "existing user output",
            "rejection after preparation never deletes or rewrites an existing destination");
    }
    finally { Directory.Delete(outputDirectory, recursive: true); }
    WeakReference cancelled = LifetimeChecks.CancelActive(() => Job(6000));
    Check(ReplayExportQueue.PendingCount == capacity && !ReplayVideoExporter.Active,
        "one separately owned active job can be cancelled without consuming the eight waiting jobs");
    WeakReference cleared = LifetimeChecks.ClearPending(() => Job(7000));
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Check(!cancelled.IsAlive && !cleared.IsAlive,
        "cancelled active metadata and cleared non-retry jobs are released");
    Check(ReplayExportQueue.CheckCapacity() && ReplayExportQueue.Enqueue(Job(8000))
        && ReplayExportQueue.PendingCount == 1,
        "clear and cancellation leave capacity available for subsequent exports");
    ReplayExportQueue.ClearPending();
    var recursionFirst = Job(9000);
    var recursionSecond = Job(9001);
    ReplayExportQueue.Enqueue(recursionFirst); ReplayExportQueue.Enqueue(recursionSecond);
    bool recursiveStarted = false, recursionBlocked = false, admissionOutsideGate = false;
    bool started = ReplayExportQueue.Pump(static () => false, job =>
    {
        recursionBlocked = ReplayExportQueue.Pump(static () => false,
            _ => { recursiveStarted = true; return true; }, static () => "unused");
        var admission = Task.Run(() => ReplayExportQueue.Enqueue(Job(9002)));
        admissionOutsideGate = admission.Wait(TimeSpan.FromSeconds(2)) && admission.Result;
        return ReferenceEquals(job, recursionFirst);
    }, static () => "unused");
    Check(started && recursionBlocked && !recursiveStarted && admissionOutsideGate
        && Snapshot().Length == 2 && ReferenceEquals(Snapshot()[0], recursionSecond)
        && !Private<bool>("_starting"),
        "pump preserves FIFO, prevents recursive starts and never holds the queue gate across exporter work");
    bool injected = false;
    try
    {
        ReplayExportQueue.Pump(static () => false,
            _ => throw new IOException("injected exporter start failure"), static () => "unused");
    }
    catch (IOException) { injected = true; }
    Check(injected && !Private<bool>("_starting") && ReplayExportQueue.PendingCount == 1,
        "start exception releases the pump guard without dropping another pending job");
    Check(!ReplayExportQueue.Pump(static () => false, static _ => false, static () => "start rejected")
        && !Private<bool>("_starting") && ReplayExportQueue.Status == "start rejected",
        "start rejection releases the pump guard and preserves failure feedback");
    ReplayExportQueue.Enqueue(Job(9450));
    bool cancelledStart = ReplayExportQueue.Pump(static () => false, job =>
    {
        LifetimeChecks.Activate(job);
        ReplayExportQueue.CancelActive();
        return false;
    }, static () => ReplayVideoExporter.Status);
    Check(!cancelledStart && !Private<bool>("_starting") && !ReplayVideoExporter.Active
        && ReplayExportQueue.CheckCapacity() && ReplayExportQueue.Status.Contains("cancel", StringComparison.OrdinalIgnoreCase),
        "cancelled start releases ownership and pump guard without blocking later admission");
    ReplayExportQueue.Enqueue(Job(9500)); ReplayExportQueue.Enqueue(Job(9501));
    bool ownsActive = false;
    int starts = 0;
    using (var staleRead = new ManualResetEventSlim())
    using (var resumeStale = new ManualResetEventSlim())
    {
        var stale = Task.Run(() => ReplayExportQueue.Pump(() =>
        {
            bool snapshot = Volatile.Read(ref ownsActive);
            staleRead.Set(); resumeStale.Wait();
            return snapshot;
        }, _ => { Interlocked.Increment(ref starts); Volatile.Write(ref ownsActive, true); return true; },
            static () => "unused", () => Volatile.Read(ref ownsActive)));
        bool first = false;
        try
        {
            if (!staleRead.Wait(TimeSpan.FromSeconds(2))) throw new Exception("Stale activity fixture did not enter.");
            first = ReplayExportQueue.Pump(() => Volatile.Read(ref ownsActive),
                _ => { Interlocked.Increment(ref starts); Volatile.Write(ref ownsActive, true); return true; },
                static () => "unused", () => Volatile.Read(ref ownsActive));
        }
        finally { resumeStale.Set(); }
        Check(stale.Wait(TimeSpan.FromSeconds(2)) && stale.Result && first && starts == 1
            && ReplayExportQueue.PendingCount == 1 && !Private<bool>("_starting"),
            "stale pre-lock activity read cannot consume or start the next queued job");
    }
    ReplayExportQueue.ClearPending();
    for (int i = 0; i < 100; i++) ReplayExportQueue.Pump();
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 1000; i++) ReplayExportQueue.Pump();
    Check(GC.GetAllocatedBytesForCurrentThread() == before,
        "warmed idle pump keeps its existing zero-allocation render-frame behavior");
    Check(!ReplayVideoExporter.Active, "file-only queue tests never start rendering or encoding");
}
finally { ReplayExportQueue.ClearPending(); }
Console.WriteLine($"RESULT {checks - failures} PASS / {failures} FAIL");
return failures == 0 ? 0 : 1;

static class LifetimeChecks
{
    internal static void Activate(ReplayVideoExportManifest job)
    {
        typeof(ReplayVideoExporter).GetField("_job", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, job);
        typeof(ReplayVideoExporter).GetField("_segments", BindingFlags.NonPublic | BindingFlags.Static)!.SetValue(null, job.Segments!.ToArray());
    }
    // NoInlining keeps test locals from being retained through the caller's GC.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference CancelActive(Func<ReplayVideoExportManifest> create)
    {
        var job = create();
        var weak = new WeakReference(job);
        Activate(job);
        if (!ReplayExportQueue.Pump()) throw new Exception("Active job did not retain export ownership.");
        ReplayExportQueue.CancelActive();
        return weak;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static WeakReference ClearPending(Func<ReplayVideoExportManifest> create)
    {
        ReplayExportQueue.ClearPending();
        var first = create();
        var weak = new WeakReference(first);
        ReplayExportQueue.Enqueue(first);
        // The explicit last-job retry slot may retain the final job, but must
        // not pin every cleared entry.
        ReplayExportQueue.Enqueue(create());
        ReplayExportQueue.ClearPending();
        return weak;
    }
}
