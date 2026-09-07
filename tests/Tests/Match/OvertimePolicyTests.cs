using System;
using System.Collections.Generic;
using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class OvertimePolicyTests
{
    public static IEnumerable<object[]> Modes()
    {
        foreach (MatchMode mode in Enum.GetValues<MatchMode>()) yield return new object[] { mode };
    }

    [Theory, MemberData(nameof(Modes))]
    public void PrimaryTieAtExpiryContinuesWithoutChangingThePlayingPhase(MatchMode mode)
    {
        using Fixture fixture = new(mode);
        fixture.Scene.Match.Players[0].Kills = 9; // secondary ranking must not break a primary tie
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.NotEqual(MatchPeriod.Regulation, fixture.Scene.Match.Period);
        Assert.Equal(MatchPhase.Playing, fixture.Scene.Match.Phase);
        Assert.Equal(-1, fixture.Scene.Match.MatchTime);
        Assert.False(fixture.Scene.Match.HasPhaseDeadline);
        Assert.Equal(17u, fixture.Scene.Match.PhaseRevision);
    }

    [Theory, MemberData(nameof(Modes))]
    public void NonTiedExpiryDoesNotInventOvertime(MatchMode mode)
    {
        using Fixture fixture = new(mode);
        if (fixture.Scene.Match.Rules.IsSurvival) fixture.Scene.Match.TeamDeaths[1] = 1;
        else if (mode is MatchMode.Defender or MatchMode.TeamDefender) fixture.Scene.Match.TeamTime[0] = 1;
        else if (mode == MatchMode.PrimeHunter) fixture.Scene.Match.Players[0].Time = 1;
        else fixture.Scene.Match.TeamPoints[0] = 1;
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.Equal(MatchPeriod.Regulation, fixture.Scene.Match.Period);
        Assert.Equal(0, fixture.Scene.Match.MatchTime);
    }

    [Fact]
    public void DisabledDefaultAndExplicitEndsNeverContinue()
    {
        using Fixture fixture = new(MatchMode.Battle);
        fixture.Scene.Match.ApplyRules(fixture.Scene.Match.Rules.With(overtimePolicy: OvertimePolicy.Disabled));
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.Equal(0, fixture.Scene.Match.MatchTime);
        fixture.Scene.Match.ApplyRules(fixture.Scene.Match.Rules.With(overtimePolicy: OvertimePolicy.ModeDefault));
        foreach (MatchEndReason reason in new[] { MatchEndReason.CompletionMessage, MatchEndReason.InvalidTeams, MatchEndReason.Forced })
        {
            MatchOvertime.Evaluate(fixture.Scene, true, reason);
            Assert.Equal(MatchPeriod.Regulation, fixture.Scene.Match.Period);
            Assert.Equal(reason, fixture.Scene.Match.PendingEndReason);
        }
        fixture.Scene.Match.PendingEndReason = null;
        fixture.Scene.Match.ForceEndGame = true;
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.Equal(MatchPeriod.Regulation, fixture.Scene.Match.Period);
    }

    [Fact]
    public void BattleLeadEndsSuddenDeathWhileSurvivalRetainsItsLivesUntilElimination()
    {
        using (Fixture battle = new(MatchMode.Battle))
        {
            MatchOvertime.Evaluate(battle.Scene, true, null);
            battle.Scene.Match.TeamPoints[0] = 1;
            MatchOvertime.Evaluate(battle.Scene, false, null);
            Assert.Equal(0, battle.Scene.Match.MatchTime);
        }
        using Fixture survival = new(MatchMode.Survival);
        MatchOvertime.Evaluate(survival.Scene, true, null);
        survival.Scene.Match.TeamDeaths[1] = 1;
        MatchOvertime.Evaluate(survival.Scene, false, null);
        Assert.Equal(-1, survival.Scene.Match.MatchTime);
        survival.Scene.Match.TeamDeaths[1] = 3;
        PlayerEntity.Players[1].Health = 0;
        MatchOvertime.Evaluate(survival.Scene, false, null);
        Assert.Equal(0, survival.Scene.Match.MatchTime);
    }

    [Fact]
    public void ExpiryDoesNotClampOneTiedScoreButRetainsACompletedGoalReasonForALeader()
    {
        using Fixture fixture = new(MatchMode.Battle);
        fixture.Scene.Match.ApplyRules(fixture.Scene.Match.Rules.With(scoreGoal: 3));
        fixture.Scene.Match.TeamPoints[0] = fixture.Scene.Match.TeamPoints[1] = 5;
        fixture.Scene.Match.Logic.ProcessMode();
        Assert.Equal(5, fixture.Scene.Match.TeamPoints[0]);
        Assert.Equal(MatchEndReason.ScoreGoal, fixture.Scene.Match.PendingEndReason);
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.Equal(MatchPeriod.SuddenDeath, fixture.Scene.Match.Period);
        fixture.Scene.Match.Period = MatchPeriod.Regulation;
        fixture.Scene.Match.MatchTime = 0;
        fixture.Scene.Match.TeamPoints[1] = 4;
        fixture.Scene.Match.Logic.ProcessMode();
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.Equal(0, fixture.Scene.Match.MatchTime);
        Assert.Equal(MatchEndReason.ScoreGoal, fixture.Scene.Match.PendingEndReason);
    }

    [Fact]
    public void LifecycleDoesNotReapplyRegulationDeadlineDuringOvertime()
    {
        MatchRuntime match = new(new MatchRules(MatchMode.Battle, "unit", timeLimit: TimeSpan.FromSeconds(1)));
        MatchLifecycle lifecycle = new(match);
        lifecycle.AdvanceBeforeStep(10, true, () => { });
        lifecycle.AdvanceBeforeStep(190, true, () => { });
        uint revision = match.PhaseRevision;
        match.Period = MatchPeriod.SuddenDeath;
        match.PeriodStartTick = 250;
        match.MatchTime = -1;
        match.HasPhaseDeadline = false;
        lifecycle.AdvanceBeforeStep(1000, true, () => { });
        Assert.Equal(MatchPhase.Playing, match.Phase);
        Assert.Equal(revision, match.PhaseRevision);
        Assert.Equal(-1, match.MatchTime);
    }

    [Fact]
    public void ReplicaAndNonPlayingPhasesCannotAuthorOvertime()
    {
        using Fixture fixture = new(MatchMode.Battle);
        foreach (MatchPhase phase in Enum.GetValues<MatchPhase>())
        {
            if (phase == MatchPhase.Playing) { continue; }
            fixture.Scene.Match.Phase = phase;
            MatchOvertime.Evaluate(fixture.Scene, true, null);
            Assert.Equal(MatchPeriod.Regulation, fixture.Scene.Match.Period);
        }
        fixture.Scene.Match.Phase = MatchPhase.Playing;
        fixture.Scene.Services = new ReplicaServices();
        MatchOvertime.Evaluate(fixture.Scene, true, null);
        Assert.Equal(MatchPeriod.Regulation, fixture.Scene.Match.Period);
    }

    private sealed class ReplicaServices : ISceneServices { public bool IsReplica => true; }

    [Fact]
    public void CliAndRulesValidatePoliciesAndPreserveCompatibilityDefaults()
    {
        Assert.Equal(OvertimePolicy.Disabled, ServerOvertimeOptions.Parse(null));
        Assert.Equal(OvertimePolicy.ModeDefault, ServerOvertimeOptions.Parse("mode", true));
        Assert.Throws<ArgumentException>(() => ServerOvertimeOptions.Parse(null, true));
        Assert.Throws<ArgumentException>(() => ServerOvertimeOptions.Parse("1", true));
        MatchRules manual = new(MatchMode.Survival, "unit");
        Assert.Equal(OvertimePolicy.Disabled, manual.OvertimePolicy);
        Assert.Equal(LateJoinPolicy.JoinImmediately, manual.LateJoinPolicy);
        Assert.Equal(LateJoinPolicy.SpectateUntilNextMatch, MatchRules.CreateDefault(MatchMode.Survival, "unit").LateJoinPolicy);
        Assert.Equal(LateJoinPolicy.SpectateUntilNextMatch, MatchRules.CreateDefault(MatchMode.TeamSurvival, "unit").LateJoinPolicy);
        Assert.Equal(LateJoinPolicy.JoinImmediately, MatchRules.CreateDefault(MatchMode.Battle, "unit").LateJoinPolicy);
        Assert.Throws<ArgumentOutOfRangeException>(() => manual.With(overtimePolicy: (OvertimePolicy)255));
        Assert.Throws<ArgumentOutOfRangeException>(() => manual.With(lateJoinPolicy: (LateJoinPolicy)255));
        MatchRules changed = manual.With(overtimePolicy: OvertimePolicy.ModeDefault, lateJoinPolicy: LateJoinPolicy.Disabled).With(roomKey: "other");
        Assert.Equal(OvertimePolicy.ModeDefault, changed.OvertimePolicy);
        Assert.Equal(LateJoinPolicy.Disabled, changed.LateJoinPolicy);
    }

    private sealed class Fixture : IDisposable
    {
        public Scene Scene { get; } = Scene.CreateHeadless();
        public Fixture(MatchMode mode)
        {
            Scene.Match.ApplyRules(new MatchRules(mode, "unit", startingLives: 2, overtimePolicy: OvertimePolicy.ModeDefault));
            Scene.Match.Phase = MatchPhase.Playing;
            Scene.Match.PhaseRevision = 17;
            Scene.Match.MatchTime = 0;
            Scene.Match.HasPhaseDeadline = true;
            Scene.Match.PhaseEndTick = 123;
            for (int slot = 0; slot < 2; slot++)
            {
                PlayerEntity player = PlayerEntity.Players[slot];
                player.TeamIndex = slot;
                player.Health = 99;
                player.LoadFlags = LoadFlags.Active | LoadFlags.Initial;
                Scene.InsertEntity(player);
            }
        }
        public void Dispose() => Scene.CloseHeadless();
    }
}
