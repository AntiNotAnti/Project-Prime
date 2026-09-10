using System;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class NodeControlClientTests
{
    [Theory]
    [InlineData(false, AuthoritativePlay.TerminalState.Completed)]
    [InlineData(true, AuthoritativePlay.TerminalState.Failed)]
    public async Task MatchingNodeCompletionStopsWorkerWithoutDisconnectingControl(bool interrupted,
        AuthoritativePlay.TerminalState expected)
    {
        var field = typeof(NodeSessions).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        Guid nodeId = Guid.NewGuid(), matchId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Hunter", nodeId, new string('a', 43))));
        client.MarkGameplayJoined(matchId);
        try
        {
            field.SetValue(null, client);
            using var worker = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
            Assert.False(worker.ObserveCompletion());
            client.ApplyEvent(NodeControlCodec.Write("match.ended", 2, null, new NodeMatchEnded(Guid.NewGuid(), false)));
            Assert.False(worker.ObserveCompletion());
            client.ApplyEvent(NodeControlCodec.Write("match.ended", 3, null, new NodeMatchEnded(matchId, interrupted)));
            Assert.True(worker.ObserveCompletion());
            Assert.Equal(expected, worker.State);
            Assert.Equal(interrupted, worker.Interrupted);
            Assert.Same(client, NodeSessions.Current);
        }
        finally { field.SetValue(null, previous); }
    }

    [Fact]
    public async Task InterruptedCompletionAfterNewHandoffStillMatchesJoinedWorker()
    {
        Guid nodeId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        var first = new NodeMatchHandoff(Guid.NewGuid(), 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus);
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 2, null, first));
        client.MarkGameplayJoined(first.MatchId);
        var next = first with { MatchId = Guid.NewGuid(), WireMatchId = 2 };
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 3, null, next));
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 4, null, new NodeMatchEnded(first.MatchId, true)));
        Assert.True(client.CompletionFor(first.MatchId)!.Interrupted);
        Assert.False(client.MatchEnded);
        Assert.Equal(next, client.Handoff); // PlayController can still join this handoff.
        Assert.Null(client.CompletionFor(next.MatchId));
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 5, null, new NodeMatchEnded(next.MatchId, false)));
        Assert.True(client.CompletionFor(first.MatchId)!.Interrupted);
        Assert.Equal(session, client.Session);
    }

    [Fact]
    public async Task WorkerCleanupDoesNotReplaceNodeOrLobby()
    {
        var field = typeof(NodeSessions).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        var lobby = new LobbySnapshot(Guid.NewGuid(), "Room", LobbyVisibility.Public, sessionId,
            LobbyPhase.Open, 1, 8, 16, [new LobbyMember(sessionId, session.PlayerId, "Hunter", Hunter.Samus, 0, false, false)], []);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, lobby));
        try
        {
            field.SetValue(null, client);
            var worker = new AuthoritativePlay("127.0.0.1", 5000, "Hunter", Hunter.Samus);
            NetSession.Stop();
            Assert.Null(AuthoritativePlay.Current);
            Assert.Same(client, NodeSessions.Current);
            Assert.Equal(session, client.Session);
            Assert.Equal(lobby.LobbyId, client.Lobby!.LobbyId);
        }
        finally { field.SetValue(null, previous); }
    }

    [Fact]
    public async Task AdvertisedMapCatalogIsSnapshottedOnSetAndRead()
    {
        await using var client = new NodeControlClient(Guid.NewGuid());
        string[] source = ["zeta", "alpha"];
        client.SetAdvertisedMapKeys(source);

        source[0] = "mutated-source";
        Assert.Equal(new[] { "zeta", "alpha" }, client.AdvertisedMapKeys);

        string[] returned = client.AdvertisedMapKeys!;
        returned[1] = "mutated-return";
        Assert.Equal(new[] { "zeta", "alpha" }, client.AdvertisedMapKeys);
    }

    [Fact]
    public async Task GreetingPinsNodeAndOrderedSnapshotsRetainSessionIdentity()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        Assert.Equal(session, client.Session);
        client.ApplyEvent(NodeControlCodec.Write("lobby.list", 2, null, new LobbyListSnapshot([], null)));
        Assert.NotNull(client.Lobbies);
        Assert.Equal(sessionId, client.Session!.SessionId);
        Assert.Throws<JsonException>(() => client.ApplyEvent(NodeControlCodec.Write("lobby.list", 2, null, new LobbyListSnapshot([], null))));
        Assert.Throws<JsonException>(() => client.ApplyEvent(NodeControlCodec.Write("lobby.list", 4, null, new LobbyListSnapshot([], null))));
    }
    [Fact]
    public async Task GuestSessionAndLobbyMembersRetainGuestIdentity()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid(), guestId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, null, "Guest", nodeId, new string('a', 43), guestId);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        var lobby = new LobbySnapshot(Guid.NewGuid(), "Room", LobbyVisibility.Public, sessionId, LobbyPhase.Open, 1, 8, 16,
            [new LobbyMember(sessionId, null, "Guest", Hunter.Samus, 0, false, false, guestId)], []);

        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, lobby));

        Assert.Equal(guestId, client.Session!.GuestSessionId);
        Assert.Null(client.Session.PlayerId);
        Assert.Equal(guestId, client.Lobby!.Members[0].GuestSessionId);
        Assert.Null(client.Lobby.Members[0].PlayerId);
    }
    [Fact]
    public async Task MatchReturnOnlyClosesCurrentHandoffAndRematchKeepsSameSession()
    {
        Guid nodeId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        var first = new NodeMatchHandoff(Guid.NewGuid(), 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus);
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 2, null, first));
        client.MarkGameplayJoined(first.MatchId);
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 3, null, new NodeMatchEnded(Guid.NewGuid(), false)));
        Assert.False(client.MatchEnded);
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 4, null, new NodeMatchEnded(first.MatchId, false)));
        Assert.True(client.MatchEnded);
        var second = first with { MatchId = Guid.NewGuid(), WireMatchId = 2 };
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 5, null, second));
        Assert.False(client.MatchEnded); Assert.Equal(second, client.Handoff); Assert.Equal(session, client.Session);
        Assert.True(client.ShouldReturnFromGameplay); // A rapid rematch cannot erase the old renderer's return signal.
        client.MarkGameplayJoined(second.MatchId);
        Assert.False(client.ShouldReturnFromGameplay);
    }
    [Fact]
    public async Task StaleLobbyCannotOverwriteCurrentRevisionAndSubscriberFailureDoesNotBreakControl()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        client.Changed += () => throw new InvalidOperationException("test listener");
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Hunter", nodeId, new string('a', 43))));
        var lobby = new LobbySnapshot(Guid.NewGuid(), "Room", LobbyVisibility.Public, sessionId, LobbyPhase.Open, 3, 8, 16,
            [new LobbyMember(sessionId, Guid.NewGuid(), "Hunter", Hunter.Samus, 0, false, false)], []);
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, lobby));
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 3, null, lobby with { Revision = 2, Name = "stale" }));
        Assert.Equal(3, client.Lobby!.Revision); Assert.Equal("Room", client.Lobby.Name);
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 4, null, lobby));
        Assert.Equal(3, client.State.Lobby!.Revision);
        Assert.Equal("A Node view listener failed.", client.Error);
    }
    [Fact]
    public async Task CommandQueueIsBoundedAndDisposeCancelsWaitersIdempotently()
    {
        var client = new NodeControlClient();
        var gate = (SemaphoreSlim)typeof(NodeControlClient).GetField("_send", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(client)!;
        await gate.WaitAsync(); // Deterministically hold the writer; no socket/timing dependency.
        var pending = Enumerable.Range(0, 32).Select(_ => client.SendAsync("node.ping", new NodePing())).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("node.ping", new NodePing()));
        await Task.WhenAll(client.DisposeAsync().AsTask(), client.DisposeAsync().AsTask());
        foreach (var send in pending) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.SendAsync("node.ping", new NodePing()));
    }
    [Fact]
    public async Task WrongNodeDuplicatePropertiesAndEventsBeforeGreetingFailClosed()
    {
        await using var client = new NodeControlClient(Guid.NewGuid());
        Assert.Throws<JsonException>(() => client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Hunter", Guid.NewGuid(), new string('a', 43)))));
        await using var clean = new NodeControlClient(Guid.NewGuid());
        Assert.Throws<JsonException>(() => clean.ApplyEvent(NodeControlCodec.Write("lobby.list", 1, null, new LobbyListSnapshot([], null))));
        Assert.Throws<JsonException>(() => clean.ApplyEvent(Encoding.UTF8.GetBytes("{\"version\":1,\"version\":1}")));
    }
}
