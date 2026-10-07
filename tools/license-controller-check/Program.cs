using System.Collections.Immutable;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;

#if MPHREAD_RMLUI_POC
if (args is ["--native", var nativeLibrary, var nativeAssets]) { NativeLicenseLayoutCheck.Run(nativeLibrary, nativeAssets); return; }
#endif

int checks = 0;
void Check(bool value, string name) { if (!value) throw new InvalidOperationException(name); checks++; }
var backend = new FakeLicense();
using (var controller = new LicenseController(backend))
{
    var initial = controller.Snapshot;
    controller.Pump();
    Check(ReferenceEquals(initial, controller.Snapshot), "unchanged License snapshot retains identity and version");
    Check(!initial.CanRecover && !initial.Data.Connected && initial.History.IsEmpty, "local offline profile cannot switch identities");
    Check(controller.Refresh().Accepted && controller.Snapshot.Loading, "explicit sync starts asynchronous profile load");
    Check(controller.Refresh().Accepted && backend.Loads[0].Token.IsCancellationRequested,
        "new sync cancels previous request and retains presentation owner");
    backend.Loads[0].Complete(FakeLicense.Career(999)); controller.Pump();
    Check(controller.Snapshot.Loading && controller.Snapshot.Data.Stats.GamesPlayed == 0,
        "superseded profile completion cannot publish");
    backend.Loads[1].Complete(FakeLicense.Career(250)); controller.Pump();
    var accepted = controller.Snapshot;
    Check(!accepted.Loading && accepted.Data.Connected && accepted.Data.Stats.GamesPlayed == 250,
        "current authoritative read model publishes through owner Pump");
    Check(accepted.Data.Matches.Length == 25 && accepted.HistoryPageCount == 4 && accepted.History.Length == 8,
        "latest accepted history is bounded to existing service limit and paged locally");
    Check(accepted.CareerMetrics.Length == 19 && accepted.Comparison.Length == 5,
        "native projection preserves every legacy career and comparison metric");
    Check(accepted.Achievements.Length == 7 && accepted.Achievements.All(value => value.Earned),
        "all seven accepted-career milestones preserve thresholds");
    Check(accepted.RecentForm.StartsWith("T / W / L / T / W") && accepted.Rank.Contains("TIER 2"),
        "recent form and rank derive from accepted profile rather than local match simulation");
    Check(!accepted.EmblemsAvailable && !accepted.TitlesAvailable && !accepted.PublicComparisonAvailable
        && accepted.EmblemsUnavailableReason.Length > 0 && accepted.PublicComparisonUnavailableReason.Length > 0,
        "missing service catalogs remain explicitly unavailable");
    Check(controller.ChangeHistoryPage(1).Accepted && controller.Snapshot.History[0].MatchId == "match8",
        "history page advances without repeating or losing accepted rows");
    controller.ChangeHistoryPage(1); controller.ChangeHistoryPage(1);
    Check(controller.Snapshot.HistoryPageIndex == 3 && controller.Snapshot.History.Length == 1,
        "last history page contains remaining accepted match");
    Check(!controller.ChangeHistoryPage(1).Accepted && controller.Snapshot.CommandError.Length > 0,
        "history boundary is rejected with visible reason");
    controller.Pump();
    Check(controller.Snapshot.CommandError.Length > 0, "License action error survives presentation refresh");
    controller.SelectFace(LicenseFace.Stats);
    Check(controller.Snapshot.CommandError.Length == 0, "next accepted License action clears command error");
    controller.Refresh(); backend.Loads[^1].Complete(new() { Status = "OFFLINE // RETRY LATER" }); controller.Pump();
    Check(!controller.Snapshot.Data.Connected && controller.Snapshot.Data.Stats.GamesPlayed == 250
        && controller.Snapshot.Data.Profile.PlayerId == accepted.Data.Profile.PlayerId,
        "offline retry retains last accepted career without inventing empty statistics");
    Check(!controller.Snapshot.CanRecover, "offline cached guest is never treated as an empty recoverable account");
    controller.Refresh();
    Check(controller.CancelRefresh().Accepted && backend.Loads[^1].Token.IsCancellationRequested
        && !controller.Snapshot.Loading, "cancel sync releases loading and cancels read model request");
    backend.Loads[^1].Complete(FakeLicense.Career(5)); controller.Pump();
    Check(controller.Snapshot.Data.Stats.GamesPlayed == 250, "cancelled completion cannot overwrite retained career");
    controller.Refresh(); backend.Loads[^1].Complete(new() { Connected = true, Status = "CONNECTED", Profile = new("guest-b", "Guest") }); controller.Pump();
    Check(controller.Snapshot.HistoryPageIndex == 0 && controller.Snapshot.HistoryPageCount == 1
        && controller.Snapshot.CanRecover, "empty authoritative profile clamps pagination and permits guest recovery");
    int customizations = 0; controller.CustomizationRequested += () => customizations++;
    controller.SelectFace(LicenseFace.Customization);
    Check(customizations == 1, "License customization requests the existing Hunter selection flow");
    Check(!controller.SelectFace((LicenseFace)999).Accepted, "unknown License face is rejected");
    bool wrongThread = Task.Run(() => { try { controller.Refresh(); } catch (InvalidOperationException) { return true; } return false; }).GetAwaiter().GetResult();
    Check(wrongThread, "workers cannot mutate License owner");
}

backend = new FakeLicense();
using (var controller = new LicenseController(backend))
{
    controller.SetCredentials("player@example.test", "secret-dummy", "different-dummy");
    Check(!controller.PerformAccount(LicenseAccountAction.FinishPassword).Accepted && backend.Accounts.Count == 0,
        "password confirmation remains independent and rejects mismatch before authority call");
    controller.SetCredentials("invalid", "secret-dummy", "secret-dummy");
    Check(!controller.PerformAccount(LicenseAccountAction.SendVerification).Accepted,
        "email action rejects invalid address before service call");
    Check(!controller.SetCredentials(new string('a', 255), "", "").Accepted
        && !controller.SetCredentials("", new string('x', 129), "").Accepted,
        "account input limits match legacy fields");
    controller.SetCredentials("player@example.test", "short", "short");
    Check(!controller.PerformAccount(LicenseAccountAction.FinishPassword).Accepted,
        "setting password requires existing minimum length");
    controller.SetCredentials("player@example.test", "secret-dummy", "secret-dummy");
    Check(!controller.PerformAccount(LicenseAccountAction.Recover).Accepted && backend.Accounts.Count == 0,
        "offline recovery cannot replace guest credential");
    controller.Refresh(); backend.Loads[^1].Complete(FakeLicense.Career(1)); controller.Pump();
    Check(!controller.PerformAccount(LicenseAccountAction.Recover).Accepted
        && controller.Snapshot.CommandError.Contains("career history"), "guest with career history cannot switch identities");
    controller.Refresh(); backend.Loads[^1].Complete(new() { Connected = true, Profile = new("guest", "Guest") }); controller.Pump();
    Check(controller.PerformAccount(LicenseAccountAction.Recover).Accepted && controller.Snapshot.SecurityBusy,
        "empty synchronized guest can recover existing account");
    Check(!controller.Refresh().Accepted && !controller.CancelRefresh().Accepted
        && !controller.PerformAccount(LicenseAccountAction.LinkGoogle).Accepted,
        "busy account action blocks refresh cancellation and overlapping authority calls");
    backend.Accounts[^1].Complete(new(false, "Account service unavailable. Retry.")); controller.Pump();
    Check(!controller.Snapshot.SecurityBusy && controller.Snapshot.CommandError.Contains("unavailable")
        && controller.Snapshot.HasPasswordInput, "failed account action retains private input for explicit retry");
    string serialized = JsonSerializer.Serialize(controller.Snapshot);
    Check(!serialized.Contains("secret-dummy") && !serialized.Contains("player@example.test"),
        "credential inputs never escape into presentation snapshot");
    Check(controller.PerformAccount(LicenseAccountAction.Recover).Accepted, "failed recovery can retry without retyping independent input");
    backend.Accounts[^1].Complete(new(true, "Recovered. Loading license.")); controller.Pump();
    Check(!controller.Snapshot.HasPasswordInput && controller.Snapshot.CredentialsClearedEpoch == 1
        && controller.Snapshot.Loading, "successful identity recovery clears sensitive fields and synchronizes profile");
    backend.Loads[^1].Complete(new() { Connected = true, Profile = new("secured", "Player"),
        Account = new(false, "player@example.test", ImmutableArray.Create("google")) }); controller.Pump();
    Check(controller.Snapshot.Data.Account.IsSecure && !controller.Snapshot.CanRecover,
        "recovered secured account retains authority identity");
    Check(!controller.PerformAccount(LicenseAccountAction.LinkGoogle).Accepted,
        "already linked sign-in method is disabled and rejected");
    Check(controller.PerformAccount(LicenseAccountAction.LinkGitHub).Accepted
        && backend.Accounts[^1].Action == LicenseAccountAction.LinkGitHub, "available provider uses explicit existing account action");
    backend.Accounts[^1].Complete(new(true, "Provider opened. Complete linking in browser then refresh.")); controller.Pump();
    Check(!controller.Snapshot.SecurityBusy && controller.Snapshot.SecurityMessage.Contains("browser"),
        "provider completion surfaces browser follow-through instruction");
    controller.SetCredentials("player@example.test", "secret-dummy", "secret-dummy");
    controller.PerformAccount(LicenseAccountAction.FinishPassword);
    backend.Accounts[^1].Complete(new(true, "License secured.")); controller.Pump();
    Check(controller.Snapshot.CredentialsClearedEpoch == 2 && controller.Snapshot.Loading,
        "successful password setup clears both fields and refreshes linked account");
}

backend = new FakeLicense();
var retired = new LicenseController(backend);
retired.Refresh();
retired.Dispose(); retired.Dispose();
backend.Loads[^1].Complete(FakeLicense.Career(123)); retired.Pump();
Check(!retired.Snapshot.Loading && retired.Snapshot.Data.Stats.GamesPlayed == 0
    && !retired.Refresh().Accepted, "retired License ignores late read-model completion and releases actions");
Check(LicenseController.Ratio(0, 0) == "0.00" && LicenseController.Ratio(2, 0) == "∞"
    && LicenseController.FormatTicks(3660 * 60) == "1h 01m", "career formatting handles zero deaths and long play time");
Console.WriteLine($"Hunter License checks passed ({checks} contracts). Production account operations: 0.");

sealed class FakeLicense : ILicenseBackend
{
    public sealed class Load
    {
        public CancellationToken Token;
        public TaskCompletionSource<LicenseData> Work = new();
        public void Complete(LicenseData data) => Work.SetResult(data);
    }
    public sealed class Account
    {
        public LicenseAccountAction Action;
        public TaskCompletionSource<LicenseActionResult> Work = new();
        public void Complete(LicenseActionResult result) => Work.SetResult(result);
    }
    public List<Load> Loads { get; } = new();
    public List<Account> Accounts { get; } = new();
    public LicenseData LocalSnapshot() => new();
    public Task<LicenseData> LoadAsync(CancellationToken cancellationToken)
    { var load = new Load { Token = cancellationToken }; Loads.Add(load); return load.Work.Task; }
    public Task<LicenseActionResult> AccountAsync(LicenseAccountAction action, string email, string password, CancellationToken cancellationToken)
    { var work = new Account { Action = action }; Accounts.Add(work); return work.Work.Task; }
    public static LicenseData Career(long games) => new()
    {
        Connected = true, Status = "GUEST LICENSE // CONNECTED", Profile = new("guest-a", "Player", 6, RatingPoints: 2500, RatingTier: 2),
        Stats = new() { GamesPlayed = games, Wins = 25, Kills = 100, Deaths = 20, Assists = 30, Headshots = 50, PlayedTicks = 3600 * 60 },
        Matches = Enumerable.Range(0, 27).Select(i => new LicenseMatch("match" + i, null, "arena" + i, 1, 0, true, "rated", true,
            i % 3 == 1, i % 3 == 0, 60 * 600, 10, 2, 3, 100)).ToImmutableArray()
    };
}
