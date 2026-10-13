using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.Input;
using MphRead.Sound;
using OpenTK.Mathematics;

// OpenAL lifecycle isolation: no RenderWindow, room resources or GL context.
static class AudioResourceStress
{
    internal static int Run(string[] args, string output)
    {
        string Value(string key, string fallback) => Array.IndexOf(args, key) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        int cycles = int.Parse(Value("-resourcecycles", "4")), frames = int.Parse(Value("-resourceframes", "120"));
        int samples = int.Parse(Value("-resource-quiescent-samples", "6")), interval = int.Parse(Value("-resource-quiescent-ms", "1000"));
        if (cycles < 1 || cycles > 200 || frames < 30 || frames > 600) throw new ArgumentException("Use cycles 1..200, frames 30..600.");
        ResourceAttribution.ValidateQuiescence(samples, interval);
        bool serial = args.Contains("-resource-audio-wait");
        bool retentionProbe = args.Contains("-resource-retention-probe");
        bool secondCollect = args.Contains("-resource-retention-second-gc");
        if (secondCollect && !retentionProbe) throw new ArgumentException("-resource-retention-second-gc requires -resource-retention-probe.");
        var weakObservations = new List<AudioRetentionProbe.Observation>();
        var records = new List<object>(); var drain = new List<object>(); var tasks = new List<Task>();
        object Audio() => new { observations = tasks.Count, remaining = tasks.Count(t => !t.IsCompleted), faulted = tasks.Count(t => t.IsFaulted), canceled = tasks.Count(t => t.IsCanceled) };
        string? error = null;
        var wall = Stopwatch.StartNew();
        Console.WriteLine($"NATIVERENDER pid={Environment.ProcessId} arm=audio contextCount=0");
        try
        {
            if (retentionProbe)
                AudioRetentionProbe.RunWorkload(cycles, frames, serial, records, drain, tasks, weakObservations, Audio);
            else
            {
                // initializeRuntime:false avoids Music.Init and game asset/resource
                // initialization. Sfx.Load exercises the ordinary audio banks,
                // sources, contexts and feedback warmup (including MiniAudio).
                var scene = new Scene(new Vector2i(320, 180), SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { }, initializeRuntime: false);
                drain.Add(new { phase = "workload-start", memory = ResourceAttribution.MemorySample() });
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    long started = Stopwatch.GetTimestamp();
                    for (int load = 0; load < 2; load++)
                    {
                        Sfx.Load(scene);
                        if (Sfx.Instance is not SfxInstance) throw new InvalidOperationException("Native audio unavailable; stub cannot pass audio lifecycle attribution.");
                        for (int step = 0; step < frames; step++) Sfx.Update(1f / 60);
                        object owned = ResourceAttribution.AudioOwnedSample();
                        Sfx.ShutDown();
                        Task task = ResourceAttribution.ShutdownTask(); tasks.Add(task);
                        if (serial && !task.Wait(TimeSpan.FromSeconds(15))) throw new TimeoutException("Audio shutdown exceeded 15 seconds.");
                        // Match the existing EndScene collection, without modifying
                        // production GC policy. This is part of the recorded fixture.
                        GC.Collect();
                        records.Add(new { cycle, candidate = load == 1, audioUpdateSteps = frames, audioOwnedBefore = owned,
                            elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds, memory = ResourceAttribution.MemorySample(), audio = Audio() });
                    }
                    Console.WriteLine($"NATIVERENDER resource arm=audio cycle={cycle + 1}/{cycles}");
                }
            }
            if (args.Contains("-resource-drain"))
            {
                drain.Add(new { phase = "before-drain", memory = ResourceAttribution.MemorySample(), audio = Audio() });
                if (!Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(15))) throw new TimeoutException("Audio final drain exceeded 15 seconds.");
                drain.Add(new { phase = "after-audio-wait", memory = ResourceAttribution.MemorySample(), audio = Audio() });
                if (retentionProbe)
                    drain.Add(new { phase = "after-workload-helper-return-before-gc", memory = ResourceAttribution.MemorySample(), audio = Audio(), retention = AudioRetentionProbe.Snapshot(weakObservations) });
                ResourceAttribution.FullGcDrain();
                drain.Add(new { phase = "after-full-gc-finalizers", memory = ResourceAttribution.MemorySample(), audio = Audio(), retention = retentionProbe ? AudioRetentionProbe.Snapshot(weakObservations) : null });
                if (secondCollect)
                {
                    // Separate diagnostic phase: objects released by finalizers
                    // can survive the first collection that queued them.
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    drain.Add(new { phase = "after-second-full-gc", memory = ResourceAttribution.MemorySample(), audio = Audio(), retention = AudioRetentionProbe.Snapshot(weakObservations) });
                }
                ResourceAttribution.QuiescentSamples(drain, samples, interval, Audio);
            }
            if (tasks.Any(t => t.IsFaulted || t.IsCanceled)) throw new InvalidOperationException("Audio teardown faulted/canceled.");
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally { Sfx.ShutDown(); }
        var result = new { schema = 2, kind = "audio-resource-stress", arm = "audio", manifest = ResourceAttribution.Manifest(),
            workload = new { cyclesRequested = cycles, loadsPerCycle = 2, audioUpdateSteps = frames, serialShutdown = serial,
                simulationHz = 60, retentionProbe, secondCollect, workloadFrameUnwoundBeforeFinalGc = retentionProbe, roomCount = 0, contextCount = 0, sceneGlInitialized = false, perUnloadGc = true,
                quiescentSamples = samples, quiescentIntervalMs = interval,
                scope = "Sfx.Load/Update/ShutDown and feedback warmup; no room music, playback, renderer, Studio or replay." },
            cyclesCompleted = records.Count / 2, records, drainSamples = drain, audioShutdown = Audio(), wallSeconds = wall.Elapsed.TotalSeconds,
            complete = error == null, error, retention = retentionProbe ? AudioRetentionProbe.Snapshot(weakObservations) : null };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("NATIVERENDER " + (error == null ? "PASS " : "FAIL ") + output);
        return error == null ? 0 : 1;
    }
}
