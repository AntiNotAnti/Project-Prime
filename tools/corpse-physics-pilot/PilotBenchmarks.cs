using System.Diagnostics;
using System.Numerics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.CorpsePhysics;

namespace CorpsePilot;

// Shared desktop/Android harness. Hz is a scheduled interpolation workload,
// not achieved display refresh. All backend calls stay on this caller thread.
public static class PilotBenchmarks
{
    private static readonly CorpseRig Rig = ReferenceScene.Samus();
    public static CorpseSpawnDescriptor Spawn(int index, uint tick, uint cohort = 1)
        => new(new(new(1, 0, 0, (byte)index, 0, cohort, 0), new(cohort, (ulong)index + 1, 0, 0)), Rig,
            new(new((index % 4 - 1.5f) * 2, 0, (index / 4 - .5f) * 3), Quaternion.Identity),
            new(.2f, 0, .1f), Vector3.Zero, null, (uint)index, tick, 240);
    private static IRagdollPresentationBackend Backend(int kind, CorpsePhysicsArena? arena = null)
        => kind == 2 ? new BepuCorpseBackend(ReferenceScene.Floor(true), CorpseQuality.High, true, arena)
            : new ProceduralDeathFallback(CorpseQuality.High, kind == 1);
    private static double Percentile(double[] samples, double p)
    { var sorted = (double[])samples.Clone(); Array.Sort(sorted); return sorted[(int)Math.Clamp(Math.Ceiling(sorted.Length * p) - 1, 0, sorted.Length - 1)]; }
    private static object Times(double[] values) => new { p50Us = Percentile(values, .5), p95Us = Percentile(values, .95), p99Us = Percentile(values, .99), maxUs = values.Max() };
    private static double Elapsed(long before) => (Stopwatch.GetTimestamp() - before) * 1_000_000.0 / Stopwatch.Frequency;
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Run(string output, int steps = 600)
    {
        if (steps is < 240 or > 3600) throw new ArgumentOutOfRangeException(nameof(steps));
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        foreach (int kind in new[] { 0, 1, 2 }) foreach (int count in new[] { 0, 1, 2, 4, 8 }) foreach (int hz in new[] { 60, 120, 240 })
        {
            using var backend = Backend(kind);
            var handles = new CorpseHandle[8]; var poses = new CorpsePose[15];
            var times = new double[steps]; var renders = new double[steps];
            var spawnTimes = new List<double>(); var retireTimes = new List<double>();
            backend.Step(0); uint cohort = 1, tick = 0;
            void Populate()
            {
                for (int i = 0; i < count; i++)
                {
                    long start = Stopwatch.GetTimestamp(); bool accepted = backend.TrySpawn(Spawn(i, tick, cohort), out handles[i]);
                    spawnTimes.Add(Elapsed(start)); Require(accepted, "Benchmark spawn rejected.");
                }
            }
            void Retire()
            {
                for (int i = 0; i < count; i++)
                { long start = Stopwatch.GetTimestamp(); Require(backend.Retire(handles[i]), "Benchmark retirement failed."); retireTimes.Add(Elapsed(start)); }
            }
            Populate();
            // Warm solver, interpolation, descriptor, retire and spawn paths.
            for (int warm = 0; warm < 180; warm++)
            { backend.Step(++tick); for (int i = 0; i < count; i++) backend.Sample(handles[i], .5f, poses); }
            Retire(); ++cohort; Populate();
            long allocationBefore = GC.GetAllocatedBytesForCurrentThread(), hotBytes = 0; long peakPool = 0; int peakConstraints = 0;
            for (int n = 0; n < steps; n++)
            {
                if (n > 0 && n % 180 == 0) { Retire(); ++cohort; Populate(); }
                long hotBefore = GC.GetAllocatedBytesForCurrentThread();
                long start = Stopwatch.GetTimestamp(); Require(backend.Step(++tick), "Fixed step failed."); times[n] = Elapsed(start);
                start = Stopwatch.GetTimestamp();
                for (int frame = 0; frame < hz / 60; frame++) for (int i = 0; i < count; i++)
                    Require(backend.Sample(handles[i], (frame + .5f) / (hz / 60), poses), "Interpolation failed.");
                renders[n] = Elapsed(start); hotBytes += GC.GetAllocatedBytesForCurrentThread() - hotBefore;
                var stats = backend.Statistics; peakPool = Math.Max(peakPool, stats.ReservedPoolBytes);
                if (backend is BepuCorpseBackend physics) peakConstraints = Math.Max(peakConstraints, physics.SolverConstraints);
                Require(stats.ActiveCorpses == count && stats.Faults == 0, "Unexpected fallback or retirement in benchmark.");
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
            Retire(); Require(backend.Statistics.DynamicBodies == 0 && (backend is not BepuCorpseBackend bp || bp.SolverConstraints == 0), "Bodies/constraints leaked.");
            rows.Add(new { backend = backend.Name, count, scheduledRenderHz = hz, fixedSteps = steps, step = Times(times), interpolationPerFixedTick = Times(renders),
                spawn = spawnTimes.Count == 0 ? null : Times(spawnTimes.ToArray()), retire = retireTimes.Count == 0 ? null : Times(retireTimes.ToArray()),
                totalManagedBytesIncludingRespawn = allocated, warmedStepSampleManagedBytes = hotBytes, peakReservedPoolBytes = peakPool, peakConstraints });
        }
        var lifecycle = Lifecycle();
        var result = new { schema = 1, experimental = true, gameIntegration = false, utc = DateTime.UtcNow,
            runtime = RuntimeInformation.FrameworkDescription, os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            cpuCount = Environment.ProcessorCount, vectorAccelerated = Vector.IsHardwareAccelerated, vectorFloatWidth = Vector<float>.Count,
            serverGc = GCSettings.IsServerGC, stopwatchFrequency = Stopwatch.Frequency, rows, lifecycle,
            limitations = "CPU proxy workload only; no display frame/thermal/GPU/game palette or physical Android qualification. Pool numbers are reserved blocks, not an outstanding ownership counter. Budgets are post-step fail-stop tripwires." };
        File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static object Lifecycle()
    {
        using var arena = new CorpsePhysicsArena(4096);
        var samples = new long[128]; var poses = new CorpsePose[15];
        for (int cycle = 0; cycle < samples.Length; cycle++)
        {
            using (var world = new BepuCorpseBackend(ReferenceScene.Floor(true), CorpseQuality.High, true, arena))
            {
                for (int i = 0; i < 8; i++) Require(world.TrySpawn(Spawn(i, 0), out _), "Lifecycle spawn.");
                for (uint tick = 1; tick <= 120; tick++) Require(world.Step(tick), "Lifecycle fixed step.");
                world.Clear(); Require(world.Statistics.ActiveCorpses == 0 && world.Statistics.DynamicBodies == 0 && world.SolverConstraints == 0, "Lifecycle cleanup.");
            }
            samples[cycle] = arena.ReservedBytes;
        }
        // Reuse the same pool without Clear across world rebuilds. Exact
        // ownership follows the mesh/body disposal path; plateau is a stress signal.
        long high = samples.Max(); bool plateau = samples.Skip(16).All(n => n == samples[16]);
        Require(plateau, "Native reserved pool grew after warm lifecycle.");
        arena.Dispose(); Require(arena.ReservedBytes == 0, "Final pool not released.");
        return new { worlds = samples.Length, corpses = samples.Length * 8, stepsPerWorld = 120, reservedBytes = samples, peakReservedPoolBytes = high, plateauAfter16 = plateau, finalReservedBytes = arena.ReservedBytes };
    }
}
