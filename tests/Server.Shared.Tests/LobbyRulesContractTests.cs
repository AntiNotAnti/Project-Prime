using System.Collections.Immutable;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class LobbyRulesContractTests
{
    [Fact]
    public void LegacyValuesNormalizeIntoModeSpecificStructuredFields()
    {
        LobbyRulesOptions survival = new LobbyRulesOptions().Normalize(MatchMode.Survival, 600, 4);
        Assert.Equal(600, survival.TimeLimitSeconds);
        Assert.Null(survival.ScoreGoal);
        Assert.Equal(4, survival.StartingLives);

        LobbyRulesOptions objective = new LobbyRulesOptions(ObjectiveTimeGoalSeconds: 120)
            .Normalize(MatchMode.Defender);
        Assert.Equal(120, objective.ObjectiveTimeGoalSeconds);
        Assert.Null(objective.ScoreGoal);
        Assert.Null(objective.StartingLives);
    }

    [Fact]
    public void ConflictingLegacyAndStructuredValuesFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new LobbyRulesOptions(TimeLimitSeconds: 601)
            .Normalize(MatchMode.Battle, 600));
        Assert.Throws<ArgumentException>(() => new LobbyRulesOptions(ScoreGoal: 12)
            .Normalize(MatchMode.Battle, legacyPointGoal: 11));
    }

    [Fact]
    public void NextModeProjectionKeepsCommonOptionsAndUsesTargetDefaults()
    {
        LobbyRulesOptions prior = new(TimeLimitSeconds: 600, ScoreGoal: 11,
            StartingLives: 4, ObjectiveTimeGoalSeconds: 120, DamageLevel: 2,
            FriendlyFire: true, AffinityWeapons: true, PlayerRadar: true,
            OctolithReset: true, KillcamPolicy: KillcamPolicy.PostRound,
            PowerupsEnabled: false, EnhancedHunters: true, BalancedMode: true);

        LobbyRulesOptions projected = prior.ForMode(MatchMode.Survival);
        Assert.Equal(600, projected.TimeLimitSeconds);
        Assert.Equal(2, projected.DamageLevel);
        Assert.True(projected.FriendlyFire);
        Assert.True(projected.AffinityWeapons);
        Assert.True(projected.PlayerRadar);
        Assert.Equal(KillcamPolicy.PostRound, projected.KillcamPolicy);
        Assert.False(projected.PowerupsEnabled);
        Assert.True(projected.EnhancedHunters);
        Assert.True(projected.BalancedMode);
        Assert.Equal(4, projected.StartingLives);
        Assert.Null(projected.ScoreGoal);
        Assert.Null(projected.ObjectiveTimeGoalSeconds);
        Assert.Null(projected.OctolithReset);
        Assert.Equal(4, projected.ToMatchRules(MatchMode.Survival, "unit").StartingLives);
        Assert.Equal(KillcamPolicy.PostRound,
            projected.ToMatchRules(MatchMode.Survival, "unit").KillcamPolicy);
        Assert.False(projected.ToMatchRules(MatchMode.Survival, "unit")
            .PowerupsEnabled);
        MatchRules enhanced = projected.ToMatchRules(MatchMode.Survival, "unit");
        Assert.True(enhanced.EnhancedHunters);
        Assert.True(enhanced.BalancedMode);
        Assert.Equal(RulesetPreset.Custom, enhanced.RulesetPreset);
        Assert.Equal(RankingEligibility.Unranked, enhanced.RankingEligibility);
    }

    [Fact]
    public void KillcamPolicyDefaultsAndExplicitHostSelectionAreValidated()
    {
        MatchRules defaults = LobbyRulesOptions.Empty.ToMatchRules(MatchMode.Battle, "unit");
        MatchRules disabled = new LobbyRulesOptions(KillcamPolicy: KillcamPolicy.Disabled)
            .ToMatchRules(MatchMode.Battle, "unit");

        Assert.True(defaults.PlayerRadar);
        Assert.Equal(KillcamPolicy.Immediate, defaults.KillcamPolicy);
        Assert.Equal(KillcamPolicy.Disabled, disabled.KillcamPolicy);
        Assert.Throws<ArgumentException>(() =>
            new LobbyRulesOptions(KillcamPolicy: (KillcamPolicy)3).Normalize(MatchMode.Battle));
    }

    [Fact]
    public void SpawnPolicyAndProtectionCancellationAreAuthoritativeLobbyRules()
    {
        MatchRules enhanced = new LobbyRulesOptions(SpawnPolicy: SpawnPolicy.Enhanced,
            CancelSpawnProtectionOnOffensiveAction: true)
            .ToMatchRules(MatchMode.TeamBattle, "unit", maxPlayers: 8);
        MatchRules duel = new LobbyRulesOptions(SpawnPolicy: SpawnPolicy.Duel)
            .ToMatchRules(MatchMode.Battle, "unit", maxPlayers: 2);

        Assert.Equal(SpawnPolicy.Enhanced, enhanced.SpawnPolicy);
        Assert.True(enhanced.CancelSpawnProtectionOnOffensiveAction);
        Assert.Equal(SpawnPolicy.Duel, duel.SpawnPolicy);
        Assert.Throws<ArgumentException>(() =>
            new LobbyRulesOptions(SpawnPolicy: SpawnPolicy.Duel)
                .ToMatchRules(MatchMode.Battle, "unit", maxPlayers: 8));
        Assert.Throws<ArgumentException>(() =>
            new LobbyRulesOptions(SpawnPolicy: SpawnPolicy.Duel)
                .Normalize(MatchMode.TeamBattle));
        Assert.Throws<ArgumentException>(() =>
            new LobbyRulesOptions(SpawnPolicy: (SpawnPolicy)255)
                .Normalize(MatchMode.Battle));
    }

    [Fact]
    public void ResourceRadarPolicyDefaultsToDisabledAndProjectsToMatchRules()
    {
        Assert.Null(LobbyRulesOptions.Empty.ResourceRadarPolicy);
        Assert.Equal(ResourceRadarPolicy.Disabled,
            LobbyRulesOptions.Empty.ToMatchRules(MatchMode.Battle, "unit")
                .ResourceRadarPolicy);

        LobbyRulesOptions selected = new(
            ResourceRadarPolicy: ResourceRadarPolicy.AvailableWithRespawn);
        Assert.Equal(ResourceRadarPolicy.AvailableWithRespawn,
            selected.Normalize(MatchMode.Battle).ResourceRadarPolicy);
        Assert.Equal(ResourceRadarPolicy.AvailableWithRespawn,
            selected.ToMatchRules(MatchMode.Battle, "unit").ResourceRadarPolicy);

        Assert.Throws<ArgumentException>(() => new LobbyRulesOptions(
            ResourceRadarPolicy: (ResourceRadarPolicy)4).Normalize(MatchMode.Battle));
    }

    [Fact]
    public void SnapshotCanonicalRulesRejectConflictingLegacyProjection()
    {
        LobbySnapshot snapshot = new(Guid.NewGuid(), "Rules", LobbyVisibility.Public, Guid.NewGuid(),
            LobbyPhase.Open, 1, 8, 0, [], [], "unit", MatchMode.Battle,
            TimeLimitSeconds: 600, PointGoal: 11,
            Rules: new LobbyRulesOptions(TimeLimitSeconds: 601, ScoreGoal: 11));

        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("lobby.snapshot", 1, null, snapshot));
    }

    [Fact]
    public void MaximumListMetadataFrameStaysWithinControlBound()
    {
        LobbyListEntry[] rows = Enumerable.Range(0, NodeControlCodec.MaximumLobbyListEntries).Select(index => new LobbyListEntry(
            Guid.NewGuid(), new string('L', 64), LobbyPhase.Open, 8, 8, 16, 1,
            WaitlistCount: 1024, ObserverLimit: 16, BotCount: 0,
            MapKey: new string('M', 128), Mode: MatchMode.Battle,
            TimeLimitSeconds: 3600, PointGoal: ushort.MaxValue,
            SeatPolicy: LobbySeatPolicy.ObserverUntilNextMatch)).ToArray();

        byte[] frame = NodeControlCodec.Write("lobby.list", 1, null,
            new LobbyListSnapshot(rows.ToImmutableArray(), null));
        Assert.InRange(frame.Length, 1, NodeControlCodec.MaximumFrameBytes);
    }
}
