using System.Collections.Immutable;
using MphRead.Entities;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class KillcamTests
{
    [Fact]
    public void CaptureWindowUsesExactLeadTailAndSaturates()
    {
        Assert.Equal(new KillcamCaptureWindow(100, 445),
            KillcamController.GetCaptureWindow(400));
        Assert.Equal(new KillcamCaptureWindow(0, 45),
            KillcamController.GetCaptureWindow(0));
        Assert.Equal(uint.MaxValue,
            KillcamController.GetCaptureWindow(uint.MaxValue - 10).End);
        Assert.Equal(uint.MaxValue,
            KillcamController.SaturatingAdd(uint.MaxValue - 10, 45));
    }

    [Fact]
    public void PendingCaptureCannotLaunchFromTheKillFrameFallback()
    {
        PendingKillcamCapture immediate = Pending(KillcamPolicy.Immediate);
        Assert.False(KillcamController.IsCaptureReady(immediate,
            hasFinalClip: false, MatchPhase.Playing));
        Assert.True(KillcamController.IsCaptureReady(immediate,
            hasFinalClip: true, MatchPhase.Playing));

        PendingKillcamCapture postRound = Pending(KillcamPolicy.PostRound);
        Assert.False(KillcamController.IsCaptureReady(postRound,
            hasFinalClip: true, MatchPhase.Playing));
        Assert.False(KillcamController.IsCaptureReady(postRound,
            hasFinalClip: true, MatchPhase.Countdown));
        Assert.True(KillcamController.IsCaptureReady(postRound,
            hasFinalClip: true, MatchPhase.Ending));
        Assert.True(KillcamController.IsCaptureReady(postRound,
            hasFinalClip: true, MatchPhase.Intermission));
    }

    [Fact]
    public void CaptureFallbackBoundIsInclusiveAndFinite()
    {
        Assert.False(KillcamController.CaptureFallbackExpired(
            KillcamController.CaptureFallbackFrames - 1));
        Assert.True(KillcamController.CaptureFallbackExpired(
            KillcamController.CaptureFallbackFrames));
        Assert.True(KillcamController.CaptureFallbackExpired(uint.MaxValue));
    }

    [Fact]
    public void IdentityHelperFencesConnectionReuseButAllowsPostRoundNewLife()
    {
        CombatActor killed = new(2, 202, 4);
        CombatActor sameConnectionNewLife = new(2, 202, 5);
        CombatActor replacementConnection = new(2, 303, 5);

        Assert.False(AuthoritativePlay.KillcamIdentityChanged(
            sameConnectionNewLife, killed, includeNewLife: false));
        Assert.True(AuthoritativePlay.KillcamIdentityChanged(
            sameConnectionNewLife, killed, includeNewLife: true));
        Assert.True(AuthoritativePlay.KillcamIdentityChanged(
            replacementConnection, killed, includeNewLife: false));
        Assert.True(AuthoritativePlay.KillcamIdentityChanged(
            CombatActor.None, killed, includeNewLife: false));
    }

    [Fact]
    public void FocusUsesStableChaseViewForEveryKillSource()
    {
        KillEvent enemy = new(1, 10, 1, 1,
            new CombatActor(1, 101, 3), new CombatActor(2, 202, 4), 0,
            KillEventFlags.Headshot, ImmutableArray<CombatActor>.Empty);
        Assert.Equal(enemy.Killer, KillcamController.ResolveFocusActor(enemy));
        Assert.Equal(SpectatorCameraMode.Chase,
            KillcamController.ResolveFocusMode(enemy));

        KillEvent suicide = new(2, 11, 1, 1,
            new CombatActor(2, 202, 4), new CombatActor(2, 202, 4), 0,
            KillEventFlags.Suicide, ImmutableArray<CombatActor>.Empty);
        Assert.Equal(suicide.Victim, KillcamController.ResolveFocusActor(suicide));
        Assert.Equal(SpectatorCameraMode.Chase,
            KillcamController.ResolveFocusMode(suicide));

        KillEvent environment = new(3, 12, 1, 1, CombatActor.None,
            new CombatActor(2, 202, 4), 255, KillEventFlags.None,
            ImmutableArray<CombatActor>.Empty, KillSourceKind.Environment);
        Assert.Equal(environment.Victim,
            KillcamController.ResolveFocusActor(environment));
        Assert.Equal(SpectatorCameraMode.Chase,
            KillcamController.ResolveFocusMode(environment));

        KillEvent invalidVictim = default;
        Assert.Equal(SpectatorCameraMode.Chase,
            KillcamController.ResolveFocusMode(invalidVictim));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void AuthoritativeCombatVisualsRequireANonHeadlessReplica(
        bool headless, bool replica, bool expected)
    {
        Assert.Equal(expected,
            PlayerPresentation.CanPresentAuthoritativeCombat(headless, replica));
    }

    [Fact]
    public void ProgressClampsAndHandlesDegenerateClip()
    {
        Assert.Equal(0, KillcamController.Progress(100, 100, 145));
        Assert.Equal(.5f, KillcamController.Progress(122, 100, 144));
        Assert.Equal(1, KillcamController.Progress(200, 100, 145));
        Assert.Equal(1, KillcamController.Progress(10, 10, 10));
        Assert.Equal(0, KillcamController.Progress(9, 10, 10));
    }

    [Fact]
    public void FinalReplayRequiresSameTickAndAuthoritativeFfaWinner()
    {
        MatchEvent ended = Ended(9_000);
        MatchResult result = Result(MatchMode.Battle, MatchEndReason.ScoreGoal,
            winner: 1);
        KillEvent kill = Kill(7, 9_000);

        Assert.True(KillcamController.IsFinalReplayEligible(kill, ended, result));
        Assert.False(KillcamController.IsFinalReplayEligible(
            kill with { Tick = 8_999 }, ended, result));
        Assert.False(KillcamController.IsFinalReplayEligible(
            kill, ended, Result(MatchMode.Battle, MatchEndReason.TimeLimit, 1)));
        Assert.False(KillcamController.IsFinalReplayEligible(
            kill, ended, Result(MatchMode.Battle, MatchEndReason.ScoreGoal, 2)));
        Assert.False(KillcamController.IsFinalReplayEligible(kill, ended, null));
    }

    [Fact]
    public void FinalReplayRejectsSuicideTeamkillAndEnvironmentFacts()
    {
        MatchEvent ended = Ended(9_000);
        MatchResult result = Result(MatchMode.Battle, MatchEndReason.ScoreGoal, 1);
        Assert.False(KillcamController.IsFinalReplayEligible(
            Kill(7, 9_000) with { Flags = KillEventFlags.TeamKill }, ended, result));
        KillEvent suicide = new(8, 9_000, 1, 2,
            new CombatActor(2, 202, 4), new CombatActor(2, 202, 4),
            0, KillEventFlags.Suicide, ImmutableArray<CombatActor>.Empty);
        Assert.False(KillcamController.IsFinalReplayEligible(suicide, ended, result));
        KillEvent environment = Kill(9, 9_000) with
        {
            Killer = CombatActor.None, Weapon = 255,
            SourceKind = KillSourceKind.Environment
        };
        Assert.False(KillcamController.IsFinalReplayEligible(environment, ended, result));
    }

    [Fact]
    public void FinalReplayRequiresEnemyWinningTeamForTeamModes()
    {
        MatchEvent ended = Ended(9_000);
        KillEvent kill = Kill(7, 9_000);
        MatchResult team = Result(MatchMode.TeamBattle,
            MatchEndReason.ScoreGoal, winner: 1, teams: new[] { 1, 0, 1, 0 });
        Assert.True(KillcamController.IsFinalReplayEligible(kill, ended, team));
        Assert.False(KillcamController.IsFinalReplayEligible(kill, ended,
            Result(MatchMode.TeamBattle, MatchEndReason.ScoreGoal, winner: 2,
                teams: new[] { 1, 0, 1, 0 })));
        Assert.True(KillcamController.IsFinalReplayEligible(kill, ended,
            Result(MatchMode.Survival, MatchEndReason.Survival, winner: 1)));
        Assert.Equal("GAME OVER", KillcamController.GetFinalWinnerLabel(null));
        Assert.Equal("WINNER: P2", KillcamController.GetFinalWinnerLabel(
            Result(MatchMode.Battle, MatchEndReason.ScoreGoal, winner: 1,
                names: new[] { "P1", "P2" })));
        Assert.Equal("WINNER: TEAM 2", KillcamController.GetFinalWinnerLabel(
            Result(MatchMode.TeamBattle, MatchEndReason.ScoreGoal, winner: 0,
                teams: new[] { 1, 0, 1, 0 })));
    }

    private static PendingKillcamCapture Pending(KillcamPolicy policy)
        => new(Kill(400), policy, 400, 100, 445, null!);

    private static KillEvent Kill(uint id, uint tick = 9_000)
        => new(id, tick, 1, 2, new CombatActor(1, 101, 3),
            new CombatActor(2, 202, 4), 0, KillEventFlags.None,
            ImmutableArray<CombatActor>.Empty);

    private static MatchEvent Ended(uint tick)
        => new(7, tick, 1, 2, MatchEventKind.MatchEnded,
            CombatActor.None, CombatActor.None);

    private static MatchResult Result(MatchMode mode, MatchEndReason reason,
        int winner, int[]? teams = null, string[]? names = null)
    {
        bool teamMode = mode is MatchMode.TeamBattle or MatchMode.TeamSurvival;
        var rules = new MatchRules(mode, "final-test", maxPlayers: 4,
            scoreGoal: mode is MatchMode.Battle or MatchMode.TeamBattle ? 7 : 0,
            startingLives: mode is MatchMode.Survival or MatchMode.TeamSurvival ? 2 : 0,
            teamCount: teamMode ? 2 : 1);
        var runtime = new MatchRuntime(rules) { MatchId = 1 };
        runtime.ResultSlots[0] = winner;
        var identities = new PlayerResultIdentity[PlayerEntity.SlotCapacity];
        for (int slot = 0; slot < identities.Length; slot++)
        {
            identities[slot] = new(Hunter.Samus,
                teams != null && slot < teams.Length ? teams[slot] : 0,
                Active: slot < 4,
                names != null && slot < names.Length ? names[slot] : $"P{slot + 1}");
        }
        return new MatchResult(runtime, 0, reason, identities);
    }
}
