using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MphRead;
using ProjectPrime.Server.Node;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Presence;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodePresenceReporterTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 8, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Handler : HttpMessageHandler
    {
        public readonly List<(HttpMethod Method, string Path, string? Scheme,
            string? Credential, string Node, JsonElement Body)> Requests = [];
        public readonly Queue<HttpStatusCode> Results = [];
        public bool Block { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Block) await Task.Delay(Timeout.Infinite, cancellationToken);
            JsonElement body = request.Content == null
                ? default
                : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken))
                    .RootElement.Clone();
            Requests.Add((request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Scheme, request.Headers.Authorization?.Parameter,
                request.Headers.GetValues("X-Server-Id").Single(), body));
            return new(Results.TryDequeue(out HttpStatusCode status)
                ? status : HttpStatusCode.OK);
        }
    }

    private static NodePresenceReporterOptions Options(Guid? nodeId = null,
        Guid? incarnation = null)
        => new(nodeId ?? Guid.NewGuid(), new Uri("https://backend.example/"),
            incarnation ?? Guid.NewGuid(), "us-central", "presence-test-credential");

    [Fact]
    public async Task PublishesOnlySanitizedPlayersAndKeepsTotalSeparate()
    {
        var options = Options();
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var snapshot = new NodePresenceSnapshot(7, 3,
            ImmutableArray.Create(new NodePresenceEntry("Visible", PlayerPresenceActivity.InLobby)));
        using var reporter = new NodePresenceReporter(options, () => snapshot,
            NullLogger<NodePresenceReporter>.Instance, http, new Clock());

        Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(TimeSpan.FromSeconds(2), reporter.NextDelay());
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/v1/node/presence", request.Path);
        Assert.Equal("Bearer", request.Scheme);
        Assert.Equal("presence-test-credential", request.Credential);
        Assert.Equal(options.NodeId.ToString("D"), request.Node);
        Assert.Equal(options.Incarnation, request.Body.GetProperty("incarnation").GetGuid());
        Assert.Equal(7, request.Body.GetProperty("revision").GetInt64());
        JsonElement player = Assert.Single(request.Body.GetProperty("players").EnumerateArray());
        Assert.Equal("Visible", player.GetProperty("displayName").GetString());
        Assert.Equal((byte)PlayerPresenceActivity.InLobby,
            player.GetProperty("activity").GetByte());
        Assert.DoesNotContain("sessionId", request.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("playerId", request.Body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoalescesBurstsKeepsAliveAndReportsAfterRegistrationReturns()
    {
        var clock = new Clock();
        var options = Options();
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        bool registered = true;
        var snapshot = new NodePresenceSnapshot(1, 1,
            ImmutableArray.Create(new NodePresenceEntry("Pilot", PlayerPresenceActivity.Online)));
        using var reporter = new NodePresenceReporter(options, () => snapshot,
            NullLogger<NodePresenceReporter>.Instance, http, clock,
            registered: () => registered);

        Assert.True(await reporter.PublishOnceAsync());
        snapshot = snapshot with { Revision = 2 };
        clock.Now = clock.Now.AddSeconds(1.99);
        Assert.False(await reporter.PublishOnceAsync());
        clock.Now = clock.Now.AddSeconds(.01);
        Assert.True(await reporter.PublishOnceAsync());

        clock.Now = clock.Now.AddSeconds(14.99);
        Assert.False(await reporter.PublishOnceAsync());
        clock.Now = clock.Now.AddSeconds(.01);
        Assert.True(await reporter.PublishOnceAsync());

        registered = false;
        clock.Now = clock.Now.AddSeconds(1);
        Assert.False(await reporter.PublishOnceAsync());
        registered = true;
        // Re-registration is a new publication boundary even if the session
        // revision has not changed since the last successful report.
        Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal(15, options.KeepaliveInterval.TotalSeconds);
        Assert.Equal(2, options.MinimumReportInterval.TotalSeconds);
    }

    [Fact]
    public async Task HealthyCadenceDoesNotDelayKeepalivePastItsDeadline()
    {
        var clock = new Clock();
        var options = Options();
        using var handler = new Handler();
        using var http = new HttpClient(handler);
        var snapshot = new NodePresenceSnapshot(1, 1,
            ImmutableArray.Create(new NodePresenceEntry("Pilot", PlayerPresenceActivity.Online)));
        using var reporter = new NodePresenceReporter(options, () => snapshot,
            NullLogger<NodePresenceReporter>.Instance, http, clock);

        Assert.True(await reporter.PublishOnceAsync());
        clock.Now = clock.Now.AddSeconds(14);
        Assert.Equal(TimeSpan.FromSeconds(1), reporter.NextDelay());
        clock.Now = clock.Now.AddSeconds(1);
        Assert.True(await reporter.PublishOnceAsync());
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task ReadinessAndCancellationBoundPublication()
    {
        var options = Options();
        using var handler = new Handler { Block = true };
        using var http = new HttpClient(handler);
        bool ready = false;
        var clock = new Clock();
        var timeoutOptions = new NodePresenceReporterOptions(options.NodeId, options.Backend,
            options.Incarnation, options.Region, "presence-test-credential")
        { RequestTimeout = TimeSpan.FromMilliseconds(100) };
        using var reporter = new NodePresenceReporter(timeoutOptions,
            () => new NodePresenceSnapshot(1, 0, ImmutableArray<NodePresenceEntry>.Empty),
            NullLogger<NodePresenceReporter>.Instance, http, clock,
            readiness: () => ready ? NodeReadinessResult.Ready
                : new(false, true, false, "worker_unready"));

        Assert.False(await reporter.PublishOnceAsync());
        Assert.Empty(handler.Requests);
        ready = true;
        Assert.False(await reporter.PublishOnceAsync());
        Assert.Equal("timeout", reporter.LastFailure);
        Assert.Equal(1, reporter.ConsecutiveFailures);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reporter.PublishOnceAsync(cancellation.Token));
    }

    [Fact]
    public void LobbyProjectionDerivesOnlineLobbyAndMatchStates()
    {
        var lobbies = new LobbyManager();
        Guid sessionId = Guid.NewGuid();
        var identity = new LobbyIdentity(sessionId, Guid.NewGuid(), "Pilot");
        var snapshot = (LobbySnapshot)lobbies.Execute(identity,
            new LobbyCreate("Arena", LobbyVisibility.Public, 2, 0));
        Assert.Equal(PlayerPresenceActivity.InLobby,
            lobbies.SnapshotPresenceActivities()[sessionId]);

        snapshot = (LobbySnapshot)lobbies.Execute(identity,
            new LobbyConfigure(snapshot.Revision, "unit", MatchMode.Battle));
        snapshot = (LobbySnapshot)lobbies.Execute(identity,
            new LobbySetReady(true, snapshot.Revision));
        MatchSpec spec = lobbies.PrepareMatch(sessionId, snapshot.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());
        // StartingMatch is already authoritative match activity while the
        // Worker is completing the handoff.
        Assert.Equal(PlayerPresenceActivity.InMatch,
            lobbies.SnapshotPresenceActivities()[sessionId]);

        Assert.True(lobbies.MatchEnded(spec.MatchId, interrupted: true));
        Assert.Equal(PlayerPresenceActivity.InLobby,
            lobbies.SnapshotPresenceActivities()[sessionId]);

        snapshot = lobbies.ForSession(sessionId)!;
        snapshot = (LobbySnapshot)lobbies.Execute(identity,
            new LobbyConfigure(snapshot.Revision, "unit", MatchMode.Battle));
        snapshot = (LobbySnapshot)lobbies.Execute(identity,
            new LobbySetReady(true, snapshot.Revision));
        spec = lobbies.PrepareMatch(sessionId, snapshot.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(PlayerPresenceActivity.InMatch,
            lobbies.SnapshotPresenceActivities()[sessionId]);

        Assert.True(lobbies.MatchEnded(spec.MatchId, interrupted: false));
        Assert.Equal(PlayerPresenceActivity.InLobby,
            lobbies.SnapshotPresenceActivities()[sessionId]);
        LobbySnapshot postMatch = lobbies.ForSession(sessionId)!;
        NodeRoundSnapshot round = lobbies.RoundForSession(sessionId)!;
        byte returnOption = Assert.Single(round.Options,
            option => option.Choice == LobbyVoteChoice.ReturnToLobby).Id;
        lobbies.Execute(identity, new LobbyVoteCast(postMatch.Revision,
            round.BallotRevision, returnOption));
        Assert.Equal(PlayerPresenceActivity.InLobby,
            lobbies.SnapshotPresenceActivities()[sessionId]);
    }

    [Fact]
    public async Task VisibilityCommandIsAcknowledgedAndDisconnectResumeUpdatesProjection()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = new Uri(host.App.Urls.Single().Replace("https:", "wss:") + "/v1/control");
        NodeSessionManager manager = host.App.Services.GetRequiredService<NodeSessionManager>();
        NodePresenceSnapshot initial = manager.CreatePresenceSnapshot();
        Assert.Equal(0, initial.Revision);
        Assert.Equal(0, initial.TotalSessions);

        using var socket = Socket("Bearer " + host.Ticket(Guid.NewGuid(), publicPresence: false));
        await socket.ConnectAsync(uri, timeout.Token);
        using var welcome = await Read(socket, timeout.Token);
        JsonElement welcomePayload = welcome.RootElement.GetProperty("payload");
        string resumeToken = welcomePayload.GetProperty("resumeToken").GetString()!;
        Guid sessionId = welcomePayload.GetProperty("sessionId").GetGuid();
        await Until(() => manager.Count == 1, timeout.Token);

        NodePresenceSnapshot hidden = manager.CreatePresenceSnapshot();
        Assert.Equal(1, hidden.TotalSessions);
        Assert.Empty(hidden.Players);
        long before = hidden.Revision;
        Assert.False(manager.TrySetPresenceVisibility(Guid.NewGuid(), true, out _));
        Assert.Equal(before, manager.CreatePresenceSnapshot().Revision);

        Guid requestId = Guid.NewGuid();
        await Send(socket, requestId, new { visible = true }, timeout.Token);
        using var acknowledged = await Read(socket, timeout.Token);
        Assert.Equal("node.presence.visibility", acknowledged.RootElement.GetProperty("type").GetString());
        Assert.Equal(requestId, acknowledged.RootElement.GetProperty("requestId").GetGuid());
        Assert.True(acknowledged.RootElement.GetProperty("payload").GetProperty("visible").GetBoolean());
        long acknowledgedRevision = acknowledged.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();
        NodePresenceSnapshot visible = manager.CreatePresenceSnapshot();
        Assert.Equal(acknowledgedRevision, visible.Revision);
        Assert.Equal(1, visible.TotalSessions);
        Assert.Contains(visible.Players, player => player.DisplayName == "Player");

        Guid noOpRequest = Guid.NewGuid();
        await Send(socket, noOpRequest, new { visible = true }, timeout.Token);
        using var noOp = await Read(socket, timeout.Token);
        Assert.Equal(noOpRequest, noOp.RootElement.GetProperty("requestId").GetGuid());
        Assert.Equal(acknowledgedRevision,
            noOp.RootElement.GetProperty("payload").GetProperty("revision").GetInt64());

        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
        await Until(() => manager.Count == 0, timeout.Token);
        NodePresenceSnapshot disconnected = manager.CreatePresenceSnapshot();
        Assert.Empty(disconnected.Players);
        Assert.True(disconnected.Revision > acknowledgedRevision);

        using var resumed = Socket("Resume " + resumeToken);
        await resumed.ConnectAsync(uri, timeout.Token);
        using var resumedWelcome = await Read(resumed, timeout.Token);
        Assert.Equal(sessionId, resumedWelcome.RootElement.GetProperty("payload")
            .GetProperty("sessionId").GetGuid());
        await Until(() => manager.Count == 1, timeout.Token);
        NodePresenceSnapshot restored = manager.CreatePresenceSnapshot();
        Assert.Contains(restored.Players, player => player.DisplayName == "Player");
        await resumed.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
    }

    private static ClientWebSocket Socket(string authorization)
    {
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.SetRequestHeader("Authorization", authorization);
        return socket;
    }

    private static async Task Send(ClientWebSocket socket, Guid requestId,
        object payload, CancellationToken cancellationToken)
    {
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = NodeControlCodec.Version,
            requestId,
            type = "node.presence.visibility",
            payload
        });
        await socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task<JsonDocument> Read(ClientWebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[NodeControlCodec.MaximumFrameBytes];
        int length = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, length,
                buffer.Length - length), cancellationToken);
            length += result.Count;
        }
        while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.AsMemory(0, length));
    }

    private static async Task Until(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }
}
