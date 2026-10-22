using System.Collections.Concurrent;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.License;

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("RML LICENSE PASS " + label);
}
static NativeLicenseOverviewController.Snapshot Wait(
    NativeLicenseOverviewController c, Func<NativeLicenseOverviewController.Snapshot, bool> match)
{
    for (int i = 0; i < 300; i++)
    {
        var state = c.Tick();
        if (match(state)) return state;
        Thread.Sleep(10);
    }
    throw new TimeoutException("License owner-thread acceptance did not complete");
}
static NativeLicenseMatch Match(int index) => new("MAP " + index, "BATTLE",
    DateTimeOffset.Parse("2026-10-17T18:00:00Z"), Won: true, Tied: index == 0,
    Eligible: index % 2 == 0, CareerEligible: index % 3 == 0,
    Kills: index + 1, Deaths: 2, Assists: 3, Damage: 100, PlayedTicks: 3660);
static NativeLicenseOverview Verified(string name, int count = 25, string visibility = "PRIVATE")
    => new(true, name, "SAMUS", "SECURED LICENSE", visibility, 1750, "TIER 2",
        20, 15, 5, 123, 27, 11, count)
    {
        Career = new(0, 2000, 216000, 50, 9, 2, 5, 6, 7, 8),
        History = Enumerable.Range(0, count).Select(Match).ToArray()
    };
static NativeLicenseOverviewController.Snapshot Load(NativeLicenseOverviewController c,
    Fake service, NativeLicenseOverview record)
{
    c.Refresh(); service.Take().SetResult(record);
    return Wait(c, s => !s.Busy);
}
static string Text(NativeLicenseOverviewController.Snapshot state, string id)
    => NativeLicenseOverviewBindings.Create(state)[id].Text;
static bool Flag(NativeLicenseOverviewController.Snapshot state, string id)
    => NativeLicenseOverviewBindings.Create(state)[id].Boolean;

var fake = new Fake();
using var model = new NativeLicenseOverviewController(fake);
var initial = model.Current();
Check(!initial.Overview.Verified && !initial.Busy && initial.Total == 0
    && Text(initial, "native_license_matches") == "NOT VERIFIED"
    && Text(initial, "native_license_history_empty").Contains("VERIFY")
    && Text(initial, "native_license_achievement_0").EndsWith("NOT VERIFIED"),
    "initial state never claims verified totals, history or achievements");
model.Refresh();
var stale = fake.Take();
Check(model.Current().Busy && fake.LastThread != Environment.CurrentManagedThreadId,
    "service admission runs off the RmlUi owner thread");
model.Refresh();
var current = fake.Take();
stale.SetResult(Verified("OLD ACCOUNT", visibility: "PUBLIC"));
current.SetResult(Verified("CURRENT PLAYER"));
var accepted = Wait(model, s => !s.Busy && s.Overview.Verified);
Check(accepted.Overview.DisplayName == "CURRENT PLAYER"
    && accepted.Overview.PublicVisibility == "PRIVATE" && accepted.Total == 25,
    "superseded account responses cannot replace the current authenticated record");
foreach (NativeLicenseFace face in Enum.GetValues<NativeLicenseFace>())
{
    model.SetFace(face);
    var bindings = NativeLicenseOverviewBindings.Create(model.Current());
    Check(bindings.Count(x => x.Key.StartsWith("visible:native_license_face_") && x.Value.Boolean) == 1
        && bindings["visible:native_license_face_" + face.ToString().ToLowerInvariant()].Boolean,
        "exactly one native face is visible for " + face);
}
Check(!model.SetFace((NativeLicenseFace)999) && !model.Page(1),
    "unknown tabs and paging outside History are rejected");
model.SetFace(NativeLicenseFace.History);
var history = model.Current();
Check(history.Pages == 4 && history.Rows.Count == 8 && !model.Page(-1)
    && Flag(history, "disabled:native_license_previous_page")
    && !Flag(history, "disabled:native_license_next_page"),
    "25 returned matches begin at page one with bounded navigation");
Check(Text(history, "native_license_match_label_0").StartsWith("TIE // MAP 0")
    && Text(history, "native_license_match_detail_0").Contains(" // CAREER // ")
    && Text(history, "native_license_match_detail_0").Contains("1m 01s")
    && Text(history, "native_license_match_detail_0").Contains("2026-10-17 18:00 UTC")
    && new[] { 1, 2, 3 }.All(i => Text(history, "native_license_match_detail_" + i).Contains("UNRANKED")),
    "ties precede wins, both eligibility flags are required, durations and dates are explicit");
for (int page = 1; page < 4; page++)
    Check(model.Page(1) && model.Current().Page == page
        && model.Current().Rows[0].Room == "MAP " + page * 8,
        "history preserves service ordering on page " + (page + 1));
history = model.Current();
Check(history.Rows.Count == 1 && !model.Page(1) && !model.Page(int.MaxValue)
    && Flag(history, "disabled:native_license_next_page")
    && !Flag(history, "visible:native_license_match_1")
    && Text(history, "native_license_match_label_1") == "",
    "last page clears unused rows and rejects out-of-range navigation");
string metrics = Text(accepted, "native_license_metrics");
Check(metrics.Contains("HEADSHOTS // 50") && metrics.Contains("PLAY TIME // 1h 00m")
    && metrics.Contains("AVG / MATCH // 3m 00s")
    && metrics.Contains("OCTOLITH SCORES // 6") && metrics.Contains("KILLS AS PRIME // 8")
    && Text(accepted, "native_license_comparison_metrics").Contains("KILLS / MATCH // 6.15")
    && Text(accepted, "native_license_achievement_3").EndsWith("UNLOCKED")
    && Text(accepted, "native_license_achievement_4").EndsWith("LOCKED"),
    "stats and milestone thresholds use career counters rather than recent-match sums");
foreach (int count in new[] { 0, 1, 8, 9, 25 })
{
    var s = Load(model, fake, Verified("PAGE FIXTURE", count));
    Check(s.Page == 0 && s.Total == count && s.Pages == Math.Max(1, (count + 7) / 8)
        && s.Rows.Count == Math.Min(8, count)
        && Flag(s, "disabled:native_license_next_page") == (count <= 8),
        "refresh resets/shrinks history safely for " + count + " matches");
}
var mutable = new List<NativeLicenseMatch> { Match(0) };
var frozen = Load(model, fake, Verified("IMMUTABLE", 1) with { History = mutable });
mutable[0] = Match(99);
Check(model.Current().Rows[0].Room == "MAP 0" && frozen.Overview.History[0].Room == "MAP 0",
    "reader source collections cannot mutate accepted history");
var zero = new NativeLicenseOverview(true, "ZERO", "SAMUS", "", "unexpected", 0,
    "UNRANKED", 0, 0, 0, 0, 0, 0, 0) { Career = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0) };
var empty = Load(model, fake, zero);
Check(empty.Overview.PublicVisibility == "UNAVAILABLE"
    && Text(empty, "native_license_metrics").Contains("K/D // 0.00")
    && Text(empty, "native_license_comparison_metrics").Contains("KILLS / MATCH // 0.00")
    && Text(empty, "native_license_history_empty") == "NO ACCEPTED MATCHES RETURNED.",
    "verified empty career handles division by zero and unknown privacy without guessing");
var extreme = Load(model, fake, zero with { Kills = 1,
    Career = zero.Career! with { PlayedTicks = long.MaxValue } });
Check(Text(extreme, "native_license_metrics").Contains("K/D // ∞"),
    "zero deaths and large tick counters render without numeric overflow");
model.Refresh();
var error = fake.Take();
var loading = model.Current();
Check(!loading.Overview.Verified && loading.Total == 0 && loading.Page == 0
    && Text(loading, "native_license_name") == "LOCAL HUNTER" && !model.Page(1)
    && !Flag(loading, "disabled:native_license_cancel_refresh"),
    "refresh retires the prior account's identity, totals and history immediately");
error.SetException(new InvalidOperationException("private@example.test Authorization: Bearer private-secret"));
var errored = Wait(model, s => !s.Busy);
Check(!errored.Overview.Verified && errored.Total == 0 && errored.Status.Contains("UNAVAILABLE")
    && !String.Join(" ", NativeLicenseOverviewBindings.Create(errored).Values.Select(v => v.Text)).Contains("private"),
    "failed requests expose no exception body or previous account");
var offline = Load(model, fake, Verified("PRIVATE FALLBACK") with
    { Verified = false, Status = "secret session body" });
Check(!offline.Overview.Verified && offline.Total == 0
    && !String.Join(" ", NativeLicenseOverviewBindings.Create(offline).Values.Select(v => v.Text)).Contains("secret"),
    "unverified fallback cannot paint account data or raw service status");
foreach (NativeLicenseOverview malformed in new[]
    { Verified("NEGATIVE") with { Kills = -1 }, Verified("TOO MANY", 26),
      Verified("BAD ROW", 1) with { History = new[] { Match(0) with { Damage = -1 } } } })
{
    var rejected = Load(model, fake, malformed);
    Check(!rejected.Overview.Verified && rejected.Status.Contains("UNAVAILABLE"), "malformed service data fails closed");
}
model.Refresh();
var cancelled = fake.Take();
long busyRevision = model.Current().Revision;
model.Cancel();
cancelled.SetResult(Verified("CANCELLED ACCOUNT"));
Thread.Sleep(30);
Check(!model.Tick().Overview.Verified && model.Current().Status.Contains("CANCELLED")
    && model.Current().Revision > busyRevision
    && Flag(model.Current(), "disabled:native_license_cancel_refresh"),
    "cancel updates controls/status and fences a cancellation-ignoring reader");
Exception? crossThread = null;
Task.Run(() => { try { model.Tick(); } catch (Exception e) { crossThread = e; } }).GetAwaiter().GetResult();
Check(crossThread is InvalidOperationException, "all snapshots are applied on their owner thread");
model.Refresh();
var retired = fake.Take();
model.Dispose();
retired.SetResult(Verified("DISPOSED ACCOUNT"));
bool disposedGuard = false;
try { model.Tick(); } catch (ObjectDisposedException) { disposedGuard = true; }
Check(disposedGuard, "a retired page cannot apply late account results");
PrivacyAcceptance.Run();
AccountAcceptance.Run();
PublicProfileAcceptance.Run();
AccountLeaseAcceptance.Run();
StarRankAcceptance.Run();
Console.WriteLine("RML LICENSE career navigation, verified history, privacy and lifetime acceptance complete.");

sealed class Fake : INativeLicenseOverviewReader
{
    private readonly BlockingCollection<TaskCompletionSource<NativeLicenseOverview>> _requests = new();
    public int LastThread;
    public Task<NativeLicenseOverview> LoadAsync(CancellationToken _)
    {
        LastThread = Environment.CurrentManagedThreadId;
        var request = new TaskCompletionSource<NativeLicenseOverview>(TaskCreationOptions.RunContinuationsAsynchronously);
        _requests.Add(request);
        return request.Task;
    }
    public TaskCompletionSource<NativeLicenseOverview> Take()
        => _requests.TryTake(out var request, TimeSpan.FromSeconds(3)) ? request
            : throw new TimeoutException("License reader was not dispatched");
}
