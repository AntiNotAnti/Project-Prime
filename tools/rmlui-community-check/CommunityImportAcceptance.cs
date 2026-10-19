using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

/// <summary>Synthetic privately prepared packages only; never opens a picker or publishes real map files.</summary>
internal static class CommunityImportAcceptance
{
    internal static void Run()
    {
        GatesAndReview(); RetirementAndFailures(); CompletionRaces(); ThreadOwnership();
    }
    private static void GatesAndReview()
    {
        var authority = new Authority(); using var owner = new NativeCommunityImportController(authority);
        Check(!owner.Confirm() && !owner.Prepare("", false) && !owner.Prepare("   ", false)
            && !owner.Prepare(new string('x', 4097), false), "empty or oversized path cannot prepare or confirm");
        authority.PickerAvailable = false;
        Check(!owner.Prepare("ignored", true) && !owner.Tick().PickerAvailable && authority.Captures == 0,
            "unavailable picker is gated before context capture");
        authority.PickerAvailable = true; authority.NoContext = true;
        Check(!owner.Prepare("/synthetic/map.ppmap", false) && !owner.Tick().Busy
            && owner.Tick().Status.Contains("IMPORT UNAVAILABLE"), "missing context cannot begin package work");
        authority.NoContext = false; authority.ThrowCapture = true;
        Check(!owner.Prepare("/synthetic/map.ppmap", false) && !owner.Tick().Status.Contains("PRIVATE SECRET"),
            "context failure is sanitized without starting work");
        authority.ThrowCapture = false;
        const string path = " /synthetic/é arena.ppmap ";
        var request = Begin(owner, authority, path, false);
        Check(request.Path == path && !request.Picker && ReferenceEquals(request.Context, authority.Context),
            "preparation captures exact path, picker flag and opaque root context");
        Check(owner.Tick().Busy && !owner.Tick().CanConfirm && !owner.Tick().Imported
            && !owner.Prepare("other.ppmap", false) && !owner.Confirm(),
            "pending preparation prevents duplicate work and optimistic confirmation");
        var prepared = new Prepared(); Complete(owner, request, prepared);
        var review = owner.Tick();
        Check(review.CanConfirm && !review.Imported && review.Name == prepared.Name && review.Version == prepared.Version
            && review.PackageHash == prepared.PackageHash && prepared.Commits == 0 && prepared.Disposals == 0,
            "verified exact name/version/hash require review before owner confirmation");
        Check(owner.Confirm() && prepared.Commits == 1 && prepared.CommitThread == Environment.CurrentManagedThreadId
            && prepared.Disposals == 1 && owner.Tick().Imported && !owner.Tick().CanConfirm,
            "one explicit owner confirmation commits exactly once and disposes preparation");
        Check(!owner.Confirm() && prepared.Commits == 1, "completed import cannot be published twice");
        owner.Cancel();
        Check(!owner.Tick().Imported && owner.Tick().Name == "" && owner.Tick().PackageHash == "",
            "Cancel clears prior imported presentation and package identity");
        var picker = Begin(owner, authority, "", true); Complete(owner, picker, null);
        Check(picker.Picker && owner.Tick().Status.Contains("FILE SELECTION CANCELED") && !owner.Tick().CanConfirm,
            "canceled synthetic picker returns no prepared package or publish action");
    }
    private static void RetirementAndFailures()
    {
        var authority = new Authority(); using var owner = new NativeCommunityImportController(authority);
        foreach (string hash in new[] { "", new string('a', 63), new string('g', 64) })
        {
            var request = Begin(owner, authority); var invalid = new Prepared { PackageHash = hash };
            Complete(owner, request, invalid);
            Check(!owner.Tick().CanConfirm && owner.Tick().PackageHash == "" && invalid.Disposals == 1 && invalid.Commits == 0,
                "malformed exact package hash is rejected and disposed");
        }
        var stale = new Prepared { Current = false }; Complete(owner, Begin(owner, authority), stale);
        Check(!owner.Tick().CanConfirm && stale.Disposals == 1, "prepared source stale at completion cannot enter review");
        var active = new Prepared(); Complete(owner, Begin(owner, authority), active);
        active.Current = false; var retired = owner.Tick();
        Check(!retired.CanConfirm && retired.Name == "" && retired.PackageHash == "" && active.Disposals == 1
            && !owner.Confirm() && active.Commits == 0, "changed prepared source retires previously reviewed labels and confirmation");
        var contextOwned = new Prepared(); Complete(owner, Begin(owner, authority), contextOwned);
        authority.Context = new object(); retired = owner.Tick();
        Check(!retired.CanConfirm && retired.Name == "" && contextOwned.Disposals == 1 && !owner.Confirm(),
            "root context replacement disposes current preparation and clears private labels");
        var pending = Begin(owner, authority); authority.Context = new object(); owner.Tick();
        Check(pending.Token.IsCancellationRequested && !owner.Tick().Busy && !owner.Tick().CanConfirm,
            "context replacement cancels held package verification");
        var late = new Prepared(); pending.Completion.TrySetResult(late);
        Wait(() => late.Disposals == 1, "late changed-context preparation is disposed exactly once");
        Check(!owner.Tick().CanConfirm && late.Commits == 0, "changed-context late result cannot resurrect review");
        var first = new Prepared(); Complete(owner, Begin(owner, authority), first);
        var replacementRequest = Begin(owner, authority);
        Check(first.Disposals == 1 && !owner.Tick().CanConfirm, "starting replacement disposes active reviewed preparation");
        var replacement = new Prepared(); Complete(owner, replacementRequest, replacement);
        owner.Cancel(); Check(replacement.Disposals == 1 && !owner.Tick().CanConfirm, "Cancel disposes replacement exactly once");
        var failureRequest = Begin(owner, authority); failureRequest.Completion.TrySetException(new InvalidOperationException("PRIVATE SECRET PATH"));
        Wait(() => !owner.Tick().Busy, "failed preparation settles");
        Check(owner.Tick().Status.Contains("PACKAGE UNAVAILABLE OR INVALID") && !owner.Tick().Status.Contains("PRIVATE SECRET"),
            "preparation errors use bounded sanitized status");
        var failing = new Prepared { FailCommit = true }; Complete(owner, Begin(owner, authority), failing);
        Check(!owner.Confirm() && failing.Commits == 1 && failing.Disposals == 1 && !owner.Tick().Imported
            && !owner.Tick().CanConfirm && owner.Tick().Status.Contains("PREPARE AGAIN")
            && !owner.Tick().Status.Contains("PRIVATE SECRET"), "commit failure is sanitized and requires fresh preparation");
        var retry = new Prepared(); Complete(owner, Begin(owner, authority), retry);
        Check(owner.Confirm() && retry.Commits == 1 && retry.Disposals == 1 && owner.Tick().Imported,
            "fresh verified preparation can retry after commit failure");
    }
    private static void CompletionRaces()
    {
        foreach (bool dispose in new[] { false, true })
        foreach (bool alreadyQueued in new[] { false, true })
        {
            var authority = new Authority(); var owner = new NativeCommunityImportController(authority);
            var request = Begin(owner, authority); var prepared = new Prepared();
            if (alreadyQueued)
            {
                request.Completion.TrySetResult(prepared);
                Wait(() => Queued(owner), "completed synthetic preparation reaches owner publication queue");
            }
            if (dispose) owner.Dispose(); else owner.Cancel();
            Check(request.Token.IsCancellationRequested || alreadyQueued,
                "retirement cancels unfinished preparation source");
            if (!alreadyQueued) request.Completion.TrySetResult(prepared); // Fake ignores cancellation.
            Wait(() => prepared.Disposals == 1, "canceled/disposed held-or-queued preparation is disposed exactly once");
            Check(prepared.Commits == 0 && (dispose || !owner.Tick().CanConfirm),
                "retired completion never commits or regains review authority");
            owner.Dispose(); Check(prepared.Disposals == 1, "repeated lifetime disposal does not dispose result twice");
        }
        // Resolve and dispose back-to-back without waiting for worker publication:
        // either side of the controller's queue/lifetime lock must own disposal.
        for (int i = 0; i < 8; i++)
        {
            var racingAuthority = new Authority(); var racingOwner = new NativeCommunityImportController(racingAuthority);
            var racingRequest = Begin(racingOwner, racingAuthority); var racingPrepared = new Prepared();
            racingRequest.Completion.TrySetResult(racingPrepared); racingOwner.Dispose();
            Wait(() => racingPrepared.Disposals == 1, "completion-versus-disposal publication race retains one disposal owner");
            racingOwner.Dispose(); Check(racingPrepared.Disposals == 1 && racingPrepared.Commits == 0,
                "publishing race cannot duplicate disposal or publish a package");
        }
        var service = new Authority(); using var current = new NativeCommunityImportController(service);
        var old = Begin(current, service); current.Cancel();
        var newest = Begin(current, service); var accepted = new Prepared { Name = "NEWEST PREPARED" };
        Complete(current, newest, accepted);
        var stale = new Prepared { Name = "OLD CANCELED PACKAGE" }; old.Completion.TrySetResult(stale);
        Wait(() => stale.Disposals == 1, "late older selection is disposed while newer review remains active");
        Check(current.Tick().Name == accepted.Name && current.Tick().CanConfirm && accepted.Disposals == 0 && stale.Commits == 0,
            "late canceled result cannot replace current reviewed package");
    }
    private static void ThreadOwnership()
    {
        var authority = new Authority(); using var owner = new NativeCommunityImportController(authority);
        RejectWorker(() => owner.Tick()); RejectWorker(() => owner.Prepare("file.ppmap", false));
        RejectWorker(() => owner.Confirm()); RejectWorker(() => owner.Cancel()); RejectWorker(() => owner.Dispose());
        var prepared = new Prepared(); Complete(owner, Begin(owner, authority), prepared);
        RejectWorker(() => owner.Confirm());
        Check(prepared.Commits == 0 && owner.Tick().CanConfirm, "off-thread confirmation cannot commit a reviewed preparation");
        Check(owner.Confirm() && prepared.CommitThread == Environment.CurrentManagedThreadId,
            "valid confirmation publishes only on original launcher owner thread");
    }
    private static void RejectWorker(Action action)
    {
        Exception? failure = Task.Run(() => { try { action(); return null; } catch (Exception error) { return error; } }).GetAwaiter().GetResult();
        Check(failure is InvalidOperationException, "import operations reject foreign thread ownership");
    }
    private static Request Begin(NativeCommunityImportController owner, Authority authority, string path = "/synthetic/map.ppmap", bool picker = false)
    {
        int previous = authority.Requests.Count;
        Check(owner.Prepare(path, picker), "synthetic package preparation starts");
        Wait(() => authority.Requests.Count == previous + 1, "injected preparation receives current work");
        return authority.Requests.Last();
    }
    private static void Complete(NativeCommunityImportController owner, Request request, Prepared? prepared)
    { request.Completion.TrySetResult(prepared); Wait(() => !owner.Tick().Busy, "owner observes current preparation outcome"); }
    private static bool Queued(NativeCommunityImportController owner)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object gate = typeof(NativeCommunityImportController).GetField("_gate", flags)!.GetValue(owner)!;
        lock (gate) return ((ICollection)typeof(NativeCommunityImportController).GetField("_done", flags)!.GetValue(owner)!).Count > 0;
    }
    private static void Wait(Func<bool> predicate, string label)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate()) { if (timer.ElapsedMilliseconds > 3000) throw new TimeoutException(label); Thread.Sleep(5); }
        Check(true, label);
    }
    private static void Check(bool pass, string label)
    { if (!pass) throw new InvalidOperationException(label); Console.WriteLine("COMMUNITY IMPORT PASS " + label); }
    private sealed record Request(string Path, bool Picker, object Context, CancellationToken Token,
        TaskCompletionSource<INativeCommunityPreparedImport?> Completion);
    private sealed class Authority : INativeCommunityImportAuthority
    {
        public bool PickerAvailable { get; set; } = true;
        internal object Context = new(); internal bool NoContext, ThrowCapture; internal int Captures;
        internal readonly ConcurrentQueue<Request> Requests = new();
        public object? CaptureContext()
        { Captures++; if (ThrowCapture) throw new InvalidOperationException("PRIVATE SECRET CONTEXT"); return NoContext ? null : Context; }
        public bool IsCurrentContext(object context) => ReferenceEquals(context, Context);
        public Task<INativeCommunityPreparedImport?> PrepareAsync(string path, bool usePicker, object context, CancellationToken token)
        {
            var completion = new TaskCompletionSource<INativeCommunityPreparedImport?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Requests.Enqueue(new(path, usePicker, context, token, completion));
            return completion.Task; // Deliberately ignores cancellation to exercise result ownership.
        }
    }
    private sealed class Prepared : INativeCommunityPreparedImport
    {
        public string Name { get; set; } = "EXPLICIT SYNTHETIC PREPARED PACKAGE";
        public string Version { get; set; } = "fixture-v1";
        public string PackageHash { get; set; } = new('a', 64);
        internal bool Current = true, FailCommit;
        internal int Commits, Disposals, CommitThread;
        public bool IsCurrent => Current;
        public void Commit()
        { Interlocked.Increment(ref Commits); CommitThread = Environment.CurrentManagedThreadId; if (FailCommit) throw new InvalidOperationException("PRIVATE SECRET COMMIT"); }
        public void Dispose() => Interlocked.Increment(ref Disposals);
    }
}
