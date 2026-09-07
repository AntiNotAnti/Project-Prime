using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MphRead.Backend.Tickets;
using MphRead.Identity;
using MphRead.Mods.Network;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MphRead.Backend.Tests;

public sealed class ServerTicketAuthorityIntegrationTests
{
    private const string Password = "Ticket-Only-Strong123!";
    private const string Secret = "test-only-server-credential-not-for-production-123456";
    private const string Issuer = "https://backend.example.test";
    private static readonly Guid ServerId = Guid.Parse("d9a4ce1f-4ab0-4f67-9864-3f8d6e39b0e1");
    private static readonly Guid Incarnation = Guid.Parse("03c5e4d1-f2ab-4c39-9b8e-1d7a6f5c4b3a");

    [Fact]
    public async Task FactoryJwksAndSessionFeedARealUdpTicketAdmission()
    {
        using var keys = new TicketTests.TestKeys();
        using var factory = new BackendFactory(configure: services =>
        {
            services.Configure<TicketOptions>(keys.Configure);
            services.Configure<GameServerOptions>(options => options.Servers.Add(new()
            {
                Id = ServerId,
                Enabled = true,
                PublicAddress = "127.0.0.1", PublicPort = 5000,
                ApiKeySha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret)))
            }));
        });
        using var player = factory.CreateDatabaseClient();
        HttpResponseMessage registered = await player.PostAsJsonAsync("/v1/auth/register", new
        {
            Email = "authority@example.test",
            Password,
            DisplayName = "Authority"
        });
        registered.EnsureSuccessStatusCode();
        JsonElement registration = await registered.Content.ReadFromJsonAsync<JsonElement>();
        PlayerId playerId = PlayerId.Parse(registration.GetProperty("playerId").GetString()!);
        var email = Assert.Single(factory.Email.Sent);
        Assert.Equal(HttpStatusCode.NoContent,
            (await player.PostAsJsonAsync("/v1/auth/confirm-email", new { email.PlayerId, email.Code })).StatusCode);

        HttpResponseMessage login = await player.PostAsJsonAsync("/v1/auth/login", new
        {
            Email = "authority@example.test",
            Password
        });
        login.EnsureSuccessStatusCode();
        JsonElement tokens = await login.Content.ReadFromJsonAsync<JsonElement>();
        player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GetProperty("accessToken").GetString());

        using var authorityHttp = factory.CreateClient();
        var options = new ServerTicketOptions(authorityHttp.BaseAddress!, Issuer, ServerId, Secret)
        {
            SessionId = Incarnation,
            RequireTickets = true
        };
        using var authority = new ServerTicketAuthority(options, authorityHttp);

        TicketResponse ticket = await WaitForTicket(player);
        Assert.Equal(ServerId, ticket.ServerId);
        Assert.Equal(Incarnation, ticket.ServerIncarnation);
        Assert.InRange(ticket.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(121));

        using var serverTransport = new UdpTestTransport();
        var server = new ServerNetwork(serverTransport,
            new MatchRules(MatchMode.Battle, "MP1 SANCTORUS", maxPlayers: 2))
        {
            TicketAuthority = authority
        };
        IPEndPoint serverEndpoint = new(IPAddress.Loopback, serverTransport.LocalPort);
        using var clientTransport = new UdpClient(0);
        JoinPacket join = new(NetHeader.Version, 42, Hunter.Samus, "Authority", Ticket: ticket.Ticket);
        byte[] datagram = new byte[NetHeader.Size + join.EncodedSize];
        new NetHeader(NetMessageType.Join, NetHeaderFlags.Unsequenced, 0, 0, 0, 0).Write(datagram);
        join.Write(datagram.AsSpan(NetHeader.Size));
        await clientTransport.SendAsync(datagram, datagram.Length, serverEndpoint);

        uint tick = 0;
        Stopwatch timeout = Stopwatch.StartNew();
        while (server.Count == 0 && timeout.Elapsed < TimeSpan.FromSeconds(5))
        {
            server.Poll(++tick);
            await Task.Delay(1);
        }

        Assert.Equal(1, server.Count);
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Peers[0]);
        Assert.Equal(playerId, peer.PlayerId!.Value);

        async Task<TicketResponse> WaitForTicket(HttpClient client)
        {
            Stopwatch deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                using HttpResponseMessage response = await client.PostAsJsonAsync("/v1/game-tickets",
                    new { ServerId, Nonce = "42" });
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return (await response.Content.ReadFromJsonAsync<TicketResponse>())!;
                }
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                await Task.Delay(10);
            }
            throw new Xunit.Sdk.XunitException("Backend session registration did not become visible.");
        }
    }

    private sealed class UdpTestTransport : INetTransport
    {
        private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        public int LocalPort => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
        public long PacketsDropped => 0;
        public int QueuedPackets => _socket.Available > 0 ? 1 : 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();

        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length) { }

        public IEnumerable<ReceivedPacket> Drain()
        {
            while (_socket.Available > 0)
            {
                IPEndPoint sender = new(IPAddress.Any, 0);
                byte[] data = _socket.Receive(ref sender);
                Metrics.Received(data.Length);
                yield return new ReceivedPacket(sender, data, data.Length);
            }
        }

        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0)
        {
            byte[] datagram = new byte[1 + payload.Length];
            datagram[0] = (byte)type;
            payload.CopyTo(datagram.AsSpan(1));
            SendDatagram(target, datagram, extraHoldTicks);
        }

        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram)
            => SendDatagram(target, datagram, 0);

        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks)
        {
            _socket.Send(datagram.ToArray(), target);
            Metrics.Sent(datagram.Length);
        }

        public void Dispose() => _socket.Dispose();
    }
}
