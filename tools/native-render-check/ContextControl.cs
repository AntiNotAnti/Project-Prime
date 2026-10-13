using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using OpenTK.Windowing.GraphicsLibraryFramework;

// Separate diagnostic protocol. Each existing stress fixture completes before
// disposing its actual window/context; no GL calls occur after disposal.
// Optional GLFW process teardown is final: OpenTK's initialized flag is not reset.
static class ContextControl
{
    internal static int SelfTest()
    {
        var valid = new[] { "-resourcestress", "-resource-arm", "map-gl", "-resource-drain", "-resource-context-cycles", "120,180" };
        var parsed = Parse(valid);
        bool pass = parsed.Cycles.SequenceEqual(new[] { 120, 180 }) && parsed.Samples == 30 && parsed.Interval == 1000;
        var driver = Parse(valid.Concat(new[] { "-resource-context-driver-drain" }).ToArray());
        pass &= driver.DriverDrain;
        foreach (var invalid in new[] { Set(valid, "-resource-context-cycles", "0"), Set(valid, "-resource-context-cycles", "201"), Set(valid, "-resource-arm", "combined"), valid.Where(v => v != "-resource-drain").ToArray(),
            valid.Concat(new[] { "-resource-context-phase-acks", "unused" }).ToArray(),
            valid.Concat(new[] { "-resource-context-driver-drain", "-resource-context-phase-acks" }).ToArray(),
            valid.Concat(new[] { "-resource-context-driver-drain", "-resource-context-phase-acks", " " }).ToArray(),
            valid.Concat(new[] { "-resource-context-driver-drain", "-resource-context-phase-acks", "-next-option" }).ToArray(),
            valid.Concat(new[] { "-resource-context-driver-drain", "-resource-context-phase-acks", "one", "-resource-context-phase-acks", "two" }).ToArray() })
        {
            try { Parse(invalid); pass = false; } catch (ArgumentException) { }
        }
        pass &= (typeof(ContextControl).GetMethod(nameof(Execute), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0;
        string temporary = Path.Combine(Path.GetTempPath(), "context-acks-selftest-" + Guid.NewGuid());
        Directory.CreateDirectory(temporary);
        try
        {
            string fresh = Path.Combine(temporary, "fresh");
            PrepareAcknowledgements(fresh);
            File.WriteAllText(Path.Combine(fresh, "after-glfw-terminate.ack"), "stale");
            foreach (string existing in new[] { fresh, Path.Combine(temporary, "empty"), Path.Combine(temporary, "file") })
            {
                if (existing.EndsWith("empty")) Directory.CreateDirectory(existing);
                if (existing.EndsWith("file")) File.WriteAllText(existing, "owned test file");
                try { PrepareAcknowledgements(existing); pass = false; } catch (IOException) { }
            }
        }
        finally { Directory.Delete(temporary, recursive: true); }
        Console.WriteLine("CONTEXTCONTROL " + (pass ? "PASS" : "FAIL") + " cycleBounds=True isolatedArms=True requiresDrain=True workloadNoInlining=True malformedAndStaleAcksRejected=True");
        return pass ? 0 : 1;
    }

    private static (int[] Cycles, int Samples, int Interval, bool DriverDrain, string? AckDirectory) Parse(string[] args)
    {
        string Value(string name, string fallback) => Array.IndexOf(args, name) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        int[] cycles = Value("-resource-context-cycles", "").Split(',').Select(int.Parse).ToArray();
        int samples = int.Parse(Value("-resource-context-post-samples", "30"));
        int interval = int.Parse(Value("-resource-context-post-ms", "1000"));
        if (!args.Contains("-resourcestress") || Value("-resource-arm", "combined") is not ("map-gl" or "studio")
            || !args.Contains("-resource-drain") || cycles.Length < 1 || cycles.Length > 8 || cycles.Any(c => c < 1 || c > 200))
            throw new ArgumentException("Context controls require drained map-gl|studio stress and 1..8 comma-separated cycle counts of 1..200.");
        ResourceAttribution.ValidateQuiescence(samples, interval);
        bool driverDrain = args.Contains("-resource-context-driver-drain");
        string? ack = null;
        if (args.Contains("-resource-context-phase-acks"))
        {
            int index = Array.IndexOf(args, "-resource-context-phase-acks");
            if (args.Count(v => v == "-resource-context-phase-acks") != 1 || index + 1 >= args.Length
                || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException("Phase acknowledgements require exactly one nonempty directory argument.");
            ack = Path.GetFullPath(args[index + 1]);
        }
        if (ack != null && !driverDrain) throw new ArgumentException("Phase acknowledgements require final driver-drain control.");
        return (cycles, samples, interval, driverDrain, ack);
    }

    internal static int Run(string[] args, string output, Func<string[], CheckWindow> create)
    {
        var (cycles, samples, interval, driverDrain, ackDirectory) = Parse(args);
        if (File.Exists(output) || Directory.Exists(output + ".contexts")) throw new IOException("Context-control output must be fresh.");
        var records = new List<object>();
        string? error = null;
        PrepareAcknowledgements(ackDirectory);
        Directory.CreateDirectory(output + ".contexts");
        WeakReference? lastWindow = null;
        void Observe(int index, string phase, WeakReference weak)
        {
            long started = Stopwatch.GetTimestamp();
            for (int sample = 0; sample < samples; sample++)
            {
                Thread.Sleep(interval);
                records.Add(new { context = index, phase, sample,
                    elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds,
                    windowWrapperWeakAlive = weak.IsAlive, memory = ResourceAttribution.MemorySample() });
            }
        }
        try
        {
            for (int index = 0; index < cycles.Length; index++)
            {
                string trial = Path.Combine(output + ".contexts", $"context-{index:D2}-{cycles[index]}cycles.json");
                string[] options = Set(Set(args, "-output", trial), "-resourcecycles", cycles[index].ToString());
                // Each helper returns before post-disposal observations, avoiding
                // a surviving JIT local reference to the managed window wrapper.
                var weak = Execute(index, options, trial, create, records, ackDirectory);
                lastWindow = weak;
                Observe(index, "post-context-disposal", weak);
                AwaitCapture(ackDirectory, $"context-{index:D2}-after-disposal");
            }
            if (driverDrain)
            {
                // Owner thread, outside callbacks, after ALL windows/context disposal.
                GLFW.PollEvents();
                Console.WriteLine("NATIVERENDER driver-drain phase=after-glfw-poll");
                Observe(cycles.Length - 1, "after-glfw-poll", lastWindow!);
                AwaitCapture(ackDirectory, "after-glfw-poll");
                GLFW.Terminate();
                // Do not use any GLFW/GL/window APIs or create another window below.
                Console.WriteLine("NATIVERENDER driver-drain phase=after-glfw-terminate");
                Observe(cycles.Length - 1, "after-glfw-terminate", lastWindow!);
                AwaitCapture(ackDirectory, "after-glfw-terminate");
            }
        }
        catch (Exception ex) { error = ex.ToString(); }
        var result = new { schema = 1, kind = "resource-context-control", manifest = ResourceAttribution.Manifest(),
            contextCycles = cycles, postDisposalSamples = samples, postDisposalIntervalMs = interval,
            additionalGcRequested = false, driverDrainRequested = driverDrain, captureAcknowledgementsRequested = ackDirectory != null,
            records, complete = error == null, error,
            limitations = "Actual sequential unshared window/context creation and disposal. Existing per-context stress drain/GC preserved. No extra GC after teardown; weak managed wrappers can remain until a later collection. Optional final GLFW.PollEvents/Terminate control is owner-thread only, after all windows are disposed; no GLFW/GL/window calls or recreation afterward. OpenTK's cached initialized flag is not reset, so this is not a reusable runtime policy. Native handles are not enumerated after destruction. External allocator profiling changes memory/CPU; idle stability does not prove repeated-load boundedness. No FPS evidence." };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("NATIVERENDER CONTEXT " + (error == null ? "PASS " : "FAIL ") + output);
        return error == null ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Execute(int index, string[] args, string output, Func<string[], CheckWindow> create, List<object> records, string? ackDirectory)
    {
        records.Add(new { context = index, phase = "before-context-creation", memory = ResourceAttribution.MemorySample() });
        var window = create(args);
        var weak = new WeakReference(window);
        records.Add(new { context = index, phase = "context-created", exists = window.Exists,
            contextCurrent = window.Context.IsCurrent, memory = ResourceAttribution.MemorySample() });
        try
        {
            window.Run();
            File.WriteAllText(output, JsonSerializer.Serialize(window.ResourceStressResult(), new JsonSerializerOptions { WriteIndented = true }) + "\n");
            if (!window.Complete) throw new InvalidOperationException("Per-context workload incomplete: " + output);
            AwaitCapture(ackDirectory, $"context-{index:D2}-before-disposal");
        }
        catch (Exception ex)
        {
            var result = JsonSerializer.SerializeToNode(window.ResourceStressResult())!;
            result["error"] = ex.ToString();
            File.WriteAllText(output, result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            throw;
        }
        finally
        {
            window.ClearResourceFailureCallback();
            window.EndScene();
            records.Add(new { context = index, phase = "before-context-disposal", exists = window.Exists,
                contextCurrent = window.Context.IsCurrent, memory = ResourceAttribution.MemorySample() });
            long started = Stopwatch.GetTimestamp(); double pause = GC.GetTotalPauseDuration().TotalMilliseconds;
            window.Dispose();
            records.Add(new { context = index, phase = "after-context-disposal", exists = window.Exists,
                disposeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                gcPauseDuringDisposeMs = GC.GetTotalPauseDuration().TotalMilliseconds - pause,
                memory = ResourceAttribution.MemorySample() });
            if (window.Exists) throw new InvalidOperationException("Disposed native window still exists.");
            Console.WriteLine($"NATIVERENDER context={index} disposed; post-disposal observations begin");
        }
        return weak;
    }

    private static void AwaitCapture(string? directory, string phase)
    {
        if (directory == null) return;
        long started = Stopwatch.GetTimestamp();
        while (!File.Exists(Path.Combine(directory, phase + ".ack")))
        {
            if (Stopwatch.GetElapsedTime(started).TotalSeconds > 90)
                throw new TimeoutException("External capture acknowledgement missing: " + phase);
            Thread.Sleep(100);
        }
    }

    private static void PrepareAcknowledgements(string? directory)
    {
        if (directory == null) return;
        if (Directory.Exists(directory) || File.Exists(directory))
            throw new IOException("Capture acknowledgement directory must be fresh; refusing stale/manual reuse: " + directory);
        Directory.CreateDirectory(directory);
    }

    private static string[] Set(string[] args, string key, string value)
    {
        var options = args.ToList(); int index = options.IndexOf(key);
        if (index < 0) { options.Add(key); options.Add(value); }
        else if (index + 1 < options.Count) options[index + 1] = value;
        else throw new ArgumentException("Missing value for " + key);
        return options.ToArray();
    }
}
