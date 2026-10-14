using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

// Fixed genuine replay state. CPU means current native owner thread CPU;
// wall includes driver submission/backpressure and explicit GL.Finish waiting.
// Neither is a GPU timer, presented FPS or live-game scheduling measurement.
internal static class ReplayRenderBenchmark
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec { internal long Seconds, Nanoseconds; }
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "clock_gettime", SetLastError = true)]
    private static extern int MacClock(int clock, out Timespec time);
    [DllImport("libc", EntryPoint = "clock_gettime", SetLastError = true)]
    private static extern int LinuxClock(int clock, out Timespec time);
    private static long OwnerCpu()
    {
        Timespec time;
        int result = OperatingSystem.IsMacOS() ? MacClock(16, out time)
            : OperatingSystem.IsLinux() ? LinuxClock(3, out time)
            : throw new PlatformNotSupportedException("Native owner CPU benchmark supports macOS/Linux thread clocks only.");
        if (result != 0) throw new InvalidOperationException("Owner CLOCK_THREAD_CPUTIME_ID failed: " + Marshal.GetLastPInvokeError());
        return checked(time.Seconds * 1_000_000_000L + time.Nanoseconds);
    }
    private static long CompatibilitySubmissions(Scene scene)
        => Convert.ToInt64(Reflection.Get(scene, "RetainedCompatibilityWorldDraws"));
    private static object Counters(Scene scene, long before)
    {
        object world = Reflection.Get(scene, "_retainedRenderWorld")!;
        return new { packets = Reflection.Int(scene, "RetainedRenderPacketCount"),
            batches = Reflection.Int(scene, "RetainedRenderBatchCount"),
            preparedGraphStateApplications = Reflection.Int(scene, "RetainedGraphStateApplicationCount"),
            preparedGraphStateReuses = Reflection.Int(scene, "RetainedGraphStateReuseCount"),
            opaqueSortRuns = Reflection.Int(scene, "RetainedOpaqueSortRunCount"),
            opaqueReorderedPackets = Reflection.Int(scene, "RetainedOpaqueReorderedPacketCount"),
            compatibilityPacketSubmissions = CompatibilitySubmissions(scene) - before,
            opaqueBatches = ((System.Collections.ICollection)Reflection.Get(world, "_opaqueBatches")!).Count,
            countScope = "Actual prepared graph batches/state groups and executed compatibility packet submissions; hardware GL draw/state counts NOT_RUN with profiler disabled." };
    }
    internal static void Run(string path, uint target, string comparisonPath, Acceptance.Report report)
    {
        if (report.OriginalOpaqueOrderCounterfactual)
            throw new ArgumentException("Render benchmark requires the candidate's unmodified default submission path.");
        if (Reflection.Bool(Reflection.Type("Mods.Render.OpenGlSubmissionProfiler"), "Enabled")
            || Reflection.Bool(Reflection.Type("Mods.Render.OpenGlFrameProfiler"), "Enabled"))
            throw new InvalidOperationException("Unprofiled render benchmark rejects enabled GL profilers.");
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) throw new FileNotFoundException("Genuine benchmark recording required.", path);
        object? replay = null;
        try
        {
            replay = Reflection.Create("PassiveReplayScene", path, new Vector2i(640, 480));
            Scene scene = (Scene)Reflection.Get(replay, "Scene")!;
            object session = Reflection.Get(replay, "Session")!;
            int steps = 0;
            while (Convert.ToUInt32(Reflection.Get(session, "CurrentFrame")) < target)
                if (++steps > 60001 || !(bool)Reflection.Call(replay, "Step")!)
                    throw new InvalidOperationException("Genuine recording does not reach bounded benchmark frame.");
            if (Convert.ToUInt32(Reflection.Get(session, "CurrentFrame")) != target || !scene.Services.IsReplica)
                throw new InvalidOperationException("Benchmark must hold the exact target private replica frame.");
            string Gameplay() => (string)Reflection.Static("ReplayStateHash", "Compute", scene, target)!;
            string gameplay = Gameplay();
            report.Stages.Add(new { stage = "native-private-render-benchmark-input", path,
                sourceSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                producerBuild = Reflection.Get(Reflection.Get(session, "Metadata")!, "BuildId"),
                consumerClient = report.ClientSha256, consumerMvid = report.ClientMvid,
                producerConsumerCompatibility = "Genuine recording accepted by actual native private decoder; producer need not equal consumer.",
                target, recordingFrame = Reflection.Get(session, "RecordingFrame"), sceneFrame = scene.FrameCount,
                room = scene.Room!.Meta.Name, gameplay, size = scene.RenderSize.ToString(),
                vendor = GL.GetString(StringName.Vendor), renderer = GL.GetString(StringName.Renderer),
                version = GL.GetString(StringName.Version), managedOwnerThread = Environment.CurrentManagedThreadId,
                ownerCpuClock = OperatingSystem.IsMacOS() ? "clock_gettime CLOCK_THREAD_CPUTIME_ID=16 (Darwin SDK _time.h)" : "clock_gettime CLOCK_THREAD_CPUTIME_ID=3 (Linux)",
                profiled = false, warmups = 8, measured = 31, fps = "NOT_MEASURED", gpuTimer = "NOT_MEASURED" });
            _ = OwnerCpu();
            for (int i = 0; i < 8; i++)
            {
                scene.OnDrawFrame(); if (!scene.OnRenderFrame()) throw new InvalidOperationException("Native warmup render stopped.");
                GL.Finish();
            }
            if (Gameplay() != gameplay) throw new InvalidOperationException("Warmup draws changed fixed native gameplay state.");
            var reference = ReplayProbe.Capture(scene, "benchmark-reference", report, draw: false);
            ReplayReadbackDiagnostics.Save(replay, "benchmark-before-samples", report);
            var walls = new List<double>(31); var cpus = new List<double>(31);
            for (int i = 0; i < 31; i++)
            {
                long submissions = CompatibilitySubmissions(scene);
                long cpu0 = OwnerCpu(), start = Stopwatch.GetTimestamp();
                scene.OnDrawFrame();
                if (!scene.OnRenderFrame()) throw new InvalidOperationException("Native measured render stopped.");
                long submitted = Stopwatch.GetTimestamp(), cpu1 = OwnerCpu();
                GL.Finish();
                long end = Stopwatch.GetTimestamp(), cpu2 = OwnerCpu();
                double wall = (end - start) * 1000.0 / Stopwatch.Frequency;
                double cpu = (cpu2 - cpu0) / 1_000_000.0;
                if (wall < 0 || cpu < 0 || GL.GetError() != ErrorCode.NoError)
                    throw new InvalidOperationException("Native benchmark clock or GL failure.");
                walls.Add(wall); cpus.Add(cpu);
                report.Stages.Add(new { stage = "native-private-render-unprofiled-sample", sample = i,
                    ownerCpuMs = cpu, ownerSubmissionCpuMs = (cpu1 - cpu0) / 1_000_000.0,
                    ownerFinishCpuMs = (cpu2 - cpu1) / 1_000_000.0,
                    submissionWallMs = (submitted - start) * 1000.0 / Stopwatch.Frequency,
                    finishWallMs = (end - submitted) * 1000.0 / Stopwatch.Frequency,
                    totalWallMs = wall, counters = Counters(scene, submissions) });
            }
            if (Gameplay() != gameplay) throw new InvalidOperationException("Measured draws changed fixed native gameplay state.");
            ReplayReadbackDiagnostics.Save(replay, "benchmark-after-samples", report);
            var after = ReplayProbe.Capture(scene, "benchmark-after", report, draw: false);
            if (after != reference) throw new InvalidOperationException("Fixed native render benchmark changed exact color/HUD/depth readback.");
            byte[] beforeState = File.ReadAllBytes(Path.Combine(report.Output, "benchmark-before-samples-native-checkpoint.bin"));
            byte[] afterState = File.ReadAllBytes(Path.Combine(report.Output, "benchmark-after-samples-native-checkpoint.bin"));
            if (!beforeState.AsSpan().SequenceEqual(afterState))
                throw new InvalidOperationException("Measured repeated draws changed exact native checkpoint state.");
            object Distribution(List<double> samples)
            {
                var sorted = samples.Order().ToArray();
                return new { count = sorted.Length, minimum = sorted[0], median = sorted[sorted.Length / 2],
                    p95 = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1], p99 = sorted[^1], p999 = sorted[^1],
                    maximum = sorted[^1], mean = samples.Average(),
                    rankScope = "31 samples: nearest-rank p95 is rank30; p99 and p99.9 are rank31/max, not robust rare-tail estimates." };
            }
            report.Stages.Add(new { stage = "native-private-render-benchmark-summary", target,
                ownerCpuMs = Distribution(cpus), synchronizedWallMs = Distribution(walls),
                exactGameplay = true, exactNativeCheckpoint = true, exactSameStateReadback = true,
                referenceHashes = reference, afterHashes = after,
                hardwareDrawCounts = "NOT_RUN: measured path intentionally unprofiled.",
                comparison = "Compare raw old-sorted/fixed-original-order costs; expected old color differs by known causal fix, never waive fixed order fidelity." });
            if (comparisonPath.Length != 0) Compare(comparisonPath, path, target, reference, report);
            else report.Missing.Add("NOT_RUN: no prior-arm receipt supplied for cross-arm exact world/HUD/depth comparison.");
            report.Missing.Add("BENCHMARK_ONLY: fixed genuine private render cost does not establish presented FPS, GPU execution time or full A3 acceptance.");
        }
        finally { (replay as IDisposable)?.Dispose(); }
    }
    private static void Compare(string receiptPath, string recording, uint target,
        ReplayProbe.CaptureHashes current, Acceptance.Report report)
    {
        receiptPath = Path.GetFullPath(receiptPath);
        using var receipt = JsonDocument.Parse(File.ReadAllBytes(receiptPath));
        JsonElement root = receipt.RootElement;
        if (root.TryGetProperty("Error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new InvalidDataException("Prior benchmark arm failed; it cannot establish a valid cost comparison.");
        if (root.GetProperty("ClientSha256").GetString() != root.GetProperty("ClientHashAfter").GetString())
            throw new InvalidDataException("Prior benchmark arm client identity changed.");
        JsonElement Stage(string name) => root.GetProperty("Stages").EnumerateArray()
            .Single(stage => stage.GetProperty("stage").GetString() == name);
        var input = Stage("native-private-render-benchmark-input");
        var summary = Stage("native-private-render-benchmark-summary");
        if (input.GetProperty("target").GetUInt32() != target
            || input.GetProperty("sourceSha256").GetString() != Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(recording))))
            throw new InvalidDataException("Cross-arm comparison requires the same genuine recording and target frame.");
        var prior = summary.GetProperty("referenceHashes");
        string world = prior.GetProperty("World").GetString()!, hud = prior.GetProperty("Hud").GetString()!, depth = prior.GetProperty("Depth").GetString()!;
        report.Stages.Add(new { stage = "native-private-render-cross-arm-comparison", receiptPath,
            receiptSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(receiptPath))),
            priorConsumer = root.GetProperty("ClientSha256").GetString(), currentConsumer = report.ClientSha256,
            target, priorHashes = new { World = world, Hud = hud, Depth = depth }, currentHashes = current,
            worldExact = world == current.World, hudExact = hud == current.Hud, depthExact = depth == current.Depth,
            priorOwnerCpuMs = summary.GetProperty("ownerCpuMs").Clone(),
            priorSynchronizedWallMs = summary.GetProperty("synchronizedWallMs").Clone(),
            interpretation = "Raw cross-arm differences retained. Known old sorted pixel is evidence of the repair; cross-arm equality is not inferred from within-arm stability or enforced with tolerance." });
    }
}
