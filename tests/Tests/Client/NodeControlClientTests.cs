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

public sealed class NodeControlClientTests
{
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
