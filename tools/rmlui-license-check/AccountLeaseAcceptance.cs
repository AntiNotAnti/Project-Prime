using System.Collections.Concurrent;
using System.Reflection;
using MphRead.Mods.Launcher;

internal static class AccountLeaseAcceptance
{
    internal static void Run()
    {
        VisibleRetirement();
        PendingPrivacy();
        LateLoads();
    }

    private static void VisibleRetirement()
    {
        foreach (Action<NativeLicenseOverviewController> entry in new Action<NativeLicenseOverviewController>[]
        {
            c => c.Current(), c => c.Tick(), c => c.SetFace(NativeLicenseFace.History),
            c => c.Page(1), c => c.HidePublicProfile(), c => c.RequestShowPublicProfile(),
            c => c.ConfirmShowPublicProfile(), c => c.CancelShowPublicProfile(),
            c => c.SubmitAccount(NativeLicenseAccountAction.LinkGoogle, "", "", "")
        })
        {
            var reader = new Reader(); using var controller = new NativeLicenseOverviewController(reader);
            Load(controller, reader);
            controller.SetFace(NativeLicenseFace.Account);
            Check(controller.RequestShowPublicProfile(), "verified private fixture can arm public confirmation");
            reader.Current = false;
            entry(controller);
            var redacted = controller.Current();
            Check(!redacted.Overview.Verified && redacted.Overview.Account == null
                && redacted.Overview.Career == null && redacted.Total == 0 && redacted.Rows.Count == 0
                && !redacted.ConfirmPublic && !redacted.CanHideProfile && !redacted.Busy
                && redacted.Status.Contains("ACCOUNT CHANGED") && reader.WriteCalls == 0,
                "every owner entry retires stale career, history, account actions and public confirmation synchronously");
        }
    }

    private static void PendingPrivacy()
    {
        foreach (bool publish in new[] { false, true })
        {
            var reader = new Reader(); using var controller = new NativeLicenseOverviewController(reader);
            Load(controller, reader);
            Check(publish ? controller.RequestShowPublicProfile() && controller.ConfirmShowPublicProfile()
                : controller.HidePublicProfile(), "captured privacy action starts on the current account");
            var write = reader.Writes.Take();
            Check(controller.Current().Busy && !controller.Current().Overview.Verified,
                "pending visibility work retains no displayed private record");
            reader.Current = false;
            var redacted = controller.Current();
            Check(write.Token.IsCancellationRequested && !redacted.Busy && !redacted.SavingPrivacy
                && !redacted.Overview.Verified && redacted.Status.Contains("ACCOUNT CHANGED"),
                "account replacement synchronously cancels captured visibility work and redacts its controls");
            write.Result.SetResult(true);
            WaitQueued(controller);
            var late = controller.Tick();
            Check(late.Revision == redacted.Revision && !late.Overview.Verified && !late.ConfirmPublic,
                "a successful late old-account privacy readback cannot repaint verified or public state");
        }
    }

    private static void LateLoads()
    {
        var reader = new Reader(); using var controller = new NativeLicenseOverviewController(reader);
        controller.Refresh(); var delayed = reader.Reads.Take(); reader.Current = false;
        delayed.SetResult(Record()); WaitQueued(controller);
        Check(!controller.Tick().Overview.Verified && controller.Current().Status.Contains("ACCOUNT CHANGED"),
            "a completed old-session load is checked on the owner before publishing any career facts");

        reader.Current = true; Load(controller, reader); controller.SetFace(NativeLicenseFace.Account);
        Check(controller.SubmitAccount(NativeLicenseAccountAction.LinkGoogle, "", "", ""),
            "current synthetic account action is admitted before its independent readback");
        reader.Writes.Take().Result.SetResult(true);
        var readback = reader.Reads.Take(); reader.Current = false; readback.SetResult(Record()); WaitQueued(controller);
        var rejected = controller.Tick();
        Check(!rejected.Overview.Verified && !rejected.Busy && rejected.Status.Contains("ACCOUNT CHANGED"),
            "late account-action readback cannot publish an obsolete auth session");

        reader.Current = true; Load(controller, reader); controller.SetFace(NativeLicenseFace.Account);
        Check(controller.SubmitAccount(NativeLicenseAccountAction.LinkGoogle, "", "", ""),
            "fresh account work can retry after explicit verified reload");
        reader.Writes.Take().Result.SetResult(true); reader.Reads.Take().SetResult(Record() with { DisplayName = "FRESH READBACK" });
        WaitQueued(controller);
        Check(controller.Tick().Overview.DisplayName == "FRESH READBACK",
            "account actions can publish a separately verified current-session readback");
    }

    private static void Load(NativeLicenseOverviewController controller, Reader reader)
    { controller.Refresh(); reader.Reads.Take().SetResult(Record()); WaitQueued(controller); controller.Tick(); }

    private static NativeLicenseOverview Record() => new(true, "SYNTHETIC PRIVATE CAREER", "SAMUS", "", "PRIVATE",
        1, "UNRANKED", 1, 1, 0, 2, 1, 0, 1)
    {
        PrivacyLease = 1, Account = new(true, true, false, false, false),
        Career = new(0, 2, 60, 1, 1, 1, 1, 0, 0, 0),
        History = new[] { new NativeLicenseMatch("PRIVATE ROOM", "BATTLE", null, true, false, true, true, 2, 1, 0, 2, 60) }
    };

    private static void WaitQueued(NativeLicenseOverviewController controller)
    {
        object queue = typeof(NativeLicenseOverviewController).GetField("_complete", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(controller)!;
        PropertyInfo count = queue.GetType().GetProperty("Count")!;
        if (!SpinWait.SpinUntil(() => (int)count.GetValue(queue)! > 0, TimeSpan.FromSeconds(3)))
            throw new TimeoutException("Synthetic License lease completion timed out.");
    }

    private static void Check(bool value, string label)
    { if (!value) throw new InvalidOperationException(label); Console.WriteLine("RML LICENSE ACCOUNT LEASE PASS " + label); }

    private sealed class Reader : INativeLicenseOverviewReader, INativeLicensePrivacyWriter,
        INativeLicensePublicProfileWriter, INativeLicenseAccountWriter
    {
        internal bool Current = true;
        internal int WriteCalls;
        internal readonly BlockingCollection<TaskCompletionSource<NativeLicenseOverview>> Reads = new();
        internal readonly BlockingCollection<(TaskCompletionSource<bool> Result, CancellationToken Token)> Writes = new();
        public bool IsCurrent(NativeLicenseOverview overview) => Current;
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token)
        { var result = new TaskCompletionSource<NativeLicenseOverview>(TaskCreationOptions.RunContinuationsAsynchronously); Reads.Add(result); return result.Task; }
        private Task<bool> Write(CancellationToken token)
        { Interlocked.Increment(ref WriteCalls); var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); Writes.Add((result, token)); return result.Task; }
        public Task<bool> HidePublicProfileAsync(long lease, CancellationToken token) => Write(token);
        public Task<bool> ShowPublicProfileAsync(long lease, CancellationToken token) => Write(token);
        public Task<bool> ExecuteAccountAsync(long lease, NativeLicenseAccountAction action, string email, string password, CancellationToken token) => Write(token);
    }
}
