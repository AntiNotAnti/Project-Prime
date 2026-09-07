using System;
using MphRead.Formats;

namespace MphRead
{
    public enum MatchMode : byte
    {
        Battle,
        TeamBattle,
        Survival,
        TeamSurvival,
        Capture,
        Bounty,
        TeamBounty,
        Nodes,
        TeamNodes,
        Defender,
        TeamDefender,
        PrimeHunter,
    }

    public static class MatchModeConversions
    {
        public static MatchMode ToMatchMode(this GameMode mode) => mode switch
        {
            GameMode.Battle => MatchMode.Battle,
            GameMode.BattleTeams => MatchMode.TeamBattle,
            GameMode.Survival => MatchMode.Survival,
            GameMode.SurvivalTeams => MatchMode.TeamSurvival,
            GameMode.Capture => MatchMode.Capture,
            GameMode.Bounty => MatchMode.Bounty,
            GameMode.BountyTeams => MatchMode.TeamBounty,
            GameMode.Nodes => MatchMode.Nodes,
            GameMode.NodesTeams => MatchMode.TeamNodes,
            GameMode.Defender => MatchMode.Defender,
            GameMode.DefenderTeams => MatchMode.TeamDefender,
            GameMode.PrimeHunter => MatchMode.PrimeHunter,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "A multiplayer mode is required.")
        };

        public static GameMode ToLegacyMode(this MatchMode mode) => mode switch
        {
            MatchMode.Battle => GameMode.Battle,
            MatchMode.TeamBattle => GameMode.BattleTeams,
            MatchMode.Survival => GameMode.Survival,
            MatchMode.TeamSurvival => GameMode.SurvivalTeams,
            MatchMode.Capture => GameMode.Capture,
            MatchMode.Bounty => GameMode.Bounty,
            MatchMode.TeamBounty => GameMode.BountyTeams,
            MatchMode.Nodes => GameMode.Nodes,
            MatchMode.TeamNodes => GameMode.NodesTeams,
            MatchMode.Defender => GameMode.Defender,
            MatchMode.TeamDefender => GameMode.DefenderTeams,
            MatchMode.PrimeHunter => GameMode.PrimeHunter,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown multiplayer mode.")
        };

        public static bool IsTeamMode(this MatchMode mode)
        {
            _ = mode.ToLegacyMode();
            return mode is MatchMode.TeamBattle or MatchMode.TeamSurvival or MatchMode.Capture
                or MatchMode.TeamBounty or MatchMode.TeamNodes or MatchMode.TeamDefender;
        }
    }
}
