using System;
using Xunit;

namespace MphRead.Tests;

public sealed class MatchLifecycleTests
{
    [Fact]
    public void CountdownDropoutRequiresAnotherResetAndOnlyPlayingConsumesTheClock()
    {
        var match = new MatchRuntime(new MatchRules(MatchMode.Battle, "room", timeLimit: TimeSpan.FromSeconds(6)));
        var lifecycle = new MatchLifecycle(match);
        int resets = 0;
        Action reset = () => resets++;
        lifecycle.AdvanceBeforeStep(9, false, reset);
        Assert.Equal(MatchPhase.WaitingForPlayers, match.Phase);
        Assert.Equal(6, match.MatchTime);
        uint waitingRevision = match.PhaseRevision;
        lifecycle.AdvanceBeforeStep(10, true, reset);
        Assert.Equal(MatchPhase.Countdown, match.Phase);
        Assert.Equal(190u, match.PhaseEndTick);
        Assert.Equal(1, resets);
        Assert.NotEqual(waitingRevision, match.PhaseRevision);
        lifecycle.AdvanceBeforeStep(100, false, reset);
        Assert.Equal(MatchPhase.WaitingForPlayers, match.Phase);
        Assert.False(match.HasPhaseDeadline);
        Assert.Equal(6, match.MatchTime);
        lifecycle.AdvanceBeforeStep(101, true, reset);
        Assert.Equal(2, resets);
        lifecycle.AdvanceBeforeStep(280, true, reset);
        Assert.Equal(MatchPhase.Countdown, match.Phase);
        lifecycle.AdvanceBeforeStep(281, true, reset);
        Assert.Equal(MatchPhase.Playing, match.Phase);
        Assert.Equal(6, match.MatchTime);
        Assert.Equal(641u, match.PhaseEndTick);
        // Once playing, population loss does not pause the authoritative clock.
        lifecycle.AdvanceBeforeStep(341, false, reset);
        Assert.Equal(5, match.MatchTime);
        lifecycle.AdvanceBeforeStep(641, false, reset);
        Assert.Equal(0, match.MatchTime);
        match.Phase = MatchPhase.Ending; // MatchFlow finishes scoring and captures the result.
        lifecycle.ObserveCompletion(641);
        Assert.Equal(821u, match.PhaseEndTick);
        lifecycle.AdvanceBeforeStep(820, false, reset);
        Assert.Equal(MatchPhase.Ending, match.Phase);
        lifecycle.AdvanceBeforeStep(821, false, reset);
        Assert.Equal(MatchPhase.Intermission, match.Phase);
        Assert.Equal(1121u, match.PhaseEndTick);
        lifecycle.AdvanceBeforeStep(1120, false, reset);
        Assert.False(lifecycle.RotationDue);
        lifecycle.AdvanceBeforeStep(1121, false, reset);
        Assert.True(lifecycle.RotationDue);
    }

    [Fact]
    public void DeadlinesAndInputEpochsRemainValidAcrossTickAndRevisionWrap()
    {
        var match = new MatchRuntime(new MatchRules(MatchMode.Battle, "room"));
        var lifecycle = new MatchLifecycle(match, UInt32.MaxValue - 91);
        match.PhaseRevision = UInt32.MaxValue;
        uint start = UInt32.MaxValue - 90;
        lifecycle.AdvanceBeforeStep(start, true, () => { });
        Assert.Equal(1u, match.PhaseRevision);
        lifecycle.AdvanceBeforeStep(unchecked(start + 179), true, () => { });
        Assert.Equal(MatchPhase.Countdown, match.Phase);
        lifecycle.AdvanceBeforeStep(unchecked(start + 180), true, () => { });
        Assert.Equal(MatchPhase.Playing, match.Phase);
        Assert.Equal(2u, match.PhaseRevision);
        Assert.False(match.HasPhaseDeadline);
        Assert.Equal(-1, match.MatchTime);
        lifecycle.AdvanceBeforeStep(100000, false, () => { });
        Assert.Equal(MatchPhase.Playing, match.Phase);
        Assert.Equal(-1, match.MatchTime);
    }

    [Fact]
    public void UnsupportedDurationIsRejectedBeforeChangingRuntimeOwnershipOrPhase()
    {
        var match = new MatchRuntime(new MatchRules(MatchMode.Battle, "room", timeLimit: TimeSpan.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MatchLifecycle(match));
        Assert.Equal(MatchPhase.Playing, match.Phase);
        Assert.Equal(1u, match.PhaseRevision);
        Assert.False(match.UsesServerLifecycle);
    }

    [Fact]
    public void AFailedResetNeverPublishesCountdownAndAnExplicitEndIsNotOverwritten()
    {
        var match = new MatchRuntime(new MatchRules(MatchMode.Battle, "room", timeLimit: TimeSpan.FromMinutes(1)));
        var lifecycle = new MatchLifecycle(match);
        uint revision = match.PhaseRevision;
        Assert.Throws<InvalidOperationException>(() => lifecycle.AdvanceBeforeStep(10, true,
            () => throw new InvalidOperationException("reset failed")));
        Assert.Equal(MatchPhase.WaitingForPlayers, match.Phase);
        Assert.Equal(revision, match.PhaseRevision);
        lifecycle.AdvanceBeforeStep(11, true, () => { });
        lifecycle.AdvanceBeforeStep(191, true, () => { });
        match.MatchTime = 0;
        lifecycle.AdvanceBeforeStep(192, true, () => { });
        Assert.Equal(0, match.MatchTime);
    }

    [Fact]
    public void CompetitiveResetClearsEverySlotAndTeamCounterWithoutReplacingRules()
    {
        MatchRules rules = new MatchRules(MatchMode.PrimeHunter, "room", playerRadar: true);
        var match = new MatchRuntime(rules);
        match.Players[3].Points = 4;
        match.Players[3].KillStreak = 5;
        match.Players[3].Time = 9;
        match.Players[3].SetBeamKills(8, 7);
        match.TeamPoints[3] = 4;
        match.TeamDeaths[3] = 3;
        match.TeamTime[3] = 9;
        match.ResultSlots[0] = 3;
        match.PrimeHunter = 3;
        match.ForceEndGame = true;
        match.ResetCompetitiveState();
        Assert.Same(rules, match.Rules);
        Assert.Equal(0, match.Players[3].Points);
        Assert.Equal(0, match.Players[3].KillStreak);
        Assert.Equal(0, match.Players[3].Time);
        Assert.Equal(0, match.Players[3].GetBeamKills(8));
        Assert.Equal(0, match.TeamPoints[3]);
        Assert.Equal(0, match.TeamDeaths[3]);
        Assert.Equal(0, match.TeamTime[3]);
        Assert.Equal(0, match.ResultSlots[0]);
        Assert.Equal(-1, match.PrimeHunter);
        Assert.False(match.ForceEndGame);
        Assert.True(match.RadarPlayers);
    }
}
