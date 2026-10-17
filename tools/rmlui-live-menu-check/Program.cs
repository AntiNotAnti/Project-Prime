using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Training;
using MphRead.Entities;

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

// Native Hunter/suit selection shares the live lobby identity epoch and only
// issues one existing Identify request. No client-side admission is invented.
services.LobbyView = services.LobbyView with { Active = true, Owner = true };
services.HunterDraft = new RmlUiHunterDraft(5, 999, Hunter.Samus, 0,
    new[] { Hunter.Samus, Hunter.Kanden }, false);
Check(controller.Dispatch(RmlUiIntentKind.HunterOpen)
    && controller.HunterOpen && Flag(controller.Tick(), "lobby_hunter_open"),
    "Hunter picker opens from a live lobby identity");
var hunterBindings = RmlUiMenuPageBindings.Project(
    RmlUiMenuPage.Hunters, controller.Tick().Bindings);
Check(hunterBindings.TryGetValue("disabled:hunter_choice2", out var disabledTrace)
    && disabledTrace.Boolean
    && hunterBindings.TryGetValue("class:hunter_choice0:selected", out var selectedSamus)
    && selectedSamus.Boolean,
    "native Hunter modal disables restricted hunters and highlights current selection");
Check(!controller.Dispatch(RmlUiIntentKind.HunterSelect, (int)Hunter.Trace)
    && controller.Dispatch(RmlUiIntentKind.HunterSelect, (int)Hunter.Kanden)
    && controller.Dispatch(RmlUiIntentKind.HunterSuit, 2)
    && Text(controller.Tick(), "hunter_identity_name").Contains("KANDEN")
    && services.HunterApplyCalls == 0,
    "Hunter and suit changes remain local until Apply");
Check(controller.Dispatch(RmlUiIntentKind.HunterApply)
    && !controller.HunterOpen && services.HunterApplyCalls == 1
    && services.SentHunter == Hunter.Kanden && services.SentSuit == 2
    && services.SentEpoch == 999,
    "Apply emits precisely one current-authority identity request");
Check(!controller.Dispatch(RmlUiIntentKind.HunterApply)
    && services.HunterApplyCalls == 1,
    "closed identity picker cannot double-send");
Check(controller.Dispatch(RmlUiIntentKind.HunterOpen)
    && controller.Dispatch(RmlUiIntentKind.HunterCancel)
    && services.HunterApplyCalls == 1,
    "Hunter selection cancellation never persists a draft");
Check(controller.Dispatch(RmlUiIntentKind.HunterOpen), "stale-session test opens");
services.HunterDraft = services.HunterDraft.Value with { AuthorityEpoch = 1000 };
Check(!controller.Dispatch(RmlUiIntentKind.HunterApply)
    && services.HunterApplyCalls == 1,
    "stale authority epoch cannot submit Hunter changes");
Check(!Flag(controller.Tick(), "lobby_hunter_open"),
    "render-thread snapshot retires invalidated Hunter modal");

// RML-23: restored admin screen retains server-authored slot generations and
// only publishes a destructive command after explicit confirmation.
services.LobbyView = services.LobbyView with { Active = true, Owner = true };
services.AdminState = new RmlUiAdminSnapshot(5, 999, 17, true, 0, 0, false, false,
    2, 8, false, "MAP READY", "", new[] { Hunter.Samus, Hunter.Kanden },
    new[]
    {
        new RmlUiAdminPlayer(0, 5, "Pilot", Hunter.Samus, 0, 0, true, false, 0, 0, 20),
        new RmlUiAdminPlayer(1, 6, "Opponent", Hunter.Trace, 1, 1, false, false, 0, 0, 45),
        new RmlUiAdminPlayer(2, 8, "Practice", Hunter.Kanden, 2, 0, false, true, 3, 10, 0)
    }, new[] { "Pilot: hello" });
Check(controller.Dispatch(RmlUiIntentKind.LobbyAdminOpen) && controller.AdminOpen
    && Flag(controller.Tick(), "lobby_admin_open"),
    "native roster admin opens on live authoritative lobby");
var adminBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Admin, controller.Tick().Bindings);
Check(adminBindings.TryGetValue("admin_player1_name", out var otherName)
    && otherName.Text == "Opponent"
    && adminBindings.TryGetValue("disabled:admin_kick", out var ownerKick)
    && ownerKick.Boolean,
    "native lobby admin binds roster rows but cannot kick selected owner");
Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 1)
    && controller.Dispatch(RmlUiIntentKind.LobbyKick)
    && Flag(controller.Tick(), "admin_confirmation_open")
    && services.AdminCalls == 0,
    "kick requires user confirmation and never sends on first click");
Check(controller.Dispatch(RmlUiIntentKind.LobbyAdminCancel)
    && services.AdminCalls == 0
    && !Flag(controller.Tick(), "admin_confirmation_open"),
    "admin Cancel safely discards destructive request");
Check(controller.Dispatch(RmlUiIntentKind.LobbyKick)
    && controller.Dispatch(RmlUiIntentKind.LobbyAdminConfirm)
    && services.AdminCalls == 1
    && services.LastAdmin.Operation == RmlUiAdminOperation.Kick
    && services.LastAdmin.TargetSlot == 1 && services.LastAdmin.TargetGeneration == 6,
    "confirmed kick targets exact roster generation with one submission");
Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 2)
    && controller.Dispatch(RmlUiIntentKind.LobbyBotRemove)
    && services.AdminCalls == 1,
    "bot removal is also staged behind confirmation");
services.AdminState = services.AdminState with
{
    Players = services.AdminState.Players.Select(p =>
        p.Slot == 2 ? p with { Generation = 9 } : p).ToArray()
};
Check(!controller.Dispatch(RmlUiIntentKind.LobbyAdminConfirm)
    && services.AdminCalls == 1
    && !Flag(controller.Tick(), "admin_confirmation_open"),
    "stale bot slot generation cancels pending removal instead of deleting replacement");
Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 2)
    && controller.Dispatch(RmlUiIntentKind.LobbyBotHunterNext)
    && controller.Dispatch(RmlUiIntentKind.LobbyBotSuitNext)
    && controller.Dispatch(RmlUiIntentKind.LobbyBotAdd)
    && services.AdminCalls == 2
    && services.LastAdmin.Operation == RmlUiAdminOperation.AddBot,
    "bot configuration uses existing server command path");
services.AdminState = services.AdminState with { CommandPending = true };
Check(!controller.Dispatch(RmlUiIntentKind.LobbyBotAdd) && services.AdminCalls == 2,
    "one outstanding authoritative command blocks duplicate administration");
services.AdminState = services.AdminState with { CommandPending = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyChatSend, adminChat: "A message")
    && services.AdminCalls == 3 && services.LastAdmin.Text == "A message",
    "native chat sends the entered field through existing service");
services.AdminState = services.AdminState with { Owner = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 1)
    && !controller.Dispatch(RmlUiIntentKind.LobbyKick)
    && services.AdminCalls == 3,
    "non-owner cannot request a kick or transfer");
services.AdminState = services.AdminState with { AuthorityEpoch = 1000 };
Check(!Flag(Wait(controller, s => !Flag(s, "lobby_admin_open")), "lobby_admin_open"),
    "retired authority automatically closes native admin and its confirmations");
Check(!controller.Dispatch(RmlUiIntentKind.LobbyAdminConfirm) && services.AdminCalls == 3,
    "retired admin never dispatches an old confirmed action");
services.LobbyView = Stub.EmptyLobby;


// Preview is a read-only launch candidate; rejected match rules cannot
// persist a changed map or previous Hunter loadout.
var previewSettings = new MenuSettings { RoomKey = "UNCHANGED MAP" };
var previewPlan = OfflineLaunch.Preview(previewSettings, "MP1 SANCTORUS",
    GameMode.Battle, Hunter.Kanden, 2, 3, 1);
Check(previewSettings.RoomKey == "UNCHANGED MAP"
    && previewPlan.RoomKey == "MP1 SANCTORUS" && previewPlan.Bots == 3
    && previewPlan.BotLevel == 1,
    "offline preview remains side-effect-free before local launch admission");

// RML-25: a native local match draft is distinct from a server admission,
// never persists global preferences, and may only hand off after a worker
// validates the current map and mode against the existing engine contract.
var offlinePager = new RmlUiOfflineDraft(
    Enumerable.Range(0, 36).Select(i => $"Arena {i:00}"),
    Hunter.Kanden, 3, 7, 2);
Check(offlinePager.Current.Hunter == Hunter.Kanden
    && offlinePager.Current.Suit == 3 && offlinePager.Current.Bots == 7
    && offlinePager.Change(3) && offlinePager.Current.Bots == 0,
    "native offline local match choices remain bounded and wrap safely");
Check(offlinePager.OpenArena()
    && offlinePager.ArenaPageCount == 3 && offlinePager.ArenaChoice(0) == "Arena 00"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 16"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 32"
    && !offlinePager.ArenaTurn(1),
    "original sixteen-map native picker pages through installed map names");
Check(offlinePager.SearchArena("arena 04")
    && offlinePager.ArenaPageCount == 1
    && offlinePager.ArenaChoice(0) == "Arena 04"
    && offlinePager.SelectArena(0)
    && offlinePager.Current.RoomKey == "Arena 04" && !offlinePager.ArenaOpen,
    "native arena search resets page cursor and selects exact verified item");
Check(offlinePager.OpenArena() && !offlinePager.SearchArena(new string('x',81))
    && !offlinePager.SelectArena(-1) && offlinePager.CloseArena(),
    "invalid search length and forged map slot do not change the offline request");

services.OfflineValidationPending =
    new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
    && controller.OfflineOpen && Flag(controller.Tick(), "offline_mode")
    && !Flag(controller.Tick(), "lobby_mode"),
    "native Offline Battle opens without requesting the legacy workspace");
Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 5)
    && Text(controller.Tick(), "offline_hunter_value") == "KANDEN",
    "Hunter choice updates native offline draft, not launcher preferences");
Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenArena)
    && Flag(controller.Tick(), "offline_arena_open"),
    "offline arena launches an owned native modal");
Check(controller.Dispatch(RmlUiIntentKind.OfflineArenaSearch, arenaSearch: "sanctorus")
    && Text(controller.Tick(), "offline_arena_name0") == "MP1 SANCTORUS",
    "authored arena modal receives case-insensitive installed map search results");
var offlineBindings = RmlUiMenuPageBindings.Project(
    RmlUiMenuPage.OfflineArena, controller.Tick().Bindings);
Check(offlineBindings.TryGetValue("offline_arena_name_0", out var firstArena)
    && firstArena.Text == "MP1 SANCTORUS"
    && offlineBindings.TryGetValue("disabled:offline_arena_pick_0", out var firstArenaDisabled)
    && !firstArenaDisabled.Boolean,
    "native sixteen-row arena bindings show and enable a valid map");
Check(controller.Dispatch(RmlUiIntentKind.OfflineSelectArena, 0)
    && !Flag(controller.Tick(), "offline_arena_open")
    && Text(controller.Tick(), "offline_arena_value") == "MP1 SANCTORUS",
    "choosing an arena retires only the selector modal");
Check(controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
    && controller.Busy && services.OfflineValidations == 1
    && services.OfflineLaunches == 0
    && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
    "native offline launch is single-flight while map capabilities validate");
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
    && !controller.OfflineOpen && !Flag(controller.Tick(), "offline_mode"),
    "leaving native Offline invalidates pending validation");
services.OfflineValidationPending.SetResult("");
Thread.Sleep(40);
Check(services.OfflineLaunches == 0,
    "cancelled validation never creates an old offline session");
services.OfflineValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
    && controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
    "second validated offline attempt creates a new epoch");
services.OfflineValidationPending.SetResult("");
Check(Flag(Wait(controller, _ => services.OfflineLaunches == 1), "offline_mode")
    && services.OfflineLaunches == 1
    && services.LastOffline.RoomKey == "MP1 SANCTORUS"
    && services.LastOffline.Mode == GameMode.Battle,
    "successful map validation hands one immutable request to existing local launcher");
Check(!controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch)
    && services.OfflineLaunches == 1,
    "accepted offline launch is one-shot until a fresh route creates a draft");
controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);

// RML-26: the existing AimTrainerDefinition is authoritative. Its native
// editor cannot create a mode/target count that the engine would reject.
var imperialist = new RmlUiTrainingDraft(AimTrainerDefinition.Default with
{
    Drill = AimTrainerDrill.ImperialistPrecision,
    Weapon = BeamType.PowerBeam,
    FixedSeed = true,
    Seed = 27
}, Hunter.Trace);
Check(imperialist.Current.Weapon == BeamType.Imperialist
    && !imperialist.Choice(9) && imperialist.Current.TargetCount == 1,
    "Imperialist Precision forces Imperialist and locks its weapon picker");
Check(imperialist.Choice(8) && imperialist.Current.Drill == AimTrainerDrill.WeaponMastery,
    "drill cycle uses the authoritative training drill enum");
Check(imperialist.Toggle(4) && !imperialist.Current.FixedSeed,
    "training fixed-seed option updates only the local drill draft");
var headshots = new RmlUiTrainingDraft(AimTrainerDefinition.Default with
{ Drill = AimTrainerDrill.HeadshotPrecision }, Hunter.Samus);
Check(headshots.Current.HeadshotsOnly && !headshots.Toggle(0)
    && !headshots.CanChangeHeadshots, "precision mode cannot disable required headshots");
var timed = new RmlUiTrainingDraft(AimTrainerDefinition.Default with
{ Drill = AimTrainerDrill.TimedFlick, TargetCount = 7 }, Hunter.Samus);
Check(timed.Current.TargetCount == 1 && !timed.Choice(11),
    "Timed Flick is always a single-target drill");
var multiple = new RmlUiTrainingDraft(AimTrainerDefinition.Default with
{ Drill = AimTrainerDrill.MultiTargetFlick, TargetCount = 7 }, Hunter.Samus);
Check(multiple.Current.TargetCount == 5
    && multiple.Choice(11) && multiple.Current.TargetCount == 4
    && multiple.Choice(11) && multiple.Current.TargetCount == 5,
    "Multi Target Flick remains within four or five targets");
Check(!multiple.Choice(16) && !multiple.Toggle(5),
    "out-of-range training choices and flags are rejected");

services.TrainingDefault = AimTrainerDefinition.Default with { FixedSeed = true, Seed = 23 };
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Training)
    && controller.OfflineOpen && Flag(controller.Tick(), "offline_training_mode")
    && !Flag(controller.Tick(), "lobby_mode"),
    "Aim Lab opens the native Offline workspace with training focus");
Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 8)
    && Text(controller.Tick(), "offline_drill_value") == "Flick",
    "the native drill control changes current training draft only");
Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenTrainingOptions)
    && controller.TrainingOptionsOpen
    && Flag(controller.Tick(), "offline_training_open"),
    "advanced training opens the original native options modal");
Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 13)
    && Text(controller.Tick(), "offline_training_difficulty_value") == "Advanced",
    "advanced training difficulty changes only the draft");
Check(controller.Dispatch(RmlUiIntentKind.OfflineTrainingToggle, 1)
    && Text(controller.Tick(), "offline_training_toggle1").Contains("OFF"),
    "native Infinite Ammo toggle uses the actual AimTrainerDefinition field");
var trainingBindings = RmlUiMenuPageBindings.Project(
    RmlUiMenuPage.OfflineTraining, controller.Tick().Bindings);
Check(trainingBindings.TryGetValue("offline_training_difficulty_value", out var difficultyBinding)
    && difficultyBinding.Text == "Advanced"
    && trainingBindings.TryGetValue("offline_training_toggle_1", out var ammoBinding)
    && ammoBinding.Text.Contains("OFF"),
    "advanced RML modal binds live labels rather than legacy fake values");
Check(!controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining)
    && services.TrainingLaunches == 0,
    "training cannot launch from beneath the advanced modal");
Check(controller.Dispatch(RmlUiIntentKind.OfflineCloseTrainingOptions)
    && !controller.TrainingOptionsOpen
    && controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining)
    && services.TrainingLaunches == 1
    && services.LastTraining.Drill.Drill == AimTrainerDrill.Flick
    && services.LastTraining.Drill.Seed == 23
    && services.LastTraining.Hunter == Hunter.Samus,
    "native Aim Lab submits exactly one sanitized drill to existing launcher");
Check(!controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining)
    && services.TrainingLaunches == 1,
    "accepted Aim Lab launch rejects duplicate clicks");
controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);

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
    public RmlUiHunterDraft? HunterDraft;
    public int HunterApplyCalls;
    public Hunter SentHunter;
    public byte SentSuit;
    public ulong SentEpoch;
    public int AdminCalls;
    public RmlUiAdminRequest LastAdmin;
    public RmlUiAdminSnapshot? AdminState;
    public AimTrainerDefinition TrainingDefault = AimTrainerDefinition.Default;
    public int TrainingLaunches;
    public (AimTrainerDefinition Drill, Hunter Hunter, int Suit) LastTraining;
    public TaskCompletionSource<string> OfflineValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public RmlUiOfflineRequest LastOffline;
    public int OfflineValidations, OfflineLaunches;
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
    public Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
    {
        OfflineValidations++;
        return OfflineValidationPending.Task.WaitAsync(token);
    }
    public AimTrainerDefinition TrainingDefaults() => TrainingDefault;
    public bool LaunchTraining(AimTrainerDefinition drill, Hunter hunter, int suit)
    {
        TrainingLaunches++;
        LastTraining = (drill, hunter, suit);
        return true;
    }
    public bool LaunchOffline(RmlUiOfflineRequest request)
    {
        if (!request.ValidateForm(BuiltInMaps)) return false;
        LastOffline = request;
        OfflineLaunches++;
        return true;
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
    public RmlUiHunterDraft? ReadHunterDraft() => LobbyView.Active ? HunterDraft : null;
    public bool ApplyHunterDraft(Hunter hunter, byte suit, ushort matchId, ulong authorityEpoch)
    {
        if (HunterDraft is not { } current || current.CommandPending
            || current.MatchId != matchId || current.AuthorityEpoch != authorityEpoch
            || !current.Allowed.Contains(hunter) || suit > 3) return false;
        HunterApplyCalls++;
        SentHunter = hunter;
        SentSuit = suit;
        SentEpoch = authorityEpoch;
        return true;
    }
    public RmlUiAdminSnapshot? ReadAdmin() => LobbyView.Active ? AdminState : null;
    public bool SubmitAdmin(RmlUiAdminRequest request)
    {
        if (AdminState == null || request.MatchId != AdminState.MatchId
            || request.AuthorityEpoch != AdminState.AuthorityEpoch
            || request.SessionRevision != AdminState.SessionRevision
            || AdminState.CommandPending) return false;
        if (request.TargetSlot != byte.MaxValue && !AdminState.Players.Any(p =>
            p.Slot == request.TargetSlot && p.Generation == request.TargetGeneration))
            return false;
        if (!AdminState.Owner && request.Operation != RmlUiAdminOperation.SendChat)
            return false;
        AdminCalls++;
        LastAdmin = request;
        return true;
    }
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
