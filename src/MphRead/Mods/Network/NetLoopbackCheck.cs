using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public static partial class NetLobbyTest
{
    /// <summary>
    /// Real dedicated simulation and UDP relay with ordinary admitted clients.
    /// Synthetic idle input measures transport/server costs; it makes no claim
    /// about rendered prediction, physical input latency or combat under loss.
    /// </summary>
    public static int RunLoopbackLoad(string[] args)
    {
        string cwd = Directory.GetCurrentDirectory();
        bool oldDiagnostics = NetDiagnostics.Enabled;
        int oldSeed = NetLag.Seed, oldRtt = NetLag.RoundTripMs, oldJitter = NetLag.JitterMs;
        double oldLoss = NetLag.LossPercent, oldReorder = NetLag.ReorderRate, oldDuplicate = NetLag.DuplicateRate;
        try
        {
            if (args.Length < 4)
                throw new ArgumentException("--loopback-load ASSET_DIRECTORY PLAYERS OUTPUT_JSON [SECONDS] [lan|moderate|severe|replay|replay-failure] [CYCLES]");
            string assets = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
            int players = int.Parse(args[2], CultureInfo.InvariantCulture);
            double seconds = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 12;
            string mode = args.Length > 5 ? args[5] : "lan";
            int cycles = args.Length > 6 ? int.Parse(args[6], CultureInfo.InvariantCulture) : 1;
            if (players is < 1 or > 8 || !double.IsFinite(seconds) || seconds is < 2 or > 120
                || cycles is < 1 or > 16 || mode is not ("lan" or "moderate" or "severe" or "replay" or "replay-failure"))
                throw new ArgumentOutOfRangeException(nameof(args));
            Directory.SetCurrentDirectory(assets);
            NetDiagnostics.Enabled = true;
            Paths.UpdatePaths(); Paths.ChooseMphPath();
            var results = new List<object>();
            for (int cycle = 0; cycle < cycles; cycle++)
                results.Add(LoopbackCycle(players, seconds, mode, cycle));
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                commit = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_COMMIT") ?? "working-tree",
                protocol = NetConfig.ProtocolVersion,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                scope = "real DedicatedServer + production UDP transport, normal admission/bootstrap, synthetic idle input; no rendered clients or combat; CPU/RSS include same-process peers",
                quantiles = "warm ServerSim step histogram deltas, 0.01 ms buckets; lifetime cold maximum separate; wholeLoop samples every main-loop body excluding pacing wait, including empty deadline-spin iterations, so its quantiles are not simulation tick costs",
                impairment = "client transports only; fixed half-RTT + nonnegative jitter per direction; server transport fault queues disabled",
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"LOOPBACK PASS {players} peers, {mode}, {cycles} start/shutdown cycle(s); {output}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            NetDiagnostics.Enabled = oldDiagnostics;
            NetLag.ConfigureSeed(oldSeed.ToString(CultureInfo.InvariantCulture));
            NetLag.Configure(oldRtt.ToString(CultureInfo.InvariantCulture));
            NetLag.ConfigureJitter(oldJitter.ToString(CultureInfo.InvariantCulture));
            NetLag.ConfigureLoss(oldLoss.ToString(CultureInfo.InvariantCulture));
            NetLag.ConfigureReorder(oldReorder.ToString(CultureInfo.InvariantCulture));
            NetLag.ConfigureDuplicate(oldDuplicate.ToString(CultureInfo.InvariantCulture));
            Directory.SetCurrentDirectory(cwd);
        }
    }

    private static object LoopbackCycle(int players, double seconds, string mode, int cycle)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        NetLag.ConfigureSeed((8128 + cycle).ToString(CultureInfo.InvariantCulture));
        NetLag.Configure("0"); NetLag.ConfigureLoss("0"); NetLag.ConfigureReorder("0"); NetLag.ConfigureDuplicate("0");
        Rig? rig = null;
        string exportBefore = Paths.Export;
        try
        {
            string? replayDirectory = null;
            if (mode is "replay" or "replay-failure")
            {
                replayDirectory = Path.Combine(Path.GetTempPath(), "prime-loopback-replay-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(replayDirectory);
                string export = replayDirectory;
                if (mode == "replay-failure")
                { export = Path.Combine(replayDirectory, "regular-file"); File.WriteAllText(export, "storage fault fixture"); }
                Paths.SetPath("Export", export);
            }
            rig = new Rig(ServerSessionPolicy.Continuous, simulate: true, room: "MP1 SANCTORUS",
                replayPolicy: new ServerReplayPolicy(Enabled: mode is "replay" or "replay-failure"));
            int port = rig.Server.BoundPort;
            // Fault queues are captured by NetTransport construction. Only the
            // clients get these queues, so each direction is impaired once.
            if (mode == "moderate")
            { NetLag.Configure("250:50"); NetLag.ConfigureLoss("5%"); NetLag.ConfigureReorder("3%"); NetLag.ConfigureDuplicate("1%"); }
            else if (mode == "severe")
            { NetLag.Configure("550:50"); NetLag.ConfigureLoss("10%"); NetLag.ConfigureReorder("3%"); NetLag.ConfigureDuplicate("1%"); }
            Client[] clients = Enumerable.Range(0, players).Select(i => rig.Add((uint)(900 + i))).ToArray();
            foreach (Client client in clients) client.Loaded();
            var transport = (NetTransport)typeof(DedicatedServer).GetField("_transport", flags)!.GetValue(rig.Server)!;
            transport.EnablePacketKindDiagnostics();
            var sim = (ServerSim)typeof(DedicatedServer).GetField("_sim", flags)!.GetValue(rig.Server)!;
            var received = new Dictionary<PacketType, (long Packets, long Bytes, int Peak)>();
            var snapshots = new long[players]; var relays = new long[players];
            var acks = new uint[players]; var frames = new uint[players];
            void Pump(bool record)
            {
                for (int i = 0; i < clients.Length; i++)
                    foreach (ReceivedPacket packet in clients[i].Transport.Drain())
                    {
                        if (record)
                        {
                            var previous = received.GetValueOrDefault(packet.Type);
                            received[packet.Type] = (previous.Packets + 1, previous.Bytes + packet.Length,
                                Math.Max(previous.Peak, packet.Length));
                        }
                        if (packet.Type == PacketType.WorldBootstrap
                            && WorldBootstrapIdentity.TryRead(packet.Payload, out var bootstrap))
                        {
                            byte[] ready = new byte[WorldBootstrapIdentity.Size]; bootstrap.Write(ready);
                            clients[i].Send(PacketType.WorldReady, ready);
                        }
                        if (packet.Type is PacketType.SnapshotFast or PacketType.Snapshot
                            && packet.Payload.Length >= SnapshotHeader.Size)
                        { acks[i] = SnapshotHeader.Read(packet.Payload).Frame; if (record) snapshots[i]++; }
                        if (packet.Type == PacketType.SlotIntent && record) relays[i]++;
                    }
            }
            void Feed()
            {
                for (int i = 0; i < clients.Length; i++)
                {
                    int slot = clients[i].Slot; var player = PlayerEntity.Players[slot];
                    var intent = new IntentPacket
                    {
                        Frame = ++frames[i], MatchId = NetSession.CurrentMatchId, AuthorityEpoch = NetSession.AuthorityEpoch,
                        SlotGeneration = NetPlayerLifecycle.Generation(slot), LifeId = NetPlayerLifecycle.Get(slot),
                        Position = player.Position, Aim = Vector3.UnitZ, WeaponSelect = 255,
                        AmmoUa = 400, AmmoMissiles = 50, AckFrame = acks[i],
                        Buttons = player.Health > 0 ? IntentButtons.InPlayState : 0
                    };
                    byte[] bytes = new byte[intent.EncodedSize]; intent.WriteNetwork(bytes);
                    clients[i].Send(PacketType.Intent, bytes);
                }
            }
            void Interval(double duration, bool record)
            {
                var clock = Stopwatch.StartNew(); double next = 0;
                while (clock.Elapsed.TotalSeconds < duration)
                {
                    if (clock.Elapsed.TotalSeconds >= next)
                    { next += 1 / 60.0; Feed(); }
                    Pump(record); Thread.Sleep(1);
                }
            }
            Interval(2, false);
            long[] Histogram() => ((long[])typeof(ServerSim).GetField("_stepHistogram", flags)!.GetValue(sim)!).ToArray();
            var before = transport.Telemetry.Capture(); var histogramBefore = Histogram();
            var loopBefore = rig.Server.LoopDiagnostics.Capture();
            PacketType[] kinds = Enum.GetValues<PacketType>().Append((PacketType)0).Distinct().ToArray();
            var kindsBefore = kinds.Select(t => transport.PacketKindDiagnostics!.Capture(t)).ToArray();
            long frameBefore = sim.Frames, allocationBefore = sim.StepAllocatedBytes, acceptedBefore = NetSession.IntentsReceived;
            long droppedBefore = sim.DroppedSteps, stallsBefore = sim.Stalls;
            using var process = Process.GetCurrentProcess(); process.Refresh();
            long rssBefore = process.WorkingSet64; var cpuBefore = process.TotalProcessorTime;
            var timer = Stopwatch.StartNew(); Interval(seconds, true); double elapsed = timer.Elapsed.TotalSeconds;
            var after = transport.Telemetry.Capture(); process.Refresh();
            var loopAfter = rig.Server.LoopDiagnostics.Capture();
            var histogram = Histogram().Select((v, i) => v - histogramBefore[i]).ToArray();
            double Quantile(double q)
            {
                long target = (long)Math.Ceiling(histogram.Sum() * q), sum = 0;
                for (int i = 0; i < histogram.Length; i++) { sum += histogram[i]; if (sum >= target) return i / 100.0; }
                return 0;
            }
            object LoopInterval(ServerLoopSample initial, ServerLoopSample final)
            {
                long[] buckets = final.Histogram == null ? Array.Empty<long>()
                    : final.Histogram.Select((n, i) => n - (initial.Histogram?[i] ?? 0)).ToArray();
                long count = buckets.Sum();
                double Percentile(double q)
                {
                    long target = (long)Math.Ceiling(count * q), sum = 0;
                    for (int i = 0; i < buckets.Length; i++)
                        if ((sum += buckets[i]) >= target) return i / 100.0;
                    return 0;
                }
                return new { count, p99Ms = Percentile(.99), p999Ms = Percentile(.999),
                    worstBucketMs = count == 0 ? 0 : Array.FindLastIndex(buckets, n => n > 0) / 100.0,
                    lifetimeWorstMs = final.WorstMilliseconds };
            }
            Check(sim.StepFailures == 0, "loopback native simulation has no failed steps");
            Check(after.QueueDrops == before.QueueDrops && after.SocketErrors == before.SocketErrors,
                "loopback server has no queue drops or socket errors");
            Check(snapshots.All(n => n > 20), "every loopback client receives continuing authority snapshots");
            Check(players == 1 || relays.All(n => n > 20), "every loopback client receives current peer intents");
            Check(clients.All(c => NetSession.RemoteIntentValid[c.Slot]), "all admitted inputs accepted by native policy");
            var replay = ServerReplayRecorder.Diagnostics;
            if (mode == "replay-failure")
                Check(replay.State == ServerReplayRecorder.RecordingState.Failed && replay.Attempts == 1 && replay.Failures == 1,
                    "a failed optional replay writer is latched after one attempt while gameplay continues");
            if (mode == "replay")
                Check(replay.State == ServerReplayRecorder.RecordingState.Recording && replay.Attempts == 1 && replay.Failures == 0,
                    "healthy canonical replay records the authority match");
            var metrics = new
            {
                cycle, players, mode, seconds = elapsed, wire = new { NetHeader.Size, intentMaximum = IntentPacket.FullSize },
                server = new { rxBytes = after.BytesReceived - before.BytesReceived, txBytes = after.BytesSent - before.BytesSent,
                    rxPackets = after.PacketsReceived - before.PacketsReceived, txPackets = after.PacketsSent - before.PacketsSent,
                    queueHigh = after.QueueHighWater, queueDrops = after.QueueDrops - before.QueueDrops,
                    socketErrors = after.SocketErrors - before.SocketErrors, invalid = after.Invalid - before.Invalid,
                    oversized = after.Oversized - before.Oversized, acceptedIntents = NetSession.IntentsReceived - acceptedBefore },
                simulation = new { steps = sim.Frames - frameBefore, p99Ms = Quantile(.99), p999Ms = Quantile(.999),
                    warmWorstBucketMs = Array.FindLastIndex(histogram, n => n > 0) / 100.0,
                    lifetimeWorstMs = sim.WorstStepSeconds * 1000, stepAllocation = sim.StepAllocatedBytes - allocationBefore,
                    sim.StepFailures, droppedStepsDuringMeasurement = sim.DroppedSteps - droppedBefore,
                    stallsDuringMeasurement = sim.Stalls - stallsBefore,
                    lifetimeDroppedSteps = sim.DroppedSteps, lifetimeStalls = sim.Stalls },
                cpuMsIncludingPeers = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds,
                rssStartIncludingPeers = rssBefore, rssEndIncludingPeers = process.WorkingSet64,
                replay = new { state = replay.State.ToString(), replay.Attempts, replay.Failures, replay.QueuedBytes, replay.Error, replayDirectory },
                wholeLoop = new { work = LoopInterval(loopBefore.Work, loopAfter.Work),
                    overBudget = loopAfter.OverBudget - loopBefore.OverBudget,
                    phases = Enum.GetValues<ServerLoopPhase>().Select((phase, i) => new
                    { phase = phase.ToString(), work = LoopInterval(loopBefore.Phases[i], loopAfter.Phases[i]) }).ToArray() },
                normalizedReceived = received.ToDictionary(p => p.Key.ToString(), p => new
                { packets = p.Value.Packets, applicationBytes = p.Value.Bytes, peakApplicationPacket = p.Value.Peak }),
                snapshots, relays, connectionStats = clients.Select(c => c.Transport.ConnectionStats(c.Server)).ToArray(),
                wireByKind = kinds.Select((t, i) =>
                {
                    var current = transport.PacketKindDiagnostics!.Capture(t); var previous = kindsBefore[i];
                    return new { type = ((byte)t == 0 ? "TransportAck" : t.ToString()),
                        packetsReceived = current.PacketsReceived - previous.PacketsReceived,
                        bytesReceived = current.BytesReceived - previous.BytesReceived,
                        packetsSent = current.PacketsSent - previous.PacketsSent,
                        bytesSent = current.BytesSent - previous.BytesSent,
                        lifetimePeakBytesReceived = current.PeakBytesReceived,
                        lifetimePeakBytesSent = current.PeakBytesSent };
                }).Where(t => t.packetsReceived + t.packetsSent != 0).ToArray()
            };
            rig.Dispose(); rig = null;
            using var bind = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            bind.Bind(new IPEndPoint(IPAddress.Loopback, port));
            Check(true, "loopback server shutdown releases its UDP port before next cycle");
            return metrics;
        }
        finally { rig?.Dispose(); Paths.SetPath("Export", exportBefore); }
    }
}
