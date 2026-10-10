using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class WireTelemetryTests
{
    public static int Run()
    {
        try
        {
            var telemetry = new NetWireTelemetry();
            byte[] intent = Datagram(PacketType.Intent, NetHeaderFlags.None, 37);
            byte[] reliable = Datagram(PacketType.SessionState, NetHeaderFlags.Reliable, 11);
            byte[] ack = Datagram(0, NetHeaderFlags.AckOnly, 0);
            byte[] discovery = { (byte)PacketType.StatusQuery, 1, 2, 3 };
            telemetry.Received(intent); telemetry.Received(reliable); telemetry.Received(ack);
            telemetry.Received(discovery); telemetry.Sent(reliable); telemetry.Sent(reliable); telemetry.Sent(ack);
            var snapshot = telemetry.Capture();
            Check(snapshot.Totals.RxPackets == 4 && snapshot.Totals.RxBytes == 61 + 39 + 24 + 4,
                "all received UDP header and payload bytes");
            Check(snapshot.Totals.TxPackets == 3 && snapshot.Totals.TxBytes == 39 * 2 + 24,
                "reliable retries and ACK bytes counted on every send");
            Check(snapshot.Totals.RxMaxDatagramBytes == 61 && snapshot.Totals.TxMaxDatagramBytes == 39, "max datagrams");
            Check(snapshot.Categories[(int)NetTrafficKind.AckOnly].RxBytes == 24, "ACK lane");
            Check(snapshot.Categories[(int)NetTrafficKind.Discovery].RxBytes == 4, "plain discovery lane");
            Check(NetWireTelemetry.Classify(Datagram(PacketType.MatchAward, NetHeaderFlags.Reliable, 1)) == NetTrafficKind.MatchResults, "semantic reliable category");
            Check(NetWireTelemetry.Classify(Datagram(PacketType.ReplayShotFact, NetHeaderFlags.Reliable, 1)) == NetTrafficKind.Replay, "replay reliable category");
            foreach (var type in new[] { PacketType.Snapshot, PacketType.PlayerSlowState, PacketType.WorldState })
                Check(NetWireTelemetry.Classify(type) == NetTrafficKind.SlowWorld, "slow world classification");
            Check(NetWireTelemetry.Classify(new byte[] { NetHeader.Marker }) == NetTrafficKind.Invalid, "truncated header");
            byte[] invalid = (byte[])intent.Clone(); invalid[3] ^= 1;
            Check(NetWireTelemetry.Classify(invalid) == NetTrafficKind.Invalid, "protocol mismatch");
            invalid = Datagram(PacketType.Intent, NetHeaderFlags.Reliable, 0);
            Check(NetWireTelemetry.Classify(invalid) == NetTrafficKind.Invalid, "invalid reliable type");
            invalid = Datagram(PacketType.SessionState, NetHeaderFlags.Reliable, 0);
            Check(NetWireTelemetry.Classify(invalid.AsSpan(0, NetHeader.Size + 3)) == NetTrafficKind.Invalid, "truncated event ID");
            Check(NetWireTelemetry.Classify(Datagram(PacketType.Intent, NetHeaderFlags.AckOnly, 0)) == NetTrafficKind.Invalid, "invalid ACK type");
            Check(NetWireTelemetry.Classify(ReadOnlySpan<byte>.Empty) == NetTrafficKind.Invalid, "empty datagram");
            long start = Stopwatch.GetTimestamp(), fourMs = Stopwatch.Frequency / 250;
            telemetry.Dequeued(PacketType.Intent, start, start + fourMs);
            telemetry.Dequeued(PacketType.Intent, start, start - 1);
            telemetry.Dequeued(PacketType.Intent, start, start + Stopwatch.Frequency * 2);
            snapshot = telemetry.Capture();
            var category = snapshot.Categories[(int)NetTrafficKind.Intent];
            Check(category.DequeuedPackets == 3 && Math.Abs(category.TotalQueueAgeMilliseconds - (2000 + fourMs * 1000.0 / Stopwatch.Frequency)) < 0.001, "queue age sum");
            Check(category.MaxQueueAgeMilliseconds == 2000 && category.QueueAgeHistogram[0] == 1
                && category.QueueAgeHistogram[2] == 1 && category.QueueAgeHistogram[11] == 1, "age histogram and max");
            Check(snapshot.QueueAgeBucketUpperMilliseconds[10] == 1024 && snapshot.QueueAgeBucketUpperMilliseconds[11] == null, "JSON finite bucket edges");
            Check(snapshot.Totals.LifetimeRxBytesPerSecond == snapshot.Totals.RxBytes / snapshot.ElapsedSeconds, "lifetime byte rate");
            for (int i = 0; i < 10000; i++) Record(telemetry, intent, reliable, ack, start);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) Record(telemetry, intent, reliable, ack, start);
            Check(GC.GetAllocatedBytesForCurrentThread() == before, "warmed recording must allocate zero bytes");
            Check(NetWireTelemetry.Classify(new byte[NetConfig.MaxPacketSize + 1]) == NetTrafficKind.Invalid, "oversize datagram");
            InputAge();
            UdpIntegration();
            LagArrival();
            Console.WriteLine("Wire telemetry tests passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void InputAge()
    {
        var peer = new NetPeerTelemetry();
        uint[] ages = { 0, 1, 2, 4, 8, 16, 32, 64, 128, 256, 257 };
        foreach (uint age in ages) peer.Consumed(age);
        peer.Consumed(uint.MaxValue);
        var sample = peer.CaptureInputAge();
        Check(sample.Samples == 11 && sample.Unavailable == 1 && sample.MaximumFrames == 257
            && sample.TotalFrames == 768, "input age samples, unavailable and sum");
        for (int i = 0; i < sample.Histogram.Length; i++) Check(sample.Histogram[i] == 1, "input age inclusive bucket edges");
        peer.NewLife();
        Check(peer.CaptureInputAge().Samples == 11, "new life retains lifetime input age");
        peer.Reset(); sample = peer.CaptureInputAge();
        Check(sample.Samples == 0 && sample.Unavailable == 0 && sample.TotalFrames == 0
            && sample.MaximumFrames == 0, "input age reset");
        foreach (long count in sample.Histogram) Check(count == 0, "input histogram reset");
    }
    private static void LagArrival()
    {
        NetLag.Configure("100:0"); NetLag.ConfigureLoss("0");
        NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
        try
        {
            using var receiver = new NetTransport(0);
            // Fault queues are captured at construction; only this receiver is delayed.
            NetLag.Configure("0");
            using var sender = new UdpClient(0);
            var address = new IPEndPoint(IPAddress.Loopback, receiver.LocalPort);
            byte[] bytes = { (byte)PacketType.StatusQuery };
            sender.Send(bytes, address);
            bool delivered = false;
            double applicationAge = 0;
            Check(SpinWait.SpinUntil(() =>
            {
                foreach (var packet in receiver.Drain())
                {
                    if (packet.Type != PacketType.StatusQuery) continue;
                    delivered = true;
                    applicationAge = (Stopwatch.GetTimestamp() - packet.ArrivedAt) * 1000.0 / Stopwatch.Frequency;
                }
                return delivered;
            }, 2000), "lagged discovery datagram admitted");
            var sample = Category(receiver.Wire.Capture(), NetTrafficKind.Discovery);
            Check(sample.RxPackets == 1 && sample.RxBytes == 1 && sample.DequeuedPackets == 1,
                "injected receive hold counts socket datagram only once");
            Check(sample.TotalQueueAgeMilliseconds >= 40
                && sample.TotalQueueAgeMilliseconds - applicationAge >= 35,
                "wire queue age includes receive hold while gameplay arrival starts at admission");
        }
        finally
        {
            NetLag.Configure("0"); NetLag.ConfigureLoss("0");
            NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
        }
    }
    private static NetWireCategorySnapshot Category(NetWireSnapshot snapshot, NetTrafficKind kind)
        => snapshot.Categories[(int)kind];
    private static void UdpIntegration()
    {
        using var server = new NetTransport(0);
        using var client = new NetTransport(0);
        Protocol17Tests.Connect(server, client);
        var serverAddress = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
        var clientAddress = new IPEndPoint(IPAddress.Loopback, client.LocalPort);
        var txBefore = client.CaptureBudget(); var rxBefore = server.CaptureBudget();
        byte[] payload = { 1, 2, 3, 4, 5 };
        for (int i = 0; i < 3; i++) client.Send(serverAddress, PacketType.Intent, payload);
        int received = 0;
        Check(SpinWait.SpinUntil(() =>
        {
            foreach (var packet in server.Drain()) if (packet.Type == PacketType.Intent) received++;
            return received == 3;
        }, 2000), "three UDP intents drained");
        var tx = client.CaptureBudget(); var rx = server.CaptureBudget();
        long expected = 3 * (NetHeader.Size + payload.Length);
        Check(Category(tx.Wire, NetTrafficKind.Intent).TxBytes - Category(txBefore.Wire, NetTrafficKind.Intent).TxBytes == expected
            && Category(rx.Wire, NetTrafficKind.Intent).RxBytes - Category(rxBefore.Wire, NetTrafficKind.Intent).RxBytes == expected,
            "exact aggregate UDP intent bytes");
        Check(Category(tx.Endpoints[0].Wire, NetTrafficKind.Intent).TxBytes - Category(txBefore.Endpoints[0].Wire, NetTrafficKind.Intent).TxBytes == expected
            && Category(rx.Endpoints[0].Wire, NetTrafficKind.Intent).RxBytes - Category(rxBefore.Endpoints[0].Wire, NetTrafficKind.Intent).RxBytes == expected,
            "exact admitted endpoint UDP intent bytes");
        Check(Category(rx.Endpoints[0].Wire, NetTrafficKind.Intent).DequeuedPackets == 3, "endpoint dequeue association");
        client.EnableRealtimeStateCoalescing();
        var before = client.CaptureBudget();
        for (int i = 0; i < 4; i++) server.Send(clientAddress, PacketType.SnapshotFast, payload);
        Check(SpinWait.SpinUntil(() => client.StatePacketsCoalesced >= 3, 2000), "snapshot burst coalesced");
        // Ensure a measurable wait belongs to the surviving datagram, before Drain.
        Thread.Sleep(10);
        int snapshots = 0;
        foreach (var packet in client.Drain()) if (packet.Type == PacketType.SnapshotFast) snapshots++;
        var after = client.CaptureBudget();
        var aggregate = Category(after.Wire, NetTrafficKind.SnapshotFast);
        var endpoint = Category(after.Endpoints[0].Wire, NetTrafficKind.SnapshotFast);
        Check(snapshots == 1 && aggregate.RxPackets - Category(before.Wire, NetTrafficKind.SnapshotFast).RxPackets == 4
            && aggregate.RxBytes - Category(before.Wire, NetTrafficKind.SnapshotFast).RxBytes == 4 * (NetHeader.Size + payload.Length),
            "coalescing preserves actual UDP packet and byte counters");
        Check(aggregate.DequeuedPackets == 1 && endpoint.DequeuedPackets == 1
            && aggregate.TotalQueueAgeMilliseconds >= 8 && endpoint.TotalQueueAgeMilliseconds >= 8,
            "coalesced survivor queue age in aggregate and admitted endpoint");
    }
    private static void Record(NetWireTelemetry t, byte[] intent, byte[] reliable, byte[] ack, long start)
    { t.Received(intent); t.Sent(reliable); t.Sent(ack); t.Dequeued(PacketType.Intent, start, start + 10); }
    private static byte[] Datagram(PacketType type, NetHeaderFlags flags, int payload)
    {
        var bytes = new byte[NetHeader.Size + ((flags & NetHeaderFlags.Reliable) != 0 ? 4 : 0) + payload];
        new NetHeader(type, flags, 7, 1, 0, 0).Write(bytes);
        return bytes;
    }
    private static void Check(bool condition, string message) => NetArchitectureTests.Check(condition, message);
}
