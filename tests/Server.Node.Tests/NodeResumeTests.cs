using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Worker;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Network;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Trait("LifecycleFast", "true")]
public sealed class NodeResumeTests
{
    private sealed class Clock : TimeProvider
    { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
    [Fact]
    public async Task ResumeRotatesSecretRestoresLobbyAndGraceExpiryReleasesMembership()
    {
        var clock = new Clock();
        await using var host = new NodeHostFixture(clock);
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = new Uri(host.App.Urls.Single().Replace("https:", "wss:") + "/v1/control");
        var manager = host.App.Services.GetRequiredService<NodeSessionManager>();
        using var first = Socket("Bearer " + host.Ticket(Guid.NewGuid()));
        await first.ConnectAsync(uri, timeout.Token);
        using var welcome = await Read(first, timeout.Token);
        var greeting = welcome.RootElement.GetProperty("payload");
        string token = greeting.GetProperty("resumeToken").GetString()!;
        Guid sessionId = greeting.GetProperty("sessionId").GetGuid();
        byte[] create = JsonSerializer.SerializeToUtf8Bytes(new { version = NodeControlCodec.Version, type = "lobby.create", requestId = Guid.NewGuid(), payload = new { name = "Resume", visibility = "Public" } });
        await first.SendAsync(create, WebSocketMessageType.Text, true, timeout.Token);
        using var lobby = await Read(first, timeout.Token);
        Guid lobbyId = lobby.RootElement.GetProperty("payload").GetProperty("lobbyId").GetGuid();
        await first.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
        while (!manager.CanResume(token)) await Task.Delay(10, timeout.Token);
        using var second = Socket("Resume " + token);
        await second.ConnectAsync(uri, timeout.Token);
        using var restored = await Read(second, timeout.Token);
        var payload = restored.RootElement.GetProperty("payload");
        Assert.Equal(sessionId, payload.GetProperty("sessionId").GetGuid());
        string rotated = payload.GetProperty("resumeToken").GetString()!;
        Assert.NotEqual(token, rotated); Assert.False(manager.CanResume(token));
        using var snapshot = await Read(second, timeout.Token);
        Assert.Equal(lobbyId, snapshot.RootElement.GetProperty("payload").GetProperty("lobbyId").GetGuid());
        await second.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test", timeout.Token);
        while (!manager.CanResume(rotated)) await Task.Delay(10, timeout.Token);
        clock.Now += TimeSpan.FromSeconds(46); manager.PruneExpired();
        Assert.False(manager.CanResume(rotated));
        Assert.Equal(0, host.App.Services.GetRequiredService<LobbyManager>().Count);
        await host.App.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task PausedSenderResumesSnapshotBeforeTerminalAndFreshHandoff()
    {
        var content = new ContentIdentity("unit", "hash", "1", "test", 8);
        WorkerLaunchOptions launch = WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) };
        await using var host = new NodeHostFixture(configure: builder =>
            builder.Configuration.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new
            {
                Node = new
                {
                    Maps = new[] { content },
                    Workers = new { Processes = new[] { launch } }
                }
            }))));
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Uri uri = new(host.App.Urls.Single().Replace("https:", "wss:") + "/v1/control");
        NodeSessionManager manager = host.App.Services.GetRequiredService<NodeSessionManager>();
        WorkerScheduler scheduler = host.App.Services.GetRequiredService<WorkerScheduler>();
        NodeMatchCoordinator coordinator = host.App.Services.GetRequiredService<NodeMatchCoordinator>();
        LobbyManager lobbies = host.App.Services.GetRequiredService<LobbyManager>();
        using var first = Socket("Bearer " + host.Ticket(Guid.NewGuid()));
        await first.ConnectAsync(uri, timeout.Token);
        using JsonDocument welcome = await Read(first, timeout.Token);
        JsonElement welcomePayload = welcome.RootElement.GetProperty("payload");
        Guid sessionId = welcomePayload.GetProperty("sessionId").GetGuid();
        Guid playerId = welcomePayload.GetProperty("playerId").GetGuid();
        string resumeToken = welcomePayload.GetProperty("resumeToken").GetString()!;

        using JsonDocument created = await SendCommand(first, "lobby.create", new { name = "Paused", visibility = "Public" }, timeout.Token);
        LobbySnapshot lobby = lobbies.ForSession(sessionId)!;
        using JsonDocument configured = await SendCommand(first, "lobby.configure", new { expectedRevision = lobby.Revision,
            mapKey = content.MapKey, mode = "Battle" }, timeout.Token);
        Assert.Equal("lobby.snapshot", configured.RootElement.GetProperty("type").GetString());
        lobby = lobbies.ForSession(sessionId)!;
        using JsonDocument ready = await SendCommand(first, "lobby.ready.set", new { ready = true, expectedRevision = lobby.Revision }, timeout.Token);
        Assert.Equal("lobby.snapshot", ready.RootElement.GetProperty("type").GetString());
        lobby = lobbies.ForSession(sessionId)!;
        var identity = new LobbyIdentity(sessionId, playerId, "Player");
        await coordinator.ExecuteAsync(identity, new LobbyStart(lobby.Revision));
        NodeMatchHandoff initial = Assert.IsType<NodeMatchHandoff>(
            coordinator.ForSession(sessionId));
        // Drain the first lifecycle over the real wire before disconnecting.
        // This keeps an initial sender/queue defect from being confused with
        // the paused-resume ordering under test.
        List<JsonDocument> initialFrames = [];
        while (initialFrames.Count < 8)
        {
            JsonDocument frame = await Read(first, timeout.Token);
            initialFrames.Add(frame);
            if (frame.RootElement.GetProperty("type").GetString() == "match.handoff")
                break;
        }
        Assert.Contains(initialFrames, frame =>
            frame.RootElement.GetProperty("type").GetString() == "lobby.snapshot");
        Assert.Contains(initialFrames, frame =>
            frame.RootElement.GetProperty("type").GetString() == "match.handoff"
            && frame.RootElement.GetProperty("payload").GetProperty("matchId").GetGuid() == initial.MatchId);
        foreach (JsonDocument frame in initialFrames) frame.Dispose();

        ClientWebSocket? resumed = null;
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Assert.True(scheduler.TrySendMatchAdmin(new(new(initial.MatchId),
                AdminAction.EndMatch, null)));

            // Drop the blocked transport, then resolve the rematch while the
            // same session is in reconnect grace. Its new handoff is a later
            // explicit credential mint, never passive resume replay.
            first.Abort();
            await WaitUntilAsync(() => manager.CanResume(resumeToken), timeout.Token);
            int sends = 0;
            manager.BeforeOutboundSend = (bytes, cancellationToken) =>
            {
                if (Interlocked.Increment(ref sends) >= 1)
                    blocked.TrySetResult();
                return new ValueTask(release.Task.WaitAsync(cancellationToken));
            };
            await WaitUntilAsync(() => coordinator.ForSessionEvents(sessionId)
                .OfType<NodeMatchCompletion>().Any(), timeout.Token);
            resumed = Socket("Resume " + resumeToken);
            await resumed.ConnectAsync(uri, timeout.Token);
            await blocked.Task.WaitAsync(timeout.Token);
            release.TrySetResult();
            List<JsonDocument> frames = [];
            while (frames.Count < 8)
            {
                JsonDocument frame = await Read(resumed, timeout.Token);
                frames.Add(frame);
                if (frames.Any(item => item.RootElement.GetProperty("type").GetString() == "match.completion")
                    && frames.Any(item => item.RootElement.GetProperty("type").GetString() == "match.ended"))
                    break;
            }
            string[] types = frames.Select(item => item.RootElement.GetProperty("type").GetString()!).ToArray();
            int snapshotIndex = Array.IndexOf(types, "lobby.snapshot");
            int completionIndex = Array.IndexOf(types, "match.completion");
            int endedIndex = Array.IndexOf(types, "match.ended");
            Assert.True(snapshotIndex >= 0 && completionIndex > snapshotIndex);
            Assert.True(endedIndex > snapshotIndex);
            long[] eventIds = frames.Select(item => item.RootElement.GetProperty("eventId").GetInt64()).ToArray();
            Assert.Equal(eventIds.OrderBy(value => value), eventIds);
            Assert.Equal(eventIds.Distinct().Count(), eventIds.Length);
            using JsonDocument ping = await SendCommand(resumed, "node.ping", new { }, timeout.Token);
            Assert.Equal("node.pong", ping.RootElement.GetProperty("type").GetString());

            // Resolve the rematch only after the reconstructive resume has
            // restored the terminal state; the disconnected host remains the
            // authoritative electorate member while in reconnect grace.
            NodeRoundSnapshot round = await WaitForRoundAsync(lobbies, sessionId, timeout.Token);
            LobbyVoteEntry rematch = round.Options.Single(option =>
                option.Choice == LobbyVoteChoice.Rematch);
            await coordinator.ExecuteAsync(identity, new LobbyVoteCast(round.Lobby.Revision,
                round.BallotRevision, rematch.Id));
            await WaitUntilAsync(() => coordinator.ForSession(sessionId) is NodeMatchHandoff handoff
                && handoff.MatchId != initial.MatchId, timeout.Token);
            Guid nextMatch = ((NodeMatchHandoff)coordinator.ForSession(sessionId)!).MatchId;
            List<JsonDocument> handoffFrames = [];
            try
            {
                while (handoffFrames.Count < 8)
                {
                    JsonDocument frame = await Read(resumed, timeout.Token);
                    handoffFrames.Add(frame);
                    if (frame.RootElement.GetProperty("type").GetString() == "match.handoff")
                        break;
                }
            }
            catch (WebSocketException error)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Resumed sender closed before fresh handoff; protocolClosures={manager.ProtocolClosures}; activeSessions={manager.Count}; lastSendFailure={manager.LastOutboundSendFailure}; events={string.Join(',', coordinator.ForSessionEvents(sessionId).Select(value => value.GetType().Name))}", error);
            }
            int handoffIndex = handoffFrames.FindIndex(frame =>
                frame.RootElement.GetProperty("type").GetString() == "match.handoff");
            Assert.True(handoffIndex > 0, $"Fresh handoff was not delivered; frames={string.Join(',', handoffFrames.Select(frame => frame.RootElement.GetProperty("type").GetString()))}");
            JsonDocument snapshotBeforeHandoff = handoffFrames[handoffIndex - 1];
            Assert.Equal("lobby.snapshot", snapshotBeforeHandoff.RootElement.GetProperty("type").GetString());
            JsonElement snapshotPayload = snapshotBeforeHandoff.RootElement.GetProperty("payload");
            Assert.Equal(nextMatch, snapshotPayload.GetProperty("currentMatchId").GetGuid());
            long handoffSnapshotRevision = snapshotPayload.GetProperty("revision").GetInt64();
            Assert.DoesNotContain(handoffFrames.Skip(handoffIndex + 1), frame =>
                frame.RootElement.GetProperty("type").GetString() == "lobby.snapshot");
            long[] handoffEventIds = handoffFrames.Select(frame =>
                frame.RootElement.GetProperty("eventId").GetInt64()).ToArray();
            Assert.Equal(handoffEventIds.OrderBy(value => value), handoffEventIds);
            List<JsonDocument> afterHandoffFrames = [];
            using JsonDocument barrier = await SendCommand(resumed, "node.ping", new { },
                timeout.Token, afterHandoffFrames);
            Assert.Equal("node.pong", barrier.RootElement.GetProperty("type").GetString());
            foreach (JsonDocument frame in afterHandoffFrames.Where(frame =>
                frame.RootElement.GetProperty("type").GetString() == "lobby.snapshot"))
            {
                JsonElement payload = frame.RootElement.GetProperty("payload");
                Assert.Equal(nextMatch, payload.GetProperty("currentMatchId").GetGuid());
                Assert.True(payload.GetProperty("revision").GetInt64() >= handoffSnapshotRevision);
            }
            foreach (JsonDocument frame in afterHandoffFrames) frame.Dispose();
            Assert.True(scheduler.TrySendMatchAdmin(new(new(nextMatch),
                AdminAction.EndMatch, null)));
            await WaitUntilAsync(() => !scheduler.TryGetAssignment(new(nextMatch), out _),
                timeout.Token);
        }
        finally
        {
            manager.BeforeOutboundSend = null;
            release.TrySetResult();
            resumed?.Dispose();
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try { await host.App.StopAsync(shutdown.Token); }
            catch (OperationCanceledException) { }
        }
    }

    private static async Task<JsonDocument> SendCommand<T>(ClientWebSocket socket, string type, T payload,
        CancellationToken cancellationToken, List<JsonDocument>? observed = null)
    {
        Guid requestId = Guid.NewGuid();
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new
        {
            version = NodeControlCodec.Version,
            type,
            requestId,
            payload
        });
        await socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken);
        while (true)
        {
            JsonDocument response = await Read(socket, cancellationToken);
            if (response.RootElement.TryGetProperty("requestId", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && id.GetGuid() == requestId)
            {
                return response;
            }
            if (observed is null) response.Dispose();
            else observed.Add(response);
        }
    }

    private static async Task<NodeRoundSnapshot> WaitForRoundAsync(LobbyManager lobbies,
        Guid sessionId, CancellationToken cancellationToken)
    {
        while (true)
        {
            if (lobbies.RoundForSession(sessionId) is { Options.IsEmpty: false } round)
                return round;
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition,
        CancellationToken cancellationToken)
    {
        while (!condition()) await Task.Delay(10, cancellationToken);
    }
    private static ClientWebSocket Socket(string authorization)
    { var socket = new ClientWebSocket(); socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; socket.Options.SetRequestHeader("Authorization", authorization); return socket; }
    private static async Task<JsonDocument> Read(ClientWebSocket socket, CancellationToken ct)
    {
        byte[] bytes = new byte[32768]; int length = 0; WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes, length, bytes.Length - length), ct); length += result.Count; } while (!result.EndOfMessage);
        return JsonDocument.Parse(bytes.AsMemory(0, length));
    }
}
