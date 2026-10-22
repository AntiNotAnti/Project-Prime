using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Network;

namespace MphRead.NetTest;

// The same runner can load the saved pre-change ProjectPrime.dll for a matched
// baseline. Read protocol/optional diagnostics at runtime, not as inlined constants.
internal static class ServerPerformanceBenchmark
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length < 5) throw new ArgumentException("--server-performance ASSET_DIRECTORY ROOM PLAYERS OUTPUT_JSON [SECONDS]");
            Directory.SetCurrentDirectory(Path.GetFullPath(args[1]));
            MphRead.Paths.UpdatePaths(); MphRead.Paths.ChooseMphPath();
            string room = args[2]; int players = int.Parse(args[3]);
            int steps = args.Length > 5 ? (int)(double.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture) * 60) : 1800;
            if (players is < 2 or > 8 || steps is < 60 or > 36000) throw new ArgumentOutOfRangeException(nameof(args));
            var sim = new ServerSim();
            if (!sim.Start(room, GameMode.Battle, players, _ => { }, () => { })) return 1;
            try
            {
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, NextRoomKey = "", Mode = (byte)GameMode.Battle,
                    Flags = MatchStatePacket.FlagInProgress, PointGoal = 10000, TimeRemaining = 3600 }, false);
                // Keep roster setup after the final match state; the shared check helper
                // applies its own minimal match state and can invalidate this workload.
                var roster = RosterPacket.Create();
                roster.MatchId = 1; roster.AuthorityEpoch = 1; roster.Revision = 1;
                for (byte slot = 0; slot < players; slot++)
                {
                    roster.Slots[slot] = slot; roster.Generations[slot] = 1;
                    roster.Hunters[slot] = (byte)(slot % 7); roster.Names[slot] = $"SIM{slot + 1}";
                    roster.Count++;
                }
                NetSession.ApplyRoster(roster);
                NetSlotManager.Sync(); // Activate requested native hunters before recording setup state.
                // Read this server's actual scene once; never alter a global RNG context.
                var scene = (Scene)typeof(ServerSim).GetField("_scene", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sim)!;
                object[] Bodies() => MphRead.Entities.PlayerEntity.Players.Take(players).Select(player => (object)new {
                    slot = player.SlotIndex, hunter = (int)player.Hunter,
                    generation = NetPlayerLifecycle.Generation(player.SlotIndex), life = NetPlayerLifecycle.Get(player.SlotIndex),
                    inPlay = player.ModIsInPlay, health = player.Health,
                    x = player.Position.X, y = player.Position.Y, z = player.Position.Z }).ToArray();
                void CheckRoster()
                {
                    if (MphRead.Entities.PlayerEntity.Players.Count < players) throw new InvalidOperationException("Missing benchmark roster bodies");
                    for (int slot = 0; slot < players; slot++)
                    {
                        var player = MphRead.Entities.PlayerEntity.Players[slot];
                        if (player.SlotIndex != slot || (int)player.Hunter != slot % 7 || (player.LoadFlags & MphRead.Entities.LoadFlags.Active) == 0 || NetPlayerLifecycle.Get(slot) == 0)
                            throw new InvalidOperationException("Benchmark roster identity/active state differs from requested workload");
                    }
                }
                int Active() => MphRead.Entities.PlayerEntity.Players.Take(players).Count(player => (player.LoadFlags & MphRead.Entities.LoadFlags.Active) != 0);
                int Alive() => MphRead.Entities.PlayerEntity.Players.Take(players).Count(player => player.ModIsInPlay && player.Health > 0);
                var beforeWarmupBodies = Bodies(); int beforeWarmupAlive = Alive(), beforeWarmupActive = Active();
                var loadedRng = new { rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2 };
                const uint rng1Seed = 431, rng2Seed = 0x3DE9179B;
                scene.Random.SetRng1(rng1Seed); scene.Random.SetRng2(rng2Seed);
                var initialRng = new { rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2 };
                var edges = new NetInputEdgeSender[players];
                for (int slot = 0; slot < players; slot++) edges[slot] = new();
                void Feed(uint frame)
                {
                    for (int slot = 0; slot < players; slot++)
                    {
                        var player = MphRead.Entities.PlayerEntity.Players[slot];
                        bool shooting = frame % 20 < 6;
                        var buttons = IntentButtons.MoveUp | (shooting ? IntentButtons.Shoot : 0)
                            | (player.ModIsInPlay ? IntentButtons.InPlayState : 0);
                        double turn = frame / 60.0 + slot;
                        NetSession.AcceptSlotIntent(slot, new IntentPacket { Frame = frame, MatchId = 1, AuthorityEpoch = 1,
                            SlotGeneration = NetPlayerLifecycle.Generation(slot), LifeId = NetPlayerLifecycle.Get(slot),
                            Buttons = buttons, Position = player.Position, WeaponSelect = 255, AmmoUa = 400, AmmoMissiles = 50,
                            Aim = new((float)Math.Cos(turn), 0, (float)Math.Sin(turn)), AckFrame = frame > 6 ? frame - 6 : 0,
                            Presses = edges[slot].Record(frame, frame % 20 == 0 ? IntentButtons.Shoot : 0) });
                    }
                }
                for (int slot = 0; slot < players; slot++) LagCompensationPolicy.SetTiming(slot, new(100, 20, 80, 3));
                for (uint i = 1; i <= 300; i++) { Feed(i); sim.Step(); }
                if (sim.StepFailures != 0) throw new InvalidOperationException("Warm-up simulation failed; refusing performance report");
                CheckRoster();
                var afterWarmupBodies = Bodies(); int afterWarmupAlive = Alive(), afterWarmupActive = Active();
                if (afterWarmupActive != players || afterWarmupAlive == 0) throw new InvalidOperationException("Warm-up did not produce requested active player workload");
                var warmedRng = new { rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2 };
                var assembly = typeof(NetConfig).Assembly;
                object? Metric(string type, string field)
                {
                    var t = assembly.GetType("MphRead.Mods.Network." + type);
                    return t?.GetField(field)?.GetValue(null) ?? t?.GetProperty(field)?.GetValue(null);
                }
                long? Count(string type, string field)
                {
                    object? value = Metric(type, field);
                    if (value is Array values)
                    {
                        long total = 0;
                        foreach (object item in values) total += Convert.ToInt64(item);
                        return total;
                    }
                    return value == null ? null : Convert.ToInt64(value);
                }
                // Capture after warm-up. Reflection and reporting stay outside timed steps.
                var before = new System.Collections.Generic.Dictionary<string, long?>();
                foreach (var entry in new[] {
                    ("NetUnlagged", "ShotsCompensated"), ("NetUnlagged", "FramesRewound"), ("NetUnlagged", "CatchUpSteps"),
                    ("NetShadowSampler", "ShadowEligible"), ("NetShadowSampler", "ShadowSampled"),
                    ("NetShadowSampler", "ShadowSimulationSteps"), ("NetShadowSampler", "ShadowSimulationTicks"),
                    ("NetDynamicGeometryHistory", "GeometryObjectsConsidered"), ("NetDynamicGeometryHistory", "ObjectsRewound"),
                    ("NetDynamicGeometryHistory", "HistoryMiss"), ("NetDynamicGeometryHistory", "InterpolationCount"),
                    ("NetDynamicGeometryHistory", "GeometryObjectsSkippedUnchanged"),
                    ("NetUnlagged", "HistoricalCollisionQueries"), ("NetUnlagged", "HistoricalCollisionCacheHits"),
                    ("NetUnlagged", "HistoricalCollisionCacheMisses"), ("NetUnlagged", "HistoricalCollisionBuildTicks"),
                    ("NetHitClaims", "RescueLookupProbes"), ("NetHitClaims", "Received"),
                    ("NetHitClaims", "AppliedHere"), ("NetHitClaims", "DuplicateHere"), ("NetHitClaims", "RefusedHere"),
                    ("NetDamage", "Fired"), ("NetDamage", "ResolvedSession"), ("NetDamage", "DamageByBeam") })
                    before[entry.Item1 + "." + entry.Item2] = Count(entry.Item1, entry.Item2);
                long? Delta(string type, string field)
                {
                    long? start = before[type + "." + field], end = Count(type, field);
                    // A reset or unavailable historical counter is not evidence of zero work.
                    return start.HasValue && end.HasValue && end >= start ? end - start : null;
                }
                long droppedBefore = sim.DroppedSteps, stallsBefore = sim.Stalls;
                var durations = new double[steps];
                long allocations = 0, overruns = 0;
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                long errors = sim.StepFailures;
                TimeSpan pauseBefore = GC.GetTotalPauseDuration();
                for (int i = 0; i < steps; i++)
                {
                    // Intent construction/admission is excluded from step time and allocation.
                    Feed((uint)(301 + i));
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    sim.Step();
                    durations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations += GC.GetAllocatedBytesForCurrentThread() - bytes;
                    if (durations[i] > 1000.0 / 60) overruns++;
                }
                int gen0 = GC.CollectionCount(0) - g0, gen1 = GC.CollectionCount(1) - g1, gen2 = GC.CollectionCount(2) - g2;
                double gcPauseMilliseconds = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
                CheckRoster();
                var endBodies = Bodies(); int endAlive = Alive(), endActive = Active();
                long? measuredShots = Delta("NetDamage", "Fired");
                if (!measuredShots.HasValue || measuredShots <= 0) throw new InvalidOperationException("Measured workload has no proven shots; refusing performance report");
                var finalRng = new { rng1 = scene.Random.Rng1, rng2 = scene.Random.Rng2 };
                // Preserve chronological samples before sorting for percentiles.
                double[] stepMilliseconds = (double[])durations.Clone();
                Array.Sort(durations);
                var result = new
                {
                    commit = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_COMMIT") ?? "working-tree",
                    protocol = (int)typeof(NetConfig).GetField(nameof(NetConfig.ProtocolVersion))!.GetRawConstantValue()!,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    environment = new { machine = Environment.MachineName, cpu = NetworkBenchmark.CpuDescription(), processors = Environment.ProcessorCount,
                        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() },
                    scenario = new { players, room, steps, warmupSteps = 300, kind = "asset-backed synthetic-intent server simulation; no transport",
                        timingScope = "sim.Step only; Feed/intent admission excluded", counterScope = "post-warmup deltas; occupancy is end-of-window; maxRewind is lifetime" },
                    mean = durations.Average(), p50 = durations[(int)(steps * .5)], p95 = durations[(int)(steps * .95)],
                    p99 = durations[(int)(steps * .99)], p999 = durations[Math.Min(steps - 1, (int)(steps * .999))], worst = durations[^1], unit = "milliseconds",
                    rng = new { loaded = loadedRng, initial = initialRng, afterWarmup = warmedRng, end = finalRng,
                        seedScope = "scene.Random reset after Start/roster/match setup; asset loading precedes explicit seeds" },
                    playerWorkload = new { requested = players, beforeWarmupActive, beforeWarmupAlive, afterWarmupActive, afterWarmupAlive, endActive, endAlive,
                        beforeWarmup = beforeWarmupBodies, afterWarmup = afterWarmupBodies, end = endBodies },
                    sampleCount = steps, stepMilliseconds, sampleOrdering = "simulation-step order after warm-up",
                    allocationScope = "current-thread sim.Step only", gcScope = "process-wide measured loop including Feed",
                    allocations, allocationsPerStep = allocations / (double)steps, gen0, gen1, gen2, gcPauseMilliseconds, overruns,
                    dropped = sim.DroppedSteps - droppedBefore, stalls = sim.Stalls - stallsBefore, failures = sim.StepFailures - errors,
                    lag = new { compensated = Delta("NetUnlagged", "ShotsCompensated"), rewind = Delta("NetUnlagged", "FramesRewound"),
                        maxRewind = NetUnlagged.WorstRewind, catchupSteps = Delta("NetUnlagged", "CatchUpSteps"),
                        shadowEligible = Delta("NetShadowSampler", "ShadowEligible"), shadowSampled = Delta("NetShadowSampler", "ShadowSampled"),
                        shadowSteps = Delta("NetShadowSampler", "ShadowSimulationSteps"), shadowTicks = Delta("NetShadowSampler", "ShadowSimulationTicks") },
                    geometry = new { considered = Delta("NetDynamicGeometryHistory", "GeometryObjectsConsidered"),
                        applied = Delta("NetDynamicGeometryHistory", "ObjectsRewound"), historyMisses = Delta("NetDynamicGeometryHistory", "HistoryMiss"),
                        interpolations = Delta("NetDynamicGeometryHistory", "InterpolationCount"),
                        skipped = Delta("NetDynamicGeometryHistory", "GeometryObjectsSkippedUnchanged") },
                    historicalCollision = new { queries = Delta("NetUnlagged", "HistoricalCollisionQueries"),
                        hits = Delta("NetUnlagged", "HistoricalCollisionCacheHits"), misses = Delta("NetUnlagged", "HistoricalCollisionCacheMisses"),
                        buildTicks = Delta("NetUnlagged", "HistoricalCollisionBuildTicks") },
                    arbitration = new { pending = NetHitClaims.ClaimsPendingCurrent, resolved = NetHitClaims.ResolvedLedgerCurrent,
                        rescued = NetHitClaims.RescuedLedgerCurrent, probes = Delta("NetHitClaims", "RescueLookupProbes") },
                    workload = new { shotsFired = Delta("NetDamage", "Fired"),
                        damageEvents = Delta("NetDamage", "ResolvedSession"), damagePoints = Delta("NetDamage", "DamageByBeam"),
                        claimsReceived = Delta("NetHitClaims", "Received"), claimsApplied = Delta("NetHitClaims", "AppliedHere"),
                        claimsDuplicate = Delta("NetHitClaims", "DuplicateHere"), claimsRefused = Delta("NetHitClaims", "RefusedHere") },
                    unavailable = new[] { "network impairment", "transport lock contention (no transport in this workload)" }
                };
                File.WriteAllText(args[4], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{(sim.StepFailures == errors ? "PASS" : "FAIL")} server performance: {players} players, {steps} steps, mean={durations.Average():F4}ms p99={durations[(int)(steps * .99)]:F4}ms worst={durations[^1]:F4}ms, allocated={allocations}");
                return sim.StepFailures == errors ? 0 : 1;
            }
            finally { sim.Stop(); }
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
