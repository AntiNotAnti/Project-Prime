using System;
using System.Linq;
using Avalonia.Controls;
using FruityPrime.Server.Shared;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class PostMatchReviewTests
{
    [Fact]
    public void ClosingGameWindowOverridesEvenReadyContinuation()
    {
        var state = new NodeControlClient.ViewState(Handoff:
            new NodeMatchHandoff(Guid.NewGuid(), 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus));
        Assert.Equal(PostMatchTransition.Quit, PostMatchFlow.Evaluate(state, Guid.NewGuid(), gameWindowOpen: false));
    }

    [Fact]
    public void LeavePreemptsVoteWithoutCancelingItsOwnRequest()
    {
        using var scope = new PostMatchCommandScope();
        Assert.True(scope.TryBeginVote(out var vote));
        Assert.True(scope.TryBeginLeave(out var leave));
        Assert.True(vote.IsCancellationRequested);
        Assert.False(leave.IsCancellationRequested);
        Assert.False(scope.TryBeginVote(out _));
        Assert.False(scope.TryBeginLeave(out _));
        scope.CompleteVote();
        scope.CompleteLeave();
        Assert.True(scope.TryBeginLeave(out var retry));
        scope.Dispose();
        Assert.True(retry.IsCancellationRequested);
    }

    [Fact]
    public void BallotAndScoreboardHaveIndependentConstrainedScrollRegions()
    {
        using var view = new PostMatchView(null);
        var zones = Assert.IsType<Grid>(view.Content);
        Assert.Equal(2, zones.ColumnDefinitions.Count);
        var score = Assert.IsType<Grid>(zones.Children[0]);
        var vote = Assert.IsType<Grid>(zones.Children[1]);
        Assert.Equal(1, Grid.GetColumn(vote));
        Assert.True(score.RowDefinitions[1].Height.IsStar);
        Assert.True(vote.RowDefinitions[2].Height.IsStar);
        Assert.Single(score.Children.OfType<ScrollViewer>());
        Assert.Single(vote.Children.OfType<ScrollViewer>());
        Assert.DoesNotContain(vote.Children.OfType<ScrollViewer>(), scroll => scroll.Content is Grid);
        Assert.Equal(5, vote.RowDefinitions.Count); // Heading, countdown, ballot scroll, hints, Leave.
    }

    [Fact]
    public void WinningCardIsMarkedIndependentlyOfLocalVote()
    {
        var winner = new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "map", MatchMode.Battle, 4);
        Assert.Contains("NEXT MATCH", PostMatchView.CardHeading(winner, 0, 2, winner));
        var returned = winner with { Choice = LobbyVoteChoice.ReturnToLobby };
        Assert.Contains("RETURNING TO LOBBY", PostMatchView.CardHeading(returned, 0, 0, returned));
        Assert.DoesNotContain("NEXT MATCH", PostMatchView.CardHeading(winner, 0, 1, returned with { Id = 2 }));
    }
}
