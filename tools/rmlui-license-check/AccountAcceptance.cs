using System.Collections.Concurrent;
using System.Text.Json;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

internal static class AccountAcceptance
{
    private const string Email = "typed-account-fixture@example.test";
    private const string Password = "private-account-password";

    internal static void Run()
    {
        Admission();
        SuccessAndReadback();
        FailureAndLifetime();
    }

    private static void Admission()
    {
        var service = new Service();
        using var controller = new NativeLicenseOverviewController(service);
        controller.SetFace(NativeLicenseFace.Account);
        Check(!Submit(controller), "initial account state cannot dispatch actions");
        foreach (var record in new[] { Record() with { Verified = false },
            Record() with { PrivacyLease = 0 }, Record() with { Account = null } })
        {
            Load(controller, service, record);
            Check(!Submit(controller), "unverified, missing lease and missing account cannot dispatch");
        }
        Load(controller, service, Record());
        controller.SetFace(NativeLicenseFace.Overview);
        Check(!Submit(controller), "account actions require the active account face");
        controller.SetFace(NativeLicenseFace.Account);
        Check(!controller.SubmitAccount((NativeLicenseAccountAction)999, Email, Password, Password),
            "unrecognized account action is rejected");
        foreach (string invalid in new[] { "", "invalid", "a@ b.example", new string('a', 250) + "@x.test" })
            Check(!controller.SubmitAccount(NativeLicenseAccountAction.SendVerification, invalid, "", ""),
                "empty, malformed, whitespace and overlong email are rejected");
        Load(controller, service, Record() with { Account = new(true, true, false, false, false) });
        foreach (var pair in new[] { (Password: "short", Confirmation: "short"),
            (Password: new string('x', 129), Confirmation: new string('x', 129)),
            (Password, Confirmation: Password + "mismatch") })
            Check(!controller.SubmitAccount(NativeLicenseAccountAction.SetPassword, "", pair.Password, pair.Confirmation),
                "new password requires 8–128 characters and independent matching confirmation");

        Load(controller, service, Record() with { Account = new(true, false, false, false, false) });
        Check(!controller.SubmitAccount(NativeLicenseAccountAction.SetPassword, "", Password, Password),
            "password setting requires a linked email");
        Load(controller, service, Record() with { Account = new(false, true, false, false, false) });
        Check(!controller.SubmitAccount(NativeLicenseAccountAction.SetPassword, "", Password, Password),
            "guest with a pending linked email cannot set a password before becoming verified and secured");
        foreach (var pair in new[] { (Password: "", Confirmation: ""),
            (Password: new string('x', 129), Confirmation: new string('x', 129)),
            (Password: "old", Confirmation: "different") })
            Check(!controller.SubmitAccount(NativeLicenseAccountAction.Recover, Email, pair.Password, pair.Confirmation),
                "recovery rejects empty or overlong existing passwords and mismatched confirmation");
        foreach (NativeLicenseOverview record in new[]
            { Record() with { Account = new(true, true, false, false, false) }, Record() with { MatchesPlayed = 1 } })
        {
            Load(controller, service, record);
            Check(!controller.SubmitAccount(NativeLicenseAccountAction.Recover, Email, Password, Password),
                "recovery rejects secured licenses and guests with career history");
        }
        Load(controller, service, Record() with { Account = new(true, true, true, true, true) });
        foreach (NativeLicenseAccountAction action in new[] { NativeLicenseAccountAction.LinkGoogle,
            NativeLicenseAccountAction.LinkGithub, NativeLicenseAccountAction.LinkDiscord })
            Check(!controller.SubmitAccount(action, "", "", ""), "already linked provider is rejected");
        Check(service.Actions.Count == 0, "all rejected admissions avoid account service calls");

        using var readOnly = new NativeLicenseOverviewController(new ReadOnly());
        readOnly.SetFace(NativeLicenseFace.Account);
        readOnly.Refresh();
        Wait(readOnly, state => !state.Busy);
        Check(!Submit(readOnly), "read-only readers cannot execute account mutations");

        Load(controller, service, Record());
        Exception? wrongThread = null;
        Task.Run(() =>
        {
            try { Submit(controller); }
            catch (Exception ex) { wrongThread = ex; }
        }).GetAwaiter().GetResult();
        Check(wrongThread is InvalidOperationException && service.Actions.Count == 0,
            "account admission runs only on the owner thread");
    }

    private static void SuccessAndReadback()
    {
        var service = new Service();
        using var controller = new NativeLicenseOverviewController(service);
        controller.SetFace(NativeLicenseFace.Account);
        foreach (NativeLicenseAccountAction action in Enum.GetValues<NativeLicenseAccountAction>())
        {
            var before = Load(controller, service, action == NativeLicenseAccountAction.SetPassword
                ? Record() with { Account = new(true, true, false, false, false) } : Record());
            Check(controller.SubmitAccount(action, "  " + Email + "  ", Password, Password)
                && !Submit(controller), "each supported account action admits once while busy");
            var request = service.TakeAction();
            bool usesEmail = action is NativeLicenseAccountAction.SendVerification or NativeLicenseAccountAction.Recover;
            bool usesPassword = action is NativeLicenseAccountAction.SetPassword or NativeLicenseAccountAction.Recover;
            Check(request.Action == action && request.Lease == before.Overview.PrivacyLease
                && request.Email == (usesEmail ? Email : "")
                && request.Password == (usesPassword ? Password : "")
                && request.Thread != Environment.CurrentManagedThreadId && service.Actions.Count == 0,
                "writer receives captured lease, selected action and only its required bounded credentials off-thread");
            var pending = controller.Current();
            Check(pending.Busy && !pending.Overview.Verified && pending.Overview.Account == null
                && pending.Total == 0 && pending.Rows.Count == 0,
                "account work immediately clears prior identity and career display");
            Redacted(pending);
            request.Result.SetResult(true);
            var readback = service.TakeRead();
            Check(controller.Tick().Busy && !controller.Current().Overview.Verified,
                "accepted account action waits for authoritative readback");
            readback.SetResult(Record() with { DisplayName = "FRESH ACCOUNT FIXTURE", Kills = 17,
                PrivacyLease = 4567892, Account = new(true, true, false, false, false) });
            var saved = Wait(controller, state => !state.Busy);
            Check(saved.Overview.Verified && saved.Overview.DisplayName == "FRESH ACCOUNT FIXTURE"
                && saved.Overview.Kills == 17 && saved.Overview.PrivacyLease == 4567892
                && saved.Overview.Account?.Secure == true,
                "successful account action replaces all career and account facts with fresh readback");
            Redacted(saved);
        }
        foreach (int size in new[] { 8, 128 })
        {
            Load(controller, service, Record() with { Account = new(true, true, false, false, false) });
            string password = new('b', size);
            Check(controller.SubmitAccount(NativeLicenseAccountAction.SetPassword, "", password, password),
                "password bounds are inclusive at " + size);
            service.TakeAction().Result.SetResult(false);
            Wait(controller, state => !state.Busy);
        }
        foreach (int size in new[] { 1, 7, 128 })
        {
            Load(controller, service, Record());
            string existingPassword = new('r', size);
            Check(controller.SubmitAccount(NativeLicenseAccountAction.Recover, Email, existingPassword, existingPassword),
                "recovery preserves existing password compatibility at " + size + " characters");
            var request = service.TakeAction();
            Check(request.Action == NativeLicenseAccountAction.Recover && request.Password == existingPassword,
                "recovery forwards the existing password unchanged instead of imposing new-password policy");
            request.Result.SetResult(false);
            Wait(controller, state => !state.Busy);
        }
    }

    private static void FailureAndLifetime()
    {
        var service = new Service();
        using var controller = new NativeLicenseOverviewController(service);
        controller.SetFace(NativeLicenseFace.Account);
        foreach (bool throws in new[] { false, true })
        {
            Load(controller, service, Record());
            Submit(controller);
            var request = service.TakeAction();
            if (throws) request.Result.SetException(new InvalidOperationException(Email + " Bearer " + Password));
            else request.Result.SetResult(false);
            var failed = Wait(controller, state => !state.Busy);
            Check(!failed.Overview.Verified && failed.Total == 0
                && failed.Status.Contains("NOT CONFIRMED") && service.Reads.Count == 0,
                "false and throwing account writers fail closed without invented readback");
            Redacted(failed);
        }
        Load(controller, service, Record());
        Submit(controller);
        service.TakeAction().Result.SetResult(true);
        service.TakeRead().SetResult(Record() with { Verified = false, Status = Email + Password });
        var offline = Wait(controller, state => !state.Busy);
        Check(!offline.Overview.Verified && !offline.Status.Contains("ACCEPTED")
            && !offline.Status.Contains("RECOVERED"),
            "offline readback cannot claim a refreshed or recovered account");
        Redacted(offline);

        Load(controller, service, Record());
        Submit(controller);
        service.TakeAction().Result.SetResult(true);
        service.TakeRead().SetException(new InvalidOperationException(Email + " Bearer " + Password));
        var readFailure = Wait(controller, state => !state.Busy);
        Check(!readFailure.Overview.Verified && readFailure.Status.Contains("NOT CONFIRMED"),
            "throwing post-action readback cannot claim confirmation");
        Redacted(readFailure);

        Load(controller, service, Record());
        Submit(controller);
        var cancelled = service.TakeAction();
        controller.Cancel();
        long revision = controller.Current().Revision;
        cancelled.Result.SetResult(true);
        Drain(controller);
        Check(cancelled.Token.IsCancellationRequested && !controller.Current().Overview.Verified
            && !controller.Current().Busy && controller.Current().Revision == revision && service.Reads.Count == 0,
            "cancelled account action ignores late acceptance and does not launch readback");

        Load(controller, service, Record());
        Submit(controller);
        service.TakeAction().Result.SetResult(true);
        var cancelledRead = service.TakeRead();
        controller.Cancel();
        cancelledRead.SetResult(Record() with { DisplayName = "LATE READBACK" });
        Drain(controller);
        Check(!controller.Current().Overview.Verified && !controller.Current().Busy,
            "cancellation also fences an already running account readback");

        Load(controller, service, Record());
        Submit(controller);
        var superseded = service.TakeAction();
        controller.Refresh();
        service.TakeRead().SetResult(Record() with { DisplayName = "NEWER ACCOUNT", PrivacyLease = 4567893 });
        var newer = Wait(controller, state => !state.Busy);
        superseded.Result.SetResult(true);
        Drain(controller);
        Check(superseded.Token.IsCancellationRequested && controller.Current().Overview == newer.Overview
            && service.Reads.Count == 0, "newer sync fences an older accepted account mutation");

        Submit(controller);
        var retired = service.TakeAction();
        controller.Dispose();
        retired.Result.SetResult(true);
        bool disposed = false;
        try { controller.Tick(); } catch (ObjectDisposedException) { disposed = true; }
        Check(retired.Token.IsCancellationRequested && disposed,
            "disposed account page cannot receive late action results");
    }

    private static bool Submit(NativeLicenseOverviewController controller)
        => controller.SubmitAccount(NativeLicenseAccountAction.SendVerification, Email, "", "");

    private static NativeLicenseOverview Record() => new(true, "ACCOUNT FIXTURE", "SAMUS", "",
        "PRIVATE", 0, "UNRANKED", 0, 0, 0, 0, 0, 0, 0)
        { PrivacyLease = 4567891, Account = new(false, true, false, false, false) };

    private static NativeLicenseOverviewController.Snapshot Load(NativeLicenseOverviewController controller,
        Service service, NativeLicenseOverview record)
    {
        controller.Refresh();
        service.TakeRead().SetResult(record);
        return Wait(controller, state => !state.Busy);
    }

    private static void Redacted(NativeLicenseOverviewController.Snapshot state)
    {
        string display = JsonSerializer.Serialize(state) + String.Join(" ",
            NativeLicenseOverviewBindings.Create(state).Values.Select(value => value.Text));
        Check(!display.Contains(Email) && !display.Contains(Password)
            && !display.Contains("Bearer"), "credentials never enter controller snapshots or native bindings");
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
        throw new TimeoutException("Account owner-thread completion timed out");
    }

    private static void Drain(NativeLicenseOverviewController controller)
    {
        for (int i = 0; i < 20; i++) { controller.Tick(); Thread.Sleep(5); }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML LICENSE ACCOUNT PASS " + label);
    }

    private sealed class ReadOnly : INativeLicenseOverviewReader
    {
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token) => Task.FromResult(Record());
    }

    private sealed record Request(long Lease, NativeLicenseAccountAction Action, string Email,
        string Password, CancellationToken Token, int Thread, TaskCompletionSource<bool> Result);

    private sealed class Service : INativeLicenseOverviewReader, INativeLicenseAccountWriter
    {
        internal readonly BlockingCollection<TaskCompletionSource<NativeLicenseOverview>> Reads = new();
        internal readonly BlockingCollection<Request> Actions = new();
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token)
        {
            var completion = new TaskCompletionSource<NativeLicenseOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
            Reads.Add(completion);
            return completion.Task;
        }
        public Task<bool> ExecuteAccountAsync(long lease, NativeLicenseAccountAction action,
            string email, string password, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Actions.Add(new(lease, action, email, password, token, Environment.CurrentManagedThreadId, completion));
            return completion.Task;
        }
        internal TaskCompletionSource<NativeLicenseOverview> TakeRead()
            => Reads.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
                : throw new TimeoutException("Account fixture read was not dispatched");
        internal Request TakeAction()
            => Actions.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
                : throw new TimeoutException("Account fixture action was not dispatched");
    }
}
