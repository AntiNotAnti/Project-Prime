using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.CorpsePhysics;

namespace CorpsePilotAndroid;

// Compiled only until the parent's shared PilotBenchmarks.cs exists. This
// establishes a narrow AOT/15-body execution smoke, not benchmark acceptance.
internal static class PilotSmoke
{
    internal static void Run(string output, int steps)
    {
        if (steps is < 1 or > 20) throw new ArgumentOutOfRangeException(nameof(steps));
        int owner = Environment.CurrentManagedThreadId;
        var watch = Stopwatch.StartNew();
        var key = new CorpseKey(new(1, 0, 0, 0, 0, 1, 0), new(1, 0, 0, 0));
        var spawn = new CorpseSpawnDescriptor(key, ReferenceScene.Samus(),
            new(new Vector3(0, .5f, 0), Quaternion.Identity), Vector3.UnitZ,
            Vector3.Zero, null, 1, 0, lifetime: 120);
        var poses = new CorpsePose[CorpseRig.PartCount];
        using var arena = new CorpsePhysicsArena();
        CorpseStatistics live;
        using (var backend = new BepuCorpseBackend(ReferenceScene.Floor(), CorpseQuality.Low, true, arena))
        {
            if (!backend.TrySpawn(spawn, out var handle)) throw new InvalidOperationException("Smoke spawn refused.");
            for (uint tick = 1; tick <= steps; tick++)
            {
                if (!backend.Step(tick) || !backend.Sample(handle, 1, poses))
                    throw new InvalidOperationException("Smoke step/sample refused at " + tick);
                foreach (var pose in poses)
                    if (!pose.IsValid) throw new InvalidOperationException("Smoke pose was not finite/unit.");
            }
            live = backend.Statistics;
            if (live.DynamicBodies != 15 || live.JointConstraints != 42 || live.Faults != 0)
                throw new InvalidOperationException("Smoke did not retain fifteen finite bodies / forty-two constraints.");
        }
        long reusableArenaBytes = arena.ReservedBytes;
        arena.Dispose();
        if (arena.ReservedBytes != 0 || owner != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Arena cleanup or owner-thread check failed.");
        watch.Stop();
        File.WriteAllText(output, JsonSerializer.Serialize(new
        {
            status = "passed", kind = "fallback-smoke-only", steps, ownerThread = owner,
            framework = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            elapsedMilliseconds = watch.Elapsed.TotalMilliseconds, live,
            reusableArenaBytes, disposedArenaBytes = arena.ReservedBytes,
            scope = "Original fifteen-box reference and synthetic static floor. No game, assets, GL, device performance or shipping integration qualification."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
