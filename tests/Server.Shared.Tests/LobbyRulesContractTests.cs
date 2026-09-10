using System.Collections.Immutable;
using FruityPrime.Server.Shared;
using MphRead;
using Xunit;

namespace FruityPrime.Server.Shared.Tests;

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
            OctolithReset: true);

        LobbyRulesOptions projected = prior.ForMode(MatchMode.Survival);
        Assert.Equal(600, projected.TimeLimitSeconds);
        Assert.Equal(2, projected.DamageLevel);
        Assert.True(projected.FriendlyFire);
        Assert.True(projected.AffinityWeapons);
        Assert.True(projected.PlayerRadar);
        Assert.Equal(4, projected.StartingLives);
        Assert.Null(projected.ScoreGoal);
        Assert.Null(projected.ObjectiveTimeGoalSeconds);
        Assert.Null(projected.OctolithReset);
        Assert.Equal(4, projected.ToMatchRules(MatchMode.Survival, "unit").StartingLives);
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
