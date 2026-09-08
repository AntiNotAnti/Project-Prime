using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Transport impairment")]
public sealed class DiscoveryIntegrationTests
{
    [Theory]
    [InlineData(true, NetWireFamily.LegacyRelay, 5)]
    [InlineData(false, NetWireFamily.LegacyRelay, 6)]
    [InlineData(false, NetWireFamily.Authoritative, 5)]
    public async Task IncompatibleServerReceivesOnlyReadOnlyDiscovery(bool legacy, NetWireFamily family, byte protocol)
    {
        byte[] reply = Status(family, protocol);
        if (legacy) { Array.Resize(ref reply, 1 + ServerStatusPacket.LegacySize); }
        using var responder = new Responder(reply);
        using var transport = new NetTransport(0);
        using var client = new NetClient(transport, responder.Endpoint, "TEST", Hunter.Samus);
        await PollUntil(client, () => client.Failure != null);
        Assert.Null(client.Connection);
        Assert.Contains("Incompatible", client.Failure!);
        ServerStatus status = NetStatus.Query("127.0.0.1", responder.Endpoint.Port, false, 1000);
        Assert.True(status.Online); Assert.False(status.Compatible);
        Assert.Equal(family, status.Family); Assert.Equal(protocol, status.Protocol);
        Assert.False(NetProbe.Probe("127.0.0.1", responder.Endpoint.Port, 1000).Ok);
        Assert.Equal(0, responder.Joins);
        Assert.True(responder.Queries >= 3);
    }

    [Fact]
    public async Task SpoofedAndMalformedStatusCannotUnlockAdmission()
    {
        byte[] malformed = Status(NetWireFamily.Authoritative, NetHeader.Version);
        malformed[^1] = 255;
        using var responder = new Responder(malformed);
        using var transport = new NetTransport(0);
        using var client = new NetClient(transport, responder.Endpoint, "TEST", Hunter.Samus);
        using var impostor = new UdpClient(0);
        byte[] compatible = Status(NetWireFamily.Authoritative, NetHeader.Version);
        await impostor.SendAsync(compatible, new IPEndPoint(IPAddress.Loopback, transport.LocalPort));
        for (int i = 0; i < 40; i++) { client.Poll(); await Task.Delay(10); }
        Assert.Null(client.Connection); Assert.Null(client.Failure);
        Assert.True(client.Rejected >= 2); Assert.Equal(0, responder.Joins);
        // A validated reply from the exact configured endpoint permits the first Join.
        responder.Reply = compatible;
        await PollUntil(client, () => responder.Joins > 0);
        Assert.Null(client.Connection); // The fake server never grants a connection.
    }

    private static byte[] Status(NetWireFamily family, byte protocol)
    {
        byte[] bytes = new byte[1 + ServerStatusPacket.Size]; bytes[0] = (byte)PacketType.StatusReply;
        new ServerStatusPacket { Family = family, Protocol = protocol, MaxPlayers = 8, ServerName = "TEST",
            Match = new MatchStatePacket { Mode = (byte)GameMode.Battle, RoomKey = "MP1 SANCTORUS", NextRoomKey = "MP1 SANCTORUS" } }.Write(bytes.AsSpan(1));
        return bytes;
    }

    private static async Task PollUntil(NetClient client, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        while (!done() && clock.ElapsedMilliseconds < 2500) { client.Poll(); await Task.Delay(5); }
        Assert.True(done(), client.Failure ?? "Timed out waiting for discovery.");
    }

    private sealed class Responder : IDisposable
    {
        private readonly UdpClient _socket = new(0);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _run;
        private byte[] _reply;
        private int _queries, _joins;
        public IPEndPoint Endpoint => new(IPAddress.Loopback, ((IPEndPoint)_socket.Client.LocalEndPoint!).Port);
        public byte[] Reply { set => Volatile.Write(ref _reply, value); }
        public int Queries => Volatile.Read(ref _queries);
        public int Joins => Volatile.Read(ref _joins);
        public Responder(byte[] reply)
        {
            _reply = reply;
            _run = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        UdpReceiveResult request = await _socket.ReceiveAsync(_stop.Token);
                        if (request.Buffer.Length == 2 && request.Buffer[0] == (byte)PacketType.StatusQuery)
                        {
                            Interlocked.Increment(ref _queries);
                            await _socket.SendAsync(Volatile.Read(ref _reply), request.RemoteEndPoint);
                        }
                        else if (NetHeader.TryRead(request.Buffer, out NetHeader header) && header.Type == NetMessageType.Join)
                        { Interlocked.Increment(ref _joins); }
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        public void Dispose() { _stop.Cancel(); _run.GetAwaiter().GetResult(); _socket.Dispose(); _stop.Dispose(); }
    }
}
