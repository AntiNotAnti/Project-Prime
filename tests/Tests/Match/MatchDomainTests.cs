using System;
using MphRead.Entities;
using MphRead.Formats;
using Xunit;

namespace MphRead.Tests;

public sealed class MatchDomainTests
{
    [Fact]
    public void MultiplayerModeConversionUsesExactlyTheTwelveLegacyModes()
    {
        var expected = new[]
        {
            (Legacy: GameMode.Battle, Modern: MatchMode.Battle),
            (Legacy: GameMode.BattleTeams, Modern: MatchMode.TeamBattle),
            (Legacy: GameMode.Survival, Modern: MatchMode.Survival),
            (Legacy: GameMode.SurvivalTeams, Modern: MatchMode.TeamSurvival),
            (Legacy: GameMode.Capture, Modern: MatchMode.Capture),
            (Legacy: GameMode.Bounty, Modern: MatchMode.Bounty),
            (Legacy: GameMode.BountyTeams, Modern: MatchMode.TeamBounty),
            (Legacy: GameMode.Nodes, Modern: MatchMode.Nodes),
            (Legacy: GameMode.NodesTeams, Modern: MatchMode.TeamNodes),
            (Legacy: GameMode.Defender, Modern: MatchMode.Defender),
            (Legacy: GameMode.DefenderTeams, Modern: MatchMode.TeamDefender),
            (Legacy: GameMode.PrimeHunter, Modern: MatchMode.PrimeHunter)
        };

        Assert.Equal(expected.Length, Enum.GetValues<MatchMode>().Length);
        foreach (var pair in expected)
        {
            Assert.Equal(pair.Modern, pair.Legacy.ToMatchMode());
            Assert.Equal(pair.Legacy, pair.Modern.ToLegacyMode());
        }

        Assert.True(MatchMode.Capture.IsTeamMode());
        Assert.False(MatchMode.Bounty.IsTeamMode());
        Assert.True(MatchMode.TeamDefender.IsTeamMode());
    }

    [Theory]
    [InlineData(GameMode.None)]
    [InlineData(GameMode.SinglePlayer)]
    [InlineData(GameMode.Unknown15)]
    public void NonMultiplayerLegacyModesAreRejected(GameMode mode)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => mode.ToMatchMode());
    }

    [Fact]
    public void UnknownModernModesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((MatchMode)255).ToLegacyMode());
    }

    [Fact]
    public void DefaultRulesPreserveLegacyGoalsAndClockSemantics()
    {
        MatchRules battle = MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS");
        Assert.Equal(PlayerEntity.SlotCapacity, battle.MaxPlayers);
        Assert.Equal(TimeSpan.FromMinutes(7), battle.TimeLimit);
        Assert.Equal(7, battle.ScoreGoal);
        Assert.Equal(0, battle.StartingLives);
        Assert.Null(battle.ObjectiveTimeGoal);
        Assert.Equal(7, battle.LegacyPointGoal);
        Assert.Equal(0f, battle.LegacyTimeGoal);
        Assert.False(battle.Teams);
        Assert.True(battle.PlayerRadar);

        MatchRules survival = MatchRules.CreateDefault(MatchMode.Survival, "MP2 ACCELERATOR");
        Assert.Equal(TimeSpan.FromMinutes(15), survival.TimeLimit);
        Assert.Equal(0, survival.ScoreGoal);
        Assert.Equal(2, survival.StartingLives);
        Assert.Equal(2, survival.LegacyPointGoal);

        MatchRules capture = MatchRules.CreateDefault(MatchMode.Capture, "MP3 PROVING GROUND");
        Assert.True(capture.Teams);
        Assert.Equal(5, capture.LegacyPointGoal);

        MatchRules nodes = MatchRules.CreateDefault(MatchMode.Nodes, "MP4 ALINOS");
        Assert.Equal(70, nodes.LegacyPointGoal);

        MatchRules defender = MatchRules.CreateDefault(MatchMode.Defender, "MP5 ARCTERRA");
        Assert.Equal(TimeSpan.FromSeconds(90), defender.ObjectiveTimeGoal);
        Assert.Equal(90f, defender.LegacyTimeGoal);

        MatchRules legacySurvival = MatchRules.FromLegacy(
            MatchMode.TeamSurvival, "MP6 CANDRO", timeLimit: 90, pointGoal: 3, timeGoal: 12);
        Assert.Equal(TimeSpan.FromSeconds(90), legacySurvival.TimeLimit);
        Assert.Equal(0, legacySurvival.ScoreGoal);
        Assert.Equal(3, legacySurvival.StartingLives);
        Assert.Equal(3, legacySurvival.LegacyPointGoal);
        Assert.Equal(12f, legacySurvival.LegacyTimeGoal);

        MatchRules unlimited = MatchRules.FromLegacy(MatchMode.Battle, "MP7", -1, 7, 0);
        Assert.Null(unlimited.TimeLimit);
        Assert.Equal(-1f, new MatchRuntime(unlimited).MatchTime);

        MatchRules expired = MatchRules.FromLegacy(MatchMode.Battle, "MP8", 0, 7, 0);
        Assert.Equal(TimeSpan.Zero, expired.TimeLimit);
        Assert.Equal(0f, new MatchRuntime(expired).MatchTime);
    }

    [Fact]
    public void RulesValidateInputsAndWithKeepsTheOriginalImmutable()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchRules((MatchMode)255, "room"));
        Assert.Throws<ArgumentException>(() => new MatchRules(MatchMode.Battle, " "));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", maxPlayers: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", maxPlayers: PlayerEntity.SlotCapacity + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", timeLimit: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", objectiveTimeGoal: TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", scoreGoal: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", startingLives: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MatchRules(MatchMode.Battle, "room", damageLevel: 3));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchRules.FromLegacy(MatchMode.Battle, "room", float.NaN, 7, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchRules.FromLegacy(MatchMode.Battle, "room", -0.5f, 7, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MatchRules.FromLegacy(MatchMode.Battle, "room", 60, 7, float.PositiveInfinity));

        var original = new MatchRules(MatchMode.Battle, "room", timeLimit: TimeSpan.FromMinutes(7),
            scoreGoal: 5, friendlyFire: true, damageLevel: 2);
        MatchRules changed = original.With(scoreGoal: 9, roomKey: "other");

        Assert.Equal(5, original.ScoreGoal);
        Assert.Equal("room", original.RoomKey);
        Assert.True(original.FriendlyFire);
        Assert.Equal(2, original.DamageLevel);
        Assert.Equal(9, changed.ScoreGoal);
        Assert.Equal("other", changed.RoomKey);
        Assert.Equal(original.TimeLimit, changed.TimeLimit);
        Assert.True(changed.FriendlyFire);
        Assert.Equal(2, changed.DamageLevel);
    }

    [Fact]
    public void MatchRuntimeInstancesDoNotShareRulesOrCounters()
    {
        MatchRules rules = MatchRules.CreateDefault(MatchMode.Battle, "room");
        var first = new MatchRuntime(rules);
        var second = new MatchRuntime(rules);

        first.MatchId = 17;
        Assert.Equal(17u, first.MatchId);
        Assert.Equal(0u, second.MatchId);
        Assert.NotSame(first.Players[0], second.Players[0]);

        first.MatchTime = 12;
        first.Points[0] = 9;
        first.Players[1].Kills = 4;
        first.BeamKills[2, 3] = 7;

        Assert.Equal(12, first.MatchTime);
        Assert.Equal(9, first.Points[0]);
        Assert.Equal(4, first.Kills[1]);
        Assert.Equal(7, first.BeamKills[2, 3]);
        Assert.Equal(420f, second.MatchTime);
        Assert.Equal(0, second.Points[0]);
        Assert.Equal(0, second.Kills[1]);
        Assert.Equal(0, second.BeamKills[2, 3]);

        MatchRules replacement = rules.With(scoreGoal: 11);
        first.ApplyRules(replacement);
        Assert.Same(replacement, first.Rules);
        Assert.Same(rules, second.Rules);
        Assert.Equal(9, first.Points[0]);
    }

    [Fact]
    public void PlayerViewsAliasRuntimeStorage()
    {
        var runtime = new MatchRuntime(MatchRules.CreateDefault(MatchMode.Battle, "room"));

        Assert.Equal(PlayerEntity.SlotCapacity, runtime.Players.Count);
        for (int slot = 0; slot < runtime.Players.Count; slot++)
        {
            PlayerMatchStats stats = runtime.Players[slot];
            Assert.Equal(slot, stats.Slot);
            Assert.Same(stats, runtime.Players[slot]);

            runtime.Points[slot] = slot + 10;
            stats.Deaths = slot + 20;
            stats.BeamDamageMax = slot + 30;

            Assert.Equal(slot + 10, stats.Points);
            Assert.Equal(slot + 20, runtime.Deaths[slot]);
            Assert.Equal(slot + 30, runtime.BeamDamageMax[slot]);
        }
    }

    [Fact]
    public void BeamStatsAliasEachWeaponAndRejectOutOfRangeIndexes()
    {
        var runtime = new MatchRuntime(MatchRules.CreateDefault(MatchMode.Battle, "room"));
        PlayerMatchStats stats = runtime.Players[2];

        stats.SetBeamKills(0, 4);
        stats.SetBeamKills(8, 9);
        stats.BeamDamageDealt = 123;
        runtime.BeamKills[2, 4] = 7;

        Assert.Equal(4, runtime.BeamKills[2, 0]);
        Assert.Equal(9, stats.GetBeamKills(8));
        Assert.Equal(7, stats.GetBeamKills(4));
        Assert.Equal(123, runtime.BeamDamageDealt[2]);

        Assert.Throws<ArgumentOutOfRangeException>(() => stats.GetBeamKills(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stats.GetBeamKills(9));
        Assert.Throws<ArgumentOutOfRangeException>(() => stats.SetBeamKills(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stats.SetBeamKills(9, 0));
    }
}
