using System.Collections.Concurrent;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

internal static class PublicProfileAcceptance
{
    internal static void Run()
    {
        ConsentAndAdmission();
        ConfirmedWriteAndFailures();
        Lifetime();
    }

    private static void ConsentAndAdmission()
    {
        var service = new Service();
        using var controller = new NativeLicenseOverviewController(service);
        Check(!controller.RequestShowPublicProfile() && !controller.ConfirmShowPublicProfile()
            && !controller.CancelShowPublicProfile(), "initial state cannot request or bypass public consent");
        foreach (NativeLicenseOverview record in new[] { Record() with { Verified = false },
            Record() with { PublicVisibility = "UNAVAILABLE" }, Record() with { PublicVisibility = "PUBLIC" },
            Record() with { PrivacyLease = 0 } })
        {
            Load(controller, service, record);
            Check(!controller.RequestShowPublicProfile() && !controller.ConfirmShowPublicProfile(),
                "public consent requires a verified private record with its current lease");
        }
        using var readOnly = new NativeLicenseOverviewController(new ReadOnly());
        readOnly.Refresh();
        Wait(readOnly, state => !state.Busy);
        Check(!readOnly.RequestShowPublicProfile(), "read-only reader cannot offer publication");

        var loaded = Load(controller, service, Record());
        Check(!controller.ConfirmShowPublicProfile() && controller.RequestShowPublicProfile()
            && controller.Current().ConfirmPublic && !controller.Current().Busy
            && controller.Current().Overview == loaded.Overview && service.Shows.Count == 0,
            "first public action only opens consent and never changes visibility or writes");
        Check(controller.CancelShowPublicProfile() && !controller.Current().ConfirmPublic
            && !controller.ConfirmShowPublicProfile() && controller.Current().Overview.PublicVisibility == "PRIVATE"
            && service.Shows.Count == 0, "dismissing consent preserves private state and prevents stale confirmation");

        controller.RequestShowPublicProfile();
        long confirmationRevision = controller.Current().Revision;
        controller.SetFace(controller.Current().Face);
        Check(!controller.Current().ConfirmPublic && controller.Current().Revision > confirmationRevision
            && !controller.ConfirmShowPublicProfile(),
            "selecting the current face also retires consent in the published snapshot");
        controller.RequestShowPublicProfile();
        controller.SetFace(NativeLicenseFace.Account);
        Check(!controller.Current().ConfirmPublic && !controller.ConfirmShowPublicProfile(),
            "changing face retires pending public consent");
        controller.RequestShowPublicProfile();
        controller.Refresh();
        Check(!controller.Current().ConfirmPublic && !controller.RequestShowPublicProfile()
            && !controller.ConfirmShowPublicProfile(), "refresh cannot carry consent across identities or busy work");
        service.TakeRead().SetResult(Record() with { DisplayName = "NEW CONSENT ACCOUNT", PrivacyLease = 987652 });
        Wait(controller, state => !state.Busy);
        Check(!controller.ConfirmShowPublicProfile() && service.Shows.Count == 0,
            "new account readback requires a new explicit public request");

        Exception? wrongThread = null;
        Task.Run(() =>
        {
            try { controller.RequestShowPublicProfile(); }
            catch (Exception ex) { wrongThread = ex; }
        }).GetAwaiter().GetResult();
        Check(wrongThread is InvalidOperationException && service.Shows.Count == 0,
            "public consent admission is owned by the UI thread");
    }

    private static void ConfirmedWriteAndFailures()
    {
        var service = new Service();
        using var controller = new NativeLicenseOverviewController(service);
        var loaded = Load(controller, service, Record());
        controller.RequestShowPublicProfile();
        Check(controller.ConfirmShowPublicProfile() && !controller.ConfirmShowPublicProfile()
            && !controller.RequestShowPublicProfile() && !controller.CancelShowPublicProfile(),
            "explicit confirmation admits one public write and retires its consent");
        var request = service.TakeShow();
        var pending = controller.Current();
        Check(request.Lease == loaded.Overview.PrivacyLease
            && request.Thread != Environment.CurrentManagedThreadId && service.Shows.Count == 0
            && service.HideCalls == 0 && pending.Busy && pending.SavingPrivacy && !pending.ConfirmPublic
            && !pending.Overview.Verified && pending.Total == 0 && pending.Rows.Count == 0
            && pending.Status.Contains("SAVING PUBLIC"),
            "public writer receives captured lease off-thread while prior account is cleared without optimistic publication");
        request.Result.SetResult(true);
        var shown = Wait(controller, state => !state.Busy);
        Check(shown.Overview == (loaded.Overview with { PublicVisibility = "PUBLIC" })
            && shown.Status.Contains("PUBLIC PREFERENCE VERIFIED") && !shown.ConfirmPublic,
            "only server-confirmed public write restores the captured record as public");

        foreach (bool throws in new[] { false, true })
        {
            Load(controller, service, Record());
            controller.RequestShowPublicProfile();
            controller.ConfirmShowPublicProfile();
            var failure = service.TakeShow();
            if (throws) failure.Result.SetException(new InvalidOperationException(
                "private-public-fixture@example.test Bearer private-public-secret"));
            else failure.Result.SetResult(false);
            var failed = Wait(controller, state => !state.Busy);
            string bindings = String.Join(" ", NativeLicenseOverviewBindings.Create(failed).Values.Select(value => value.Text));
            Check(!failed.Overview.Verified && !failed.ConfirmPublic && !failed.SavingPrivacy
                && failed.Status.Contains("PUBLIC SAVE NOT CONFIRMED") && failed.Total == 0
                && !bindings.Contains("private-public-fixture") && !bindings.Contains("private-public-secret"),
                "false or throwing public write cannot publish a claim or disclose response bodies");
        }
    }

    private static void Lifetime()
    {
        var service = new Service();
        using var controller = new NativeLicenseOverviewController(service);
        Load(controller, service, Record());
        controller.RequestShowPublicProfile();
        controller.Cancel();
        Check(!controller.Current().ConfirmPublic && !controller.ConfirmShowPublicProfile(),
            "general cancel retires unsubmitted consent");

        Start(controller, service);
        var cancelled = service.TakeShow();
        controller.Cancel();
        long revision = controller.Current().Revision;
        Check(controller.Current().Status.Contains("NOT CONFIRMED")
            && !controller.Current().Status.Contains("PRIVATE SAVE"),
            "cancelled publication acknowledges uncertain visibility without mislabeling it as a private save");
        cancelled.Result.SetResult(true);
        Drain(controller);
        Check(cancelled.Token.IsCancellationRequested && !controller.Current().Overview.Verified
            && !controller.Current().ConfirmPublic && controller.Current().Revision == revision,
            "late publication success cannot repaint a cancelled page");

        Start(controller, service);
        var superseded = service.TakeShow();
        controller.Refresh();
        service.TakeRead().SetResult(Record() with { DisplayName = "SUPERSEDING ACCOUNT", PrivacyLease = 987653 });
        var fresh = Wait(controller, state => !state.Busy);
        superseded.Result.SetResult(true);
        Drain(controller);
        Check(superseded.Token.IsCancellationRequested && controller.Current().Overview == fresh.Overview
            && !controller.Current().ConfirmPublic,
            "newer account sync fences old publication success and consent");

        controller.RequestShowPublicProfile();
        controller.ConfirmShowPublicProfile();
        var retired = service.TakeShow();
        controller.Dispose();
        retired.Result.SetResult(true);
        bool disposed = false;
        try { controller.Tick(); } catch (ObjectDisposedException) { disposed = true; }
        Check(retired.Token.IsCancellationRequested && disposed,
            "disposed page cannot apply late public preference results");
    }

    private static void Start(NativeLicenseOverviewController controller, Service service)
    {
        Load(controller, service, Record());
        Check(controller.RequestShowPublicProfile() && controller.ConfirmShowPublicProfile(),
            "fresh publication starts only through request and confirmation");
    }

    private static NativeLicenseOverview Record() => new(true, "PUBLIC CONSENT FIXTURE", "SAMUS", "",
        "PRIVATE", 0, "UNRANKED", 1, 1, 0, 4, 2, 0, 0) { PrivacyLease = 987651 };

    private static NativeLicenseOverviewController.Snapshot Load(NativeLicenseOverviewController controller,
        Service service, NativeLicenseOverview record)
    {
        controller.Refresh();
        service.TakeRead().SetResult(record);
        return Wait(controller, state => !state.Busy);
    }

    private static NativeLicenseOverviewController.Snapshot Wait(NativeLicenseOverviewController controller,
        Func<NativeLicenseOverviewController.Snapshot, bool> predicate)
    {
        for (int i = 0; i < 300; i++)
        {
            var state = controller.Tick();
            if (predicate(state)) return state;
            Thread.Sleep(10);
        }
        throw new TimeoutException("Public profile owner-thread completion timed out");
    }

    private static void Drain(NativeLicenseOverviewController controller)
    {
        for (int i = 0; i < 20; i++) { controller.Tick(); Thread.Sleep(5); }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML LICENSE PUBLIC PROFILE PASS " + label);
    }

    private sealed class ReadOnly : INativeLicenseOverviewReader
    {
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token) => Task.FromResult(Record());
    }

    private sealed record Request(long Lease, CancellationToken Token, int Thread, TaskCompletionSource<bool> Result);

    private sealed class Service : INativeLicenseOverviewReader, INativeLicensePrivacyWriter, INativeLicensePublicProfileWriter
    {
        private readonly BlockingCollection<TaskCompletionSource<NativeLicenseOverview>> _reads = new();
        internal readonly BlockingCollection<Request> Shows = new();
        internal int HideCalls;
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token)
        {
            var completion = new TaskCompletionSource<NativeLicenseOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
            _reads.Add(completion);
            return completion.Task;
        }
        public Task<bool> ShowPublicProfileAsync(long lease, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Shows.Add(new(lease, token, Environment.CurrentManagedThreadId, completion));
            return completion.Task;
        }
        public Task<bool> HidePublicProfileAsync(long lease, CancellationToken token)
        {
            Interlocked.Increment(ref HideCalls);
            throw new InvalidOperationException("Public confirmation invoked private writer");
        }
        internal TaskCompletionSource<NativeLicenseOverview> TakeRead()
            => _reads.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
                : throw new TimeoutException("Public fixture read was not dispatched");
        internal Request TakeShow()
            => Shows.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
                : throw new TimeoutException("Public fixture write was not dispatched");
    }
}
