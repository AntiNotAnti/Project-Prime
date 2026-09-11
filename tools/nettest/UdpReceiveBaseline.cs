using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Measurement-only receive baseline. The sender side uses real localhost UDP
/// sockets and the receiver is the production UdpTransport; this command does
/// not change transport allocation or queue policy.
/// </summary>
internal static class UdpReceiveBaseline
{
    private const int SmokeSeconds = 30;
    private const int MeasurementSeconds = 60;
    private const int PacketsPerSecondPerPlayer = 60;
    private const int MaxDurationSeconds = 3600;
    private const int SelfTestMilliseconds = 600;
    private const int MaxPacketsPerDrain = 256;
    private const int MaximumCatchUpTicks = 4;
    private const int QuietDrainMilliseconds = 25;
    private const int MaximumDrainMilliseconds = 500;
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(5);
    private static readonly int[] DefaultLoads = [1, 8, 16, 32];

    public static int Run(string[] args)
    {
        if (!TryParse(args, out Options? options, out string? error))
        {
            Console.Error.WriteLine(error);
            return 2;
        }

        try
        {
            UdpReceiveReport report = Execute(options!);
            string path = Path.GetFullPath(options!.OutputPath);
            string? directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"Wrote localhost UDP receive baseline: {path}");
            foreach (UdpReceiveCase measurement in report.Cases)
            {
                Console.WriteLine($"load={measurement.Players} "
                    + $"sent={measurement.PacketsSentPerSecond:0.0} pps "
                    + $"received={measurement.PacketsReceivedPerSecond:0.0} pps "
                    + $"allocated={measurement.ManagedAllocatedBytesPerSecond:0.0} B/s "
                    + $"drops={measurement.QueueDrops} status={measurement.Status}");
            }
            return report.Cases.Any(static value => value.Status != "ok") ? 1 : 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"UDP receive baseline failed: {exception}");
            return 1;
        }
    }

    /// <summary>
    /// Parser, wire-shape, and one short real-socket exercise. This is kept
    /// separate from the 30/60 second operator runs so CI stays bounded.
    /// </summary>
    public static int SelfTest()
    {
        string output = Path.Combine(Path.GetTempPath(), $"prime-udp-baseline-self-test-{Guid.NewGuid():N}.json");
        string[] args =
        [
            "--udp-receive-baseline", output,
            "--mode", "smoke",
            "--duration-ms", SelfTestMilliseconds.ToString(),
            "--loads", "1"
        ];
        if (!TryParse(args, out Options? options, out string? error))
        {
            Console.Error.WriteLine($"UDP receive baseline self-test parser failed: {error}");
            return 1;
        }
        byte[] selfTestKey = BuildKey(1);
        byte[] malformedEvent = BuildEventDatagram(1, selfTestKey);
        Array.Resize(ref malformedEvent, malformedEvent.Length - 1);
        bool parserBoundaries = TryParse(new[]
                { "--udp-receive-baseline", output, "--duration-ms", "50", "--loads", "256" },
                out Options? minimumOptions, out _)
            && minimumOptions!.DurationMilliseconds == 50
            && minimumOptions.Loads.SequenceEqual([256])
            && TryParse(new[]
                { "--udp-receive-baseline", output, "--duration", MaxDurationSeconds.ToString(), "--loads", "1" },
                out Options? maximumOptions, out _)
            && maximumOptions!.DurationMilliseconds == MaxDurationSeconds * 1000
            && !TryParse(new[]
                { "--udp-receive-baseline", output, "--unknown" },
                out _, out _);
        bool malformedParserRejected = parserBoundaries
            && !TryParse(new[]
                { "--udp-receive-baseline", output, "--duration-ms", "49" },
                out _, out _)
            && !TryParse(new[]
                { "--udp-receive-baseline", output, "--loads", "1,1" },
                out _, out _)
            && NetHeader.TryRead(BuildInputDatagram(1, selfTestKey), out _)
            && !ReliableEventPacket.TryRead(malformedEvent[NetHeader.Size..], out _, out _, out _);
        bool wireShape = options!.Loads.SequenceEqual([1])
            && options.DurationMilliseconds == SelfTestMilliseconds
            && options.Mode == "smoke"
            && BuildInputDatagram(1, selfTestKey).Length > NetHeader.Size
            && BuildAckDatagram(1, selfTestKey).Length == NetAuthentication.AuthenticatedSize(0)
            && BuildEventDatagram(1, selfTestKey).Length > NetHeader.Size + ReliableEventPacket.HeaderSize
            && NetAuthentication.TryVerify(selfTestKey, NetAuthDirection.ClientToServer,
                BuildInputDatagram(1, selfTestKey), out _, out _)
            && NetAuthentication.TryVerify(selfTestKey, NetAuthDirection.ClientToServer,
                BuildAckDatagram(1, selfTestKey), out _, out _)
            && NetAuthentication.TryVerify(selfTestKey, NetAuthDirection.ClientToServer,
                BuildEventDatagram(1, selfTestKey), out _, out _);
        if (!wireShape || !malformedParserRejected)
        {
            Console.Error.WriteLine("UDP receive baseline self-test failed: parser, malformed boundary, or signed wire shape mismatch.");
            return 1;
        }

        try
        {
            UdpReceiveReport report = Execute(options);
            UdpReceiveCase measurement = report.Cases.Single();
            string json = JsonSerializer.Serialize(report);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            JsonElement jsonCase = root.GetProperty("Cases")[0];
            string roundTripJson = JsonSerializer.Serialize(root);
            using JsonDocument roundTrip = JsonDocument.Parse(roundTripJson);
            JsonElement roundTripRoot = roundTrip.RootElement;
            long accountedPackets = jsonCase.GetProperty("InputPacketsSent").GetInt64()
                + jsonCase.GetProperty("AckPacketsSent").GetInt64()
                + jsonCase.GetProperty("EventPacketsSent").GetInt64();
            long accountedBytes = jsonCase.GetProperty("InputBytesSent").GetInt64()
                + jsonCase.GetProperty("AckBytesSent").GetInt64()
                + jsonCase.GetProperty("EventBytesSent").GetInt64();
            bool jsonShape = root.GetProperty("SchemaVersion").GetInt32() == 1
                && root.GetProperty("RenderedWanProof").GetBoolean() == false
                && root.GetProperty("QueueHighWaterSource").GetString() == "UdpTransport.enqueue-side-exact"
                && roundTripRoot.GetProperty("Kind").GetString() == "ProjectPrime.LocalhostUdpReceiveBaseline"
                && jsonCase.GetProperty("PacketsSent").GetInt64() == accountedPackets
                && jsonCase.GetProperty("BytesSent").GetInt64() == accountedBytes
                && jsonCase.GetProperty("ExpectedSchedulerIntervals").GetInt64() > 0
                && jsonCase.GetProperty("PacketsSentMinusReceived").GetInt64() == 0
                && jsonCase.GetProperty("PacketsReceivedMinusDrained").GetInt64() == 0
                && !jsonCase.GetProperty("ZeroReceiverDelivery").GetBoolean();
            bool passed = measurement.Status == "ok"
                && measurement.PacketsSent > 0
                && measurement.PacketsReceived > 0
                && measurement.AckPacketsSent > 0
                && measurement.EventPacketsSent > 0
                && measurement.LocalPort > 0
                && measurement.RenderedWanProof == false
                && report.ProcessLocalEvidence
                && jsonShape;
            Console.WriteLine($"UDP receive baseline self-test: {(passed ? "PASS" : "FAIL")} "
                + $"sent={measurement.PacketsSent} received={measurement.PacketsReceived} "
                + $"drops={measurement.QueueDrops}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"UDP receive baseline self-test failed: {ex}");
            return 1;
        }
        finally
        {
            try { if (File.Exists(output)) File.Delete(output); }
            catch { /* The self-test report is only a temporary diagnostic. */ }
        }
    }

    private static UdpReceiveReport Execute(Options options)
    {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        var measurements = new List<UdpReceiveCase>(options.Loads.Length);
        foreach (int players in options.Loads)
            measurements.Add(RunLoad(options, players));
        return new UdpReceiveReport
        {
            SchemaVersion = 1,
            Kind = "ProjectPrime.LocalhostUdpReceiveBaseline",
            RunId = Guid.NewGuid(),
            StartedUtc = startedUtc,
            Mode = options.Mode,
            DurationSeconds = options.DurationMilliseconds / 1000.0,
            PacketsPerSecondPerPlayer = PacketsPerSecondPerPlayer,
            Loads = options.Loads.ToArray(),
            ProcessLocalEvidence = true,
            RenderedWanProof = false,
            EvidenceClass = "localhost-udp-process-local-measurement",
            ManagedAllocationCaveat = "Managed allocation is measured process-wide with GC.GetTotalAllocatedBytes; the current-thread value is reported separately and includes the harness thread, not an isolated UdpTransport worker allocation.",
            QueueHighWaterSource = "UdpTransport.enqueue-side-exact",
            SchedulerPolicy = "absolute-deadline-60Hz-with-bounded-catch-up",
            RepresentativeTraffic = "current-protocol signed InputBundle, Ack, and ClientReady reliable-event datagrams are prebuilt before measurement and reused by the transport-only load",
            Runtime = new RuntimeFacts(),
            Cases = measurements.ToArray()
        };
    }

    private static UdpReceiveCase RunLoad(Options options, int players)
    {
        long setupStart = Stopwatch.GetTimestamp();
        INetTransport? transport = null;
        Process? process = null;
        Socket[] senders = Array.Empty<Socket>();
        try
        {
            transport = CreateWorkerTransport();
            var target = new IPEndPoint(IPAddress.Loopback, transport.LocalPort);
            senders = new Socket[players];
            var input = new byte[players][];
            var ack = new byte[players][];
            var events = new byte[players][];
            for (int player = 0; player < players; player++)
            {
                senders[player] = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                byte[] key = BuildKey((ulong)(player + 1));
                input[player] = BuildInputDatagram((ulong)(player + 1), key);
                ack[player] = BuildAckDatagram((ulong)(player + 1), key);
                events[player] = BuildEventDatagram((ulong)(player + 1), key);
            }
            var drain = new ReceivedPacket[MaxPacketsPerDrain];
            var sentByClass = new long[3];
            var bytesByClass = new long[3];
            if (!TryWithin(setupStart, SetupTimeout))
                return Failure(players, transport.LocalPort, "setup-timeout", "UDP transport setup and datagram prebuild exceeded the setup interval.");

            // Establish every allocation, CPU, and GC baseline after all setup
            // arrays and signed datagrams exist. The receive worker remains
            // production UdpTransport; allocations after this point are
            // measurement data.
            process = Process.GetCurrentProcess();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
            long allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
            long threadAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            TimeSpan cpuBefore = process.TotalProcessorTime;
            bool gcPauseAvailable = TryGetTotalPauseDuration(out TimeSpan pauseBefore);
            long dropsBefore = transport.PacketsDropped;
            long packetsAttempted = 0;
            long packetsSent = 0;
            long bytesSent = 0;
            long packetsDrained = 0;
            long bytesDrained = 0;
            int sendErrors = 0;
            long start = Stopwatch.GetTimestamp();
            long deadline = start + DurationTicks(options.DurationMilliseconds);
            long expectedSchedulerIntervals = ExpectedSchedulerIntervals(options.DurationMilliseconds);
            long nextTickIndex = 0;
            long schedulerIntervalsEmitted = 0;
            long skippedSchedulerIntervals = 0;
            while (true)
            {
                long now = Stopwatch.GetTimestamp();
                if (now >= deadline) break;
                long schedulerElapsedTicks = Math.Max(0, now - start);
                long dueTickCount = schedulerElapsedTicks * PacketsPerSecondPerPlayer / Stopwatch.Frequency + 1;
                long due = dueTickCount - nextTickIndex;
                if (due > 0)
                {
                    // The deadline is absolute. Emit only a bounded catch-up
                    // suffix and report intervals skipped during a stall.
                    int emit = (int)Math.Min(MaximumCatchUpTicks, due);
                    skippedSchedulerIntervals += due - emit;
                    long firstTick = nextTickIndex + due - emit;
                    long finalTick = nextTickIndex + due;
                    for (long scheduledTick = firstTick; scheduledTick < finalTick; scheduledTick++)
                    {
                        long tick = scheduledTick + 1;
                        for (int player = 0; player < players; player++)
                        {
                            Send(senders[player], target, input[player],
                                ref packetsAttempted, ref packetsSent, ref bytesSent,
                                sentByClass, bytesByClass, 0, ref sendErrors);
                            // ACKs and reliable events are intentionally
                            // lower-rate representative traffic, not a fake
                            // high-rate substitute for gameplay packets.
                            if (tick % 6 == 0)
                                Send(senders[player], target, ack[player],
                                    ref packetsAttempted, ref packetsSent, ref bytesSent,
                                    sentByClass, bytesByClass, 1, ref sendErrors);
                            if (tick % 30 == 0)
                                Send(senders[player], target, events[player],
                                    ref packetsAttempted, ref packetsSent, ref bytesSent,
                                    sentByClass, bytesByClass, 2, ref sendErrors);
                        }
                    }
                    schedulerIntervalsEmitted += emit;
                    nextTickIndex += due;
                }

                int count = transport.Drain(drain);
                packetsDrained += count;
                for (int i = 0; i < count; i++) bytesDrained += drain[i].Length;
                if (count == 0) Thread.Sleep(1);
            }
            long schedulerEnd = Stopwatch.GetTimestamp();

            // Give the worker a bounded quiet drain window. A single empty
            // poll is not enough: the receive worker may be between socket
            // wakeup and enqueue. Stop only after quiet time or the hard cap.
            long drainStart = schedulerEnd;
            long drainDeadline = drainStart + MillisecondsToTicks(MaximumDrainMilliseconds);
            long quietSince = drainStart;
            while (true)
            {
                int count = transport.Drain(drain);
                packetsDrained += count;
                for (int i = 0; i < count; i++) bytesDrained += drain[i].Length;
                long now = Stopwatch.GetTimestamp();
                if (count == 0 && transport.QueuedPackets == 0 && transport.HeldIncomingPackets == 0)
                {
                    if (now - quietSince >= MillisecondsToTicks(QuietDrainMilliseconds)) break;
                }
                else
                {
                    quietSince = now;
                }
                if (now >= drainDeadline) break;
                Thread.Sleep(1);
            }

            long measurementEnd = Stopwatch.GetTimestamp();
            long elapsedTicks = Math.Max(1, measurementEnd - start);
            double elapsedSeconds = elapsedTicks / (double)Stopwatch.Frequency;
            process.Refresh();
            TimeSpan cpuAfter = process.TotalProcessorTime;
            long allocatedAfter = GC.GetTotalAllocatedBytes(precise: false);
            long threadAllocatedAfter = GC.GetAllocatedBytesForCurrentThread();
            bool pauseAfterAvailable = TryGetTotalPauseDuration(out TimeSpan pauseAfter);
            long trailingSkippedSchedulerIntervals = Math.Max(0,
                expectedSchedulerIntervals - nextTickIndex);
            skippedSchedulerIntervals += trailingSkippedSchedulerIntervals;
            long expectedPackets = ExpectedPackets(expectedSchedulerIntervals, players);
            long packetsReceived = transport.Metrics.PacketsReceived;
            long packetsSentMinusReceived = Math.Max(0, packetsSent - packetsReceived);
            long packetsReceivedMinusDrained = Math.Max(0, packetsReceived - packetsDrained);
            bool zeroReceiverDelivery = packetsSent > 0 && packetsReceived == 0;
            string loadState = sendErrors > 0 ? "measurement-errors"
                : zeroReceiverDelivery ? "zero-receiver-delivery"
                : skippedSchedulerIntervals > 0 || packetsAttempted < expectedPackets
                    ? "insufficient-load" : "valid";
            double schedulerSeconds = Math.Max(1, schedulerEnd - start) / (double)Stopwatch.Frequency;
            long managedAllocated = Math.Max(0, allocatedAfter - allocatedBefore);
            long currentThreadAllocated = Math.Max(0, threadAllocatedAfter - threadAllocatedBefore);
            int gen0 = Math.Max(0, GC.CollectionCount(0) - gen0Before);
            int gen1 = Math.Max(0, GC.CollectionCount(1) - gen1Before);
            int gen2 = Math.Max(0, GC.CollectionCount(2) - gen2Before);
            bool pauseMeasured = gcPauseAvailable && pauseAfterAvailable && pauseAfter >= pauseBefore;
            TimeSpan pauseDelta = pauseMeasured ? pauseAfter - pauseBefore : TimeSpan.Zero;
            return new UdpReceiveCase
            {
                Players = players,
                LocalPort = transport.LocalPort,
                Status = loadState == "valid" ? "ok" : loadState,
                LoadState = loadState,
                FailureReason = loadState switch
                {
                    "measurement-errors" => $"{sendErrors} localhost sends failed.",
                    "zero-receiver-delivery" => $"Sent {packetsSent} packets but the receiver delivered zero packets.",
                    "insufficient-load" => $"Skipped {skippedSchedulerIntervals} absolute scheduler intervals or failed to offer the expected packet count.",
                    _ => null
                },
                DurationSeconds = elapsedSeconds,
                SchedulerDurationSeconds = schedulerSeconds,
                DrainDurationMilliseconds = (measurementEnd - drainStart) * 1000.0 / Stopwatch.Frequency,
                ScheduledTicks = nextTickIndex,
                ExpectedSchedulerIntervals = expectedSchedulerIntervals,
                SchedulerIntervalsEmitted = schedulerIntervalsEmitted,
                TrailingSkippedSchedulerIntervals = trailingSkippedSchedulerIntervals,
                SkippedSchedulerIntervals = skippedSchedulerIntervals,
                ExpectedPackets = expectedPackets,
                ExpectedPacketsPerSecond = expectedPackets / schedulerSeconds,
                PacketsAttempted = packetsAttempted,
                PacketsAttemptedPerSecond = packetsAttempted / schedulerSeconds,
                PacketsSent = packetsSent,
                PacketsReceived = packetsReceived,
                PacketsDrained = packetsDrained,
                PacketsSentMinusReceived = packetsSentMinusReceived,
                PacketsReceivedMinusDrained = packetsReceivedMinusDrained,
                ZeroReceiverDelivery = zeroReceiverDelivery,
                BytesSent = bytesSent,
                BytesReceived = transport.Metrics.BytesReceived,
                BytesDrained = bytesDrained,
                PacketsSentPerSecond = packetsSent / elapsedSeconds,
                PacketsReceivedPerSecond = transport.Metrics.PacketsReceived / elapsedSeconds,
                BytesSentPerSecond = bytesSent / elapsedSeconds,
                BytesReceivedPerSecond = transport.Metrics.BytesReceived / elapsedSeconds,
                ManagedAllocatedBytes = managedAllocated,
                ManagedAllocatedBytesPerSecond = managedAllocated / elapsedSeconds,
                CurrentThreadAllocatedBytes = currentThreadAllocated,
                CurrentThreadAllocatedBytesPerSecond = currentThreadAllocated / elapsedSeconds,
                Gen0Collections = gen0,
                Gen1Collections = gen1,
                Gen2Collections = gen2,
                Gen0CollectionsPerSecond = gen0 / elapsedSeconds,
                GcPauseDurationAvailable = pauseMeasured,
                GcPauseMeasurement = pauseMeasured ? "cumulative-delta-over-wall" : "unavailable",
                GcPauseMilliseconds = pauseDelta.TotalMilliseconds,
                GcPausePercentOfWall = pauseDelta.TotalMilliseconds / (elapsedSeconds * 1000) * 100,
                ProcessCpuMilliseconds = Math.Max(0, (cpuAfter - cpuBefore).TotalMilliseconds),
                ProcessCpuPercent = Math.Max(0, (cpuAfter - cpuBefore).TotalSeconds / elapsedSeconds / Environment.ProcessorCount * 100),
                QueueHighWater = transport.Metrics.QueueHighWater,
                QueueDrops = Math.Max(0, transport.PacketsDropped - dropsBefore),
                TransportRejected = transport.Metrics.PacketsRejected,
                SendErrors = sendErrors,
                InputPacketsSent = sentByClass[0],
                AckPacketsSent = sentByClass[1],
                EventPacketsSent = sentByClass[2],
                InputBytesSent = bytesByClass[0],
                AckBytesSent = bytesByClass[1],
                EventBytesSent = bytesByClass[2],
                InputDatagramBytes = input[0].Length,
                AckDatagramBytes = ack[0].Length,
                EventDatagramBytes = events[0].Length,
                DatagramsAuthenticated = true,
                ProcessLocalMeasurement = true,
                RenderedWanProof = false
            };
        }
        catch (Exception error)
        {
            return Failure(players, transport?.LocalPort ?? 0, "measurement-exception", error.ToString());
        }
        finally
        {
            foreach (Socket sender in senders)
            {
                try { sender.Dispose(); }
                catch { /* Reported measurements are retained; teardown is best effort. */ }
            }
            try { transport?.Dispose(); }
            catch { /* The run already has a bounded measurement result. */ }
            process?.Dispose();
        }
    }

    private static void Send(Socket sender, IPEndPoint target, byte[] datagram,
        ref long packetsAttempted, ref long packetsSent, ref long bytesSent,
        long[] sentByClass, long[] bytesByClass,
        int packetClass, ref int sendErrors)
    {
        packetsAttempted++;
        try
        {
            int bytes = sender.SendTo(datagram, target);
            packetsSent++;
            bytesSent += bytes;
            sentByClass[packetClass]++;
            bytesByClass[packetClass] += bytes;
        }
        catch (SocketException)
        {
            sendErrors++;
        }
    }

    private static byte[] BuildKey(ulong connectionId)
    {
        byte[] key = new byte[NetAuthentication.KeySize];
        for (int i = 0; i < key.Length; i++) key[i] = unchecked((byte)(connectionId + (ulong)i));
        return key;
    }

    private static byte[] BuildInputDatagram(ulong connectionId, byte[] key)
    {
        byte[] payload = new byte[InputBundle.MaxSize];
        Span<InputCommand> commands = stackalloc InputCommand[1];
        commands[0] = new InputCommand(1, 1, 1, InputButtons.None,
            InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon);
        int payloadLength = InputBundle.Write(payload, 1, commands);
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(payloadLength)];
        NetHeader header = new(NetMessageType.Input, NetHeaderFlags.None,
            connectionId, 1, 0, 0);
        NetAuthentication.Sign(key, NetAuthDirection.ClientToServer, header,
            payload.AsSpan(0, payloadLength), datagram);
        return datagram;
    }

    private static byte[] BuildAckDatagram(ulong connectionId, byte[] key)
    {
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(0)];
        NetHeader header = new(NetMessageType.Ack, NetHeaderFlags.HasAck,
            connectionId, 2, 1, 0);
        NetAuthentication.Sign(key, NetAuthDirection.ClientToServer, header,
            ReadOnlySpan<byte>.Empty, datagram);
        return datagram;
    }

    private static byte[] BuildEventDatagram(ulong connectionId, byte[] key)
    {
        byte[] payload = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 1);
        byte[] body = new byte[ReliableEventPacket.HeaderSize + payload.Length];
        ReliableEventPacket.Write(body, 1, ReliableEventType.ClientReady, payload);
        byte[] datagram = new byte[NetAuthentication.AuthenticatedSize(body.Length)];
        NetHeader header = new(NetMessageType.Event, NetHeaderFlags.HasAck,
            connectionId, 3, 1, 0);
        NetAuthentication.Sign(key, NetAuthDirection.ClientToServer, header,
            body, datagram);
        return datagram;
    }

    private static long ExpectedPackets(long scheduledTicks, int players)
        => checked((scheduledTicks + scheduledTicks / 6 + scheduledTicks / 30) * players);

    private static long ExpectedSchedulerIntervals(int milliseconds)
        => Math.Max(1, DurationTicks(milliseconds) * PacketsPerSecondPerPlayer / Stopwatch.Frequency);

    private static long DurationTicks(int milliseconds)
        => Math.Max(1, (long)(milliseconds * (double)Stopwatch.Frequency / 1000));

    private static long MillisecondsToTicks(int milliseconds)
        => Math.Max(1, (long)(milliseconds * (double)Stopwatch.Frequency / 1000));

    private static UdpReceiveCase Failure(int players, int localPort, string reason, string detail)
        => new()
        {
            Players = players,
            LocalPort = localPort,
            Status = "setup-failure",
            LoadState = "setup-failure",
            FailureReason = $"{reason}: {detail}",
            ProcessLocalMeasurement = true,
            RenderedWanProof = false
        };

    private static INetTransport CreateWorkerTransport()
    {
        // Server.Worker source-links the production UdpTransport. nettest also
        // references the client assembly, which source-links the same type;
        // reflection selects the Worker copy without changing project aliases
        // or compiling a second harness transport.
        Assembly worker = Assembly.Load("ProjectPrime.Server.Worker");
        Type type = worker.GetType("MphRead.Mods.Network.UdpTransport", throwOnError: true)!;
        return (INetTransport)Activator.CreateInstance(type, [0, IPAddress.Loopback])!;
    }

    private static bool TryWithin(long start, TimeSpan limit)
        => Stopwatch.GetTimestamp() - start <= limit.TotalSeconds * Stopwatch.Frequency;

    private static bool TryGetTotalPauseDuration(out TimeSpan duration)
    {
        try
        {
            duration = GC.GetTotalPauseDuration();
            return true;
        }
        catch
        {
            duration = TimeSpan.Zero;
            return false;
        }
    }

    private static bool TryParse(string[] args, out Options? options, out string? error)
    {
        options = null;
        error = null;
        if (args.Length > 1 && args[1] is "--help" or "-h")
        {
            error = "Usage: --udp-receive-baseline [OUTPUT_JSON] [--mode smoke|measurement] [--duration SEC] [--duration-ms N] [--loads 1,8,16,32]";
            return false;
        }
        string output = "udp-receive-baseline.json";
        string mode = "smoke";
        int? durationSeconds = null;
        int? durationMilliseconds = null;
        int[] loads = DefaultLoads;
        int index = 1;
        if (index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal))
            output = args[index++];
        while (index < args.Length)
        {
            string option = args[index++];
            if (option is "--mode")
            {
                if (index == args.Length || args[index] is not ("smoke" or "measurement"))
                    return Invalid("--mode must be smoke or measurement.", out options, out error);
                mode = args[index++];
            }
            else if (option is "--duration" or "--duration-seconds")
            {
                if (index == args.Length || !Int32.TryParse(args[index++], out int seconds)
                    || seconds is < 1 or > MaxDurationSeconds)
                    return Invalid("--duration must be between 1 and 3600 seconds.", out options, out error);
                durationSeconds = seconds;
            }
            else if (option == "--duration-ms")
            {
                if (index == args.Length || !Int32.TryParse(args[index++], out int milliseconds)
                    || milliseconds is < 50 or > MaxDurationSeconds * 1000)
                    return Invalid("--duration-ms must be between 50 and 3600000.", out options, out error);
                durationMilliseconds = milliseconds;
            }
            else if (option is "--loads")
            {
                if (index == args.Length || !TryParseLoads(args[index++], out loads, out error))
                    return Invalid(error ?? "--loads is invalid.", out options, out error);
            }
            else if (option is "--output")
            {
                if (index == args.Length || String.IsNullOrWhiteSpace(args[index]))
                    return Invalid("--output requires a path.", out options, out error);
                output = args[index++];
            }
            else
            {
                return Invalid($"Unknown UDP receive baseline option '{option}'.", out options, out error);
            }
        }
        int effectiveMilliseconds = durationMilliseconds
            ?? (durationSeconds.HasValue ? checked(durationSeconds.Value * 1000)
                : (mode == "measurement" ? MeasurementSeconds : SmokeSeconds) * 1000);
        options = new Options(output, mode, effectiveMilliseconds, loads);
        return true;
    }

    private static bool TryParseLoads(string value, out int[] loads, out string? error)
    {
        loads = Array.Empty<int>();
        error = null;
        string[] parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length is < 1 or > 32)
        {
            error = "--loads must contain between one and thirty-two positive player counts.";
            return false;
        }
        var parsed = new List<int>(parts.Length);
        foreach (string part in parts)
        {
            if (!Int32.TryParse(part, out int load) || load is < 1 or > 256 || parsed.Contains(load))
            {
                error = "--loads contains a duplicate or out-of-range player count (1..256).";
                return false;
            }
            parsed.Add(load);
        }
        loads = parsed.ToArray();
        return true;
    }

    private static bool Invalid(string message, out Options? options, out string? error)
    {
        options = null;
        error = message;
        return false;
    }

    private sealed record Options(string OutputPath, string Mode, int DurationMilliseconds, int[] Loads);

    private sealed class UdpReceiveReport
    {
        public int SchemaVersion { get; init; }
        public string Kind { get; init; } = String.Empty;
        public Guid RunId { get; init; }
        public DateTimeOffset StartedUtc { get; init; }
        public string Mode { get; init; } = String.Empty;
        public double DurationSeconds { get; init; }
        public int PacketsPerSecondPerPlayer { get; init; }
        public int[] Loads { get; init; } = Array.Empty<int>();
        public bool ProcessLocalEvidence { get; init; }
        public bool RenderedWanProof { get; init; }
        public string EvidenceClass { get; init; } = String.Empty;
        public string ManagedAllocationCaveat { get; init; } = String.Empty;
        public string QueueHighWaterSource { get; init; } = String.Empty;
        public string SchedulerPolicy { get; init; } = String.Empty;
        public string RepresentativeTraffic { get; init; } = String.Empty;
        public RuntimeFacts Runtime { get; init; } = new();
        public UdpReceiveCase[] Cases { get; init; } = Array.Empty<UdpReceiveCase>();
    }

    private sealed class UdpReceiveCase
    {
        public int Players { get; init; }
        public int LocalPort { get; init; }
        public string Status { get; init; } = String.Empty;
        public string LoadState { get; init; } = String.Empty;
        public string? FailureReason { get; init; }
        public double DurationSeconds { get; init; }
        public double SchedulerDurationSeconds { get; init; }
        public double DrainDurationMilliseconds { get; init; }
        public long ScheduledTicks { get; init; }
        public long ExpectedSchedulerIntervals { get; init; }
        public long SchedulerIntervalsEmitted { get; init; }
        public long TrailingSkippedSchedulerIntervals { get; init; }
        public long SkippedSchedulerIntervals { get; init; }
        public long ExpectedPackets { get; init; }
        public double ExpectedPacketsPerSecond { get; init; }
        public long PacketsAttempted { get; init; }
        public double PacketsAttemptedPerSecond { get; init; }
        public long PacketsSent { get; init; }
        public long PacketsReceived { get; init; }
        public long PacketsDrained { get; init; }
        public long PacketsSentMinusReceived { get; init; }
        public long PacketsReceivedMinusDrained { get; init; }
        public bool ZeroReceiverDelivery { get; init; }
        public long BytesSent { get; init; }
        public long BytesReceived { get; init; }
        public long BytesDrained { get; init; }
        public double PacketsSentPerSecond { get; init; }
        public double PacketsReceivedPerSecond { get; init; }
        public double BytesSentPerSecond { get; init; }
        public double BytesReceivedPerSecond { get; init; }
        public long ManagedAllocatedBytes { get; init; }
        public double ManagedAllocatedBytesPerSecond { get; init; }
        public long CurrentThreadAllocatedBytes { get; init; }
        public double CurrentThreadAllocatedBytesPerSecond { get; init; }
        public int Gen0Collections { get; init; }
        public int Gen1Collections { get; init; }
        public int Gen2Collections { get; init; }
        public double Gen0CollectionsPerSecond { get; init; }
        public bool GcPauseDurationAvailable { get; init; }
        public string GcPauseMeasurement { get; init; } = String.Empty;
        public double GcPauseMilliseconds { get; init; }
        public double GcPausePercentOfWall { get; init; }
        public double ProcessCpuMilliseconds { get; init; }
        public double ProcessCpuPercent { get; init; }
        public long QueueHighWater { get; init; }
        public long QueueDrops { get; init; }
        public long TransportRejected { get; init; }
        public int SendErrors { get; init; }
        public long InputPacketsSent { get; init; }
        public long AckPacketsSent { get; init; }
        public long EventPacketsSent { get; init; }
        public long InputBytesSent { get; init; }
        public long AckBytesSent { get; init; }
        public long EventBytesSent { get; init; }
        public int InputDatagramBytes { get; init; }
        public int AckDatagramBytes { get; init; }
        public int EventDatagramBytes { get; init; }
        public bool DatagramsAuthenticated { get; init; }
        public bool ProcessLocalMeasurement { get; init; }
        public bool RenderedWanProof { get; init; }
    }

    private sealed class RuntimeFacts
    {
        public string OperatingSystem { get; init; } = RuntimeInformation.OSDescription;
        public string OSArchitecture { get; init; } = RuntimeInformation.OSArchitecture.ToString();
        public string ProcessArchitecture { get; init; } = RuntimeInformation.ProcessArchitecture.ToString();
        public string Framework { get; init; } = RuntimeInformation.FrameworkDescription;
        public string DotNetVersion { get; init; } = Environment.Version.ToString();
        public int ProcessorCount { get; init; } = Environment.ProcessorCount;
        public long StopwatchFrequency { get; init; } = Stopwatch.Frequency;
    }
}
