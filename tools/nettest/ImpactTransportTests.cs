using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

internal static class ImpactTransportTests
{
    internal static void Run()
    {
        bool enabled = NetConfirmedImpacts.Enabled;
        NetConfirmedImpacts.Enabled = true;
        try { Ingress(); }
        finally { NetConfirmedImpacts.Enabled = enabled; }
    }
    private static void Check(bool value, string message) => NetArchitectureTests.Check(value, message);
    private static T Field<T>(object owner, string name) => (T)owner.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static void Ingress()
    {
        using var client = new NetTransport(0);
        using var authority = new NetTransport(0);
        using var stranger = new UdpClient(0);
        var clientAddress = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
        var authorityAddress = new IPEndPoint(IPAddress.Loopback, authority.LocalPort);
        byte[] hello = new byte[6]; hello[0] = (byte)NetConfig.ProtocolVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), 123);
        client.Send(authorityAddress, PacketType.Hello, hello);
        byte[] welcome = new byte[17]; BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), 123);
        authority.Send(clientAddress, PacketType.Welcome, welcome);
        Check(SpinWait.SpinUntil(() => client.ConnectionStats(authorityAddress).HasValue, 2000),
            "impact client binds through real authority Welcome");
        foreach (var packet in client.Drain()) { }
        foreach (var packet in authority.Drain()) { }
        var connections = Field<Dictionary<IPEndPoint, NetConnection>>(client, "_connections");
        var connection = connections[authorityAddress];
        Check(connection.ReceivesAuthorityFacts, "Welcome grants authority-only cosmetic lane");
        var fact = new ConfirmedImpact(new ShotKey(1, 1, 0, 1, 1, 1), 1, 0, 1, 1, 1, 1,
            1, 0, 0, ImpactProvenance.NativePoint, Vector3.Zero);
        byte[] payload = new byte[ConfirmedImpact.Size]; fact.Write(payload);
        Check(authority.TrySendImpact(clientAddress, payload), "authority can send valid impact");
        var inbox = Field<NetPacketQueue>(client, "_liveInbox");
        Check(SpinWait.SpinUntil(() => inbox.CosmeticCount == 1, 2000), "real UDP authority cue reaches independent queue");
        foreach (var packet in client.Drain(NetPumpBudget.BeforeSimulation))
            Check(packet.Type != PacketType.ConfirmedImpact, "before-simulation pump excludes cosmetics");
        Check(inbox.CosmeticCount == 1, "before-simulation preserves queued cosmetic");
        int delivered = 0;
        foreach (var packet in client.Drain(NetPumpBudget.AfterSimulation))
            if (packet.Type == PacketType.ConfirmedImpact) { Check(ConfirmedImpact.TryRead(packet.Payload, out var read) && read == fact,
                "impact wire payload round trip"); delivered++; }
        Check(delivered == 1 && inbox.CosmeticCount == 0, "after-simulation releases delivered cosmetic queue entry");

        // Raw wire injection tests the real receive worker, not application parsing.
        byte[] wire = new byte[NetHeader.Size + payload.Length]; payload.CopyTo(wire, NetHeader.Size);
        uint sequence = 100;
        void Reject(NetHeaderFlags flags = NetHeaderFlags.None, uint ack = 0, uint bits = 0,
            int deltaLength = 0, bool wrongId = false)
        {
            long before = client.CosmeticDrops + client.Telemetry.Capture().Invalid;
            new NetHeader(PacketType.ConfirmedImpact, flags, connection.Id + (wrongId ? 1UL : 0UL), sequence++, ack, bits).Write(wire);
            byte[] bytes = new byte[wire.Length + deltaLength]; wire.AsSpan(0, Math.Min(wire.Length, bytes.Length)).CopyTo(bytes);
            Inject(client, authorityAddress, bytes);
            Check(client.CosmeticDrops + client.Telemetry.Capture().Invalid > before && inbox.CosmeticCount == 0,
                "invalid impact envelope never reaches presentation queue");
        }
        Reject(NetHeaderFlags.AckValid); Reject(NetHeaderFlags.Reliable); Reject(NetHeaderFlags.AckOnly);
        Reject(ack: 1); Reject(bits: 1); Reject(deltaLength: -1); Reject(deltaLength: 1); Reject(wrongId: true);
        connection.QueueOnly = true; Reject(); connection.QueueOnly = false;
        connection.RetiredAt = 1; Reject(); connection.RetiredAt = null;
        long invalid = client.Telemetry.Capture().Invalid;
        stranger.Send(new byte[] { (byte)PacketType.ConfirmedImpact }, clientAddress);
        new NetHeader(PacketType.ConfirmedImpact, 0, connection.Id, sequence++, 0, 0).Write(wire);
        stranger.Send(wire, clientAddress);
        Check(SpinWait.SpinUntil(() => client.Telemetry.Capture().Invalid >= invalid + 2, 2000),
            "raw impact and unknown endpoint rejected over UDP");
        var serverConnection = Field<Dictionary<IPEndPoint, NetConnection>>(authority, "_connections")[clientAddress];
        long refused = authority.CosmeticDrops;
        new NetHeader(PacketType.ConfirmedImpact, 0, serverConnection.Id, sequence++, 0, 0).Write(wire);
        Inject(authority, clientAddress, wire);
        Check(authority.CosmeticDrops > refused, "server rejects authenticated client-origin impact");

        // Deterministic ingress avoids socket timing/rate refill obscuring storage isolation.
        var beforeStats = connection.Capture();
        for (int i = 0; i < 100; i++)
        {
            new NetHeader(PacketType.ConfirmedImpact, 0, connection.Id, sequence++, 0, 0).Write(wire);
            Inject(client, authorityAddress, wire);
        }
        Check(inbox.CosmeticCount <= 64 && inbox.Count == 0 && client.CosmeticDrops > 0,
            "cosmetic flood bounded independently from gameplay storage");
        var afterStats = connection.Capture();
        Check(afterStats.Sent == beforeStats.Sent && afterStats.Acknowledged == beforeStats.Acknowledged
            && afterStats.EstimatedLost == beforeStats.EstimatedLost && afterStats.RttMilliseconds == beforeStats.RttMilliseconds,
            "cosmetic flood leaves gameplay attempt and RTT accounting unchanged");
        client.EnableRealtimeStateCoalescing();
        byte[] snapshot = new byte[NetHeader.Size + 1]; snapshot[^1] = 42;
        serverConnection.Send(PacketType.SnapshotFast, 0).Write(snapshot);
        Inject(client, authorityAddress, snapshot);
        int drained = 0; bool sawSnapshot = false;
        foreach (var packet in client.Drain(NetPumpBudget.Default))
        {
            if (packet.Type == PacketType.SnapshotFast) sawSnapshot = true;
            if (packet.Type == PacketType.ConfirmedImpact)
            { Check(sawSnapshot, "coalesced snapshot drains before cosmetic work"); drained++; }
        }
        Check(sawSnapshot && drained <= 8, "cosmetic pump has independent eight-packet ceiling");
    }
    private static void Inject(NetTransport transport, IPEndPoint sender, byte[] wire)
    {
        // Use the production synchronized ingress; real UDP above proves endpoint binding.
        var method = typeof(NetTransport).GetMethod("AcceptDatagram", BindingFlags.Instance | BindingFlags.NonPublic)!;
        method.Invoke(transport, new object[] { sender, (byte[])wire.Clone(), wire.Length, System.Diagnostics.Stopwatch.GetTimestamp() });
    }
}
