using System;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Player-facing game type. The legacy team-specific GameMode values remain
    /// the runtime/wire representation so existing replays, rotations and
    /// protocol values keep their numeric meaning.
    /// </summary>
    public readonly record struct MatchTypeDefinition(
        string Label, GameMode Free, GameMode Team, bool TeamOnly = false, bool FfaOnly = false)
    {
        public GameMode Resolve(MatchFormat format)
        {
            if (FfaOnly) return Free;
            if (TeamOnly) return Team;
            return format == MatchFormat.FreeForAll ? Free : Team;
        }
    }

    public readonly record struct MatchupDefinition(string Label, MatchFormat Format);

    public static class MatchTypeCatalog
    {
        public static readonly MatchTypeDefinition[] GameTypes =
        {
            new("Battle", GameMode.Battle, GameMode.BattleTeams),
            new("Survival", GameMode.Survival, GameMode.SurvivalTeams),
            new("Bounty", GameMode.Bounty, GameMode.BountyTeams),
            new("Defender", GameMode.Defender, GameMode.DefenderTeams),
            new("Nodes", GameMode.Nodes, GameMode.NodesTeams),
            new("Capture", GameMode.Capture, GameMode.Capture, TeamOnly: true),
            new("Hardpoint", GameMode.Hardpoint, GameMode.HardpointTeams),
            new("Gun Game", GameMode.GunGame, GameMode.GunGame, FfaOnly: true),
            new("One in the Chamber", GameMode.OneInTheChamber, GameMode.OneInTheChamber, FfaOnly: true),
            new("Kill Confirmed", GameMode.KillConfirmed, GameMode.KillConfirmedTeams),
            new("Headhunter", GameMode.Headhunter, GameMode.Headhunter, FfaOnly: true),
            new("Relic", GameMode.Relic, GameMode.Relic, FfaOnly: true),
            new("Prime Hunter", GameMode.PrimeHunter, GameMode.PrimeHunter, FfaOnly: true)
        };

        public static readonly MatchupDefinition[] Matchups =
        {
            new("FFA", MatchFormat.FreeForAll),
            new("Teams", MatchFormat.Auto),
            new("1v1", MatchFormat.OneVsOne),
            new("2v2", MatchFormat.TwoVsTwo),
            new("3v3", MatchFormat.ThreeVsThree),
            new("4v4", MatchFormat.FourVsFour),
            new("2v2v2v2", MatchFormat.TwoVsTwoVsTwoVsTwo),
            new("Custom", MatchFormat.Custom)
        };

        public static readonly MatchupDefinition[] BasicMatchups =
        {
            new("FFA", MatchFormat.FreeForAll),
            new("Teams", MatchFormat.Auto)
        };

        public static int BaseModeIndex(GameMode mode)
        {
            if (mode == GameMode.InstaGib) mode = GameMode.Battle;
            int index = Array.FindIndex(GameTypes, type => type.Free == mode || type.Team == mode);
            return index < 0 ? 0 : index;
        }

        public static int MatchupIndex(MatchFormat format)
        {
            int index = Array.FindIndex(Matchups, matchup => matchup.Format == format);
            return index < 0 ? 0 : index;
        }

        public static int BasicMatchupIndex(MatchFormat format) =>
            format == MatchFormat.FreeForAll ? 0 : 1;

        public static MatchFormat FormatForMode(GameMode mode) =>
            IsInternalTeamMode(mode) ? MatchFormat.Auto : MatchFormat.FreeForAll;

        public static MatchFormat NormalizeFormat(MatchTypeDefinition type, MatchFormat format)
        {
            if (type.FfaOnly) return MatchFormat.FreeForAll;
            if (type.TeamOnly && (format == MatchFormat.FreeForAll
                || format == MatchFormat.TwoVsTwoVsTwoVsTwo))
                return MatchFormat.Auto;
            return format;
        }

        public static bool IsInternalTeamMode(GameMode mode) =>
            mode is GameMode.BattleTeams or GameMode.SurvivalTeams or GameMode.Capture
                or GameMode.BountyTeams or GameMode.NodesTeams or GameMode.DefenderTeams
                or GameMode.HardpointTeams or GameMode.KillConfirmedTeams;

        public static string BaseLabel(GameMode mode)
        {
            if (mode == GameMode.InstaGib) mode = GameMode.Battle;
            int index = Array.FindIndex(GameTypes, type => type.Free == mode || type.Team == mode);
            if (index >= 0) return GameTypes[index].Label;

            string name = mode.ToString();
            var builder = new System.Text.StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && Char.IsUpper(name[i])) builder.Append(' ');
                builder.Append(name[i]);
            }
            return builder.ToString();
        }

        public static string MatchupLabel(GameMode mode, MatchFormat format)
        {
            if (format == MatchFormat.Auto)
                return IsInternalTeamMode(mode) ? "Teams" : "FFA";
            int index = Array.FindIndex(Matchups, matchup => matchup.Format == format);
            return index >= 0 ? Matchups[index].Label : format.ToString();
        }
    }
}
