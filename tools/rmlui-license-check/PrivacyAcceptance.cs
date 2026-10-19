using System.Collections.Concurrent;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

internal static class PrivacyAcceptance
{
    internal static void Run()
    {
        AdmissionAndSuccess();
        FailuresAndLifetime();
        GuardTransaction().GetAwaiter().GetResult();
    }

    private static void AdmissionAndSuccess()
    {
        var service = new Writer();
        using var controller = new NativeLicenseOverviewController(service);
        Check(!controller.Current().CanHideProfile && !controller.HidePublicProfile()
            && Flag(controller.Current(), "disabled:native_license_hide_profile"),
            "initial privacy is unavailable and cannot write");
        foreach (var unavailable in new[]
        {
            Record() with { Verified = false }, Record() with { PublicVisibility = "UNAVAILABLE" },
            Record() with { PublicVisibility = "unexpected" }, Record() with { PrivacyLease = 0 }
        })
        {
            var state = Load(controller, service, unavailable);
            Check(!state.CanHideProfile && !controller.HidePublicProfile()
                && Flag(state, "disabled:native_license_hide_profile"),
                "offline, unknown visibility and missing leases cannot write");
        }
        Check(service.Writes.Count == 0, "rejected privacy admissions dispatch no writer");
        using (var readOnly = new NativeLicenseOverviewController(new ReadOnly()))
        {
            readOnly.Refresh();
            Wait(readOnly, s => !s.Busy);
            Check(!readOnly.Current().CanHideProfile && !readOnly.HidePublicProfile(),
                "read-only readers cannot admit privacy actions");
        }
        foreach (string visibility in new[] { "PUBLIC", "PRIVATE" })
        {
            var loaded = Load(controller, service, Record() with { PublicVisibility = visibility });
            Check(loaded.CanHideProfile && !Flag(loaded, "disabled:native_license_hide_profile")
                && Text(loaded, "native_license_hide_profile") == (visibility == "PRIVATE"
                    ? "SAVE PRIVATE PREFERENCE" : "HIDE PUBLIC PROFILE"),
                "both confirmed public and private records offer a fixed opt-out");
            Check(controller.HidePublicProfile() && !controller.HidePublicProfile(),
                "privacy save admits once and rejects duplicate clicks");
            var request = service.TakeWrite();
            var pending = controller.Current();
            Check(request.Lease == loaded.Overview.PrivacyLease
                && request.Thread != Environment.CurrentManagedThreadId && service.Writes.Count == 0,
                "writer receives the captured opaque lease off the UI thread exactly once");
            Check(pending.Busy && pending.SavingPrivacy && !pending.CanHideProfile
                && !pending.Overview.Verified && pending.Total == 0 && pending.Rows.Count == 0
                && pending.Overview.PublicVisibility != "PRIVATE"
                && Text(pending, "native_license_privacy_status").Contains("WAITING FOR SERVER")
                && Text(pending, "native_license_name") == "LOCAL HUNTER",
                "pending save retires account and history without optimistic private state");
            request.Result.SetResult(true);
            var saved = Wait(controller, s => !s.Busy);
            Check(saved.Overview == (loaded.Overview with { PublicVisibility = "PRIVATE" })
                && !saved.SavingPrivacy && saved.CanHideProfile && saved.Status.Contains("VERIFIED")
                && Text(saved, "native_license_privacy_status").Contains("PRIVATE PREFERENCE VERIFIED")
                && Text(saved, "native_license_hide_profile") == "SAVE PRIVATE PREFERENCE",
                "confirmed save restores the exact captured account record as private");
            Check(!String.Join(" ", NativeLicenseOverviewBindings.Create(saved).Values.Select(x => x.Text))
                .Contains(loaded.Overview.PrivacyLease.ToString()),
                "opaque privacy lease never reaches native text bindings");
        }
        Exception? wrongThread = null;
        Task.Run(() =>
        {
            try { controller.HidePublicProfile(); }
            catch (Exception ex) { wrongThread = ex; }
        }).GetAwaiter().GetResult();
        Check(wrongThread is InvalidOperationException && service.Writes.Count == 0,
            "privacy admission enforces UI owner thread before dispatch");
    }

    private static void FailuresAndLifetime()
    {
        var service = new Writer();
        using var controller = new NativeLicenseOverviewController(service);
        foreach (bool throws in new[] { false, true })
        {
            Load(controller, service, Record());
            controller.HidePublicProfile();
            var request = service.TakeWrite();
            if (throws) request.Result.SetException(new InvalidOperationException(
                "private@example.test Bearer private-secret 00000000-0000-0000-0000-000000000001"));
            else request.Result.SetResult(false);
            var failed = Wait(controller, s => !s.Busy);
            string display = String.Join(" ", NativeLicenseOverviewBindings.Create(failed).Values.Select(x => x.Text));
            Check(!failed.Overview.Verified && !failed.CanHideProfile && !failed.SavingPrivacy
                && failed.Status.Contains("PRIVATE SAVE NOT CONFIRMED")
                && !display.Contains("private@example") && !display.Contains("private-secret")
                && !display.Contains("00000000-0000") && failed.Total == 0,
                "false or throwing writer fails closed and redacts service bodies");
        }
        Load(controller, service, Record());
        controller.HidePublicProfile();
        var cancelled = service.TakeWrite();
        controller.Cancel();
        Check(cancelled.Token.IsCancellationRequested && !controller.Current().Busy
            && controller.Current().Status.Contains("PRIVATE SAVE NOT CONFIRMED"),
            "cancel stops waiting without promising privacy rollback");
        long cancelledRevision = controller.Current().Revision;
        cancelled.Result.SetResult(true);
        Drain(controller);
        Check(!controller.Current().Overview.Verified && controller.Current().Revision == cancelledRevision,
            "late success after cancelled save cannot repaint private account");

        Load(controller, service, Record());
        controller.HidePublicProfile();
        var superseded = service.TakeWrite();
        controller.Refresh();
        var fresh = service.TakeLoad();
        Check(superseded.Token.IsCancellationRequested && !controller.Current().SavingPrivacy
            && !controller.HidePublicProfile(), "refresh supersedes saving and rejects writes during its load");
        fresh.SetResult(Record() with { DisplayName = "RECOVERED ACCOUNT", PrivacyLease = 88888882,
            Kills = 800, PublicVisibility = "PUBLIC" });
        var recovered = Wait(controller, s => !s.Busy);
        superseded.Result.SetResult(true);
        Drain(controller);
        Check(controller.Current().Overview == recovered.Overview
            && controller.Current().Overview.PublicVisibility == "PUBLIC",
            "late saved account cannot replace superseding account refresh");

        controller.HidePublicProfile();
        var retired = service.TakeWrite();
        controller.Dispose();
        Check(retired.Token.IsCancellationRequested, "page disposal cancels pending privacy work");
        retired.Result.SetResult(true);
        bool disposed = false;
        try { controller.Tick(); } catch (ObjectDisposedException) { disposed = true; }
        Check(disposed, "retired controller rejects snapshots after late save completion");
    }

    private static async Task GuardTransaction()
    {
        Guid owner = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid other = Guid.Parse("00000000-0000-0000-0000-000000000002");
        foreach (var identity in new[]
            { (Expected: Guid.Empty, Actual: owner), (Expected: owner, Actual: other),
                (Expected: owner, Actual: Guid.Empty) })
        {
            var calls = new List<string>();
            bool result = await HunterLicensePrivacyWrite.HideAsync(identity.Expected,
                _ => { calls.Add("owner"); return Task.FromResult(identity.Actual); },
                _ => { calls.Add("write"); return Task.FromResult(false); },
                _ => { calls.Add("read"); return Task.FromResult(false); }, CancellationToken.None);
            Check(!result && !calls.Contains("write") && !calls.Contains("read")
                && (identity.Expected != Guid.Empty || calls.Count == 0),
                "empty or mismatched expected account never persists private preference");
        }
        foreach (var scenario in new[]
        {
            (WrittenPublic: false, ReadPublic: false, Success: true, ReadCount: 1),
            (WrittenPublic: true, ReadPublic: false, Success: false, ReadCount: 0),
            (WrittenPublic: false, ReadPublic: true, Success: false, ReadCount: 1)
        })
        {
            var calls = new List<string>();
            bool result = await HunterLicensePrivacyWrite.HideAsync(owner,
                _ => { calls.Add("owner"); return Task.FromResult(owner); },
                _ => { calls.Add("write"); return Task.FromResult(scenario.WrittenPublic); },
                _ => { calls.Add("read"); return Task.FromResult(scenario.ReadPublic); }, CancellationToken.None);
            Check(result == scenario.Success && calls.SequenceEqual(scenario.ReadCount == 1
                ? new[] { "owner", "write", "read" } : new[] { "owner", "write" }),
                "guard requires ordered matching owner, private write and private readback");
        }
        using var cts = new CancellationTokenSource();
        int reads = 0;
        bool cancelled = false;
        try
        {
            await HunterLicensePrivacyWrite.HideAsync(owner, _ => Task.FromResult(owner),
                _ => { cts.Cancel(); return Task.FromResult(false); },
                _ => { reads++; return Task.FromResult(false); }, cts.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && reads == 0, "cancellation after persistence cannot read back or claim success");
    }

    private static NativeLicenseOverview Record() => new(true, "PRIVACY FIXTURE", "SAMUS", "",
        "PUBLIC", 1750, "TIER 2", 20, 15, 5, 123, 27, 11, 1)
    {
        PrivacyLease = 77777771,
        Career = new(0, 2000, 216000, 50, 9, 2, 5, 6, 7, 8),
        History = Array.AsReadOnly(new[] { new NativeLicenseMatch("FIXTURE MAP", "BATTLE", null,
            true, false, true, true, 10, 2, 3, 100, 3660) })
    };

    private static NativeLicenseOverviewController.Snapshot Load(NativeLicenseOverviewController controller,
        Writer service, NativeLicenseOverview record)
    {
        controller.Refresh();
        service.TakeLoad().SetResult(record);
        return Wait(controller, s => !s.Busy);
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
        throw new TimeoutException("Privacy owner-thread completion timed out");
    }

    private static void Drain(NativeLicenseOverviewController controller)
    {
        // Deliberately cancellation-ignoring fixture replies can arrive after the UI epoch retires.
        for (int i = 0; i < 20; i++) { controller.Tick(); Thread.Sleep(5); }
    }

    private static string Text(NativeLicenseOverviewController.Snapshot state, string id)
        => NativeLicenseOverviewBindings.Create(state)[id].Text;
    private static bool Flag(NativeLicenseOverviewController.Snapshot state, string id)
        => NativeLicenseOverviewBindings.Create(state)[id].Boolean;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new InvalidOperationException(label);
        Console.WriteLine("RML LICENSE PRIVACY PASS " + label);
    }

    private sealed class ReadOnly : INativeLicenseOverviewReader
    {
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token) => Task.FromResult(Record());
    }

    private sealed record WriteRequest(long Lease, CancellationToken Token, int Thread,
        TaskCompletionSource<bool> Result);

    private sealed class Writer : INativeLicenseOverviewReader, INativeLicensePrivacyWriter
    {
        private readonly BlockingCollection<TaskCompletionSource<NativeLicenseOverview>> _loads = new();
        internal readonly BlockingCollection<WriteRequest> Writes = new();
        public Task<NativeLicenseOverview> LoadAsync(CancellationToken token)
        {
            var completion = new TaskCompletionSource<NativeLicenseOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
            _loads.Add(completion);
            return completion.Task;
        }
        public Task<bool> HidePublicProfileAsync(long lease, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Writes.Add(new(lease, token, Environment.CurrentManagedThreadId, completion));
            return completion.Task;
        }
        internal TaskCompletionSource<NativeLicenseOverview> TakeLoad()
            => _loads.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
                : throw new TimeoutException("Privacy fixture load was not dispatched");
        internal WriteRequest TakeWrite()
            => Writes.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
                : throw new TimeoutException("Privacy fixture write was not dispatched");
    }
}
