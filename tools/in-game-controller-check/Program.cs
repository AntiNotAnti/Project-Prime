using System.Collections.Immutable;
using System.Runtime.InteropServices;
using MphRead;
using MphRead.Droid;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.InGame;
using MphRead.Mods.Training;

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

if (args.Length > 0)
{
    if (args.Length is not (1 or 3) || args.Length == 3 && args[1] != "--assets") throw new ArgumentException("Usage: in-game-controller-check [bridge [--assets directory]]");
    string assets = args.Length == 3 ? Path.GetFullPath(args[2]) : Path.Combine(AppContext.BaseDirectory, "rmlui");
    nint module = NativeLibrary.Load(Path.GetFullPath(args[0]));
    NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly, (name, _, _) => name == "ProjectPrime.RmlUi.Native" ? module : 0);
    try
    {
        using var host = new RmlUiHost(); Check(host.Initialize(1280, 720, 1, assets, RmlUiRenderBackend.DrawList), "Actual native game page host initializes");
        using var pages = new RmlUiPageManager(host);
        bool Activate(RmlUiDocumentToken document, string id, Func<RmlUiIntent, bool> handle, bool pointer = false)
        {
            Check(host.FocusDocument(document, id), "Actual DOM focuses " + id);
            host.Update(); host.Render(1280, 720);
            if (pointer)
            {
                Check(host.TryGetElementBounds(document, id, out float x, out float y, out float w, out float h) && w > 0 && h > 0 && x >= 0 && y >= 0 && x + w <= 1280 && y + h <= 720, "Focused control stays inside its framebuffer viewport: " + id);
                host.Input.PointerMoved(x + w / 2, y + h / 2); host.Input.PointerButton(0, x + w / 2, y + h / 2, true); host.Input.PointerButton(0, x + w / 2, y + h / 2, false);
            }
            else { host.Input.Key(2, true); host.Input.Key(2, false); }
            host.Update();
            Check(host.TryTakeIntent(out var intent) && intent.Document == document, "Actual typed packet emitted " + id); return handle(intent);
        }
        foreach (float density in new[] { 1f, 1.25f, 2f })
        {
            host.Resize(1280, 720, density); backend = new FakeMatch();
            using var menu = new InGameController(backend); using var presenter = new InGamePagePresenter(host, pages, menu); presenter.Open(); host.Update(); host.Render(1280, 720);
            Check(Activate(presenter.Document, "ingame_bots", value => presenter.Handle(value), true) && menu.Snapshot().Page == InGamePage.Bots, "Real bot controls open at density " + density);
            Check(Activate(presenter.Document, "ingame_bot_hunter", value => presenter.Handle(value)) && Activate(presenter.Document, "ingame_bot_apply", value => presenter.Handle(value)), "Real draft and apply controls reach authority seam");
            Check(Activate(presenter.Document, "ingame_bot_remove", value => presenter.Handle(value)) && pages.Top != presenter.Document, "Real destructive action opens native modal");
            Check(presenter.Back() && menu.Snapshot().Page == InGamePage.Bots && pages.Top == presenter.Document, "Native Back cancels confirmation and restores bot page");
            backend.State = backend.State with { CanManageBots = false }; presenter.Refresh(); Check(!host.FocusDocument(presenter.Document, "ingame_bot_apply"), "Actual native DOM disables lost authority");
            presenter.Back(); presenter.Back(); presenter.Dispose(); Check(presenter.TryTakeEffect(out var effect) && effect == InGameEffect.Resume, "Resume effect survives page retirement");
            resultsBackend = new FakeResults(); using var controller = new MatchResultsController(resultsBackend); using var results = new MatchResultsPagePresenter(host, pages, controller); results.Open(); host.Update(); host.Render(1280, 720);
            Check(host.TryGetElementBounds(results.Document, "results_map0", out _, out _, out float width, out float height) && width > 200 * density && height > 0, "Native result rows have readable geometry");
            Check(Activate(results.Document, "results_map0", value => results.Handle(value), true) && resultsBackend.Selected == "arena_0", "Native result row chooses actual keyed arena");
            resultsBackend.State = resultsBackend.State with { Maps = resultsBackend.State.Maps.Reverse().ToImmutableArray() }; results.Refresh();
            Check(Activate(results.Document, "results_map0", value => results.Handle(value)) && resultsBackend.Selected == "arena_9", "Reordered result row emits stable catalog identity");
            host.SetField(results.Document, "results_search", "Arena 2"); Check(Activate(results.Document, "results_search_apply", value => results.Handle(value)) && controller.Snapshot().Rows.Single().Key == "arena_2", "Native field explicit search reaches complete ballot");
            Check(host.ReadField(results.Document, "results_search") == "Arena 2", "Result refresh retains local search draft");
            Check(Activate(results.Document, "results_close", value => results.Handle(value)), "Native close queues pause transition"); results.Dispose(); Check(results.TryTakeEffect(out var resultEffect) && resultEffect == MatchResultsEffect.Close, "Close effect survives page retirement");
        }
        foreach (var action in Enum.GetValues<AimResultsAction>())
        {
            using var controller = new AimResultsController(report); using var presenter = new AimResultsPagePresenter(host, pages, controller); presenter.Open(); host.Update(); host.Render(1280, 720);
            string id = action == AimResultsAction.Retry ? "aim_results_retry" : action == AimResultsAction.ChangeDrill ? "aim_results_change" : "aim_results_exit";
            Check(Activate(presenter.Document, id, value => presenter.Handle(value), true), "Actual training result action is consumed"); presenter.Dispose(); Check(presenter.TryTakeAction(out var selected) && selected == action, "Training result effect survives document retirement");
        }
        // Construct the actual scene state without initializing graphics or
        // loading a room. No account, save, personal-best or network operation
        // runs: backend fixture and completion flag are controlled explicitly.
        var androidScene = new Scene(new(1280, 720), null!, null!, _ => { }, () => { }, initializeRuntime: false);
        androidScene.GameState.Mode = GameMode.Battle;
        androidScene.GameState.MatchState = MatchState.Ending;
        resultsBackend = new FakeResults();
        int pauses = 0;
        var trainingActions = new List<(AimTrainerSession Session, AimResultsAction Action)>();
        using (var androidResults = new AndroidRmlUiResultsSession(host, pages, () => pauses++,
            (session, action) => { Check(pages.Page == default, "Android retires the result document before a platform transition"); trainingActions.Add((session, action)); }, resultsBackend))
        {
            Check(androidResults.OpenIfReady(androidScene, false) && androidResults.Visible && EndScreen.PanelUp, "Android uses its existing native host for actual scene results");
            var beforeMenu = androidResults.Document;
            Check(host.FocusedElement() == "results_close", "Opening Android results preserves authored initial focus");
            androidResults.Update(androidScene, true);
            Check(!androidResults.Visible && !EndScreen.PanelUp && !host.IsAlive(beforeMenu), "Android pause/settings/HUD retire match results");
            androidResults.Update(androidScene, false);
            Check(androidResults.Visible && androidResults.Document != beforeMenu, "Results can return after a menu closes");
            Check(androidResults.Back() && pauses == 1 && !androidResults.Visible && !EndScreen.PanelUp, "Android Back retires results and requests the existing pause menu once");
            androidResults.Update(androidScene, false);
            Check(!androidResults.Visible && !androidResults.Back() && pauses == 1, "Dismissed Android results do not reopen during the same result phase");
            androidScene.GameState.MatchState = MatchState.InProgress; androidResults.Update(androidScene, false);
            androidScene.GameState.MatchState = MatchState.Ending; androidResults.Update(androidScene, false);
            Check(androidResults.Visible, "A new authoritative result phase restores Android result availability");
            Check(Activate(androidResults.Document, "results_rematch", value => androidResults.HandleIntent(value), true)
                && resultsBackend.RematchCount == 1 && !androidResults.Visible && pauses == 1, "Android rematch delegates once to the existing generation-guarded match hook");
            Check(Task.Run(() => { try { androidResults.Close(); return false; } catch (InvalidOperationException) { return true; } }).Result, "Android result lifetime is guarded by render ownership");

            var trainingPlan = new LaunchPlan { Kind = LaunchKind.AimTrainer, Hunter = Hunter.Samus, Training = AimTrainerDefinition.Default };
            AimTrainerSession.Attach(androidScene, trainingPlan);
            var session = androidScene.AimTrainer!;
            typeof(AimTrainerSession).GetProperty(nameof(AimTrainerSession.Completed))!.SetValue(session, true);
            typeof(SceneGameState).GetProperty(nameof(SceneGameState.MenuPause))!.SetValue(androidScene.GameState, true);
            typeof(AimTrainerStats).GetProperty(nameof(AimTrainerStats.Score))!.SetValue(session.Stats, 321);
            Check(androidResults.OpenIfReady(androidScene, false) && androidResults.TrainingVisible && session.ResultsShown && !EndScreen.PanelUp,
                "Android completed training opens native immutable actual statistics and marks presentation only after open");
            var beforeSuspend = androidResults.Document;
            androidResults.Update(androidScene, true);
            Check(!androidResults.Visible && !session.ResultsShown && !host.IsAlive(beforeSuspend), "Suspended completed training remains available after the other menu");
            androidResults.Update(androidScene, false);
            Check(androidResults.TrainingVisible && session.ResultsShown && androidResults.Document != beforeSuspend, "Completed training returns on the same authoritative session");
            Check(androidResults.Back() && trainingActions.Count == 1 && ReferenceEquals(trainingActions[0].Session, session)
                && trainingActions[0].Action == AimResultsAction.Exit && !androidResults.Visible, "Android training Back hands the exact completed session to the existing exit route");
            androidResults.Update(androidScene, false);
            Check(!androidResults.Visible && trainingActions.Count == 1, "Completed training cannot repeat its accepted platform transition");
        }
        typeof(SceneGameState).GetProperty(nameof(SceneGameState.MenuPause))!.SetValue(androidScene.GameState, false);
        androidScene.GameState.MatchState = MatchState.Ending;
        for (int cycle = 0; cycle < 10; cycle++)
        {
            using var androidResults = new AndroidRmlUiResultsSession(host, pages, () => { }, (_, _) => { }, new FakeResults());
            // The completed training was already presented; results should
            // remain suppressed for that training scene's whole lifetime.
            Check(!androidResults.OpenIfReady(androidScene, false), "Retired completed training does not become a multiplayer result page");
        }
        Check(pages.Page == default && !EndScreen.PanelUp, "Android result retirement leaves shared page ownership clean");
    }
    finally { NativeLibrary.Free(module); }
}
Console.WriteLine($"In-game/results contracts passed: {checks} assertions; authority, generation, team capacity, handicap, confirmation return, real service seams, ballot identity, offline rematch, immutable actual training statistics" + (args.Length > 0 ? ", actual native DOM/densities/queued effects." : "."));

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
