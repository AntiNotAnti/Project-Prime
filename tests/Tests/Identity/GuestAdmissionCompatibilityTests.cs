using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

/// <summary>
/// Ticket authentication is an opt-in trust boundary. Existing guest joins must
/// keep working while a server is configured for optional account admission.
/// </summary>
public sealed class GuestAdmissionCompatibilityTests
{
    [Fact]
    public void GuestJoinRemainsCompatibleWithoutTicketAuthority()
    {
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort);
        var server = new ServerNetwork(serverTransport, "MP1 SANCTORUS", GameMode.Battle);
        using var guest = new NetClient(clientTransport, endpoint, "GUEST", Hunter.Samus);

        Pump(server, guest, () => guest.HasRoster);

        Assert.Null(guest.Failure);
        Assert.NotNull(guest.Connection);
        Assert.Null(server.Find(guest.Connection!.Id)!.PlayerId);
    }

    [Fact]
    public void GuestJoinRemainsCompatibleWhenTicketAuthorityIsOptional()
    {
        Guid serverId = Guid.NewGuid();
        using var backend = new HttpClient(new ImmediateFailureHandler());
        using var authority = new ServerTicketAuthority(OptionalOptions(serverId, requireTickets: false), backend);
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort);
        var server = new ServerNetwork(serverTransport, "MP1 SANCTORUS", GameMode.Battle)
        {
            TicketAuthority = authority
        };
        using var guest = new NetClient(clientTransport, endpoint, "GUEST", Hunter.Kanden);

        Pump(server, guest, () => guest.HasRoster);

        Assert.Null(guest.Failure);
        Assert.NotNull(guest.Connection);
        ServerPeer peer = Assert.IsType<ServerPeer>(server.Find(guest.Connection!.Id));
        Assert.Equal(serverId, server.ServerId);
        Assert.Null(peer.PlayerId);
    }

    [Fact]
    public void RequiredTicketAuthorityRejectsGuestJoinWithoutTicket()
    {
        using var backend = new HttpClient(new ImmediateFailureHandler());
        using var authority = new ServerTicketAuthority(OptionalOptions(Guid.NewGuid(), requireTickets: true), backend);
        using var serverTransport = new NetTransport(0);
        using var clientTransport = new NetTransport(0);
        var endpoint = new IPEndPoint(IPAddress.Loopback, serverTransport.LocalPort);
        var server = new ServerNetwork(serverTransport, "MP1 SANCTORUS", GameMode.Battle)
        {
            TicketAuthority = authority
        };
        using var guest = new NetClient(clientTransport, endpoint, "GUEST", Hunter.Trace);

        Pump(server, guest, () => guest.Connection != null || guest.Failure != null);

        Assert.Null(guest.Connection);
        Assert.Contains("requires a game ticket", guest.Failure!, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, server.Count);
    }

    private static ServerTicketOptions OptionalOptions(Guid serverId, bool requireTickets)
        => new(new Uri("https://tickets.example.test/"), "https://tickets.example.test", serverId,
            "operator-secret-for-test")
        {
            RequireTickets = requireTickets
        };

    private static void Pump(ServerNetwork server, NetClient client, Func<bool> done)
    {
        var watch = Stopwatch.StartNew();
        uint tick = 0;
        while (watch.Elapsed < TimeSpan.FromSeconds(5))
        {
            server.Poll(++tick);
            client.Poll();
            if (done()) return;
            Thread.Sleep(1);
        }
        Assert.Fail(client.Failure ?? "Guest admission loopback condition timed out.");
    }

    private sealed class ImmediateFailureHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
