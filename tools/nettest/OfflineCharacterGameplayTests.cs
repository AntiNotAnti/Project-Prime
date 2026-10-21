using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead.Entities;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;
using MphRead.Mods.Training;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

// Pure draft/launch policy and real replay admission. No game data, room
// initialization, player physics, native UI, GL, network or disk recorder.
internal static class OfflineCharacterGameplayTests
{
    private static int _checks;
    private static void Check(bool value, string why)
    { _checks++; if (!value) throw new InvalidOperationException(why); }
    private static bool Refused(Action action)
    { try { action(); return false; } catch (ArgumentException) { return true; } }

    internal static int Run()
    {
        _checks = 0;
        string temporary = Path.Combine(Path.GetTempPath(), "prime-offline-character-test-" + Guid.NewGuid().ToString("N"));
        string oldDirectory = LauncherPrefs.Directory;
        var oldPrefs = (LauncherPrefs.LastHunter, LauncherPrefs.LastColor, LauncherPrefs.Bots,
            LauncherPrefs.BotLevel, LauncherPrefs.LastKind, LauncherPrefs.PlayerName);
        try
        {
            Check(!NetSession.Active && !DemoPlayback.IsActive && !ReplayCapture.LiveRecordingActive,
                "offline policy fixture requires no active transport, playback or writer");
            Directory.CreateDirectory(temporary); LauncherPrefs.Directory = temporary;
            byte[] sentinel = new byte[] { 1, 7, 19, 251 }; File.WriteAllBytes(Path.Combine(temporary, "launcher.txt"), sentinel);
            LauncherPrefs.LastHunter = Hunter.Spire; LauncherPrefs.LastColor = 2; LauncherPrefs.Bots = 3;
            LauncherPrefs.BotLevel = 2; LauncherPrefs.LastKind = 87; LauncherPrefs.PlayerName = "PLAN FIXTURE";
            var settings = new MenuSettings { RoomKey = "previous-room", MovementModel = "Native",
                BalancedMode = "off", LowTier = "off", RiftlineM7Available = false };
            string settingsBefore = JsonSerializer.Serialize(settings);

            var ordinary = OfflineLaunch.Preview(settings, "synthetic-arena", GameMode.Battle, Hunter.Samus, 2, 3, 2);
            var explicitNative = OfflineLaunch.Preview(settings, "synthetic-arena", GameMode.Battle, Hunter.Samus, 2, 3, 2, OfflineCharacterMode.Native);
            Check(ordinary.OfflineCharacter == OfflineCharacterMode.Native && !ordinary.IsPlaytest
                && ordinary.Kind == LaunchKind.Offline && ordinary.Hunter == Hunter.Samus
                && ordinary.Mode == GameMode.Battle && ordinary.Bots == 3 && ordinary.BotLevel == 2,
                "existing ordinary offline request stays native with the same match and player facts");
            Check(ordinary.MatchRules == explicitNative.MatchRules && ordinary.Hunter == explicitNative.Hunter
                && ordinary.OfflineCharacter == explicitNative.OfflineCharacter && !explicitNative.IsPlaytest,
                "omitted and explicit Native selections remain equivalent");
            Check(OfflineCharacterGameplay.TryAdmit(ordinary, out _) && OfflineCharacterGameplay.TryAdmit(
                ordinary with { Kind = LaunchKind.Online }, out _), "Native helper adds no new local or online policy restriction");
            foreach (int suit in new[] { 0, 1, 2, 3 })
            {
                // Neither the Weavel actor, a source-looking room/ID nor the HD
                // presentation preference can opt in to new gameplay mechanics.
                var presentation = new MenuSettings { MovementModel = "Native", CharacterModelReplacements = "on" };
                var native = OfflineLaunch.Preview(presentation, "trooper-power", GameMode.Battle, Hunter.Weavel, suit, 0, 0);
                Check(native.OfflineCharacter == OfflineCharacterMode.Native && !native.IsPlaytest,
                    "presentation and native actor identity do not infer Trooper mechanics, suit" + suit);
            }

            var draft = new RmlUiOfflineDraft(new[] { "synthetic-arena" }, Hunter.Samus, 2, 4, 1);
            Check(draft.Current.Character == OfflineCharacterMode.Native && draft.Valid, "new draft is ordinary native play");
            Check(draft.Change(16) && draft.Current.Character == OfflineCharacterMode.TrooperWallClingPractice
                && draft.Current.Hunter == Hunter.Weavel && draft.Current.Mode == GameMode.Battle && draft.Current.Bots == 0 && draft.Valid,
                "literal choice16 explicitly selects a valid Weavel Battle with no bots");
            RmlUiOfflineRequest captured = draft.Current;
            Check(draft.Change(16) && draft.Current.Character == OfflineCharacterMode.TrooperScoutDronePractice
                && draft.Current.Hunter == Hunter.Weavel && draft.Current.Mode == GameMode.Battle && draft.Current.Bots == 0 && draft.Valid,
                "second choice16 selects explicit Scout Drone practice without changing its required actor/match");
            RmlUiOfflineRequest capturedDrone = draft.Current;
            Check(draft.CharacterName == "TROOPER SCOUT DRONE" && draft.CharacterNotice.Contains("Native fallback models", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Zoom for a laser", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("No blaster or replays", StringComparison.Ordinal),
                "drone draft names native fallbacks, explicit laser input and still-excluded abilities");
            Check(draft.Change(16) && draft.Current.Character == OfflineCharacterMode.TrooperScoutDroneCombatPractice
                && (int)draft.Current.Character == 3 && draft.Current.Hunter == Hunter.Weavel
                && draft.Current.Mode == GameMode.Battle && draft.Current.Bots == 1 && draft.Valid,
                "third choice16 explicitly captures combat practice with one native opponent");
            RmlUiOfflineRequest capturedCombat = draft.Current;
            Check(draft.CharacterName == "DRONE COMBAT PRACTICE"
                && draft.CharacterNotice.Contains("Native fallback models", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Zoom for a laser", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("No blaster or replays", StringComparison.Ordinal),
                "combat notice exposes its actual fallback, controls and exclusions");
            Check(draft.Change(16) && draft.Current.Character == OfflineCharacterMode.TrooperArsenalPractice
                && (int)draft.Current.Character == 4 && draft.Current.Hunter == Hunter.Weavel
                && draft.Current.Mode == GameMode.Battle && draft.Current.Bots == 1 && draft.Valid,
                "fourth choice16 explicitly captures Arsenal with one native opponent");
            RmlUiOfflineRequest capturedArsenal = draft.Current;
            Check(draft.CharacterName == "TROOPER ARSENAL"
                && draft.CharacterNotice.Contains("Hunters BODY", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Trooper body and Blaster cannon", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Native keeps native models", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Unsupported poses and drone use native fallback", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Native bolts", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("60 shared UA each life", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("Private Blaster", StringComparison.Ordinal)
                && draft.CharacterNotice.Contains("No replays", StringComparison.Ordinal),
                "Arsenal notice distinguishes selected body/cannon, native fallback and unchanged private UA/bolts/no-replay rules");
            Check(draft.Change(16) && draft.Current.Character == OfflineCharacterMode.Native && draft.CharacterNotice == "",
                "fifth choice16 cycles back to Native without a persistent gameplay preference");
            Check(captured.Character == OfflineCharacterMode.TrooperWallClingPractice
                && capturedDrone.Character == OfflineCharacterMode.TrooperScoutDronePractice
                && capturedCombat.Character == OfflineCharacterMode.TrooperScoutDroneCombatPractice && capturedCombat.Bots == 1
                && capturedArsenal.Character == OfflineCharacterMode.TrooperArsenalPractice && capturedArsenal.Bots == 1
                && new RmlUiOfflineDraft(new[] { "synthetic-arena" }, Hunter.Weavel, 0, 0, 0).Current.Character == OfflineCharacterMode.Native,
                "captured wall/drone/combat/Arsenal requests are detached and a fresh draft never inherits mechanics");
            var practice = OfflineLaunch.Preview(settings, captured.RoomKey, captured.Mode, captured.Hunter,
                captured.Suit, captured.Bots, captured.BotLevel, captured.Character);
            Check(practice.OfflineCharacter == OfflineCharacterMode.TrooperWallClingPractice && practice.IsPlaytest
                && practice.Hunter == Hunter.Weavel && practice.Bots == 0 && practice.MatchRules.MovementModel == MovementModel.Native,
                "detached captured request carries explicit mechanics through the shared pure plan builder");
            Check(OfflineCharacterGameplay.TryAdmit(practice, out string reason) && reason == "",
                "the supported explicit offline plan is admitted");
            var drone = OfflineLaunch.Preview(settings, capturedDrone.RoomKey, capturedDrone.Mode, capturedDrone.Hunter,
                capturedDrone.Suit, capturedDrone.Bots, capturedDrone.BotLevel, capturedDrone.Character);
            Check(drone.OfflineCharacter == OfflineCharacterMode.TrooperScoutDronePractice && drone.IsPlaytest
                && drone.Hunter == Hunter.Weavel && drone.Mode == GameMode.Battle && drone.Bots == 0,
                "captured drone request carries its distinct explicit mechanics through pure plan creation");
            Check(OfflineCharacterGameplay.TryAdmit(drone, out var droneReason) && droneReason == ""
                && CharacterGameplayRules.TrooperDroneV1.IsValidFor(Hunter.Weavel)
                && !CharacterGameplayRules.TrooperDroneV1.IsValidFor(Hunter.Samus)
                && CharacterGameplayRules.TrooperDroneV1 != CharacterGameplayRules.TrooperV1,
                "drone policy is admitted with a distinct finite Weavel-only gameplay rule");
            var combat = OfflineLaunch.Preview(settings, capturedCombat.RoomKey, capturedCombat.Mode, capturedCombat.Hunter,
                capturedCombat.Suit, capturedCombat.Bots, capturedCombat.BotLevel, capturedCombat.Character);
            CombatPolicy(settings, capturedCombat, combat);
            var arsenal = OfflineLaunch.Preview(settings, capturedArsenal.RoomKey, capturedArsenal.Mode, capturedArsenal.Hunter,
                capturedArsenal.Suit, capturedArsenal.Bots, capturedArsenal.BotLevel, capturedArsenal.Character);
            CombatPolicy(settings, capturedArsenal, arsenal, arsenal: true);
            Check(CharacterGameplayRules.TrooperArsenalV1.UsesPirateBlaster
                && CharacterGameplayRules.TrooperArsenalV1.UsesScoutDrone
                && !CharacterGameplayRules.Native.UsesPirateBlaster && !CharacterGameplayRules.TrooperV1.UsesPirateBlaster
                && !CharacterGameplayRules.TrooperDroneV1.UsesPirateBlaster && !CharacterGameplayRules.TrooperDroneCombatV1.UsesPirateBlaster,
                "only explicit current-version Arsenal rule opts into private Blaster while old modes retain their exact abilities");
            Check(!new CharacterGameplayRules(0, CharacterGameplayVariant.TrooperArsenalV1).UsesPirateBlaster
                && !new CharacterGameplayRules(2, CharacterGameplayVariant.TrooperArsenalV1).UsesPirateBlaster,
                "unsupported rule versions cannot activate private Blaster");
            Check(!OfflineRematch.TryPlan(drone, "another-synthetic-arena", out _), "drone practice is excluded from ordinary rematch");
            Check(!OfflineRematch.TryPlan(practice, "another-synthetic-arena", out _), "private practice playtest cannot enter ordinary results rematch");
            Check(OfflineRematch.TryPlan(ordinary, "another-synthetic-arena", out var rematch)
                && rematch.OfflineCharacter == OfflineCharacterMode.Native && !rematch.IsPlaytest,
                "ordinary Native rematch remains available without inferring private mechanics");

            foreach (var bad in new[] {
                captured with { Character = (OfflineCharacterMode)77 }, captured with { Hunter = Hunter.Samus },
                captured with { Mode = GameMode.BattleTeams }, captured with { Bots = 1 },
                captured with { Bots = -1 }, captured with { Suit = 4 } })
                Check(!bad.ValidateForm(new[] { "synthetic-arena" }), "invalid explicit practice form refused before launch");
            foreach (var bad in new[] {
                capturedDrone with { Character = (OfflineCharacterMode)(-1) }, capturedDrone with { Character = (OfflineCharacterMode)77 },
                capturedDrone with { Hunter = Hunter.Samus }, capturedDrone with { Mode = GameMode.BattleTeams },
                capturedDrone with { Bots = 1 }, capturedDrone with { Bots = -1 }, capturedDrone with { Suit = 4 } })
                Check(!bad.ValidateForm(new[] { "synthetic-arena" }), "invalid drone form refuses before launch");
            Check(!capturedDrone.ValidateForm(new[] { "other-arena" }), "drone capture also requires a listed room");
            Check(!captured.ValidateForm(new[] { "other-arena" }), "captured practice still requires a listed room");
            foreach (int changed in new[] { 1, 3, 5 })
            {
                var editable = new RmlUiOfflineDraft(new[] { "synthetic-arena" }, Hunter.Samus, 0, 3, 1);
                editable.Change(16); Check(editable.Change(changed) && !editable.Valid,
                    "editing mode/bots/hunter cannot leave an invalid practice draft launchable: " + changed);
                Check(editable.Change(16) && editable.Current.Character == OfflineCharacterMode.TrooperScoutDronePractice
                    && editable.Current.Hunter == Hunter.Weavel && editable.Current.Mode == GameMode.Battle
                    && editable.Current.Bots == 0 && editable.Valid,
                    "explicit drone selection captures its required valid Weavel/Battle/zero-bot configuration");
                Check(editable.Change(changed) && !editable.Valid,
                    "independently editing mode/bots/hunter cannot leave drone practice launchable: " + changed);
                var editedDrone = editable.Current;
                Check(Refused(() => OfflineLaunch.Preview(settings, editedDrone.RoomKey, editedDrone.Mode, editedDrone.Hunter,
                    editedDrone.Suit, editedDrone.Bots, editedDrone.BotLevel, editedDrone.Character)),
                    "pure plan creation refuses an independently edited invalid drone draft");
                var forgedDrone = drone with { Hunter = editedDrone.Hunter, Mode = editedDrone.Mode, Bots = editedDrone.Bots,
                    MatchRules = drone.MatchRules with { Mode = editedDrone.Mode } };
                Check(!OfflineMatchPreflight.TryAdmit(forgedDrone, out var editedReason) && !string.IsNullOrWhiteSpace(editedReason),
                    "actual preflight independently refuses the same edited drone facts");
                Check(editable.Change(16) && editable.Current.Character == OfflineCharacterMode.TrooperScoutDroneCombatPractice
                    && editable.Current.Hunter == Hunter.Weavel && editable.Current.Mode == GameMode.Battle
                    && editable.Current.Bots == 1 && editable.Valid,
                    "selecting combat after invalid drone edits restores required actor/match and one opponent");
                Check(editable.Change(16) && editable.Current.Character == OfflineCharacterMode.TrooperArsenalPractice
                    && editable.Current.Hunter == Hunter.Weavel && editable.Current.Mode == GameMode.Battle
                    && editable.Current.Bots == 1 && editable.Valid,
                    "explicit Arsenal selection preserves required actor/match/one-opponent defaults");
                Check(editable.Change(16) && editable.Current.Character == OfflineCharacterMode.Native && editable.Valid,
                    "the same ordinary draft edits remain usable after the fifth Native cycle step");
            }

            var refusedPlans = new[] {
                practice with { Kind = LaunchKind.Online }, practice with { Kind = LaunchKind.Host },
                practice with { Kind = LaunchKind.Adventure }, practice with { Kind = LaunchKind.Demo },
                practice with { Kind = LaunchKind.AimTrainer }, practice with { IsPlaytest = false },
                practice with { OfflineCharacter = (OfflineCharacterMode)77 }, practice with { Hunter = Hunter.Samus },
                practice with { Bots = 1 }, practice with { Mode = GameMode.SinglePlayer },
                practice with { MatchRules = practice.MatchRules with { Mode = GameMode.Survival } },
                practice with { MatchRules = practice.MatchRules with { BalancedMode = true } },
                practice with { MatchRules = practice.MatchRules with { EnhancedHunters = true } },
                practice with { MatchRules = practice.MatchRules with { LowTier = true } },
                practice with { MatchRules = practice.MatchRules with { RiftlineM7Available = true } },
                practice with { MatchRules = practice.MatchRules with { MovementModel = MovementModel.Momentum } },
                practice with { MatchRules = practice.MatchRules with { MovementModel = MovementModel.Quake3 } },
            practice with { MatchRules = practice.MatchRules with { MovementModel = MovementModel.Traversal } },
            practice with { MatchRules = practice.MatchRules with { MovementModel = MovementModel.Parkour } }
            };
            foreach (var bad in refusedPlans.Concat(refusedPlans.Select(p => p.OfflineCharacter == OfflineCharacterMode.TrooperWallClingPractice
                ? p with { OfflineCharacter = OfflineCharacterMode.TrooperScoutDronePractice } : p)))
            {
                Check(!OfflineCharacterGameplay.TryAdmit(bad, out var policyReason) && !string.IsNullOrWhiteSpace(policyReason),
                    "forged unsupported plan fails the shared character policy");
                Check(!OfflineMatchPreflight.TryAdmit(bad, out var preflightReason) && preflightReason == policyReason,
                    "launch preflight refuses the same unsupported plan before any synthetic room/media resolution");
            }
            foreach (MenuSettings conflict in new[] {
                new MenuSettings { MovementModel = "Native", BalancedMode = "on" },
                new MenuSettings { MovementModel = "Native", LowTier = "on" },
                new MenuSettings { MovementModel = "Momentum" }, new MenuSettings { MovementModel = "Quake3" },
                new MenuSettings { MovementModel = "Traversal" }, new MenuSettings { MovementModel = "Parkour" },
                new MenuSettings { MovementModel = "Native", RiftlineM7Available = true } })
                foreach (var mechanics in new[] { OfflineCharacterMode.TrooperWallClingPractice, OfflineCharacterMode.TrooperScoutDronePractice,
                    OfflineCharacterMode.TrooperScoutDroneCombatPractice, OfflineCharacterMode.TrooperArsenalPractice })
                    Check(Refused(() => OfflineLaunch.Preview(conflict, "synthetic-arena", GameMode.Battle, Hunter.Weavel, 0, 0, 0,
                        mechanics)), "active conflicting saved rule refuses either pure practice preview before persistence");
            // The old EnhancedHunters string is an ignored compatibility field;
            // it must not activate a retired gameplay rule through this feature.
            var compatibility = new MenuSettings { MovementModel = "Native", EnhancedHunters = "on" };
            var ignored = OfflineLaunch.Preview(compatibility, "synthetic-arena", GameMode.Battle, Hunter.Weavel, 0, 0, 0,
                OfflineCharacterMode.TrooperWallClingPractice);
            Check(!ignored.MatchRules.EnhancedHunters && OfflineCharacterGameplay.TryAdmit(ignored, out _),
                "ignored legacy Enhanced setting stays inactive rather than acquiring new gameplay authority");

            var ignoredDrone = OfflineLaunch.Preview(compatibility, "synthetic-arena", GameMode.Battle, Hunter.Weavel, 0, 0, 0,
                OfflineCharacterMode.TrooperScoutDronePractice);
            Check(!ignoredDrone.MatchRules.EnhancedHunters && OfflineCharacterGameplay.TryAdmit(ignoredDrone, out _),
                "drone preview also keeps the retired Enhanced compatibility setting inactive");
            var ignoredCombat = OfflineLaunch.Preview(compatibility, "synthetic-arena", GameMode.Battle, Hunter.Weavel, 0, 1, 0,
                OfflineCharacterMode.TrooperScoutDroneCombatPractice);
            Check(!ignoredCombat.MatchRules.EnhancedHunters && OfflineCharacterGameplay.TryAdmit(ignoredCombat, out _),
                "combat keeps ignored Enhanced compatibility settings inactive too");
            var ignoredArsenal = OfflineLaunch.Preview(compatibility, "synthetic-arena", GameMode.Battle, Hunter.Weavel, 0, 1, 0,
                OfflineCharacterMode.TrooperArsenalPractice);
            Check(!ignoredArsenal.MatchRules.EnhancedHunters && OfflineCharacterGameplay.TryAdmit(ignoredArsenal, out _),
                "Arsenal also keeps retired Enhanced compatibility settings inactive");
            TransportAndPlaybackFence(practice, drone, combat, arsenal);
            Intents(); ReplayFence(practice); ReplayFence(drone); ReplayFence(combat); ReplayFence(arsenal);
            Check(JsonSerializer.Serialize(settings) == settingsBefore && settings.RoomKey == "previous-room",
                "preview and refused plans leave the caller settings snapshot unchanged");
            Check((LauncherPrefs.LastHunter, LauncherPrefs.LastColor, LauncherPrefs.Bots, LauncherPrefs.BotLevel,
                LauncherPrefs.LastKind, LauncherPrefs.PlayerName) == (Hunter.Spire, 2, 3, 2, 87, "PLAN FIXTURE")
                && File.ReadAllBytes(Path.Combine(temporary, "launcher.txt")).SequenceEqual(sentinel)
                && Directory.GetFiles(temporary).Length == 1, "all pure draft/preview/refusal paths preserve preferences and disk bytes");
            Console.WriteLine($"OFFLINE CHARACTER GAMEPLAY PASS {_checks} source-free plan/draft/intent/preflight/replay policy assertions; no imported models, player physics, native UI or GL");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("OFFLINE CHARACTER GAMEPLAY FAIL " + error); return 1; }
        finally
        {
            LauncherPrefs.Directory = oldDirectory;
            (LauncherPrefs.LastHunter, LauncherPrefs.LastColor, LauncherPrefs.Bots, LauncherPrefs.BotLevel,
                LauncherPrefs.LastKind, LauncherPrefs.PlayerName) = oldPrefs;
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        }
    }

    // Draft-only future combat coverage. All bot/difficulty values here are
    // raw inputs: Preview must refuse invalid values before its legacy clamps.
    private static void CombatPolicy(MenuSettings settings, RmlUiOfflineRequest captured, LaunchPlan combat, bool arsenal = false)
    {
        var expectedMode = arsenal ? OfflineCharacterMode.TrooperArsenalPractice : OfflineCharacterMode.TrooperScoutDroneCombatPractice;
        var expectedRule = arsenal ? CharacterGameplayRules.TrooperArsenalV1 : CharacterGameplayRules.TrooperDroneCombatV1;
        Check((int)expectedMode == (arsenal ? 4 : 3) && combat.OfflineCharacter == expectedMode && combat.IsPlaytest
            && combat.Kind == LaunchKind.Offline && combat.Hunter == Hunter.Weavel
            && combat.Mode == GameMode.Battle && combat.Bots == 1,
            "literal mode3/4 carries explicit private bot-practice identity rather than inferring it from media");
        Check(expectedRule.IsValidFor(Hunter.Weavel)
            && !expectedRule.IsValidFor(Hunter.Samus)
            && expectedRule != CharacterGameplayRules.TrooperDroneV1
            && expectedRule.UsesScoutDrone
            && CharacterGameplayRules.TrooperDroneV1.UsesScoutDrone
            && !CharacterGameplayRules.TrooperV1.UsesScoutDrone,
            "combat is distinct, finite and Weavel-only while retaining the shared drone owner");
        Check(!OfflineRematch.TryPlan(combat, "another-synthetic-arena", out _),
            "combat practice remains excluded from ordinary native rematches");
        for (int bots = 0; bots <= 7; bots++)
        for (int level = 0; level <= 3; level++)
        for (int suit = 0; suit <= 3; suit++)
        {
            var request = captured with { Bots = bots, BotLevel = level, Suit = suit };
            Check(request.ValidateForm(new[] { "synthetic-arena" }),
                "combat form admits exact supported raw bot/level/suit tuple " + bots + "/" + level + "/" + suit);
            var plan = OfflineLaunch.Preview(settings, request.RoomKey, request.Mode, request.Hunter,
                request.Suit, request.Bots, request.BotLevel, request.Character);
            Check(plan.Bots == bots && plan.BotLevel == level && plan.IsPlaytest
                && plan.OfflineCharacter == expectedMode
                && OfflineCharacterGameplay.TryAdmit(plan, out var reason) && reason == "",
                "raw supported tuple survives Preview unchanged and shared policy admits it");
        }
        foreach (var bad in new[] {
            captured with { Bots = -1 }, captured with { Bots = 8 },
            captured with { BotLevel = -1 }, captured with { BotLevel = 4 },
            captured with { Suit = -1 }, captured with { Suit = 4 },
            captured with { Hunter = Hunter.Samus }, captured with { Mode = GameMode.BattleTeams },
            captured with { Mode = GameMode.SinglePlayer }, captured with { Character = (OfflineCharacterMode)5 } })
        {
            Check(!bad.ValidateForm(new[] { "synthetic-arena" }), "malformed combat form refuses before launch");
            Check(Refused(() => OfflineLaunch.Preview(settings, bad.RoomKey, bad.Mode, bad.Hunter,
                bad.Suit, bad.Bots, bad.BotLevel, bad.Character)),
                "Preview independently refuses malformed combat raw inputs instead of normalizing them");
        }
        Check(!captured.ValidateForm(new[] { "other-arena" }), "combat requires its captured available room");
        var invalidPlans = new[] {
            combat with { Bots = -1 }, combat with { Bots = 8 },
            combat with { BotLevel = -1 }, combat with { BotLevel = 4 },
            combat with { IsPlaytest = false }, combat with { Hunter = Hunter.Samus },
            combat with { Mode = GameMode.BattleTeams }, combat with { Mode = GameMode.SinglePlayer },
            combat with { OfflineCharacter = (OfflineCharacterMode)5 },
            combat with { MatchRules = combat.MatchRules with { Mode = GameMode.Survival } },
            combat with { MatchRules = combat.MatchRules with { BalancedMode = true } },
            combat with { MatchRules = combat.MatchRules with { EnhancedHunters = true } },
            combat with { MatchRules = combat.MatchRules with { LowTier = true } },
            combat with { MatchRules = combat.MatchRules with { RiftlineM7Available = true } },
            combat with { MatchRules = combat.MatchRules with { MovementModel = MovementModel.Momentum } },
            combat with { MatchRules = combat.MatchRules with { MovementModel = MovementModel.Quake3 } },
            combat with { MatchRules = combat.MatchRules with { MovementModel = MovementModel.Traversal } },
            combat with { MatchRules = combat.MatchRules with { MovementModel = MovementModel.Parkour } }
        };
        foreach (var bad in invalidPlans.Concat(new[] { LaunchKind.None, LaunchKind.Online, LaunchKind.Host,
            LaunchKind.Adventure, LaunchKind.Demo, LaunchKind.AimTrainer }.Select(kind => combat with { Kind = kind })))
        {
            Check(!OfflineCharacterGameplay.TryAdmit(bad, out var reason) && !string.IsNullOrWhiteSpace(reason),
                "forged combat facts fail shared admission before room lookup");
            Check(!OfflineMatchPreflight.TryAdmit(bad, out var preflight) && preflight == reason,
                "central preflight preserves exact shared combat refusal");
        }
        if (arsenal)
        {
            foreach (var incompatible in new[] {
                combat with { MatchRules = combat.MatchRules with { InstaGib = true } },
                combat with { MatchRules = combat.MatchRules with { OneInTheChamber = true } },
                combat with { MatchRules = combat.MatchRules with { Fiesta = true } },
                combat with { MatchRules = combat.MatchRules with { NoImperialist = true } } })
            {
                Check(!OfflineCharacterGameplay.TryAdmit(incompatible, out var reason)
                    && reason == "Trooper arsenal practice requires standard weapon and ammo rules.",
                    "each Arsenal-only weapon/ammo modifier refuses the literal12/16/shared-UA policy");
                Check(!OfflineMatchPreflight.TryAdmit(incompatible, out var preflight) && preflight == reason,
                    "central preflight preserves the same Arsenal-only modifier refusal");
            }
        }
        // The existing private modes deliberately do not acquire combat bots.
        foreach (var mode in new[] { OfflineCharacterMode.TrooperWallClingPractice, OfflineCharacterMode.TrooperScoutDronePractice })
        for (int bots = 1; bots <= 7; bots++)
        {
            Check(!(captured with { Character = mode, Bots = bots }).ValidateForm(new[] { "synthetic-arena" }),
                "older mechanics forms still require zero opponents");
            Check(!OfflineCharacterGameplay.TryAdmit(combat with { OfflineCharacter = mode, Bots = bots }, out _),
                "older shared mechanics policy still requires zero opponents");
        }
    }

    private static void TransportAndPlaybackFence(params LaunchPlan[] practices)
    {
        // Predicate-control fixture only: no transport, replay reader or session
        // starts. Save and restore the existing metadata exactly in finally.
        var roleProperty = typeof(NetSession).GetProperty(nameof(NetSession.Role))!;
        var oldRole = NetSession.Role;
        var playback = DemoPlayback.Session;
        var activeProperty = playback.GetType().GetProperty("IsActive")!;
        bool oldActive = (bool)activeProperty.GetValue(playback)!;
        try
        {
            foreach (var role in new[] { NetRole.Host, NetRole.Client, NetRole.Server })
            {
                roleProperty.SetValue(null, role);
                foreach (var practice in practices)
                {
                    Check(!OfflineCharacterGameplay.TryAdmit(practice, out var reason)
                        && reason.Contains("fresh offline", StringComparison.OrdinalIgnoreCase),
                        "each private mode refuses active transport role " + role);
                    Check(!OfflineMatchPreflight.TryAdmit(practice, out var preflight) && preflight == reason,
                        "central preflight preserves transport refusal without starting a connection");
                }
            }
            roleProperty.SetValue(null, oldRole);
            activeProperty.SetValue(playback, true);
            Check(DemoPlayback.IsActive, "synthetic playback metadata reaches the actual static predicate");
            foreach (var practice in practices)
                Check(!OfflineCharacterGameplay.TryAdmit(practice, out var reason)
                    && reason.Contains("fresh offline", StringComparison.OrdinalIgnoreCase)
                    && !OfflineMatchPreflight.TryAdmit(practice, out var preflight) && preflight == reason,
                    "each private mode refuses active playback metadata through real policy/preflight");
        }
        finally
        {
            activeProperty.SetValue(playback, oldActive);
            roleProperty.SetValue(null, oldRole);
        }
    }

    private static void Intents()
    {
        var document = new RmlUiDocumentToken(17, 29);
        foreach (int choice in new[] { 13, 14, 15, 16 })
        {
            var wire = new RmlUiNativeIntent { Size = 40, Version = 1, Kind = 140, Argument = choice,
                Generation = 17, DocumentId = 29, Sequence = 3 };
            Check(RmlUiIntentRegistry.TryDecode(wire, out var decoded) && decoded.Kind == RmlUiIntentKind.OfflineChoice
                && decoded.Argument == choice && decoded.Document == document && decoded.Sequence == 3,
                "literal ABI offline kind140 retains valid choice" + choice);
            Check(RmlUiIntentRegistry.TryParseLegacy("offline:choice:" + choice, document, 4, out var legacy)
                && legacy.Argument == choice && legacy.Kind == RmlUiIntentKind.OfflineChoice,
                "authored action maps the same valid offline choice" + choice);
        }
        foreach (int choice in new[] { -1, 17 })
        {
            var wire = new RmlUiNativeIntent { Size = 40, Version = 1, Kind = 140, Argument = choice,
                Generation = 17, DocumentId = 29, Sequence = 3 };
            Check(!RmlUiIntentRegistry.TryDecode(wire, out _) && !RmlUiIntentRegistry.TryParseLegacy("offline:choice:" + choice, document, 4, out _),
                "both intent boundaries refuse outside choice16: " + choice);
        }
        var training = new RmlUiTrainingDraft(AimTrainerDefinition.Default, Hunter.Samus);
        var before = training.Current;
        Check(training.Choice(13) && training.Current.Difficulty != before.Difficulty, "training choice13 still changes difficulty");
        Check(training.Choice(14) && training.Current.Distance != before.Distance, "training choice14 still changes distance");
        Check(training.Choice(15) && training.Current.Scope != before.Scope, "training choice15 still changes scope");
        var kept = training.Current;
        Check(!training.Choice(16) && training.Current == kept, "mechanics choice16 is not silently repurposed as a training operation");
    }

    private static void ReplayFence(LaunchPlan practice)
    {
        var oldState = GameState.Current; var oldPlayers = PlayerEntity.LegacyRegistry; var oldRandom = Rng.Current;
        var pending = typeof(DemoRecorder).GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic)!;
        object? oldPending = pending.GetValue(null); var scenes = new List<Scene>();
        Scene Fresh()
        {
            var scene = new Scene(new Vector2i(32,32), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _=>{}, ()=>{}, initializeRuntime:false);
            scene.GameState.Mode = GameMode.Battle; scenes.Add(scene); return scene;
        }
        try
        {
            Headless.Enter();
            var native = Fresh();
            Check(ReplayCapture.CanStartLiveRecording(out _) && !CustomReplayBoundary.IsBlocked(native), "fresh native Scene remains recordable");
            pending.SetValue(null, true);
            Check(ReplayCapture.NeedsWorld, "synthetic pending recorder actually requests replay-world construction in the native Scene");
            pending.SetValue(null, false);
            native.MarkPrivateCharacterGameplayUnsupported();
            Check(!ReplayCapture.CanStartLiveRecording(out var reason) && reason == CustomReplayBoundary.Error,
                "actual recorder admission rejects the selected character gameplay Scene latch");
            pending.SetValue(null, true);
            Check(!ReplayCapture.NeedsWorld, "the same pending recorder cannot request a world from the unsupported character Scene");
            pending.SetValue(null, false); CustomReplayBoundary.ResetMatch();
            Check(!ReplayCapture.CanStartLiveRecording(out _) && !ReplayCapture.NeedsWorld,
                "ordinary match-boundary reset cannot erase the Scene's unsupported gameplay latch");
            var fresh = Fresh();
            Check(!fresh.PrivateCharacterGameplayUnsupported && ReplayCapture.CanStartLiveRecording(out _),
                "a genuinely fresh native Scene restores recording without inheriting the old character latch");
            pending.SetValue(null, true);
            Check(!OfflineCharacterGameplay.TryAdmit(practice, out var busy) && busy.Contains("recording", StringComparison.OrdinalIgnoreCase),
                "pending real-recorder admission refuses practice before any player bind");
            Check(!OfflineMatchPreflight.TryAdmit(practice, out var same) && same == busy,
                "central offline preflight shares the pending-writer refusal");
        }
        finally
        {
            pending.SetValue(null, oldPending);
            foreach (var scene in scenes) { scene.DoCleanup(); scene.UnloadGl(); }
            GameState.Current = oldState; PlayerEntity.LegacyRegistry = oldPlayers; Rng.Current = oldRandom;
        }
    }
}
