using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

/// <summary>Disk-backed, per-frame verification of the production private player.
/// The trace bounds memory independently of recording duration.</summary>
internal static class ReplayDeterminism
{
    internal static PassiveReplayPlayer Open(string path)
    {
        var player = new PassiveReplayPlayer(path, new Vector2i(256, 192));
        try
        {
            player.Seek(0);
            while (!player.Ready) player.Update();
            return player;
        }
        catch { player.Dispose(); throw; }
    }
    internal static byte[] Projection(Scene scene, uint frame)
        => Convert.FromHexString(ReplayStateHash.Compute(scene, frame) + scene.ReplayPresentationHash(frame));

    public static int Run(string path, string? hashOutput = null, string? reportOutput = null)
    {
        Headless.Enter();
        string tracePath = Path.Combine(Path.GetTempPath(), "prime-replay-trace-" + Guid.NewGuid().ToString("N"));
        string? sourceHash = null;
        var seekUpdates = new ReplayTimingWindow();
        var seeks = new ReplayTimingWindow();
        int exportSamples = 0;
        void Report(string status, string? error = null)
        {
            if (reportOutput == null) return;
            if (!string.Equals(Path.GetExtension(reportOutput), ".json", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Replay parity report must use a .json path.");
            string output = Path.GetFullPath(reportOutput);
            if (ReplayPathComparer.Same(output, path) || hashOutput != null && ReplayPathComparer.Same(output, hashOutput))
                throw new ArgumentException("Replay parity report must be separate from source and archive output.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Schema = 1, Status = status, Error = error, SourceSha256 = sourceHash,
                Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                Platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                InteractiveBudgetMilliseconds = PassiveReplayPlayer.InteractiveBudgetMilliseconds,
                SeekUpdates = seekUpdates.Snapshot(), Seeks = seeks.Snapshot(), ExportStateSamples = exportSamples,
                ImageParity = "NOT_RUN", AudioParity = "NOT_RUN", PhysicalDisplayPacing = "NOT_RUN",
                Scope = "Canonical gameplay and reconstructed presentation hashes; synthetic export sample clocks do not establish rendered output or captured-source image/audio parity."
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        void Settle(PassiveReplayPlayer player)
        {
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            while (!player.Ready)
            {
                player.Update(); seekUpdates.Add(player.LastUpdateMilliseconds);
                if (elapsed.Elapsed.TotalMinutes > 5) throw new TimeoutException("Replay seek failed to settle within five minutes.");
            }
            seeks.Add(elapsed.Elapsed.TotalMilliseconds);
        }
        try
        {
            using (var source = File.OpenRead(path)) sourceHash = Convert.ToHexString(SHA256.HashData(source));
            using var trace = new FileStream(tracePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                64 * 1024, FileOptions.DeleteOnClose);
            var expected = new List<ReplayExpectedHash>();
            uint duration;
            using (var linear = Open(path))
            {
                duration = linear.Current.Session.LastFrame;
                void Capture(Scene scene)
                {
                    uint frame = linear.Current.Session.CurrentFrame;
                    RequireCursor(scene, frame);
                    if (trace.Position != frame * 64L) throw new InvalidDataException($"Missing/duplicate frame {frame}.");
                    byte[] projection = Projection(scene, frame); trace.Write(projection);
                    if (frame % 300 == 0 || frame == duration) expected.Add(new(frame, Convert.ToHexString(projection.AsSpan(0, 32))));
                }
                Capture(linear.Current.Scene); linear.Stepped += Capture;
                linear.Transport.Play();
                while (!linear.Current.Session.AtEnd) linear.Update();
            }
            trace.Flush();
            void Compare(PassiveReplayPlayer actualPlayer, uint frame)
            {
                Scene scene = actualPlayer.Current.Scene;
                RequireCursor(scene, frame);
                trace.Position = frame * 64L;
                Span<byte> reference = stackalloc byte[64]; trace.ReadExactly(reference);
                byte[] actual = Projection(scene, frame);
                if (!actual.AsSpan().SequenceEqual(reference))
                {
                    string detail;
                    try
                    {
                        using var referenceWorld = new PassiveReplayScene(path, new Vector2i(256, 192));
                        while (referenceWorld.Session.IsWarming || referenceWorld.Session.CurrentFrame < frame)
                            if (!referenceWorld.Step()) throw new InvalidDataException("Diagnostic linear reference ended early.");
                        if (referenceWorld.Session.CurrentFrame != frame)
                            throw new InvalidDataException("Diagnostic linear reference missed the visible frame.");
                        RequireCursor(referenceWorld.Scene, frame);
                        if (!Projection(referenceWorld.Scene, frame).AsSpan().SequenceEqual(reference))
                            throw new InvalidDataException("Diagnostic linear reference differs from the original trace.");
                        detail = ReplayReplicaCheck.DescribeDifferences(referenceWorld, actualPlayer.Current);
                    }
                    catch (Exception ex) { detail = $"Diagnostic failed: {ex.GetType().Name}: {ex.Message}"; }
                    throw new InvalidOperationException($"First gameplay/presentation divergence at frame {frame}. "
                        + $"gameplay equal={actual.AsSpan(0, 32).SequenceEqual(reference[..32])}, "
                        + $"presentation equal={actual.AsSpan(32).SequenceEqual(reference[32..])}. "
                        + $"source={actualPlayer.CheckpointSource}, restore={actualPlayer.SeekRestoreFrame}, "
                        + $"steps={actualPlayer.SeekSimulationSteps}, rejected={actualPlayer.RejectedCheckpoints}, "
                        + $"checkpointError={actualPlayer.LastCheckpointError}; {detail}");
                }
            }
            var random = new Random(2718);
            uint[] targets = new[] { 0u, Math.Min(60u, duration), Math.Min(300u, duration), duration / 3, duration * 2 / 3, duration }
                .Concat(Enumerable.Range(0, 8).Select(_ => (uint)random.NextInt64(duration + 1L))).Distinct().ToArray();
            foreach (uint target in targets)
            {
                using var player = Open(path);
                Compare(player, 0);
                player.Stepped += scene => Compare(player, player.Current.Session.CurrentFrame);
                player.Seek(target);
                Settle(player);
                Compare(player, target);
            }
            // Exercise already populated checkpoint caches, including backward seeks.
            using (var cached = Open(path))
            {
                cached.Seek(duration);
                Settle(cached);
                foreach (uint target in targets.Reverse())
                {
                    cached.Seek(target);
                    Settle(cached);
                    Compare(cached, target);
                }
                // New UI requests abandon earlier targets, including a rebuild
                // whose immutable checkpoint read may still be outstanding.
                foreach (uint target in targets)
                {
                    cached.Seek(duration); cached.Update(1, 0);
                    cached.Seek(duration / 2); cached.Seek(target);
                    Settle(cached); Compare(cached, target);
                }
            }
            foreach (int fps in new[] { 30, 60, 120, 240 })
            {
                using var sampled = Open(path);
                uint first = duration / 2, last = Math.Min(duration, first + 60);
                var sampler = new ReplayExportSampler(first, last, fps);
                double previous = -1;
                for (long index = 0; index < sampler.Count; index++)
                {
                    var sample = sampler.At(index);
                    if (sample.Frame <= previous || sample.SimulationFrame > last)
                        throw new InvalidDataException("Export samples are duplicated or outside the range.");
                    sampled.Seek(sample.SimulationFrame); Settle(sampled);
                    Compare(sampled, sample.SimulationFrame); previous = sample.Frame; exportSamples++;
                }
            }
            foreach (float rate in new[] { .25f, .5f, 1f, 2f, 4f })
            {
                using var player = Open(path);
                player.Stepped += scene => Compare(player, player.Current.Session.CurrentFrame);
                player.Transport.SetPlaybackRate(rate); player.Transport.Play();
                while (!player.Current.Session.AtEnd) player.Update();
                Compare(player, duration);
                byte[] before = Projection(player.Current.Scene, duration);
                for (int i = 0; i < 20; i++) player.Update();
                if (!Projection(player.Current.Scene, duration).AsSpan().SequenceEqual(before))
                    throw new InvalidDataException("Frozen EOF advanced.");
            }
            // The checker must detect and identify a corrupt reference frame.
            using (var probe = Open(path))
            {
                trace.Position = 0; int original = trace.ReadByte(); trace.Position = 0; trace.WriteByte((byte)(original ^ 1));
                bool detected = false;
                try { Compare(probe, 0); }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith(
                    "First gameplay/presentation divergence at frame 0.", StringComparison.Ordinal)) { detected = true; }
                trace.Position = 0; trace.WriteByte((byte)original);
                if (!detected) throw new InvalidDataException("The checker accepted a damaged reference hash.");
            }
            if (hashOutput != null)
            {
                var result = ReplayArchive.WithExpectedHashes(path, hashOutput, expected);
                if (result != ReplayOpenResult.Success) throw new IOException($"Could not write expected hashes: {result}.");
            }
            using (var source = File.OpenRead(path))
                if (sourceHash != Convert.ToHexString(SHA256.HashData(source))) throw new IOException("Source replay changed during parity verification.");
            Report("PASS");
            Console.WriteLine($"[replaydeterminism] PASS: {duration + 1} gameplay/presentation frames, {targets.Length} cold/cached/coalesced seeks, all five rates, {exportSamples} state samples at 30/60/120/240 FPS, frozen EOF and divergence detection. Image/audio parity not run.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("[replaydeterminism] FAIL: " + ex);
            try { Report("FAIL", ex.Message); } catch (Exception reportError) { Console.WriteLine("[replaydeterminism] Could not write report: " + reportError.Message); }
            return 1;
        }
    }
    private static void RequireCursor(Scene scene, uint frame)
    {
        if (scene.ReplayPoses is { } poses && !poses.Prepare())
            throw new InvalidDataException($"Replay presentation cursor failed at frame {frame}: {poses.LastError}");
    }

}
