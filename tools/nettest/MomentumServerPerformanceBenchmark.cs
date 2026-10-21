using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.Entities;
using MphRead.Mods.Movement;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// Separate from the historical synthetic-human-intent benchmark. ServerSim owns
// actual authority, native spawning, AI controls, combat and snapshot publication.
internal static class MomentumServerPerformanceBenchmark
{
    private const int Players = 8, WarmSteps = 1200;
    private const uint Seed1 = 431, Seed2 = 0x3DE9179B;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed class SlotWork
    {
        public int Slot { get; init; }
        public string Hunter { get; init; } = "";
        public int Level { get; init; }
        public long ActiveSteps { get; set; }
        public long AliveSteps { get; set; }
        public long BipedSteps { get; set; }
        public long EligibleSteps { get; set; }
        public long AltSteps { get; set; }
        public long TransitionSteps { get; set; }
        public long MovingSteps { get; set; }
        public double Travel { get; set; }
        public long Deaths { get; set; }
        public long Respawns { get; set; }
        public long IntentEvents { get; set; }
        public long MovingIntentEvents { get; set; }
        public long PartialIntentEvents { get; set; }
        public long DiagonalIntentEvents { get; set; }
        public int ContactOverflows { get; set; }
        public int RecoveryFallbacks { get; set; }
        public ushort LastLife { get; set; }
        public ushort LastGeneration { get; set; }
    }
    private readonly record struct Totals(long Shots, long Damage, long Claims, long AppliedClaims,
        long RefusedClaims, long RewoundShots, long RewoundFrames, long HistoryQueries, long HistoryMisses,
        long CatchUpSteps, long Snapshots, long SnapshotBytes, long BotIntents);
    // Opt-in diagnostic only. These are thread execution cycles, never CPU milliseconds.
    private sealed class ThreadCycleObserver : IDisposable
    {
        private readonly UIntPtr _previousAffinity;
        private readonly ulong _allowed, _selected;
        private readonly uint _startProcessor;
        private readonly ulong[] _raw;
        private bool _restored;
        [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr SetThreadAffinityMask(IntPtr thread, UIntPtr mask);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessorNumber();
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private ThreadCycleObserver(int steps)
        {
            _raw = new ulong[steps];
            using var process = Process.GetCurrentProcess();
            _allowed = unchecked((ulong)process.ProcessorAffinity.ToInt64());
            Require(_allowed != 0, "thread-cycle observer requires a nonempty process affinity mask");
            _selected = _allowed & (~_allowed + 1);
            _previousAffinity = SetThreadAffinityMask(GetCurrentThread(), (UIntPtr)_selected);
            if (_previousAffinity == UIntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { _startProcessor = GetCurrentProcessorNumber(); Read(); }
            catch { Dispose(); throw; }
        }
        internal static ThreadCycleObserver? Create(int steps) => OperatingSystem.IsWindows()
            && Environment.GetEnvironmentVariable("PRIME_BENCHMARK_THREAD_CYCLES") == "1" ? new(steps) : null;
        internal ulong Read()
        {
            if (!QueryThreadCycleTime(GetCurrentThread(), out ulong cycles))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return cycles;
        }
        internal void Record(int tick, ulong before, ulong after)
        { Require(after >= before, "thread execution cycle counter must be monotonic"); _raw[tick] = after - before; }
        internal object Report()
        {
            ulong[] ordered = (ulong[])_raw.Clone(); Array.Sort(ordered);
            ulong P(double q) => ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * q) - 1, 0, ordered.Length - 1)];
            return new { unit = "thread CPU execution cycles (user plus kernel)", diagnosticOnly = true,
                scope = "current simulation thread; includes the two wall timestamp reads; excludes waiting/preempted execution; no cycles-to-time conversion or calibration subtraction",
                allowedAffinityMask = _allowed.ToString("X", CultureInfo.InvariantCulture),
                selectedAffinityMask = _selected.ToString("X", CultureInfo.InvariantCulture),
                startProcessor = _startProcessor, endProcessor = GetCurrentProcessorNumber(),
                p50 = P(.5), p95 = P(.95), p99 = P(.99), rawStepCycles = _raw,
                caveat = "same hardware/core/runtime settings required; hardware cycle timer frequency behavior varies; differing AI/world work remains unmatched" };
        }
        public void Dispose()
        {
            if (_restored) return;
            if (SetThreadAffinityMask(GetCurrentThread(), _previousAffinity) == UIntPtr.Zero)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _restored = true;
        }
    }
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static int Run(string[] args)
    {
        string? output = null;
        try
        {
            if (args.Length is < 5 or > 7)
                throw new ArgumentException("--momentum-server-performance ASSETS ROOM Native|Momentum|Quake3|Traversal|Parkour OUTPUT_JSON [SECONDS>=60] [timing|verify]");
            output = Path.GetFullPath(args[4]);
            Require(MovementModelRules.TryParse(args[3], out var model) && !string.IsNullOrEmpty(args[3]), "explicit Native, Momentum, Quake3, Traversal or Parkour model required");
            double seconds = args.Length > 5 ? double.Parse(args[5], CultureInfo.InvariantCulture) : 60;
            Require(double.IsFinite(seconds) && seconds is >= 60 and <= 600, "duration must cover 3600..36000 fixed simulation steps");
            string phase = args.Length > 6 ? args[6] : "timing";
            Require(phase is "timing" or "verify", "phase must be timing or verify");
            Require(NetShotProof.Mode == ShotProofMode.Shadow && NetFireEvents.AcceptedPlaybackEnabled,
                "benchmark requires production-default shot proof Shadow and accepted-shot playback enabled");
            Directory.SetCurrentDirectory(Path.GetFullPath(args[1])); Paths.UpdatePaths(); Paths.ChooseMphPath();
            Require(!NetSession.Active && !DemoPlayback.IsActive, "benchmark requires a fresh inactive non-replay process");
            Require(ServerSim.Available(out string reason), reason);
            object result = Measure(args[2], model, checked((int)(seconds * 60)), phase == "verify");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(result, JsonOptions));
            var status = JsonSerializer.SerializeToElement(result);
            bool passed = !status.TryGetProperty("failure", out var failure) || failure.ValueKind == JsonValueKind.Null;
            Console.WriteLine($"MOMENTUM SERVER PERFORMANCE {(passed ? "PASS" : "FAIL")} {model}/{phase}: 8 actual AI bots; evidence {output}");
            return passed ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (output != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, JsonSerializer.Serialize(new { failure = ex.ToString(), acceptedPerformanceEvidence = false }, JsonOptions));
            }
            Console.Error.WriteLine("MOMENTUM SERVER PERFORMANCE FAIL " + ex); return 1;
        }
    }

    private static object Measure(string room, MovementModel model, int steps, bool verify)
    {
        using var threadCycles = ThreadCycleObserver.Create(steps);
        // Test-only opt-in export. Reuse existing Step wall timestamps; no EventSource/ETW writes in the sample window.
        bool spanRequested = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_STEP_SPANS") == "1";
        bool spanSupported = spanRequested && OperatingSystem.IsWindows();
        long[]? spanStarts = spanSupported ? new long[steps] : null;
        long[]? spanEnds = spanSupported ? new long[steps] : null;
        uint spanNativeThread = spanSupported ? GetCurrentThreadId() : 0;
        Rng.SetRng1(Seed1); Rng.SetRng2(Seed2);
        var roster = RosterPacket.Create();
        roster.Count = Players; roster.MatchId = 7; roster.AuthorityEpoch = 23; roster.Revision = 1; roster.ContainsBots = true;
        for (byte slot = 0; slot < Players; slot++)
        {
            roster.Slots[slot] = slot; roster.Generations[slot] = 1;
            roster.Hunters[slot] = (byte)(slot % 7); roster.BotLevels[slot] = (byte)(slot % 4);
            roster.Flags[slot] = 1; roster.Teams[slot] = -1; roster.Names[slot] = "Perf bot " + slot;
        }
        var definition = new MatchDefinition { RoomKey = room, Mode = GameMode.Battle,
            Format = MatchFormat.Auto, MovementModel = model, TimeLimitSeconds = 0, PointGoal = 10000 };
        var session = new SessionStatePacket { MatchId = roster.MatchId, AuthorityEpoch = roster.AuthorityEpoch,
            Revision = 1, Phase = SessionPhase.InMatch, MaxPlayers = Players,
            WorldProfile = MatchWorldProfile.Resolve(Players), Match = definition };
        var sim = new ServerSim(); Scene? scene = null;
        var work = Enumerable.Range(0, Players).Select(slot => new SlotWork { Slot = slot,
            Hunter = ((Hunter)roster.Hunters[slot]).ToString(), Level = roster.BotLevels[slot] }).ToArray();
        long snapshots = 0, snapshotBytes = 0, intents = 0, verifiedSnapshotBodies = 0;
        uint lastSnapshot = 0;
        bool measuring = false;
        using var snapshotHash = verify ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        long hashedSnapshotPayloads = 0;
        Action<int, IntentPacket>? originalSink = NetBotInput.Sink;
        try
        {
            Require(sim.Start(room, GameMode.Battle, Players, payload =>
            {
                snapshots++; snapshotBytes += payload.Length;
                if (snapshotHash != null && measuring)
                {
                    snapshotHash.AppendData(payload);
                    hashedSnapshotPayloads++;
                }
                if (!verify || scene == null) return;
                var header = SnapshotHeader.Read(payload);
                Require(header.Frame > lastSnapshot && header.MatchId == roster.MatchId && header.AuthorityEpoch == roster.AuthorityEpoch,
                    "actual snapshot frame/identity must advance"); lastSnapshot = header.Frame;
                for (int i = 0; i < header.PlayerCount; i++)
                {
                    var state = PlayerState.Read(payload.Slice(SnapshotHeader.Size + i * PlayerState.Size, PlayerState.Size));
                    Require(state.SlotIndex < Players, "snapshot contains requested native slots only");
                    var body = scene.Players.Items[state.SlotIndex];
                    Require(state.Position == body.Position && state.Speed == body.Speed
                        && state.SlotGeneration == NetPlayerLifecycle.Generation(state.SlotIndex)
                        && state.LifeId == NetPlayerLifecycle.Get(state.SlotIndex), "published native AI body and lifecycle agree exactly");
                    verifiedSnapshotBodies++;
                }
            }, () => { }, roster, session), "actual ServerSim.Start with eight bot roster");
            scene = sim.EvidenceScene ?? throw new InvalidOperationException("authority scene missing");
            Require(NetSession.Role == NetRole.Server && NetSession.IsAuthority && NetSession.LocalSlot == -1
                && NetRoomChange.GameplayReady && !NetSession.FreezeGameplay, "production authority and native world readiness");
            Require(scene.GameState.MovementModel == model && NetSession.ActiveMatchDefinition?.MovementModel == model,
                "actual frozen session applies selected model");
            var loadedRng = new { scene.Random.Rng1, scene.Random.Rng2 };
            scene.Random.SetRng1(Seed1); scene.Random.SetRng2(Seed2);
            typeof(Scene).GetField("_developerMomentumDiagnostics", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scene, verify);
            NetBotInput.Sink = (slot, intent) =>
            {
                intents++;
                if (verify)
                    Require((uint)slot < Players && intent.Frame == NetSession.NetFrame
                        && intent.MatchId == roster.MatchId && intent.AuthorityEpoch == roster.AuthorityEpoch
                        && NetPlayerLifecycle.Matches(slot, intent.SlotGeneration, intent.LifeId), "actual bot intent belongs to current native lifecycle");
                if (!measuring || (uint)slot >= Players) return;
                SlotWork row = work[slot]; row.IntentEvents++;
                if (intent.MoveX != 0 || intent.MoveY != 0) row.MovingIntentEvents++;
                if (Math.Abs((int)intent.MoveX) is > 0 and < 127 || Math.Abs((int)intent.MoveY) is > 0 and < 127) row.PartialIntentEvents++;
                if (intent.MoveX != 0 && intent.MoveY != 0) row.DiagonalIntentEvents++;
            };
            int Alive() => scene.Players.Items.Take(Players).Count(p => p.ModIsInPlay && p.Health > 0);
            void Identities()
            {
                Require(scene.Players.Items.Count >= Players && scene.Players.PlayerCount == Players, "requested native bot roster size persists");
                for (int slot = 0; slot < Players; slot++)
                {
                    var player = scene.Players.Items[slot];
                    Require(player.SlotIndex == slot && player.IsBot && !NetHooks.IsPuppet(player)
                        && player.Hunter == (Hunter)roster.Hunters[slot] && player.BotLevel == roster.BotLevels[slot]
                        && player.LoadFlags.TestFlag(LoadFlags.Active) && NetPlayerLifecycle.Generation(slot) != 0
                        && NetPlayerLifecycle.Get(slot) != 0, "all eight actual native bots spawned with requested identities");
                }
            }
            int warm = 0, firstAllEightAliveWarmStep = -1;
            for (; warm < WarmSteps; warm++)
            {
                sim.Step();
                Require(sim.StepFailures == 0, "no failed native warm-up steps");
                if (firstAllEightAliveWarmStep < 0 && Alive() == Players)
                    firstAllEightAliveWarmStep = warm + 1;
            }
            Require(firstAllEightAliveWarmStep > 0, "fixed warm-up must observe eight simultaneously living native bots without forced health/life");
            int warmAliveMask = 0;
            for (int slot = 0; slot < Players; slot++)
                if (scene.Players.Items[slot].ModIsInPlay && scene.Players.Items[slot].Health > 0)
                    warmAliveMask |= 1 << slot;
            int warmAliveCount = Alive();
            Identities();
            var warmBodies = scene.Players.Items.Take(Players).Select(p => new { slot = p.SlotIndex,
                hunter = p.Hunter.ToString(), p.BotLevel, p.Health, life = NetPlayerLifecycle.Get(p.SlotIndex),
                generation = NetPlayerLifecycle.Generation(p.SlotIndex), position = new[] { p.Position.X, p.Position.Y, p.Position.Z } }).ToArray();
            var warmedRng = new { scene.Random.Rng1, scene.Random.Rng2 };
            var previousPositions = scene.Players.Items.Take(Players).Select(p => p.Position).ToArray();
            var previousAlive = scene.Players.Items.Take(Players).Select(p => p.ModIsInPlay && p.Health > 0).ToArray();
            int[] baseOverflows = scene.Players.Items.Take(Players).Select(p => p.ModCollisionContactOverflows).ToArray();
            int[] baseFallbacks = scene.Players.Items.Take(Players).Select(p => p.ModCollisionRecoveryFallbacks).ToArray();
            var occupancy = new long[Players + 1]; var alivePerStep = new int[steps]; var eligiblePerStep = new int[steps];
            var raw = new double[steps]; var solverObservedSlots = new bool[Players];
            long solverSampleTicks = 0, positiveInputSolverTicks = 0;
            Totals Counters() => new(NetDamage.Fired.Sum(value => (long)value), NetDamage.ResolvedSession.Sum(value => (long)value),
                NetHitClaims.Received, NetHitClaims.AppliedHere, NetHitClaims.RefusedHere,
                NetUnlagged.ShotsCompensated, NetUnlagged.FramesRewound, NetUnlagged.HistoricalCollisionQueries,
                NetUnlagged.HistoryMisses, NetUnlagged.CatchUpSteps, snapshots, snapshotBytes, intents);
            var countersBefore = Counters();
            long allocated = 0, processBytesBefore = GC.GetTotalAllocatedBytes(precise: true);
            long failedBefore = sim.StepFailures, droppedBefore = sim.DroppedSteps, stallsBefore = sim.Stalls;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            TimeSpan pauseBefore = GC.GetTotalPauseDuration();
            using var process = Process.GetCurrentProcess();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            long loopBefore = Stopwatch.GetTimestamp(), solverBefore = scene.DeveloperMomentumSteps;
            measuring = true;
            for (int tick = 0; tick < steps; tick++)
            {
                ulong frame = scene.FrameCount;
                long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
                ulong cycleBefore = threadCycles?.Read() ?? 0;
                long started = Stopwatch.GetTimestamp();
                sim.Step();
                long wallEnded = Stopwatch.GetTimestamp(); // Stop wall timing before the end-cycle API read.
                if (threadCycles != null) threadCycles.Record(tick, cycleBefore, threadCycles.Read());
                raw[tick] = (wallEnded - started) * (1000d / Stopwatch.Frequency);
                if (spanStarts != null) { spanStarts[tick] = started; spanEnds![tick] = wallEnded; }
                allocated += GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
                int alive = 0, eligible = 0;
                // Matched nonallocating observations are outside the sim.Step timer.
                for (int slot = 0; slot < Players; slot++)
                {
                    var player = scene.Players.Items[slot]; SlotWork row = work[slot];
                    bool living = player.ModIsInPlay && player.Health > 0;
                    if (player.LoadFlags.TestFlag(LoadFlags.Active)) row.ActiveSteps++;
                    if (living)
                    {
                        alive++; row.AliveSteps++;
                        if (player.IsMorphing || player.IsUnmorphing) row.TransitionSteps++;
                        else if (player.IsAltForm) row.AltSteps++;
                        else row.BipedSteps++;
                        if (player.ModMomentumEligible) { row.EligibleSteps++; eligible++; }
                        float displacement = (player.Position - previousPositions[slot]).Length;
                        if (previousAlive[slot] && displacement > .00001f && displacement < 4)
                        { row.MovingSteps++; row.Travel += displacement; }
                        row.LastLife = NetPlayerLifecycle.Get(slot); row.LastGeneration = NetPlayerLifecycle.Generation(slot);
                    }
                    if (previousAlive[slot] && !living) row.Deaths++;
                    if (!previousAlive[slot] && living) row.Respawns++;
                    previousAlive[slot] = living; previousPositions[slot] = player.Position;
                }
                occupancy[alive]++; alivePerStep[tick] = alive; eligiblePerStep[tick] = eligible;
                if (verify && scene.LastMomentumStep is { } sample && sample.SimulationFrame == frame)
                {
                    solverSampleTicks++; solverObservedSlots[sample.Slot] = true;
                    if (sample.Input.MoveAxes.LengthSquared > .000001f) positiveInputSolverTicks++;
                    var incoming = new OpenTK.Mathematics.Vector3(sample.NativeVelocityBefore.X * 30, 0, sample.NativeVelocityBefore.Z * 30);
                    if (sample.GroundSupport.HasValue)
                        incoming = GroundSlopeMovement.Flatten(sample.NativeVelocityBefore) * 30;
                    var expected = model switch
                    {
                        MovementModel.Momentum => MomentumProfileV1.Step(incoming, sample.Input),
                        MovementModel.Quake3 => Quake3MovementProfileV4.Step(incoming, sample.Input),
                        MovementModel.Traversal => TraversalMovementProfileV5.Step(incoming, sample.Input, sample.Crouched, sample.Sliding),
                        MovementModel.Parkour => ParkourMovementProfileV3.Step(incoming, sample.Input, sample.Crouched, sample.Sliding),
                        _ => throw new InvalidOperationException("Classic cannot produce a momentum solver sample.")
                    };
                    Require(MathF.Abs(expected.Velocity.X / 30 - sample.NativeVelocityAfter.X) <= .00001f
                        && MathF.Abs(expected.Velocity.Z / 30 - sample.NativeVelocityAfter.Z) <= .00001f
                        && BitConverter.SingleToInt32Bits(sample.NativeVelocityBefore.Y) == BitConverter.SingleToInt32Bits(sample.NativeVelocityAfter.Y),
                        "verification sample uses canonical actual bot solver and preserves Y");
                }
            }
            measuring = false;
            double loopSeconds = Stopwatch.GetElapsedTime(loopBefore).TotalSeconds;
            double processCpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            long processAllocationBytes = GC.GetTotalAllocatedBytes(precise: true) - processBytesBefore;
            double pauseMilliseconds = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            int gen0 = GC.CollectionCount(0) - g0, gen1 = GC.CollectionCount(1) - g1, gen2 = GC.CollectionCount(2) - g2;
            long solverApplications = scene.DeveloperMomentumSteps - solverBefore;
            Totals after = Counters();
            if (verify) Require(hashedSnapshotPayloads > 0 && hashedSnapshotPayloads == after.Snapshots - countersBefore.Snapshots,
                "verification digest covers every actual published snapshot in the measured window");
            Identities();
            var workloadFailures = new List<string>();
            if (sim.StepFailures != failedBefore) workloadFailures.Add("Native simulation step failure invalidates measured window.");
            Require(scene.GameState.MovementModel == model && NetSession.ActiveMatchDefinition?.MovementModel == model,
                "measured world retains frozen selected model");
            foreach (SlotWork row in work)
            {
                if (!(row.ActiveSteps == steps && row.AliveSteps > 60 && row.MovingSteps > 30 && row.Travel > 1
                    && row.IntentEvents > 0 && row.MovingIntentEvents > 0))
                    workloadFailures.Add($"Slot {row.Slot} lacks nontrivial native work: active={row.ActiveSteps}, alive={row.AliveSteps}, moving={row.MovingSteps}, travel={row.Travel}, intent={row.IntentEvents}, movingIntent={row.MovingIntentEvents}.");
                if (MovementModelRules.UsesMomentumSolver(model) && row.EligibleSteps <= 0)
                    workloadFailures.Add($"Slot {row.Slot} has no measured eligible {model} biped steps.");
                var player = scene.Players.Items[row.Slot];
                row.ContactOverflows = player.ModCollisionContactOverflows - baseOverflows[row.Slot];
                row.RecoveryFallbacks = player.ModCollisionRecoveryFallbacks - baseFallbacks[row.Slot];
                Require(row.ContactOverflows >= 0 && row.RecoveryFallbacks >= 0, "counter reset invalidates collision evidence");
            }
            if (after.Shots <= countersBefore.Shots)
                workloadFailures.Add("Measured actual AI workload has no proven shots; combat benchmark is invalid.");
            if (verify && work.Sum(row => row.PartialIntentEvents) <= 0)
                workloadFailures.Add("Verification window has no genuine partial analog AI intent events.");
            if (verify && MovementModelRules.UsesMomentumSolver(model)
                && !(solverApplications > 0 && solverSampleTicks > 0 && positiveInputSolverTicks > 0))
                workloadFailures.Add($"Diagnostic verification has no proven actual nonzero-input {model} solver applications.");
            if (model == MovementModel.Native) Require(scene.DeveloperMomentumSteps == 0, "Native control never invokes Momentum solver");
            if (!verify) Require(scene.DeveloperMomentumSteps == 0, "timing arm has diagnostic capture disabled");
            long aliveSteps = work.Sum(row => row.AliveSteps), activeSteps = work.Sum(row => row.ActiveSteps);
            double[] ordered = (double[])raw.Clone(); Array.Sort(ordered);
            double P(double q) => ordered[Math.Min(ordered.Length - 1, (int)Math.Ceiling(ordered.Length * q) - 1)];
            long Delta(long end, long begin) => end >= begin ? end - begin : throw new InvalidOperationException("counter reset invalidates benchmark evidence");
            return new
            {
                schema = 1, failure = workloadFailures.Count == 0 ? null : string.Join("\n", workloadFailures),
                acceptedPerformanceEvidence = !verify && workloadFailures.Count == 0, phase = verify ? "diagnostic-verification" : "timing",
                model = model.ToString(), profileVersion = MovementModelRules.ProfileVersion(model),
                protocol = NetConfig.ProtocolVersion, commit = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_COMMIT") ?? "working-tree",
                scenario = new { room, players = Players, steps, simulationHz = 60, warmupSteps = warm, warmupPolicy = "fixed 1200 native simulation steps in every model; genuine combat deaths preserved",
                    kind = "actual eight-bot ServerSim authority with genuine AI, native spawning/combat/respawn; no transport", seed1 = Seed1, seed2 = Seed2 },
                environment = new { runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription,
                    architecture = RuntimeInformation.ProcessArchitecture.ToString(), machine = Environment.MachineName,
                    cpu = NetworkBenchmark.CpuDescription(), processors = Environment.ProcessorCount, stopwatchFrequency = Stopwatch.Frequency,
                    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                    tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                    readyToRun = Environment.GetEnvironmentVariable("DOTNET_ReadyToRun"),
                    stepSpansRequested = spanRequested,
                    stepSpansSupport = OperatingSystem.IsWindows() ? "Windows native-TID/raw-QPC export opt-in" : "unsupported; null spans",
                    threadCyclesRequested = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_THREAD_CYCLES") == "1",
                    threadCyclesSupport = OperatingSystem.IsWindows() ? "Windows QueryThreadCycleTime opt-in" : "unsupported; null cycle evidence is not CPU measurement",
                    shotProofMode = NetShotProof.Mode.ToString(), acceptedShotPlayback = NetFireEvents.AcceptedPlaybackEnabled,
                    timedShotProofMode = NetTimedShotProof.Mode.ToString(), campaignEvidenceEnabled = NetCampaignEvidence.Enabled,
                    soakRequested = Environment.GetEnvironmentVariable("PRIME_NET2_SOAK") == "1",
                    serverPerformanceInstrumentation = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PRIME_SERVER_PERF_OUTPUT")) },
                timing = new { unit = "milliseconds", kind = "elapsed wall-clock sim.Step, includes scheduling/GC; not per-step CPU sampling",
                    scope = "sim.Step only, including matched minimal snapshot/AI-intent counters; post-step occupancy observations excluded",
                    mean = raw.Average(), p50 = P(.5), p95 = P(.95), p99 = P(.99), p999 = P(.999), worst = ordered[^1],
                    processCpuMilliseconds, processCpuScope = "whole measured loop including matched post-step observations and verification when enabled",
                    loopSeconds, simulationStepsPerWallSecond = steps / loopSeconds, rawStepMilliseconds = raw },
                threadCycles = threadCycles?.Report(),
                stepSpans = spanStarts == null ? null : new {
                    schema = 1, clock = "QueryPerformanceCounter via Stopwatch; validate ETL clock and PerfFreq before joining",
                    qpcFrequency = Stopwatch.Frequency, processId = Environment.ProcessId,
                    nativeThreadId = spanNativeThread, nativeThreadIdAtReport = GetCurrentThreadId(),
                    rawStartQpc = spanStarts, rawEndQpc = spanEnds,
                    scope = "existing timestamps around sim.Step only; export stores occur after wall end; no marker provider or per-step event emission; ETW reconstruction required for CPU milliseconds" },
                allocation = new { simStepCurrentThreadBytes = allocated, bytesPerStep = allocated / (double)steps,
                    bytesPerAlivePlayerStep = allocated / (double)aliveSteps, bytesPerActivePlayerStep = allocated / (double)activeSteps,
                    aliveSteps, activeSteps, processAllocationBytes, processScope = "whole measured loop; includes observers/verification",
                    gen0, gen1, gen2, gcPauseMilliseconds = pauseMilliseconds },
                workload = new { slots = work, alivePerStep, eligiblePerStep, aliveOccupancyHistogram = occupancy,
                    warmBodies, warmAliveMask, warmAliveCount, firstAllEightAliveWarmStep, loadedRng, warmedRng, seedScope = "scene.Random reset after actual Start/OnLoad and before warm-up; asset loading precedes explicit seed reset", finalRng = new { scene.Random.Rng1, scene.Random.Rng2 },
                    shotsFired = Delta(after.Shots, countersBefore.Shots), damageEvents = Delta(after.Damage, countersBefore.Damage),
                    publishedSnapshots = Delta(after.Snapshots, countersBefore.Snapshots), publishedSnapshotBytes = Delta(after.SnapshotBytes, countersBefore.SnapshotBytes),
                    botIntentEvents = Delta(after.BotIntents, countersBefore.BotIntents),
                    throughputScope = "logical published snapshot payloads and AI intent events; no UDP packet/byte throughput claim" },
                verification = new { diagnosticsEnabled = verify, solverApplications, solverSampleTicks, positiveInputSolverTicks, solverObservedSlots,
                    observedSlotsScope = "LastMomentumStep is one bounded cell; later eligible bots can overwrite earlier slots, so no per-slot invocation count claim",
                    verifiedSnapshotBodies, allEightAliveObservedDuringWarmup = firstAllEightAliveWarmStep > 0,
                    snapshotPayloadsHashed = hashedSnapshotPayloads,
                    snapshotPayloadSha256 = snapshotHash == null ? null : Convert.ToHexString(snapshotHash.GetHashAndReset()),
                    snapshotDigestScope = "verification only: concatenated actual published snapshot payload bytes in measured window; timing disabled; not all per-frame game state" },
                networkAndCollision = new { stepFailures = sim.StepFailures - failedBefore, dropped = sim.DroppedSteps - droppedBefore,
                    stalls = sim.Stalls - stallsBefore, overruns = raw.Count(value => value > 1000d / 60),
                    claimsReceived = Delta(after.Claims, countersBefore.Claims), claimsApplied = Delta(after.AppliedClaims, countersBefore.AppliedClaims),
                    claimsRefused = Delta(after.RefusedClaims, countersBefore.RefusedClaims), compensatedShots = Delta(after.RewoundShots, countersBefore.RewoundShots),
                    rewindFrames = Delta(after.RewoundFrames, countersBefore.RewoundFrames), catchupSteps = Delta(after.CatchUpSteps, countersBefore.CatchUpSteps),
                    historyQueries = Delta(after.HistoryQueries, countersBefore.HistoryQueries), historyMisses = Delta(after.HistoryMisses, countersBefore.HistoryMisses),
                    contactOverflows = work.Sum(row => row.ContactOverflows), recoveryFallbacks = work.Sum(row => row.RecoveryFallbacks) },
                gate = new { matchedNativeP95IncreaseLimit = .05, appliesOnlyToTiming = true,
                    interpretation = "paired repeated process experiment; >5% p95 increase stops for investigation, not a hardware-independent product guarantee" },
                limitations = new[] { "No UDP/client CPU/fault or rendered-device acceptance", "AI physics diverges between models; compare body/alive/form/shot/collision distributions",
                    "This is full-world combat cost, not zero-allocation solver microbenchmark", "No forced health, life, controls, speed, roles or readiness" }
            };
        }
        finally { measuring = false; sim.Stop(); NetBotInput.Sink = originalSink; }
    }
}
