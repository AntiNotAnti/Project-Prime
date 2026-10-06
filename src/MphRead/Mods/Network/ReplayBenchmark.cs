using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Reproducible simulation measurements. The baseline is the same
/// recording without a durable seek index, not an estimate of an older engine.</summary>
internal static class ReplayBenchmark
{
    private static readonly Vector2i Size = new(256, 192);
    private sealed record Sample(string Operation, string Source, double WallMs, double CpuMs,
        long AllocatedBytes, int Steps, uint RestoreFrame, string Hash);
    private sealed record StagedSample(string Operation, double WallMs, double CpuMs,
        long ProcessAllocatedBytes, double? DetachedPreparationMs, double OwnerCommitMs,
        double MaximumOwnerCallbackMs, double OwnerWorkMs, int OwnerCallbacks,
        int Steps, uint RestoreFrame, string Hash);
    internal static int Run(string source, string output)
    {
        Headless.Enter(); Directory.CreateDirectory(output);
        var samples = new List<Sample>();
        var stagedSamples = new List<StagedSample>();
        string indexed = Path.Combine(output, "indexed.ppdemo"), baseline = Path.Combine(output, "unindexed.ppdemo");
        var timeline = new RollingReplayTimeline();
        ReplayTimelineClip? frozenClip = null;
        bool previousTelemetry = ReplayPerfTelemetry.Enabled;
        ReplayPerfTelemetry.Enabled = true;
        try
        {
            // Upgrade only the local benchmark copy. Both variants start from
            // exactly the same world and accepted packet sequence.
            uint last;
            using (var world = new PassiveReplayScene(source, Size))
            using (var reader = DemoReader.Open(source)!)
            {
                if (!world.Session.HasSimulatedFrame && !world.Step()) throw new InvalidDataException("Empty benchmark source.");
                var metadata = ReplayTimelineArchive.Metadata(world, ReplayType.FullMatch);
                using var withIndex = new ReplayWriterV3(indexed, metadata);
                using var withoutIndex = new ReplayWriterV3(baseline, metadata);
                DemoRecord? pending = reader.ReadNext();
                do
                {
                    uint frame = world.Session.CurrentFrame;
                    while (pending is { } packet && packet.Frame <= frame)
                    {
                        if (packet.Frame > 0)
                        {
                            withIndex.WriteRecord(packet.Frame, packet.Data); withoutIndex.WriteRecord(packet.Frame, packet.Data);
                            timeline.Append(new(packet.Frame + metadata.OriginRecordingFrame, packet.Frame, ReplayFactKind.Snapshot, packet.Data));
                        }
                        pending = reader.ReadNext();
                    }
                    ReplayTimelineArchive.EndFrame(withIndex, frame); ReplayTimelineArchive.EndFrame(withoutIndex, frame);
                    timeline.Append(new(frame + metadata.OriginRecordingFrame, frame, ReplayFactKind.Event, ReadOnlySpan<byte>.Empty));
                    if (frame % 300 == 0)
                    {
                        using var checkpoint = ReplayWorldCheckpoint.Capture(world, world.Session.RecordingFrame);
                        if (frame != 0) withIndex.WriteCheckpoint(frame, checkpoint.Bytes);
                        timeline.AppendRestorePoint(new(checkpoint.Frame, frame, ReplayRestoreKind.ReplicaCheckpoint,
                            [new(checkpoint.Frame, frame, ReplayFactKind.World, checkpoint.Bytes)]));
                    }
                } while (world.Step());
                last = world.Session.CurrentFrame;
            }
            if (last < 18000) throw new InvalidDataException("Benchmark source must contain at least five minutes (18,000 frames).");
            foreach (string file in new[] { baseline, indexed })
            {
                // Warm the JIT and asset cache once, then report three samples.
                using (var warm = new PassiveReplayPlayer(file, Size)) { warm.Seek(301); Complete(warm); }
                for (int trial = 0; trial < 3; trial++)
                {
                    Measure("startup", file, samples, () =>
                    { using var player = new PassiveReplayPlayer(file, Size); return (0, 0u, ReplayStateHash.Compute(player.Current.Scene, player.Current.Session.CurrentFrame)); });
                    foreach (uint target in new uint[] { 1800, 18000 })
                    {
                        using var player = new PassiveReplayPlayer(file, Size);
                        Measure("cold seek " + target, file, samples, () =>
                        {
                            player.Seek(target); Complete(player);
                            return (player.SeekSimulationSteps, player.SeekRestoreFrame, ReplayStateHash.Compute(player.Current.Scene, target));
                        });
                        if (target == 18000)
                            Measure("backward seek 1800", file, samples, () =>
                            {
                                player.Seek(1800); Complete(player);
                                return (player.SeekSimulationSteps, player.SeekRestoreFrame, ReplayStateHash.Compute(player.Current.Scene, 1800));
                            });
                    }
                }
                using var linear = new PassiveReplayPlayer(file, Size);
                Measure("linear 18001 frames", file, samples, () =>
                {
                    int steps = 0;
                    do { steps += linear.Update(); } while (linear.Current.Session.CurrentFrame < 18000);
                    return (steps, 0u, ReplayStateHash.Compute(linear.Current.Scene, 18000));
                });
            }
            foreach (var group in samples.Where(s => s.Operation != "startup").GroupBy(s => s.Operation))
                if (group.Select(s => s.Hash).Distinct().Count() != 1) throw new InvalidDataException("Benchmark paths diverged: " + group.Key);
            for (int trial = 0; trial < 3; trial++)
            {
                PassiveReplayPlayer? staged = null;
                try
                {
                    MeasureStaged("startup", stagedSamples, () =>
                    {
                        var detached = Stopwatch.StartNew();
                        using var job = ReplayPreparationJob.File(indexed);
                        using var prepared = job.WaitCompleted();
                        detached.Stop();
                        var owner = Stopwatch.StartNew();
                        staged = new PassiveReplayPlayer(prepared, Size, new(EnableAsyncPreparation: true));
                        owner.Stop();
                        var callbacks = CompleteStaged(staged);
                        return (staged, (double?)detached.Elapsed.TotalMilliseconds,
                            owner.Elapsed.TotalMilliseconds, callbacks);
                    });
                    foreach (uint target in new uint[] { 1800, 18000, 1800 })
                    {
                        string operation = target == 1800 && staged!.Current.Session.CurrentFrame > target
                            ? "backward seek 1800" : "cold seek " + target;
                        MeasureStaged(operation, stagedSamples, () =>
                        {
                            staged!.Seek(target);
                            return (staged, (double?)null, 0d, CompleteStaged(staged));
                        });
                    }
                }
                finally { staged?.Dispose(); }
            }
            foreach (var staged in stagedSamples)
            {
                var reference = samples.First(s => s.Operation == staged.Operation);
                if (staged.Hash != reference.Hash)
                    throw new InvalidDataException("Staged replay diverged: " + staged.Operation);
            }
            uint end = timeline.LastRecordingFrame!.Value;
            if (!timeline.TryFreeze(end - 120, end, out var clip) || clip == null) throw new InvalidDataException("Benchmark history unavailable.");
            frozenClip = clip;
            long retainedBefore = GC.GetTotalMemory(forceFullCollection: true);
            PassiveReplayPlayer? killcam = null;
            long retainedPlayer;
            try
            {
                Measure("killcam prepare 120 frames", indexed, samples, () =>
                {
                    killcam = new PassiveReplayPlayer(clip, Size); Complete(killcam);
                    return (killcam.SeekSimulationSteps, killcam.SeekRestoreFrame, ReplayStateHash.Compute(killcam.Current.Scene, killcam.Current.Session.CurrentFrame));
                });
                retainedPlayer = Math.Max(0, GC.GetTotalMemory(forceFullCollection: true) - retainedBefore);
            }
            finally { killcam?.Dispose(); }
            var report = new
            {
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Source = Path.GetFileName(source), Frames = last + 1,
                Notes = "Headless simulation, warm OS/asset/JIT caches. Seek baseline is identical v4 facts without a durable checkpoint index. CPU is process CPU; allocations are calling-thread allocations. No rendering/GPU costs included.",
                TimelineBytes = timeline.PayloadBytes, timeline.RecordCount, timeline.RestorePointCount,
                ClipBytes = clip.RestorePoint.PayloadBytes + clip.Records.Sum(r => r.PayloadBytes), KillcamManagedBytes = retainedPlayer,
                IndexedBytes = new FileInfo(indexed).Length, BaselineBytes = new FileInfo(baseline).Length,
                Samples = samples,
                StagedSamples = stagedSamples,
                StagedNotes = "Production detached preparation and owner adoption, followed by staged seeks on the same player. Synchronous cold-seek samples create a fresh player, so these warm staged samples are correctness and owner-work observations, not a controlled speed comparison. Worker waits yield 1 ms in this headless driver. Process-wide allocations include workers and runtime activity; owner callback time excludes the wait. Scene/load/restore and exact-map publication remain monolithic owner operations, so the 1 ms step budget is not a construction deadline.",
                ProcessReplayOperations = ReplayPerfTelemetry.Snapshot()
            };
            File.WriteAllText(Path.Combine(output, "benchmark.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var group in samples.GroupBy(s => (s.Operation, s.Source)))
            {
                var median = group.OrderBy(s => s.WallMs).ElementAt(group.Count() / 2);
                Console.WriteLine($"[replaybench] {median.Source} {median.Operation}: {median.WallMs:F2} ms, {median.CpuMs:F2} CPU ms, {median.AllocatedBytes} B, {median.Steps} steps from {median.RestoreFrame}");
            }
            foreach (var group in stagedSamples.GroupBy(s => s.Operation))
            {
                var median = group.OrderBy(s => s.WallMs).ElementAt(group.Count() / 2);
                Console.WriteLine($"[replaybench] staged {median.Operation}: {median.WallMs:F2} ms, max owner callback {median.MaximumOwnerCallbackMs:F2} ms, {median.ProcessAllocatedBytes} process B, {median.Steps} steps from {median.RestoreFrame}");
            }
            Console.WriteLine($"[replaybench] PASS: identical seek/linear hashes; timeline {timeline.PayloadBytes} B, {timeline.RestorePointCount} restore points; clip {report.ClipBytes} B, private world {retainedPlayer} managed B.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine("[replaybench] FAIL: " + ex); return 1; }
        finally
        {
            try { frozenClip?.Dispose(); }
            finally { timeline.Reset(); ReplayPerfTelemetry.Enabled = previousTelemetry; }
        }
    }
    private static void Complete(PassiveReplayPlayer player)
    { while (!player.Ready) if (player.Update() > 120) throw new InvalidDataException("Seek exceeded its update budget."); }
    private readonly record struct OwnerCallbacks(double MaximumMs, double TotalMs, int Count);
    private static OwnerCallbacks CompleteStaged(PassiveReplayPlayer player)
    {
        double maximum = 0, total = 0;
        int count = 0;
        var timeout = Stopwatch.StartNew();
        while (!player.Ready)
        {
            if (timeout.Elapsed.TotalSeconds > 30) throw new InvalidDataException("Staged replay did not complete.");
            long start = Stopwatch.GetTimestamp();
            int steps = player.Update(maximumSteps: 24, maximumMilliseconds: 1);
            double elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            maximum = Math.Max(maximum, elapsed); total += elapsed; count++;
            if (steps > 24) throw new InvalidDataException("Staged seek exceeded its step budget.");
            if (steps == 0 && !player.Ready) Thread.Sleep(1);
        }
        return new(maximum, total, count);
    }
    private static void MeasureStaged(string operation, List<StagedSample> output,
        Func<(PassiveReplayPlayer Player, double? DetachedMs, double CommitMs, OwnerCallbacks Callbacks)> action)
    {
        using var process = Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime;
        long allocated = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew(); var result = action(); timer.Stop();
        var player = result.Player;
        output.Add(new(operation, timer.Elapsed.TotalMilliseconds,
            (process.TotalProcessorTime - cpu).TotalMilliseconds,
            GC.GetTotalAllocatedBytes(precise: true) - allocated, result.DetachedMs, result.CommitMs,
            Math.Max(result.CommitMs, result.Callbacks.MaximumMs), result.CommitMs + result.Callbacks.TotalMs,
            result.Callbacks.Count + (result.CommitMs > 0 ? 1 : 0), player.SeekSimulationSteps,
            player.SeekRestoreFrame, ReplayStateHash.Compute(player.Current.Scene, player.Current.Session.CurrentFrame)));
    }
    private static void Measure(string operation, string file, List<Sample> output, Func<(int Steps, uint Restore, string Hash)> action)
    {
        using var process = Process.GetCurrentProcess();
        TimeSpan cpu = process.TotalProcessorTime; long allocated = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew(); var result = action(); timer.Stop();
        output.Add(new(operation, Path.GetFileNameWithoutExtension(file), timer.Elapsed.TotalMilliseconds,
            (process.TotalProcessorTime - cpu).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocated,
            result.Steps, result.Restore, result.Hash));
    }
}
