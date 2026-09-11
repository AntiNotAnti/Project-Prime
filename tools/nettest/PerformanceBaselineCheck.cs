using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Formats;
using MphRead.Runtime.HistoricalCollision;
using OpenTK.Mathematics;

namespace MphRead.NetTest;

/// <summary>
/// Small, content-free performance smoke baseline for the bounded networking
/// and simulation primitives. This is an observation tool only: it does not
/// alter simulation policy, transport authority, or protocol bytes.
/// </summary>
internal static class PerformanceBaselineCheck
{
    private const int WarmupIterations = 32;
    private const int SampleCount = 4096;
    private const int OperationsPerSample = 128;
    private const int SelfTestSampleCount = 32;
    private const int SelfTestOperationsPerSample = 32;
    private static readonly double NanosecondsPerTick = 1_000_000_000.0 / Stopwatch.Frequency;
    private static readonly SnapshotPlayer[] EmptyPlayers = Array.Empty<SnapshotPlayer>();
    private static readonly IPEndPoint Endpoint = new(IPAddress.Loopback, 50000);
    private static int _sink;

    public static int Run(string[] args)
    {
        if (args.Length > 4)
        {
            Console.Error.WriteLine("Usage: --performance-baseline OUTPUT_JSON [AMHE1_DIRECTORY | --data AMHE1_DIRECTORY]");
            return 2;
        }

        string output = args.Length > 1 ? args[1] : "performance-baseline.json";
        if (String.IsNullOrWhiteSpace(output) || output[0] == '-')
        {
            Console.Error.WriteLine("A writable OUTPUT_JSON path is required.");
            return 2;
        }
        string? dataDirectory = null;
        if (args.Length == 3)
        {
            if (args[2].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Usage: --performance-baseline OUTPUT_JSON [AMHE1_DIRECTORY | --data AMHE1_DIRECTORY]");
                return 2;
            }
            dataDirectory = args[2];
        }
        else if (args.Length == 4)
        {
            if (args[2] is not ("--data" or "--content-dir") || String.IsNullOrWhiteSpace(args[3])
                || args[3].StartsWith("-", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Usage: --performance-baseline OUTPUT_JSON [AMHE1_DIRECTORY | --data AMHE1_DIRECTORY]");
                return 2;
            }
            dataDirectory = args[3];
        }

        ContentPerformanceFixture? content = null;
        Exception? contentSetupError = null;
        if (dataDirectory != null)
        {
            try { content = new ContentPerformanceFixture(dataDirectory); }
            catch (Exception error) { contentSetupError = error; }
        }

        bool contentBacked = dataDirectory != null;
        try
        {
        long suiteStart = Stopwatch.GetTimestamp();
        var scenarios = new List<PerformanceScenario>(15)
        {
            RunSafe("SnapshotPacket.Write", MeasureSnapshotWrite),
            RunSafe("LagCompensationPolicy.ResolveTick", MeasureLagCompensationPolicy),
            RunSafe("LagCompensationHistory.TryGet", MeasureLagCompensationHistory),
            RunSafe("ServerInputStream.ReceiveTake", MeasureServerInputStream),
            RunSafe("ReliableChannel.TryGetDue.MarkSent", MeasureReliableDue),
            RunSafe("ReliableChannel.TryEnqueue", () => MeasureReliableEnqueue()),
            RunSafe("ReliableEventPacket.Encode", () => MeasureReliableEventEncode()),
            RunSafe("NetClient.EventReceiveQueue", () => MeasureNetClientEventReceiveQueue()),
            RunSafe("NetClient.CombatReceiveQueue", () => MeasureNetClientCombatReceiveQueue()),
            RunSafe("MatchDatagramTransport.EnqueueFlush", MeasureMatchTransport),
            RunSafe("ServerCombat.CaptureShot", MeasureServerCombatShot),
            RunSafe("DynamicCollisionHistory.Record", MeasureDynamicCollisionHistoryRecord),
            RunSafe("SnapshotState.CaptureServerState", () => MeasureSnapshotStateCapture(content, contentSetupError)),
            RunSafe("HistoricalCollisionQueryEngine.TryQuery", () => MeasureHistoricalCollisionQuery(content, contentSetupError)),
            RunSafe("WorldStateCapture.Capture", () => MeasureWorldStateCapture(content, contentSetupError))
        };
        double suiteDurationMilliseconds = ElapsedMilliseconds(Stopwatch.GetTimestamp() - suiteStart);

        string fullPath = Path.GetFullPath(output);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var report = new PerformanceBaselineReport
        {
            SchemaVersion = 1,
            Kind = contentBacked ? "ProjectPrime.ContentBackedPerformanceBaseline" : "ProjectPrime.ContentFreePerformanceBaseline",
            ContentMode = contentBacked ? "AMHE1" : "content-free",
            GeneratedUtc = DateTimeOffset.UtcNow,
            GitCommit = ReadGitCommit(),
            GitDirty = ReadGitDirty(),
            Runtime = new PerformanceRuntime
            {
                Framework = RuntimeInformation.FrameworkDescription,
                RuntimeVersion = Environment.Version.ToString(),
                OperatingSystem = RuntimeInformation.OSDescription,
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                StopwatchFrequency = Stopwatch.Frequency,
                Is64BitProcess = Environment.Is64BitProcess
            },
            WarmupIterations = WarmupIterations,
            SampleCount = SampleCount,
            OperationsPerSample = OperationsPerSample,
            PercentileDefinition = "P50/P95/P99/P99.9/max of per-sample batch-average latency; each sample batches OperationsPerSample calls.",
            TotalDurationMilliseconds = suiteDurationMilliseconds,
            Scenarios = scenarios.ToArray()
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(fullPath, JsonSerializer.Serialize(report, options));
        Console.WriteLine($"Wrote {(contentBacked ? "AMHE1 content-backed" : "content-free")} performance baseline: {fullPath}");
        foreach (PerformanceScenario scenario in scenarios)
        {
            Console.WriteLine($"{scenario.Name}: {scenario.Status} "
                + $"{scenario.NanosecondsPerOperation:0.0} ns/op, "
                + $"{scenario.AllocatedBytesPerOperation:0.0} B/op"
                + (scenario.AllocationBudgetBytesPerOperation is { } budget
                    ? $", budget={budget:0.0} B/op ({(scenario.AllocationBudgetPassed ? "pass" : "FAIL")})"
                    : String.Empty));
        }
        return scenarios.Exists(static scenario => scenario.Status == "gap") ? 1 : 0;
        }
        finally { content?.Dispose(); }
    }

    /// <summary>
    /// Short operator/CI entry point for the four N12-H allocation paths. The
    /// full performance report remains available through --performance-baseline;
    /// this check deliberately does not change any channel or codec behavior.
    /// </summary>
    public static int AllocationSelfTest()
    {
        var scenarios = new[]
        {
            RunSafe("ReliableChannel.TryEnqueue", () => MeasureReliableEnqueue(SelfTestSampleCount, SelfTestOperationsPerSample)),
            RunSafe("ReliableEventPacket.Encode", () => MeasureReliableEventEncode(SelfTestSampleCount, SelfTestOperationsPerSample)),
            RunSafe("NetClient.EventReceiveQueue", () => MeasureNetClientEventReceiveQueue(SelfTestSampleCount, SelfTestOperationsPerSample)),
            RunSafe("NetClient.CombatReceiveQueue", () => MeasureNetClientCombatReceiveQueue(SelfTestSampleCount, SelfTestOperationsPerSample))
        };
        bool passed = scenarios.All(static value => value.Status == "ok"
            && value.Iterations > 0
            && Double.IsFinite(value.NanosecondsPerOperation)
            && value.AllocatedBytes >= 0);
        Console.WriteLine($"Reliable allocation self-test: {(passed ? "PASS" : "FAIL")} "
            + String.Join(", ", scenarios.Select(static value =>
                $"{value.Name}={value.NanosecondsPerOperation:0.0}ns/{value.AllocatedBytesPerOperation:0.0}B")));
        return passed ? 0 : 1;
    }

    private static PerformanceScenario RunSafe(string name, Func<PerformanceScenario> scenario)
    {
        long start = Stopwatch.GetTimestamp();
        PerformanceScenario result;
        try { result = scenario(); }
        catch (Exception ex) { result = Gap(name, ex); }
        result.DurationMilliseconds = ElapsedMilliseconds(Stopwatch.GetTimestamp() - start);
        return result;
    }

    private static PerformanceScenario MeasureSnapshotWrite()
    {
        var packet = new SnapshotPacket(100, 5, 42, 4, true, 1, 2);
        byte[] destination = new byte[SnapshotPacket.MaxSize];
        return Measure("SnapshotPacket.Write", "encode empty authoritative snapshot header", () =>
        {
            _sink ^= packet.Write(destination, EmptyPlayers);
        });
    }

    private static PerformanceScenario MeasureLagCompensationPolicy()
    {
        LagCompensationTime resolved = default;
        return Measure("LagCompensationPolicy.ResolveTick", "resolve bounded rewind from tick/view/RTT", () =>
        {
            resolved = LagCompensationPolicy.ResolveTick(1000, 987, 120);
            _sink ^= (int)resolved.RewindTicks;
        });
    }

    private static PerformanceScenario MeasureLagCompensationHistory()
    {
        var history = new LagCompensationHistory();
        var state = new LagCompensationState
        {
            Slot = 0,
            ConnectionId = 1,
            LifeId = 1,
            Alive = true,
            SphereRadius = 1
        };
        history.Record(100, state);
        LagCompensationState found = default;
        return Measure("LagCompensationHistory.TryGet", "identity-checked historical state lookup", () =>
        {
            if (history.TryGet(0, 100, 1, 1, out found)) _sink ^= found.Slot;
        });
    }

    private static PerformanceScenario MeasureServerInputStream()
    {
        var stream = new ServerInputStream();
        var commands = new InputCommand[1];
        uint sequence = 1;
        uint tick = 1;
        return Measure("ServerInputStream.ReceiveTake", "bounded input enqueue and one-tick dequeue", () =>
        {
            commands[0] = new InputCommand(sequence++, tick, tick, InputButtons.None,
                InputButtons.None, -OpenTK.Mathematics.Vector3.UnitZ, InputCommand.NoWeapon);
            stream.Receive(commands, tick);
            _sink ^= (int)stream.Take(tick++).Sequence;
        });
    }

    private static PerformanceScenario MeasureReliableDue()
    {
        var channel = new ReliableChannel();
        if (!channel.TryEnqueue(ReliableEventType.Combat, ReadOnlySpan<byte>.Empty, out uint eventId))
            throw new InvalidOperationException("ReliableChannel did not admit its baseline event.");
        double now = 1;
        uint packetSequence = 1;
        return Measure("ReliableChannel.TryGetDue.MarkSent", "due lookup and send-attempt bookkeeping", () =>
        {
            if (channel.TryGetDue(now, out uint id, out _, out _))
            {
                channel.MarkSent(id, packetSequence++, now);
                _sink ^= (int)(id ^ eventId);
            }
            now += 0.2;
        });
    }

    private static PerformanceScenario MeasureReliableEnqueue(
        int sampleCount = SampleCount, int operationsPerSample = OperationsPerSample)
    {
        var channel = new ReliableChannel();
        byte[] payload = [1, 2, 3, 4];
        uint packetSequence = 1;
        double now = 1;
        return Measure("ReliableChannel.TryEnqueue", "bounded reliable admission and payload ownership copy", () =>
        {
            if (!channel.TryEnqueue(ReliableEventType.Combat, payload, out uint eventId))
                throw new InvalidOperationException("ReliableChannel did not admit the allocation baseline event.");
            if (!channel.TryGetDue(now, out uint dueId, out _, out _))
                throw new InvalidOperationException("ReliableChannel did not expose the allocation baseline event.");
            channel.MarkSent(dueId, packetSequence, now);
            channel.Acknowledge(packetSequence, 0);
            _sink ^= (int)(eventId ^ dueId);
            packetSequence++;
            now += 0.001;
        }, sampleCount: sampleCount, operationsPerSample: operationsPerSample);
    }

    private static PerformanceScenario MeasureReliableEventEncode(
        int sampleCount = SampleCount, int operationsPerSample = OperationsPerSample)
    {
        byte[] payload = [1, 2, 3, 4, 5, 6, 7, 8];
        byte[] destination = new byte[ReliableEventPacket.HeaderSize + payload.Length];
        uint eventId = 1;
        return Measure("ReliableEventPacket.Encode", "reliable application event wire encoding", () =>
        {
            _sink ^= ReliableEventPacket.Write(destination, eventId++, ReliableEventType.TimingProfileApplied, payload);
        }, allocationBudgetBytesPerOperation: 0,
            sampleCount: sampleCount, operationsPerSample: operationsPerSample);
    }

    private static PerformanceScenario MeasureNetClientEventReceiveQueue(
        int sampleCount = SampleCount, int operationsPerSample = OperationsPerSample)
    {
        using var fixture = new NetClientQueueFixture();
        uint eventId = 2;
        uint sequence = 1;
        return Measure("NetClient.EventReceiveQueue",
            "production NetClient application queue: owned payload copy, deferred drain, and duplicate suppression",
            () =>
            {
                fixture.QueueChat(eventId, sequence);
                if (!fixture.TryDequeueOwnedChat(out NetApplicationEvent message)
                    || message.Type != ReliableEventType.Chat
                    || message.Payload.Length != SessionChatPacket.Size
                    || BinaryPrimitives.ReadUInt64LittleEndian(message.Payload.Span) != NetClientQueueFixture.ChatConnectionId)
                {
                    throw new InvalidOperationException($"NetClient did not retain the valid owned chat payload (rejected={fixture.Client.Rejected}, failure={fixture.Client.Failure ?? "none"}, state={fixture.Client.State}, queued={fixture.Client.TryDequeueEvent(out _)}).");
                }
                _sink ^= message.Payload.Span[0];
                eventId++;
                sequence += 2;
            }, allocationBudgetBytesPerOperation: null,
                sampleCount: sampleCount, operationsPerSample: operationsPerSample);
    }

    private static PerformanceScenario MeasureNetClientCombatReceiveQueue(
        int sampleCount = SampleCount, int operationsPerSample = OperationsPerSample)
    {
        using var fixture = new NetClientQueueFixture();
        uint eventId = 2;
        uint sequence = 1;
        return Measure("NetClient.CombatReceiveQueue",
            "production NetClient application queue: combat validation, owned payload copy, deferred drain, and duplicate suppression",
            () =>
            {
                fixture.QueueCombat(eventId, sequence);
                if (!fixture.TryDequeueOwnedCombat(out NetApplicationEvent message))
                    throw new InvalidOperationException($"NetClient did not retain the valid owned combat payload (rejected={fixture.Client.Rejected}, failure={fixture.Client.Failure ?? "none"}, state={fixture.Client.State}, queued={fixture.Client.TryDequeueEvent(out _)}).");
                _sink ^= message.Payload.Span[0];
                eventId++;
                sequence += 2;
            }, allocationBudgetBytesPerOperation: null,
                sampleCount: sampleCount, operationsPerSample: operationsPerSample);
    }

    private static PerformanceScenario MeasureServerCombatShot()
    {
        var combat = new ServerCombat();
        var actor = new CombatActor(0, 1, 1);
        var mechanics = new BeamMechanics(BeamType.Imperialist, BeamType.Imperialist,
            Continuous: false, InstantArea: false, Homing: 0, Speed: 100, Lifespan: 2);
        combat.BeginTick(1000);
        combat.SetCommand(0, new InputCommand(10, 990, 990, InputButtons.None,
            InputButtons.None, -Vector3.UnitZ, InputCommand.NoWeapon));
        CombatShot shot = default;
        return Measure("ServerCombat.CaptureShot", "content-free immutable actor/command shot attribution", () =>
        {
            shot = combat.CaptureShot(actor, mechanics);
            _sink ^= (int)(shot.CommandSequence ^ shot.RewindTicks);
        }, allocationBudgetBytesPerOperation: 0);
    }

    private static PerformanceScenario MeasureDynamicCollisionHistoryRecord()
    {
        using Scene scene = Scene.CreateHeadless();
        var registry = new HistoricalCollisionRegistry(scene, hardColliderCap: 1);
        var entity = new BaselineEntity(scene, 1);
        scene.InsertEntity(entity);
        if (!registry.TryRegister(entity, 0, out _))
            throw new InvalidOperationException("Dynamic collision baseline entity could not be registered.");
        registry.Seal();
        var history = new DynamicCollisionHistory(registry);
        history.Record(0);
        uint tick = 1;
        return Measure("DynamicCollisionHistory.Record", "content-free fixed-ring dynamic collision capture", () =>
        {
            history.Record(tick++);
            _sink ^= history.ColliderCount;
        }, allocationBudgetBytesPerOperation: 0);
    }

    private static PerformanceScenario MeasureSnapshotStateCapture(ContentPerformanceFixture? content, Exception? setupError)
    {
        if (content == null)
            return ContentGap("SnapshotState.CaptureServerState", setupError?.ToString()
                ?? "PlayerEntity.CaptureServerState requires a content-backed ServerSimulation/Scene.LoadServerRoom (AMHE1); the content-free harness does not synthesize a player entity.");
        PlayerEntity player = content.Player;
        return Measure("SnapshotState.CaptureServerState", "content-backed PlayerEntity authoritative snapshot capture", () =>
        {
            SnapshotPlayer snapshot = player.CaptureServerState();
            _sink ^= snapshot.Slot ^ snapshot.Health;
        });
    }

    private static PerformanceScenario MeasureHistoricalCollisionQuery(ContentPerformanceFixture? content, Exception? setupError)
    {
        if (content == null)
            return ContentGap("HistoricalCollisionQueryEngine.TryQuery", setupError?.ToString()
                ?? "HistoricalCollisionQueryEngine requires a loaded authoritative room and registered collision geometry (AMHE1); an empty headless Scene would not be a representative query.");
        HistoricalCollisionQueryEngine engine = content.HistoricalCollision;
        HistoricalCollisionQuery query = content.HistoricalQuery;
        return Measure("HistoricalCollisionQueryEngine.TryQuery", "content-backed historical room/collider query", () =>
        {
            if (engine.TryQuery(query, 0, out HistoricalCollisionResult result)) _sink ^= result.Hit ? 1 : 0;
        });
    }

    private static PerformanceScenario MeasureWorldStateCapture(ContentPerformanceFixture? content, Exception? setupError)
    {
        if (content == null)
            return ContentGap("WorldStateCapture.Capture", setupError?.ToString()
                ?? "WorldStateCapture.Capture validates the loaded authoritative room and objectives on first use (AMHE1); the content-free harness does not synthesize world entities.");
        WorldStateCapture capture = content.World;
        Scene scene = content.Simulation.Scene;
        uint revision = 1;
        return Measure("WorldStateCapture.Capture", "content-backed authoritative world replication capture", () =>
        {
            capture.Capture(scene, 1, revision++, 0);
            _sink ^= capture.Count;
        });
    }

    private static PerformanceScenario ContentGap(string name, string reason)
        => Gap(name, new InvalidOperationException(reason));

    private static PerformanceScenario MeasureMatchTransport()
    {
        using var physical = new BaselineTransport();
        var router = new BaselineRouter();
        using var hub = new WorkerNetworkHub(physical, Guid.NewGuid(), router);
        using MatchDatagramTransport match = hub.RegisterMatch(1, queueCapacity: 256, drainBudget: 256);
        ulong connectionId = match.AllocateConnectionId();
        router.Route = new WorkerDatagramRoute(0, connectionId, IsJoin: false);
        byte[] inboundBytes = new byte[] { 1 };
        var inbound = new ReceivedPacket(Endpoint, inboundBytes, inboundBytes.Length);
        byte[] outbound = new byte[] { 1 };
        var drained = new ReceivedPacket[256];
        return Measure("MatchDatagramTransport.EnqueueFlush", "virtual enqueue, bounded drain, and hub flush", () =>
        {
            physical.Enqueue(inbound);
            match.SendDatagram(Endpoint, outbound);
            hub.Pump();
            int count = match.Drain(drained);
            for (int i = 0; i < count; i++) _sink ^= drained[i].Length;
        });
    }

    private static PerformanceScenario Measure(string name, string workload, Action operation,
        long? allocationBudgetBytesPerOperation = null,
        int sampleCount = SampleCount, int operationsPerSample = OperationsPerSample)
    {
        for (int warmup = 0; warmup < WarmupIterations; warmup++)
            for (int iteration = 0; iteration < operationsPerSample; iteration++) operation();

        var samples = new double[sampleCount];

        // Warm-up includes JIT and one-time data structures. Establish the
        // allocation/collection baseline only after it has completed.
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        long totalTicks = 0;
        int measuredSamples = 0;
        try
        {
            for (int sample = 0; sample < sampleCount; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int iteration = 0; iteration < operationsPerSample; iteration++) operation();
                long elapsed = Stopwatch.GetTimestamp() - start;
                totalTicks += elapsed;
                samples[measuredSamples++] = elapsed * NanosecondsPerTick / operationsPerSample;
            }
        }
        catch (Exception ex)
        {
            return Gap(name, ex, measuredSamples);
        }

        // Capture collection counters at the end of the measured loop. The
        // reporting sampler below sorts/records retained samples and must not
        // be charged to the operation's GC deltas.
        int gen0After = GC.CollectionCount(0);
        int gen1After = GC.CollectionCount(1);
        int gen2After = GC.CollectionCount(2);
        long allocated = Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
        var percentile = new BoundedPercentileSampler(sampleCount);
        for (int i = 0; i < measuredSamples; i++) percentile.Record(samples[i]);
        BoundedPercentileSnapshot summary = percentile.Snapshot();
        long operations = (long)measuredSamples * operationsPerSample;
        double allocatedPerOperation = operations == 0 ? 0 : (double)allocated / operations;
        bool allocationBudgetPassed = allocationBudgetBytesPerOperation is not { } budget
            || allocated <= checked(budget * operations);
        return new PerformanceScenario
        {
            Name = name,
            Status = allocationBudgetPassed ? "ok" : "gap",
            Workload = workload,
            SampleCount = measuredSamples,
            WarmupIterations = WarmupIterations,
            OperationsPerSample = operationsPerSample,
            Iterations = operations,
            AllocatedBytes = allocated,
            AllocatedBytesPerOperation = allocatedPerOperation,
            AllocationBudgetBytesPerOperation = allocationBudgetBytesPerOperation,
            AllocationBudgetPassed = allocationBudgetPassed,
            Exception = allocationBudgetPassed ? null
                : $"Measured {allocatedPerOperation:0.0} B/op, exceeding the enforced budget of {allocationBudgetBytesPerOperation:0.0} B/op.",
            NanosecondsPerOperation = operations == 0 ? 0 : totalTicks * NanosecondsPerTick / operations,
            P50Nanoseconds = summary.P50,
            P95Nanoseconds = summary.P95,
            P99Nanoseconds = summary.P99,
            P999Nanoseconds = summary.P999,
            MaxNanoseconds = summary.Max,
            Gen0Collections = Math.Max(0, gen0After - gen0Before),
            Gen1Collections = Math.Max(0, gen1After - gen1Before),
            Gen2Collections = Math.Max(0, gen2After - gen2Before)
        };
    }

    private static PerformanceScenario Gap(string name, Exception exception, int samples = 0)
        => new()
        {
            Name = name,
            Status = "gap",
            Workload = "not measured",
            SampleCount = samples,
            WarmupIterations = WarmupIterations,
            OperationsPerSample = OperationsPerSample,
            Iterations = (long)samples * OperationsPerSample,
            Exception = exception.ToString()
        };

    private static double ElapsedMilliseconds(long ticks)
        => ticks <= 0 ? 0 : ticks * (1000.0 / Stopwatch.Frequency);

    private static string ReadGitCommit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return "unavailable";
            string result = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(2000);
            return process.ExitCode == 0 && result.Length > 0 ? result : "unavailable";
        }
        catch { return "unavailable"; }
    }

    private static bool ReadGitDirty()
    {
        // Only inspect whether Git emitted one status byte. Do not materialize
        // or report filenames, since worktrees can contain sensitive paths.
        return GitHasOutput("status --porcelain --untracked-files=no")
            || GitHasOutput("ls-files --others --exclude-standard");
    }

    private static bool GitHasOutput(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = AppContext.BaseDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process == null) return true;
            bool output = process.StandardOutput.Read() >= 0;
            if (!process.WaitForExit(2000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return true;
            }
            return output || process.ExitCode != 0;
        }
        catch { return true; }
    }

    /// <summary>
    /// One real AMHE1 scene shared by the content-backed scenarios. Content
    /// loading, room setup, registry construction and player activation happen
    /// before any timed operation; disposal happens after the report is written.
    /// </summary>
    private sealed class ContentPerformanceFixture : IDisposable
    {
        private readonly IDisposable _contentContext;
        private bool _disposed;

        public ServerSimulation Simulation { get; }
        public PlayerEntity Player { get; }
        public HistoricalCollisionQueryEngine HistoricalCollision { get; }
        public HistoricalCollisionQuery HistoricalQuery { get; }
        public WorldStateCapture World { get; }

        public ContentPerformanceFixture(string dataDirectory)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("AMHE1 data directory is required.", nameof(dataDirectory));
            _contentContext = ServerContent.PreserveContext("AMHE1");
            ServerSimulation? simulation = null;
            try
            {
                ServerContent.Open(Path.GetFullPath(dataDirectory), "AMHE1");
                simulation = new ServerSimulation(new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Battle });
                Scene scene = simulation.Scene;
                scene.Match.Phase = MatchPhase.Playing;
                PlayerEntity player = scene.Players[0];
                player.ServerActivate(100, Hunter.Samus, 0);
                scene.Players.ActiveCount = 1;

                simulation.Combat.DynamicCollisionHistory.Record(0);
                HistoricalCollision = new HistoricalCollisionQueryEngine(scene,
                    simulation.Combat.HistoricalCollisionRegistry,
                    simulation.Combat.DynamicCollisionHistory, simulation.Combat);
                HistoricalQuery = new HistoricalCollisionQuery(new(1, 1, 1), new(1, 1, -1), TestFlags.Beams);
                Simulation = simulation;
                Player = player;
                World = new WorldStateCapture();
            }
            catch
            {
                simulation?.Dispose();
                _contentContext.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Simulation.Dispose(); }
            finally { _contentContext.Dispose(); }
        }
    }

    private sealed class BaselineRouter : IWorkerDatagramRouter
    {
        public WorkerDatagramRoute Route;

        public bool TryRoute(ReadOnlySpan<byte> datagram, out WorkerDatagramRoute route)
        {
            route = Route;
            return true;
        }
    }

    private sealed class BaselineEntity : EntityBase
    {
        public BaselineEntity(Scene scene, int id) : base(EntityType.Object, scene) => Id = id;
    }

    /// <summary>
    /// Bounded in-memory host transport for N12-H. It keeps the production
    /// NetClient Poll/Handle/ACK/deferred-queue path intact while making the
    /// benchmark independent of socket timing and unreachable endpoints.
    /// </summary>
    private sealed class InMemoryNetTransport : INetTransport
    {
        private const int MaxQueuedPackets = 256;
        private readonly Queue<ReceivedPacket> _incoming = new();
        private readonly IPEndPoint _sender;
        private bool _disposed;

        public InMemoryNetTransport(IPEndPoint sender) => _sender = sender;

        public int LocalPort => 0;
        public long PacketsDropped { get; private set; }
        public int QueuedPackets => _incoming.Count;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();

        public void EnqueueForPlayback(byte[] data, int length)
        {
            if (_disposed || length <= 0 || length > NetConfig.MaxPacketSize || length > data.Length)
            {
                Metrics.Reject();
                return;
            }
            if (_incoming.Count == MaxQueuedPackets)
            {
                _incoming.Dequeue();
                PacketsDropped++;
                Metrics.DropQueued();
            }
            _incoming.Enqueue(new ReceivedPacket(_sender, data, length));
        }

        public IEnumerable<ReceivedPacket> Drain()
        {
            while (_incoming.Count > 0)
                yield return _incoming.Dequeue();
        }

        public int Drain(Span<ReceivedPacket> destination)
        {
            int count = 0;
            while (count < destination.Length && _incoming.TryDequeue(out ReceivedPacket packet))
                destination[count++] = packet;
            return count;
        }

        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram)
            => SendDatagram(target, datagram, 0);

        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks)
        {
            if (_disposed || datagram.Length == 0 || datagram.Length > NetConfig.MaxPacketSize)
            {
                Metrics.Reject();
                return;
            }
            // This is the bounded sink: ACKs are consumed here, never sent to
            // an OS socket, so a valid application send cannot create a
            // SocketException or contaminate the allocation result.
            Metrics.Sent(datagram.Length);
        }

        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload,
            long extraHoldTicks = 0)
        {
            if (payload.Length >= NetConfig.MaxPacketSize)
            {
                Metrics.Reject();
                return;
            }
            Span<byte> datagram = stackalloc byte[payload.Length + 1];
            datagram[0] = (byte)type;
            payload.CopyTo(datagram[1..]);
            SendDatagram(target, datagram, extraHoldTicks);
        }

        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void Dispose()
        {
            _disposed = true;
            _incoming.Clear();
        }
    }

    /// <summary>
    /// A real client-side receive boundary used only by N12-H. It admits one
    /// valid bootstrap packet, then feeds production NetClient event packets
    /// through the transport playback boundary. Each operation queues one new
    /// event and one newer-sequence duplicate; NetClient must copy the payload
    /// before the source datagram is mutated and must deliver only one event.
    /// </summary>
    private sealed class NetClientQueueFixture : IDisposable
    {
        public const ulong ChatConnectionId = 0x0102_0304_0506_0708;
        private const ulong ClientConnectionId = 0x1112_1314_1516_1718;
        private const ulong ClientNonce = 0x2122_2324_2526_2728;
        private const uint MatchId = 1;
        private const uint AcceptedEventId = 1;
        // The bounded in-memory transport presents the same sender endpoint
        // to NetClient for admission fencing and consumes production ACKs
        // without sending them to an unbound Loopback:0 socket.
        private readonly IPEndPoint _server = new(IPAddress.Loopback, 43000);
        private readonly byte[] _chatPayload;
        private readonly byte[] _combatPayload;
        private readonly byte[] _chatPrimary;
        private readonly byte[] _chatDuplicate;
        private readonly byte[] _combatPrimary;
        private readonly byte[] _combatDuplicate;
        private bool _disposed;

        public InMemoryNetTransport Transport { get; }
        public NetClient Client { get; }

        public NetClientQueueFixture()
        {
            InMemoryNetTransport? transport = null;
            NetClient? client = null;
            try
            {
                transport = new InMemoryNetTransport(_server);
                client = new NetClient(transport, _server, "N12-QUEUE", Hunter.Samus,
                    nonce: ClientNonce, wireMatchId: MatchId);
                transport.EnqueueForPlayback(BuildAcceptedDatagram(),
                    NetHeader.Size + ReliableEventPacket.HeaderSize + JoinAcceptedPacket.Size);
                client.Poll();
                if (client.Connection == null || client.Connection.Id != ClientConnectionId)
                {
                    throw new InvalidOperationException("NetClient did not accept the valid N12 queue bootstrap.");
                }

                _chatPayload = BuildChatPayload();
                _combatPayload = BuildCombatPayload();
                _chatPrimary = new byte[DatagramSize(_chatPayload.Length)];
                _chatDuplicate = new byte[_chatPrimary.Length];
                _combatPrimary = new byte[DatagramSize(_combatPayload.Length)];
                _combatDuplicate = new byte[_combatPrimary.Length];
                PrepareEvent(_chatPrimary, _chatPayload, ReliableEventType.Chat, 2, 1);
                PrepareEvent(_chatDuplicate, _chatPayload, ReliableEventType.Chat, 2, 2);
                bool headerValid = NetHeader.TryRead(_chatPrimary, out _);
                bool packetValid = ReliableEventPacket.TryRead(_chatPrimary[NetHeader.Size..], out _,
                    out ReliableEventType chatType, out ReadOnlySpan<byte> chatBody);
                bool chatPayloadValid = packetValid && chatType == ReliableEventType.Chat
                    && chatBody.Length == _chatPayload.Length
                    && BinaryPrimitives.ReadUInt32LittleEndian(chatBody) == MatchId
                    && SessionChatPacket.TryRead(chatBody[4..], out _);
                if (!headerValid || !chatPayloadValid)
                {
                    string name = packetValid && chatBody.Length >= 4 + 9 + ChatPacket.MaxNameBytes
                        ? NetText.Read(chatBody.Slice(4 + 9, ChatPacket.MaxNameBytes)) : "?";
                    string text = packetValid && chatBody.Length >= 4 + SessionChatPacket.Size
                        ? NetText.Read(chatBody.Slice(4 + 9 + ChatPacket.MaxNameBytes, ChatPacket.MaxTextBytes)) : "?";
                    throw new InvalidOperationException($"N12 queue fixture did not build a valid production chat event (header={headerValid}, packet={packetValid}, type={chatType}, body={chatBody.Length}, expected={_chatPayload.Length}, match={(packetValid && chatBody.Length >= 4 ? BinaryPrimitives.ReadUInt32LittleEndian(chatBody) : 0)}, chat={chatPayloadValid}, name='{name}', text='{text}').");
                }
                Transport = transport;
                Client = client;
            }
            catch
            {
                client?.Dispose();
                transport?.Dispose();
                throw;
            }
        }

        public void QueueChat(uint eventId, uint sequence)
        {
            PrepareEvent(_chatPrimary, _chatPayload, ReliableEventType.Chat, eventId, sequence);
            PrepareEvent(_chatDuplicate, _chatPayload, ReliableEventType.Chat, eventId, sequence + 1);
            Transport.EnqueueForPlayback(_chatPrimary, _chatPrimary.Length);
            Transport.EnqueueForPlayback(_chatDuplicate, _chatDuplicate.Length);
            Client.Poll();
            AssertNoSendFailures();
        }

        public void QueueCombat(uint eventId, uint sequence)
        {
            PrepareEvent(_combatPrimary, _combatPayload, ReliableEventType.Combat, eventId, sequence);
            PrepareEvent(_combatDuplicate, _combatPayload, ReliableEventType.Combat, eventId, sequence + 1);
            Transport.EnqueueForPlayback(_combatPrimary, _combatPrimary.Length);
            Transport.EnqueueForPlayback(_combatDuplicate, _combatDuplicate.Length);
            Client.Poll();
            AssertNoSendFailures();
        }

        private void AssertNoSendFailures()
        {
            long failures = Transport.Metrics.SendErrors;
            if (failures != 0)
                throw new InvalidOperationException($"NetClient queue fixture produced {failures} ACK send failures for bound sink {_server}.");
        }

        public bool TryDequeueOwnedChat(out NetApplicationEvent message)
        {
            // NetClient should have copied payload[4..] into its application
            // queue. Mutating the source after Poll catches aliasing regressions.
            _chatPrimary[NetHeader.Size + ReliableEventPacket.HeaderSize + 4] ^= 0x7f;
            bool present = Client.TryDequeueEvent(out message);
            bool valid = present && message.Type == ReliableEventType.Chat
                && message.Payload.Length == SessionChatPacket.Size
                && BinaryPrimitives.ReadUInt64LittleEndian(message.Payload.Span) == ChatConnectionId;
            return valid && !Client.TryDequeueEvent(out _);
        }

        public bool TryDequeueOwnedCombat(out NetApplicationEvent message)
        {
            _combatPrimary[NetHeader.Size + ReliableEventPacket.HeaderSize + 4] ^= 0x7f;
            bool present = Client.TryDequeueEvent(out message);
            Span<CombatEvent> events = stackalloc CombatEvent[CombatEventBatch.MaxCount];
            bool valid = present && message.Type == ReliableEventType.Combat
                && CombatEventBatch.TryRead(message.Payload.Span, events, out int count) && count == 1;
            return valid && !Client.TryDequeueEvent(out _);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Client.Dispose(); }
            finally { Transport.Dispose(); }
        }

        private byte[] BuildAcceptedDatagram()
        {
            byte[] datagram = new byte[DatagramSize(JoinAcceptedPacket.Size)];
            new NetHeader(NetMessageType.Accepted, NetHeaderFlags.None,
                ClientConnectionId, 0, 0, 0).Write(datagram);
            Span<byte> body = datagram.AsSpan(NetHeader.Size);
            Span<byte> accepted = body[ReliableEventPacket.HeaderSize..];
            new JoinAcceptedPacket(ClientNonce, 0, MatchId, 1, 60,
                GameMode.Battle, "MP1 SANCTORUS").Write(accepted);
            ReliableEventPacket.Write(body, AcceptedEventId, ReliableEventType.Welcome, accepted);
            return datagram;
        }

        private byte[] BuildChatPayload()
        {
            byte[] payload = new byte[4 + SessionChatPacket.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(payload, MatchId);
            new SessionChatPacket(ChatConnectionId, 0, "N12", "queue baseline").Write(payload.AsSpan(4));
            return payload;
        }

        private static byte[] BuildCombatPayload()
        {
            byte[] payload = new byte[CombatEventBatch.MaxSize];
            Span<CombatEvent> source = stackalloc CombatEvent[1];
            source[0] = new CombatEvent(1, 1, 1, CombatEventKind.Shot, 0,
                CombatEventFlags.None, new CombatActor(0, ChatConnectionId, 1),
                CombatActor.None, 0, 0, Vector3.Zero, -Vector3.UnitZ, 0, 0, 0);
            int length = CombatEventBatch.Write(payload, source);
            byte[] result = new byte[4 + length];
            BinaryPrimitives.WriteUInt32LittleEndian(result, MatchId);
            payload.AsSpan(0, length).CopyTo(result.AsSpan(4));
            return result;
        }

        private static int DatagramSize(int payloadLength)
            => NetHeader.Size + ReliableEventPacket.HeaderSize + payloadLength;

        private static void PrepareEvent(byte[] datagram, byte[] payload,
            ReliableEventType type, uint eventId, uint sequence)
        {
            new NetHeader(NetMessageType.Event, NetHeaderFlags.None,
                ClientConnectionId, sequence, 0, 0).Write(datagram);
            int length = ReliableEventPacket.Write(datagram.AsSpan(NetHeader.Size), eventId, type, payload);
            if (length + NetHeader.Size != datagram.Length)
                throw new InvalidOperationException("N12 queue datagram length drifted from its prebuilt payload.");
        }
    }

    private sealed class BaselineTransport : INetTransport
    {
        private readonly Queue<ReceivedPacket> _incoming = new();
        public int LocalPort => 0;
        public long PacketsDropped => 0;
        public int QueuedPackets => 0;
        public int HeldIncomingPackets => 0;
        public int HeldOutgoingPackets => 0;
        public NetTrafficMetrics Metrics { get; } = new();

        public void Enqueue(ReceivedPacket packet) => _incoming.Enqueue(packet);
        public IEnumerable<ReceivedPacket> Drain()
        {
            while (_incoming.Count > 0) yield return _incoming.Dequeue();
        }
        public int Drain(Span<ReceivedPacket> destination)
        {
            int count = 0;
            while (count < destination.Length && _incoming.TryDequeue(out ReceivedPacket packet))
                destination[count++] = packet;
            return count;
        }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram) { }
        public void SendDatagram(IPEndPoint target, ReadOnlySpan<byte> datagram, long extraHoldTicks) { }
        public void Send(IPEndPoint target, PacketType type, ReadOnlySpan<byte> payload, long extraHoldTicks = 0) { }
        public void SetKeepAlive(IPEndPoint? target, ReadOnlySpan<byte> datagram = default) { }
        public void SetKeepAlives(ReadOnlySpan<NetKeepAlive> entries) { }
        public void AnswerPingsImmediately() { }
        public void EnqueueForPlayback(byte[] data, int length) { }
        public void Dispose() { }
    }

    private sealed class PerformanceBaselineReport
    {
        public int SchemaVersion { get; init; }
        public string Kind { get; init; } = String.Empty;
        public string ContentMode { get; init; } = String.Empty;
        public DateTimeOffset GeneratedUtc { get; init; }
        public string GitCommit { get; init; } = String.Empty;
        public bool GitDirty { get; init; }
        public PerformanceRuntime Runtime { get; init; } = new();
        public int WarmupIterations { get; init; }
        public int SampleCount { get; init; }
        public int OperationsPerSample { get; init; }
        public string PercentileDefinition { get; init; } = String.Empty;
        public double TotalDurationMilliseconds { get; init; }
        public PerformanceScenario[] Scenarios { get; init; } = Array.Empty<PerformanceScenario>();
    }

    private sealed class PerformanceRuntime
    {
        public string Framework { get; init; } = String.Empty;
        public string RuntimeVersion { get; init; } = String.Empty;
        public string OperatingSystem { get; init; } = String.Empty;
        public string ProcessArchitecture { get; init; } = String.Empty;
        public int ProcessorCount { get; init; }
        public long StopwatchFrequency { get; init; }
        public bool Is64BitProcess { get; init; }
    }

    private sealed class PerformanceScenario
    {
        public string Name { get; init; } = String.Empty;
        public string Status { get; init; } = String.Empty;
        public string Workload { get; init; } = String.Empty;
        public double DurationMilliseconds { get; set; }
        public int SampleCount { get; init; }
        public int WarmupIterations { get; init; }
        public int OperationsPerSample { get; init; }
        public long Iterations { get; init; }
        public long AllocatedBytes { get; init; }
        public double AllocatedBytesPerOperation { get; init; }
        public long? AllocationBudgetBytesPerOperation { get; init; }
        public bool AllocationBudgetPassed { get; init; } = true;
        public double NanosecondsPerOperation { get; init; }
        public double P50Nanoseconds { get; init; }
        public double P95Nanoseconds { get; init; }
        public double P99Nanoseconds { get; init; }
        public double P999Nanoseconds { get; init; }
        public double MaxNanoseconds { get; init; }
        public int Gen0Collections { get; init; }
        public int Gen1Collections { get; init; }
        public int Gen2Collections { get; init; }
        public string? Exception { get; init; }
    }
}
