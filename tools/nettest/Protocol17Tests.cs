using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class Protocol17Tests
{
    private static void Check(bool ok, string label) => NetArchitectureTests.Check(ok, label);
    internal static void Connect(NetTransport server, NetTransport client)
    {
        var serverAddress = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
        var clientAddress = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
        client.Send(serverAddress, PacketType.Hello, new byte[] { NetConfig.ProtocolVersion, 255, 42, 0, 0, 0 });
        Span<byte> welcome = stackalloc byte[17]; welcome.Clear(); welcome[1] = 42;
        server.Send(clientAddress, PacketType.Welcome, welcome);
        Check(SpinWait.SpinUntil(() => client.ConnectionStats(serverAddress)?.ConnectionId == server.ConnectionStats(clientAddress)?.ConnectionId, 2000), "connection established on UDP");
        foreach (var packet in server.Drain()) { }
        foreach (var packet in client.Drain()) { }
    }
    internal static bool SplitSnapshotBudgets()
    {
        int canonicalLength = SnapshotHeader.Size + 8 * PlayerState.Size + NetMatchTimeSync.Size + NetHealthSync.HeaderSize;
        var canonical = new byte[canonicalLength];
        new SnapshotHeader { MatchId = 7, AuthorityEpoch = 8, Frame = 30, PlayerCount = 8 }.Write(canonical);
        for (byte i = 0; i < 8; i++) new PlayerState { SlotIndex = i, SlotGeneration = 1, LifeId = 1,
            Form = new PlayerFormState { Frame = 30, Phase = FormPhase.Biped } }.Write(canonical.AsSpan(SnapshotHeader.Size + i * PlayerState.Size));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(canonical.AsSpan(canonical.Length - NetHealthSync.HeaderSize), 7);
        var lanes = new NetReplicationLanes(); lanes.Prepare(canonical);
        int bootstrapOverhead = NetHeader.Size + 4 + WorldBootstrapIdentity.Size + 1;
        return canonicalLength + NetHealthSync.MaxSpawns * NetHealthSync.EntrySize <= NetConfig.MaxSnapshotSize
            && lanes.FastLength + NetHeader.Size <= 1200
            && lanes.SlowLength + NetHeader.Size <= 1200 && lanes.WorldLength + NetHeader.Size <= 1200
            && lanes.FastLength - SnapshotFast.ReceiptSize + bootstrapOverhead <= 1200
            && lanes.SlowLength + bootstrapOverhead <= 1200 && lanes.WorldLength + bootstrapOverhead <= 1200
            && NetReplicationLanes.LaneHeader + NetHealthSync.HeaderSize + NetHealthSync.MaxSpawns * NetHealthSync.EntrySize + bootstrapOverhead <= 1200;
    }
    public static int Run()
    {
        try
        {
            Check(NetConfig.ProtocolVersion == 64, "current protocol train is 64");
            var window = new NetReceiveWindow();
            Check(window.Observe(uint.MaxValue - 1) == SequenceResult.New, "initial sequence");
            Check(window.Observe(0) == SequenceResult.New && window.Bits == 2, "wrap skips missing sequence");
            Check(window.Observe(uint.MaxValue) == SequenceResult.Reordered && window.Bits == 3, "late bit fills across wrap");
            Check(window.Observe(uint.MaxValue) == SequenceResult.Duplicate, "duplicate across wrap");
            Check(window.Observe(32) == SequenceResult.New && window.Bits == 0x80000000, "distance 32 is not masked to zero shift");
            Check(window.Observe(0) == SequenceResult.Duplicate, "oldest bit duplicate");
            Check(window.Observe(uint.MaxValue) == SequenceResult.TooOld, "outside ACK window");
            Check(window.Observe(65) == SequenceResult.New && window.Bits == 0, "distance 33 clears history");
            for (uint i = 0; i < 32; i++) Check(window.Observe(64 - i) == SequenceResult.Reordered, "reorder fills each ACK bit");
            Check(window.Bits == uint.MaxValue && NetReceiveWindow.Acknowledges(33, 65, window.Bits)
                && !NetReceiveWindow.Acknowledges(32, 65, window.Bits), "ACK field coverage");
            var endpoint = new IPEndPoint(IPAddress.Loopback, 1234);
            var a = new NetConnection(endpoint, 7, initialSequence: uint.MaxValue);
            var b = new NetConnection(endpoint, 7);
            var sent = a.Send(PacketType.Intent, 0);
            b.Receive(sent, 50);
            a.Receive(b.Send(PacketType.Snapshot, 50), 100);
            Check(a.Capture().Acknowledged == 1 && a.Capture().RttMilliseconds == 100, "ACK derived RTT");
            Check(a.Send(PacketType.Intent, 101).Sequence == 0, "send rollover");
            Check(!a.Accepts(new IPEndPoint(IPAddress.Loopback, 4321), sent)
                && !a.Accepts(endpoint, sent with { ConnectionId = 8 }), "endpoint and incarnation fence");
            Span<byte> bytes = stackalloc byte[NetHeader.Size]; sent.Write(bytes);
            Check(bytes[3] == 64 && NetHeader.TryRead(bytes, out var decoded) && decoded == sent, "protocol64 envelope round trip");
            foreach (byte oldProtocol in new byte[] { 60,61,62,63 })
            {
                bytes[3] = oldProtocol;
                Check(!NetHeader.TryRead(bytes,out _),"old live envelope rejected despite preserved native payload contract: "+oldProtocol);
            }
            Check(SplitSnapshotBudgets(), "eight-player canonical state and actual split live/bootstrap lanes remain bounded");
            using var server = new NetTransport(0);
            using var client = new NetTransport(0);
            Connect(server, client);
            var serverAddress = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
            var clientAddress = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
            client.Send(serverAddress, PacketType.Intent, new byte[] { 7 });
            bool delivered = false;
            Check(SpinWait.SpinUntil(() => { foreach (var p in server.Drain()) if (p.Type == PacketType.Intent) delivered = p.Payload[0] == 7; return delivered; }, 2000),
                "production envelope passes application payload");
            server.Send(clientAddress, PacketType.Snapshot, new byte[] { 8 });
            Check(SpinWait.SpinUntil(() => client.ConnectionStats(serverAddress)?.Acknowledged > 0, 2000), "real UDP ACK");
            using var stranger = new UdpClient(0);
            byte[] forged = new byte[NetHeader.Size + 1];
            new NetHeader(PacketType.Bye, 0, client.ConnectionStats(serverAddress)!.Value.ConnectionId, 80, 0, 0).Write(forged);
            long invalid = server.Telemetry.Capture().Invalid;
            stranger.Send(forged, serverAddress);
            Check(SpinWait.SpinUntil(() => server.Telemetry.Capture().Invalid > invalid, 2000), "known connection from wrong endpoint rejected over UDP");
            ulong prior = client.ConnectionStats(serverAddress)!.Value.ConnectionId;
            server.ForgetConnection(clientAddress);
            Connect(server, client);
            ulong replacement = client.ConnectionStats(serverAddress)!.Value.ConnectionId;
            Check(replacement != prior, "server restart establishes a new incarnation on the existing client socket");
            client.Send(serverAddress, PacketType.Hello, new byte[] { NetConfig.ProtocolVersion, 255, 42, 0, 0, 0 });
            byte[] staleWelcome = new byte[NetHeader.Size + 4 + 17];
            new NetHeader(PacketType.Welcome, NetHeaderFlags.Reliable, prior, 999, 0, 0).Write(staleWelcome);
            staleWelcome[NetHeader.Size + 4 + 1] = 42;
            // Inject from the real server socket to exercise the endpoint as well as ID fence.
            var socket = (UdpClient)typeof(NetTransport).GetField("_socket", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(server)!;
            long beforeStale = client.Telemetry.Capture().Invalid;
            socket.Send(staleWelcome, clientAddress);
            Check(SpinWait.SpinUntil(() => client.Telemetry.Capture().Invalid > beforeStale, 2000)
                && client.ConnectionStats(serverAddress)!.Value.ConnectionId == replacement, "old Welcome cannot restore superseded connection");
            Console.WriteLine("PASS: protocol 19 transport sequences, ACK bits, wrap, identity and UDP integration"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
