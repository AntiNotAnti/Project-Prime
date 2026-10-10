using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class TransportBoundaryTests
{
    internal static void Run()
    {
        HeaderAndIdentity();
        SequenceBoundaries();
        TokenRefill();
        UnauthorizedAndMalformedIngress();
    }

    private static void HeaderAndIdentity()
    {
        var expected = new NetHeader(PacketType.Intent, NetHeaderFlags.AckValid,
            0x1020304050607080, uint.MaxValue, 0, 0x80000001);
        byte[] bytes = new byte[NetHeader.Size];
        expected.Write(bytes);
        NetArchitectureTests.Check(NetHeader.TryRead(bytes, out var actual) && actual == expected,
            "header preserves connection identity and unsigned sequence/ACK fields");
        for (int length = 0; length < NetHeader.Size; length++)
            NetArchitectureTests.Check(!NetHeader.TryRead(bytes.AsSpan(0, length), out _),
                "every truncated header is rejected without throwing");
        foreach (int offset in new[] { 0, 2, 3 })
        {
            byte saved = bytes[offset];
            bytes[offset] = offset == 2 ? (byte)8 : unchecked((byte)(saved + 1));
            NetArchitectureTests.Check(!NetHeader.TryRead(bytes, out _), "invalid marker, flags or protocol rejected");
            bytes[offset] = saved;
        }
        (expected with { ConnectionId = 0 }).Write(bytes);
        NetArchitectureTests.Check(!NetHeader.TryRead(bytes, out _), "zero connection identity rejected");
        byte[] ack = new byte[NetHeader.Size + 1];
        (expected with { Flags = NetHeaderFlags.AckOnly }).Write(ack);
        NetArchitectureTests.Check(NetHeader.TryRead(ack.AsSpan(0, NetHeader.Size), out _)
            && !NetHeader.TryRead(ack, out _), "ACK-only packet cannot carry trailing payload");
        var endpoint = new IPEndPoint(IPAddress.Loopback, 42);
        var connection = new NetConnection(endpoint, expected.ConnectionId);
        NetArchitectureTests.Check(connection.Accepts(new IPEndPoint(IPAddress.Loopback, 42), expected)
            && !connection.Accepts(new IPEndPoint(IPAddress.Loopback, 43), expected)
            && !connection.Accepts(new IPEndPoint(IPAddress.Parse("127.0.0.2"), 42), expected)
            && !connection.Accepts(endpoint, expected with { ConnectionId = expected.ConnectionId + 1 }),
            "admission requires both endpoint address/port and connection incarnation");
    }

    private static void SequenceBoundaries()
    {
        var window = new NetReceiveWindow();
        NetArchitectureTests.Check(window.Observe(uint.MaxValue - 1) == SequenceResult.New
            && window.Observe(0) == SequenceResult.New
            && window.Observe(uint.MaxValue) == SequenceResult.Reordered
            && window.Observe(uint.MaxValue) == SequenceResult.Duplicate
            && window.Observe(0) == SequenceResult.Duplicate,
            "sequence wrap accepts unseen reordering and rejects duplicate ingress");
        NetArchitectureTests.Check(NetReceiveWindow.Acknowledges(uint.MaxValue, window.Latest, window.Bits)
            && NetReceiveWindow.Acknowledges(uint.MaxValue - 1, window.Latest, window.Bits)
            && !NetReceiveWindow.Acknowledges(1, window.Latest, window.Bits),
            "ACK bits cross sequence wrap without acknowledging future packets");
        window = new NetReceiveWindow();
        window.Observe(100);
        NetArchitectureTests.Check(window.Observe(132) == SequenceResult.New && window.Bits == 0x80000000
            && window.Observe(100) == SequenceResult.Duplicate
            && window.Observe(99) == SequenceResult.TooOld
            && window.Observe(101) == SequenceResult.Reordered,
            "32-packet ACK boundary retains oldest bit and rejects packets outside window");
        window.Observe(165);
        NetArchitectureTests.Check(window.Bits == 0 && window.Observe(132) == SequenceResult.TooOld,
            "jump beyond ACK window clears stale bits");
        var sender = new NetConnection(new IPEndPoint(IPAddress.Loopback, 42), 1,
            initialSequence: uint.MaxValue);
        var receiver = new NetConnection(new IPEndPoint(IPAddress.Loopback, 42), 1);
        var first = sender.Send(PacketType.Intent, 0);
        var second = sender.Send(PacketType.Intent, 1);
        receiver.Receive(second, 2);
        receiver.Receive(first, 3);
        var ack = receiver.Send(0, 4, NetHeaderFlags.AckOnly);
        sender.Receive(ack, 5);
        sender.Receive(ack, 6);
        NetArchitectureTests.Check(first.Sequence == uint.MaxValue && second.Sequence == 0
            && sender.Capture().Acknowledged == 2, "wrapped send attempts acknowledge exactly once");
    }

    private static void TokenRefill()
    {
        var bucket = new NetTokenBucket();
        for (int i = 0; i < 3; i++)
            NetArchitectureTests.Check(bucket.Take(0, 4, 3), "initial burst admits exact capacity");
        NetArchitectureTests.Check(!bucket.Take(0, 4, 3) && !bucket.Take(249, 4, 3)
            && bucket.Take(250, 4, 3) && !bucket.Take(250, 4, 3),
            "fractional refill cannot admit early or admit twice at refill boundary");
        for (int i = 0; i < 3; i++)
            NetArchitectureTests.Check(bucket.Take(100000, 4, 3), "idle refill capped to configured burst");
        NetArchitectureTests.Check(!bucket.Take(100000, 4, 3), "long idle cannot accumulate unlimited credit");
    }

    private static void UnauthorizedAndMalformedIngress()
    {
        using var transport = new NetTransport(0);
        using var authorized = new UdpClient(0);
        using var stranger = new UdpClient(0);
        authorized.Client.ReceiveTimeout = 2000;
        var remote = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)authorized.Client.LocalEndPoint!).Port);
        var server = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
        transport.Send(remote, PacketType.Welcome, new byte[17]);
        var from = new IPEndPoint(IPAddress.Any, 0);
        byte[] welcome = authorized.Receive(ref from);
        NetArchitectureTests.Check(NetHeader.TryRead(welcome, out var header), "UDP admission bootstrap header");
        var sender = new NetConnection(server, header.ConnectionId);
        sender.Receive(header, 0);
        byte[] ack = new byte[NetHeader.Size];
        sender.Send(0, 1, NetHeaderFlags.AckOnly).Write(ack);
        authorized.Send(ack, server);

        byte[] packet = new byte[NetHeader.Size + 1];
        var ingress = sender.Send(PacketType.Intent, 2);
        packet[^1] = 99;
        long invalid = transport.Telemetry.Capture().Invalid;
        (ingress with { ConnectionId = ingress.ConnectionId ^ 1 }).Write(packet);
        authorized.Send(packet, server);
        ingress.Write(packet);
        stranger.Send(packet, server);
        NetArchitectureTests.Check(SpinWait.SpinUntil(() => transport.Telemetry.Capture().Invalid >= invalid + 2, 2000),
            "real UDP rejects wrong incarnation and stranger endpoint");
        NetArchitectureTests.Check(transport.ConnectionStats(new IPEndPoint(IPAddress.Loopback,
            ((IPEndPoint)stranger.Client.LocalEndPoint!).Port)) == null,
            "unauthorized ingress cannot create a connection");
        foreach (var received in transport.Drain())
            NetArchitectureTests.Check(received.Type != PacketType.Intent, "unauthorized intents never reach application inbox");

        // A fixed flood exercises every truncated header shape without a producer
        // loop that can hide unbounded ingress or a dead socket worker.
        invalid = transport.Telemetry.Capture().Invalid;
        for (int length = 1; length < NetHeader.Size; length++)
            authorized.Send(packet.AsSpan(0, length).ToArray(), server);
        byte[] malformed = (byte[])packet.Clone();
        malformed[2] = 8;
        authorized.Send(malformed, server);
        NetArchitectureTests.Check(SpinWait.SpinUntil(() => transport.Telemetry.Capture().Invalid >= invalid + NetHeader.Size, 2000),
            "bounded malformed UDP flood rejects every truncated header and invalid flags");
        ingress.Write(packet);
        packet[^1] = 42;
        authorized.Send(packet, server);
        int delivered = 0;
        NetArchitectureTests.Check(SpinWait.SpinUntil(() =>
        {
            foreach (var received in transport.Drain())
            {
                NetArchitectureTests.Check(received.Type != PacketType.Intent || received.Payload[0] == 42,
                    "only authorized ingress survives malformed flood");
                if (received.Type == PacketType.Intent) delivered++;
            }
            return delivered == 1;
        }, 2000), "authorized endpoint remains live after rejected UDP ingress");
        NetArchitectureTests.Check(transport.Telemetry.Capture().QueueHighWater <= 2048,
            "malformed UDP pressure respects inbox bound");
    }
}
