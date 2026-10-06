using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace MphRead.Mods.Network;

internal enum ReplayPerfOperation { Capture, Step, Checkpoint, AuthorityCapture, AuthorityEncode, Timeline, Enqueue, ArchiveOpen, WorldConstruction, WorldRestore, Lookahead, DecodeChunk, MapValidation, MapPublication }

/// <summary>Atomic operation totals, with owner-only frame attribution. Detached
/// readers cannot contribute to the simulation frame budget. Measurement
/// allocates no formatting, timer or collection objects.</summary>
internal static class ReplayPerfTelemetry
{
    private struct Counter { internal long Count, Ticks, Maximum, Allocated; }
    private static readonly Counter[] Counters = new Counter[Enum.GetValues<ReplayPerfOperation>().Length];
    private static readonly long[] FrameTicks = new long[Enum.GetValues<ReplayPerfOperation>().Length];
    private static int _frameOwnerThread;
    private static readonly int[] Collections = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];
    internal static bool Enabled { get; set; }
    internal static long CheckpointBytes { get; set; }
    private static long _lastSpike;
    private static readonly double[] FrameTimes = new double[3600];
    private static int _frameSamples;
    private static double _maximumFrame;
    private static long _checkpointFrames, _ordinaryFrames;
    private static double _checkpointFrameTotal, _ordinaryFrameTotal;
    internal readonly struct Scope : IDisposable
    {
        private readonly bool _enabled;
        private readonly ReplayPerfOperation _operation;
        private readonly long _start, _allocated;
        private readonly bool _ownerFrame;
        internal Scope(ReplayPerfOperation operation)
        {
            _enabled = Enabled || NetDiagnostics.Enabled; _operation = operation;
            _start = _enabled ? Stopwatch.GetTimestamp() : 0;
            _allocated = _enabled ? GC.GetAllocatedBytesForCurrentThread() : 0;
            _ownerFrame = _enabled && Environment.CurrentManagedThreadId == Volatile.Read(ref _frameOwnerThread);
        }
        public void Dispose()
        {
            if (!_enabled) return;
            long ticks = Stopwatch.GetTimestamp() - _start;
            ref var counter = ref Counters[(int)_operation];
            Interlocked.Add(ref counter.Ticks, ticks);
            Interlocked.Add(ref counter.Allocated, GC.GetAllocatedBytesForCurrentThread() - _allocated);
            long maximum = Volatile.Read(ref counter.Maximum);
            while (ticks > maximum)
            {
                long observed = Interlocked.CompareExchange(ref counter.Maximum, ticks, maximum);
                if (observed == maximum) break;
                maximum = observed;
            }
            Interlocked.Increment(ref counter.Count);
            if (_ownerFrame && Environment.CurrentManagedThreadId == Volatile.Read(ref _frameOwnerThread))
                FrameTicks[(int)_operation] += ticks;
        }
    }
    internal readonly struct FrameScope : IDisposable
    {
        private readonly long _start;
        internal FrameScope(bool enabled) { _start = enabled ? Stopwatch.GetTimestamp() : 0; if (enabled) BeginFrame(); }
        public void Dispose() { if (_start != 0) EndFrame(NetSession.NetFrame, _start); }
    }
    internal static FrameScope Frame() => new(Enabled || NetDiagnostics.Enabled);
    internal static Scope Measure(ReplayPerfOperation operation) => new(operation);
    internal static void BeginFrame()
    {
        if (Enabled || NetDiagnostics.Enabled)
        { Volatile.Write(ref _frameOwnerThread, Environment.CurrentManagedThreadId); Array.Clear(FrameTicks); }
    }
    internal static long OwnerFrameTicks(ReplayPerfOperation operation) => FrameTicks[(int)operation];
    internal static void EndFrame(uint frame, long start)
    {
        if (!Enabled && !NetDiagnostics.Enabled) return;
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        FrameTimes[_frameSamples++ % FrameTimes.Length] = ms;
        _maximumFrame = Math.Max(_maximumFrame, ms);
        if (FrameTicks[(int)ReplayPerfOperation.Checkpoint] > 0) { _checkpointFrames++; _checkpointFrameTotal += ms; }
        else { _ordinaryFrames++; _ordinaryFrameTotal += ms; }
        long now = Stopwatch.GetTimestamp();
        if (ms < 20 || Stopwatch.GetElapsedTime(_lastSpike, now).TotalSeconds < 1) return;
        _lastSpike = now;
        Console.WriteLine($"[frametime] frame={frame} cadence={frame % 300} total={ms:F2}ms replayStep={Milliseconds(FrameTicks[1]):F2}ms checkpoint={Milliseconds(FrameTicks[2]):F2}ms authority={Milliseconds(FrameTicks[3]):F2}ms");
    }
    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    internal readonly record struct OperationStats(string Operation, long Count, double MeanMs, double MaximumMs, long MeanAllocatedBytes);
    internal static OperationStats[] Snapshot()
    {
        var results = new OperationStats[Counters.Length];
        for (int i = 0; i < results.Length; i++)
        {
            ref var c = ref Counters[i];
            long count = Volatile.Read(ref c.Count);
            results[i] = new(((ReplayPerfOperation)i).ToString(), count,
                Milliseconds(Volatile.Read(ref c.Ticks)) / Math.Max(1, count),
                Milliseconds(Volatile.Read(ref c.Maximum)), Volatile.Read(ref c.Allocated) / Math.Max(1, count));
        }
        return results;
    }
    internal static string Summary(RollingReplayTimeline timeline)
    {
        var text = new StringBuilder("[replayperf]");
        foreach (var c in Snapshot())
        {
            text.Append($" {c.Operation}={c.MeanMs:F3}ms max={c.MaximumMs:F3}ms alloc={c.MeanAllocatedBytes}B n={c.Count}");
        }
        text.Append($" checkpoint={CheckpointBytes}B timeline={timeline.PayloadBytes}B facts={timeline.RecordCount} GC={GC.CollectionCount(0) - Collections[0]}/{GC.CollectionCount(1) - Collections[1]}/{GC.CollectionCount(2) - Collections[2]}");
        int samples = Math.Min(_frameSamples, FrameTimes.Length);
        if (samples > 0)
        {
            var sorted = FrameTimes.AsSpan(0, samples).ToArray(); Array.Sort(sorted);
            text.Append($" simulationP50={sorted[(samples - 1) / 2]:F3}ms P95={sorted[(int)((samples - 1) * .95)]:F3}ms P99={sorted[(int)((samples - 1) * .99)]:F3}ms max={_maximumFrame:F3}ms checkpointFrames={_checkpointFrames} mean={_checkpointFrameTotal / Math.Max(1, _checkpointFrames):F3}ms ordinaryMean={_ordinaryFrameTotal / Math.Max(1, _ordinaryFrames):F3}ms");
        }
        return text.ToString();
    }
}
