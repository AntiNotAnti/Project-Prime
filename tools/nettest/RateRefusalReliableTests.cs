using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using MphRead.Mods.MatchEvents;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class RateRefusalReliableTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T WithConnection<T>(NetTransport transport, IPEndPoint endpoint, Func<NetConnection, T> read)
    {
        var gate = typeof(NetTransport).GetField("_connectionLock", Hidden)!.GetValue(transport)!;
        lock (gate)
        {
            var connections = (Dictionary<IPEndPoint, NetConnection>)typeof(NetTransport)
                .GetField("_connections", Hidden)!.GetValue(transport)!;
            return read(connections[endpoint]);
        }
    }
    private static NetReceiveWindow Window(NetConnection connection)
        => (NetReceiveWindow)typeof(NetConnection).GetField("_received", Hidden)!.GetValue(connection)!;
    private static double Now => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
    private static void Check(bool value, string message) => NetArchitectureTests.Check(value, message);

    internal static int CheckRun(bool falsifyEarlyAck)
    {
        try { Run(falsifyEarlyAck); return 0; }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    internal static void Run(bool falsifyEarlyAck = false)
    {
        Check(!NetLag.Active, "rate refusal control uses device-free unmodified loopback, not injected wire loss");
        using var authority = new NetTransport(0);
        using var owner = new NetTransport(0);
        using var bridge = new UdpClient(0);
        var authorityAddress = new IPEndPoint(IPAddress.Loopback, authority.LocalPort);
        var ownerAddress = new IPEndPoint(IPAddress.Loopback, owner.LocalPort);
        var peer = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)bridge.Client.LocalEndPoint!).Port);
        var heldAwards = new Queue<byte[]>();
        // A transparent socket bridge holds only award attempts. Production
        // transport performs handshake, sequencing, admission, retry and ACK;
        // the fixture never manufactures the positive delivery or a retry.
        void PumpBridge(bool holdAwards)
        {
            while (bridge.Available > 0)
            {
                var from = new IPEndPoint(IPAddress.Any, 0);
                byte[] bytes = bridge.Receive(ref from);
                Check(from.Equals(authorityAddress) || from.Equals(ownerAddress), "bridge has only its admitted endpoints");
                if (from.Equals(authorityAddress) && NetHeader.TryRead(bytes, out var header)
                    && header.Type == PacketType.MatchAward && holdAwards)
                    heldAwards.Enqueue(bytes);
                else bridge.Send(bytes, from.Equals(authorityAddress) ? ownerAddress : authorityAddress);
            }
        }
        byte[] hello = new byte[6]; hello[0] = NetConfig.ProtocolVersion; hello[1] = 255;
        BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), 123);
        owner.Send(peer, PacketType.Hello, hello);
        byte[] welcome = new byte[17]; BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), 123);
        authority.Send(peer, PacketType.Welcome, welcome);
        bool welcomed = false;
        Check(SpinWait.SpinUntil(() =>
        {
            PumpBridge(true);
            foreach (var packet in authority.Drain()) { }
            foreach (var packet in owner.Drain()) welcomed |= packet.Type == PacketType.Welcome;
            return welcomed && authority.ReliableStats(peer) is { Pending: 0 };
        }, 3000), "actual Hello/Welcome establishes and acknowledges the frozen authority connection");
        var inbox = (NetPacketQueue)typeof(NetTransport).GetField("_liveInbox", Hidden)!.GetValue(owner)!;
        Check(inbox.Count == 0 && inbox.CanAccept(PacketType.MatchAward), "valid award has inbox capacity before rate refusal");

        const ushort match = 42; const ulong epoch = 77;
        var consumer = new MatchSemanticReceiver(); consumer.Begin(match, epoch, requireBaseline: true);
        var baseline = new MatchSemanticEventPacket(match, epoch, 1, 100, MatchSemanticEventType.MatchStarted,
            byte.MaxValue, 0, 0, byte.MaxValue, 0, 0, -1, 0, -1, 0, IsBaseline: true);
        Check(consumer.Accept(baseline), "actual authority baseline establishes source event one");
        var award = new MatchAwardPacket(match, epoch, 1, 1, 110, 0, 1, 1, MatchAwardKind.FirstBlood);
        byte[] encoded = new byte[MatchAwardPacket.Size]; award.Write(encoded);
        Check(MatchAwardPacket.TryRead(encoded, out var roundtrip) && roundtrip == award, "real authority award codec roundtrip");
        byte[] payload = encoded.AsSpan(1).ToArray();
        double bucketClock = Now + 60_000;
        var before = WithConnection(owner, peer, connection =>
        {
            Check(connection.ReceivesAuthorityFacts, "Welcome-derived recipient has the authority fact lane");
            for (int i = 0; i < NetConnection.AuthorityFactBurst; i++)
                Check(connection.Allow(PacketType.MatchAward, bucketClock), "existing finite authority bucket admits its burst");
            Check(!connection.Allow(PacketType.MatchAward, bucketClock), "existing authority bucket is exhausted without inbox pressure");
            return Window(connection);
        });
        long initialDrops = owner.Telemetry.Capture().QueueDrops;
        long initialRetransmissions = authority.ReliableStats(peer)!.Value.Retransmissions;
        authority.Send(peer, PacketType.MatchAward, payload);
        Check(SpinWait.SpinUntil(() => { PumpBridge(true); return heldAwards.Count > 0; }, 3000), "production transport emits the first award attempt");
        byte[] first = heldAwards.Dequeue();
        Check(NetHeader.TryRead(first, out var firstHeader) && firstHeader.Type == PacketType.MatchAward
            && (firstHeader.Flags & NetHeaderFlags.Reliable) != 0, "valid award uses actual reliable envelope");
        uint eventId = BinaryPrimitives.ReadUInt32LittleEndian(first.AsSpan(NetHeader.Size));
        bridge.Send(first, ownerAddress);
        Check(SpinWait.SpinUntil(() => owner.Telemetry.Capture().QueueDrops > initialDrops, 3000), "actual datagram is refused by the exhausted rate lane");
        WithConnection(owner, peer, connection =>
        {
            var after = Window(connection);
            Check(inbox.Count == 0 && inbox.CanAccept(PacketType.MatchAward), "rate refusal is not an inbox-full refusal");
            Check(after.Initialized == before.Initialized && after.Latest == before.Latest && after.Bits == before.Bits
                && !connection.Reliable.AlreadyReceived(eventId), "refusal updates neither ACK bitmap nor reliable event history");
            return 0;
        });
        Check(consumer.Awards.Count == 0 && consumer.PresentedAward == 0, "refused valid award has no downstream application");
        Console.WriteLine($"RATE_REFUSAL event={eventId} sequence={firstHeader.Sequence} drops={owner.Telemetry.Capture().QueueDrops - initialDrops} inbox={inbox.Count} receiveLatest={before.Latest} receiveBits={before.Bits} applications={consumer.Awards.Count}");

        if (falsifyEarlyAck)
        {
            // Negative control only: an admitted endpoint falsely ACKs the
            // refused sequence. The unchanged no-ACK/pending oracle must fail.
            NetHeader falseAck = WithConnection(owner, peer, connection => connection.Send(0, Now, NetHeaderFlags.AckOnly))
                with { Flags = NetHeaderFlags.AckValid | NetHeaderFlags.AckOnly, Ack = firstHeader.Sequence, AckBits = 0 };
            byte[] bytes = new byte[NetHeader.Size]; falseAck.Write(bytes); bridge.Send(bytes, authorityAddress);
            Check(SpinWait.SpinUntil(() => authority.ReliableStats(peer) is { Pending: 0 }, 3000), "negative control delivers a false early ACK");
            Console.WriteLine($"EARLY_ACK_FALSIFIER event={eventId} sequence={firstHeader.Sequence} pending=0 applications={consumer.Awards.Count}");
        }
        var ready = new WorldBootstrapIdentity(new(match, epoch, 1), 1, 1, 1);
        byte[] readyBytes = new byte[WorldBootstrapIdentity.Size]; ready.Write(readyBytes);
        Check(WorldBootstrapIdentity.TryRead(readyBytes, out _), "reverse control has an actual valid WorldReady codec");
        owner.Send(peer, PacketType.WorldReady, readyBytes);
        bool readyDelivered = false;
        Check(SpinWait.SpinUntil(() =>
        {
            PumpBridge(true);
            foreach (var packet in authority.Drain()) readyDelivered |= packet.Type == PacketType.WorldReady && packet.Payload.SequenceEqual(readyBytes);
            foreach (var packet in owner.Drain()) Check(packet.Type != PacketType.MatchAward, "held/refused award cannot reach the application");
            return readyDelivered && owner.ReliableStats(peer) is { Pending: 0 };
        }, 3000), "ACK-only bypasses exhausted authority lane and closes independent reverse control");
        Check(authority.ReliableStats(peer) is { Pending: 1, Failed: false }, "refused award remains pending despite unrelated ACK traffic");
        Console.WriteLine($"NO_ACK event={eventId} reverseWorldReadyClosed=true targetPending=1");
        WithConnection(owner, peer, connection =>
        {
            var receive = Window(connection);
            Check(!NetReceiveWindow.Acknowledges(firstHeader.Sequence, receive.Latest, receive.Bits), "unrelated ACK-only advancement does not ACK the refused sequence");
            Check(connection.Allow(PacketType.MatchAward, bucketClock + 1000), "same bounded bucket refills through the existing time-based limiter");
            return 0;
        });
        Check(SpinWait.SpinUntil(() => { PumpBridge(true); return heldAwards.Count > 0; }, 3000), "existing automatic reliable retry is produced without manual requeue");
        byte[] retry = heldAwards.Dequeue();
        Check(NetHeader.TryRead(retry, out var retryHeader) && SequenceMath.Newer(retryHeader.Sequence, firstHeader.Sequence)
            && BinaryPrimitives.ReadUInt32LittleEndian(retry.AsSpan(NetHeader.Size)) == eventId
            && retry.AsSpan(NetHeader.Size + 4).SequenceEqual(payload), "automatic retry retains exact event/payload with fresh datagram sequence");
        bridge.Send(retry, ownerAddress);
        int wireAwards = 0, presented = 0;
        void DrainOwner()
        {
            foreach (var packet in owner.Drain())
            {
                if (packet.Type != PacketType.MatchAward) continue;
                wireAwards++;
                byte[] full = new byte[MatchAwardPacket.Size]; full[0] = (byte)packet.Type; packet.Payload.CopyTo(full.AsSpan(1));
                Check(packet.Payload.SequenceEqual(payload), "actual delivered authority award retains exact bytes");
                Check(MatchAwardPacket.TryRead(full, out var decoded), "actual delivered authority award retains a valid codec");
                consumer.Accept(decoded);
                consumer.DrainPresentation(_ => throw new InvalidOperationException("unexpected source fact"), value =>
                { Check(value == award, "downstream applies the exact original award"); presented++; });
            }
        }
        Check(SpinWait.SpinUntil(() =>
        {
            PumpBridge(false); foreach (var packet in authority.Drain()) { } DrainOwner();
            return presented == 1 && authority.ReliableStats(peer) is { Pending: 0, Failed: false };
        }, 3000), "actual retry applies award once and closes sender through production ACK");
        // Remaining copies of that logical event are deduplicated by transport.
        while (heldAwards.TryDequeue(out var copy)) bridge.Send(copy, ownerAddress);
        // A new producer event carrying the same immutable award must also be
        // suppressed by the actual downstream consumer, not just transport.
        authority.Send(peer, PacketType.MatchAward, payload, immediateCopies: 3);
        Check(SpinWait.SpinUntil(() =>
        {
            PumpBridge(false); foreach (var packet in authority.Drain()) { } DrainOwner();
            return wireAwards == 2 && authority.ReliableStats(peer) is { Pending: 0, Failed: false };
        }, 3000), "new reliable event and duplicate attempts close without replaying the logical award");
        Check(consumer.Awards.Count == 1 && consumer.PresentedAward == 1 && presented == 1
            && !consumer.PresentationOverrun, "downstream immutable award/presentation remains exactly once");
        Check(authority.ReliableStats(peer)!.Value.Retransmissions > initialRetransmissions
            && owner.Telemetry.Capture().QueueDrops > initialDrops, "positive refusal/retry denominator is retained");
        Console.WriteLine($"RECOVERY event={eventId} firstSequence={firstHeader.Sequence} retrySequence={retryHeader.Sequence} retransmissions={authority.ReliableStats(peer)!.Value.Retransmissions - initialRetransmissions} wireApplications={wireAwards} logicalAwards={consumer.Awards.Count} presentationCallbacks={presented} pending=0 failed=false");
        Console.WriteLine("PASS: actual authority rate refusal withholds ACK; automatic same-event retry and downstream once-only award close");
    }
}
