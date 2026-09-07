using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using MphRead.Combat;

namespace MphRead.Hud
{
    /// <summary>Prepared immutable result text, separate from the live scoreboard.</summary>
    public sealed class PostMatchPresentation
    {
        public string[][] Pages { get; }
        public string[] Headers { get; } = { "RESULTS: KILLS / DEATHS / ASSISTS", "DMG / HS KILLS / STREAK / BEST", "MODE RESULTS" };
        public PostMatchPresentation(MatchResult result)
        {
            var general = new List<string>(8);
            var combat = new List<string>(8);
            var objective = new List<string>(8);
            foreach (int slot in result.ResultSlots)
            {
                if (slot < 0 || slot >= result.Players.Length) continue;
                PlayerMatchResult p = result.Players[slot];
                if (!p.Active) continue;
                string name = p.Nickname.Length > 12 ? p.Nickname[..12] : p.Nickname;
                string prefix = $"{p.Standings + 1}. {name}{(p.IsBot ? " [BOT]" : "")}";
                general.Add($"{prefix}   {p.Kills}/{p.Deaths}/{p.Assists}");
                combat.Add($"{name}  {p.DamageDealt} / {p.HeadshotKills} / {p.LongestKillStreak} / {BestWeapon(p.BeamKills)}");
                string mode = result.Rules.Mode switch
                {
                    MatchMode.Bounty or MatchMode.TeamBounty or MatchMode.Capture => $"scores {p.OctolithScores} drops {p.OctolithDrops} stops {p.OctolithStops}",
                    MatchMode.Nodes or MatchMode.TeamNodes or MatchMode.Defender or MatchMode.TeamDefender => $"nodes {p.NodesCaptured} lost {p.NodesLost}",
                    MatchMode.PrimeHunter => $"Prime {p.Time:0.0}s kills {p.KillsAsPrime} stops {p.PrimesKilled}",
                    _ => $"score {p.Points} suicides {p.Suicides} team kills {p.FriendlyKills}"
                };
                objective.Add($"{name}  {mode}");
            }
            Pages = new[] { general.ToArray(), combat.ToArray(), objective.ToArray() };
        }
        public static string BestWeapon(ImmutableArray<int> kills)
        {
            int best = -1, maximum = 0;
            for (int i = 0; i < Math.Min(9, kills.Length); i++) if (kills[i] > maximum) { best = i; maximum = kills[i]; }
            return best < 0 ? "None" : CombatFeedback.WeaponName((byte)best);
        }
    }
}
