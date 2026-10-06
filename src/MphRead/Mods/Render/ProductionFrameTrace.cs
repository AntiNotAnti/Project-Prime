using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace MphRead.Mods.Render;

/// <summary>Opt-in bounded production timestamps. Disabled runs do no clock,
/// allocation-counter or file work. Storage runs only on shutdown.</summary>
internal static class ProductionFrameTrace
{
    private static readonly string? Output = Environment.GetEnvironmentVariable("PROJECT_PRIME_FRAME_TRACE");
    private static readonly FrameTraceLedger? Ledger = String.IsNullOrWhiteSpace(Output) ? null : new(Stopwatch.Frequency);
    private static readonly object Gate = new();
    private static int _flushed;
    internal static bool Enabled => Ledger != null && !Ledger.Full && Volatile.Read(ref _flushed) == 0;
    static ProductionFrameTrace() { if (Ledger != null) AppDomain.CurrentDomain.ProcessExit += (_, _) => Flush(); }
    internal static long StartOperation() => Enabled && Ledger!.Open ? Stopwatch.GetTimestamp() : 0;
    internal static void Begin() { if (Enabled) Ledger!.Begin(Stopwatch.GetTimestamp()); }
    internal static void Cancel() { if (Enabled) Ledger!.Cancel(); }
    internal static void WaitEnd(long started) { if (started != 0) Ledger!.AddWait(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    internal static void NetworkEnd(long started) { if (started != 0) Ledger!.AddNetwork(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    internal static void AcquireEnd(long started) { if (started != 0) Ledger!.AddAcquire(Stopwatch.GetElapsedTime(started).TotalMilliseconds); }
    internal static void PreparationStart() { if (Enabled) Ledger!.PreparationStart(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()); }
    internal static void PreparationEnd() { if (Enabled) Ledger!.PreparationEnd(Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()); }
    internal static void Mark(LowLatencyMarker marker)
    {
        if (!Enabled) return;
        long ticks = Stopwatch.GetTimestamp();
        switch (marker)
        {
            case LowLatencyMarker.InputSample: Ledger!.Input(ticks); break;
            case LowLatencyMarker.SimulationStart: Ledger!.SimulationStart(ticks, GC.GetAllocatedBytesForCurrentThread()); break;
            case LowLatencyMarker.SimulationEnd: Ledger!.SimulationEnd(ticks, GC.GetAllocatedBytesForCurrentThread()); break;
            case LowLatencyMarker.RenderSubmitStart: Ledger!.RenderStart(ticks, GC.GetAllocatedBytesForCurrentThread()); break;
            case LowLatencyMarker.RenderSubmitEnd: Ledger!.RenderEnd(ticks, GC.GetAllocatedBytesForCurrentThread()); break;
            case LowLatencyMarker.PresentStart: Ledger!.PresentStart(ticks); break;
            case LowLatencyMarker.PresentEnd: lock (Gate) { if (Enabled) Ledger!.Complete(ticks); } break;
        }
    }
    internal static void Flush()
    {
        if (Ledger == null || Interlocked.Exchange(ref _flushed, 1) != 0) return;
        FrameTraceSample[] samples;
        lock (Gate) samples = Ledger.Samples.ToArray();
        try
        {
            string path = Path.GetFullPath(Output!);
            if (File.Exists(path) || File.Exists(path + ".part")) path += "." + Guid.NewGuid().ToString("N") + ".json";
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            double[] intervals = samples.Where(s => s.PresentReturnIntervalMs.HasValue).Select(s => s.PresentReturnIntervalMs!.Value).Order().ToArray();
            double? Percentile(double p) => intervals.Length == 0 ? null : intervals[Math.Clamp((int)Math.Ceiling(intervals.Length * p) - 1, 0, intervals.Length - 1)];
            var document = new { Version = 1, Build = typeof(ProductionFrameTrace).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                Platform = Environment.OSVersion.ToString(), StopwatchFrequency = Stopwatch.Frequency,
                TimingContract = "CPU and driver-return timestamps; not GPU durations or display scanout",
                MaximumFrames = 8192, CapacityReached = Ledger.Full, CancelledFrames = Ledger.CancelledFrames,
                PresentReturnIntervalP50Ms = Percentile(.5), PresentReturnIntervalP95Ms = Percentile(.95), PresentReturnIntervalP99Ms = Percentile(.99), Frames = samples };
            using (var stream = new FileStream(path + ".part", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, document); stream.Flush(flushToDisk: true); }
            File.Move(path + ".part", path);
            Console.WriteLine("[frame-trace] " + path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { Console.WriteLine("[frame-trace] could not save: " + ex.Message); }
    }
}
