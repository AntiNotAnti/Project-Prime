using System.Text.Json;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Training;

#if MPHREAD_RMLUI_POC
if (args.Length == 3 && args[0] == "--native")
{
    NativeOfflineCheck.Run(args[1], args[2]);
    return;
}
#endif
if (args.Length != 0) throw new ArgumentException("Usage: offline-controller-check [--native <library> <packaged-assets>], native mode requires -p:MphReadRmlUi=true.");
int passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
LauncherPrefs.LastHunter = Hunter.Samus; LauncherPrefs.LastColor = 2; LauncherPrefs.Bots = 3; LauncherPrefs.BotLevel = 2;
LauncherPrefs.Training = AimTrainerDefinition.Default;
var settings = new MenuSettings { RoomKey = "first", PointGoal = "8", TimeLimit = "4:30", EnhancedHunters = "off" };
var backend = new FakeBackend();
using var c = new OfflineController(settings, new[] { "first", "second", "bad", "first" }, backend);
var initial = c.Snapshot();
Check(initial.ArenaKey == "first" && initial.Arenas.Length == 3, "authoritative arena list copied and deduplicated");
Check(initial.Bots == 3 && initial.BotDifficulty == 2 && initial.Suit == 2 && initial.Hunter == Hunter.Samus, "saved deployment preferences retained");
Check(initial.Rules.PointGoal == "8" && initial.Rules.TimeLimit == "4:30", "persisted rules retained");
Check(c.Snapshot().Revision == initial.Revision && c.Snapshot().Lifetime == initial.Lifetime, "unchanged snapshots retain structural revision and lifetime");
Check(!c.SelectArena("unknown").Accepted && !c.SelectArena("bad").Accepted && c.Snapshot().ArenaKey == "first", "unavailable and incompatible arena selection rejected");
Check(!c.Cycle((OfflineChoice)99).Accepted, "unknown choice rejected");
for (int i = 0; i < MatchTypeCatalog.GameTypes.Length; i++)
{
    var snapshot = c.Snapshot(); var type = MatchTypeCatalog.GameTypes[snapshot.GameType];
    Check(snapshot.Mode == type.Resolve(MatchTypeCatalog.BasicMatchups[snapshot.Matchup].Format), "catalog mode " + type.Label);
    c.Cycle(OfflineChoice.Matchup);
    snapshot = c.Snapshot();
    if (type.TeamOnly || type.FfaOnly) Check(!snapshot.MatchupEditable && (type.TeamOnly ? snapshot.Matchup == 1 : snapshot.Matchup == 0), "forced matchup " + type.Label);
    c.Cycle(OfflineChoice.GameType);
}
backend.Available = false;
Check(!c.RequestMatch().Accepted && !c.RequestTraining().Accepted && backend.MatchCreates == 0 && backend.TrainingCreates == 0, "active session and missing content gate precedes launch persistence");
backend.Available = true;
c.OpenRules(); c.SetRuleFields("1000", "7:00", "1:30");
Check(!c.ApplyRules().Accepted && backend.Commits == 0, "score bounds block persistence");
c.SetRuleFields("20", "bad", "1:30");
Check(!c.ApplyRules().Accepted && backend.Commits == 0, "duration grammar blocks persistence");
c.SetRuleFields("20", "7:00", "1:30"); c.ToggleRule(OfflineRuleToggle.BalancedMode); c.ToggleRule(OfflineRuleToggle.EnhancedHunters);
Check(!c.ApplyRules().Accepted && backend.Commits == 0, "authoritative modifier incompatibility blocks persistence");
c.CancelRules();
settings.AffinityWeapons = "legacy-unknown"; settings.OneInTheChamber = "on";
string before = JsonSerializer.Serialize(settings);
c.OpenRules(); c.SetRuleFields("42", "9:00", "2:00"); c.ToggleRule(OfflineRuleToggle.LowTier); c.ToggleRule(OfflineRuleToggle.EnhancedHunters);
backend.FailCommit = true;
Check(!c.ApplyRules().Accepted && JsonSerializer.Serialize(settings) == before && backend.Applies == 0, "failed settings save restores every exact persisted value");
Check(c.Snapshot().RulesOpen && c.Snapshot().Error.Contains("Could not save rules"), "failed save retains retryable draft and error");
backend.FailCommit = false;
Check(c.ApplyRules().Accepted && backend.Applies == 1 && settings.PointGoal == "42" && settings.OneInTheChamber == "off", "successful rules save commits then applies runtime and clears legacy chamber modifier");
Check(c.Snapshot().Hunter == Hunter.Kanden && !c.Snapshot().RulesOpen, "low tier selection normalized after successful save");
c.OpenRules(); c.ToggleRule(OfflineRuleToggle.NoImperialist); c.ToggleRule(OfflineRuleToggle.InstaGib);
Check(c.Snapshot().Rules.InstaGib && !c.Snapshot().Rules.NoImperialist, "Insta-Gib clears No Imp");
c.ToggleRule(OfflineRuleToggle.NoImperialist);
Check(!c.Snapshot().Rules.InstaGib && c.Snapshot().Rules.NoImperialist, "No Imp clears Insta-Gib");
c.CancelRules();
Check(c.RequestMatch().Accepted && backend.MatchCreates == 1, "validated local match creates authoritative plan once");
Check(!c.RequestMatch().Accepted && !c.RequestTraining().Accepted && backend.MatchCreates == 1, "duplicate pending launch rejected");
Check(c.TryTakeLaunch(out var plan) && plan.Kind == LaunchKind.Offline && plan.MatchRules.EnhancedHunters && plan.MatchRules.LowTier && plan.Bots == 3, "all exposed modifiers and bot preferences carried in launch plan");
Check(!c.TryTakeLaunch(out _), "launch handoff consumed once");
Check(!c.RequestMatch().Accepted && backend.MatchCreates == 1, "launch issued latch survives plan handoff");
c.ReportLaunchFailure("test retry");
for (int i = 0; i < (int)AimTrainerDrill.TimedFlick; i++) c.Cycle(OfflineChoice.Drill);
c.Cycle(OfflineChoice.Targets);
Check(c.Snapshot().Training.Drill == AimTrainerDrill.TimedFlick && c.Snapshot().Training.TargetCount == 1, "Timed Flick target count enforced");
c.Cycle(OfflineChoice.Drill);
Check(c.Snapshot().Training.TargetCount == 5 && c.Snapshot().Training.Movement == AimTrainerMovement.Static, "Multi Target Flick defaults normalized");
c.Cycle(OfflineChoice.Targets);
Check(c.Snapshot().Training.TargetCount == 4, "Multi Target Flick cycles only valid target counts");
c.OpenTrainingOptions();
Check(!c.RequestTraining().Accepted, "open training sheet blocks premature launch");
c.ToggleTraining(OfflineTrainingToggle.FixedSeed); c.CloseTrainingOptions();
Check(c.RequestTraining().Accepted && c.TryTakeLaunch(out var fixedPlan) && fixedPlan.Training!.Value.Seed == 1 && backend.NewSeeds == 0, "fixed seed survives launch");
c.ReportLaunchFailure("test retry");
c.ToggleTraining(OfflineTrainingToggle.FixedSeed);
Check(c.RequestTraining().Accepted && c.TryTakeLaunch(out var randomPlan) && randomPlan.Training!.Value.Seed == 123 && backend.NewSeeds == 1, "nonfixed training generates authoritative fresh seed");
Check(!c.ToggleTraining((OfflineTrainingToggle)99).Accepted, "unknown training flag rejected");
c.ReportLaunchFailure("device lost");
Check(c.Snapshot().Error == "device lost", "engine launch failure returned to setup");
bool ownerRejected = false;
Task.Run(() => { try { c.Snapshot(); } catch (InvalidOperationException) { ownerRejected = true; } }).GetAwaiter().GetResult();
Check(ownerRejected, "worker thread cannot access offline controller");
c.Dispose();
bool disposedRejected = false; try { c.Snapshot(); } catch (ObjectDisposedException) { disposedRejected = true; }
Check(disposedRejected, "retired lifetime rejects commands and snapshots");
Console.WriteLine($"Offline controller: {passed} contracts passed.");

sealed class FakeBackend : IOfflineBackend
{
    public bool Available = true, FailCommit;
    public int Commits, Applies, MatchCreates, TrainingCreates, NewSeeds;
    public bool CanLaunch(out string reason) { reason = Available ? "" : "Leave active session or set up game files."; return Available; }
    public bool ValidateArena(string room, GameMode mode, int participants, out string reason) { reason = room == "bad" ? "Arena does not support this mode." : ""; return reason.Length == 0; }
    public bool ValidateRules(MatchDefinition match, out string reason) => MphRead.Mods.Multiplayer.MatchModifierRules.Validate(match, out reason);
    public void CommitSettings(MenuSettings settings) { Commits++; if (FailCommit) throw new IOException("simulated atomic write failure"); }
    public void ApplySettings(MenuSettings settings) => Applies++;
    public LaunchPlan CreateMatch(MenuSettings settings, string room, GameMode mode, Hunter hunter, int suit, int bots, int level)
    { MatchCreates++; return new() { Kind = LaunchKind.Offline, RoomKey = room, Mode = mode, Hunter = hunter, Bots = bots, BotLevel = level }; }
    public LaunchPlan CreateTraining(AimTrainerDefinition training, Hunter hunter, int suit)
    { TrainingCreates++; return new() { Kind = LaunchKind.AimTrainer, RoomKey = AimTrainerLaunch.Room, Training = training, Hunter = hunter, Bots = training.TargetCount }; }
    public uint NewTrainingSeed() { NewSeeds++; return 123; }
}
