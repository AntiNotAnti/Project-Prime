using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool value, string label)
{ if (!value) throw new InvalidOperationException(label); checks++; }
var dock = new NativeCampaignShipDock(2, 1);
var backend = new FixtureBackend(dock);
using (var controller = new NativeCampaignShipController<string>(backend, dock, 4))
{
    var state = controller.Snapshot();
    Check(state.Verified && !state.StarMap && state.Selected == 1 && state.Hunter == 4 && state.Dock == dock,
        "initial capture retains slot, planet and hunter");
    Check(state.Destinations.Count == 5 && !state.Closed && !controller.TryTakeEffect(out _), "opening projects five planets without a launch");
    backend.Rows[1] = backend.Rows[1] with { Name = "MUTATED AFTER READ" };
    Check(state.Destinations[1].Name == "SYNTHETIC PLANET 1", "backend array mutation cannot alter captured projection");
    bool readOnly = false;
    try { ((IList<NativeCampaignShipDestination>)state.Destinations)[1] = default; }
    catch (NotSupportedException) { readOnly = true; }
    Check(readOnly, "snapshot rows cannot be edited by presentation");
    Check(!controller.Select(0) && !controller.Launch(false), "cockpit cannot use star-map actions");
    Check(controller.OpenMap() && !controller.OpenMap(), "map opens once without backend launch");
    Check(!controller.Select(-1) && !controller.Select(5), "invalid planet choices rejected");
    Check(controller.Select(2) && !controller.Launch(false) && backend.Attempts == 0, "locked planet selectable for detail but cannot land");
    Check(!controller.Launch(true), "map cannot issue cockpit surface exit");
    Check(controller.Back() && !controller.Snapshot().StarMap && !controller.TryTakeEffect(out _), "map Back returns to cockpit without launch or close");
    Check(controller.Launch(true), "surface exit is authorized through existing backend boundary");
    Check(backend.Requests.Single() == new NativeCampaignShipRequest(new(dock, FixtureBackend.HashA, 4), 1, true),
        "surface request captures exact dock, SHA bytes, hunter and current destination");
    Check(controller.TryTakeEffect(out var effect) && controller.Snapshot().Closed
        && effect.Launch && effect.Plan == "synthetic:2:1:4:True" && effect.Dock == dock, "successful plan transfers one typed launch effect");
    Check(effect.Fingerprint == FixtureBackend.HashA, "launch effect preserves captured SHA for final runtime intake");
    Check(!controller.TryTakeEffect(out _) && !controller.Launch(true) && !controller.ReturnToSaves()
        && !controller.Refresh() && !controller.Back(), "consumed successful launch cannot be recreated");
    Check(backend.Creates == 1, "successful departure creates exactly one plan");
}

backend = new FixtureBackend(dock);
using (var controller = new NativeCampaignShipController<string>(backend, dock, 6))
{
    controller.OpenMap(); controller.Select(0);
    Check(controller.Launch(false) && backend.Requests.Single().Destination == 0
        && !backend.Requests.Single().ExitShipToSurface && backend.Requests.Single().Witness.Hunter == 6,
        "star-map landing captures selected destination and hunter");
    Check(controller.TryTakeEffect(out var effect) && effect.Plan == "synthetic:2:0:6:False"
        && effect.Fingerprint == FixtureBackend.HashA, "landing plan and captured SHA retained through drain");
}

foreach (string replacement in new[] { FixtureBackend.HashB, "EMPTY", "UNAVAILABLE" })
{
    backend = new FixtureBackend(dock);
    using var controller = new NativeCampaignShipController<string>(backend, dock, 0);
    backend.Hash = replacement;
    Check(!controller.Launch(true) && !controller.Snapshot().Closed && !controller.Snapshot().Verified
        && controller.Snapshot().Destinations.Count == 0 && controller.Snapshot().Error.Length > 0,
        "changed/deleted/unreadable save clears verified totals and remains open");
    Check(!controller.TryTakeEffect(out _) && backend.Creates == 0, "failed save witness never produces a plan");
    int attempts = backend.Attempts;
    Check(!controller.Launch(true) && backend.Attempts == attempts, "failure requires fresh read before another departure");
    backend.Hash = FixtureBackend.HashB;
    Check(controller.Refresh() && controller.Snapshot().Verified && controller.Launch(true)
        && backend.Requests.Last().Witness.Fingerprint == FixtureBackend.HashB,
        "refresh explicitly captures changed SHA before retry");
}

foreach (var replacement in new[] { new NativeCampaignShipDock(1, 1), new NativeCampaignShipDock(2, 0) })
{
    backend = new FixtureBackend(dock);
    using var controller = new NativeCampaignShipController<string>(backend, dock, 0);
    backend.CurrentDock = replacement;
    Check(!controller.Launch(true) && !controller.TryTakeEffect(out _) && backend.Creates == 0,
        "replacement dock slot or planet cannot authorize captured departure");
}

var malformed = new List<NativeCampaignShipRead>();
foreach (string hash in new[] { "EMPTY", "UNAVAILABLE", "", new string('A', 63), new string('Z', 64) })
    malformed.Add(new(hash, FixtureBackend.RowsForTest()));
malformed.Add(new(FixtureBackend.HashA, Array.Empty<NativeCampaignShipDestination>()));
malformed.Add(new(FixtureBackend.HashA, null!));
var duplicate = FixtureBackend.RowsForTest(); duplicate[4] = duplicate[0]; malformed.Add(new(FixtureBackend.HashA, duplicate));
var wrongId = FixtureBackend.RowsForTest(); wrongId[4] = wrongId[4] with { Planet = 5 }; malformed.Add(new(FixtureBackend.HashA, wrongId));
var missingName = FixtureBackend.RowsForTest(); missingName[0] = missingName[0] with { Name = "" }; malformed.Add(new(FixtureBackend.HashA, missingName));
var badTotal = FixtureBackend.RowsForTest(); badTotal[0] = badTotal[0] with { Octoliths = 3 }; malformed.Add(new(FixtureBackend.HashA, badTotal));
var badArtifacts = FixtureBackend.RowsForTest(); badArtifacts[0] = badArtifacts[0] with { Artifacts = -1 }; malformed.Add(new(FixtureBackend.HashA, badArtifacts));
var dockLocked = FixtureBackend.RowsForTest(); dockLocked[1] = dockLocked[1] with { Unlocked = false }; malformed.Add(new(FixtureBackend.HashA, dockLocked));
malformed.Add(new(FixtureBackend.HashA, FixtureBackend.RowsForTest(), "SYNTHETIC UNVERIFIED READ"));
foreach (var response in malformed)
{
    backend = new FixtureBackend(dock) { ReadOverride = response };
    using var controller = new NativeCampaignShipController<string>(backend, dock, 0);
    Check(!controller.Snapshot().Verified && controller.Snapshot().Destinations.Count == 0
        && controller.Snapshot().Error.Length > 0, "malformed response is redacted and fails closed");
    Check(!controller.OpenMap() && !controller.Launch(true) && !controller.TryTakeEffect(out _) && backend.Creates == 0,
        "malformed response never grants travel authority");
    Check(controller.ReturnToSaves() && controller.TryTakeEffect(out var effect) && !effect.Launch && effect.Dock == dock,
        "unavailable save still permits safe return to file select");
}

backend = new FixtureBackend(dock);
using (var controller = new NativeCampaignShipController<string>(backend, dock, 0))
{
    Check(controller.Back() && !controller.Back() && controller.TryTakeEffect(out var effect)
        && !effect.Launch && effect.Plan == null && effect.Fingerprint.Length == 0 && effect.Dock == dock && backend.Creates == 0,
        "cockpit Back captures one return without a launch plan");
    Check(!controller.TryTakeEffect(out _), "return effect drains once");
}

var disposed = new NativeCampaignShipController<string>(new FixtureBackend(dock), dock, 0);
disposed.ReturnToSaves(); disposed.Dispose(); disposed.Dispose();
Check(!disposed.TryTakeEffect(out _) && !disposed.Refresh() && !disposed.OpenMap() && !disposed.Back(), "disposal discards queued effects and prevents all work");

using (var controller = new NativeCampaignShipController<string>(new FixtureBackend(dock), dock, 0))
{
    foreach (Action action in new Action[] { () => controller.Snapshot(), () => controller.Refresh(), () => controller.OpenMap(),
        () => controller.Cockpit(), () => controller.Select(0), () => controller.Launch(true), () => controller.Back(),
        () => controller.ReturnToSaves(), () => controller.TryTakeEffect(out _), controller.Dispose })
    {
        Exception? caught = null;
        var worker = new Thread(() => { try { action(); } catch (Exception error) { caught = error; } });
        worker.Start(); worker.Join();
        Check(caught is InvalidOperationException, "foreign thread rejected without backend mutation");
    }
    Check(controller.Snapshot().Verified && !controller.Snapshot().Closed, "owner remains usable after rejected calls");
}
Console.WriteLine($"PASS {checks} campaign ship controller checks (synthetic backend; no save files or preference writes).");

internal sealed class FixtureBackend : INativeCampaignShipBackend<string>
{
    internal const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    internal const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";
    internal string Hash = HashA;
    internal NativeCampaignShipDock CurrentDock;
    internal NativeCampaignShipDestination[] Rows = RowsForTest();
    internal NativeCampaignShipRead? ReadOverride;
    internal readonly List<NativeCampaignShipRequest> Requests = new();
    internal int Creates, Attempts;
    internal FixtureBackend(NativeCampaignShipDock dock) => CurrentDock = dock;
    public NativeCampaignShipRead Read(NativeCampaignShipDock dock) => ReadOverride ?? new(Hash, Rows);
    public bool TryCreateLaunch(NativeCampaignShipRequest request, out string plan, out string error)
    {
        Attempts++; Requests.Add(request); plan = "";
        if (request.Witness.Dock != CurrentDock || request.Witness.Fingerprint != Hash)
        { error = "SYNTHETIC DOCK OR SAVE CHANGED"; return false; }
        Creates++; plan = $"synthetic:{request.Witness.Dock.Slot}:{request.Destination}:{request.Witness.Hunter}:{request.ExitShipToSurface}";
        error = ""; return true;
    }
    internal static NativeCampaignShipDestination[] RowsForTest() => Enumerable.Range(0, 5)
        .Select(i => new NativeCampaignShipDestination(i, "SYNTHETIC PLANET " + i, i < 2, i == 1,
            false, 0, i == 4 ? 0 : 2, 0, i == 4 ? 0 : 6)).ToArray();
}
