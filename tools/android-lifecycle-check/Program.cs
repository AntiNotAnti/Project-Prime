using MphRead.Droid;

int failures = 0;
void Check(bool result, string name)
{
    Console.WriteLine($"ANDROIDLIFECYCLE {(result ? "PASS" : "FAIL")} {name}");
    if (!result) failures++;
}

// Exercise the actual gate used by gameplay and in-process preview owners.
var old = new AndroidRenderLifetime();
var loading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var finishNativeLoad = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var cleanup = new List<string>();
Task oldThread = Task.Run(async () =>
{
    IDisposable? lease = null;
    try
    {
        lease = old.Enter();
        loading.SetResult();
        // An asset/native call already in flight cannot be forcibly cancelled.
        await finishNativeLoad.Task;
        old.Cancellation.ThrowIfCancellationRequested();
    }
    catch (OperationCanceledException) when (old.StopRequested) { }
    finally
    {
        cleanup.Add("scene");
        cleanup.Add("native context");
        old.Complete(lease);
    }
});
await loading.Task.WaitAsync(TimeSpan.FromSeconds(5));
Task stopped = old.RequestStop();
Check(ReferenceEquals(stopped, old.RequestStop()), "repeated stop requests return the same completion task");
Check(!stopped.IsCompleted, "stop during loading does not report completed ownership");

var next = new AndroidRenderLifetime();
var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task nextThread = Task.Run(() =>
{
    attempted.SetResult();
    IDisposable lease = next.Enter();
    Check(cleanup.SequenceEqual(new[] { "scene", "native context" }),
        "replacement acquires only after old scene and native context cleanup");
    acquired.SetResult();
    next.Complete(lease);
});
await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
await Task.Delay(40);
Check(!acquired.Task.IsCompleted && !stopped.IsCompleted,
    "an elapsed callback timeout cannot authorize a replacement owner");
finishNativeLoad.SetResult();
await Task.WhenAll(oldThread, nextThread, stopped).WaitAsync(TimeSpan.FromSeconds(5));
Check(next.Completion.IsCompletedSuccessfully, "replacement completes after ownership handoff");

var owner = new AndroidRenderLifetime();
IDisposable ownerLease = owner.Enter();
var cancelled = new AndroidRenderLifetime();
var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Task cancelledThread = Task.Run(() =>
{
    waiting.SetResult();
    try { cancelled.Enter(); Check(false, "cancelled queued owner cannot acquire"); }
    catch (OperationCanceledException) { Check(true, "cancelled queued owner cannot acquire"); }
    finally { cancelled.Complete(null); }
});
await waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
Task cancelledStop = cancelled.RequestStop();
await Task.WhenAll(cancelledThread, cancelledStop).WaitAsync(TimeSpan.FromSeconds(5));
Check(!owner.Completion.IsCompleted, "cancelling a queued owner leaves the active owner untouched");
bool released = false;
owner.Complete(new ObservedLease(ownerLease, () =>
{
    Check(!owner.Completion.IsCompleted, "completion is published after lease disposal");
    released = true;
}));
Check(released && owner.Completion.IsCompletedSuccessfully, "completed owner released its lease");
owner.Complete(ownerLease, new Exception("duplicate completion must be ignored"));
ownerLease.Dispose();
var afterDuplicate = new AndroidRenderLifetime();
afterDuplicate.Complete(afterDuplicate.Enter());
Check(afterDuplicate.Completion.IsCompletedSuccessfully, "lease disposal and completion are idempotent");

var surface = new AndroidRenderLifetime();
long first = surface.AdvanceSurfaceGeneration();
Check(surface.IsCurrentSurface(first), "new surface generation is current");
surface.AdvanceSurfaceGeneration();
Check(!surface.IsCurrentSurface(first), "destroyed surface generation cannot draw or present");
long replacement = surface.AdvanceSurfaceGeneration();
Check(surface.IsCurrentSurface(replacement) && !surface.IsCurrentSurface(first),
    "recreated surface does not reuse the retired generation");
surface.Complete(null);

// Last: a genuine native teardown failure permanently requires an app restart.
var failed = new AndroidRenderLifetime();
IDisposable failedLease = failed.Enter();
var blocked = new AndroidRenderLifetime();
Task blockedThread = Task.Run(() =>
{
    try { blocked.Enter(); Check(false, "failed retirement blocks new global ownership"); }
    catch (InvalidOperationException ex)
    {
        Check(ex.InnerException?.Message == "native teardown failed",
            "failed retirement blocks new global ownership");
    }
    finally { blocked.Complete(null); }
});
failed.Complete(failedLease, new Exception("native teardown failed"));
try { await failed.Completion; Check(false, "native teardown failure faults stop completion"); }
catch (Exception ex) { Check(ex.Message == "native teardown failed", "native teardown failure faults stop completion"); }
await blockedThread.WaitAsync(TimeSpan.FromSeconds(5));

Console.WriteLine($"ANDROIDLIFECYCLE failures={failures}");
return failures == 0 ? 0 : 1;

sealed class ObservedLease(IDisposable inner, Action beforeRelease) : IDisposable
{
    public void Dispose() { beforeRelease(); inner.Dispose(); }
}
