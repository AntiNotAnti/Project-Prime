using System;

namespace MphRead.Mods.Network
{
    /// <summary>One policy composition for startup, rotation, and operator selection.</summary>
    public static class RulesetResolver
    {
        public static RulesetPreset Parse(string? value) => value?.ToLowerInvariant() switch
        {
            null or "classic" => RulesetPreset.Classic,
            "competitive" => RulesetPreset.Competitive,
            "duel" => RulesetPreset.Duel,
            "custom" => RulesetPreset.Custom,
            _ => throw new ArgumentException("-ruleset requires classic, competitive, duel, or custom.")
        };

        public static MatchRules Resolve(RotationEntry entry, RulesetPreset preset, int maxPlayers = 8, bool friendlyFire = false)
        {
            if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
            if (preset == RulesetPreset.Duel && entry.Mode != GameMode.Battle)
                throw new ArgumentException("Every Duel rotation entry must use Battle mode.");
            MatchRules rules = entry.ToMatchRules(preset == RulesetPreset.Duel ? 2 : maxPlayers, friendlyFire);
            if (preset is RulesetPreset.Classic or RulesetPreset.Custom) return rules.With(rulesetPreset: preset);
            // Revision 1 changes fairness policy only; content damage, movement, charge, and item timings stay intact.
            return rules.With(rulesetPreset: preset,
                spawnPolicy: preset == RulesetPreset.Duel ? SpawnPolicy.Duel : SpawnPolicy.Enhanced,
                cancelSpawnProtectionOnOffensiveAction: true, overtimePolicy: OvertimePolicy.ModeDefault,
                lateJoinPolicy: LateJoinPolicy.Disabled,
                rankingEligibility: RankingEligibility.VerifiedServerOnly,
                radarPolicy: RadarPolicy.Disabled, teamBalancePolicy: TeamBalancePolicy.Locked);
        }
    }
}
