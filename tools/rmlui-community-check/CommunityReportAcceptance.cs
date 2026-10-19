using System.Collections.Concurrent;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;

internal static class CommunityReportAcceptance
{
    public static void Run()
    {
        Check(RmlUiPageCommandRouting.Community(RmlUiIntentKind.CommunityReport),
            "root dispatch delegates explicit report submission to the active Community page");
        Check(RmlUiPageCommandRouting.Community(RmlUiIntentKind.CommunityReportReason),
            "root dispatch delegates report reason changes to the active Community page");
        ValidationAndExactSubmission();
        FailuresAndRetry();
        AccountRetirement();
        CancellationAndDisposal();
        OwnerThread();
        Console.WriteLine("RML COMMUNITY REPORT ACCEPTANCE COMPLETE");
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException(label);
        Console.WriteLine("RML COMMUNITY REPORT PASS " + label);
    }
    private static NativeCommunityEntry Row(string name = "Report selection")
        => new(Guid.NewGuid(), name, "Creator", new string('a', 64), 7, "Battle", 2, 8)
        { Version = "release-7", Installed = false };
    private static NativeCommunityReportController.View Wait(NativeCommunityReportController controller,
        Func<NativeCommunityReportController.View, bool> ready)
    {
        for (int i = 0; i < 200; i++)
        {
            var view = controller.Tick();
            if (ready(view)) return view;
            Thread.Sleep(5);
        }
        throw new TimeoutException("Community report controller did not settle");
    }
    private static void Drain(NativeCommunityReportController controller)
    { for (int i = 0; i < 30; i++) { controller.Tick(); Thread.Sleep(2); } }
    private static void Ready(NativeCommunityReportController controller, Authority authority,
        NativeCommunityEntry selection, object? witness = null)
    {
        Check(controller.Open(selection, witness), "valid immutable selection opens report preparation");
        authority.TakePrepare().Result.SetResult(authority.Lease);
        Wait(controller, s => s.CanSubmit);
    }

    private static void ValidationAndExactSubmission()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityReportController(authority);
        var selection = Row();
        Check(!controller.Open(selection with { MapId = Guid.Empty }, null)
            && !controller.Open(selection with { LatestHash = new string('g', 64) }, null)
            && !controller.Open(selection with { Installed = true }, null)
            && !controller.Open(selection with { Version = new string('v', 65) }, null)
            && !controller.Open(selection with { Version = null! }, null)
            && authority.Prepares.IsEmpty,
            "invalid identities installed rows and invalid version bounds never contact report authority");
        Check(controller.Open(selection with { Version = new string('v', 64) }, null),
            "sixty-four character package version remains valid");
        var retiredBoundary = authority.TakePrepare();
        controller.Cancel();
        retiredBoundary.Result.SetResult(authority.Lease);
        object witness = new();
        Check(controller.Open(selection, witness), "selected report begins account verification");
        var prepare = authority.TakePrepare();
        Check(prepare.Selection == selection && ReferenceEquals(prepare.Witness, witness),
            "prepare captures exact immutable map selection and opaque catalog witness");
        Check(controller.Tick().Busy && !controller.Tick().CanSubmit && !controller.Tick().Submitted
            && !controller.Submit("too early") && !controller.Reason(1),
            "unverified account cannot change reason or submit a report");
        prepare.Result.SetResult(authority.Lease);
        var verified = Wait(controller, s => s.CanSubmit);
        Check(verified.Selection == selection && verified.Reason == 0 && !verified.Submitted,
            "verified account enables explicit submission without reporting optimistically");
        Check(!controller.Reason(-1) && !controller.Reason(6) && controller.Reason(5),
            "report reason accepts exactly the six authoritative reason choices");
        Check(!controller.Submit(null!) && !controller.Submit(new string('x', 4001))
            && authority.Submissions.IsEmpty, "null or oversized details are rejected before authority call");
        string details = new('d', 4000);
        Check(controller.Submit(details) && !controller.Submit("duplicate") && !controller.Reason(0),
            "one pending report blocks duplicate submission and reason changes");
        var submit = authority.TakeSubmit();
        Check(submit.Selection.MapId == selection.MapId
            && submit.Selection.LatestHash == selection.LatestHash
            && submit.Selection.Version == selection.Version
            && submit.Reason == MapCreatorCatalog.ReportReasons[5]
            && submit.Details == details && ReferenceEquals(submit.Lease, authority.Lease),
            "submit captures exact map version hash reason boundary details and verified account lease");
        Check(controller.Tick().Busy && !controller.Tick().Submitted && !controller.Tick().CanSubmit,
            "pending authority acknowledgement cannot show report submitted");
        submit.Result.SetResult();
        var done = Wait(controller, s => s.Submitted);
        Check(!done.Busy && !done.CanSubmit && !controller.Submit("again") && authority.Submissions.IsEmpty,
            "acknowledged report becomes submitted once and blocks another send");
    }

    private static void FailuresAndRetry()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityReportController(authority);
        controller.Open(Row(), null);
        authority.TakePrepare().Result.SetException(new InvalidOperationException("secret-ticket /private/account"));
        var unavailable = Wait(controller, s => !s.Busy);
        Check(!unavailable.CanSubmit && !unavailable.Submitted && unavailable.Status.Contains("CLOSE AND REOPEN")
            && !unavailable.Status.Contains("secret") && !unavailable.Status.Contains("/private")
            && !controller.Submit("retry without verification"),
            "prepare failure is redacted and requires reopened verification before submission");
        controller.Cancel();
        Ready(controller, authority, Row("Retry selected"));
        controller.Reason(1);
        controller.Submit("optional explanation");
        authority.TakeSubmit().Result.SetException(new InvalidOperationException("secret-ticket /private/report"));
        var retry = Wait(controller, s => !s.Busy);
        Check(retry.CanSubmit && !retry.Submitted && retry.Status.Contains("RETRY")
            && !retry.Status.Contains("secret") && !retry.Status.Contains("/private"),
            "submit failure is redacted and retains a verified retry without optimistic success");
        Check(controller.Submit(""), "empty optional report details can retry a failed submission");
        var repeated = authority.TakeSubmit();
        Check(repeated.Details == "" && repeated.Reason == MapCreatorCatalog.ReportReasons[1],
            "retry captures current explicit details and selected authoritative reason");
        repeated.Result.SetResult();
        Wait(controller, s => s.Submitted);
    }

    private static void AccountRetirement()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityReportController(authority);
        Ready(controller, authority, Row());
        controller.Submit("captured under old account");
        var pending = authority.TakeSubmit();
        authority.Lease = new object();
        var retired = controller.Tick();
        Check(retired.Selection == null && !retired.CanSubmit && !retired.Busy && !retired.Submitted
            && pending.Token.IsCancellationRequested && !controller.Submit("stale account"),
            "account retirement clears selection and lease and cancels the pending submission");
        pending.Result.SetResult();
        Drain(controller);
        Check(controller.Tick().Selection == null && !controller.Tick().Submitted,
            "late successful submission from a retired account cannot restore report state");
        controller.Open(Row("Prepare retired"), null);
        var preparing = authority.TakePrepare();
        object oldLease = authority.Lease;
        authority.Lease = new object();
        preparing.Result.SetResult(oldLease);
        retired = Wait(controller, s => !s.Busy);
        Check(retired.Selection == null && !retired.CanSubmit && !retired.Submitted,
            "prepare completion carrying an outdated account lease retires its selection");
    }

    private static void CancellationAndDisposal()
    {
        var authority = new Authority();
        var controller = new NativeCommunityReportController(authority);
        controller.Open(Row("Canceled preparation"), null);
        var canceled = authority.TakePrepare();
        controller.Cancel();
        Check(canceled.Token.IsCancellationRequested, "closing report cancels account preparation");
        Ready(controller, authority, Row("Current report"));
        canceled.Result.SetResult(authority.Lease);
        Drain(controller);
        Check(controller.Tick().Selection?.Name == "Current report" && controller.Tick().CanSubmit,
            "late canceled preparation cannot replace a newer verified selection");
        controller.Submit("canceled send");
        var send = authority.TakeSubmit();
        controller.Cancel();
        send.Result.SetResult();
        Drain(controller);
        Check(send.Token.IsCancellationRequested && controller.Tick().Selection == null
            && !controller.Tick().Submitted, "canceled submission acknowledgement cannot publish submitted state");
        Ready(controller, authority, Row("Disposed send"));
        controller.Submit("disposed send");
        var disposedSend = authority.TakeSubmit();
        controller.Dispose();
        disposedSend.Result.SetResult();
        bool disposed = false;
        try { controller.Tick(); } catch (ObjectDisposedException) { disposed = true; }
        Check(disposedSend.Token.IsCancellationRequested && disposed,
            "disposal cancels pending submission and prevents all completion projection");
        controller.Dispose();
        var preparingController = new NativeCommunityReportController(authority);
        preparingController.Open(Row(), null);
        var disposedPrepare = authority.TakePrepare();
        preparingController.Dispose();
        disposedPrepare.Result.SetResult(authority.Lease);
        Check(disposedPrepare.Token.IsCancellationRequested, "disposal also cancels pending account preparation");
    }

    private static void OwnerThread()
    {
        var authority = new Authority();
        using var controller = new NativeCommunityReportController(authority);
        foreach (Action action in new Action[] { () => controller.Open(Row(), null),
            () => controller.Reason(0), () => controller.Submit(""), () => controller.Tick(), () => controller.Cancel(), () => controller.Dispose() })
        {
            Exception? failure = null;
            Task.Run(() => { try { action(); } catch (Exception ex) { failure = ex; } }).GetAwaiter().GetResult();
            Check(failure is InvalidOperationException, "report state operations require the launcher owner thread");
        }
    }

    private sealed record PrepareCall(NativeCommunityEntry Selection, object? Witness, CancellationToken Token)
    { public TaskCompletionSource<object> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed record SubmitCall(NativeCommunityEntry Selection, string Reason, string Details, object Lease, CancellationToken Token)
    { public TaskCompletionSource Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); }
    private sealed class Authority : INativeCommunityReportAuthority
    {
        public object Lease = new();
        public ConcurrentQueue<PrepareCall> Prepares { get; } = new();
        public ConcurrentQueue<SubmitCall> Submissions { get; } = new();
        public bool IsCurrentLease(object lease) => ReferenceEquals(lease, Lease);
        public Task<object> PrepareAsync(NativeCommunityEntry selection, object? witness, CancellationToken token)
        {
            var call = new PrepareCall(selection, witness, token); Prepares.Enqueue(call);
            return call.Result.Task; // Ignore cancellation deliberately: controller must fence late results.
        }
        public Task SubmitAsync(NativeCommunityEntry selection, string reason, string details, object lease, CancellationToken token)
        {
            var call = new SubmitCall(selection, reason, details, lease, token); Submissions.Enqueue(call);
            return call.Result.Task;
        }
        public PrepareCall TakePrepare() => Take(Prepares);
        public SubmitCall TakeSubmit() => Take(Submissions);
        private static T Take<T>(ConcurrentQueue<T> queue)
        {
            for (int i = 0; i < 200; i++)
            { if (queue.TryDequeue(out var call)) return call; Thread.Sleep(5); }
            throw new TimeoutException("Expected synthetic Community report authority call");
        }
    }
}
