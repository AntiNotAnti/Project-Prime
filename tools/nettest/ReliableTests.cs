using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class ReliableTests
{
    private readonly record struct Wire(bool ToReceiver, NetHeader Header, uint EventId);
    public static int Run()
    {
        try
        {
            FullStateStartupBurst();
            BackgroundInboxRetry();
            AuthorityFactBudget();
            RateRefusalReliableTests.Run();
            RetiredPeerAcknowledgesBye();
            RetiredPeerRetriesLostByeAck();
            var endpoint = new IPEndPoint(IPAddress.Loopback, 42);
            NetConnection sender = new NetConnection(endpoint, 12), receiver = new NetConnection(endpoint, 12);
            var queue = new NetFaultQueue<Wire>(42, 160, 80, .05, .03, .01);
            var applied = new HashSet<uint>();
            for (int i = 0; i < 32; i++)
                NetArchitectureTests.Check(sender.Reliable.TryQueue(PacketType.LobbyCommandResult, BitConverter.GetBytes(i), 0, out _), "ordinary reliable capacity");
            NetArchitectureTests.Check(!sender.Reliable.TryQueue(PacketType.Roster, new byte[] { 80 }, 0, out _), "ordinary queue cannot consume reserve");
            for (int i = 0; i < 8; i++)
                NetArchitectureTests.Check(sender.Reliable.TryQueue(PacketType.SessionState, BitConverter.GetBytes(i), 0, out _), "critical reserve");
            for (double now = 0; now <= 14000; now += 1000.0 / 60)
            {
                while (sender.Reliable.TrySend(now, out var packet))
                    queue.Enqueue(now, new(true, sender.Send(packet.Type, now, NetHeaderFlags.Reliable, packet.EventId), packet.EventId));
                // Real-time packets continue throughout; a retry gets a fresh
                // datagram sequence after the first attempt leaves the ACK window.
                queue.Enqueue(now, new(true, sender.Send(PacketType.Intent, now), 0));
                while (queue.TryDequeue(now, out var wire))
                {
                    if (!wire.ToReceiver) { sender.Receive(wire.Header, now); continue; }
                    var sequence = receiver.Receive(wire.Header, now);
                    if ((wire.Header.Flags & NetHeaderFlags.Reliable) != 0)
                    {
                        if (sequence is SequenceResult.New or SequenceResult.Reordered && receiver.Reliable.Receive(wire.EventId))
                            NetArchitectureTests.Check(applied.Add(wire.EventId), "exactly once application");
                        queue.Enqueue(now, new(false, receiver.Send(0, now, NetHeaderFlags.AckOnly), 0));
                    }
                }
            }
            NetArchitectureTests.Check(applied.Count == 40 && sender.Reliable.Capture(14000).Pending == 0, "every critical and ordinary event delivered under impairment");
            NetArchitectureTests.Check(sender.Reliable.Capture(14000).HighWater == 40 && sender.Reliable.Capture(14000).Retransmissions > 0, "bounded retries measured");
            var full = new NetReliableChannel();
            for (int i = 0; i < 40; i++) full.TryQueue(PacketType.SessionState, BitConverter.GetBytes(i), 0, out _);
            NetArchitectureTests.Check(!full.TryQueue(PacketType.Bye, new byte[] { 99 }, 0, out _) && full.Failed, "critical exhaustion fails visibly");
            var span = new NetReliableChannel(); span.TryQueue(PacketType.Roster, new byte[] { 0 }, 0, out _);
            for (int i = 1; i < NetReliableChannel.History; i++)
            {
                NetArchitectureTests.Check(span.TryQueue(PacketType.Roster, BitConverter.GetBytes(i), 0, out uint id), "dedup span admits covered events");
                span.Acknowledge(id);
            }
            NetArchitectureTests.Check(!span.TryQueue(PacketType.Roster, new byte[] { 1 }, 0, out _)
                && span.Capture(1).SpanRefused == 1, "old pending event protects receiver dedup window");
            var fastRto = new NetReliableChannel();
            fastRto.UpdateRto(20, 2);
            fastRto.TryQueue(PacketType.MatchLoaded, new byte[] { 7 }, 0, out _);
            NetArchitectureTests.Check(fastRto.TrySend(0, out _) && !fastRto.TrySend(74, out _)
                && fastRto.TrySend(75, out _) && fastRto.Capture(75).CurrentRtoMilliseconds == 75,
                "low-latency reliable retry uses bounded RTT-derived RTO");
            var slowRto = new NetReliableChannel();
            slowRto.UpdateRto(400, 50);
            slowRto.TryQueue(PacketType.MatchLoaded, new byte[] { 8 }, 0, out _);
            NetArchitectureTests.Check(slowRto.TrySend(0, out _) && !slowRto.TrySend(599, out _)
                && slowRto.TrySend(600, out _) && slowRto.Capture(600).CurrentRtoMilliseconds == 600,
                "high-latency reliable retry waits for RTT variance");
            var expiry = new NetReliableChannel(); expiry.TryQueue(PacketType.MatchLoaded, new byte[] { 1 }, 0, out _);
            expiry.TrySend(15000, out _);
            NetArchitectureTests.Check(expiry.Failed, "bounded retransmit lifetime");
            foreach (var type in new[] { PacketType.Intent, PacketType.SlotIntent, PacketType.Snapshot, PacketType.Ping, PacketType.Pong })
                NetArchitectureTests.Check(!NetReliableChannel.IsReliable(type), "realtime never reliable");
            using var transport = new NetTransport(0);
            using var blackhole = new UdpClient(0);
            var address = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)blackhole.Client.LocalEndPoint!).Port);
            transport.Send(address, PacketType.Welcome, new byte[17]);
            for (int i = 0; i <= NetReliableChannel.OrdinaryCapacity; i++)
                transport.Send(address, PacketType.LobbyCommandResult, BitConverter.GetBytes(i));
            bool disconnected = false;
            NetArchitectureTests.Check(SpinWait.SpinUntil(() =>
            {
                foreach (var packet in transport.Drain()) disconnected |= packet.Type == PacketType.Bye;
                return disconnected;
            }, 2000), "production transport surfaces ordinary control exhaustion as disconnect");
            Console.WriteLine("PASS: reliability under 5% loss / 80ms jitter / 3% reorder / 1% duplicate; capacity, dedup span and expiry"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void BackgroundInboxRetry()
    {
        using var transport = new NetTransport(0);
        using var wire = new UdpClient(0);
        wire.Client.ReceiveTimeout = 2000;
        var remote = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)wire.Client.LocalEndPoint!).Port);
        var server = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        transport.Send(remote, PacketType.Welcome, new byte[17]);
        var from = new IPEndPoint(IPAddress.Any, 0);
        byte[] welcome = wire.Receive(ref from);
        NetArchitectureTests.Check(NetHeader.TryRead(welcome, out var header), "real transport bootstrap header");
        var sender = new NetConnection(server, header.ConnectionId);
        sender.Receive(header, 0);
        var queue = (NetPacketQueue)typeof(NetTransport).GetField("_liveInbox",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(transport)!;
        byte[] intent = { (byte)PacketType.Intent };
        while (queue.CanAccept(PacketType.Intent)) queue.TryEnqueue(new(remote, intent, 1));
        NetArchitectureTests.Check(queue.CanAcceptCritical, "normal saturation retains critical reserve");
        void SendFact()
        {
            byte[] packet = new byte[NetHeader.Size + 5];
            sender.Send(PacketType.MatchSemanticEvent, 0, NetHeaderFlags.Reliable, 1).Write(packet);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(NetHeader.Size), 1);
            packet[^1] = 42;
            wire.Send(packet, server);
        }
        long drops = transport.Telemetry.Capture().QueueDrops;
        SendFact();
        NetArchitectureTests.Check(SpinWait.SpinUntil(() => transport.Telemetry.Capture().QueueDrops > drops, 2000),
            "first background reliable attempt deferred at normal inbox limit");
        while (queue.TryDequeue(NetPacketPriority.Realtime, out _)) { }
        SendFact();
        bool applied = false;
        NetArchitectureTests.Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in transport.Drain()) applied |= packet.Type == PacketType.MatchSemanticEvent && packet.Payload[0] == 42;
            return applied;
        }, 2000), "same reliable event survives inbox saturation and applies on retry");
    }

    private static void AuthorityFactBudget()
    {
        var endpoint = new IPEndPoint(IPAddress.Loopback, 42);
        var authority = new NetConnection(endpoint, 1) { ReceivesAuthorityFacts = true };
        var client = new NetConnection(endpoint, 2);
        // One semantic event and one resolution per shot, for eight continuous
        // weapons at 60 Hz. No queue increase or unlimited admission is needed.
        for (int frame = 0; frame < 240; frame++)
            for (int fact = 0; fact < 16; fact++)
                NetArchitectureTests.Check(authority.Allow(fact % 2 == 0
                    ? PacketType.MatchSemanticEvent : PacketType.ReplayShotFact, frame * 1000.0 / 60),
                    "eight-player authority facts fit the client drain budget");
        int burst = 0;
        while (authority.Allow(PacketType.MatchAward, 5000) && burst < 2048) burst++;
        NetArchitectureTests.Check(burst < 2048 && !authority.Allow(PacketType.MatchAward, 5000),
            "authority fact admission remains bounded");
        int clientBurst = 0;
        while (client.Allow(PacketType.MatchSemanticEvent, 0) && clientBurst < 2048) clientBurst++;
        NetArchitectureTests.Check(clientBurst == 32 && client.Allow(PacketType.WorldReady, 0),
            "client-to-server background budget and critical reserve remain independent");
    }

    private static void RetiredPeerAcknowledgesBye()
    {
        using var server = new NetTransport(0);
        using var client = new NetTransport(0);
        var serverAddress = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
        var clientAddress = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
        client.Send(serverAddress, PacketType.Hello, new byte[] { (byte)NetConfig.ProtocolVersion });
        server.Send(clientAddress, PacketType.Welcome, new byte[17]);
        bool welcomed = false;
        NetArchitectureTests.Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in client.Drain()) welcomed |= packet.Type == PacketType.Welcome;
            return welcomed;
        }, 2000), "close regression admitted a real UDP client");
        // Ensure the application retires this connection BEFORE ACK maintenance.
        var serviceAt = typeof(NetTransport).GetField("_nextConnectionService",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        serviceAt.SetValue(server, System.Diagnostics.Stopwatch.GetTimestamp() + 30 * System.Diagnostics.Stopwatch.Frequency);
        client.Send(serverAddress, PacketType.Bye, ReadOnlySpan<byte>.Empty);
        bool closed = false;
        NetArchitectureTests.Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in server.Drain())
                if (packet.Type == PacketType.Bye) { server.RetireConnection(packet.Sender); closed = true; }
            return closed;
        }, 2000), "application retires the peer after receiving Bye");
        serviceAt.SetValue(server, 0L);
        client.Dispose();
        NetArchitectureTests.Check(client.UnacknowledgedCloseEvents == 0,
            "retirement preserves the received Bye ACK until the socket worker sends it");
    }

    private static void RetiredPeerRetriesLostByeAck()
    {
        using var server = new NetTransport(0);
        using var wire = new UdpClient(0);
        wire.Client.ReceiveTimeout = 2000;
        var remote = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)wire.Client.LocalEndPoint!).Port);
        var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var serviceAt = typeof(NetTransport).GetField("_nextConnectionService", flags)!;
        var service = typeof(NetTransport).GetMethod("ServiceConnections", flags)!;
        // Freeze autonomous maintenance; every service turn below is explicit.
        serviceAt.SetValue(server, System.Diagnostics.Stopwatch.GetTimestamp()
            + 30 * System.Diagnostics.Stopwatch.Frequency);
        void Service() => service.Invoke(server, new object[] { true });
        var from = new IPEndPoint(IPAddress.Any, 0);
        server.Send(remote, PacketType.Welcome, new byte[17]);
        byte[] welcome = wire.Receive(ref from);
        NetArchitectureTests.Check(NetHeader.TryRead(welcome, out var header), "lost-close-ACK fixture admits UDP incarnation");
        var sender = new NetConnection(endpoint, header.ConnectionId); sender.Receive(header, 0);
        NetArchitectureTests.Check(sender.Reliable.TryQueue(PacketType.Bye, ReadOnlySpan<byte>.Empty, 0, out uint id),
            "lost-close-ACK fixture queues one reliable Bye");
        void Bye()
        {
            byte[] packet = new byte[NetHeader.Size + 4];
            sender.Send(PacketType.Bye, 0, NetHeaderFlags.Reliable, id).Write(packet);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(NetHeader.Size), id);
            wire.Send(packet, endpoint);
        }
        int applications = 0;
        Bye();
        NetArchitectureTests.Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in server.Drain())
                if (packet.Type == PacketType.Bye) { applications++; server.RetireConnection(packet.Sender); }
            return applications == 1;
        }, 2000), "first Bye retires the peer exactly once");
        Service();
        byte[] lostAck = wire.Receive(ref from);
        NetArchitectureTests.Check(NetHeader.TryRead(lostAck, out var discarded)
            && discarded.Flags.HasFlag(NetHeaderFlags.AckOnly), "first retired-peer ACK is deliberately blackholed");
        // Do NOT deliver lostAck to sender.Receive. An empty sender queue and a
        // cleared ACK flag must not destroy the receiver's retirement tombstone.
        Service();
        NetArchitectureTests.Check(server.ConnectionStats(remote) != null && sender.Reliable.HasPending(PacketType.Bye),
            "retired peer survives lost first ACK with no outgoing pending events");
        Bye();
        NetArchitectureTests.Check(SpinWait.SpinUntil(() => { Service(); return wire.Available > 0; }, 2000),
            "retired peer re-ACKs a fresh datagram retry of the same Bye");
        byte[] retryAck = wire.Receive(ref from);
        NetArchitectureTests.Check(NetHeader.TryRead(retryAck, out var acknowledged)
            && acknowledged.Flags.HasFlag(NetHeaderFlags.AckOnly), "retry produces a transport ACK");
        sender.Receive(acknowledged, 1);
        foreach (var packet in server.Drain()) if (packet.Type == PacketType.Bye) applications++;
        NetArchitectureTests.Check(!sender.Reliable.HasPending(PacketType.Bye) && applications == 1,
            "lost-ACK retry confirms close without duplicate Bye application");
        // Advance only the logical retirement age; do not wait 15 seconds.
        var connections = (Dictionary<IPEndPoint, NetConnection>)typeof(NetTransport).GetField("_connections", flags)!.GetValue(server)!;
        var connectionLock = typeof(NetTransport).GetField("_connectionLock", flags)!.GetValue(server)!;
        lock (connectionLock)
            connections[remote].RetiredAt = System.Diagnostics.Stopwatch.GetTimestamp() * 1000.0
                / System.Diagnostics.Stopwatch.Frequency - NetReliableChannel.LifetimeMilliseconds - 1;
        Service();
        NetArchitectureTests.Check(server.ConnectionStats(remote) == null, "retirement tombstone expires at its bounded lifetime");
        server.Send(remote, PacketType.Welcome, new byte[17]);
        byte[] replacement = wire.Receive(ref from);
        NetArchitectureTests.Check(NetHeader.TryRead(replacement, out var newer)
            && newer.ConnectionId != header.ConnectionId, "replacement endpoint receives a fresh incarnation");
        long invalid = server.Telemetry.Capture().Invalid;
        Bye();
        NetArchitectureTests.Check(SpinWait.SpinUntil(() => server.Telemetry.Capture().Invalid > invalid, 2000),
            "retired incarnation cannot close its replacement");
        foreach (var packet in server.Drain()) if (packet.Type == PacketType.Bye) applications++;
        NetArchitectureTests.Check(applications == 1 && server.ConnectionStats(remote)?.ConnectionId == newer.ConnectionId,
            "replacement survives stale Bye and retired application remains exactly once");
    }

    private static void FullStateStartupBurst()
    {
        var channel = new NetReliableChannel();
        var receiver = new NetReliableChannel();
        channel.TryQueue(PacketType.Roster, new byte[] { 1 }, 0, out uint oldId, supersedeState: true);
        receiver.Receive(oldId);
        channel.TryQueue(PacketType.Roster, new byte[] { 2 }, 1, out uint newId, supersedeState: true);
        channel.Acknowledge(oldId);
        NetArchitectureTests.Check(newId != oldId && receiver.Receive(newId)
            && channel.Capture(1).Pending == 1, "superseding full state has a new dedup identity and survives an old ACK");
        NetArchitectureTests.Check(channel.TrySend(1, out var latest) && latest.Payload.Span[0] == 2,
            "only latest full state retries");

        // Several join/identity/loaded/ready updates can queue while a peer is
        // constructing its scene and has not ACKed anything yet. Exercise the
        // production Send path, including three distinct bootstrap lanes.
        using var transport = new NetTransport(0);
        using var blackhole = new UdpClient(0);
        var address = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)blackhole.Client.LocalEndPoint!).Port);
        transport.Send(address, PacketType.Welcome, new byte[17]);
        for (int revision = 0; revision < 96; revision++)
        {
            transport.Send(address, PacketType.Roster, BitConverter.GetBytes(revision));
            transport.Send(address, PacketType.SessionState, BitConverter.GetBytes(revision));
        }
        for (byte lane = 0; lane < 3; lane++)
            transport.Send(address, PacketType.WorldBootstrap, new byte[] { lane });
        var stats = transport.ReliableStats(address)!.Value;
        NetArchitectureTests.Check(!stats.Failed && stats.Refused == 0 && stats.Pending == 6,
            "slow-loader state burst retains welcome, latest roster/session and every bootstrap lane without exhaustion");
    }
}
