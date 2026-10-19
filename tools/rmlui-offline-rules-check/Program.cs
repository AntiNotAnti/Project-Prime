using System.Globalization;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Launcher.RmlUi.Core;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

static void Check(bool condition, string label)
{
    if (!condition) throw new InvalidOperationException(label);
    Console.WriteLine("RML OFFLINE RULES PASS " + label);
}

static RmlUiOfflineRules Rules(ushort flags = 0)
    => new("7", "7:00", "1:30", "medium", flags);

var initial = Rules((1 << 0) | (1 << 1) | (1 << 5));
var battle = new RmlUiOfflineRuleEditor(GameMode.Battle, initial);
Check(battle.Original == initial && !battle.Draft.Enabled(5)
    && battle.Draft.Enabled(0) && battle.Draft.Enabled(1),
    "opening clears retired chamber modifier in draft while preserving original and hidden options");
foreach (int index in new[] { int.MinValue, -1, 12, int.MaxValue, 5, 0, 1 })
{
    var before = battle.Draft;
    Check(!battle.Toggle(index) && battle.Draft == before,
        "out-of-range, retired and hidden battle toggles cannot mutate draft: " + index);
}
Check(!initial.Enabled(-1) && !initial.Enabled(12) && !initial.Enabled(int.MaxValue),
    "flag lookup rejects invalid indices without shifting into another option");
for (int index = 2; index < 12; index++)
{
    if (index == 5) continue;
    var editor = new RmlUiOfflineRuleEditor(GameMode.Battle, Rules());
    Check(editor.Toggle(index) && editor.Draft.Enabled(index)
        && editor.Toggle(index) && !editor.Draft.Enabled(index),
        "available battle option toggles on and off: " + index);
}
var exclusive = new RmlUiOfflineRuleEditor(GameMode.Battle, Rules());
Check(exclusive.Toggle(7) && exclusive.Draft.Enabled(7) && !exclusive.Draft.Enabled(9)
    && exclusive.Toggle(9) && exclusive.Draft.Enabled(9) && !exclusive.Draft.Enabled(7)
    && exclusive.Toggle(7) && exclusive.Draft.Enabled(7) && !exclusive.Draft.Enabled(9),
    "Insta-Gib and No Imp replace each other in either selection order");
var damage = new RmlUiOfflineRuleEditor(GameMode.Battle, Rules() with { DamageLevel = "low" });
Check(damage.NextDamage() && damage.Draft.DamageLevel == "medium"
    && damage.NextDamage() && damage.Draft.DamageLevel == "high"
    && damage.NextDamage() && damage.Draft.DamageLevel == "low",
    "damage cycles low, medium, high and wraps");
var unknownDamage = new RmlUiOfflineRuleEditor(GameMode.Battle, Rules() with { DamageLevel = "invalid" });
Check(unknownDamage.NextDamage() && unknownDamage.Draft.DamageLevel == "low",
    "unknown legacy damage value recovers to low");

foreach (var expected in new[]
{
    (Mode: GameMode.Battle, Points: true, Limit: true, Goal: false, Fire: false, Reset: false),
    (Mode: GameMode.BattleTeams, Points: true, Limit: true, Goal: false, Fire: true, Reset: false),
    (Mode: GameMode.Capture, Points: true, Limit: true, Goal: false, Fire: true, Reset: true),
    (Mode: GameMode.Bounty, Points: true, Limit: true, Goal: false, Fire: false, Reset: true),
    (Mode: GameMode.BountyTeams, Points: true, Limit: true, Goal: false, Fire: true, Reset: true),
    (Mode: GameMode.Relic, Points: false, Limit: true, Goal: true, Fire: false, Reset: true),
    (Mode: GameMode.DefenderTeams, Points: false, Limit: true, Goal: true, Fire: true, Reset: false),
    (Mode: GameMode.PrimeHunter, Points: false, Limit: true, Goal: true, Fire: false, Reset: false),
    (Mode: GameMode.HardpointTeams, Points: false, Limit: true, Goal: true, Fire: true, Reset: false),
    (Mode: GameMode.GunGame, Points: false, Limit: true, Goal: false, Fire: false, Reset: false),
    (Mode: GameMode.OneInTheChamber, Points: false, Limit: false, Goal: false, Fire: false, Reset: false)
})
{
    var editor = new RmlUiOfflineRuleEditor(expected.Mode, Rules());
    Check(editor.PointGoalVisible == expected.Points && editor.TimeLimitVisible == expected.Limit
        && editor.TimeGoalVisible == expected.Goal && editor.FireVisible == expected.Fire
        && editor.AutoResetVisible == expected.Reset,
        "objective, team-fire and Octolith controls match mode: " + expected.Mode);
    Check(editor.Toggle(0) == expected.Reset && editor.Toggle(1) == expected.Fire,
        "only visible mode-specific toggles admit changes: " + expected.Mode);
}

var numeric = new RmlUiOfflineRuleEditor(GameMode.Battle, Rules());
var previousCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    Check(numeric.Validate("0", "0:00", "00:00")
        && numeric.Validate("999", "59:59", "09:05")
        && numeric.Draft.PointGoal == "999" && numeric.Draft.TimeGoal == "09:05",
        "invariant points and duration limits include zero and preserve accepted text");
    foreach (string points in new[] { "-1", "+1", "1000", "1,000", "1.5", " 7", "7 ", "", "999999999999" })
    {
        var before = numeric.Draft;
        Check(!numeric.Validate(points, "7:00", "1:30") && numeric.Draft == before && numeric.Error.Length > 0,
            "invalid point goal fails without changing accepted draft: " + points);
    }
    foreach (string duration in new[] { "", "1:2", "1:60", "60:00", "99:59", "100:00", "-1:00", "1,30", "1:30:00", " 1:30" })
    {
        var before = numeric.Draft;
        Check(!numeric.Validate("7", duration, "1:30") && numeric.Draft == before
            && !numeric.Validate("7", "7:00", duration) && numeric.Draft == before,
            "malformed or out-of-component-range duration fails in either field: " + duration);
    }
}
finally { CultureInfo.CurrentCulture = previousCulture; }
Check(numeric.Validate("7", "7:00", "1:30") && numeric.Error == "",
    "valid correction clears numeric validation error");

foreach (var incompatible in new[]
{
    (Mode: GameMode.Battle, Flags: (ushort)((1 << 6) | (1 << 7))),
    (Mode: GameMode.Battle, Flags: (ushort)((1 << 6) | (1 << 10))),
    (Mode: GameMode.Battle, Flags: (ushort)((1 << 7) | (1 << 10))),
    (Mode: GameMode.GunGame, Flags: (ushort)(1 << 9)),
    (Mode: GameMode.GunGame, Flags: (ushort)(1 << 10)),
    (Mode: GameMode.OneInTheChamber, Flags: (ushort)(1 << 6))
})
{
    var editor = new RmlUiOfflineRuleEditor(incompatible.Mode, Rules(incompatible.Flags));
    var realDefinition = new MatchDefinition { Mode = incompatible.Mode,
        Fiesta = (incompatible.Flags & (1 << 6)) != 0, InstaGib = (incompatible.Flags & (1 << 7)) != 0,
        NoImperialist = (incompatible.Flags & (1 << 9)) != 0, BalancedMode = (incompatible.Flags & (1 << 10)) != 0 };
    Check(!MatchModifierRules.Validate(realDefinition, out string reason)
        && !editor.Validate("7", "7:00", "1:30") && editor.Error == reason
        && editor.Draft == Rules(incompatible.Flags),
        "editor propagates actual game modifier incompatibility without committing text: " + incompatible.Mode + "/" + incompatible.Flags);
}
var corrected = new RmlUiOfflineRuleEditor(GameMode.Battle, Rules((1 << 6) | (1 << 10)));
Check(!corrected.Validate("7", "7:00", "1:30") && corrected.Toggle(10)
    && corrected.Error == "" && corrected.Validate("7", "7:00", "1:30"),
    "removing a conflicting modifier clears error and restores valid rules");

var settings = new MenuSettings { RoomKey = "TEMP FIXTURE MAP", Language = "French", SfxVolume = "0.42" };
var roundtrip = new RmlUiOfflineRules("123", "15:25", "02:45", "high", 0x0AAA);
roundtrip.CopyTo(settings);
Check(RmlUiOfflineRules.Capture(settings) == roundtrip && settings.OneInTheChamber == "on"
    && settings.RoomKey == "TEMP FIXTURE MAP" && settings.Language == "French" && settings.SfxVolume == "0.42",
    "capture/copy roundtrips every rule field including legacy bit while preserving unrelated settings");
for (int bit = 0; bit < 12; bit++)
{
    var single = Rules((ushort)(1 << bit));
    single.CopyTo(settings);
    Check(RmlUiOfflineRules.Capture(settings) == single,
        "capture/copy preserves independent toggle position: " + bit);
}

var expectedRules = RmlUiOfflineRules.Capture(settings);
var nextRules = Rules((1 << 2) | (1 << 8)) with { PointGoal = "42", DamageLevel = "low" };
int writes = 0;
settings.PointGoal = "55";
var conflict = RmlUiOfflineRules.Capture(settings);
Check(!RmlUiOfflineRules.TryCommit(settings, expectedRules, nextRules, _ => writes++, out string conflictError)
    && writes == 0 && conflictError.Contains("CHANGED") && RmlUiOfflineRules.Capture(settings) == conflict,
    "expected-value conflict invokes no persistence and leaves external change intact");
expectedRules.CopyTo(settings);
Check(!RmlUiOfflineRules.TryCommit(settings, expectedRules, nextRules, draft =>
    {
        writes++;
        Check(RmlUiOfflineRules.Capture(draft) == nextRules, "persistence delegate receives proposed rule subset");
        draft.SfxVolume = "0.99";
        throw new IOException("Synthetic persistence failure");
    }, out string rollbackError)
    && writes == 1 && rollbackError.Contains("COULD NOT BE SAVED")
    && RmlUiOfflineRules.Capture(settings) == expectedRules && settings.SfxVolume == "0.99"
    && settings.RoomKey == "TEMP FIXTURE MAP" && settings.Language == "French",
    "throwing persistence restores exact rules and preserves unrelated setting changes");

string temporaryDirectory = Path.Combine(Path.GetTempPath(), "rmlui-offline-rules-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporaryDirectory);
try
{
    string path = Path.Combine(temporaryDirectory, "fixture-settings.json");
    Check(RmlUiOfflineRules.TryCommit(settings, expectedRules, nextRules, draft =>
        {
            writes++;
            File.WriteAllText(path, JsonSerializer.Serialize(draft));
        }, out string successError) && successError == "" && writes == 2,
        "successful commit invokes the durable delegate once");
    MenuSettings persisted = JsonSerializer.Deserialize<MenuSettings>(File.ReadAllText(path))!;
    Check(RmlUiOfflineRules.Capture(settings) == nextRules && RmlUiOfflineRules.Capture(persisted) == nextRules
        && persisted.RoomKey == "TEMP FIXTURE MAP" && persisted.Language == "French" && persisted.SfxVolume == "0.99",
        "successful commit survives temp-file readback with unrelated settings preserved");
}
finally { Directory.Delete(temporaryDirectory, recursive: true); }
Console.WriteLine("RML offline rule editor and isolated persistence acceptance complete.");
