using MphRead;
using MphRead.Droid;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Core;

int checks = 0;
void Check(bool value, string label)
{ checks++; if (!value) throw new InvalidOperationException(label); }
string hash = new('A', 64);
var occupied = new RmlUiAdventureSlot(1, true, false, "SYNTHETIC ARCHIVES", 2, 79, 99, hash);
var empty = new RmlUiAdventureSlot(2, false, false, "", 0, 0, 0, "EMPTY");
var dock = new CampaignDockReceipt(1, CampaignPlanet.CelestialArchives);
var plan = new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace };
Check(!default(RmlUiAndroidLocalLaunchRequest).MatchesPlan(plan, 0), "default request cannot admit a plan");
Check(RmlUiAndroidLocalLaunchRequest.TryAdventure(occupied, false, Hunter.Trace, out var resume)
    && resume.MatchesPlan(plan, 0), "resume captures exact occupied slot and hunter");
Check(resume.IsCurrent(_ => occupied, () => dock), "unchanged detached source remains current");
foreach (var changed in new[] { occupied with { Fingerprint = new string('B', 64) }, occupied with { Slot = 2 },
    occupied with { Area = "OTHER AREA" }, occupied with { Health = 78 }, occupied with { HealthMax = 199 },
    occupied with { Octoliths = 3 }, occupied with { Used = false }, occupied with { Unreadable = true } })
    Check(!resume.IsCurrent(_ => changed, () => dock), "every captured save metadata/byte change refuses admission");
Check(!RmlUiAndroidLocalLaunchRequest.TryAdventure(empty, false, Hunter.Trace, out _), "empty resume refuses");
Check(RmlUiAndroidLocalLaunchRequest.TryAdventure(empty, true, Hunter.Trace, out var fresh)
    && fresh.MatchesPlan(new() { Kind = LaunchKind.Adventure, SaveSlot = 2, Hunter = Hunter.Trace, NewGame = true }, 0),
    "new run captures exact empty destination without writing it");
Check(!fresh.IsCurrent(_ => occupied with { Slot = 2 }, () => dock), "new-run empty slot cannot replace newly appeared bytes");
foreach (var invalid in new[] { occupied with { Slot = 0 }, occupied with { Slot = 4 }, occupied with { Unreadable = true },
    occupied with { Fingerprint = "UNAVAILABLE" }, occupied with { Fingerprint = "EMPTY" }, occupied with { Fingerprint = new string('Z', 64) },
    empty with { Fingerprint = hash } })
    Check(!RmlUiAndroidLocalLaunchRequest.TryAdventure(invalid, true, Hunter.Trace, out _), "malformed/unavailable identities never authorize New Run");
Check(!RmlUiAndroidLocalLaunchRequest.TryAdventure(occupied, false, (Hunter)7, out _), "unresolved hunter rejected");
foreach (var mismatch in new[] {
    new LaunchPlan { Kind = LaunchKind.Offline, SaveSlot = 1, Hunter = Hunter.Trace },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 2, Hunter = Hunter.Trace },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Samus },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace, NewGame = true },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace, AdventureDestination = CampaignPlanet.Alinos },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace, ExitShipToSurface = true },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace, Spectate = true },
    new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace, IsPlaytest = true } })
    Check(!resume.MatchesPlan(mismatch, 0), "foreign or changed plan cannot substitute for captured resume");
var shipPlan = new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace,
    AdventureDestination = CampaignPlanet.CelestialArchives, ExitShipToSurface = true };
var effect = new NativeCampaignShipEffect<LaunchPlan>(true, shipPlan, new(1, (int)dock.Planet)) { Fingerprint = hash };
Check(RmlUiAndroidLocalLaunchRequest.TryShip(effect, out var ship) && ship.MatchesPlan(shipPlan, 0)
    && ship.IsCurrent(_ => occupied, () => dock), "ship admission retains exact dock, fingerprint, hunter and destination");
Check(!ship.IsCurrent(_ => occupied with { Fingerprint = new string('C', 64) }, () => dock), "ship changed SHA refuses");
Check(!ship.IsCurrent(_ => occupied, () => null), "retired dock refuses");
Check(!ship.IsCurrent(_ => occupied, () => new(2, dock.Planet)), "foreign dock slot refuses");
Check(!ship.IsCurrent(_ => occupied, () => new(1, CampaignPlanet.Alinos)), "changed dock planet refuses");
int dockReads = 0;
Check(!ship.IsCurrent(_ => occupied, () => ++dockReads == 1 ? dock : null), "dock changed during save read refuses");
Check(!RmlUiAndroidLocalLaunchRequest.TryShip(effect with { Launch = false }, out _), "Back effect cannot launch");
Check(!RmlUiAndroidLocalLaunchRequest.TryShip(effect with { Fingerprint = "EMPTY" }, out _), "unverified ship save refuses");
Check(!RmlUiAndroidLocalLaunchRequest.TryShip(effect with { Dock = new(2, (int)dock.Planet) }, out _), "ship slot mismatch refuses");
Check(!ship.MatchesPlan(plan, 0), "resume plan cannot replace ship departure");
var wrongLanding = new LaunchPlan { Kind = LaunchKind.Adventure, SaveSlot = 1, Hunter = Hunter.Trace,
    AdventureDestination = CampaignPlanet.Alinos, ExitShipToSurface = true };
Check(!RmlUiAndroidLocalLaunchRequest.TryShip(effect with { Plan = wrongLanding }, out _), "Exit Ship cannot change current dock planet");

var offline = new RmlUiOfflineRequest("SYNTHETIC", GameMode.Battle, Hunter.Trace, 2, 3, 1);
var offlineRequest = RmlUiAndroidLocalLaunchRequest.ForOffline(offline);
var offlinePlan = new LaunchPlan { Kind = LaunchKind.Offline, RoomKey = "SYNTHETIC", Mode = GameMode.Battle,
    Hunter = Hunter.Trace, Bots = 3, BotLevel = 1 };
Check(offlineRequest.MatchesPlan(offlinePlan, 2) && !offlineRequest.MatchesPlan(offlinePlan, 1), "existing Offline suit witness is preserved");
Check(!resume.MatchesPlan(offlinePlan, 2) && !offlineRequest.MatchesPlan(plan, 2), "local tagged families cannot cross-admit");
var practice = offline with { Hunter = Hunter.Weavel, Bots = 0,
    Character = OfflineCharacterMode.TrooperWallClingPractice };
var practiceRequest = RmlUiAndroidLocalLaunchRequest.ForOffline(practice);
var practicePlan = offlinePlan with { Hunter = Hunter.Weavel, Bots = 0,
    OfflineCharacter = OfflineCharacterMode.TrooperWallClingPractice, IsPlaytest = true };
Check(practiceRequest.MatchesPlan(practicePlan, 2), "Android request retains explicit wall-cling practice identity");
Check(!practiceRequest.MatchesPlan(practicePlan with { OfflineCharacter = OfflineCharacterMode.Native }, 2)
    && !offlineRequest.MatchesPlan(offlinePlan with { OfflineCharacter = OfflineCharacterMode.TrooperWallClingPractice }, 2),
    "native and practice Android handoffs cannot substitute mechanics");
Check(!practiceRequest.MatchesPlan(practicePlan with { IsPlaytest = false }, 2)
    && !offlineRequest.MatchesPlan(offlinePlan with { IsPlaytest = true }, 2),
    "captured practice distinction requires its matching playtest lifetime");
Check(!resume.MatchesPlan(plan with { OfflineCharacter = OfflineCharacterMode.TrooperWallClingPractice }, 0)
    && !ship.MatchesPlan(shipPlan with { OfflineCharacter = OfflineCharacterMode.TrooperWallClingPractice }, 0),
    "Adventure and Ship handoffs cannot carry offline character mechanics");
var drone = practice with { Character = OfflineCharacterMode.TrooperScoutDronePractice };
var droneRequest = RmlUiAndroidLocalLaunchRequest.ForOffline(drone);
var dronePlan = practicePlan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice };
Check(droneRequest.MatchesPlan(dronePlan, 2), "Android handoff retains distinct explicit Scout Drone practice");
Check(!droneRequest.MatchesPlan(practicePlan, 2) && !practiceRequest.MatchesPlan(dronePlan, 2)
    && !offlineRequest.MatchesPlan(dronePlan, 2), "wall-cling, drone and native captures cannot substitute each other's mechanics");
Check(!droneRequest.MatchesPlan(dronePlan with { IsPlaytest = false }, 2)
    && !droneRequest.MatchesPlan(dronePlan, 1), "drone admission retains exact playtest and suit witnesses");
foreach (var bad in new[] { dronePlan with { OfflineCharacter = (OfflineCharacterMode)77 },
    dronePlan with { Hunter = Hunter.Samus }, dronePlan with { Bots = 1 }, dronePlan with { Mode = GameMode.BattleTeams },
    dronePlan with { RoomKey = "OTHER" }, dronePlan with { Kind = LaunchKind.Adventure } })
    Check(!droneRequest.MatchesPlan(bad, 2), "changed drone request identity cannot cross the Android handoff");
var unknownRequest = RmlUiAndroidLocalLaunchRequest.ForOffline(drone with { Character = (OfflineCharacterMode)77 });
Check(!unknownRequest.MatchesPlan(dronePlan with { OfflineCharacter = (OfflineCharacterMode)77 }, 2),
    "even matching unknown enum values never admit private mechanics");
Check(!resume.MatchesPlan(plan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice }, 0)
    && !ship.MatchesPlan(shipPlan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice }, 0),
    "Adventure and Ship cannot carry drone mechanics");
var droneAdmission = new RmlUiAndroidLauncherAdmission<RmlUiAndroidLocalLaunchRequest, LaunchPlan>();
Check(droneAdmission.TryRequest(droneRequest, out var droneCapture)
    && !droneAdmission.TryAccept(droneCapture with { Request = practiceRequest }, practicePlan)
    && droneAdmission.TryAccept(droneCapture, dronePlan), "pending Android owner cannot replace captured drone with wall-cling");
Check(droneAdmission.TryTakePlan(() => true, out var deliveredDrone, out var deliveredRequest)
    && deliveredDrone.Equals(dronePlan) && deliveredRequest == droneRequest
    && !droneAdmission.TryTakePlan(() => true, out _, out _), "exact detached drone plan/request retires atomically once");
// New mode3 handoff is an immutable exact-identity boundary. Shared launcher
// policy separately validates the supported count/difficulty/suit ranges.
var combat = practice with { Character = OfflineCharacterMode.TrooperScoutDroneCombatPractice, Bots = 1 };
var combatRequest = RmlUiAndroidLocalLaunchRequest.ForOffline(combat);
var combatPlan = practicePlan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDroneCombatPractice, Bots = 1 };
Check((int)combat.Character == 3 && combatRequest.MatchesPlan(combatPlan, 2),
    "Android captures literal mode3 combat with its exact one-bot private lifetime");
Check(!combatRequest.MatchesPlan(dronePlan, 2) && !combatRequest.MatchesPlan(practicePlan, 2)
    && !combatRequest.MatchesPlan(offlinePlan, 2) && !droneRequest.MatchesPlan(combatPlan, 2)
    && !practiceRequest.MatchesPlan(combatPlan, 2) && !offlineRequest.MatchesPlan(combatPlan, 2),
    "combat, drone, wall-cling and native captures cannot substitute mechanics");
for (int bots = 0; bots <= 7; bots++)
for (int level = 0; level <= 3; level++)
for (int suit = 0; suit <= 3; suit++)
{
    var exact = combat with { Bots = bots, BotLevel = level, Suit = suit };
    var request = RmlUiAndroidLocalLaunchRequest.ForOffline(exact);
    var exactPlan = combatPlan with { Bots = bots, BotLevel = level };
    Check(request.MatchesPlan(exactPlan, suit), "each supported raw combat tuple retains exact Android identity");
    Check(!request.MatchesPlan(exactPlan with { Bots = (bots + 1) % 8 }, suit)
        && !request.MatchesPlan(exactPlan with { BotLevel = (level + 1) % 4 }, suit)
        && !request.MatchesPlan(exactPlan, (suit + 1) % 4),
        "even another valid bot/difficulty/suit cannot replace the captured combat tuple");
}
foreach (var bad in new[] {
    combatPlan with { Bots = 0 }, combatPlan with { Bots = 2 }, combatPlan with { Bots = -1 }, combatPlan with { Bots = 8 },
    combatPlan with { BotLevel = -1 }, combatPlan with { BotLevel = 4 }, combatPlan with { BotLevel = 2 },
    combatPlan with { IsPlaytest = false }, combatPlan with { Hunter = Hunter.Samus },
    combatPlan with { Mode = GameMode.BattleTeams }, combatPlan with { RoomKey = "OTHER" },
    combatPlan with { Kind = LaunchKind.Online }, combatPlan with { Kind = LaunchKind.Adventure },
    combatPlan with { OfflineCharacter = (OfflineCharacterMode)4 }, combatPlan with { SaveSlot = 1 },
    combatPlan with { NewGame = true }, combatPlan with { AdventureDestination = CampaignPlanet.Alinos },
    combatPlan with { ExitShipToSurface = true } })
    Check(!combatRequest.MatchesPlan(bad, 2), "mutating any captured combat identity cannot cross Android handoff");
Check(!combatRequest.MatchesPlan(combatPlan, -1) && !combatRequest.MatchesPlan(combatPlan, 4),
    "out-of-range current suits cannot equal captured valid combat suit");
Check(!resume.MatchesPlan(plan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDroneCombatPractice }, 0)
    && !ship.MatchesPlan(shipPlan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDroneCombatPractice }, 0),
    "Adventure and Ship cannot acquire combat mechanics");
var combatTaggedShip = shipPlan with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDroneCombatPractice };
Check(!RmlUiAndroidLocalLaunchRequest.TryShip(effect with { Plan = combatTaggedShip }, out var forbiddenShip)
    || !forbiddenShip.MatchesPlan(combatTaggedShip, 0),
    "a combat-tagged ship effect cannot pass final Android handoff even if the factory captures it");
var combatAdmission = new RmlUiAndroidLauncherAdmission<RmlUiAndroidLocalLaunchRequest, LaunchPlan>();
Check(combatAdmission.TryRequest(combatRequest, out var combatCapture)
    && !combatAdmission.TryAccept(combatCapture with { Request = droneRequest }, dronePlan)
    && !combatAdmission.TryAccept(combatCapture with {
        Request = RmlUiAndroidLocalLaunchRequest.ForOffline(combat with { Bots = 2 }) }, combatPlan with { Bots = 2 })
    && !combatAdmission.TryAccept(combatCapture with {
        Request = RmlUiAndroidLocalLaunchRequest.ForOffline(combat with { BotLevel = 2 }) }, combatPlan with { BotLevel = 2 })
    && !combatAdmission.TryAccept(combatCapture with {
        Request = RmlUiAndroidLocalLaunchRequest.ForOffline(combat with { Suit = 1 }) }, combatPlan)
    && combatAdmission.TryAccept(combatCapture, combatPlan),
    "same-generation Android owner cannot exchange captured combat mechanics, count, difficulty or suit");
Check(combatAdmission.TryTakePlan(() => true, out var deliveredCombat, out var deliveredCombatRequest)
    && deliveredCombat.Equals(combatPlan) && deliveredCombatRequest == combatRequest
    && !combatAdmission.TryTakePlan(() => true, out _, out _),
    "exact combat request and plan retire atomically once");

var admission = new RmlUiAndroidLauncherAdmission<RmlUiAndroidLocalLaunchRequest, LaunchPlan>();
Check(admission.TryRequest(resume, out var captured) && !admission.TryRequest(ship, out _), "one request can await existing UI owner");
Check(!admission.TryAccept(captured with { Request = ship }, shipPlan), "same generation cannot substitute a different captured request");
Check(admission.TryAccept(captured, plan), "exact request accepts one owner-produced plan");
int retired = 0;
Check(admission.TryTakePlan(() => { retired++; return true; }, out var accepted, out var acceptedRequest)
    && accepted.SaveSlot == 1 && acceptedRequest == resume && retired == 1,
    "plan and immutable request are atomically copied before successful original-owner retirement");
Check(!admission.TryTakePlan(() => { retired++; return true; }, out _, out _) && retired == 1
    && !admission.TryAccept(captured, plan), "retired admission cannot repeat effect or expose request again");
var failed = new RmlUiAndroidLauncherAdmission<RmlUiAndroidLocalLaunchRequest, LaunchPlan>();
failed.TryRequest(fresh, out var failureWitness); failed.TryAccept(failureWitness, plan);
Check(!failed.TryTakePlan(() => false, out var absentPlan, out var absentRequest)
    && absentRequest == default && absentPlan.Kind == LaunchKind.None,
    "failed native retirement consumes both request and plan without exposing them");
Console.WriteLine($"PASS {checks} Android Adventure request checks: actual linked immutable admission source + current engine DTOs; no GameView/EGL, assets, user saves, simulation or device acceptance.");
