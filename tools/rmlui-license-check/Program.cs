using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("RML LICENSE PASS " + label);
}
static NativeLicenseOverviewController.Snapshot Wait(
    NativeLicenseOverviewController c, Func<NativeLicenseOverviewController.Snapshot, bool> match)
{
    for (int i = 0; i < 120; i++)
    {
        var state = c.Tick();
        if (match(state)) return state;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Licence owner-thread acceptance did not complete");
}

static NativeLicenseOverview Verified(string name, long games, string visibility = "PRIVATE")
    => new(true, name, "SAMUS", "SECURED LICENSE", visibility, 1750, "TIER 2",
        games, 15, 5, 123, 27, 11, 4);

var fake = new Fake();
using var model = new NativeLicenseOverviewController(fake);
Check(!model.Current().Overview.Verified && !model.Current().Busy
    && model.Current().Overview.MatchesPlayed == 0,
    "initial state is unverified and does not claim an authenticated career record");

model.Refresh();
Check(model.Current().Busy && model.Current().Status.Contains("CONTACTING"),
    "HTTPS admission is asynchronous and does not block the RmlUi owner thread");
var stale = fake.Pending;
fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
model.Refresh();
stale.SetResult(Verified("OLD", 9999, "PUBLIC"));
fake.Pending.SetResult(Verified("CURRENT PLAYER", 20));
var accepted = Wait(model, s => !s.Busy && s.Overview.Verified);
Check(accepted.Overview.DisplayName == "CURRENT PLAYER"
    && accepted.Overview.MatchesPlayed == 20
    && accepted.Overview.PublicVisibility == "PRIVATE",
    "stale responses cannot replace server-accepted private career data");
long currentRevision = accepted.Revision;
fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
model.Refresh();
fake.Pending.SetException(new InvalidOperationException("Simulated TLS unavailable"));
var errored = Wait(model, s => !s.Busy && s.Status.Contains("UNAVAILABLE"));
Check(errored.Overview == accepted.Overview && errored.Revision > currentRevision,
    "failed refresh retains last verified values and shows a stale status");
fake.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
model.Refresh();
model.Cancel();
fake.Pending.SetResult(Verified("CANCELLED", 1000));
Thread.Sleep(30);
Check(model.Tick().Overview.DisplayName == "CURRENT PLAYER",
    "cancelled account operations cannot paint a retired native page");
Exception? crossThread = null;
Task.Run(() =>
{
    try { model.Tick(); }
    catch (Exception e) { crossThread = e; }
}).GetAwaiter().GetResult();
Check(crossThread is InvalidOperationException,
    "license snapshots and completed requests are applied on their owner thread");
Console.WriteLine("RML LICENSE verified-data, privacy and epoch acceptance complete.");

sealed class Fake : INativeLicenseOverviewReader
{
    public TaskCompletionSource<NativeLicenseOverview> Pending =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<NativeLicenseOverview> LoadAsync(CancellationToken _)
        => Pending.Task; // Deliberately ignores cancellation, testing epoch fencing.
}
