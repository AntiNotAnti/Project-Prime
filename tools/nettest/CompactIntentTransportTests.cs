using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using MphRead.Mods.Network;

namespace MphRead.NetTest;
internal static class CompactIntentTransportTests
{
    public static int Run()
    {
        NetLag.Configure("0"); NetLag.ConfigureLoss("0"); NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
        using var server = new NetTransport(0);
        using var flood = new NetTransport(0);
        using var normal = new NetTransport(0);
        var address = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
        var floodAddress = new IPEndPoint(IPAddress.Loopback, flood.LocalPort);
        var normalAddress = new IPEndPoint(IPAddress.Loopback, normal.LocalPort);
        Admit(flood, floodAddress, 101); Admit(normal, normalAddress, 102);
        normal.EnableRealtimeStateCoalescing();
        for (byte id = 1; id <= 8; id++) server.Send(normalAddress, PacketType.SlotIntent, new byte[] { 1, id });
        Thread.Sleep(30); // Let the complete burst reach the inbox before draining.
        var distinct = new HashSet<byte>();
        PumpUntil(() => distinct.Count == 8, () =>
        {
            foreach (var packet in normal.Drain()) if (packet.Type == PacketType.SlotIntent) distinct.Add(packet.Payload[1]);
            foreach (var packet in server.Drain()) { }
        });
        NetArchitectureTests.Check(distinct.Count == 8 && normal.StatePacketsCoalesced == 0,
            "enabled snapshot coalescing preserves every distinct SlotIntent carrier on UDP");

        byte[] large = new byte[NetConfig.MaxPacketSize - NetHeader.Size];
        var elapsed = Stopwatch.StartNew();
        for (int i = 0; i < 64; i++) flood.Send(address, PacketType.Intent, large);
        for (int i = 0; i < 10; i++) normal.Send(address, PacketType.Intent, new byte[] { (byte)i });
        int flooded = 0, ordinary = 0;
        PumpUntil(() => ordinary == 10 && server.ConnectionStats(floodAddress)?.IntentByteRejections > 0, () =>
        {
            foreach (var packet in server.Drain()) if (packet.Type == PacketType.Intent)
            {
                if (packet.Sender.Equals(floodAddress)) flooded++; else if (packet.Sender.Equals(normalAddress)) ordinary++;
            }
            foreach (var packet in flood.Drain()) { }
            foreach (var packet in normal.Drain()) { }
        });
        var capture = server.CaptureBudget();
        NetArchitectureTests.Check(ordinary == 10 && server.ConnectionStats(normalAddress)?.IntentByteRejections == 0,
            "one peer's byte flood does not reject another peer's ordinary intents");
        NetArchitectureTests.Check(capture.IntentByteRejections > 0 && flooded < 64,
            "actual UDP Intent flood records explicit byte admission rejections");
        NetArchitectureTests.Check((long)flooded * NetConfig.MaxPacketSize <= NetConnection.IntentByteBurst
            + elapsed.Elapsed.TotalSeconds * NetConnection.IntentBytesPerSecond,
            "admitted Intent bytes include the complete datagram header and respect token bounds");
        NetArchitectureTests.Check(capture.Queue.QueueHighWater <= NetTransport.MaxQueuedPackets,
            "intent admission preserves bounded transport queue");
        var budget = Array.Find(NetTransport.AdmissionBudgets, value => value.Lane == "Intent");
        NetArchitectureTests.Check(budget.MaximumBytesPerSecond == 96000 && budget.ByteBurst == 38400,
            "Intent byte rate and burst are inspectable budget metadata");
        var sustained = new NetConnection(address, 1);
        for (int frame = 0; frame < 600; frame++)
            NetArchitectureTests.Check(sustained.Allow(PacketType.Intent, frame * 1000.0 / 60, 1200),
                "maximum realtime Intent remains admitted at sustained 60 Hz");
        var burst = new NetConnection(address, 2);
        for (int i = 0; i < 32; i++)
            NetArchitectureTests.Check(burst.Allow(PacketType.Intent, 0, 1200), "full 38400 byte burst admitted");
        NetArchitectureTests.Check(!burst.Allow(PacketType.Intent, 0, 1) && burst.IntentByteRejections == 1,
            "one extra datagram byte is rejected at exact byte burst boundary");
        NetArchitectureTests.Check(burst.Allow(PacketType.SlotIntent, 0, 1200),
            "authority SlotIntent does not consume uplink Intent byte budget");
        return 0;

        void Admit(NetTransport client, IPEndPoint endpoint, uint id)
        {
            byte[] hello = new byte[6]; hello[0] = NetConfig.ProtocolVersion; hello[1] = 255;
            BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), id);
            client.Send(address, PacketType.Hello, hello);
            byte[] welcome = new byte[17];
            BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), id);
            server.Send(endpoint, PacketType.Welcome, welcome);
            PumpUntil(() => client.ConnectionStats(address).HasValue, () =>
            {
                foreach (var packet in server.Drain()) { }
                foreach (var packet in client.Drain()) { }
            });
            NetArchitectureTests.Check(client.ConnectionStats(address).HasValue, "UDP peer admitted");
        }
    }
    private static void PumpUntil(Func<bool> done, Action pump)
    {
        var watch = Stopwatch.StartNew();
        while (!done() && watch.Elapsed.TotalSeconds < 2) { pump(); Thread.Sleep(2); }
        pump();
    }
}
