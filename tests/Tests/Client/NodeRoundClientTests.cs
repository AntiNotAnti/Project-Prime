using System;
using System.Collections.Immutable;
using System.Text.Json;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

public sealed class NodeRoundClientTests
{
    private static NodeRoundSnapshot Round(Guid session, Guid player)
    {
        var lobby = new LobbySnapshot(Guid.NewGuid(), "Room", LobbyVisibility.Public, session,
            LobbyPhase.PostMatch, 4, 8, 16,
            [new LobbyMember(session, player, "Hunter", Hunter.Samus, 0, false, false)], [], "MP1 SANCTORUS");
        return new(lobby, null, null, false, false, 1, 1, DateTimeOffset.UtcNow.AddSeconds(15),
            [new(1, LobbyVoteChoice.Rematch, lobby.MapKey, MatchMode.Battle, 0)], 0);
    }

    [Fact]
    public async Task RoundPushUsesCodecValidationAndPreservesResolvedVoteAgainstLateReply()
    {
        Guid node = Guid.NewGuid(), session = Guid.NewGuid(), player = Guid.NewGuid();
        await using var client = new NodeControlClient(node);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(session, player, "Hunter", node, new string('a', 43))));
        var round = Round(session, player);
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, round.Lobby));
        client.ApplyEvent(NodeControlCodec.Write("lobby.round", 3, null, round));
        Assert.Equal(round.BallotRevision, client.Round!.BallotRevision);
        var resolved = round with { OwnVote = 1, ResolvedOption = round.Options[0] };
        client.ApplyEvent(NodeControlCodec.Write("lobby.round", 4, null, resolved));
        client.ApplyEvent(NodeControlCodec.Write("lobby.round", 5, null, round));
        Assert.NotNull(client.Round.ResolvedOption);
        Assert.Equal((byte)1, client.Round.OwnVote);
        client.ApplyEvent(NodeControlCodec.Write("lobby.left", 6, null, new LobbyLeft(round.Lobby.LobbyId)));
        Assert.Null(client.Round);
    }

    [Fact]
    public async Task MalformedRoundPayloadCannotReachViewState()
    {
        Guid node = Guid.NewGuid(), session = Guid.NewGuid(), player = Guid.NewGuid();
        await using var client = new NodeControlClient(node);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(session, player, "Hunter", node, new string('a', 43))));
        var round = Round(session, player);
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, round.Lobby));
        var payload = JsonSerializer.SerializeToElement(round with { OwnVote = 8 }, NodeJsonContext.Default.NodeRoundSnapshot);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new NodeControlEvent(NodeControlCodec.Version,
            "lobby.round", 3, null, payload), NodeJsonContext.Default.NodeControlEvent);
        Assert.Throws<JsonException>(() => client.ApplyEvent(bytes));
        Assert.Null(client.Round);
    }

    [Fact]
    public void OldConfigurationBallotAndForeignLobbyCannotOverwriteRound()
    {
        var round = Round(Guid.NewGuid(), Guid.NewGuid());
        var state = new NodeControlClient.ViewState(Lobby: round.Lobby, Round: round);
        Assert.Same(state, NodeControlClient.AcceptRound(state, round with { ConfigurationRevision = 0 }));
        Assert.Same(state, NodeControlClient.AcceptRound(state, round with { BallotRevision = 0 }));
        Assert.Same(state, NodeControlClient.AcceptRound(state, round with { Lobby = round.Lobby with { LobbyId = Guid.NewGuid() } }));
        Assert.Throws<JsonException>(() => NodeControlClient.AcceptRound(state,
            round with { Lobby = round.Lobby with { MapKey = "changed" } }));
    }

    [Fact]
    public void FreshHandoffWinsOverCompletionOfPreviousRound()
    {
        var round = Round(Guid.NewGuid(), Guid.NewGuid());
        Guid completed = Guid.NewGuid();
        var handoff = new NodeMatchHandoff(Guid.NewGuid(), 2, "127.0.0.1", 5000, "ticket", 2, false, Hunter.Samus);
        var state = new NodeControlClient.ViewState(Lobby: round.Lobby, Handoff: handoff,
            LastEndedMatchId: completed, LastMatchInterrupted: false, Round: round);
        Assert.Equal(PostMatchTransition.Continue, PostMatchFlow.Evaluate(state, completed));
        Assert.Equal(PostMatchTransition.Wait, PostMatchFlow.Evaluate(state with { Handoff = handoff with { MatchId = completed } }, completed));
        Assert.Equal(PostMatchTransition.Lobby, PostMatchFlow.Evaluate(state with { Handoff = null, Lobby = round.Lobby with { Phase = LobbyPhase.Open } }, completed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturningOpenClearsHandoffButPreservesCompletedWorkerHistory(bool roundPush)
    {
        Guid node = Guid.NewGuid(), session = Guid.NewGuid(), player = Guid.NewGuid(), match = Guid.NewGuid();
        await using var client = new NodeControlClient(node);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(session, player, "Hunter", node, new string('a', 43))));
        var round = Round(session, player);
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, round.Lobby));
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 3, null,
            new NodeMatchHandoff(match, 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus)));
        client.MarkGameplayJoined(match);
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 4, null, new NodeMatchEnded(match, false)));
        var open = round.Lobby with { Revision = 5, Phase = LobbyPhase.Open, CurrentMatchId = null };
        if (roundPush)
            client.ApplyEvent(NodeControlCodec.Write("lobby.round", 5, null, round with
                { Lobby = open, Options = [], VoteDeadline = null, ResolvedOption = round.Options[0] }));
        else client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 5, null, open));
        Assert.Null(client.Handoff);
        Assert.Null(client.State.JoinedMatchId);
        Assert.False(client.MatchEnded);
        Assert.Equal(match, client.CompletionFor(match)!.MatchId);
    }

    [Fact]
    public async Task PlacementFailureWithoutHandoffIsRecordedAfterLobbyReturnsOpen()
    {
        Guid node = Guid.NewGuid(), session = Guid.NewGuid(), player = Guid.NewGuid(), match = Guid.NewGuid();
        await using var client = new NodeControlClient(node);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(session, player, "Hunter", node, new string('a', 43))));
        var lobby = Round(session, player).Lobby with { Phase = LobbyPhase.StartingMatch, CurrentMatchId = match };
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, lobby));
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 3, null, lobby with { Phase = LobbyPhase.Open, Revision = 5, CurrentMatchId = null }));
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 4, null, new NodeMatchEnded(match, true)));
        Assert.Equal(match, client.State.LastEndedMatchId);
        Assert.True(client.State.LastMatchInterrupted);
        Assert.True(client.MatchEnded);
    }
}
