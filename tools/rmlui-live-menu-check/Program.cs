using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Network;
using MphRead.Mods.Movement;
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

int ownedAudioArg = Array.IndexOf(args, OwnedMenuAudioAcceptance.Argument);
if (ownedAudioArg >= 0)
{
    if (ownedAudioArg + 1 >= args.Length)
        throw new ArgumentException("--owned-menu-audio requires an absolute owned filesystem path.");
    OwnedMenuAudioAcceptance.Run(args[ownedAudioArg + 1]);
    return;
}

if (args.Contains(ReplayProjectFilesAcceptance.ChildArgument))
{
    ReplayProjectFilesAcceptance.RunIsolated();
    return;
}

if (args.Contains(NativeSettingsAcceptance.ChildArgument))
{
    NativeSettingsAcceptance.RunIsolated();
    return;
}

#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
// Source-linked async ownership checks intentionally avoid unrelated fixtures
// that reflect the prebuilt engine via the controller assembly identity.
if (args.Contains("--offline-rules-async"))
{
    OfflineRulesAsyncAcceptance.Run(); // Includes linked offline UI dispatcher checks.
    AdventureSaveAsyncAcceptance.Run();
    AndroidWorkAcceptance.Run();
    return;
}
#endif

LobbyHunterCardsAcceptance.Run(Check);

var services = new Stub();
using var controller = new RmlUiMenuController(services);
var home = controller.Tick();
Check(Text(home, "player_name") == "Pilot" && Text(home, "hunter_name") == "SAMUS"
    && !Flag(home, "lobby_mode") && !Flag(home, "multiplayer_mode"),
    "Home uses presentation-neutral current player and no fabricated session");
Check(Text(home, "home_feature_category") == "NEWS"
    && Text(home, "home_feature_title") == "PROJECT PRIME COMMUNITY UPDATE"
    && Flag(home, "home_feature_available"),
    "Home news rail projects the actual shared bundled dispatch source");
Check(Text(home, "home_session_state") == "SOLO"
    && !Flag(home, "home_social_ready")
    && Flag(home, "home_social_rail_visible") && Flag(home, "home_social_empty_visible")
    && Text(home, "home_social_summary").Contains("OPEN SOCIAL"),
    "Home never fabricates Social presence, notifications, friends or online counts");
var homeBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Home, home.Bindings);
Check(homeBindings.TryGetValue("home_feature_title", out var newsTitle)
    && newsTitle.Text == "PROJECT PRIME COMMUNITY UPDATE"
    && homeBindings.TryGetValue("disabled:home_social", out var socialButton)
    && socialButton.Boolean,
    "native Home feature rail is populated and unavailable Social action cannot mislead");
Check(controller.Tick().Revision == home.Revision,
    "unchanged native snapshots retain revision");

services.SocialOverride = new(true, true, new[] { "Pilot", "Fellow Hunter" }, 1);
var linkedSocial = Wait(controller, s => Flag(s, "home_social_ready") &&
    Flag(s, "home_social_rail_visible"));
Check(Text(linkedSocial, "home_social_summary").Contains("2 FRIEND PROFILES")
    && Text(linkedSocial, "home_friends_online") == "UNKNOWN"
    && Text(linkedSocial, "home_requests_count") == "1"
    && Flag(linkedSocial, "home_social_alert_visible")
    && Text(linkedSocial, "home_invites_count") == "NOT SYNCED",
    "Home uses actual authenticated social counts, never fabricated online/invite activity");
var socialBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Home, linkedSocial.Bindings);
Check(socialBindings.TryGetValue("disabled:home_social", out var activeSocial)
    && !activeSocial.Boolean,
    "native Social route becomes actionable only after its authenticated client is installed");
services.SocialOverride = new(false, false, Array.Empty<string>(), 0);
Wait(controller, s => !Flag(s, "home_social_ready"));

string FriendId(int value) => $"PP-AAAA-BBBB-CCCC-DDDD-{value:X4}";
var observedAt = new DateTimeOffset(2026, 10, 17, 18, 0, 0, TimeSpan.Zero);
var homeFriends = new NativeSocialDirectory(FriendId(1), new[]
{
    new NativeSocialPerson(FriendId(2), "Private friend", NativeSocialRelation.Friend),
    new NativeSocialPerson(FriendId(3), "Shared friend", NativeSocialRelation.Friend),
    new NativeSocialPerson(FriendId(4), "Expired friend", NativeSocialRelation.Friend),
    new NativeSocialPerson(FriendId(5), "Blocked player", NativeSocialRelation.Blocked)
});
var homePresence = new NativePresenceDirectory(FriendId(1), new("friends", "friends", "friends", false), new[]
{
    new NativePresencePerson(FriendId(3), "Shared friend", "in_match", "", "", false, observedAt),
    new NativePresencePerson(FriendId(4), "Expired friend", "menu", "", "", false, observedAt.AddSeconds(-45)),
    new NativePresencePerson(FriendId(5), "Blocked player", "menu", "", "", false, observedAt),
    new NativePresencePerson(FriendId(6), "Unrelated player", "menu", "", "", false, observedAt)
});
services.SocialOverride = RmlUiSocialHomeState.Project(homeFriends, homePresence, 2, observedAt);
var presenceHome = Wait(controller, s => Text(s, "home_friends_online") == "1");
Check(Text(presenceHome, "home_friend0_name") == "Shared friend"
    && Text(presenceHome, "home_friend0_activity") == "IN MATCH"
    && Text(presenceHome, "home_friend_count") == "3"
    && !Flag(presenceHome, "home_friend0_joinable"),
    "Home counts only shared unexpired friend presence and preserves Social route ownership");
Check(Text(presenceHome, "home_friend1_activity") == "PRESENCE NOT SHARED"
    && Text(presenceHome, "home_friend2_activity") == "PRESENCE NOT SHARED",
    "expired or privacy-hidden presence is not presented as offline");
services.SocialOverride = RmlUiSocialHomeState.Project(homeFriends,
    homePresence with { SelfPrimeId = FriendId(9) }, 2, observedAt);
var changedPresence = Wait(controller, s => Text(s, "home_friends_online") == "UNKNOWN");
Check(Text(changedPresence, "home_friend0_activity") == "PRESENCE UNAVAILABLE",
    "a different account's presence cannot populate Home friend activity");
services.SocialOverride = new(false, false, Array.Empty<string>(), 0);
Wait(controller, s => !Flag(s, "home_social_ready"));

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
Check((sourceRules.Match with { ThirdPersonCamera = true }).Rules.HasFlag(MatchModifierFlags.ThirdPersonCamera)
    && !sourceRules.Match.Rules.HasFlag(MatchModifierFlags.ThirdPersonCamera),
    "third-person match modifier has its own opt-in protocol bit");
var thirdPersonState = new SessionStatePacket
{
    Phase = SessionPhase.Lobby, Policy = ServerSessionPolicy.Lobby,
    MatchId = sourceRules.MatchId, AuthorityEpoch = sourceRules.AuthorityEpoch,
    Revision = sourceRules.SessionRevision, OwnerSlot = 0, MaxPlayers = 8,
    Match = sourceRules.Match with { ThirdPersonCamera = true }
};
byte[] thirdPersonWire = new byte[SessionStatePacket.Size];
thirdPersonState.Write(thirdPersonWire);
Check(SessionStatePacket.TryRead(thirdPersonWire, out var receivedThirdPerson)
    && receivedThirdPerson.Match.ThirdPersonCamera
    && receivedThirdPerson.Match.Rules.HasFlag(MatchModifierFlags.ThirdPersonCamera),
    "session packet round-trips the authoritative third-person match modifier");
Check(editor.IsOn(0) && editor.IsOn(3) && editor.IsOn(4),
    "native 19-choice sheet presents inverse health/powerup labels correctly");
for (int i = 0; i < RmlUiRuleEditor.ChoiceCount; i++)
{
    if (i == 17)
    {
        var beforeMovement = editor.Draft.Match.MovementModel;
        Check(editor.Toggle(i) && editor.MovementLabel == "Momentum"
            && editor.Toggle(i) && editor.MovementLabel == "Quake III"
            && editor.Toggle(i) && editor.MovementLabel == "Traversal"
            && editor.Toggle(i) && editor.MovementLabel == "Parkour"
            && editor.Draft.Match.MovementModel == MovementModel.Parkour
            && editor.Toggle(i) && editor.Draft.Match.MovementModel == beforeMovement
            && editor.Draft.Match.MovementModel == MovementModel.Native
            && editor.MovementLabel == "Classic", "movement choice completes its five-way cycle with explicit Parkour then Classic");
    }
    else
    {
        bool before = editor.IsOn(i);
        Check(editor.Toggle(i) && editor.IsOn(i) != before && editor.Toggle(i)
            && editor.IsOn(i) == before, "rule toggle " + i + " is reversible");
    }
}
Check(!editor.Toggle(-1) && !editor.Toggle(19) && editor.Draft == sourceRules,
    "expanded rule range still rejects invalid actions without changing draft");
Check(RmlUiRuleEditor.ChoiceCount == 19 && editor.MovementLabel == "Classic"
    && !editor.IsOn(17), "native rule draft defaults to Classic movement");
var movementEditor = new RmlUiRuleEditor(sourceRules);
Check(movementEditor.Toggle(17) && movementEditor.IsOn(17) && movementEditor.MovementLabel == "Momentum"
    && movementEditor.Draft.Match.MovementModel == MovementModel.Momentum
    && movementEditor.Toggle(16) && movementEditor.Draft.Match.ThirdPersonCamera,
    "movement and third-person choices remain independent draft fields");
Check(movementEditor.Toggle(17) && movementEditor.IsOn(17)
    && movementEditor.MovementLabel == "Quake III"
    && movementEditor.Draft.Match.MovementModel == MovementModel.Quake3
    && movementEditor.Validate(out _)
    && movementEditor.Toggle(17) && movementEditor.IsOn(17)
    && movementEditor.MovementLabel == "Traversal"
    && movementEditor.Draft.Match.MovementModel == MovementModel.Traversal
    && movementEditor.Validate(out _)
    && movementEditor.Toggle(17) && movementEditor.IsOn(17)
    && movementEditor.MovementLabel == "Parkour"
    && movementEditor.Draft.Match.MovementModel == MovementModel.Parkour
    && movementEditor.Validate(out _)
    && movementEditor.Toggle(17) && !movementEditor.IsOn(17)
    && movementEditor.Draft.Match.MovementModel == MovementModel.Native
    && movementEditor.MovementLabel == "Classic"
    && movementEditor.Toggle(17), "movement editor cycles all five choices with valid Quake III, Traversal and Parkour drafts");
Check(movementEditor.NextMode() && movementEditor.NextFormat()
    && movementEditor.TrySelectMap("MP2 HARVESTER", out _)
    && movementEditor.TrySetLimits("8:20", "10", out _)
    && movementEditor.Draft.Match.MovementModel == MovementModel.Momentum,
    "mode format map and numeric edits preserve selected Momentum");
Check(movementEditor.Toggle(8) && !movementEditor.Validate(out string movementConflict)
    && movementConflict.Contains("Momentum") && movementEditor.Draft.Match.MovementModel == MovementModel.Momentum,
    "Balanced refuses Momentum without silently replacing either selection");
var traversalEditor = new RmlUiRuleEditor(sourceRules with
    { Match = sourceRules.Match with { MovementModel = MovementModel.Traversal, BalancedMode = true } });
Check(!traversalEditor.Validate(out _) && traversalEditor.Draft.Match.MovementModel == MovementModel.Traversal,
    "Balanced conflict preserves Traversal selection");
Check(MovementModelRules.TryParse("Traversal", out var traversalParsed) && traversalParsed == MovementModel.Traversal
    && MovementModelRules.FromRules(MatchModifierFlags.TraversalMovement) == MovementModel.Traversal
    && MovementModelRules.ProfileVersion(MovementModel.Traversal) == 5
    && new MatchDefinition().MovementModel == MovementModel.Native,
    "Traversal model maps its fixed profile without changing Classic default");
foreach (var conflicting in new[] { MatchModifierFlags.MomentumMovement, MatchModifierFlags.Quake3Movement })
    Check(!MovementModelRules.ValidRules(conflicting | MatchModifierFlags.TraversalMovement)
        && !MovementModelRules.IsDefined(MovementModelRules.FromRules(conflicting | MatchModifierFlags.TraversalMovement)),
        "Traversal refuses multiple movement bits: " + conflicting);
var unsupportedMovement = new RmlUiRuleEditor(sourceRules with
    { Match = sourceRules.Match with { MovementModel = (MovementModel)255 } });
Check(unsupportedMovement.MovementLabel == "Unsupported" && !unsupportedMovement.Toggle(17)
    && !unsupportedMovement.Validate(out _) && unsupportedMovement.Draft.Match.MovementModel == (MovementModel)255,
    "unknown authoritative enum remains explicit and invalid instead of auto-repairing");
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
Check(Text(controller.Tick(), "rules_toggle17") == "CLASSIC"
    && controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17)
    && Text(controller.Tick(), "rules_toggle17") == "MOMENTUM"
    && services.AuthorityRules?.Match.MovementModel == MovementModel.Native,
    "movement label distinguishes unsubmitted local draft from authoritative Classic");
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesApply, ruleTime: "8:20", ruleGoal: "10")
    && services.Submissions == 1 && Flag(controller.Tick(), "rules_pending"),
    "owner Apply submits exactly one validated UpdateMatch draft");
Check(Flag(controller.Tick(), "lobby_rules_open") && Text(controller.Tick(), "rules_toggle17") == "MOMENTUM"
    && !controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17)
    && services.SubmittedRule.Match.MovementModel == MovementModel.Momentum,
    "pending movement request remains visible and immutable until authoritative confirmation");
services.AuthorityRules = services.SubmittedRule with { SessionRevision = 18 };
Check(!Flag(Wait(controller, s => !Flag(s, "lobby_rules_open")), "lobby_rules_open"),
    "server-confirmed matching rules retire local draft and modal");
services.LobbyView = services.LobbyView with { Owner = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesOpen)
    && !controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 1)
    && Text(controller.Tick(), "rules_toggle17") == "MOMENTUM"
    && !controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17)
    && !controller.Dispatch(RmlUiIntentKind.LobbyRulesApply, ruleTime: "8:20", ruleGoal: "10"),
    "non-owner cannot mutate rule state");
controller.Dispatch(RmlUiIntentKind.LobbyRulesClose);
// Independent controller instances prove reload/epoch fencing without sharing
// a pending movement editor with the subsequent map/roster acceptance cases.
var movementServices = new Stub { LobbyView = services.LobbyView with { Owner = true, Active = true }, AuthorityRules = sourceRules };
using (var movementController = new RmlUiMenuController(movementServices))
{
    string MovementText() => Text(movementController.Tick(), "rules_toggle17");
    Check(movementController.Dispatch(RmlUiIntentKind.LobbyRulesOpen) && MovementText() == "CLASSIC"
        && movementController.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17) && MovementText() == "MOMENTUM",
        "fresh host editor projects exact authoritative default and private movement choice");
    movementServices.AuthorityRules = sourceRules with { SessionRevision = 18 };
    Check(!movementController.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17)
        && !movementController.Dispatch(RmlUiIntentKind.LobbyRulesApply, ruleTime: "7:00", ruleGoal: "7")
        && movementServices.Submissions == 0 && MovementText() == "MOMENTUM",
        "stale revision cannot submit or silently overwrite the local movement draft");
    movementController.Dispatch(RmlUiIntentKind.LobbyRulesClose);
    Check(movementController.Dispatch(RmlUiIntentKind.LobbyRulesOpen) && MovementText() == "CLASSIC",
        "reopening stale editor reloads authoritative movement instead of retaining old draft");
    movementController.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 17);
    movementServices.AuthorityRules = sourceRules with { AuthorityEpoch = 1000, SessionRevision = 1 };
    Check(!Flag(movementController.Tick(), "lobby_rules_open")
        && movementController.Dispatch(RmlUiIntentKind.LobbyRulesOpen) && MovementText() == "CLASSIC",
        "authority epoch change retires private Momentum and reloads new authoritative Classic");
    movementServices.AuthorityRules = sourceRules with { MatchId = 6, AuthorityEpoch = 1000, SessionRevision = 2,
        Match = sourceRules.Match with { MovementModel = MovementModel.Momentum } };
    Check(!Flag(movementController.Tick(), "lobby_rules_open")
        && movementController.Dispatch(RmlUiIntentKind.LobbyRulesOpen) && MovementText() == "MOMENTUM",
        "new match identity retires prior editor and displays its accepted Momentum rule");
}
using (var reloadedMovementController = new RmlUiMenuController(movementServices))
{
    Check(reloadedMovementController.Dispatch(RmlUiIntentKind.LobbyRulesOpen)
        && Text(reloadedMovementController.Tick(), "rules_toggle17") == "MOMENTUM",
        "controller reload derives movement from current server definition");
    movementServices.LobbyView = movementServices.LobbyView with { Active = false };
    Check(!Flag(reloadedMovementController.Tick(), "lobby_rules_open"), "leaving session retires movement display draft");
    movementServices.LobbyView = movementServices.LobbyView with { Active = true };
    movementServices.AuthorityRules = sourceRules with { MatchId = 7, AuthorityEpoch = 2000, SessionRevision = 1 };
    Check(reloadedMovementController.Dispatch(RmlUiIntentKind.LobbyRulesOpen)
        && Text(reloadedMovementController.Tick(), "rules_toggle17") == "CLASSIC",
        "new session restores its own Classic definition instead of prior Momentum");
}
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
    && !mapPages.NextPage() && !mapPages.SelectCategory(5),
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
    && Text(controller.Tick(), "map_choice0_name") == "Data Shrine",
    "lobby opens a native map picker bound to current authoritative rule draft");
var mapBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Maps, controller.Tick().Bindings);
Check(mapBindings.TryGetValue("map_name0", out var mapText)
    && mapText.Text == "Data Shrine"
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
    && !controller.MapPickerOpen && !Flag(controller.Tick(), "lobby_rules_open")
    && services.Submissions == beforeMap + 1
    && services.SubmittedRule.Match.RoomKey == "MP1 SANCTORUS"
    && services.SubmittedRule.Match.TimeLimitSeconds == sourceRules.Match.TimeLimitSeconds
    && services.SubmittedRule.Match.PointGoal == sourceRules.Match.PointGoal
    && services.SubmittedRule.Flags == sourceRules.Flags,
    "direct map selection submits a map-only request and returns to lobby without opening rules");
controller.Dispatch(RmlUiIntentKind.LobbyRulesClose);
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen)
    && controller.Dispatch(RmlUiIntentKind.LobbyMapClose)
    && !Flag(controller.Tick(), "lobby_rules_open"),
    "cancelled direct map picker does not leave hidden rule editor state");
services.LobbyView = services.LobbyView with { Owner = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyMapOpen)
    && !controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
    && services.Submissions == beforeMap + 1,
    "non-owner can preview but never select or submit server map updates");
controller.Dispatch(RmlUiIntentKind.LobbyMapClose);

services.LobbyView = services.LobbyView with { Owner = true };
int beforeRuleMap = services.Submissions;
Check(controller.Dispatch(RmlUiIntentKind.LobbyRulesOpen)
    && controller.Dispatch(RmlUiIntentKind.LobbyRulesToggle, 2)
    && controller.Dispatch(RmlUiIntentKind.LobbyMapOpen)
    && controller.Dispatch(RmlUiIntentKind.LobbyMapSelect, 0)
    && !controller.MapPickerOpen && Flag(controller.Tick(), "lobby_rules_open")
    && services.Submissions == beforeRuleMap,
    "picker opened from an existing rules draft preserves unapplied edits without submitting them");
controller.Dispatch(RmlUiIntentKind.LobbyRulesClose);

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
    && services.AdminCalls == 2 && services.ChatCalls == 1
    && services.LastChat == "A message",
    "native chat uses its dedicated existing NetChat service, not an owner-only admin command");
services.AdminState = services.AdminState with { Owner = false };
Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 1)
    && !controller.Dispatch(RmlUiIntentKind.LobbyKick)
    && services.AdminCalls == 2 && services.ChatCalls == 1,
    "non-owner cannot request a kick or transfer");
services.AdminState = services.AdminState with { AuthorityEpoch = 1000 };
Check(!Flag(Wait(controller, s => !Flag(s, "lobby_admin_open")), "lobby_admin_open"),
    "retired authority automatically closes native admin and its confirmations");
Check(!controller.Dispatch(RmlUiIntentKind.LobbyAdminConfirm)
    && services.AdminCalls == 2 && services.ChatCalls == 1,
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
    && offlinePager.ArenaPageCount == 6 && offlinePager.ArenaChoice(0) == "Arena 00"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 06"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 12"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 18"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 24"
    && offlinePager.ArenaTurn(1) && offlinePager.ArenaChoice(0) == "Arena 30"
    && !offlinePager.ArenaTurn(1),
    "native offline picker pages through six previewable installed map names");
Check(offlinePager.SearchArena("arena 04")
    && offlinePager.ArenaPageCount == 1
    && offlinePager.ArenaChoice(0) == "Arena 04"
    && offlinePager.SelectArena(0)
    && offlinePager.Current.RoomKey == "Arena 04" && !offlinePager.ArenaOpen,
    "native arena search resets page cursor and selects exact verified item");
Check(offlinePager.OpenArena() && !offlinePager.SearchArena(new string('x',81))
    && !offlinePager.SelectArena(-1) && offlinePager.CloseArena(),
    "invalid search length and forged map slot do not change the offline request");
var displayNames = new RmlUiOfflineDraft(
    new[] { "MP11 BREAKTHROUGH", "MP1 SANCTORUS" }, Hunter.Samus, 0, 0, 0);
Check(LauncherRoomDisplayName.Get("MP1 SANCTORUS") == "Data Shrine"
    && LauncherRoomDisplayName.Get("MP11 BREAKTHROUGH") == "Sanctorus"
    && displayNames.Current.RoomKey == "MP1 SANCTORUS"
    && displayNames.Map == "Data Shrine"
    && displayNames.OpenArena() && displayNames.SearchArena("sanctorus")
    && displayNames.ArenaCount == 2
    && displayNames.ArenaChoice(0) == "MP1 SANCTORUS"
    && displayNames.ArenaChoice(1) == "MP11 BREAKTHROUGH",
    "native map labels use InGameName while selection and search retain internal room keys");

Check(offlinePager.Current.Character == OfflineCharacterMode.Native && offlinePager.CharacterName == "NATIVE",
    "existing source-linked offline draft begins with Native mechanics");
Check(offlinePager.Change(16) && offlinePager.Current.Character == OfflineCharacterMode.TrooperWallClingPractice
    && offlinePager.Valid && offlinePager.CharacterName == "TROOPER WALL-CLING",
    "first mechanics cycle preserves explicit wall-cling practice");
var capturedWall = offlinePager.Current;
Check(offlinePager.Change(16) && offlinePager.Current.Character == OfflineCharacterMode.TrooperScoutDronePractice
    && offlinePager.Valid && offlinePager.Current.Hunter == Hunter.Weavel && offlinePager.Current.Mode == GameMode.Battle
    && offlinePager.Current.Bots == 0 && offlinePager.CharacterName == "TROOPER SCOUT DRONE",
    "second mechanics cycle selects explicit valid zero-bot Scout Drone practice");
var capturedDrone = offlinePager.Current;
Check(offlinePager.CharacterNotice.Contains("Native fallback models", StringComparison.Ordinal)
    && offlinePager.CharacterNotice.Contains("Zoom for a laser", StringComparison.Ordinal)
    && offlinePager.CharacterNotice.Contains("No blaster or replays", StringComparison.Ordinal),
    "drone notice keeps current native fallback and missing-feature scope explicit");
Check(offlinePager.Change(16) && offlinePager.Current.Character == OfflineCharacterMode.TrooperScoutDroneCombatPractice
    && (int)offlinePager.Current.Character == 3 && offlinePager.Current.Bots == 1 && offlinePager.Valid
    && offlinePager.CharacterName == "DRONE COMBAT PRACTICE",
    "third cycle explicitly captures combat practice with one native opponent");
var capturedCombat = offlinePager.Current;
Check(offlinePager.Change(16) && offlinePager.Current.Character == OfflineCharacterMode.TrooperArsenalPractice
    && (int)offlinePager.Current.Character == 4 && offlinePager.Valid && offlinePager.Current.Bots == 1
    && offlinePager.CharacterName == "TROOPER ARSENAL"
    && offlinePager.CharacterNotice.Contains("Unsupported states use native fallback.", StringComparison.Ordinal)
    && offlinePager.CharacterNotice.Contains("60 shared UA each life", StringComparison.Ordinal),
    "fourth cycle captures explicit Arsenal with its shared-UA/fallback notice");
var capturedArsenal = offlinePager.Current;
Check(offlinePager.Change(16) && offlinePager.Current.Character == OfflineCharacterMode.Native
    && offlinePager.CharacterNotice == "" && capturedWall.Character == OfflineCharacterMode.TrooperWallClingPractice
    && capturedDrone.Character == OfflineCharacterMode.TrooperScoutDronePractice
    && capturedCombat.Character == OfflineCharacterMode.TrooperScoutDroneCombatPractice && capturedCombat.Bots == 1
    && capturedArsenal.Character == OfflineCharacterMode.TrooperArsenalPractice && capturedArsenal.Bots == 1,
    "fifth cycle restores Native while all four immutable private captures retain their identities");
Check(new RmlUiOfflineDraft(new[] { "Arena 00" }, Hunter.Weavel, 0, 0, 0).Current.Character == OfflineCharacterMode.Native
    && !(capturedDrone with { Character = (OfflineCharacterMode)77 }).ValidateForm(new[] { capturedDrone.RoomKey }),
    "fresh drafts never inherit mechanics and unknown values remain refused");

services.OfflineValidationPending =
    new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
    && controller.OfflineOpen && Flag(controller.Tick(), "offline_mode")
    && !Flag(controller.Tick(), "lobby_mode"),
    "native Offline Battle opens without requesting the legacy workspace");
var botSetupBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Offline, controller.Tick().Bindings);
Check(botSetupBindings.TryGetValue("visible:offline_match_panel", out var visibleBotSetup)
    && visibleBotSetup.Boolean
    && botSetupBindings.TryGetValue("visible:offline_training_panel", out var hiddenAimSetup)
    && !hiddenAimSetup.Boolean
    && botSetupBindings.TryGetValue("offline_heading", out var botSetupHeading)
    && botSetupHeading.Text == "OFFLINE BATTLE"
    && !Flag(controller.Tick(), "offline_training_can_start"),
    "Offline Battle shows only bot-match settings and the correct heading");
Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice, 8)
    && !controller.Dispatch(RmlUiIntentKind.OfflineOpenTrainingOptions)
    && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchTraining),
    "Offline Battle cannot edit or launch Aim Lab through a stale intent");
string[] offlinePreviewRequest = Array.Empty<string>(); int offlinePreviewCalls = 0;
services.MapPreviews = rooms =>
{
    offlinePreviewCalls++;
    offlinePreviewRequest = rooms.ToArray();
    return rooms.Select(room => room.Length == 0 ? default
        : new RmlUiArenaPreview("/synthetic/offline/" + room + ".tga", "OFFLINE MAP PREVIEW")).ToArray();
};
Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 5)
    && Text(controller.Tick(), "offline_hunter_value") == "KANDEN",
    "Hunter choice updates native offline draft, not launcher preferences");
Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenArena)
    && Flag(controller.Tick(), "offline_arena_open"),
    "offline arena launches an owned native modal");
int previewCallsBeforeProjection = offlinePreviewCalls;
Check(controller.Dispatch(RmlUiIntentKind.OfflineArenaSearch, arenaSearch: "sanctorus")
    && Text(controller.Tick(), "offline_arena_name0") == "Data Shrine",
    "authored arena modal receives case-insensitive installed map search results");
var offlineArenaSnapshot = controller.Tick();
var offlineBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.OfflineArena, offlineArenaSnapshot.Bindings);
Check(offlineBindings.TryGetValue("offline_arena_name_0", out var firstArena)
    && firstArena.Text == "Data Shrine"
    && offlineBindings.TryGetValue("disabled:offline_arena_pick_0", out var firstArenaDisabled)
    && !firstArenaDisabled.Boolean
    && offlineBindings.TryGetValue("image:offline_arena_image_0", out var firstArenaImage)
    && firstArenaImage.Text == "/synthetic/offline/MP1 SANCTORUS.tga"
    && offlineBindings.TryGetValue("visible:offline_arena_image_0", out var firstArenaImageVisible)
    && firstArenaImageVisible.Boolean
    && offlineBindings.TryGetValue("visible:offline_arena_preview_status_0", out var firstArenaStatusVisible)
    && !firstArenaStatusVisible.Boolean
    && offlinePreviewCalls == previewCallsBeforeProjection + 1
    && offlinePreviewRequest.Length == RmlUiOfflineDraft.ArenaPageSize
    && offlinePreviewRequest[0] == "MP1 SANCTORUS",
    "native six-card arena bindings show names and exactly one page of offline previews");
Check(controller.Dispatch(RmlUiIntentKind.OfflineSelectArena, 0)
    && !Flag(controller.Tick(), "offline_arena_open")
    && Text(controller.Tick(), "offline_arena_value") == "Data Shrine",
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
var aimSetupBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Offline, controller.Tick().Bindings);
Check(aimSetupBindings.TryGetValue("visible:offline_match_panel", out var hiddenBotSetup)
    && !hiddenBotSetup.Boolean
    && aimSetupBindings.TryGetValue("visible:offline_training_panel", out var visibleAimSetup)
    && visibleAimSetup.Boolean
    && aimSetupBindings.TryGetValue("offline_heading", out var aimSetupHeading)
    && aimSetupHeading.Text == "AIM LAB"
    && !Flag(controller.Tick(), "offline_can_start"),
    "Aim Lab shows only drill settings and the correct heading");
Check(!controller.Dispatch(RmlUiIntentKind.OfflineChoice, 3)
    && !controller.Dispatch(RmlUiIntentKind.OfflineChoice, 16)
    && !controller.Dispatch(RmlUiIntentKind.OfflineOpenArena)
    && !controller.Dispatch(RmlUiIntentKind.OfflineOpenRules)
    && !controller.Dispatch(RmlUiIntentKind.OfflineLaunchMatch),
    "Aim Lab cannot edit or launch bot matches through a stale intent");
Check(controller.Dispatch(RmlUiIntentKind.OfflineChoice, 8)
    && Text(controller.Tick(), "offline_drill_value") == "Flick",
    "the native drill control changes current training draft only");
Check(controller.Dispatch(RmlUiIntentKind.OfflineOpenTrainingOptions)
    && controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Offline)
    && !controller.TrainingOptionsOpen && !Flag(controller.Tick(), "offline_training_mode")
    && controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Training)
    && !controller.TrainingOptionsOpen && Flag(controller.Tick(), "offline_training_mode"),
    "switching local activities retires an old Aim Lab options modal");
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

// RML-27: only the existing AdventureSave / AdventureLaunch is allowed to
// interpret save state. No native controller edits/loads save JSON.
if (args.Contains("--offline-only"))
{
    MapPickerCatalogAcceptance.Run();
    OfflineReturnChecks.Run();
    Console.WriteLine("RML OFFLINE AND MAP MENU CHECKS PASS");
    return;
}

services.AdventureSlots = new[]
{
    new RmlUiAdventureSlot(1, false, false, "", 0, 0, 0, "EMPTY"),
    new RmlUiAdventureSlot(2, true, false, "Celestial Archives", 3, 79, 99, "SHA-ORIGINAL"),
    new RmlUiAdventureSlot(3, false, true, "", 0, 0, 0, "UNAVAILABLE")
};
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure)
    && controller.AdventureOpen
    && Flag(controller.Tick(), "adventure_mode")
    && !Flag(controller.Tick(), "offline_mode"),
    "authoritative Adventure saves open native page without legacy fallback");
Check(Text(controller.Tick(), "adventure_slot2_label").Contains("OCTOLITHS 3/8")
    && Text(controller.Tick(), "adventure_slot3_label").Contains("UNREADABLE"),
    "native save labels show only verified slot data and distinguish corrupt saves");
var adventureBindings = RmlUiMenuPageBindings.Project(
    RmlUiMenuPage.Adventure, controller.Tick().Bindings);
Check(adventureBindings.TryGetValue("adventure_slot2_label", out var slot2Text)
    && slot2Text.Text.Contains("CELESTIAL ARCHIVES", StringComparison.Ordinal)
    && adventureBindings.TryGetValue("disabled:adventure_continue", out var continueDisabled)
    && continueDisabled.Boolean,
    "authored Adventure RML slots and action permissions project from portable snapshot");
Check(controller.Dispatch(RmlUiIntentKind.AdventureSelectSlot, 3)
    && !controller.Dispatch(RmlUiIntentKind.AdventureNewRun)
    && !controller.Dispatch(RmlUiIntentKind.AdventureContinue)
    && services.AdventureLaunches == 0,
    "unreadable save is protected from resume and unconfirmed overwrite");
Check(controller.Dispatch(RmlUiIntentKind.AdventureSelectSlot, 2)
    && controller.Dispatch(RmlUiIntentKind.AdventureNewRun)
    && Flag(controller.Tick(), "adventure_confirmation_open")
    && services.AdventureLaunches == 0,
    "occupied Adventure slot requires an explicit destructive confirmation");
var overwriteBindings = RmlUiMenuPageBindings.Project(
    RmlUiMenuPage.AdventureConfirm, controller.Tick().Bindings);
Check(overwriteBindings.TryGetValue("adventure_overwrite_title", out var overwriteTitle)
    && overwriteTitle.Text.Contains("02"),
    "native confirmation displays the exact slot about to be replaced");
Check(controller.Dispatch(RmlUiIntentKind.AdventureCancelNewRun)
    && !Flag(controller.Tick(), "adventure_confirmation_open")
    && services.AdventureLaunches == 0,
    "Cancel preserves the existing campaign save without touching persistence");
Check(controller.Dispatch(RmlUiIntentKind.AdventureNewRun),
    "re-opening confirmation captures immutable save identity");
services.AdventureSlots[1] = services.AdventureSlots[1] with { Fingerprint = "SHA-CHANGED" };
Check(!controller.Dispatch(RmlUiIntentKind.AdventureConfirmNewRun)
    && services.AdventureLaunches == 0
    && !Flag(controller.Tick(), "adventure_confirmation_open"),
    "same slot whose file bytes changed cannot be overwritten by stale confirmation");
Check(controller.Dispatch(RmlUiIntentKind.AdventureRefresh)
    && controller.Dispatch(RmlUiIntentKind.AdventureNewRun)
    && controller.Dispatch(RmlUiIntentKind.AdventureConfirmNewRun)
    && services.AdventureLaunches == 1
    && services.LastAdventure.NewGame
    && services.LastAdventure.Expected.Fingerprint == "SHA-CHANGED",
    "refreshed confirmation submits exactly one current-save new run");
Check(!controller.Dispatch(RmlUiIntentKind.AdventureConfirmNewRun)
    && services.AdventureLaunches == 1,
    "confirmed Adventure cannot be launched twice from retired modal");
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home),
    "return Home discards local Adventure page state");
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure)
    && controller.Dispatch(RmlUiIntentKind.AdventureSelectSlot, 2)
    && controller.Dispatch(RmlUiIntentKind.AdventureContinue)
    && services.AdventureLaunches == 2
    && !services.LastAdventure.NewGame,
    "resume calls one existing Adventure launch without an overwrite confirmation");
controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Adventure)
    && controller.Dispatch(RmlUiIntentKind.AdventureNewRun)
    && services.AdventureLaunches == 3
    && services.LastAdventure.Expected.Slot == 1
    && services.LastAdventure.NewGame,
    "empty Adventure slot launches a new run with no misleading overwrite prompt");
controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home);

// RML-28: the replay archive is a presentation of indexed library metadata,
// never an independent replay decoder, renderer, or user-supplied filesystem path.
// BEGIN THEATRE VISIBLE SELECTION SOURCE UNIT CHECKS
static void CheckTheatreVisibleSelection()
{
    long recorded = DateTime.Now.Ticks;
    var alpha = new RmlUiReplayEntry("/_demos/alpha.ppdemo", "Match Alpha", "Arena A", "A",
        recorded, 100, 1000, true, false, false, true);
    var beta = new RmlUiReplayEntry("/_demos/beta.ppdemo", "Match Beta", "Arena B", "B",
        recorded - 1, 200, 2000, false, true, false, true);
    var index = new RmlUiTheatreIndex();
    index.Refresh(new[] { beta, alpha });
    Check(index.Select(0) && index.Selected == alpha && index.SelectedUnchanged(alpha),
        "Theatre source unit: an indexed replay can be explicitly selected before filtering");
    Check(index.SearchFor("Beta") && index.Count == 1 && index.Selected == beta
        && !index.SelectedUnchanged(alpha) && index.SelectedUnchanged(beta),
        "Theatre source unit: search hiding selected Alpha chooses shown Beta and rejects Alpha's worker stamp");
    var automatic = new RmlUiTheatreIndex();
    automatic.Refresh(new[] { beta, alpha });
    Check(automatic.Selected == alpha && automatic.SelectedUnchanged(alpha),
        "Theatre source unit: initial filtered snapshot selects its first shown replay");
    Check(index.SearchFor("") && index.Selected == beta,
        "Theatre source unit: clearing a search preserves a selection still present in the filtered results");
    Check(index.SearchFor("no matching recording") && index.Count == 0 && index.Selected == null
        && !index.SelectedUnchanged(alpha) && !index.SelectedUnchanged(beta),
        "Theatre source unit: zero matches clears selection and every captured selection stamp");
    Check(index.SearchFor("") && index.Count == 2 && index.Selected == alpha,
        "Theatre source unit: clearing an empty search selects a shown recording again");
    Check(index.Select(1) && index.Selected == beta && index.NextFilter()
        && index.FilterLabel == "FULL MATCHES" && index.Selected == alpha
        && !index.SelectedUnchanged(beta),
        "Theatre source unit: a filter hiding selected Beta chooses the shown full replay");
    Check(index.NextFilter() && index.FilterLabel == "CLIPS" && index.Selected == beta
        && index.NextFilter() && index.FilterLabel == "FAVORITES" && index.Selected == alpha,
        "Theatre source unit: clip and favorite filters cannot retain a hidden selection");
    Check(index.NextFilter() && index.NextFilter() && index.FilterLabel == "RECOVERABLE"
        && index.Count == 0 && index.Selected == null,
        "Theatre source unit: an empty recoverable view clears actionable selection");
    for (int filter = 0; filter < 9; filter++) index.NextFilter();
    Check(index.FilterLabel == "ALL REPLAYS" && index.Selected == alpha,
        "Theatre source unit: an empty filter clears selection and returning to all chooses a shown row");
    Check(index.Select(1) && index.NextSort() && index.Selected == beta && index.SelectedUnchanged(beta),
        "Theatre source unit: sorting retains a valid selected path rather than retargeting its row number");
    var changedBeta = beta with { Bytes = beta.Bytes + 1, LastWriteTicks = beta.LastWriteTicks + 1 };
    index.Refresh(new[] { alpha, changedBeta });
    Check(index.Selected == changedBeta && !index.SelectedUnchanged(beta)
        && index.SelectedUnchanged(changedBeta),
        "Theatre source unit: refresh preserves the selected path but rejects old file size and timestamp witnesses");
    index.Refresh(new[] { alpha });
    Check(index.Selected == alpha && !index.SelectedUnchanged(changedBeta),
        "Theatre source unit: removing a selected replay chooses a shown replacement");
    index.Refresh(Array.Empty<RmlUiReplayEntry>());
    Check(index.Selected == null && index.Count == 0 && !index.SelectedUnchanged(alpha),
        "Theatre source unit: empty refresh retires the old selected path");
    var virtualAlpha = alpha with { VirtualWitness = new("descriptor-a", alpha.Path, alpha.Bytes, alpha.LastWriteTicks) };
    index.Refresh(new[] { virtualAlpha });
    var replacedVirtual = virtualAlpha with { VirtualWitness = new("descriptor-b", alpha.Path, alpha.Bytes, alpha.LastWriteTicks) };
    index.Refresh(new[] { replacedVirtual });
    Check(index.Selected == replacedVirtual && !index.SelectedUnchanged(virtualAlpha),
        "Theatre source unit: selection normalization retains virtual descriptor witness rejection");

    var paged = new RmlUiTheatreIndex();
    var rows = Enumerable.Range(0, 18).Select(i => alpha with
    { Path = "/_demos/page_" + i + ".ppdemo", Title = "Page " + i, RecordedTicks = recorded + i }).ToArray();
    paged.Refresh(rows);
    var first = paged.Selected;
    Check(paged.Turn(1) && paged.Selected == first && paged.Select(3),
        "Theatre source unit: turning pages retains a valid filtered selection until an explicit row selection");
    var removed = paged.Selected!.Value;
    Check(paged.Turn(1) && paged.Selected == removed,
        "Theatre source unit: pagination does not reinterpret a selected replay as another page's row slot");
    paged.Refresh(rows.Where(row => row.Path != removed.Path));
    Check(paged.Page == 2 && paged.Selected == paged.Entry(0) && !paged.SelectedUnchanged(removed),
        "Theatre source unit: removing an off-page selection chooses a shown row on the current page");
    var retained = paged.Selected;
    paged.Refresh(rows.Take(5));
    Check(paged.Page == 0 && paged.Selected == retained,
        "Theatre source unit: refresh clamps pagination while preserving a still-filtered selected path");
    var beforeInvalid = paged.Selected;
    Check(!paged.SearchFor(new string('X', 513)) && paged.Selected == beforeInvalid,
        "Theatre source unit: rejected search cannot change selection");
    paged.MarkLaunched();
    Check(paged.Launched && !paged.Select(0) && !paged.SelectedUnchanged(beforeInvalid!.Value),
        "Theatre source unit: filtered-selection repair does not reopen a confirmed launch or worker stamp");
}
CheckTheatreVisibleSelection();
CustomReplayAudioAcceptance.Run();
StudioAudioSettingsAcceptance.Run();
TheatreSmartViewAcceptance.Run();
TheatreMetadataAcceptance.Run();
// END THEATRE VISIBLE SELECTION SOURCE UNIT CHECKS
RmlUiReplayEntry[] replayRows = Enumerable.Range(0, 18).Select(i =>
    new RmlUiReplayEntry($"/_demos/replay_{i:00}.ppdemo", $"Replay {i:00}",
        $"Arena {i % 3}", $"Map {i % 3}", i + 1, 100 + i,
        1000 + i, i % 5 == 0, i % 2 == 0, false, true)).ToArray();
var archive = new RmlUiTheatreIndex();
archive.Refresh(replayRows);
Check(archive.TotalCount == 18 && archive.Count == 18 && archive.Pages == 3
    && archive.Entry(0)?.Title == "Replay 17" && archive.Entry(7)?.Title == "Replay 10"
    && archive.Turn(1) && archive.Entry(0)?.Title == "Replay 09"
    && archive.Turn(1) && archive.Entry(0)?.Title == "Replay 01"
    && !archive.Turn(1),
    "native replay archive sorts newest-first and pages eight bounded rows");
Check(archive.SearchFor("rEpLaY 06") && archive.Count == 1
    && archive.Entry(0)?.Title == "Replay 06"
    && archive.Page == 0 && !archive.Turn(-1)
    && !archive.SearchFor(new string('X',513)),
    "case-insensitive replay search resets pagination and rejects oversized query");
Check(archive.Select(0) && archive.Selected?.Title == "Replay 06"
    && archive.SelectedUnchanged(replayRows[6])
    && !archive.SelectedUnchanged(replayRows[6] with { Bytes = 99 }),
    "replay actions bind the exact indexed file size and write timestamp");
archive.MarkLaunched();
Check(!archive.Select(0) && !archive.SelectedUnchanged(replayRows[6]),
    "native playback handoff cannot be re-entered after a confirmed launch");

services.TheatreRows = replayRows.Take(3)
    .Select(row => row with { Room = "MP1 SANCTORUS" }).ToArray();
services.TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre)
    && controller.TheatreOpen && controller.Busy
    && Flag(controller.Tick(), "theatre_mode")
    && services.TheatreReadCalls == 1,
    "native Theatre opens its own page and queues replay metadata work off the owner thread");
services.TheatreReadPending.SetResult(services.TheatreRows);
Check(Text(Wait(controller, s => Flag(s,"theatre_entry0_present")), "theatre_entry0_title")
    == "Replay 02", "native replay list displays the indexed library result");
var theatreBindings = RmlUiMenuPageBindings.Project(RmlUiMenuPage.Theatre, controller.Tick().Bindings);
Check(theatreBindings.TryGetValue("theatre_entry0_title", out var theatreTitle)
    && theatreTitle.Text == "Replay 02"
    && theatreBindings.TryGetValue("disabled:theatre_watch", out var theatreWatchDisabled)
    && !theatreWatchDisabled.Boolean,
    "native Theatre bindings show metadata and enable Watch for the first shown replay");
Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 0, theatreSearch: "replay 01")
    && Text(controller.Tick(), "theatre_entry0_title") == "Replay 01"
    && controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0)
    && Text(controller.Tick(), "theatre_selected_title") == "Replay 01",
    "native replay search and selection use immutable eight-row result slots");
var theatreMapView = controller.Tick();
Check(Text(theatreMapView, "theatre_metadata").EndsWith("MAP // Data Shrine", StringComparison.Ordinal)
    && Text(theatreMapView, "theatre_hero") == "FULL REPLAY // Data Shrine"
    && Text(theatreMapView, "theatre_entry0_detail").Contains("Data Shrine", StringComparison.Ordinal),
    "native Theatre detail, hero and cards show map titles while replay rows retain runtime room keys");
services.TheatreValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 6) && controller.Busy
    && services.TheatreValidations == 1 && services.ReplayLaunches == 0
    && !controller.Dispatch(RmlUiIntentKind.TheatreAction, 6),
    "Watch performs one async protocol/map preflight, without optimistic scene startup");
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
    && !controller.TheatreOpen,
    "leaving Theatre cancels pending native replay playback");
services.TheatreValidationPending.SetResult("");
Thread.Sleep(30);
Check(services.ReplayLaunches == 0,
    "stale replay validation completion cannot launch after document retirement");

services.TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre),
    "reopening Theatre starts a new generation of library data");
services.TheatreReadPending.SetResult(services.TheatreRows);
Wait(controller,s=>Flag(s,"theatre_entry0_present"));
Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0), "new foreground replay can be selected");
services.TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 7)
    && services.TheatreFavorites == 1 && controller.Busy,
    "toggle Favorite delegates to an exact on-disk entry then rereads authoritative library");
services.TheatreReadPending.SetResult(services.TheatreRows);
Wait(controller,s=>!controller.Busy);
Check(services.TheatreRows[2].Favorite && services.ReplayLaunches == 0,
    "favorite mutation changes only its existing replay marker, not playback");
services.TheatreValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 6),
    "selected replay can begin a second checked playback attempt");
services.TheatreValidationPending.SetResult("");
Wait(controller, _ => services.ReplayLaunches == 1);
Check(services.ReplayLaunches == 1
    && services.LastReplay.Path == replayRows[2].Path && !services.LastReplayOpenEditor
    && !controller.Dispatch(RmlUiIntentKind.TheatreAction, 6),
    "validated Theatre Watch hands off once to the existing playback launcher");
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Home)
    && !Flag(controller.Tick(), "theatre_mode"),
    "Theatre route exits without retaining playable selection");

services.TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.Navigate, (int)RmlUiRouteArgument.Theatre),
    "native editor launch fixture opens replay archive");
services.TheatreReadPending.SetResult(services.TheatreRows);
Wait(controller,s=>Flag(s,"theatre_entry0_present"));
controller.Dispatch(RmlUiIntentKind.TheatreAction, 0, theatreSearch: "NO MATCHING FIXTURE");
Check(!controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && services.LegacyTheatreHandoffs == 0,
    "native editor requires an exact selected replay and never falls back to legacy Studio");
controller.Dispatch(RmlUiIntentKind.TheatreAction, 23);
Check(controller.Dispatch(RmlUiIntentKind.TheatreEntry, 0), "editor launch captures a foreground library selection");
services.TheatreValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(controller.Dispatch(RmlUiIntentKind.TheatreAction, 18) && controller.Busy && services.ReplayLaunches == 1,
    "native editor waits for the same replay protocol/map preflight as Watch");
services.TheatreValidationPending.SetResult("");
Wait(controller, _ => services.ReplayLaunches == 2);
Check(services.LastReplayOpenEditor && services.LastReplay == services.LastValidatedReplay
    && services.LegacyTheatreHandoffs == 0,
    "verified native editor launch forwards only captured selected replay with explicit editor presentation flag");

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
Check(!controller.Dispatch(RmlUiIntentKind.PlayCreate, playerName: "Pilot", lobbyName: "Duplicate"),
    "pending host allocation cannot double-start");
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
// RML-29: the original eight-pad lobby and single Home Hunter stage are
// projections of verified live roster data, not hypothetical UI silhouettes.
var stageHub = new HubSnapshot("Pilot", Hunter.Samus, Hunter.Kanden, 2, true, "Desktop");
var stageEmptyLobby = Stub.EmptyLobby;
var stageHome = RmlUiHunterStagePlan.Build(stageHub, stageEmptyLobby, null);
Check(!stageHome.Lobby && stageHome.Count == 0 && stageHome.HomeHunter == Hunter.Kanden
    && stageHome.HomeSuit == 2 && stageHome.GameFilesReady,
    "native Home stages the actual selected Hunter and suit");
Check(LauncherLobbyFormation.Validate(),
    "archived eight-player platform centers and preview rectangles remain valid");
var localStage = LauncherLobbyFormation.At(0);
var packedLobbyPads = LauncherLobbyFormation.PackPads();
for (int slotIndex = 1; slotIndex < LauncherLobbyFormation.Capacity; slotIndex++)
{
    var supportingStage = LauncherLobbyFormation.At(slotIndex);
    Check(supportingStage.Valid && supportingStage.RadiusX < localStage.RadiusX * .5f
        && supportingStage.HunterBottom - supportingStage.HunterTop < (localStage.HunterBottom - localStage.HunterTop) * .42f,
        "secondary lobby Hunter and platform remain subordinate to readable local hero: " + slotIndex);
    Check(Math.Abs(packedLobbyPads[slotIndex * 4 + 2] - supportingStage.RadiusX) < .00001f
        && Math.Abs(packedLobbyPads[slotIndex * 4 + 3] - supportingStage.RadiusY) < .00001f,
        "shader platform consumes the same compact secondary geometry as the model: " + slotIndex);
    Check(Math.Abs((supportingStage.HunterLeft + supportingStage.HunterRight) * .5f - supportingStage.LabelX) < .00001f,
        "secondary Hunter remains centered beneath its distinct nameplate anchor: " + slotIndex);
}
NativeSettingsAcceptance.Run();
ReplayProjectFilesAcceptance.Run();
QueuedReplayAudioAcceptance.Run();
HomePartyPadAcceptance.Run();
StudioPresentationPreferencesAcceptance.Run();
HomeRailAcceptance.Run();
MapPickerCatalogAcceptance.Run();
CommunityHostingAcceptance.Run();
GameFilesSetupResumeChecks.Run();
TrainingReturnChecks.Run();
OfflineReturnChecks.Run();
MapSelectionPreferenceChecks.Run();
HostRequestIdentityAcceptance.Run();
LobbyStartGateAcceptance.Run();
var stageLobby = new RmlUiLobbyView(true, false, true, false, false,
    true, true, "Stage lobby", "MP1 SANCTORUS", "Battle", "FreeForAll",
    "Ready", 8, Array.Empty<RmlUiLobbyPlayer>());
var stageRoster = new RmlUiAdminSnapshot(9, 888, 3, true, 0, 3, false,
    false, 2, 8, false, "READY", "", new[] { Hunter.Samus, Hunter.Trace },
    new[]
    {
        new RmlUiAdminPlayer(4, 10, "Remote", Hunter.Trace, 1, 1,
            false, false, 0, 0, 85),
        new RmlUiAdminPlayer(3, 12, "Pilot", Hunter.Kanden, 2, 0,
            true, false, 0, 0, 20),
        new RmlUiAdminPlayer(0, 15, "Guest", Hunter.Samus, 0, 1,
            true, false, 0, 0, 34)
    }, Array.Empty<string>());
var stage = RmlUiHunterStagePlan.Build(stageHub, stageLobby, stageRoster);
Check(stage.Lobby && stage.Count == 3 && stage.Formation[0].Local
    && stage.Formation[0].Name == "Pilot" && stage.Formation[0].Hunter == Hunter.Kanden
    && stage.Formation[0].Suit == 2 && stage.Formation[0].Ready
    && stage.Formation[1].NetSlot == 0 && stage.Formation[2].NetSlot == 4,
    "native lobby local player uses front platform and remaining models stable network-slot order");
Check(stage.Formation.Select(x => x.Identity).Distinct().Count() == 3
    && stage.Formation[1].Identity != stage.Formation[0].Identity,
    "each preview pad carries exact server slot + occupant generation identity");
var staleStage = RmlUiHunterStagePlan.Build(stageHub, stageLobby, null);
Check(staleStage.Lobby && staleStage.Count == 0,
    "absent authoritative roster cannot invent remote character previews");
var badStage = stageRoster with
{
    Players = stageRoster.Players.Concat(new[]
    {
        stageRoster.Players[0] with { Hunter = Hunter.Sylux },
        stageRoster.Players[0] with { Slot = 7, Generation = 0 },
        stageRoster.Players[0] with { Slot = 6, Suit = 8 }
    }).ToArray()
};
var rejectedStage = RmlUiHunterStagePlan.Build(stageHub, stageLobby, badStage);
Check(rejectedStage.Count == 2 && rejectedStage.Formation.All(player => player.NetSlot != 4)
    && rejectedStage.Formation.Select(player => player.NetSlot).SequenceEqual(
        RmlUiLobbyDisplayOrder.Ordered(badStage).Select(player => player.Slot)),
    "all ambiguous duplicate-slot occupants are rejected and stage order matches native labels");
Check(stage.MatchId == stageRoster.MatchId && stage.AuthorityEpoch == stageRoster.AuthorityEpoch,
    "stage forwards authoritative match and epoch to scope reused preview identities");
Check(stage.Formation[0].Team == 0 && stage.Formation[1].Team == 1 && stage.Formation[2].Team == 1,
    "valid authoritative teams reach platform accents independently of suit indices");
var invalidTeamStage = RmlUiHunterStagePlan.Build(stageHub, stageLobby, stageRoster with
{
    Players = stageRoster.Players.Select(player => player with { Team = 2 }).ToArray()
});
Check(invalidTeamStage.Count == 3 && invalidTeamStage.Formation.All(player => player.Team == -1),
    "team equal to authoritative team count cannot fabricate platform membership");
foreach (byte invalidTeamCount in new byte[] { 0, 5 })
{
    var unassignedTeamStage = RmlUiHunterStagePlan.Build(stageHub, stageLobby,
        stageRoster with { TeamCount = invalidTeamCount });
    Check(unassignedTeamStage.Count == 3 && unassignedTeamStage.Formation.All(player => player.Team == -1),
        "absent or invalid authoritative team count leaves accents unassigned: " + invalidTeamCount);
}
Check(RmlUiHunterStagePlan.Build(stageHub with { GameFilesReady = false },
    stageLobby, stageRoster) is { Lobby: true, GameFilesReady: false, Count: 0 },
    "when game assets are missing, the lobby keeps its stage but never touches model GL");
HunterStagePlanCacheAcceptance.Run(stageHub, stageLobby, stageRoster);
// RML-30: chat, match conditions and slot actions displayed on the central
// Lobby must use the same authoritative local-front order as the 3D stage.
services.LobbyView = stageLobby with
{
    Players = new[]
    {
        new RmlUiLobbyPlayer("Pilot", "KANDEN", true, true, false),
        new RmlUiLobbyPlayer("Guest", "SAMUS", true, false, false),
        new RmlUiLobbyPlayer("Remote", "TRACE", false, false, false)
    }
};
services.AdminState = stageRoster;
services.Chrome = new RmlUiLobbyChrome("7:00", "7 POINT GOAL",
    "Pilot: Hello world", "CHAT AVAILABLE", "MAP READY",
    "MAP: SANCTORUS // PREVIEW PENDING", "", "");
var lobbyChrome = Wait(controller, snap =>
    Text(snap, "lobby_time") == "7:00");
Check(Text(lobbyChrome, "lobby_score") == "7 POINT GOAL"
    && Text(lobbyChrome, "lobby_chat_history") == "Pilot: Hello world"
    && Text(lobbyChrome, "lobby_field_caption") == "MAP READY"
    && Text(lobbyChrome, "lobby_map_preview_status").Contains("Data Shrine"),
    "native Lobby maps real authoritative match clock, score, map and chat history");
Check(Flag(lobbyChrome, "slot0_can_team")
    && Text(lobbyChrome, "slot0_team") == "TEAM A"
    && Flag(lobbyChrome, "slot0_team0")
    && !Flag(lobbyChrome, "slot7_can_select"),
    "team controls use actual verified occupant and disable empty slots");
Check(controller.Dispatch(RmlUiIntentKind.LobbyPlayerSelect, 0)
    && controller.AdminOpen
    && Text(controller.Tick(), "admin_selected_name") == "Pilot",
    "clicking a local-first podium/nameplate maps display index to actual network slot 3");
Check(controller.Dispatch(RmlUiIntentKind.LobbyAdminClose),
    "native Lobby player modal closes before direct-team action");
Check(controller.Dispatch(RmlUiIntentKind.LobbySlotTeamNext, 0)
    && services.LastAdmin.Operation == RmlUiAdminOperation.SetTeam
    && services.LastAdmin.TargetSlot == 3
    && services.LastAdmin.TargetGeneration == 12
    && services.LastAdmin.Team == 1,
    "local nameplate team action submits exact authoritative slot+generation, not display index");
Check(controller.Dispatch(RmlUiIntentKind.LobbyChatSend,
        adminChat: "  Ready to play  ")
    && services.ChatCalls == 2 && services.LastChat == "Ready to play",
    "central Lobby chat sends real user text without requiring Admin modal");
Check(!controller.Dispatch(RmlUiIntentKind.LobbyChatSend,
        adminChat: new string('X', ChatPacket.MaxTextBytes + 1))
    && services.ChatCalls == 2,
    "chat refuses messages over protocol UTF-8 wire bound");
services.AdminState = stageRoster with
{
    Owner = false,
    TeamsLocked = true
};
Check(!controller.Dispatch(RmlUiIntentKind.LobbySlotTeamNext, 1),
    "non-owner cannot modify a locked remote Hunter team");
Check(!controller.Dispatch(RmlUiIntentKind.LobbySlotTeamNext, 7),
    "blank nameplate never submits a team change to a synthetic player");
services.LobbyView = Stub.EmptyLobby;
services.AdminState = null;
HomePartyAcceptance.Run();
LobbyBotAcceptance.Run();
OfflineRulesAcceptance.Run();
OfflineRulesAsyncAcceptance.Run();
AdventureSaveAcceptance.Run();
TheatreWorkAcceptance.Run();
PartyConnectionAcceptance.Run();
SocialConnectionAcceptance.Run();
PartyQueueAcceptance.Run();
RandomMatchAcceptance.Run();
CreatedLobbyCompletionAcceptance.Run();
OfflineArenaInstallChecks.Run();
Console.WriteLine("RML03 owner-thread launcher service contracts passed.");

sealed class Stub : IRmlUiMenuServices
{
    public static readonly RmlUiLobbyView EmptyLobby = new(false, false, false, false,
        false, false, false, "", "", "", "", "", 8, Array.Empty<RmlUiLobbyPlayer>());
    public TaskCompletionSource<RmlUiDirectory> BrowsePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<RmlUiJoinResult> JoinPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<RmlUiJoinResult> PartyJoinPending = new();
    public NativePartyJoinTarget? PartyTarget;
    public CancellationToken PartyJoinToken;
    public int PartyJoins, PartyDiscards, PartyCancellations;
    public bool PartyMayAccept = true, AcceptJoinResult = true;
    public readonly System.Collections.Generic.List<string> PartyEvents = new();
    public NativePartyQueueSnapshot PartyQueue = NativePartyQueueSnapshot.Empty;
    public readonly System.Collections.Generic.List<(NativePartyQueueSnapshot Snapshot, NativePartyQueueCommand Command)> PartyQueueCommands = new();
    public TaskCompletionSource<RmlUiJoinResult> CreatePending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public LobbyCreationRequest CreatedRequest;
    public CancellationToken CreateToken;
    public bool CreatedMayAccept = true;
    public int CreatedNotes, CreatedDiscards, CreatedCancellations;
    public readonly System.Collections.Generic.List<string> CreateEvents = new();
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
    public RmlUiAdventureSlot[] AdventureSlots = Array.Empty<RmlUiAdventureSlot>();
    public bool AdventureSaveManagementAvailable = true;
    public bool AdventureSaveAsync;
    public Func<RmlUiAdventureSaveChange, CancellationToken, Task<RmlUiAdventureSaveResult>>? AsyncAdventureSave;
    public int AdventureSaveChangeAttempts;
    public readonly System.Collections.Generic.List<RmlUiAdventureSaveChange> AdventureSaveChanges = new();
    public int AdventureLaunches;
    public (RmlUiAdventureSlot Expected, bool NewGame, Hunter Hunter) LastAdventure;
    public int TrainingLaunches;
    public (AimTrainerDefinition Drill, Hunter Hunter, int Suit) LastTraining;
    public TaskCompletionSource<string> OfflineValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public RmlUiOfflineRequest LastOffline;
    public int OfflineValidations, OfflineLaunches;
    public RmlUiOfflineRules? OfflineRules = new("7", "7:00", "3:00", "medium", 0);
    public bool FailOfflineRulesSave;
    public bool OfflineRulesAsync;
    public RmlUiOfflineRulesCache? OfflineRulesCache;
    public Func<RmlUiOfflineRulesChange,CancellationToken,Task<RmlUiOfflineRulesSaveResult>>? AsyncOfflineRuleSave;
    public int OfflineRulesSaveAttempts, OfflineRulesSaves;
    public TaskCompletionSource<RmlUiReplayEntry[]> TheatreReadPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<string> TheatreValidationPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public RmlUiReplayEntry[] TheatreRows = Array.Empty<RmlUiReplayEntry>();
    public RmlUiReplayEntry LastReplay, LastValidatedReplay;
    public CancellationToken LastReplayValidationToken;
    public bool IgnoreReplayValidationCancellation, LastReplayOpenEditor;
    public bool ReplayEditorSupported = true;
    public bool ReplayEditingAllowed { get; set; } = true;
    public bool StudioReplayAccepted = true;
    public int StudioReplayCalls;
    public RmlUiReplayEntry LastStudioReplay;
    public bool OpenStudioReplay(RmlUiReplayEntry entry, out string error)
    {
        StudioReplayCalls++; LastStudioReplay = entry;
        bool current = TheatreRows.Any(row => row.Path == entry.Path && row.Bytes == entry.Bytes
            && row.LastWriteTicks == entry.LastWriteTicks && row.VirtualWitness == entry.VirtualWitness && row.Playable);
        error = StudioReplayAccepted && current ? "" : "PRIME STUDIO HANDOFF REFUSED";
        return StudioReplayAccepted && current;
    }
    public int ReplayLaunchAttempts;
    public TaskCompletionSource<RmlUiReplayWorkResult> TheatreWorkPending = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public RmlUiReplayWorkRequest LastTheatreWork;
    public CancellationToken LastTheatreWorkToken;
    public int TheatreWorkCalls, TheatreWorkThread, RevealCalls;
    public int TheatreReadCalls, TheatreValidations, TheatreFavorites, ReplayLaunches, LegacyTheatreHandoffs;
    public RmlUiLobbyChrome Chrome = RmlUiLobbyChrome.Empty;
    public int ChatCalls;
    public string LastChat = "";
    public int CreateCalls;
    public int LobbyCapacity = 8;
    public int LobbyPlayerCapacity(string roomKey) => LobbyCapacity;
    public RmlUiLobbyView LobbyView = EmptyLobby;
    public int Accepted, ReadyCalls, StartCalls, LeaveCalls, Submissions;
    public RmlUiRuleDraft? AuthorityRules;
    public RmlUiRuleDraft SubmittedRule;
    public RmlUiSocialHomeState SocialOverride =
        new(false, false, Array.Empty<string>(), 0);
    public RmlUiSocialHomeState SocialHome() => SocialOverride;
    public NativePartyView HomeParty = new(false, false, "DISCONNECTED", 0, null);
    public NativePartyView PartyHome() => HomeParty;
    public NewsDispatch? FeaturedNews() => new BundledNewsProvider().Read()[0];
    public bool GameFilesReady = true, ConfigureGameFilesAccepted;
    public int ConfigureGameFilesCalls, NavigationCalls;
    public bool ConfigureGameFiles() { ConfigureGameFilesCalls++; return ConfigureGameFilesAccepted; }
    public HubSnapshot Hub() => new("Pilot", Hunter.Samus, Hunter.Samus, 0, GameFilesReady, "Desktop");
    public string BuildVersion() => "0.1.99";
    public RmlUiLobbyView Lobby() => LobbyView;
    public Task<RmlUiDirectory> Browse(CancellationToken _) => BrowsePending.Task;
    public int RandomDiscards;
    public void DiscardRandomJoin(LaunchPlan plan) => RandomDiscards++;
    public Func<CancellationToken, Task<RmlUiServerCandidate?>>? RandomScan;
    public Func<CancellationToken, Task<RmlUiJoinResult>>? RandomJoin;
    public Task<RmlUiServerCandidate?> QuickMatch(CancellationToken token) =>
        RandomScan?.Invoke(token) ?? Task.FromResult<RmlUiServerCandidate?>(null);
    public Task<RmlUiJoinResult> Join(string _, string __, CancellationToken token)
        => RandomJoin?.Invoke(token) ?? JoinPending.Task;
    public Task<RmlUiJoinResult> JoinParty(NativePartyJoinTarget target, string playerName, CancellationToken token)
    {
        PartyJoins++; PartyTarget = target; PartyJoinToken = token;
        return PartyJoinPending.Task;
    }
    public bool CanAcceptPartyJoin(NativePartyJoinTarget target, LaunchPlan plan)
    { PartyEvents.Add("validate"); return ReferenceEquals(target, PartyTarget) && PartyMayAccept; }
    public void NotePartyJoined(NativePartyJoinTarget target) => PartyEvents.Add("joined");
    public void DiscardPartyJoin(NativePartyJoinTarget target, LaunchPlan plan)
    { PartyDiscards++; PartyEvents.Add("discard"); }
    public void CancelPartyJoin() => PartyCancellations++;
    public NativePartyQueueSnapshot CapturePartyQueue() => PartyQueue;
    public bool CommandPartyQueue(NativePartyQueueSnapshot expected, NativePartyQueueCommand command)
    {
        if (!ReferenceEquals(expected, PartyQueue) || !expected.Active
            || command == NativePartyQueueCommand.Accept && !expected.CanAccept
            || command == NativePartyQueueCommand.Decline && !expected.CanDecline) return false;
        PartyQueueCommands.Add((expected, command));
        PartyQueue = expected with { Revision = expected.Revision + 1,
            Phase = command == NativePartyQueueCommand.Accept ? NativePartyQueuePhase.Admitting : NativePartyQueuePhase.Cancelled,
            CanAccept = false, CanDecline = false };
        return true;
    }
    public Task<RmlUiJoinResult> CreateLobby(LobbyCreationRequest request, CancellationToken token)
    {
        CreatedRequest = request;
        CreateToken = token;
        CreateCalls++;
        return CreatePending.Task;
    }
    public bool CanAcceptCreatedLobby(LaunchPlan plan) { CreateEvents.Add("validate"); return CreatedMayAccept; }
    public void NoteCreatedLobby(LaunchPlan plan) { CreatedNotes++; CreateEvents.Add("note"); }
    public void DiscardCreatedLobby(LaunchPlan plan) { CreatedDiscards++; CreateEvents.Add("discard"); }
    public void CancelCreatedLobby() { CreatedCancellations++; }
    public Task<string> ValidateOfflineAsync(RmlUiOfflineRequest request, CancellationToken token)
    {
        OfflineValidations++;
        return OfflineValidationPending.Task.WaitAsync(token);
    }
    public RmlUiAdventureSlot[] ReadAdventureSlots() => AdventureSlots.ToArray();
    public bool CanManageAdventureSaves => AdventureSaveManagementAvailable;
    public bool AdventureSaveChangesAreAsync => AdventureSaveAsync;
    public Task<RmlUiAdventureSaveResult> ManageAdventureSaveAsync(RmlUiAdventureSaveChange change, CancellationToken token)
        => AsyncAdventureSave?.Invoke(change, token) ?? Task.FromResult(new RmlUiAdventureSaveResult(change, false, "FAKE ASYNC OWNER UNAVAILABLE"));
    public bool ManageAdventureSave(RmlUiAdventureSaveChange change, out string status)
    {
        AdventureSaveChangeAttempts++;
        status = "SIMULATED SAVE CHANGE REFUSED // REFRESH SAVES";
        if (!CanManageAdventureSaves || AdventureSlots.SingleOrDefault(slot => slot.Slot == change.Source.Slot) != change.Source
            || change.Destination is { } destination && AdventureSlots.SingleOrDefault(slot => slot.Slot == destination.Slot) != destination) return false;
        AdventureSaveChanges.Add(change);
        if (change.Action == RmlUiAdventureSaveAction.Copy && change.Destination is { } target)
            AdventureSlots[target.Slot - 1] = change.Source with { Slot = target.Slot };
        else AdventureSlots[change.Source.Slot - 1] = new(change.Source.Slot, false, false, "", 0, 0, 0, "EMPTY");
        status = "SIMULATED EXACT SAVE CHANGE VERIFIED";
        return true;
    }
    public bool LaunchAdventure(RmlUiAdventureSlot expected, bool newGame, Hunter hunter)
    {
        if (expected.Slot is < 1 or > 3) return false;
        RmlUiAdventureSlot current = AdventureSlots[expected.Slot - 1];
        if (current != expected || current.Unreadable
            || (!newGame && !current.Used)) return false;
        AdventureLaunches++;
        LastAdventure = (expected, newGame, hunter);
        return true;
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
    public RmlUiOfflineRules? ReadOfflineRules() => OfflineRulesCache is {} cache ? cache.Read() : OfflineRules;
    public bool OfflineRuleSavesAreAsync => OfflineRulesAsync;
    public Task<RmlUiOfflineRulesSaveResult> SaveOfflineRulesAsync(RmlUiOfflineRulesChange change,CancellationToken token)
        => AsyncOfflineRuleSave?.Invoke(change,token) ?? Task.FromResult(new RmlUiOfflineRulesSaveResult(change,false,"FAKE ASYNC RULE OWNER UNAVAILABLE",null));
    public bool SaveOfflineRules(RmlUiOfflineRules expected, RmlUiOfflineRules next, out string error)
    {
        OfflineRulesSaveAttempts++;
        error = FailOfflineRulesSave ? "SIMULATED SAVE FAILURE // RETRY OR CANCEL"
            : OfflineRules != expected ? "RULES CHANGED // REOPEN" : "";
        if (error.Length != 0) return false;
        OfflineRules = next;
        OfflineRulesSaves++;
        return true;
    }
    public bool AcceptJoin(LaunchPlan _) { Accepted++; PartyEvents.Add("accept"); CreateEvents.Add("accept"); return AcceptJoinResult; }
    public int LegacyCreateCalls;
    public bool OpenCreateLobby() { LegacyCreateCalls++; return true; }
    public bool NavigateTo(RmlUiRouteArgument route)
    {
        NavigationCalls++;
        if (route == RmlUiRouteArgument.Theatre) LegacyTheatreHandoffs++;
        return true;
    }
    public Task<RmlUiReplayEntry[]> ReadTheatreAsync(CancellationToken token)
    {
        TheatreReadCalls++;
        return TheatreReadPending.Task.WaitAsync(token);
    }
    public Task<string> ValidateReplayAsync(RmlUiReplayEntry entry, CancellationToken token)
    {
        LastValidatedReplay = entry;
        LastReplayValidationToken = token;
        TheatreValidations++;
        return IgnoreReplayValidationCancellation ? TheatreValidationPending.Task : TheatreValidationPending.Task.WaitAsync(token);
    }
    public Task<RmlUiReplayWorkResult> WorkReplayAsync(RmlUiReplayWorkRequest request, CancellationToken token)
    {
        LastTheatreWork = request;
        LastTheatreWorkToken = token;
        TheatreWorkThread = Environment.CurrentManagedThreadId;
        Interlocked.Increment(ref TheatreWorkCalls);
        return TheatreWorkPending.Task; // Ignore cancellation to verify controller epoch fencing.
    }
    public bool RevealReplayFolder() { RevealCalls++; return true; }
    public bool LaunchReplay(RmlUiReplayEntry entry)
    {
        if (!TheatreRows.Any(r => r.Path == entry.Path
            && r.Bytes == entry.Bytes && r.LastWriteTicks == entry.LastWriteTicks
            && r.VirtualWitness == entry.VirtualWitness && r.Playable)) return false;
        ReplayLaunches++;
        LastReplay = entry;
        return true;
    }
    public bool LaunchReplay(RmlUiReplayEntry entry, bool openEditor)
    {
        ReplayLaunchAttempts++;
        LastReplayOpenEditor = openEditor;
        if (openEditor && !ReplayEditorSupported) return false;
        return LaunchReplay(entry);
    }
    public bool ToggleReplayFavorite(RmlUiReplayEntry entry)
    {
        int index = Array.FindIndex(TheatreRows, x => x.Path == entry.Path
            && x.Bytes == entry.Bytes && x.LastWriteTicks == entry.LastWriteTicks);
        if (index < 0) return false;
        TheatreRows[index] = TheatreRows[index] with { Favorite = !TheatreRows[index].Favorite };
        TheatreFavorites++;
        return true;
    }
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
    public RmlUiLobbyChrome ReadLobbyChrome() => LobbyView.Active ? Chrome
        : RmlUiLobbyChrome.Empty;
    public bool SendLobbyChat(string message)
    {
        if (!LobbyView.Active || String.IsNullOrWhiteSpace(message)
            || System.Text.Encoding.UTF8.GetByteCount(message.Trim()) > ChatPacket.MaxTextBytes)
            return false;
        ChatCalls++;
        LastChat = message.Trim();
        return true;
    }
    public bool CycleLobbyTeam(int index)
    {
        RmlUiAdminSnapshot? now = ReadAdmin();
        var actor = RmlUiLobbyDisplayOrder.Resolve(now, index);
        if (now == null || !RmlUiLobbyDisplayOrder.CanChangeTeam(now, actor))
            return false;
        return SubmitAdmin(new RmlUiAdminRequest(RmlUiAdminOperation.SetTeam,
            now.MatchId, now.AuthorityEpoch, now.SessionRevision, actor!.Value.Slot,
            actor.Value.Generation, Team: RmlUiLobbyDisplayOrder.NextTeam(now, actor.Value)));
    }

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
    public Func<CancellationToken, Task<IReadOnlyList<RmlUiArenaChoice>>>? MapCatalog;
    public Func<IReadOnlyList<string>, RmlUiArenaPreview[]>? MapPreviews;
    public Task<IReadOnlyList<RmlUiArenaChoice>> ReadMapPickerCatalogAsync(CancellationToken token)
        => MapCatalog?.Invoke(token) ?? Task.FromResult<IReadOnlyList<RmlUiArenaChoice>>(
            CommunityMaps.Select(room => new RmlUiArenaChoice(room, RmlUiArenaCategory.Community)).ToArray());
    public RmlUiArenaPreview[] ReadMapPickerPreviews(IReadOnlyList<string> rooms)
        => MapPreviews?.Invoke(rooms) ?? Array.Empty<RmlUiArenaPreview>();
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
