using System;
using System.Globalization;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Core;

/// <summary>Exact local rule values, independent of controls and the simulation.</summary>
public readonly record struct RmlUiOfflineRules(string PointGoal, string TimeLimit,
    string TimeGoal, string DamageLevel, ushort Flags)
{
    // Retain the saved model while editing the existing rule controls.
    public string? MovementModel { get; init; } = "Native";
    public bool Enabled(int index) => index is >= 0 and < 13 && (Flags & (1 << index)) != 0;
    public static RmlUiOfflineRules Capture(MenuSettings s)
    {
        string[] values = { s.AutoReset, s.FriendlyFire, s.AffinityWeapons, s.ShadowFreeze,
            s.SpawnProtection, s.OneInTheChamber, s.Fiesta, s.InstaGib, s.LowTier,
            s.NoImperialist, s.BalancedMode, s.HunterRadar, s.ThirdPersonCamera };
        ushort flags = 0;
        for (int i = 0; i < values.Length; i++) if (values[i] == "on") flags |= (ushort)(1 << i);
        return new(s.PointGoal, s.TimeLimit, s.TimeGoal, s.DamageLevel, flags) { MovementModel = s.MovementModel };
    }

    public void CopyTo(MenuSettings s)
    {
        ushort flags = Flags;
        string On(int index) => (flags & (1 << index)) != 0 ? "on" : "off";
        s.PointGoal = PointGoal; s.TimeLimit = TimeLimit; s.TimeGoal = TimeGoal; s.DamageLevel = DamageLevel;
        s.AutoReset = On(0); s.FriendlyFire = On(1); s.AffinityWeapons = On(2);
        s.ShadowFreeze = On(3); s.SpawnProtection = On(4); s.OneInTheChamber = On(5);
        s.Fiesta = On(6); s.InstaGib = On(7); s.LowTier = On(8); s.NoImperialist = On(9);
        s.BalancedMode = On(10); s.HunterRadar = On(11); s.ThirdPersonCamera = On(12);
        s.MovementModel = MovementModel ?? "Native";
    }

    // The caller holds the existing settings-write lease. Roll back the exact
    // in-memory rule subset if persistence throws; unrelated settings survive.
    public static bool TryCommit(MenuSettings settings, RmlUiOfflineRules expected,
        RmlUiOfflineRules next, Action<MenuSettings> persist, out string error)
    {
        error = "";
        if (Capture(settings) != expected) { error = "RULES CHANGED // CLOSE AND REOPEN THIS DRAFT"; return false; }
        next.CopyTo(settings);
        try { persist(settings); return true; }
        catch { expected.CopyTo(settings); error = "RULES COULD NOT BE SAVED // RETRY OR CANCEL"; return false; }
    }
}

public sealed class RmlUiOfflineRuleEditor
{
    public static readonly string[] Labels = { "OCTOLITH AUTO RESET", "FRIENDLY FIRE", "AFFINITY WEAPONS",
        "SHADOW FREEZE", "SPAWN PROTECTION (3s)", "ONE IN THE CHAMBER", "FIESTA", "INSTA-GIB",
        "LOW TIER", "NO IMP", "BALANCED MODE", "HUNTER RADAR", "THIRD PERSON CAMERA", "MOVEMENT" };
    public RmlUiOfflineRules Original { get; }
    public RmlUiOfflineRules Draft { get; private set; }
    public GameMode Mode { get; }
    public string Error { get; private set; } = "";
    public bool PointGoalVisible => !MatchGoalRules.UsesTimeTarget(Mode) && Mode is not (GameMode.GunGame or GameMode.OneInTheChamber);
    public bool TimeLimitVisible => Mode != GameMode.OneInTheChamber;
    public bool TimeGoalVisible => MatchGoalRules.UsesTimeTarget(Mode);
    public bool FireVisible => GameState.IsTeamMode(Mode);
    public bool AutoResetVisible => MatchModifierRules.UsesOctolith(Mode);
    public bool MovementVisible => Mode != GameMode.SinglePlayer;
    public string MovementLabel => Movement.MovementModelRules.TryParse(Draft.MovementModel, out var model)
        ? Movement.MovementModelRules.DisplayName(model) : "Unsupported";

    public RmlUiOfflineRuleEditor(GameMode mode, RmlUiOfflineRules original)
    {
        Mode = mode; Original = original;
        // One in the Chamber is selected as a mode, never a stale modifier.
        Draft = original with { Flags = (ushort)(original.Flags & ~(1 << 5)) };
    }

    public bool Toggle(int index)
    {
        if (index == 13)
        {
            if (!MovementVisible) return false;
            // A user explicitly activating an unsupported saved choice selects
            // Classic. Merely opening/applying an unknown value still fails validation.
            bool known = Movement.MovementModelRules.TryParse(Draft.MovementModel, out var model);
            Draft = Draft with { MovementModel = known ? Movement.MovementModelRules.Next(model).ToString() : "Native" };
            Error = ""; return true;
        }
        if (index is < 0 or >= 13 or 5 || index == 0 && !AutoResetVisible || index == 1 && !FireVisible) return false;
        ushort flags = (ushort)(Draft.Flags ^ (1 << index));
        // Match the legacy mutually exclusive loadout controls.
        if ((flags & (1 << index)) != 0 && index is 7 or 9)
            flags &= (ushort)~(1 << (index == 7 ? 9 : 7));
        Draft = Draft with { Flags = flags }; Error = ""; return true;
    }

    public bool NextDamage()
    {
        Draft = Draft with { DamageLevel = Draft.DamageLevel switch { "low" => "medium", "medium" => "high", _ => "low" } };
        Error = ""; return true;
    }

    public bool Validate(string points, string limit, string goal)
    {
        bool Duration(string value) => TimeSpan.TryParseExact(value,
            new[] { @"m\:ss", @"mm\:ss" }, CultureInfo.InvariantCulture, out _);
        if (!Int32.TryParse(points, NumberStyles.None, CultureInfo.InvariantCulture, out int amount)
            || amount is < 0 or > 999 || !Duration(limit) || !Duration(goal))
        { Error = "USE A POINT GOAL FROM 0–999 AND DURATIONS AS m:ss"; return false; }
        if (!Movement.MovementModelRules.TryParse(Draft.MovementModel, out var movementModel))
        { Error = "CHOOSE CLASSIC, MOMENTUM, OR QUAKE III MOVEMENT"; return false; }
        var rules = new MatchDefinition { Mode = Mode, MovementModel = Mode == GameMode.SinglePlayer
                ? Movement.MovementModel.Native : movementModel, InstaGib = Draft.Enabled(7),
            NoImperialist = Draft.Enabled(9), Fiesta = Draft.Enabled(6), BalancedMode = Draft.Enabled(10) };
        if (!MatchModifierRules.Validate(rules, out string reason)) { Error = reason; return false; }
        Draft = Draft with { PointGoal = points, TimeLimit = limit, TimeGoal = goal };
        Error = ""; return true;
    }

    public void Failed(string error) => Error = error;
}
