using System;
using System.Collections.Immutable;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class PostMatchResultsTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-09T12:00:00Z");

    [Fact]
    public void MissingResultStaysExplicitlyUnavailable()
    {
        PostMatchResultsModel model = PostMatchResultsBuilder.Build(null, localSlot: 0);

        Assert.False(model.HasAuthoritativeResult);
        Assert.Equal(PostMatchOutcome.Unknown, model.Outcome);
        Assert.Equal("RESULTS UNAVAILABLE", model.OutcomeLabel);
        Assert.Empty(model.Scoreboard);
        Assert.False(model.HasLocalPlayer);
    }

    [Fact]
    public void NodeCompletionFallbackPresentsOnlyImmutableAvailableCounters()
    {
        PlayerId local = new(Guid.NewGuid());
        var completion = new MatchCompletionSummary(new(Guid.NewGuid()), new(Guid.NewGuid()),
            MatchEndReason.ScoreGoal,
            [
                new(Guid.NewGuid(), local, ParticipantKind.RegisteredHuman, "Local",
                    ParticipantOutcome.Finished, 0, 0, 9, 4, 1, Slot: 2,
                    Hunter: Hunter.Kanden, TeamIndex: 0),
                new(Guid.NewGuid(), new PlayerId(Guid.NewGuid()), ParticipantKind.RegisteredHuman, "Other",
                    ParticipantOutcome.Finished, 1, 1, 5, 2, 4, Slot: 5,
                    Hunter: Hunter.Trace, TeamIndex: 1)
            ], Guid.NewGuid(), null, Guid.NewGuid());
        var snapshot = new MatchResultsSnapshot("fallback-map", GameMode.Battle, null,
            completion, local);

        PostMatchResultsModel model = PostMatchResultsBuilder.Build(snapshot);

        Assert.True(model.HasAuthoritativeResult);
        Assert.Equal(PostMatchOutcome.Victory, model.Outcome);
        Assert.Equal("Score goal", model.EndReasonLabel);
        Assert.Equal(2, model.Scoreboard.Length);
        PostMatchScoreRow row = Assert.Single(model.Scoreboard, row => row.IsLocal);
        Assert.Equal((9, 4, 1), (row.Score, row.Kills, row.Deaths));
        Assert.False(row.HasDetailedStats);
    }

    [Fact]
    public void ScoreboardUsesFrozenCountersAndExactLocalSlot()
    {
        using var state = new MatchBaselineTests.State();
        var match = state.Configure(GameMode.Battle);
        state.Activate(0, 0);
        state.Activate(1, 1);
        match.Points[0] = 9;
        match.Points[1] = 3;
        match.Kills[0] = 4;
        match.Deaths[0] = 1;
        match.Assists[0] = 2;
        match.DamageDealt[0] = 321;
        match.HeadshotKills[0] = 3;
        match.LongestKillStreak[0] = 4;
        match.Logic.UpdateState();
        match.CaptureResult(12);
        var snapshot = new MatchResultsSnapshot("fallback-map", GameMode.Battle, match.Result!);

        PostMatchResultsModel model = PostMatchResultsBuilder.Build(snapshot, localSlot: 0);
        Assert.True(model.HasAuthoritativeResult);
        Assert.Equal("match-baseline", model.MapKey);
        Assert.Equal(MatchMode.Battle, model.Mode);
        Assert.Equal(PostMatchOutcome.Victory, model.Outcome);
        Assert.Equal(2, model.Scoreboard.Length);
        PostMatchScoreRow local = model.Scoreboard[0];
        Assert.True(local.IsLocal);
        Assert.Equal(0, local.Slot);
        Assert.Equal(9, local.Score);
        Assert.Equal(4, local.Kills);
        Assert.Equal(1, local.Deaths);
        Assert.Equal(2, local.Assists);
        Assert.Equal(321, local.Damage);
        Assert.Equal(3, local.HeadshotKills);
        Assert.Equal(4, local.LongestKillStreak);
        Assert.False(model.Scoreboard[1].IsLocal);

        Assert.Equal(PostMatchOutcome.Unknown,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 7).Outcome);
    }

    [Fact]
    public void TeamOutcomeUsesCapturedPlayerStandings()
    {
        using var state = new MatchBaselineTests.State();
        var match = state.Configure(GameMode.BattleTeams);
        state.Activate(0, 0);
        state.Activate(1, 1);
        match.Points[0] = 10;
        match.Points[1] = 1;
        match.Logic.UpdateState();
        match.CaptureResult(12);
        var snapshot = new MatchResultsSnapshot("team-map", GameMode.BattleTeams, match.Result!);

        Assert.Equal(PostMatchOutcome.Victory,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 0).Outcome);
        Assert.Equal(PostMatchOutcome.Defeat,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 1).Outcome);
    }

    [Fact]
    public void EqualCapturedTeamMetricsRenderDraw()
    {
        using var state = new MatchBaselineTests.State();
        var match = state.Configure(GameMode.BattleTeams);
        state.Activate(0, 0);
        state.Activate(1, 1);
        match.Points[0] = match.Points[1] = 10;
        match.Deaths[0] = match.Deaths[1] = 2;
        match.Logic.UpdateState();
        match.CaptureResult(12);
        var snapshot = new MatchResultsSnapshot("team-tie", GameMode.BattleTeams, match.Result!);

        Assert.Equal(PostMatchOutcome.Draw,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 0).Outcome);
        Assert.Equal(PostMatchOutcome.Draw,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 1).Outcome);
    }

    [Fact]
    public void CapturedMultiPlayerTeamRanksRemainConsistent()
    {
        using var state = new MatchBaselineTests.State();
        var match = state.Configure(GameMode.BattleTeams);
        state.Activate(0, 0);
        state.Activate(1, 1);
        state.Activate(2, 0);
        state.Activate(4, 0);
        state.Activate(5, 1);
        match.Points[0] = match.Points[2] = match.Points[4] = 10;
        match.Logic.UpdateState();
        match.CaptureResult(12);
        var snapshot = new MatchResultsSnapshot("team-contradiction", GameMode.BattleTeams, match.Result!);

        Assert.Equal(PostMatchOutcome.Victory,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 0).Outcome);
        Assert.Equal(PostMatchOutcome.Defeat,
            PostMatchResultsBuilder.Build(snapshot, localSlot: 1).Outcome);
    }

    [Fact]
    public void BallotProjectionExposesVoteTotalsLeadTieAndAuthoritativeDeadline()
    {
        var options = ImmutableArray.Create(
            new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "same-map", MatchMode.Battle, 4),
            new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "next-map", MatchMode.Battle, 4),
            new LobbyVoteEntry(3, LobbyVoteChoice.ReturnToLobby, "", MatchMode.Battle, 1));
        var round = new NodeRoundSnapshot(null!, null, null, false, false, 1, 7,
            Now.AddSeconds(2.25), options, OwnVote: 2);

        PostMatchBallotModel model = PostMatchBallotModel.From(round, Now);

        Assert.Equal(PostMatchBallotState.Voted, model.State);
        Assert.Equal((byte)2, model.OwnVote);
        Assert.Equal(4, model.LeadingVotes);
        Assert.True(model.IsTied);
        Assert.Equal((byte)1, model.LeadingOption!.Id);
        Assert.Equal(TimeSpan.FromSeconds(2.25), model.TimeRemaining);
        Assert.True(model.HasAuthoritativeDeadline);
        Assert.False(model.CanVote);
        Assert.Contains("Your vote: Next map", model.StatusText);
        Assert.Contains("locked", model.StatusText);
        Assert.Contains("Tied for lead", model.LeadingText);
    }

    [Fact]
    public void BallotOptionsAreBoundedToEightAndExpiredVoteIsClosed()
    {
        var builder = ImmutableArray.CreateBuilder<LobbyVoteEntry>(10);
        for (byte id = 1; id <= 10; id++)
            builder.Add(new LobbyVoteEntry(id, LobbyVoteChoice.Map, $"map-{id}", MatchMode.Battle, id));
        var round = new NodeRoundSnapshot(null!, null, null, false, false, 1, 8,
            Now.AddSeconds(-1), builder.MoveToImmutable(), OwnVote: 0);

        PostMatchBallotModel model = PostMatchBallotModel.From(round, Now);

        Assert.Equal(PostMatchBallotState.Closed, model.State);
        Assert.False(model.CanVote);
        Assert.Equal(8, model.Options.Length);
        Assert.Equal((byte)8, model.Options[^1].Id);
        Assert.Equal("Voting closed.", model.StatusText);
    }
}
