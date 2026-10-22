using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Text.Json;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Real loopback sockets and production pumps, deliberately separate from Scene
// simulation. Configured fault injection is not a physical WAN measurement.
internal static class TransportProfile
{
    internal static int Run(string path)
    {
        try
        {
            var emptyPump = EmptyPumpCalibration();
            var rows = new List<object>();
            foreach (int players in new[] { 2, 4, 8 })
                foreach (bool impaired in new[] { false, true }) rows.Add(Measure(players, impaired));
            File.WriteAllText(Path.GetFullPath(path), JsonSerializer.Serialize(new {
                sourceSha = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_COMMIT") ?? "working-tree",
                protocol = NetConfig.ProtocolVersion, runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                cpu = NetworkBenchmark.CpuDescription(), scope = "loopback UDP; production pumps; nominal60Hz synthetic intent, compact SnapshotFast, and control payloads; no world simulation",
                mainThreadAllocationScope = "measured send/pump loop; excludes admission, preallocated fixture buffers/sets/timing storage and result serialization; socket workers excluded",
                emptyPump,
                rows }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("TRANSPORT PROFILE PASS 2/4/8 peers, clean/impaired, exact-once reliable control and bounded queues");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { Configure(false); }
    }
    private static object EmptyPumpCalibration()
    {
        Configure(false);
        using var transport = new NetTransport(0);
        for (int i = 0; i < 256; i++) foreach (var packet in transport.Drain())
            throw new InvalidOperationException("Unexpected calibration packet.");
        const int count = 10000;
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < count; i++) foreach (var packet in transport.Drain())
            throw new InvalidOperationException("Unexpected calibration packet.");
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return new { calls = count, allocatedBytes = allocated, bytesPerCall = (double)allocated / count,
            microsecondsPerCall = elapsed * 1000 / count, scope = "empty production Drain enumeration after 256 warm calls; no peers" };
    }

    private static void Configure(bool impaired)
    {
        NetLag.Configure(impaired ? "400:40" : "0"); NetLag.ConfigureLoss(impaired ? "5%" : "0");
        NetLag.ConfigureReorder(impaired ? "3%" : "0"); NetLag.ConfigureDuplicate(impaired ? "1%" : "0");
        NetLag.ConfigureSeed("431");
    }
    private static object Measure(int players, bool impaired)
    {
        Configure(false);
        using var server = new NetTransport(0);
        var address = new IPEndPoint(IPAddress.Loopback, server.LocalPort);
        var clients = new List<NetTransport>();
        var endpoints = new IPEndPoint[players];
        var controls = new HashSet<int>[players];
        var snapshotFrames = new uint[players];
        var snapshotCount = new int[players];
        long intents = 0, pumps = 0;
        try
        {
            Configure(impaired);
            for (int i = 0; i < players; i++)
            {
                var client = new NetTransport(0); clients.Add(client); client.EnableRealtimeStateCoalescing();
                endpoints[i] = new IPEndPoint(IPAddress.Loopback, client.LocalPort); controls[i] = new HashSet<int>(16);
                byte[] hello = new byte[6]; hello[0] = NetConfig.ProtocolVersion;
                BinaryPrimitives.WriteUInt32LittleEndian(hello.AsSpan(2), (uint)(100 + i)); client.Send(address, PacketType.Hello, hello);
                byte[] welcome = new byte[17]; welcome[0] = (byte)i;
                BinaryPrimitives.WriteUInt32LittleEndian(welcome.AsSpan(1), (uint)(100 + i)); server.Send(endpoints[i], PacketType.Welcome, welcome);
            }
            void Pump(bool stalled)
            {
                foreach (var packet in server.Drain()) if (packet.Type == PacketType.Intent) intents++;
                pumps++;
                for (int i = 0; i < players; i++)
                {
                    if (stalled && i == players - 1) continue;
                    foreach (var packet in clients[i].Drain())
                    {
                        if (packet.Type == PacketType.SessionState)
                        {
                            int id = BinaryPrimitives.ReadInt32LittleEndian(packet.Payload);
                            if (!controls[i].Add(id)) throw new InvalidOperationException("Reliable control applied twice.");
                        }
                        if (packet.Type == PacketType.SnapshotFast)
                        {
                            if (!NetReplicationReceiver.ValidateFast(packet.Payload, 1, 1, out var header)
                                || header.PlayerCount != players)
                                throw new InvalidOperationException("Production fast snapshot validation/player count failed.");
                            if (SequenceMath.Newer(header.Frame, snapshotFrames[i])) { snapshotFrames[i] = header.Frame; snapshotCount[i]++; }
                        }
                    }
                    pumps++;
                }
            }
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 6000)
            {
                Pump(false);
                if (clients.All(client => client.ConnectionStats(address).HasValue)) break;
                Thread.Sleep(2);
            }
            if (clients.Any(client => !client.ConnectionStats(address).HasValue)) throw new InvalidOperationException("Admission failed.");
            // Exercise the same codecs/pump paths before measurement, then keep
            // reliable application IDs distinct from admission/warmup packets.
            byte[] intent = NetArchitectureTests.IntentFixture();
            byte[] snapshot = new byte[SnapshotHeader.Size + players * PlayerState.Size];
            byte[] fastSnapshot = new byte[SnapshotFast.MaximumEncodedSize - NetHeader.Size];
            var controlBytes = Enumerable.Range(1, 16).Select(BitConverter.GetBytes).ToArray();
            for (int i = 0; i < 30; i++) Pump(false);
            server.ResetContentionStats(); foreach (var client in clients) client.ResetContentionStats();
            var pumpDurations = new List<double>(310);
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread(), pumpBefore = pumps, intentsBefore = intents;
            clock.Restart();
            foreach (var endpoint in endpoints) foreach (var control in controlBytes) server.Send(endpoint, PacketType.SessionState, control);
            uint frame = 0; double nextTick = 0; long lateTicks = 0;
            while (clock.ElapsedMilliseconds < 5000)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                if (now < nextTick) { Thread.Sleep(1); continue; }
                if (now - nextTick > 1000.0 / 60) lateTicks++;
                nextTick += 1000.0 / 60;
                if (now < 2000)
                {
                    frame++;
                    BinaryPrimitives.WriteUInt32LittleEndian(intent, frame);
                    new SnapshotHeader { Frame = frame, MatchId = 1, AuthorityEpoch = 1, PlayerCount = (byte)players }.Write(snapshot);
                    for (int slot = 0; slot < players; slot++)
                        new PlayerState { SlotIndex = (byte)slot, SlotGeneration = 1, LifeId = 1, Health = 99 }.Write(snapshot.AsSpan(SnapshotHeader.Size + slot * PlayerState.Size));
                    int fastSnapshotLength = SnapshotFast.Write(snapshot, fastSnapshot);
                    if (fastSnapshotLength + NetHeader.Size > 1200)
                        throw new InvalidOperationException("Production fast snapshot exceeded the realtime datagram budget.");
                    for (int slot = 0; slot < players; slot++)
                    {
                        clients[slot].Send(address, PacketType.Intent, intent);
                        server.Send(endpoints[slot], PacketType.SnapshotFast, fastSnapshot.AsSpan(0, fastSnapshotLength));
                    }
                }
                long started = Stopwatch.GetTimestamp();
                Pump(now is >= 500 and < 900);
                pumpDurations.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            var serverTelemetry = server.Telemetry.Capture();
            var peerRows = new List<object>();
            for (int i = 0; i < players; i++)
            {
                var telemetry = clients[i].Telemetry.Capture();
                var reliable = server.ReliableStats(endpoints[i]);
                if (controls[i].Count != 16 || snapshotCount[i] <= 20 || telemetry.QueueDrops != 0
                    || telemetry.QueueHighWater > 2048 || reliable?.Pending != 0) throw new InvalidOperationException("Delivery/queue/reliable gate failed.");
                peerRows.Add(new { slot = i, reliableControls = controls[i].Count, snapshots = snapshotCount[i],
                    newestFrame = snapshotFrames[i], transport = telemetry, contention = clients[i].ContentionStats(), reliability = reliable });
            }
            if (serverTelemetry.QueueDrops != 0 || intents - intentsBefore <= players * 20) throw new InvalidOperationException("Server realtime delivery/queue gate failed.");
            pumpDurations.Sort();
            Console.WriteLine($"TRANSPORT PROFILE peers={players} impaired={impaired} intents={intents - intentsBefore} controls={16 * players} allocation={allocated} pumps={pumps - pumpBefore}");
            return new { players, impaired, configuredRtt = impaired ? 400 : 0, jitter = impaired ? 40 : 0,
                loss = impaired ? .05 : 0, reorder = impaired ? .03 : 0, duplicate = impaired ? .01 : 0,
                nominalHz = 60, sendMilliseconds = 2000, drainMilliseconds = 3000, clientPumpStallMilliseconds = 400,
                generatedFrames = frame, lateTicks, intents = intents - intentsBefore, pumps = pumps - pumpBefore,
                mainThreadAllocation = allocated, allocationPerPump = (double)allocated / (pumps - pumpBefore),
                pumpMeanMs = pumpDurations.Average(), pumpP95Ms = pumpDurations[(int)(pumpDurations.Count * .95)],
                pumpP99Ms = pumpDurations[(int)(pumpDurations.Count * .99)], pumpMaximumMs = pumpDurations[^1],
                server = serverTelemetry, contention = server.ContentionStats(), peers = peerRows };
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
}
