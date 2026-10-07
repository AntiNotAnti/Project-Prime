using System;
using System.Globalization;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher
{
    /// <summary>
    /// Renderer-neutral lobby rule input/format contract. Avalonia and RmlUi
    /// must never disagree about minutes, survival lives or hold-time goals.
    /// </summary>
    public static class LobbyRuleEditValues
    {
        public static string DurationText(ushort seconds)
            => $"{seconds / 60}:{seconds % 60:00}";

        public static string GoalText(GameMode mode, ushort goal)
            => MatchGoalRules.UsesLives(mode)
                ? ((int)goal + 1).ToString(CultureInfo.InvariantCulture)
                : MatchGoalRules.UsesTimeTarget(mode)
                    ? DurationText(goal)
                    : goal.ToString(CultureInfo.InvariantCulture);

        public static string GoalLabel(GameMode mode) => mode switch
        {
            GameMode.Survival or GameMode.SurvivalTeams or GameMode.OneInTheChamber => "LIVES",
            GameMode.Bounty or GameMode.BountyTeams => "BOUNTY GOAL",
            GameMode.Capture => "CAPTURES",
            GameMode.Defender or GameMode.DefenderTeams => "HOLD TIME",
            GameMode.Nodes or GameMode.NodesTeams => "NODE SCORE",
            GameMode.PrimeHunter => "PRIME TIME",
            GameMode.Relic => "RELIC HOLD TIME",
            GameMode.Hardpoint or GameMode.HardpointTeams => "HARDPOINT HOLD TIME",
            _ => "SCORE GOAL"
        };

        public static bool TryDuration(string? text, bool allowZero, out ushort seconds)
        {
            seconds = 0;
            text = (text ?? "").Trim();
            if (text.Length == 0) return false;

            int colon = text.IndexOf(':');
            if (colon >= 0)
            {
                if (text.IndexOf(':', colon + 1) >= 0
                    || !int.TryParse(text[..colon], NumberStyles.None,
                        CultureInfo.InvariantCulture, out int minutes)
                    || !int.TryParse(text[(colon + 1)..], NumberStyles.None,
                        CultureInfo.InvariantCulture, out int remainder)
                    || remainder is < 0 or >= 60)
                    return false;
                long total = minutes * 60L + remainder;
                if (total > UInt16.MaxValue || (!allowZero && total == 0))
                    return false;
                seconds = (ushort)total;
                return true;
            }

            if (!double.TryParse(text, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out double decimalMinutes)
                || !double.IsFinite(decimalMinutes)
                || decimalMinutes < 0 || (!allowZero && decimalMinutes == 0)
                || decimalMinutes * 60 > UInt16.MaxValue)
                return false;
            seconds = (ushort)Math.Round(decimalMinutes * 60,
                MidpointRounding.AwayFromZero);
            return allowZero || seconds != 0;
        }

        public static bool TryGoal(GameMode mode, string? text,
            out ushort goal, out string reason)
        {
            reason = "";
            goal = 0;
            if (mode == GameMode.OneInTheChamber) { goal = 2; return true; }
            if (mode == GameMode.GunGame)
            {
                goal = GunGameRules.StageCount;
                return true;
            }

            text = (text ?? "").Trim();
            if (MatchGoalRules.UsesLives(mode))
            {
                if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture,
                        out int lives) || lives is < 1 or > UInt16.MaxValue + 1)
                {
                    reason = "Lives must be a whole number from 1 to 65536.";
                    return false;
                }
                goal = (ushort)(lives - 1);
                return true;
            }

            if (MatchGoalRules.UsesTimeTarget(mode))
            {
                if (!TryDuration(text, allowZero: false, out goal))
                {
                    reason = $"{GoalLabel(mode)} must be minutes (1.5) or m:ss (1:30).";
                    return false;
                }
                return true;
            }

            if (!ushort.TryParse(text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out goal))
            {
                reason = $"{GoalLabel(mode)} must be a whole number from 0 to 65535.";
                return false;
            }
            return true;
        }
    }
}
