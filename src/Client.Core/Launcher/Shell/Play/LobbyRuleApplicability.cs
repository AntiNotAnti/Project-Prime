using System;
using System.Globalization;
using MphRead;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Describes which structured host-rule controls make sense for a mode.  The
/// server remains the authority; this projection only keeps the editor clear
/// and prevents sending fields that the selected mode cannot consume.
/// </summary>
internal readonly record struct LobbyRuleApplicability(
    bool TimeLimit,
    bool ScoreGoal,
    bool StartingLives,
    bool ObjectiveTimeGoal,
    bool DamageLevel,
    bool FriendlyFire,
    bool AffinityWeapons,
    bool EnhancedHunters,
    bool BalancedMode,
    bool PlayerRadar,
    bool OctolithReset)
{
    public static LobbyRuleApplicability For(MatchMode mode)
    {
        bool survival = mode is MatchMode.Survival or MatchMode.TeamSurvival;
        bool objective = mode is MatchMode.Defender or MatchMode.TeamDefender or MatchMode.PrimeHunter;
        bool octolith = mode is MatchMode.Capture or MatchMode.Bounty or MatchMode.TeamBounty;
        return new(
            TimeLimit: true,
            ScoreGoal: !survival && !objective,
            StartingLives: survival,
            ObjectiveTimeGoal: objective,
            DamageLevel: true,
            FriendlyFire: true,
            AffinityWeapons: true,
            EnhancedHunters: true,
            BalancedMode: true,
            PlayerRadar: true,
            OctolithReset: octolith);
    }

    public string GoalLabel => StartingLives ? "Lives" : ObjectiveTimeGoal ? "Objective time" : "Score limit";

    public static LobbyRulesOptions RulesFromLobby(LobbySnapshot lobby)
    {
        ArgumentNullException.ThrowIfNull(lobby);
        if (lobby.Rules is { } structured)
            return structured.ForMode(lobby.Mode);

        bool survival = lobby.Mode is MatchMode.Survival or MatchMode.TeamSurvival;
        LobbyRulesOptions legacy = new(
            TimeLimitSeconds: lobby.TimeLimitSeconds,
            ScoreGoal: survival ? null : lobby.PointGoal,
            StartingLives: survival ? lobby.PointGoal : null,
            ObjectiveTimeGoalSeconds: null);
        try { return legacy.Normalize(lobby.Mode); }
        catch (ArgumentException) { return legacy; }
    }
}

/// <summary>
/// User-facing labels for nullable lobby rules.  The values come from the
/// authoritative runtime defaults rather than a second table in the launcher.
/// </summary>
internal static class LobbyRuleDefaults
{
    private const string DisplayRoom = "launcher-default-preview";

    public static MatchRules For(MatchMode mode)
        => MatchRules.CreateDefault(mode, DisplayRoom);

    public static string Time(MatchMode mode, int? seconds)
        => seconds is { } value
            ? FormatDuration(value)
            : FormatDuration((int)For(mode).TimeLimit!.Value.TotalSeconds);

    public static string Score(MatchMode mode, int? value)
        => value?.ToString(CultureInfo.InvariantCulture)
            ?? For(mode).ScoreGoal.ToString(CultureInfo.InvariantCulture);

    public static string Lives(MatchMode mode, int? value)
        => value?.ToString(CultureInfo.InvariantCulture)
            ?? For(mode).StartingLives.ToString(CultureInfo.InvariantCulture);

    public static string ObjectiveTime(MatchMode mode, int? seconds)
        => seconds is { } value
            ? FormatDuration(value)
            : FormatDuration((int)For(mode).ObjectiveTimeGoal!.Value.TotalSeconds);

    public static string TimeWatermark(MatchMode mode)
        => FormatDuration((int)For(mode).TimeLimit!.Value.TotalSeconds);

    public static string NumberWatermark(MatchMode mode, bool lives)
        => (lives ? For(mode).StartingLives : For(mode).ScoreGoal)
            .ToString(CultureInfo.InvariantCulture);

    public static string ObjectiveWatermark(MatchMode mode)
        => FormatDuration((int)For(mode).ObjectiveTimeGoal!.Value.TotalSeconds);

    public static string Damage(MatchMode mode)
        => DamageName(For(mode).DamageLevel);

    public static string Bool(MatchMode mode, Func<MatchRules, bool> selector)
        => selector(For(mode)) ? "On" : "Off";

    public static string ResourceRadar(ResourceRadarPolicy? value)
        => value is { } policy ? ResourceRadar(policy) : "Disabled";

    public static string ResourceRadar(ResourceRadarPolicy value) => value switch
    {
        ResourceRadarPolicy.SpawnLocations => "Spawn locations",
        ResourceRadarPolicy.AvailableResources => "Available resources",
        ResourceRadarPolicy.AvailableWithRespawn => "Available + respawn",
        _ => "Disabled"
    };

    private static string DamageName(int value) => value switch
    {
        0 => "Low",
        2 => "High",
        _ => "Normal"
    };

    private static string FormatDuration(int seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds))
            .ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);
}
