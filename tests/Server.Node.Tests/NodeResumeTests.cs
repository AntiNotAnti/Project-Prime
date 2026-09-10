using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

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
    private static ClientWebSocket Socket(string authorization)
    { var socket = new ClientWebSocket(); socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; socket.Options.SetRequestHeader("Authorization", authorization); return socket; }
    private static async Task<JsonDocument> Read(ClientWebSocket socket, CancellationToken ct)
    {
        byte[] bytes = new byte[32768]; int length = 0; WebSocketReceiveResult result;
        do { result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes, length, bytes.Length - length), ct); length += result.Count; } while (!result.EndOfMessage);
        return JsonDocument.Parse(bytes.AsMemory(0, length));
    }
}
