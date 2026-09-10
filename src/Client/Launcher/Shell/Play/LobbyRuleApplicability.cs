using System;
using FruityPrime.Server.Shared;

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
