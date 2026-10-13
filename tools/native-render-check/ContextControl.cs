using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

// Separate diagnostic protocol. Each existing stress fixture completes before
// disposing its actual window/context; no GL calls occur after disposal.
static class ContextControl
{
    internal static int SelfTest()
    {
        var valid = new[] { "-resourcestress", "-resource-arm", "map-gl", "-resource-drain", "-resource-context-cycles", "120,180" };
        var parsed = Parse(valid);
        bool pass = parsed.Cycles.SequenceEqual(new[] { 120, 180 }) && parsed.Samples == 30 && parsed.Interval == 1000;
        foreach (var invalid in new[] { Set(valid, "-resource-context-cycles", "0"), Set(valid, "-resource-context-cycles", "201"), Set(valid, "-resource-arm", "combined"), valid.Where(v => v != "-resource-drain").ToArray() })
        {
            try { Parse(invalid); pass = false; } catch (ArgumentException) { }
        }
        pass &= (typeof(ContextControl).GetMethod(nameof(Execute), BindingFlags.Static | BindingFlags.NonPublic)!
            .GetMethodImplementationFlags() & MethodImplAttributes.NoInlining) != 0;
        Console.WriteLine("CONTEXTCONTROL " + (pass ? "PASS" : "FAIL") + " cycleBounds=True isolatedArms=True requiresDrain=True workloadNoInlining=True");
        return pass ? 0 : 1;
    }

    private static (int[] Cycles, int Samples, int Interval) Parse(string[] args)
    {
        string Value(string name, string fallback) => Array.IndexOf(args, name) is int i && i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        int[] cycles = Value("-resource-context-cycles", "").Split(',').Select(int.Parse).ToArray();
        int samples = int.Parse(Value("-resource-context-post-samples", "30"));
        int interval = int.Parse(Value("-resource-context-post-ms", "1000"));
        if (!args.Contains("-resourcestress") || Value("-resource-arm", "combined") is not ("map-gl" or "studio")
            || !args.Contains("-resource-drain") || cycles.Length < 1 || cycles.Length > 8 || cycles.Any(c => c < 1 || c > 200))
            throw new ArgumentException("Context controls require drained map-gl|studio stress and 1..8 comma-separated cycle counts of 1..200.");
        ResourceAttribution.ValidateQuiescence(samples, interval);
        return (cycles, samples, interval);
    }

    internal static int Run(string[] args, string output, Func<string[], CheckWindow> create)
    {
        var (cycles, samples, interval) = Parse(args);
        if (File.Exists(output) || Directory.Exists(output + ".contexts")) throw new IOException("Context-control output must be fresh.");
        var records = new List<object>();
        string? error = null;
        Directory.CreateDirectory(output + ".contexts");
        try
        {
            for (int index = 0; index < cycles.Length; index++)
            {
                string trial = Path.Combine(output + ".contexts", $"context-{index:D2}-{cycles[index]}cycles.json");
                string[] options = Set(Set(args, "-output", trial), "-resourcecycles", cycles[index].ToString());
                // Each helper returns before post-disposal observations, avoiding
                // a surviving JIT local reference to the managed window wrapper.
                var weak = Execute(index, options, trial, create, records);
                long started = Stopwatch.GetTimestamp();
                for (int sample = 0; sample < samples; sample++)
                {
                    Thread.Sleep(interval);
                    records.Add(new { context = index, phase = "post-context-disposal", sample,
                        elapsedSeconds = Stopwatch.GetElapsedTime(started).TotalSeconds,
                        windowWrapperWeakAlive = weak.IsAlive, memory = ResourceAttribution.MemorySample() });
                }
            }
        }
        catch (Exception ex) { error = ex.ToString(); }
        var result = new { schema = 1, kind = "resource-context-control", manifest = ResourceAttribution.Manifest(),
            contextCycles = cycles, postDisposalSamples = samples, postDisposalIntervalMs = interval,
            additionalGcRequested = false, records, complete = error == null, error,
            limitations = "Actual sequential unshared window/context creation and disposal. Existing per-context stress drain/GC preserved. No extra GC after teardown; weak managed wrappers can remain until a later collection. Native handles are not enumerated after destruction. Driver allocation capture is external; idle stability does not prove repeated-load boundedness. No FPS evidence." };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine("NATIVERENDER CONTEXT " + (error == null ? "PASS " : "FAIL ") + output);
        return error == null ? 0 : 1;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Execute(int index, string[] args, string output, Func<string[], CheckWindow> create, List<object> records)
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

    private static string[] Set(string[] args, string key, string value)
    {
        var options = args.ToList(); int index = options.IndexOf(key);
        if (index < 0) { options.Add(key); options.Add(value); }
        else if (index + 1 < options.Count) options[index + 1] = value;
        else throw new ArgumentException("Missing value for " + key);
        return options.ToArray();
    }
}
