using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        var scenarios = new List<PerformanceScenario>(11)
        {
            RunSafe("SnapshotPacket.Write", MeasureSnapshotWrite),
            RunSafe("LagCompensationPolicy.ResolveTick", MeasureLagCompensationPolicy),
            RunSafe("LagCompensationHistory.TryGet", MeasureLagCompensationHistory),
            RunSafe("ServerInputStream.ReceiveTake", MeasureServerInputStream),
            RunSafe("ReliableChannel.TryGetDue.MarkSent", MeasureReliableDue),
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
        long? allocationBudgetBytesPerOperation = null)
    {
        for (int warmup = 0; warmup < WarmupIterations; warmup++)
            for (int iteration = 0; iteration < OperationsPerSample; iteration++) operation();

        var samples = new double[SampleCount];

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
            for (int sample = 0; sample < SampleCount; sample++)
            {
                long start = Stopwatch.GetTimestamp();
                for (int iteration = 0; iteration < OperationsPerSample; iteration++) operation();
                long elapsed = Stopwatch.GetTimestamp() - start;
                totalTicks += elapsed;
                samples[measuredSamples++] = elapsed * NanosecondsPerTick / OperationsPerSample;
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
        var percentile = new BoundedPercentileSampler(SampleCount);
        for (int i = 0; i < measuredSamples; i++) percentile.Record(samples[i]);
        BoundedPercentileSnapshot summary = percentile.Snapshot();
        long operations = (long)measuredSamples * OperationsPerSample;
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
            OperationsPerSample = OperationsPerSample,
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
