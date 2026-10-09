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
            NetCombatProfile.Enabled = Array.IndexOf(args, "--impact-profile") >= 0;
            var sim = new ServerSim();
            if (!sim.Start(room, GameMode.Battle, players, _ => { }, () => { })) return 1;
            try
            {
                const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
                typeof(ServerSimCheck).GetMethod("ApplyRoster", flags)!.Invoke(null, new object[] { players });
                NetSession.ApplyMatchState(new MatchStatePacket { MatchId = 1, AuthorityEpoch = 1,
                    RoomKey = room, NextRoomKey = "", Mode = (byte)GameMode.Battle,
                    Flags = MatchStatePacket.FlagInProgress, PointGoal = 10000, TimeRemaining = 3600 }, false);
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
                NetCombatProfile.Reset();
                var durations = new double[steps];
                long allocations = 0, overruns = 0;
                int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
                long errors = sim.StepFailures;
                for (int i = 0; i < steps; i++)
                {
                    Feed((uint)(301 + i));
                    long bytes = GC.GetAllocatedBytesForCurrentThread(), start = Stopwatch.GetTimestamp();
                    sim.Step();
                    durations[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocations += GC.GetAllocatedBytesForCurrentThread() - bytes;
                    if (durations[i] > 1000.0 / 60) overruns++;
                }
                int gen0 = GC.CollectionCount(0) - g0, gen1 = GC.CollectionCount(1) - g1, gen2 = GC.CollectionCount(2) - g2;
                Array.Sort(durations);
                var assembly = typeof(NetConfig).Assembly;
                object? Metric(string type, string field)
                {
                    var t = assembly.GetType("MphRead.Mods.Network." + type);
                    return t?.GetField(field)?.GetValue(null) ?? t?.GetProperty(field)?.GetValue(null);
                }
                var result = new
                {
                    commit = Environment.GetEnvironmentVariable("PRIME_BENCHMARK_COMMIT") ?? "working-tree",
                    protocol = (int)typeof(NetConfig).GetField(nameof(NetConfig.ProtocolVersion))!.GetRawConstantValue()!,
                    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                    environment = new { machine = Environment.MachineName, cpu = NetworkBenchmark.CpuDescription(), processors = Environment.ProcessorCount,
                        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() },
                    scenario = new { players, room, steps, warmupSteps = 300, kind = "asset-backed synthetic-intent server simulation; no transport" },
                    mean = durations.Average(), p50 = durations[(int)(steps * .5)], p95 = durations[(int)(steps * .95)],
                    p99 = durations[(int)(steps * .99)], p999 = durations[(int)(steps * .999)], worst = durations[^1], unit = "milliseconds",
                    peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64,
                    profileEnabled = NetCombatProfile.Enabled, profile = NetCombatProfile.Capture(),
                    allocations, allocationsPerStep = allocations / (double)steps, gen0, gen1, gen2, overruns,
                    dropped = sim.DroppedSteps, stalls = sim.Stalls, failures = sim.StepFailures - errors,
                    lag = new { compensated = NetUnlagged.ShotsCompensated, rewind = NetUnlagged.FramesRewound,
                        maxRewind = NetUnlagged.WorstRewind, catchupSteps = NetUnlagged.CatchUpSteps,
                        shadowEligible = Metric("NetShadowSampler", "ShadowEligible"), shadowSampled = Metric("NetShadowSampler", "ShadowSampled"),
                        shadowSteps = Metric("NetShadowSampler", "ShadowSimulationSteps"), shadowTicks = Metric("NetShadowSampler", "ShadowSimulationTicks") },
                    geometry = new { considered = Metric("NetDynamicGeometryHistory", "GeometryObjectsConsidered"),
                        applied = NetDynamicGeometryHistory.ObjectsRewound, historyMisses = NetDynamicGeometryHistory.HistoryMiss,
                        interpolations = NetDynamicGeometryHistory.InterpolationCount,
                        skipped = Metric("NetDynamicGeometryHistory", "GeometryObjectsSkippedUnchanged") },
                    historicalCollision = new { queries = Metric("NetUnlagged", "HistoricalCollisionQueries"),
                        hits = Metric("NetUnlagged", "HistoricalCollisionCacheHits"), misses = Metric("NetUnlagged", "HistoricalCollisionCacheMisses"),
                        buildTicks = Metric("NetUnlagged", "HistoricalCollisionBuildTicks") },
                    arbitration = new { pending = NetHitClaims.ClaimsPendingCurrent, resolved = NetHitClaims.ResolvedLedgerCurrent,
                        rescued = NetHitClaims.RescuedLedgerCurrent, probes = Metric("NetHitClaims", "RescueLookupProbes") },
                    unavailable = new[] { "network impairment", "transport lock contention (no transport in this workload)" }
                };
                File.WriteAllText(args[4], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"{(sim.StepFailures == errors ? "PASS" : "FAIL")} server performance: {players} players, {steps} steps, mean={durations.Average():F4}ms p99={durations[(int)(steps * .99)]:F4}ms worst={durations[^1]:F4}ms, allocated={allocations}");
                return sim.StepFailures == errors ? 0 : 1;
            }
            finally { sim.Stop(); NetCombatProfile.Enabled = false; }
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
