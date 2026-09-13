using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Network;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

public sealed class PostMatchLeaveTests
{
    private static LobbySnapshot Lobby() => new(Guid.NewGuid(), "Room", LobbyVisibility.Public,
        Guid.NewGuid(), LobbyPhase.PostMatch, 1, 8, 16, [], []);
    private static NodeControlEvent Error(string code) => new(NodeControlCodec.Version, "error", 1, null,
        JsonSerializer.SerializeToElement(new NodeControlError(code, code), NodeJsonContext.Default.NodeControlError));
    private static NodeControlEvent Left(Guid lobby) => new(NodeControlCodec.Version, "lobby.left", 2, null,
        JsonSerializer.SerializeToElement(new LobbyLeft(lobby), NodeJsonContext.Default.LobbyLeft));

    [Fact]
    public async Task StaleLeaveRetriesWithLatestRevisionAndWaitsForAcknowledgement()
    {
        var lobby = Lobby();
        var revisions = new List<long>();
        var ack = new TaskCompletionSource<NodeControlEvent>();
        Task leave = PlayController.LeaveLobbyWithRetryAsync(() => lobby, (request, _) =>
        {
            revisions.Add(request.ExpectedRevision);
            if (revisions.Count == 1) { lobby = lobby with { Revision = 2 }; return Task.FromResult(Error("stale_revision")); }
            return ack.Task;
        });
        Assert.False(leave.IsCompleted);
        Assert.Equal(new long[] { 1, 2 }, revisions);
        ack.SetResult(Left(lobby.LobbyId));
        await leave;
    }

    [Fact]
    public async Task RepeatedStaleReplyIsTerminalAfterExactlyOneRetry()
    {
        var lobby = Lobby();
        int requests = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlayController.LeaveLobbyWithRetryAsync(() => lobby,
            (_, _) => { requests++; lobby = lobby with { Revision = lobby.Revision + 1 }; return Task.FromResult(Error("stale_revision")); }));
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task WrongLobbyAcknowledgementCannotCompleteLeave()
    {
        var lobby = Lobby();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PlayController.LeaveLobbyWithRetryAsync(() => lobby,
            (_, _) => Task.FromResult(Left(Guid.NewGuid()))));
    }

    [Fact]
    public async Task LobbyLeftInvalidatesCurrentLobbyAndCachedDirectory()
    {
        Guid nodeId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid playerId = Guid.NewGuid();
        Guid lobbyId = Guid.NewGuid();
        await using var node = new NodeControlClient(nodeId);
        node.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(sessionId, playerId, "Hunter", nodeId,
                new string('a', 43))));
        node.ApplyEvent(NodeControlCodec.Write("lobby.list", 2, null,
            new LobbyListSnapshot(ImmutableArray.Create(
                new LobbyListEntry(lobbyId, "Room", LobbyPhase.Open,
                    1, 8, 0, 1)), null)));
        node.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 3, null,
            new LobbySnapshot(lobbyId, "Room", LobbyVisibility.Public,
                sessionId, LobbyPhase.Open, 1, 8, 16,
                ImmutableArray.Create(new LobbyMember(sessionId, playerId,
                    "Hunter", Hunter.Samus, 0, false, false)), [])));

        Assert.NotNull(node.Lobby);
        Assert.NotNull(node.Lobbies);

        node.ApplyEvent(NodeControlCodec.Write("lobby.left", 4, null,
            new LobbyLeft(lobbyId)));

        Assert.Null(node.Lobby);
        Assert.Null(node.Lobbies);
    }
}
