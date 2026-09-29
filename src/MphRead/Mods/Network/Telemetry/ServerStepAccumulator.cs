using System;

namespace MphRead.Mods.Network.Telemetry;

/// <summary>Simulation-thread-owned, fixed storage. At most five events per 60 steps.
/// Histogram buckets are quarter milliseconds below 7.75ms, then an overflow bucket.
/// Exact count, sum, minimum and maximum are carried independently of quantiles.</summary>
public sealed class ServerStepAccumulator
{
    private readonly long[] _histogram = new long[32];
    public int Count { get; private set; }
    private double _total, _min = double.MaxValue, _max;
    private long _allocations, _maxAllocation, _overruns, _dropped, _stalls;
    private uint _frame;
    public bool Add(uint frame, double milliseconds, long allocations, long dropped, long stalls)
    {
        _frame = frame; Count++; _total += milliseconds; _min = Math.Min(_min, milliseconds); _max = Math.Max(_max, milliseconds);
        _allocations += allocations; _maxAllocation = Math.Max(_maxAllocation, allocations);
        if (milliseconds > 1000.0 / 60) _overruns++;
        _dropped = dropped; _stalls = stalls;
        _histogram[Math.Clamp((int)(milliseconds * 4), 0, 31)]++;
        return Count >= 60;
    }
    public int Flush(Span<NetTelemetryEvent> output)
    {
        if (Count == 0) return 0;
        if (output.Length < 5) throw new ArgumentException("Five event slots required", nameof(output));
        int length = 0;
        output[length++] = new(TelemetryEventType.ServerStepAggregate, _frame, Result: Count,
            A: _total, B: _min, C: _max, D: _allocations, E: _maxAllocation, F: _overruns, G: _dropped, H: _stalls);
        for (int i = 0; i < 32; i += 8)
        {
            long total = 0; for (int j = 0; j < 8; j++) total += _histogram[i + j];
            if (total == 0) continue;
            output[length++] = new(TelemetryEventType.ServerStepHistogram, _frame, Id: (uint)i,
                A: _histogram[i], B: _histogram[i + 1], C: _histogram[i + 2], D: _histogram[i + 3],
                E: _histogram[i + 4], F: _histogram[i + 5], G: _histogram[i + 6], H: _histogram[i + 7]);
        }
        Reset(); return length;
    }
    public void Reset()
    {
        Array.Clear(_histogram); Count = 0; _total = _max = 0; _min = double.MaxValue;
        _allocations = _maxAllocation = _overruns = _dropped = _stalls = 0;
    }
}
