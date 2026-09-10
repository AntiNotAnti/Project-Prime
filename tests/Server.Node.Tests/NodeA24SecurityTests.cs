using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FruityPrime.Server.Node.Identity;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Node.Sessions;
using FruityPrime.Server.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MphRead;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class NodeA24SecurityTests
{
    [Fact]
    public async Task PlaintextControlUpgradeIsRejectedBeforeSessionAdmission()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        int port = new Uri(host.App.Urls.Single()).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await using NetworkStream stream = client.GetStream();
        byte[] request = Encoding.ASCII.GetBytes("GET /v1/control HTTP/1.1\r\nHost: localhost\r\nConnection: Upgrade\r\nUpgrade: websocket\r\n\r\n");
        await stream.WriteAsync(request, timeout.Token);
        byte[] response = new byte[256];
        int read = 0;
        try { read = await stream.ReadAsync(response, timeout.Token); }
        catch (IOException) { }
        catch (SocketException) { }
        Assert.True(read == 0 || !Encoding.ASCII.GetString(response, 0, read).Contains("101", StringComparison.Ordinal));
        Assert.Equal(0, host.App.Services.GetRequiredService<NodeSessionManager>().Count);
    }

    [Theory]
    [InlineData("binary")]
    [InlineData("oversize")]
    [InlineData("malformed")]
    [InlineData("duplicate")]
    public async Task InvalidControlFramesCloseOnlyTheOffendingSessionWithoutMutation(string kind)
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var offender = await ConnectAsync(host, timeout.Token);
        using var sibling = await ConnectAsync(host, timeout.Token);

        await SendInvalidAsync(offender.Socket, kind, timeout.Token);
        await AssertClosedAsync(offender.Socket, timeout.Token);
        Assert.Equal(0, host.App.Services.GetRequiredService<LobbyManager>().Count);

        await SendPingAsync(sibling.Socket, timeout.Token);
        Assert.Equal(1, host.App.Services.GetRequiredService<NodeSessionManager>().Count);
    }

    [Fact]
    public async Task UnauthorizedConfigureAndStartLeaveLobbyRevisionAndStateUnchanged()
    {
        await using var host = new NodeHostFixture(configure: builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Node:Maps:0:MapKey"] = "unit",
            ["Node:Maps:0:ContentHash"] = "hash",
            ["Node:Maps:0:ContentVersion"] = "1",
            ["Node:Maps:0:BuildVersion"] = "test",
            ["Node:Maps:0:ProtocolVersion"] = "8"
        }));
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var owner = await ConnectAsync(host, timeout.Token);
        using var other = await ConnectAsync(host, timeout.Token);

        using var created = await SendCommandAsync(owner.Socket, "lobby.create",
            new { name = "A24", visibility = "Public" }, timeout.Token);
        Guid lobbyId = created.RootElement.GetProperty("payload").GetProperty("lobbyId").GetGuid();
        long revision = created.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();

        using var joined = await SendCommandAsync(other.Socket, "lobby.join",
            new { lobbyId, expectedRevision = revision, observer = false }, timeout.Token);
        revision = joined.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();

        using var configured = await SendCommandAsync(owner.Socket, "lobby.configure",
            new { expectedRevision = revision, mapKey = "unit", mode = "Battle", botCount = 0, timeLimitSeconds = 120 }, timeout.Token);
        revision = configured.RootElement.GetProperty("payload").GetProperty("revision").GetInt64();

        using var configureError = await SendCommandAsync(other.Socket, "lobby.configure",
            new { expectedRevision = revision, mapKey = "unit", mode = "Battle", botCount = 0, timeLimitSeconds = 120 }, timeout.Token);
        Assert.Equal("error", configureError.RootElement.GetProperty("type").GetString());
        Assert.Equal("owner", configureError.RootElement.GetProperty("payload").GetProperty("code").GetString());

        using var startError = await SendCommandAsync(other.Socket, "lobby.start",
            new { expectedRevision = revision }, timeout.Token);
        Assert.Equal("error", startError.RootElement.GetProperty("type").GetString());
        Assert.Equal("owner", startError.RootElement.GetProperty("payload").GetProperty("code").GetString());

        LobbySnapshot snapshot = host.App.Services.GetRequiredService<LobbyManager>().ForSession(owner.SessionId)!;
        Assert.Equal(revision, snapshot.Revision);
        Assert.Equal("unit", snapshot.MapKey);
        Assert.Equal(MatchMode.Battle, snapshot.Mode);
        Assert.Equal(LobbyPhase.Open, snapshot.Phase);
        await SendPingAsync(other.Socket, timeout.Token);
    }

    [Fact]
    public async Task ControlUpgradeRateLimitReturns429AfterTheConfiguredWindow()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator };
        using var client = new HttpClient(handler);
        Uri endpoint = new(new Uri(host.App.Urls.Single()), "/v1/control");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        for (int index = 0; index < 60; index++)
        {
            using HttpResponseMessage response = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        using HttpResponseMessage rejected = await client.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        using HttpResponseMessage health = await client.GetAsync(new Uri(new Uri(host.App.Urls.Single()), "/health"), timeout.Token);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task CommandRateFloodClosesOffenderAndAHealthySiblingAndNewSessionRecover()
    {
        var clock = new MutableClock(DateTimeOffset.UtcNow) { Timestamp = 42 };
        await using var host = new NodeHostFixture(clock);
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var offender = await ConnectAsync(host, timeout.Token);
        using var sibling = await ConnectAsync(host, timeout.Token);

        for (int index = 0; index < 31; index++)
        {
            Guid requestId = Guid.NewGuid();
            byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new
            {
                version = NodeControlCodec.Version,
                type = "node.ping",
                requestId,
                payload = new { }
            });
            await offender.Socket.SendAsync(frame, WebSocketMessageType.Text, true, timeout.Token);
        }
        await AssertClosedAsync(offender.Socket, timeout.Token);
        await SendPingAsync(sibling.Socket, timeout.Token);

        using var recovered = await ConnectAsync(host, timeout.Token, Guid.NewGuid());
        await SendPingAsync(recovered.Socket, timeout.Token);
    }

    [Fact]
    public async Task NodeAdmissionRejectsExpiredFutureIssuedAndWrongKeyTokens()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var clock = new MutableClock(now);
        await using var host = new NodeHostFixture(clock);
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();
        Assert.Null(await validator.ValidateAsync(Issue(host, host.SigningKey, now.AddSeconds(-10), now.AddSeconds(-1))));
        Assert.Null(await validator.ValidateAsync(Issue(host, host.SigningKey, now.AddSeconds(30), now.AddSeconds(90))));
        using var wrongKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Assert.Null(await validator.ValidateAsync(Issue(host, wrongKey, now, now.AddSeconds(60))));
    }

    [Fact]
    public async Task FailedAdmissionHandshakeCreatesNoSession()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var clock = new MutableClock(now);
        await using var host = new NodeHostFixture(clock);
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.SetRequestHeader("Authorization", "Bearer " + Issue(host, host.SigningKey, now.AddSeconds(-10), now.AddSeconds(-1)));
        Uri uri = ControlUri(host);
        await Assert.ThrowsAnyAsync<WebSocketException>(() => socket.ConnectAsync(uri, timeout.Token));
        Assert.Equal(0, host.App.Services.GetRequiredService<NodeSessionManager>().Count);
    }

    [Fact]
    public async Task NodeAdmissionReplayStoreReclaimsExpiredEntriesAfterCapacityIsReached()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var clock = new MutableClock(now);
        await using var host = new NodeHostFixture(clock);
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();
        for (int index = 0; index < 8192; index++)
        {
            string token = Issue(host, host.SigningKey, now, now.AddSeconds(120), Guid.NewGuid());
            Assert.NotNull(await validator.ValidateAsync(token));
        }
        Assert.Null(await validator.ValidateAsync(Issue(host, host.SigningKey, now, now.AddSeconds(120), Guid.NewGuid())));
        clock.Now = now.AddSeconds(121);
        Assert.NotNull(await validator.ValidateAsync(Issue(host, host.SigningKey, clock.Now, clock.Now.AddSeconds(120), Guid.NewGuid())));
    }

    private static Uri ControlUri(NodeHostFixture host)
        => new(host.App.Urls.Single().Replace("https:", "wss:", StringComparison.Ordinal) + "/v1/control");

    private sealed class SessionConnection(ClientWebSocket socket, Guid sessionId) : IDisposable
    {
        public ClientWebSocket Socket { get; } = socket;
        public Guid SessionId { get; } = sessionId;
        public void Dispose() => Socket.Dispose();
    }

    private static async Task<SessionConnection> ConnectAsync(NodeHostFixture host, CancellationToken cancellationToken, Guid? player = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        socket.Options.SetRequestHeader("Authorization", "Bearer " + host.Ticket(player ?? Guid.NewGuid()));
        try
        {
            await socket.ConnectAsync(ControlUri(host), cancellationToken);
            using JsonDocument welcome = await ReadAsync(socket, cancellationToken);
            Assert.Equal("node.session", welcome.RootElement.GetProperty("type").GetString());
            Guid sessionId = welcome.RootElement.GetProperty("payload").GetProperty("sessionId").GetGuid();
            return new(socket, sessionId);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static async Task<JsonDocument> SendCommandAsync(ClientWebSocket socket, string type, object payload, CancellationToken cancellationToken)
    {
        Guid requestId = Guid.NewGuid();
        byte[] frame = JsonSerializer.SerializeToUtf8Bytes(new { version = NodeControlCodec.Version, type, requestId, payload });
        await socket.SendAsync(frame, WebSocketMessageType.Text, true, cancellationToken);
        return await ReadForRequestAsync(socket, requestId, cancellationToken);
    }

    private static async Task SendPingAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using JsonDocument response = await SendCommandAsync(socket, "node.ping", new { }, cancellationToken);
        Assert.Equal("node.pong", response.RootElement.GetProperty("type").GetString());
    }

    private static async Task SendInvalidAsync(ClientWebSocket socket, string kind, CancellationToken cancellationToken)
    {
        switch (kind)
        {
            case "binary":
                await socket.SendAsync(Encoding.UTF8.GetBytes("{}"), WebSocketMessageType.Binary, true, cancellationToken);
                break;
            case "malformed":
                await socket.SendAsync(Encoding.UTF8.GetBytes("{"), WebSocketMessageType.Text, true, cancellationToken);
                break;
            case "duplicate":
                string duplicate = $"{{\"version\":{NodeControlCodec.Version},\"version\":{NodeControlCodec.Version},\"type\":\"node.ping\",\"requestId\":\"{Guid.NewGuid():D}\",\"payload\":{{}}}}";
                await socket.SendAsync(Encoding.UTF8.GetBytes(duplicate), WebSocketMessageType.Text, true, cancellationToken);
                break;
            case "oversize":
                byte[] oversized = new byte[NodeControlCodec.MaximumFrameBytes + 1];
                Array.Fill(oversized, (byte)' ');
                oversized[0] = (byte)'{';
                try
                {
                    await socket.SendAsync(oversized.AsMemory(0, 16384), WebSocketMessageType.Text, false, cancellationToken);
                    await socket.SendAsync(oversized.AsMemory(16384, 16384), WebSocketMessageType.Text, false, cancellationToken);
                    await socket.SendAsync(oversized.AsMemory(32768, 1), WebSocketMessageType.Text, true, cancellationToken);
                }
                catch (WebSocketException) { }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    private static async Task AssertClosedAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024];
        try
        {
            while (true)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close) return;
            }
        }
        catch (WebSocketException)
        {
            // Node aborts protocol-violation sessions after recording the bounded closure.
        }
    }

    private static async Task<JsonDocument> ReadForRequestAsync(ClientWebSocket socket, Guid requestId, CancellationToken cancellationToken)
    {
        while (true)
        {
            JsonDocument document = await ReadAsync(socket, cancellationToken);
            if (document.RootElement.TryGetProperty("requestId", out JsonElement request)
                && request.ValueKind == JsonValueKind.String && request.GetGuid() == requestId)
                return document;
            document.Dispose();
        }
    }

    private static async Task<JsonDocument> ReadAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        byte[] bytes = new byte[NodeControlCodec.MaximumFrameBytes];
        int count = 0;
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes, count, bytes.Length - count), cancellationToken);
            count += result.Count;
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(bytes.AsMemory(0, count));
    }

    private static string Issue(NodeHostFixture host, ECDsa key, DateTimeOffset issued, DateTimeOffset expires, Guid? ticketId = null)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://backend.example",
            Audience = NodeAdmissionValidator.Audience(host.NodeId),
            TokenType = NodeAdmissionValidator.TokenType,
            IssuedAt = issued.UtcDateTime,
            NotBefore = issued.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new(new ECDsaSecurityKey(key) { KeyId = "test" }, "ES256"),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["jti"] = (ticketId ?? Guid.NewGuid()).ToString("D"),
                ["name"] = "Player"
            }
        });

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public long Timestamp { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
        public override long GetTimestamp() => Timestamp;
    }
}
