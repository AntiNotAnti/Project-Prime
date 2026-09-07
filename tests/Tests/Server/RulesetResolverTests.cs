using System;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class RulesetResolverTests
{
    [Theory]
    [InlineData(null, RulesetPreset.Classic)]
    [InlineData("classic", RulesetPreset.Classic)]
    [InlineData("COMPETITIVE", RulesetPreset.Competitive)]
    [InlineData("duel", RulesetPreset.Duel)]
    [InlineData("custom", RulesetPreset.Custom)]
    public void ParseUsesOnlyTheNamedPresets(string? value, RulesetPreset expected)
        => Assert.Equal(expected, RulesetResolver.Parse(value));

    [Theory]
    [InlineData("")]
    [InlineData(" ranked ")]
    [InlineData("survival")]
    public void ParseRejectsUnknownPresetNames(string value)
        => Assert.Throws<ArgumentException>(() => RulesetResolver.Parse(value));

    [Fact]
    public void VerifyClassicParity()
    {
        var entry = new RotationEntry
        {
            RoomKey = "MP5 ARCTERRA",
            Mode = GameMode.Defender,
            TimeLimit = 900,
            PointGoal = 17,
            ObjectiveTimeGoal = 45
        };

        MatchRules expected = entry.ToMatchRules(maxPlayers: 6, friendlyFire: true);
        MatchRules resolved = RulesetResolver.Resolve(entry, RulesetPreset.Classic,
            maxPlayers: 6, friendlyFire: true);

        Assert.Equal(expected, resolved);
        Assert.Equal(RulesetPreset.Classic, resolved.RulesetPreset);
    }

    [Fact]
    public void CompetitiveChangesPolicyOnlyAndKeepsDamageHealthAndTimingInputs()
    {
        var entry = new RotationEntry
        {
            RoomKey = "MP2 ACCELERATOR",
            Mode = GameMode.Survival,
            TimeLimit = 180,
            PointGoal = 3
        };
        MatchRules baseline = entry.ToMatchRules(maxPlayers: 6, friendlyFire: true);
        MatchRules resolved = RulesetResolver.Resolve(entry, RulesetPreset.Competitive,
            maxPlayers: 6, friendlyFire: true);

        Assert.Equal(baseline.Mode, resolved.Mode);
        Assert.Equal(baseline.RoomKey, resolved.RoomKey);
        Assert.Equal(baseline.MaxPlayers, resolved.MaxPlayers);
        Assert.Equal(baseline.TimeLimit, resolved.TimeLimit);
        Assert.Equal(baseline.ScoreGoal, resolved.ScoreGoal);
        Assert.Equal(baseline.StartingLives, resolved.StartingLives);
        Assert.Equal(baseline.ObjectiveTimeGoal, resolved.ObjectiveTimeGoal);
        Assert.Equal(baseline.FriendlyFire, resolved.FriendlyFire);
        Assert.Equal(baseline.AffinityWeapons, resolved.AffinityWeapons);
        Assert.Equal(baseline.DamageLevel, resolved.DamageLevel);
        Assert.Equal(baseline.AssistMinimumDamage, resolved.AssistMinimumDamage);
        Assert.Equal(baseline.AssistWindowTicks, resolved.AssistWindowTicks);
        Assert.Equal(baseline.PickupRespawnAnnouncements, resolved.PickupRespawnAnnouncements);

        Assert.Equal(RulesetPreset.Competitive, resolved.RulesetPreset);
        Assert.Equal(SpawnPolicy.Enhanced, resolved.SpawnPolicy);
        Assert.True(resolved.CancelSpawnProtectionOnOffensiveAction);
        Assert.Equal(OvertimePolicy.ModeDefault, resolved.OvertimePolicy);
        Assert.Equal(LateJoinPolicy.Disabled, resolved.LateJoinPolicy);
        Assert.Equal(RankingEligibility.VerifiedServerOnly, resolved.RankingEligibility);
        Assert.Equal(RadarPolicy.Disabled, resolved.RadarPolicy);
        Assert.False(resolved.PlayerRadar);
        Assert.Equal(TeamBalancePolicy.Locked, resolved.TeamBalancePolicy);
    }

    [Fact]
    public void DuelRequiresBattleAndAlwaysUsesTwoPlayers()
    {
        MatchRules duel = RulesetResolver.Resolve(new RotationEntry
        {
            RoomKey = "MP1 SANCTORUS",
            Mode = GameMode.Battle,
            TimeLimit = 420,
            PointGoal = 7
        }, RulesetPreset.Duel, maxPlayers: 8);

        Assert.Equal(2, duel.MaxPlayers);
        Assert.Equal(RulesetPreset.Duel, duel.RulesetPreset);
        Assert.Equal(SpawnPolicy.Duel, duel.SpawnPolicy);
        Assert.Equal(RankingEligibility.VerifiedServerOnly, duel.RankingEligibility);
        Assert.Equal(LateJoinPolicy.Disabled, duel.LateJoinPolicy);
        Assert.Equal(TeamBalancePolicy.Locked, duel.TeamBalancePolicy);

        Assert.Throws<ArgumentException>(() => RulesetResolver.Resolve(new RotationEntry
        {
            RoomKey = "MP1 SANCTORUS",
            Mode = GameMode.BattleTeams,
            TimeLimit = 420,
            PointGoal = 7
        }, RulesetPreset.Duel));
    }

    [Fact]
    public void RankedRulesAreLockedAndPublicStartRemainsFailClosed()
    {
        MatchRules ranked = RulesetResolver.Resolve(new RotationEntry
        {
            RoomKey = "MP1 SANCTORUS",
            Mode = GameMode.Battle,
            TimeLimit = 420,
            PointGoal = 7
        }, RulesetPreset.Competitive);

        Assert.True(ServerRankedPolicy.HasLockedConstraints(ranked));
        Assert.False(ServerRankedPolicy.PublicStartAllowed(ranked));
        InvalidOperationException disabled = Assert.Throws<InvalidOperationException>(
            () => ServerRankedPolicy.ValidatePublicStart(ranked));
        Assert.Equal(ServerRankedPolicy.PublicDisabledReason, disabled.Message);

        MatchRules inconsistent = ranked.With(lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch);
        Assert.False(ServerRankedPolicy.HasLockedConstraints(inconsistent));
        Assert.Throws<ArgumentException>(() => ServerRankedPolicy.ValidatePublicStart(inconsistent));
    }

    [Fact]
    public void WithPreservesAllPoliciesWhenChangingAContentField()
    {
        MatchRules original = new(MatchMode.Battle, "POLICY", maxPlayers: 6,
            timeLimit: TimeSpan.FromMinutes(7), scoreGoal: 7,
            spawnPolicy: SpawnPolicy.Enhanced, cancelSpawnProtectionOnOffensiveAction: true,
            assistMinimumDamage: 31, assistWindowTicks: 401,
            overtimePolicy: OvertimePolicy.ModeDefault,
            lateJoinPolicy: LateJoinPolicy.SpectateUntilNextMatch,
            pickupRespawnAnnouncements: true,
            rulesetPreset: RulesetPreset.Competitive,
            rankingEligibility: RankingEligibility.VerifiedServerOnly,
            radarPolicy: RadarPolicy.Disabled,
            teamBalancePolicy: TeamBalancePolicy.Locked);

        MatchRules changed = original.With(scoreGoal: 9, roomKey: "CHANGED");

        Assert.Equal("CHANGED", changed.RoomKey);
        Assert.Equal(9, changed.ScoreGoal);
        Assert.Equal(original.MaxPlayers, changed.MaxPlayers);
        Assert.Equal(original.TimeLimit, changed.TimeLimit);
        Assert.Equal(original.SpawnPolicy, changed.SpawnPolicy);
        Assert.Equal(original.CancelSpawnProtectionOnOffensiveAction, changed.CancelSpawnProtectionOnOffensiveAction);
        Assert.Equal(original.AssistMinimumDamage, changed.AssistMinimumDamage);
        Assert.Equal(original.AssistWindowTicks, changed.AssistWindowTicks);
        Assert.Equal(original.OvertimePolicy, changed.OvertimePolicy);
        Assert.Equal(original.LateJoinPolicy, changed.LateJoinPolicy);
        Assert.Equal(original.PickupRespawnAnnouncements, changed.PickupRespawnAnnouncements);
        Assert.Equal(original.RulesetPreset, changed.RulesetPreset);
        Assert.Equal(original.RankingEligibility, changed.RankingEligibility);
        Assert.Equal(original.RadarPolicy, changed.RadarPolicy);
        Assert.Equal(original.TeamBalancePolicy, changed.TeamBalancePolicy);
    }

    [Fact]
    public void EnabledRadarOverrideSurvivesSurvivalWrites()
    {
        Scene scene = Scene.CreateHeadless();
        try
        {
            MatchRules rules = new(MatchMode.Survival, "SURVIVAL", maxPlayers: 2,
                startingLives: 2, radarPolicy: RadarPolicy.Enabled);
            scene.Match.ApplyRules(rules);

            scene.Match.RadarPlayers = false;
            scene.Match.Logic.ModeStateSurvival();

            Assert.True(scene.Match.RadarPlayers);
            Assert.True(rules.PlayerRadar);
        }
        finally
        {
            scene.CloseHeadless();
        }
    }

    [Fact]
    public void LockedTeamBalanceSkipsPreMatchRebalance()
    {
        using var transport = new NetTransport(0);
        MatchRules rules = new(MatchMode.TeamBattle, "LOCKED", maxPlayers: 4,
            teamBalancePolicy: TeamBalancePolicy.Locked);
        var server = new ServerNetwork(transport, rules);

        Assert.Equal(TeamBalancePolicy.Locked, server.Rules.TeamBalancePolicy);
        Assert.False(server.RebalanceBeforeStart());
    }
}
