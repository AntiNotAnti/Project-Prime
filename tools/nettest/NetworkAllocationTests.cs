using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

internal static class NetworkAllocationTests
{
    private static uint _sink;
    public static int Run(bool reportOnly = false)
    {
        try
        {
            byte[] intentBytes = NetArchitectureTests.CurrentIntentFixture();
            byte[] bytes = new byte[NetConfig.MaxPacketSize];
            var intent = IntentPacket.Read(intentBytes);
            var player = new PlayerState();
            var header = new SnapshotHeader { PlayerCount = 8 };
            var claim = new HitClaimPacket();
            var envelope = new NetHeader(PacketType.Intent, NetHeaderFlags.AckValid, 7, 9, 8, 255);
            byte[] envelopeBytes = new byte[NetHeader.Size]; envelope.Write(envelopeBytes);
            byte[] health = new byte[NetHealthSync.HeaderSize + 56 * NetHealthSync.EntrySize];
            health[2] = 56;
            for (short i = 0; i < 56; i++) BinaryPrimitives.WriteInt16LittleEndian(health.AsSpan(3 + i * NetHealthSync.EntrySize), i);
            NetHealthSync.BeginRoom();
            var operations = new Dictionary<string, Action>
            {
                ["NetHeader.Read"] = () => { NetHeader.TryRead(envelopeBytes, out var value); _sink = value.Sequence; },
                ["NetHeader.Write"] = () => envelope.Write(envelopeBytes),
                ["HitClaim.Read"] = () => _sink = HitClaimPacket.Read(bytes).ClaimId,
                ["HitClaim.Write"] = () => claim.Write(bytes),
                ["Health.Validate56"] = () => _sink = NetHealthSync.Validate(health) ? 1u : 0u,
                ["Health.Receive56"] = () => NetHealthSync.Receive(health),
                ["Health.ComposeEmpty"] = () => _sink = (uint)NetHealthSync.Write(bytes),
                ["IntentPacket.Read"] = () => _sink = IntentPacket.Read(intentBytes).Frame,
                ["IntentPacket.Write"] = () => intent.Write(bytes),
                ["IntentPacket.WriteNetwork"] = () => _sink = (uint)intent.WriteNetwork(bytes),
                ["IntentPacket.TryReadNetwork"] = () => _sink = IntentPacket.TryReadNetwork(intentBytes, out var read) ? read.Frame : 0,
                ["PlayerState.Read"] = () => _sink = PlayerState.Read(bytes).LifeId,
                ["PlayerState.Write"] = () => player.Write(bytes),
                ["Snapshot.Compose"] = () => { header.Write(bytes); for (int i = 0; i < 8; i++) player.Write(bytes.AsSpan(SnapshotHeader.Size + i * PlayerState.Size)); },
                ["Snapshot.Decode"] = () => { _sink = SnapshotHeader.Read(bytes).Frame; for (int i = 0; i < 8; i++) _sink += PlayerState.Read(bytes.AsSpan(SnapshotHeader.Size + i * PlayerState.Size)).LifeId; }
            };
            foreach (var (name, action) in operations)
            {
                for (int i = 0; i < 10000; i++) action();
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 10000; i++) action();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Console.WriteLine($"{name}: {allocated / 10000.0:F2} B/op");
                if (!reportOnly) NetArchitectureTests.Check(allocated == 0, $"{name} allocation regression: {allocated}");
            }
            using var server = new NetTransport(0);
            using var client = new NetTransport(0);
            Protocol17Tests.Connect(server, client);
            var endpoint = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
            for (int i = 0; i < 10000; i++) client.Send(endpoint, PacketType.Intent, intentBytes);
            var transportBefore = client.Telemetry.Capture();
            var contentionBefore = client.ContentionStats();
            int gen0Before = GC.CollectionCount(0), gen1Before = GC.CollectionCount(1), gen2Before = GC.CollectionCount(2);
            long sendStart = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) client.Send(endpoint, PacketType.Intent, intentBytes);
            long sendBytes = GC.GetAllocatedBytesForCurrentThread() - sendStart;
            int gen0After = GC.CollectionCount(0), gen1After = GC.CollectionCount(1), gen2After = GC.CollectionCount(2);
            var transportAfter = client.Telemetry.Capture();
            var contentionAfter = client.ContentionStats();
            Console.WriteLine($"NetTransport.Send UDP: {sendBytes / 10000.0:F2} B/op; exact allocated bytes={sendBytes}, measured calls=10000 (receive/fault/replay ownership excluded)");
            Console.WriteLine($"UDP measurement: successful sends={transportAfter.PacketsSent - transportBefore.PacketsSent}, socket errors={transportAfter.SocketErrors - transportBefore.SocketErrors}, connection lock acquisitions={contentionAfter.Acquisitions - contentionBefore.Acquisitions}, contended={contentionAfter.Contended - contentionBefore.Contended}; runtime={RuntimeInformation.FrameworkDescription}; OS={RuntimeInformation.OSDescription}; architecture={RuntimeInformation.ProcessArchitecture}");
            Console.WriteLine($"UDP measurement collections: Gen0={gen0After - gen0Before}, Gen1={gen1After - gen1Before}, Gen2={gen2After - gen2Before}; serverGC={System.Runtime.GCSettings.IsServerGC}");
            if (!reportOnly) NetArchitectureTests.Check(sendBytes == 0, $"steady connected UDP send allocation regression: {sendBytes} bytes across 10000 measured sends");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
