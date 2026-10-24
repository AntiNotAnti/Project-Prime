using System;

namespace MphRead.Mods.Network;

/// <summary>Bounded owner-thread diagnostics. Sampling allocates nothing;
/// sorting occurs only when a report explicitly requests a snapshot.</summary>
internal sealed class ReplayTimingWindow
{
    private readonly double[] _samples = new double[2048];
    private int _next, _count;
    internal long TotalSamples { get; private set; }
    internal void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0) return;
        _samples[_next] = milliseconds; _next = (_next + 1) % _samples.Length;
        _count = Math.Min(_count + 1, _samples.Length); TotalSamples++;
    }
    internal ReplayTimingSummary Snapshot()
    {
        if (_count == 0) return default;
        double[] sorted = _samples.AsSpan(0, _count).ToArray(); Array.Sort(sorted);
        double Percentile(double fraction) => sorted[Math.Clamp((int)Math.Ceiling(_count * fraction) - 1, 0, _count - 1)];
        return new(_count, TotalSamples, Percentile(.5), Percentile(.95), Percentile(.99), sorted[^1]);
    }
}

internal readonly record struct ReplayTimingSummary(int WindowSamples, long TotalSamples,
    double P50Milliseconds, double P95Milliseconds, double P99Milliseconds, double MaximumMilliseconds);
