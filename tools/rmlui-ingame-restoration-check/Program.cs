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
    public string Execute(InGameAction action, string map) { Executed.Add((action, map)); return ""; }
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
