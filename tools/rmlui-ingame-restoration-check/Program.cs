using System.Collections.Immutable;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Training;

System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
var backend = new FakeMatch();
using (var menu = new InGameController(backend))
{
    long revision = menu.Snapshot().Revision; menu.Refresh(); menu.Refresh();
    Check(menu.Snapshot().Revision == revision, "Equal roster copies retain snapshot revision");
    Check(!menu.Dispatch(menu.Intent(InGameAction.BotRemove)).Accepted && menu.Snapshot().Page == InGamePage.Pause, "Remove cannot open confirmation outside bot administration");
    Check(menu.Dispatch(menu.Intent(InGameAction.Bots)).Accepted, "Owner opens real bot administration policy");
    Check(menu.Dispatch(menu.Intent(InGameAction.BotHunter)).Accepted && menu.Snapshot().BotDraft.Hunter == Hunter.Kanden, "Hunter is an explicit local bot draft");
    Check(backend.Edits.Count == 0, "Draft edits do not mutate the authoritative backend");
    Check(menu.Dispatch(menu.Intent(InGameAction.BotApply)).Accepted && backend.Edits[^1].Bot.Generation == 7, "Apply retains selected slot generation");
    Check(menu.Dispatch(menu.Intent(InGameAction.BotTeam)).Accepted && menu.Snapshot().BotDraft.Team == -1, "Team cycle includes auto and excludes full foreign team");
    Check(menu.Dispatch(menu.Intent(InGameAction.BotTeam)).Accepted && menu.Snapshot().BotDraft.Team == 0, "Selected bot may remain on its full current team");
    for (int step = 0; step < 6; step++) Check(menu.Dispatch(menu.Intent(InGameAction.BotHandicap)).Accepted && menu.Snapshot().BotDraft.Handicap <= 50, "Handicap uses server range");
    Check(menu.Snapshot().BotDraft.Handicap == 0 && backend.Edits[^1].Action == InGameAction.BotHandicap, "Handicap cycles at50 and invokes separate authority operation");
    Check(menu.Dispatch(menu.Intent(InGameAction.BotRemove)).Accepted && menu.Snapshot().Page == InGamePage.Confirmation, "Destructive bot removal requires confirmation");
    Check(!menu.Dispatch(menu.Intent(InGameAction.Resume)).Accepted, "Confirmation rejects background menu actions");
    Check(menu.Dispatch(menu.Intent(InGameAction.Cancel)).Accepted && menu.Snapshot().Page == InGamePage.Bots, "Cancel returns to originating bot page");
    menu.Dispatch(menu.Intent(InGameAction.BotRemove)); backend.State = backend.State with { Bots = ImmutableArray.Create(backend.State.Bots[0] with { Generation = 8 }) };
    int sent = backend.Edits.Count;
    Check(!menu.Dispatch(menu.Intent(InGameAction.Confirm)).Accepted && backend.Edits.Count == sent, "Reused bot slot cannot receive retained destructive intent");
    menu.Dispatch(menu.Intent(InGameAction.Cancel)); menu.Dispatch(menu.Intent(InGameAction.BotSelect));
    backend.State = backend.State with { CanManageBots = false, Owner = false }; menu.Refresh();
    Check(!menu.Dispatch(menu.Intent(InGameAction.BotApply)).Accepted && !menu.Dispatch(menu.Intent(InGameAction.BotRemove)).Accepted, "Ownership loss rejects apply and destructive confirmation");
    backend.State = backend.State with { CanManageBots = true, Owner = true, CanAddBot = false }; menu.Refresh();
    Check(!menu.Dispatch(menu.Intent(InGameAction.BotAdd)).Accepted, "Full session rejects bot addition before backend send");
    backend.State = backend.State with { Teams = ImmutableArray<InGameTeam>.Empty }; menu.Refresh();
    Check(!menu.Dispatch(menu.Intent(InGameAction.BotTeam)).Accepted, "Free for all rejects team assignment");
    menu.Dispatch(menu.Intent(InGameAction.Cancel));
    Check(menu.Dispatch(menu.Intent(InGameAction.Vote)).Accepted && menu.Snapshot().Page == InGamePage.MapVote, "Existing map service opens local ballot");
    menu.Dispatch(menu.Intent(InGameAction.MapNext));
    Check(menu.Dispatch(menu.Intent(InGameAction.VoteSubmit)).Accepted && backend.Executed[^1] == (InGameAction.VoteSubmit, "arena_b"), "Selected map reaches existing proposal service");
    backend.State = backend.State with { VoteActive = true, VoteAnswered = false }; menu.Refresh();
    Check(menu.Dispatch(menu.Intent(InGameAction.VoteYes)).Accepted, "Unanswered server vote accepts yes");
    backend.State = backend.State with { VoteAnswered = true }; menu.Refresh();
    Check(!menu.Dispatch(menu.Intent(InGameAction.VoteNo)).Accepted, "Answered vote rejects second answer");
    Check(menu.Dispatch(menu.Intent(InGameAction.ReturnLobby)).Accepted, "Persistent match return is confirmed");
    backend.State = backend.State with { Owner = false }; menu.Refresh();
    Check(!menu.Dispatch(menu.Intent(InGameAction.Confirm)).Accepted, "Return to lobby checks owner again at confirmation");
    menu.Dispatch(menu.Intent(InGameAction.Cancel));
    Check(!menu.Dispatch(new(menu.Snapshot().Lifetime, menu.Snapshot().Revision - 1, InGameAction.Quit)).Accepted, "Old menu revision is rejected");
    Check(Task.Run(() => { try { menu.Intent(InGameAction.Resume); return false; } catch (InvalidOperationException) { return true; } }).Result, "Owner-thread intent construction is guarded");
}
// Custom selection remains a menu intent; this fake backend never mutates a world.
Check((int)InGameAction.JournalBack == 33 && (int)InGameAction.CustomEquip == 34
    && (int)InGameAction.CustomReturnNative == 35, "Custom actions append without changing journal indices");
var customBackend = new FakeMatch();
customBackend.State = customBackend.State with { Owner = false, CanManageBots = false,
    CustomAvailable = true, CanEquipCustom = true, CanReturnNative = false,
    CustomBindingHelp = "Selection unbound; use menu buttons." };
using (var customMenu = new InGameController(customBackend))
{
    Check(customMenu.Dispatch(customMenu.Intent(InGameAction.CustomEquip)).Effect == InGameEffect.Resume
        && customBackend.Executed.Single().Action == InGameAction.CustomEquip
        && customBackend.Edits.Count == 0, "Non-owner custom equip routes to selection backend, never bot administration");
    int sent = customBackend.Executed.Count;
    Check(!customMenu.Dispatch(customMenu.Intent(InGameAction.CustomReturnNative)).Accepted
        && customBackend.Executed.Count == sent, "Disabled native return never reaches backend");
    customBackend.State = customBackend.State with { CanEquipCustom = false, CanReturnNative = true,
        CustomNativePending = true }; customMenu.Refresh();
    Check(customMenu.Snapshot().Facts.CustomNativePending && customMenu.Snapshot().Facts.CustomBindingHelp.Contains("unbound"),
        "Readonly custom facts and binding fallback reach menu snapshot");
    Check(customMenu.Dispatch(customMenu.Intent(InGameAction.CustomReturnNative)).Effect == InGameEffect.Resume
        && customBackend.Executed.Last().Action == InGameAction.CustomReturnNative, "Allowed native return resumes after backend accepts");
    customBackend.ExecuteError = "The loadout changed.";
    Check(!customMenu.Dispatch(customMenu.Intent(InGameAction.CustomReturnNative)).Accepted
        && customMenu.Snapshot().Error == customBackend.ExecuteError, "Refused queue request remains visible without resume");
    customBackend.ExecuteError = "";
    customBackend.State = customBackend.State with { CustomAvailable = false }; customMenu.Refresh();
    sent = customBackend.Executed.Count;
    Check(!customMenu.Dispatch(customMenu.Intent(InGameAction.CustomEquip)).Accepted
        && customBackend.Executed.Count == sent, "Readiness loss refuses custom action before backend");
    customBackend.State = customBackend.State with { CustomAvailable = true, CanEquipCustom = true, Replay = true };
    Check(!customMenu.Dispatch(customMenu.Intent(InGameAction.CustomEquip)).Accepted, "Replay cannot request custom selection");
}
// Equal-looking display facts cannot let an old click adopt a replacement source witness.
// These are fake immutable source identities; asset-backed Scene/mailbox checks live separately.
var originalContext = new SelectionTestContext(7, 9, 11, "manifest-a");
foreach (var (label, replacement) in new[]
{
    ("life", originalContext with { Life = 10 }),
    ("bootstrap", originalContext with { Bootstrap = 12 }),
    ("manifest", originalContext with { Manifest = "manifest-b" }),
    ("scene", originalContext with { SceneGeneration = 8 }),
    ("equal full context, distinct publication", originalContext)
})
foreach (var action in new[] { InGameAction.CustomEquip, InGameAction.CustomReturnNative })
{
    object firstWitness = new SelectionTestWitness(originalContext);
    object replacementWitness = new SelectionTestWitness(replacement);
    var raceBackend = new FakeMatch();
    raceBackend.State = raceBackend.State with { Owner = false, CanManageBots = false,
        CustomAvailable = true, CanEquipCustom = action == InGameAction.CustomEquip,
        CanReturnNative = action == InGameAction.CustomReturnNative, CustomSelectionWitness = firstWitness };
    using var raceMenu = new InGameController(raceBackend);
    long initialRevision = raceMenu.Snapshot().Revision;
    raceMenu.Refresh();
    Check(raceMenu.Snapshot().Revision == initialRevision,
        "Stable selection witness preserves revision: " + label + "/" + action);
    InGameIntent queued = raceMenu.Intent(action);
    InGameFacts oldDisplay = raceMenu.Snapshot().Facts;
    raceBackend.State = raceBackend.State with { CustomSelectionWitness = replacementWitness };
    Check((oldDisplay with { CustomSelectionWitness = null, Bots = ImmutableArray<InGameBot>.Empty,
            BotHunters = ImmutableArray<Hunter>.Empty, Teams = ImmutableArray<InGameTeam>.Empty })
        == (raceBackend.State with { CustomSelectionWitness = null, Bots = ImmutableArray<InGameBot>.Empty,
            BotHunters = ImmutableArray<Hunter>.Empty, Teams = ImmutableArray<InGameTeam>.Empty }),
        "Source replacement keeps all displayed permission facts equal: " + label + "/" + action);
    InGameResult refused = raceMenu.Dispatch(queued);
    Check(!refused.Accepted && refused.Effect == InGameEffect.None
        && raceBackend.Executed.Count == 0 && raceBackend.Edits.Count == 0
        && refused.Error.Contains("loadout changed", StringComparison.OrdinalIgnoreCase),
        "Old click before Refresh cannot adopt replacement witness: " + label + "/" + action);
    Check(ReferenceEquals(raceMenu.Snapshot().Facts.CustomSelectionWitness, firstWitness)
        && !raceMenu.Dispatch(raceMenu.Intent(action)).Accepted && raceBackend.Executed.Count == 0,
        "Refusal preserves old publication until Refresh: " + label + "/" + action);
    long refusedRevision = raceMenu.Snapshot().Revision;
    raceMenu.Refresh();
    Check(raceMenu.Snapshot().Revision > refusedRevision
        && ReferenceEquals(raceMenu.Snapshot().Facts.CustomSelectionWitness, replacementWitness),
        "Refresh advances revision for equal-looking replacement source: " + label + "/" + action);
    Check(!raceMenu.Dispatch(queued).Accepted && raceBackend.Executed.Count == 0,
        "Old revision remains rejected after Refresh: " + label + "/" + action);
    InGameResult retried = raceMenu.Dispatch(raceMenu.Intent(action));
    Check(retried.Accepted && retried.Effect == InGameEffect.Resume
        && raceBackend.Executed.Single().Action == action && raceBackend.Edits.Count == 0,
        "Explicit current retry reaches selection backend once: " + label + "/" + action);
}
var resultsBackend = new FakeResults();
using (var results = new MatchResultsController(resultsBackend))
{
    long revision = results.Snapshot().Revision; results.Refresh(); Check(results.Snapshot().Revision == revision, "Equal result copies retain revision");
    Check(results.Snapshot().Rows.Length == 8 && results.Snapshot().PageCount == 2, "Whole actual ballot is paged");
    MatchResultsIntent choose = results.Intent(MatchResultsAction.SelectMap, results.Snapshot().Rows[0].CatalogIndex);
    resultsBackend.State = resultsBackend.State with { Maps = resultsBackend.State.Maps.Reverse().ToImmutableArray() };
    Check(results.Dispatch(choose).Accepted && resultsBackend.Selected == "arena_0", "Queued selection retains key when tally order changes");
    results.Refresh(); Check(results.Snapshot().Rows[0].Key == "arena_9", "Visible ballot preserves authoritative vote ordering");
    results.Dispatch(results.Intent(MatchResultsAction.Search), "Arena 3"); Check(results.Snapshot().Rows.Single().Key == "arena_3", "Search filters actual metadata names");
    results.Dispatch(results.Intent(MatchResultsAction.ClearSearch)); results.Dispatch(results.Intent(MatchResultsAction.NextPage));
    Check(results.Snapshot().Page == 1 && results.Snapshot().Rows.Length == 2, "Next page retains every arena");
    var removed = results.Intent(MatchResultsAction.SelectMap, results.Snapshot().Rows[0].CatalogIndex);
    resultsBackend.State = resultsBackend.State with { Maps = resultsBackend.State.Maps.Where(map => map.Key != removed.MapKey).ToImmutableArray() };
    int submitted = resultsBackend.ChooseCount;
    Check(!results.Dispatch(removed).Accepted && resultsBackend.ChooseCount == submitted, "Removed arena cannot receive a queued vote");
    resultsBackend.State = resultsBackend.State with { Persistent = true, Online = true }; results.Refresh();
    Check(!results.Dispatch(results.Intent(MatchResultsAction.Rematch)).Accepted && !results.Dispatch(results.Intent(MatchResultsAction.SelectMap, 0)).Accepted, "Persistent lobby keeps next deployment under server authority");
    resultsBackend.State = resultsBackend.State with { Persistent = false, Online = false }; results.Refresh();
    Check(results.Dispatch(results.Intent(MatchResultsAction.Rematch)).Effect == MatchResultsEffect.Rematch && resultsBackend.RematchCount == 1, "Offline action invokes existing rematch hook");
    resultsBackend.State = resultsBackend.State with { Available = false }; results.Refresh();
    Check(!results.Dispatch(results.Intent(MatchResultsAction.Close)).Accepted, "Ended result lifetime rejects actions");
}
var stats = new AimTrainerStats();
void Stat(string name, object value) => typeof(AimTrainerStats).GetProperty(name)!.SetValue(stats, value);
Stat("Score", 900); Stat("ShotsFired", 10); Stat("ShotsHit", 7); Stat("ShotsMissed", 3); Stat("ScopedShots", 6); Stat("ScopedHits", 5); Stat("TrackingFrames", 120); Stat("TrackingHitFrames", 90); Stat("LongestContinuousTrack", 60); stats.ReactionSamples.Add(12);
var report = AimResultsReport.From(AimTrainerDefinition.Default with { Weapon = BeamType.Imperialist }, stats, TrainingInputSource.Gamepad, false, false, "Storage unavailable");
Check(report.Metrics.Any(metric => metric.Label == "ACCURACY" && metric.Value == "70.0%") && report.Metrics.Any(metric => metric.Label == "SCOPED HITS / SHOTS" && metric.Value == "5/6"), "Training report copies actual weapon-specific statistics");
Stat("Score", 1000); Check(report.Metrics.First().Value == "900" && report.Status == "Storage unavailable", "Completed report is immutable and retains save feedback");
var tracking = AimResultsReport.From(AimTrainerDefinition.Default with { Weapon = BeamType.ShockCoil }, stats, TrainingInputSource.Touch, true, true, "");
Check(tracking.Metrics.Any(metric => metric.Label == "TRACKING" && metric.Value == "75.0%") && tracking.Metrics.Any(metric => metric.Label == "REACQUISITION") && tracking.Status == "NEW PERSONAL BEST", "Tracking and Shock Coil metrics preserve existing result semantics");
foreach (var action in Enum.GetValues<AimResultsAction>()) { using var controller = new AimResultsController(report); Check(!controller.Dispatch(Guid.NewGuid(), action) && controller.Dispatch(controller.Lifetime, action) && !controller.Dispatch(controller.Lifetime, action), "Training action is scoped and accepted once"); }

Console.WriteLine($"Restored in-game/results contracts passed: {checks} assertions; fake authority, confirmations, roster generations, keyed ballots, rematch and immutable training reports.");

sealed class FakeMatch : IInGameBackend
{
    public InGameFacts State = new(true, false, false, true, false, true, true, false, false, false, "", "", "")
    { Bots = ImmutableArray.Create(new InGameBot(2, 7, Hunter.Samus, 0, 1, 0, 0)), BotHunters = ImmutableArray.Create(Hunter.Samus, Hunter.Kanden, Hunter.Random), Teams = ImmutableArray.Create(new InGameTeam(0, 2, 2), new InGameTeam(1, 2, 2)), CanManageBots = true, CanAddBot = true };
    public List<(InGameAction Action, InGameBot Bot)> Edits = new(); public List<(InGameAction Action, string Map)> Executed = new();
    public InGameFacts Capture() => State with { Bots = State.Bots.ToArray().ToImmutableArray(), BotHunters = State.BotHunters.ToArray().ToImmutableArray(), Teams = State.Teams.ToArray().ToImmutableArray() };
    public ImmutableArray<string> Maps() => ImmutableArray.Create("arena_a", "arena_b");
    public string ExecuteError = "";
    public string Execute(InGameAction action, string map) { Executed.Add((action, map)); return ExecuteError; }
    public string EditBot(InGameAction action, InGameBot bot) { Edits.Add((action, bot)); return ""; }
}
sealed class FakeResults : IMatchResultsBackend
{
    public MatchResultsFacts State = new(true, false, false, true, 10, "", "", "", 1, Enumerable.Range(0, 10).Select(i => new MatchResultsMap("arena_" + i, "Arena " + i, i)).ToImmutableArray());
    public string Selected = ""; public int ChooseCount, RematchCount;
    public MatchResultsFacts Capture() => State with { Maps = State.Maps.ToArray().ToImmutableArray() };
    public string Choose(string roomKey) { Selected = roomKey; ChooseCount++; State = State with { Picked = roomKey }; return ""; }
    public string Rematch() { RematchCount++; return ""; }
}

readonly record struct SelectionTestContext(long SceneGeneration, uint Life, uint Bootstrap, string Manifest);
// Deliberately reference equality, matching the production immutable publication class.
sealed class SelectionTestWitness(SelectionTestContext context)
{
    public SelectionTestContext Context { get; } = context;
}
