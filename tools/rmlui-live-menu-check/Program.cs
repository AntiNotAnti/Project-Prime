using System;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;

static void Check(bool pass, string name)
{
    if (!pass) throw new InvalidOperationException(name);
    Console.WriteLine("RML03 PASS " + name);
}
static bool Flag(RmlUiMenuSnapshot snapshot, string key)
    => snapshot.Bindings.TryGetValue(key, out var value)
       && value.Kind == RmlUiBindingKind.Boolean && value.Boolean;
static string Text(RmlUiMenuSnapshot snapshot, string key)
    => snapshot.Bindings.TryGetValue(key, out var value) ? value.Text : "";
static RmlUiMenuSnapshot Wait(RmlUiMenuController controller, Func<RmlUiMenuSnapshot, bool> predicate)
{
    for (int i = 0; i < 150; i++)
    {
        var snapshot = controller.Tick();
        if (predicate(snapshot)) return snapshot;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Timed out waiting for a versioned native presentation result.");
}

var services = new Stub();
using var controller = new RmlUiMenuController(services);
var home = controller.Tick();
Check(Text(home, "player_name") == "Pilot" && Text(home, "hunter_name") == "SAMUS"
    && !Flag(home, "lobby_mode") && !Flag(home, "multiplayer_mode"),
    "Home uses presentation-neutral current player and no fabricated session");
Check(controller.Tick().Revision == home.Revision,
    "unchanged native snapshots retain revision");

services.BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.PlayBrowse), "browser starts");
Check(controller.Busy && Flag(controller.Tick(), "play_busy"), "search status is visible but nonblocking");
services.BrowsePending.SetResult(new(true, "2 servers answered.", new[]
{
    new RmlUiServerCandidate("Test Arena", "127.0.0.1:27888", 2, 8, 35, true),
    new RmlUiServerCandidate("Old Protocol", "127.0.0.2:27888", 1, 8, 5, false)
}));
var directory = Wait(controller, s => Flag(s, "play_server0_present"));
Check(!controller.Busy && Text(directory, "play_server_count") == "1 LIVE"
    && Text(directory, "play_server0_name") == "Test Arena"
    && !Flag(directory, "play_server1_present"),
    "only compatible live directory rows are shown, with truthful counts");

services.JoinPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.PlayServer, 0, playerName: "Pilot"),
    "selected server starts authoritative join");
Check(controller.Joining && !controller.Dispatch(RmlUiIntentKind.PlayQuick),
    "duplicate admission is refused while connection is pending");
services.JoinPending.SetResult(new(true,
    new LaunchPlan { Kind = LaunchKind.Online, PlayerName = "Pilot", Hunter = Hunter.Samus },
    ""));
var joined = Wait(controller, s => services.Accepted == 1);
Check(services.Accepted == 1 && !controller.Joining,
    "admitted plan is handed off exactly once on the owner thread");
services.LobbyView = new(true, false, true, true, false, true, true,
    "Lobby", "MP1 SANCTORUS", "BATTLE", "FFA", "WAITING FOR HUNTERS", 8,
    new[]
    {
        new RmlUiLobbyPlayer("Pilot", "SAMUS", false, true, false),
        new RmlUiLobbyPlayer("Other", "TRACE", true, false, false)
    });
var lobby = Wait(controller, s => Flag(s, "lobby_mode"));
Check(Flag(lobby, "lobby_owner") && Flag(lobby, "lobby_require_ready")
    && Text(lobby, "lobby_ready_count") == "1 READY"
    && Text(lobby, "slot1_name") == "Other"
    && !Flag(lobby, "multiplayer_mode"),
    "native lobby projects only authoritative rules and roster");
Check(controller.Dispatch(RmlUiIntentKind.LobbyReady) && services.ReadyCalls == 1,
    "ready sends server command without client-side acknowledgement");
Check(!Flag(controller.Tick(), "lobby_local_ready"),
    "local ready state remains false until server confirms it");
services.LobbyView = services.LobbyView with { LocalReady = true };
Check(Flag(Wait(controller, s => Flag(s, "lobby_local_ready")), "lobby_local_ready"),
    "server response updates ready projection");
Check(controller.Dispatch(RmlUiIntentKind.LobbyStart) && services.StartCalls == 1,
    "owner match start delegated to existing service");
Check(controller.Dispatch(RmlUiIntentKind.LobbyLeave) && services.LeaveCalls == 1,
    "leave delegates to existing lobby lifecycle");
services.LobbyView = Stub.EmptyLobby;
Check(!Flag(Wait(controller, s => !Flag(s, "lobby_mode")), "lobby_mode"),
    "closing session resets native lobby without local invented state");

services.BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
controller.Dispatch(RmlUiIntentKind.PlayBrowse);
controller.Dispatch(RmlUiIntentKind.PlayCancel);
services.BrowsePending.SetResult(new(true, "STALE DATA",
    new[] { new RmlUiServerCandidate("Stale", "127.0.0.3:27888", 1, 8, 9, true) }));
Thread.Sleep(50);
Check(Text(controller.Tick(), "play_status") == "READY TO DEPLOY",
    "late cancelled discovery cannot overwrite current page");
Check(!controller.Dispatch(RmlUiIntentKind.PlayServer, 8),
    "invalid ABI server index rejected before touching services");


// Native rule editing uses a private revision-fenced draft; it never changes
// the session until the host echoes UpdateMatch at a newer revision.
var sourceRules = new RmlUiRuleDraft(5, 999, 17,
    new MatchDefinition
    {
        RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle,
        Format = MatchFormat.FreeForAll, PointGoal = 7, TimeLimitSeconds = 420
    }, LobbyRuleFlags.RequireReady);
var editor = new RmlUiRuleEditor(sourceRules);
Check(editor.IsOn(0) && editor.IsOn(3) && editor.IsOn(4),
    "native 16-toggle sheet presents inverse health/powerup labels correctly");
for (int i = 0; i < 16; i++)
{
    bool before = editor.IsOn(i);
    Check(editor.Toggle(i) && editor.IsOn(i) != before && editor.Toggle(i)
          && editor.IsOn(i) == before, "rule toggle " + i + " is reversible");
}
Check(editor.Draft == sourceRules && !editor.Dirty,
    "cancelled local rules preserve the exact original session");
editor.Toggle(8); editor.Toggle(9);
Check(!editor.Validate(out string invalid) && invalid.Contains("Balanced Mode"),
    "current modifier conflicts still reject native drafts");
editor.Toggle(9); editor.Toggle(8);
Check(editor.NextFormat() && editor.Draft.Match.Mode == GameMode.BattleTeams,
    "native matchup cycling uses the current canonical format catalog");
Check(editor.TrySetLimits("7:30", "12", out _) &&
      editor.Draft.Match.TimeLimitSeconds == 450 && editor.Draft.Match.PointGoal == 12,
    "native time and score fields parse into existing wire values");
Check(!editor.TrySetLimits("7:65", "12", out _),
    "invalid time cannot be submitted to the server");
Check(!editor.SameSession(sourceRules with { SessionRevision = 18 }),
    "stale rule revisions are refused");

services.LobbyView = services.LobbyView with
{
    CanStart = true,
    Owner = true,
    Active = true
};
services.AuthorityRules = sourceRules;
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesOpen)
    && Flag(controller.Tick(), "lobby_rules_open"),
    "native rules modal opens from real authority snapshot");
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 0)
    && Text(controller.Tick(), "rules_toggle0") == "OFF"
    && services.Submissions == 0, "native edits remain private until Apply");
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesApply, ruleTime: "8:20", ruleGoal: "10")
    && services.Submissions == 1 && Flag(controller.Tick(), "rules_pending"),
    "owner Apply submits exactly one validated UpdateMatch draft");
Check(Flag(controller.Tick(), "lobby_rules_open"),
    "native sheet waits for authoritative revision instead of optimistic success");
services.AuthorityRules = services.SubmittedRule with { SessionRevision = 18 };
Check(!Flag(Wait(controller, s => !Flag(s, "lobby_rules_open")), "lobby_rules_open"),
    "server-confirmed matching rules retire local draft and modal");
services.LobbyView = services.LobbyView with { Owner = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesOpen)
    && !controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 1)
    && !controller.Dispatch(RmlUiIntentKind.LobbyRulesApply, ruleTime: "8:20", ruleGoal: "10"),
    "non-owner cannot mutate rule state");
controller.Dispatch(RmlUiIntentKind.LobbyRulesClose);
var mapPages = new RmlUiMapPickerModel(
    new[] { "G", "F", "E", "D", "C", "B", "A", "a" },
    new[] { "U", "V", "A", "v" });
Check(mapPages.Count == 7 && mapPages.Pages == 2
    && mapPages.Choice(0) == "A" && mapPages.Choice(5) == "F"
    && mapPages.Choice(-1) == null && mapPages.NextPage()
    && mapPages.Choice(0) == "G" && mapPages.Choice(1) == null,
    "native map picker deduplicates and caps pages to six stable choices");
Check(mapPages.SelectCategory(1) && mapPages.Count == 2
    && mapPages.Page == 0 && mapPages.Choice(0) == "U"
    && !mapPages.NextPage() && !mapPages.SelectCategory(2),
    "category change resets cursor and rejects out-of-range indices");
Check(mapPages.SelectCategory(0) && !mapPages.PreviousPage(),
    "first map page cannot move behind zero");

services.LobbyView = services.LobbyView with { Owner = true, Active = true };
services.AuthorityRules = sourceRules;
services.BuiltInMaps = new[] { "MP1 SANCTORUS" };
services.CommunityMaps = Array.Empty<string>();
int beforeMap = services.Submissions;
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen)
    && controller.MapPickerOpen
    && Flag(controller.Tick(), "lobby_map_open")
    && Flag(controller.Tick(), "lobby_rules_open")
    && Text(controller.Tick(), "map_choice0_name") == "MP1 SANCTORUS",
    "lobby opens a native map picker bound to current authoritative rule draft");
var mapBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Maps, controller.Tick().Bindings);
Check(mapBindings.TryGetValue("map_name0", out var mapText)
    && mapText.Text == "MP1 SANCTORUS"
    && mapBindings.TryGetValue("disabled:map_choice0", out var mapEnabled)
    && !mapEnabled.Boolean
    && mapBindings.TryGetValue("disabled:map_previous", out var mapPrevious)
    && mapPrevious.Boolean,
    "authored map modal receives native name/enable/page bindings");
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 1)
    && Flag(controller.Tick(), "map_empty")
    && !controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0),
    "empty community map category cannot stage missing packages");
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapCategory, 0)
    && controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
    && !controller.MapPickerOpen && Flag(controller.Tick(), "lobby_rules_open")
    && services.Submissions == beforeMap,
    "staging a built-in map opens rules editor without submitting UpdateMatch");
controller.Dispatch(RmlUiIntentKind.LobbyRulesClose);
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen)
    && controller.Dispatch(RmlUiIntentKind.LobbyMapClose)
    && !Flag(controller.Tick(), "lobby_rules_open"),
    "cancelled direct map picker does not leave hidden rule editor state");
services.LobbyView = services.LobbyView with { Owner = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen)
    && !controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
    && services.Submissions == beforeMap,
    "non-owner can preview but never select or submit server map updates");
controller.Dispatch(RmlUiIntentKind.LobbyMapClose);
services.LobbyView = Stub.EmptyLobby;


// The native Create Lobby form uses the same authoritative backend as legacy
// hosting. It has no local-simulation fallback and cannot be double-submitted.
services.CreatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.PlayCreateOpen)
    && Flag(controller.Tick(), "play_create_mode"),
    "native lobby form opens from current supported map choices");
Check(controller.Dispatch(RmlUiIntentKind.PlayNextMode)
    && Text(controller.Tick(), "play_create_mode_name") == "Survival",
    "native game mode cycles through canonical MatchTypeCatalog");
Check(controller.Dispatch(RmlUiIntentKind.PlayToggleHost)
    && Text(controller.Tick(), "play_create_host") == "LOCAL // DEDICATED",
    "authoritative host selection preserves explicit dedicated choice");
Check(controller.Dispatch(RmlUiIntentKind.PlayCreate, playerName: "Pilot", lobbyName: "Test Lobby")
    && controller.Creating && services.CreateCalls == 1,
    "native create uses one explicit authoritative allocation");
Check(!controller.Dispatch(RmlUiIntentKind.PlayCreate, playerName: "Pilot", lobbyName: "Duplicate")
    && !controller.Dispatch(RmlUiIntentKind.PlayCancel),
    "pending host allocation cannot double-start or disappear under a cancelled UI");
Check(services.CreatedRequest.Name == "Test Lobby"
    && services.CreatedRequest.RoomKey == "MP1 SANCTORUS"
    && services.CreatedRequest.Dedicated,
    "requested map/name/server topology reach the reusable backend unchanged");
services.CreatePending.SetResult(new(true,
    new LaunchPlan { Kind = LaunchKind.Host, PlayerName = "Pilot", Hunter = Hunter.Samus },
    ""));
Wait(controller, _ => services.Accepted == 2);
Check(!controller.Creating && !Flag(controller.Tick(), "play_create_mode")
    && services.Accepted == 2,
    "accepted native host handoff occurs once and closes the form");

Exception? wrongThread = null;
Task.Run(() =>
{
    try { controller.Tick(); }
    catch (Exception error) { wrongThread = error; }
}).GetAwaiter().GetResult();
Check(wrongThread is InvalidOperationException,
    "native state projection rejects worker-thread writes");
Console.WriteLine("RML03 owner-thread launcher service contracts passed.");

sealed class Stub : IRmlUiMenuServices
{
    public static readonly RmlUiLobbyView EmptyLobby = new(false, false, false, false,
        false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
    public TaskCompletionSource<RmlUiDirectory> BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<RmlUiJoinResult> JoinPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<RmlUiJoinResult> CreatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public LobbyCreationRequest CreatedRequest;
    public string[] BuiltInMaps = { "MP1 SANCTORUS" };
    public string[] CommunityMaps = Array.Empty<string>();
    public int CreateCalls;
    public RmlUiLobbyView LobbyView = EmptyLobby;
    public int Accepted, ReadyCalls, StartCalls, LeaveCalls, Submissions;
    public RmlUiRuleDraft? AuthorityRules;
    public RmlUiRuleDraft SubmittedRule;
    public HubSnapshot Hub() => new("Pilot", Hunter.Samus, Hunter.Samus, 0, true, "Desktop");
    public string BuildVersion() => "0.1.99";
    public RmlUiLobbyView Lobby() => LobbyView;
    public Task<RmlUiDirectory> Browse(CancellationToken _) => BrowsePending.Task;
    public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken _) =>
        Task.FromResult<RmlUiServerCandidate?>(null);
    public Task<RmlUiJoinResult> Join(string _, string __, CancellationToken ___) => JoinPending.Task;
    public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken _)
    {
        CreatedRequest = request;
        CreateCalls++;
        return CreatePending.Task;
    }
    public bool AcceptJoin(LaunchPlan _) { Accepted++; return true; }
    public bool OpenCreateLobby() => true;
    public bool NavigateTo(RmlUiRouteArgument _) => true;
    public bool OpenStudio() => true;
    public bool SetReady() { ReadyCalls++; return true; }
    public bool StartMatch() { StartCalls++; return true; }
    public bool LeaveLobby() { LeaveCalls++; LobbyView = EmptyLobby; return true; }
    public bool NextHunter() => true;
    public bool NextSuit() => true;
    public bool OpenLobbyDetails() => true;
    public RmlUiRuleDraft? ReadRules() => LobbyView.Active ? AuthorityRules : null;
    public IReadOnlyList<string> BuiltInArenaKeys() => BuiltInMaps;
    public IReadOnlyList<string> InstalledCommunityArenaKeys() => CommunityMaps;
    public bool SubmitRules(RmlUiRuleDraft draft, out string error)
    {
        error = "";
        if (!LobbyView.Owner || AuthorityRules?.SessionRevision != draft.SessionRevision)
            return false;
        SubmittedRule = draft;
        Submissions++;
        return true;
    }
    public void Quit() { }
}
