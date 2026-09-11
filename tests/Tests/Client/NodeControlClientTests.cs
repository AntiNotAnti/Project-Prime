using System;
using System.Collections.Immutable;
using System.Text;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class NodeControlClientTests
{
    private static MatchCompletionSummary Completion(Guid matchId, Guid? reportId = null)
        => new(new(matchId), new(Guid.NewGuid()), MatchEndReason.ScoreGoal,
            [new(Guid.NewGuid(), new PlayerId(Guid.NewGuid()), ParticipantKind.RegisteredHuman,
                "Hunter", ParticipantOutcome.Finished, 0, 0, 7, 3, 1)],
            Guid.NewGuid(), null, reportId ?? Guid.NewGuid());

    [Fact]
    public async Task ImmutableNodeCompletionIsRelevantValidatedAndIdempotent()
    {
        Guid nodeId = Guid.NewGuid(), matchId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Hunter", nodeId, new string('a', 43))));
        var handoff = new NodeMatchHandoff(matchId, 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus);
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 2, null, handoff));
        client.MarkGameplayJoined(matchId);
        MatchCompletionSummary summary = Completion(matchId);

        client.ApplyEvent(NodeControlCodec.Write("match.completion", 3, null, new NodeMatchCompletion(summary)));
        client.ApplyEvent(NodeControlCodec.Write("match.completion", 4, null, new NodeMatchCompletion(summary)));

        Assert.Equal(summary.ReportId, client.CompletionSummaryFor(matchId)!.ReportId);
        Assert.False(client.CompletionFor(matchId)!.Interrupted);
        Assert.True(client.ShouldReturnFromGameplay);
        Assert.Throws<JsonException>(() => client.ApplyEvent(NodeControlCodec.Write("match.completion", 5, null,
            new NodeMatchCompletion(Completion(matchId)))));
    }
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
    public async Task MatchingLobbyLeftClearsJoinedStateButPreservesCompletionHistory()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid(), playerId = Guid.NewGuid();
        Guid lobbyId = Guid.NewGuid(), matchId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, playerId, "Hunter", nodeId, new string('a', 43));
        var lobby = new LobbySnapshot(lobbyId, "Room", LobbyVisibility.Public, sessionId,
            LobbyPhase.StartingMatch, 1, 8, 16,
            [new LobbyMember(sessionId, playerId, "Hunter", Hunter.Samus, 0, false, false)], [],
            CurrentMatchId: matchId);
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, lobby));
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 3, null,
            new NodeMatchHandoff(matchId, 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus)));
        client.MarkGameplayJoined(matchId);
        client.ApplyEvent(NodeControlCodec.Write("match.ended", 4, null, new NodeMatchEnded(matchId, false)));
        MatchCompletionSummary summary = Completion(matchId);
        client.ApplyEvent(NodeControlCodec.Write("match.completion", 5, null, new NodeMatchCompletion(summary)));

        client.ApplyEvent(NodeControlCodec.Write("lobby.left", 6, null, new LobbyLeft(lobbyId)));

        Assert.Null(client.Lobby);
        Assert.Null(client.Round);
        Assert.Null(client.Handoff);
        Assert.False(client.MatchEnded);
        Assert.Null(client.State.JoinedMatchId);
        Assert.Null(client.State.JoinedCompletion);
        Assert.Null(client.State.JoinedCompletionSummary);
        Assert.Equal(matchId, client.State.LastEndedMatchId);
        Assert.Equal(summary.ReportId, client.State.LastCompletionSummary!.ReportId);
    }

    [Fact]
    public async Task StaleLobbyLeftCannotClearAReplacementLobbyOrJoinedState()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid(), playerId = Guid.NewGuid();
        Guid firstLobbyId = Guid.NewGuid(), secondLobbyId = Guid.NewGuid(), matchId = Guid.NewGuid(), secondMatchId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, playerId, "Hunter", nodeId, new string('a', 43));
        var first = new LobbySnapshot(firstLobbyId, "First", LobbyVisibility.Public, sessionId,
            LobbyPhase.StartingMatch, 1, 8, 16,
            [new LobbyMember(sessionId, playerId, "Hunter", Hunter.Samus, 0, false, false)], [],
            CurrentMatchId: matchId);
        var second = first with { LobbyId = secondLobbyId, Name = "Second", Phase = LobbyPhase.Open,
            Revision = first.Revision + 1, CurrentMatchId = null };
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 2, null, first));
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 3, null,
            new NodeMatchHandoff(matchId, 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus)));
        client.MarkGameplayJoined(matchId);
        client.ApplyEvent(NodeControlCodec.Write("lobby.snapshot", 4, null, second));
        client.ApplyEvent(NodeControlCodec.Write("match.handoff", 5, null,
            new NodeMatchHandoff(secondMatchId, 2, "127.0.0.1", 5000, "ticket", 2, false, Hunter.Samus)));
        client.MarkGameplayJoined(secondMatchId);

        client.ApplyEvent(NodeControlCodec.Write("lobby.left", 6, null, new LobbyLeft(firstLobbyId)));

        Assert.Equal(secondLobbyId, client.Lobby!.LobbyId);
        Assert.Equal(secondMatchId, client.State.JoinedMatchId);
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

    [Fact]
    public async Task HostileV2SnapshotWithConflictingRuleProjectionIsRejected()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        var valid = new LobbySnapshot(Guid.NewGuid(), "Room", LobbyVisibility.Public, sessionId,
            LobbyPhase.Open, 1, 8, 16,
            [new LobbyMember(sessionId, session.PlayerId, "Hunter", Hunter.Samus, 0, false, false)], [],
            "unit", MatchMode.Battle, TimeLimitSeconds: 600, PointGoal: 11,
            Rules: new LobbyRulesOptions(TimeLimitSeconds: 600, ScoreGoal: 11));
        JsonElement payload = JsonSerializer.SerializeToElement(valid with { TimeLimitSeconds = 601 },
            NodeJsonContext.Default.LobbySnapshot);

        var error = Assert.Throws<JsonException>(() => client.ApplyEvent(RawEvent("lobby.snapshot", 2, payload)));
        Assert.Equal("Invalid lobby snapshot.", error.Message);
        Assert.Null(client.Lobby);
    }

    [Fact]
    public async Task HostileV2ListWithModeSpecificMetadataIsRejected()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        var invalid = new LobbyListSnapshot([
            new LobbyListEntry(Guid.NewGuid(), "Room", LobbyPhase.Open, 0, 8, 0, 1,
                MapKey: "unit", Mode: MatchMode.Defender, PointGoal: 1)
        ], null);
        JsonElement payload = JsonSerializer.SerializeToElement(invalid, NodeJsonContext.Default.LobbyListSnapshot);

        var error = Assert.Throws<JsonException>(() => client.ApplyEvent(RawEvent("lobby.list", 2, payload)));
        Assert.Equal("Invalid lobby list.", error.Message);
        Assert.Null(client.Lobbies);
    }

    [Fact]
    public async Task HostileV2ListOverMaximumEntriesIsRejected()
    {
        Guid nodeId = Guid.NewGuid(), sessionId = Guid.NewGuid();
        await using var client = new NodeControlClient(nodeId);
        var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(), "Hunter", nodeId, new string('a', 43));
        client.ApplyEvent(NodeControlCodec.Write("node.session", 1, null, session));
        var rows = Enumerable.Range(0, NodeControlCodec.MaximumLobbyListEntries + 1)
            .Select(_ => new LobbyListEntry(Guid.NewGuid(), "Room", LobbyPhase.Open, 0, 8, 0, 1))
            .ToImmutableArray();
        JsonElement payload = JsonSerializer.SerializeToElement(new LobbyListSnapshot(rows, null),
            NodeJsonContext.Default.LobbyListSnapshot);

        var error = Assert.Throws<JsonException>(() => client.ApplyEvent(RawEvent("lobby.list", 2, payload)));
        Assert.Equal("Invalid lobby list.", error.Message);
        Assert.Null(client.Lobbies);
    }

    private static byte[] RawEvent(string type, long eventId, JsonElement payload)
        => JsonSerializer.SerializeToUtf8Bytes(new NodeControlEvent(NodeControlCodec.Version, type,
            eventId, null, payload), NodeJsonContext.Default.NodeControlEvent);
}
