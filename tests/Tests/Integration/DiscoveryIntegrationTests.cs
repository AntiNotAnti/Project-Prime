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

    [Fact]
    public async Task LegacyDirectoryIsReadableButNeverReceivesHostRequest()
    {
        using var responder = new Responder([(byte)PacketType.MasterList, 0, 0]);
        MasterListResult result = await Task.Run(() => NetMasterClient.Query("127.0.0.1", responder.Endpoint.Port, 500));
        Assert.True(result.Answered); Assert.False(result.Compatible);
        Assert.Equal(NetWireFamily.LegacyRelay, result.Family);
        HostedGame hosted = await Task.Run(() => NetMasterClient.RequestGame("127.0.0.1", responder.Endpoint.Port,
            "MP1 SANCTORUS", GameMode.Battle, 600, 10, 8, "TEST", 500));
        Assert.False(hosted.Started); Assert.Contains("Incompatible architecture", hosted.Reason);
        Assert.Equal(0, responder.Hosts);
    }

    [Fact]
    public async Task AuthoritativeDirectoryPublishesFamiliesAndRefusesLegacyHostMutation()
    {
        var master = new MasterServer(0);
        using var stop = new CancellationTokenSource();
        Task running = Task.Run(() => master.Run(stop.Token));
        try
        {
            var clock = Stopwatch.StartNew();
            while (master.BoundPort == 0 && clock.ElapsedMilliseconds < 2000) { await Task.Delay(5); }
            Assert.True(master.BoundPort > 0);
            using var sender = new UdpClient(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, master.BoundPort);
            byte[] heartbeat = new byte[1 + MasterHeartbeatPacket.Size];
            heartbeat[0] = (byte)PacketType.MasterHeartbeat;
            new MasterHeartbeatPacket { Family = NetWireFamily.Authoritative, Protocol = NetHeader.Version,
                Port = 27888, MaxPlayers = 8, Mode = (byte)GameMode.Battle, RoomKey = "MP1 SANCTORUS", ServerName = "CURRENT" }.Write(heartbeat.AsSpan(1));
            await sender.SendAsync(heartbeat, endpoint);
            byte[] legacy = new byte[1 + MasterHeartbeatPacket.LegacySize];
            heartbeat.AsSpan(0, legacy.Length).CopyTo(legacy);
            legacy[1] = 5;
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(legacy.AsSpan(2), 27887);
            await sender.SendAsync(legacy, endpoint);
            await Task.Delay(60);
            MasterListResult result = await Task.Run(() => NetMasterClient.Query("127.0.0.1", master.BoundPort, 1000));
            Assert.True(result.Compatible); Assert.Equal(2, result.Servers.Count);
            Assert.Contains(result.Servers, entry => entry.Compatible && entry.Port == 27888);
            Assert.Contains(result.Servers, entry => !entry.Compatible && entry.Family == NetWireFamily.LegacyRelay && entry.Protocol == 5);
            byte[] request = new byte[1 + HostRequestPacket.LegacySize];
            request[0] = (byte)PacketType.HostRequest;
            await sender.SendAsync(request, endpoint);
            using var timeout = new CancellationTokenSource(2000);
            UdpReceiveResult answer = await sender.ReceiveAsync(timeout.Token);
            Assert.Equal((byte)PacketType.HostReply, answer.Buffer[0]);
            Assert.True(HostReplyPacket.TryRead(answer.Buffer.AsSpan(1), out HostReplyPacket refusal));
            Assert.False(refusal.Started); Assert.Contains("malformed", refusal.Reason);
            HostedGame hosted = await Task.Run(() => NetMasterClient.RequestGame("127.0.0.1", master.BoundPort,
                "MP1 SANCTORUS", GameMode.Battle, 600, 10, 8, "TEST", 1000));
            Assert.False(hosted.Started); Assert.Contains("hosting is disabled", hosted.Reason);
        }
        finally { stop.Cancel(); master.Stop(); await running.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Fact]
    public async Task DirectoryAndHostReplyRejectImpostorEndpoints()
    {
        using var directory = new UdpClient(0);
        using var impostor = new UdpClient(0);
        int port = ((IPEndPoint)directory.Client.LocalEndPoint!).Port;
        using var timeout = new CancellationTokenSource(3000);
        byte[] modernList = [(byte)PacketType.MasterList, 0, 0, (byte)NetWireIdentity.Family, NetHeader.Version];
        Task<MasterListResult> query = Task.Run(() => NetMasterClient.Query("127.0.0.1", port, 1000));
        UdpReceiveResult request = await directory.ReceiveAsync(timeout.Token);
        await impostor.SendAsync(modernList, request.RemoteEndPoint);
        await directory.SendAsync(new byte[] { (byte)PacketType.MasterList, 0, 0 }, request.RemoteEndPoint);
        Assert.Equal(NetWireFamily.LegacyRelay, (await query).Family);

        Task<HostedGame> hosting = Task.Run(() => NetMasterClient.RequestGame("127.0.0.1", port,
            "MP1 SANCTORUS", GameMode.Battle, 600, 10, 8, "TEST", 1000));
        request = await directory.ReceiveAsync(timeout.Token);
        Assert.Equal((byte)PacketType.MasterQuery, request.Buffer[0]);
        await directory.SendAsync(modernList, request.RemoteEndPoint);
        request = await directory.ReceiveAsync(timeout.Token);
        Assert.Equal((byte)PacketType.HostRequest, request.Buffer[0]);
        byte[] answer = new byte[1 + HostReplyPacket.Size];
        answer[0] = (byte)PacketType.HostReply;
        new HostReplyPacket { Started = true, Port = 27888 }.Write(answer.AsSpan(1));
        await impostor.SendAsync(answer, request.RemoteEndPoint);
        new HostReplyPacket { Reason = "DENIED" }.Write(answer.AsSpan(1));
        await directory.SendAsync(answer, request.RemoteEndPoint);
        HostedGame result = await hosting;
        Assert.False(result.Started); Assert.Equal("DENIED", result.Reason);
    }

    [Fact]
    public async Task DirectoryQueryBurstHasBoundedReplies()
    {
        var master = new MasterServer(0);
        using var stop = new CancellationTokenSource();
        Task running = Task.Run(() => master.Run(stop.Token));
        try
        {
            var clock = Stopwatch.StartNew();
            while (master.BoundPort == 0 && clock.ElapsedMilliseconds < 2000) { await Task.Delay(5); }
            Assert.True(master.BoundPort > 0);
            using var socket = new UdpClient(0);
            var endpoint = new IPEndPoint(IPAddress.Loopback, master.BoundPort);
            byte[] query = [(byte)PacketType.MasterQuery, NetHeader.Version];
            clock.Restart();
            for (int i = 0; i < 100; i++) { await socket.SendAsync(query, endpoint); }
            int replies = 0;
            using var receiveDeadline = new CancellationTokenSource(300);
            try { while (true) { await socket.ReceiveAsync(receiveDeadline.Token); replies++; } }
            catch (OperationCanceledException) { }
            Assert.InRange(replies, 1, 10 + (int)Math.Ceiling(clock.Elapsed.TotalSeconds * 5));
        }
        finally { stop.Cancel(); master.Stop(); await running.WaitAsync(TimeSpan.FromSeconds(3)); }
    }

    [Fact]
    public void MasterListRejectsPartialTrailingAndUnknownFamilyWithoutPublishingEntries()
    {
        byte[] bytes = new byte[4 + MasterEntryPacket.Size];
        bytes[0] = bytes[1] = 1; bytes[2] = (byte)NetWireIdentity.Family; bytes[3] = NetHeader.Version;
        new MasterEntryPacket { Address = 0x7F000001, Port = 27888, MaxPlayers = 8, Mode = (byte)GameMode.Battle,
            Protocol = NetHeader.Version, RoomKey = "MP1 SANCTORUS", ServerName = "TEST" }.Write(bytes.AsSpan(4));
        var entries = new MasterEntryPacket[12];
        Assert.True(MasterListPacket.TryRead(bytes, entries, out int count, out int total, out _, out _));
        Assert.Equal(1, count); Assert.Equal(1, total);
        for (int length = 0; length < bytes.Length; length++)
        { Assert.False(MasterListPacket.TryRead(bytes.AsSpan(0, length), entries, out _, out _, out _, out _)); }
        Assert.False(MasterListPacket.TryRead(new byte[NetConfig.MaxPacketSize + 1], entries, out _, out _, out _, out _));
        bytes[2] = 255;
        Assert.False(MasterListPacket.TryRead(bytes, entries, out _, out _, out _, out _));
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
        private int _queries, _joins, _hosts;
        public IPEndPoint Endpoint => new(IPAddress.Loopback, ((IPEndPoint)_socket.Client.LocalEndPoint!).Port);
        public byte[] Reply { set => Volatile.Write(ref _reply, value); }
        public int Queries => Volatile.Read(ref _queries);
        public int Joins => Volatile.Read(ref _joins);
        public int Hosts => Volatile.Read(ref _hosts);
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
                        if (request.Buffer.Length == 2 && request.Buffer[0] is (byte)PacketType.StatusQuery or (byte)PacketType.MasterQuery)
                        {
                            Interlocked.Increment(ref _queries);
                            await _socket.SendAsync(Volatile.Read(ref _reply), request.RemoteEndPoint);
                        }
                        else if (NetHeader.TryRead(request.Buffer, out NetHeader header) && header.Type == NetMessageType.Join)
                        { Interlocked.Increment(ref _joins); }
                        else if (request.Buffer[0] == (byte)PacketType.HostRequest) { Interlocked.Increment(ref _hosts); }
                    }
                }
                catch (OperationCanceledException) { }
            });
        }
        public void Dispose() { _stop.Cancel(); _run.GetAwaiter().GetResult(); _socket.Dispose(); _stop.Dispose(); }
    }
}
