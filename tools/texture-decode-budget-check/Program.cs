using System.Diagnostics;
using MphRead.Mods.Render;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    checks++; Console.WriteLine("PASS " + name);
}
void Wait(Func<bool> condition, string name)
    => Check(SpinWait.SpinUntil(condition, TimeSpan.FromSeconds(10)), name);
TextureDecodeBudget.Reservation Acquire(TextureDecodeBudget budget, long bytes)
{
    Check(budget.TryAcquire(bytes, out var lease), "admitted reservation " + bytes);
    return lease;
}

// Negative control models the old per-Scene accounting: throwing away the
// retiring Scene's task list falsely makes the full budget available again.
var oldSceneBudget = new TextureDecodeBudget(64, 1);
using var oldLease = Acquire(oldSceneBudget, 64);
var newSceneBudget = new TextureDecodeBudget(64, 1);
using var newLease = Acquire(newSceneBudget, 64);
Check(oldSceneBudget.ReservedBytes + newSceneBudget.ReservedBytes > 64,
    "negative control: per-Scene reset admits twice the intended cross-transition bytes");

var shared = new TextureDecodeBudget(64, 1);
using var started = new ManualResetEventSlim();
using var unblockCodec = new ManualResetEventSlim();
bool observedCancellation = false;
var retired = new TextureDecodeJob<object>(Acquire(shared, 64), token =>
{
    started.Set(); unblockCodec.Wait(); // Deliberately non-interruptible native-codec stand-in.
    observedCancellation = token.IsCancellationRequested;
    return new object();
});
Check(started.Wait(TimeSpan.FromSeconds(10)), "old Scene codec entered");
long began = Stopwatch.GetTimestamp();
retired.Dispose(); retired.Dispose();
Check(Stopwatch.GetElapsedTime(began).TotalSeconds < 1 && !retired.Completion.IsCompleted,
    "teardown returns without waiting for non-interruptible codec");
Check(shared.ReservedBytes == 64 && shared.ActiveJobs == 1 && !shared.TryAcquire(64, out _),
    "retired decoder still blocks new Scene admission");
unblockCodec.Set();
Wait(() => retired.Completion.IsCompleted && shared.ActiveJobs == 0, "late completion releases retired lease");
Check(observedCancellation, "retired worker observes cancellation before returning");
Check(retired.TakeCompleted() == null && shared.ReservedBytes == 0,
    "late result cannot publish into new Scene");

var ready = new TextureDecodeJob<object>(Acquire(shared, 64), _ => new object());
Wait(() => ready.Completion.IsCompleted, "current Scene decode completes");
Check(shared.ReservedBytes == 64 && !shared.TryAcquire(1, out _),
    "completed prepared data retains its reservation while promotion waits");
Check(ready.TakeCompleted() != null, "completed prepared data transfers to render owner");
Check(shared.ReservedBytes == 64, "promotion owns reservation through upload");
ready.Dispose();
Check(shared.ReservedBytes == 0 && shared.ActiveJobs == 0, "promotion disposal releases reservation once");

var empty = new TextureDecodeJob<object>(Acquire(shared, 64), _ => null);
Wait(() => empty.Completion.IsCompleted && shared.ReservedBytes == 0, "unsupported decode releases lease without promotion");
Check(empty.TakeCompleted() == null, "unsupported decode stays empty"); empty.Dispose();
var failed = new TextureDecodeJob<object>(Acquire(shared, 64), _ => throw new InvalidDataException("fixture codec failure"));
Wait(() => failed.Completion.IsCompleted && shared.ActiveJobs == 0, "failed decode releases lease without promotion");
bool faultObserved = false;
try { failed.TakeCompleted(); } catch (InvalidDataException) { faultObserved = true; }
Check(faultObserved, "decode fault remains observable to renderer"); failed.Dispose();

// Exercise completion/publication/retirement interleavings. The same production
// helper is linked, not a mirrored implementation or a simulated Scene budget.
for (int iteration = 0; iteration < 400; iteration++)
{
    var job = new TextureDecodeJob<object>(Acquire(shared, 64), _ => new object());
    if ((iteration & 1) == 0)
    {
        Wait(() => job.Completion.IsCompleted, "race decode completes");
        _ = job.TakeCompleted();
        Check(shared.ReservedBytes == 64, "completion continuation cannot release transferred upload ownership");
    }
    job.Dispose();
    Wait(() => job.Completion.IsCompleted && shared.ActiveJobs == 0, "race retirement releases lease");
}

var concurrent = new TextureDecodeBudget(100, 2);
int outstanding = 0, peakJobs = 0;
Parallel.For(0, 4000, _ =>
{
    if (!concurrent.TryAcquire(40, out var lease)) return;
    int active = Interlocked.Increment(ref outstanding);
    int previous;
    do { previous = Volatile.Read(ref peakJobs); }
    while (active > previous && Interlocked.CompareExchange(ref peakJobs, active, previous) != previous);
    Thread.SpinWait(100);
    Interlocked.Decrement(ref outstanding); lease.Dispose(); lease.Dispose();
});
Check(peakJobs <= 2 && concurrent.PeakReservedBytes <= 100,
    "concurrent admission respects shared job and byte bounds");
Check(concurrent.ReservedBytes == 0 && concurrent.ActiveJobs == 0, "concurrent idempotent release leaves no outstanding ownership");
Check(!concurrent.TryAcquire(101, out _) && !concurrent.TryAcquire(0, out _), "oversized and invalid requests refused");
Console.WriteLine($"PASS {checks} checks; Windows={OperatingSystem.IsWindows()}; configuration="
#if DEBUG
    + "Debug"
#else
    + "Release"
#endif
);
