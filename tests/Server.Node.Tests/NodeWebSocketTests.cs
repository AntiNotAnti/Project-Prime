using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FruityPrime.Server.Node.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class NodeWebSocketTests
{
    [Fact]
    public async Task RealTlsControlConnectionAuthenticatesAndReturnsRevisionedLobby()
    {
        await using var host = new NodeHostFixture();
        await host.App.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var uri = new Uri(host.App.Urls.Single().Replace("https:", "wss:") + "/v1/control");
        using var socket = new ClientWebSocket();
        socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true; // Test-only ephemeral certificate.
        socket.Options.SetRequestHeader("Authorization", "Bearer " + host.Ticket(Guid.NewGuid()));
        await socket.ConnectAsync(uri, timeout.Token);
        using var welcome = await Read(socket, timeout.Token);
        Assert.Equal("node.session", welcome.RootElement.GetProperty("type").GetString());
        string frame = JsonSerializer.Serialize(new { version = 1, type = "lobby.create", requestId = Guid.NewGuid(), payload = new { name = "Arena", visibility = "Public" } });
        await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, timeout.Token);
        using var snapshot = await Read(socket, timeout.Token);
        Assert.Equal("lobby.snapshot", snapshot.RootElement.GetProperty("type").GetString());
        Assert.Equal(1, snapshot.RootElement.GetProperty("payload").GetProperty("revision").GetInt64());
        Assert.Equal(1, host.App.Services.GetRequiredService<NodeSessionManager>().Count);
        socket.Abort();
        await host.App.StopAsync(timeout.Token);
    }
    private static async Task<JsonDocument> Read(ClientWebSocket socket, CancellationToken ct)
    {
        byte[] bytes = new byte[32768]; int count = 0; WebSocketReceiveResult received;
        do { received = await socket.ReceiveAsync(new ArraySegment<byte>(bytes, count, bytes.Length - count), ct); count += received.Count; }
        while (!received.EndOfMessage);
        return JsonDocument.Parse(bytes.AsMemory(0, count));
    }
}
