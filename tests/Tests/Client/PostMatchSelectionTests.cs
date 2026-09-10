using System;
using System.Collections.Immutable;
using FruityPrime.Server.Shared;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

public sealed class PostMatchSelectionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-09T12:00:00Z");
    private static NodeRoundSnapshot Round(uint revision = 1, byte ownVote = 0) => new(
        null!, null, null, false, false, 1, revision, Now.AddSeconds(10),
        ImmutableArray.Create(new LobbyVoteEntry(4, LobbyVoteChoice.Rematch, "map", MatchMode.Battle, 0),
            new LobbyVoteEntry(8, LobbyVoteChoice.ReturnToLobby, "", MatchMode.Battle, 0)), ownVote);

    [Fact]
    public void StatusKeepsLockedCountdownAndDistinguishesLobbyReturn()
    {
        Assert.Contains("10s", PostMatchView.Status(Round(ownVote: 4), Now));
        Assert.Contains("locked", PostMatchView.Status(Round(ownVote: 4), Now));
        Assert.Equal("Returning to lobby…", PostMatchView.Status(Round() with { ResolvedOption = Round().Options[1] }, Now));
        Assert.Equal("Tournament paused.", PostMatchView.Status(Round() with { Paused = true, ResolvedOption = Round().Options[0] }, Now));
        Assert.Equal("Tournament ended.", PostMatchView.Status(Round() with { Paused = true, TournamentEnded = true }, Now));
    }

    [Fact]
    public void MovementAndPointerSelectionChooseServerIdsAndLockPending()
    {
        var selection = new PostMatchSelection();
        selection.Update(Round(), Now);
        selection.Move(-1);
        Assert.Equal(1, selection.SelectedIndex);
        Assert.Equal((byte)8, selection.Choose());
        selection.Update(Round(), Now);
        Assert.Null(selection.Choose());
        selection.RejectPending();
        selection.Select(0);
        Assert.Equal((byte)4, selection.Choose());
    }

    [Fact]
    public void NewBallotUnlocksButOwnVoteAndExpiredBallotsStayLocked()
    {
        var selection = new PostMatchSelection();
        selection.Update(Round(ownVote: 4), Now);
        Assert.Null(selection.Choose());
        selection.Update(Round(2), Now);
        Assert.Equal((byte)4, selection.Choose());
        selection.Update(Round(3), Now.AddSeconds(10));
        Assert.Null(selection.Choose());
        selection.Update(null, Now);
        Assert.Null(selection.Choose());
    }

    [Fact]
    public void InvalidPointerIndexCannotChangeSelection()
    {
        var selection = new PostMatchSelection();
        selection.Update(Round(), Now);
        selection.Select(8);
        Assert.Equal(0, selection.SelectedIndex);
        selection.Update(Round() with { ResolvedOption = Round().Options[0] }, Now);
        Assert.True(selection.Locked);
    }
}
