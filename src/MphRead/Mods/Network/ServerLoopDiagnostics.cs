using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods.Network;

public enum ServerLoopPhase { Ingress, MapAndControl, Simulation, PostIngress, Maintenance }

public readonly record struct ServerLoopSample(long Count, double MeanMilliseconds,
    double P95Milliseconds, double P99Milliseconds, double P999Milliseconds,
    double WorstMilliseconds, long[] Histogram);

public readonly record struct ServerLoopSnapshot(ServerLoopSample Work,
    ServerLoopSample[] Phases, long OverBudget);

/// <summary>Optional loop-body measurements, including packet handlers and map,
/// replay, match and directory maintenance. Deliberate pacing wait is excluded.
/// Histograms are bounded; disabled sampling does not read the clock or allocate.</summary>
public sealed class ServerLoopDiagnostics
{
    private const int Buckets = 20001; // 0.01 ms bins, overflow at 200 ms.
    private sealed class Samples
    {
        internal readonly long[] Histogram = new long[Buckets];
        internal long Count, Ticks, Worst;
        internal void Add(long ticks)
        {
            Interlocked.Increment(ref Histogram[Math.Min(Buckets - 1,
                (int)Math.Min(Int32.MaxValue, ticks * 100000d / Stopwatch.Frequency))]);
            Interlocked.Add(ref Ticks, ticks);
            long prior;
            do { prior = Interlocked.Read(ref Worst); if (ticks <= prior) break; }
            while (Interlocked.CompareExchange(ref Worst, ticks, prior) != prior);
            Interlocked.Increment(ref Count);
        }
        internal ServerLoopSample Capture(bool includeHistogram)
        {
            long[] histogram = includeHistogram ? new long[Buckets] : Array.Empty<long>();
            long count = includeHistogram ? 0 : Interlocked.Read(ref Count);
            for (int i = 0; i < histogram.Length; i++) count += histogram[i] = Interlocked.Read(ref Histogram[i]);
            double worst = Interlocked.Read(ref Worst) * 1000d / Stopwatch.Frequency;
            double Percentile(double percentile)
            {
                if (count == 0) return 0;
                long target = (long)Math.Ceiling(count * percentile), sum = 0;
                for (int i = 0; i < Buckets; i++)
                    if ((sum += includeHistogram ? histogram[i] : Interlocked.Read(ref Histogram[i])) >= target)
                        return i == Buckets - 1 ? worst : i / 100d;
                return worst;
            }
            return new(count, count == 0 ? 0 : Interlocked.Read(ref Ticks) * 1000d / Stopwatch.Frequency / count,
                Percentile(.95), Percentile(.99), Percentile(.999), worst, histogram);
        }
    }
    private Samples? _work;
    private Samples[]? _phases;
    private long _start, _phaseStart, _overBudget;
    private ServerLoopPhase _phase;
    private bool _active;
    public readonly struct Scope : IDisposable
    {
        private readonly ServerLoopDiagnostics? _owner;
        internal Scope(ServerLoopDiagnostics? owner) => _owner = owner;
        public void Dispose() => _owner?.End();
    }
    public Scope Begin(bool enabled)
    {
        if (!enabled) return default;
        if (_work == null)
        {
            _work = new Samples(); _phases = new Samples[Enum.GetValues<ServerLoopPhase>().Length];
            for (int i = 0; i < _phases.Length; i++) _phases[i] = new Samples();
        }
        _start = _phaseStart = Stopwatch.GetTimestamp(); _phase = ServerLoopPhase.Ingress; _active = true;
        return new(this);
    }
    public void MarkPhase(ServerLoopPhase next)
    {
        if (!_active) return;
        long now = Stopwatch.GetTimestamp();
        _phases![(int)_phase].Add(now - _phaseStart); _phaseStart = now; _phase = next;
    }
    private void End()
    {
        if (!_active) return;
        long now = Stopwatch.GetTimestamp(); _active = false;
        _phases![(int)_phase].Add(now - _phaseStart); _work!.Add(now - _start);
        if ((now - _start) * 60d > Stopwatch.Frequency) Interlocked.Increment(ref _overBudget);
    }
    public ServerLoopSnapshot Capture(bool includeHistogram = true)
    {
        var phases = new ServerLoopSample[Enum.GetValues<ServerLoopPhase>().Length];
        for (int i = 0; i < phases.Length; i++) phases[i] = _phases?[i].Capture(includeHistogram) ?? default;
        return new(_work?.Capture(includeHistogram) ?? default, phases, Interlocked.Read(ref _overBudget));
    }
    public string Describe()
    {
        var snapshot = Capture(includeHistogram: false); var replay = ServerReplayRecorder.Diagnostics;
        return $"loop-body samples={snapshot.Work.Count} mean={snapshot.Work.MeanMilliseconds:0.00}ms "
            + $"p99={snapshot.Work.P99Milliseconds:0.00}ms p999={snapshot.Work.P999Milliseconds:0.00}ms "
            + $"worst={snapshot.Work.WorstMilliseconds:0.00}ms over16.67ms={snapshot.OverBudget}; "
            + $"phase-p99 ingress={snapshot.Phases[0].P99Milliseconds:0.00} map/control={snapshot.Phases[1].P99Milliseconds:0.00} "
            + $"sim={snapshot.Phases[2].P99Milliseconds:0.00} post-ingress={snapshot.Phases[3].P99Milliseconds:0.00} "
            + $"maintenance={snapshot.Phases[4].P99Milliseconds:0.00}ms; replay={replay.State} "
            + $"attempts={replay.Attempts} failures={replay.Failures} queue={replay.QueuedBytes}B "
            + $"error={replay.Error ?? "none"}";
    }
}

public sealed partial class DedicatedServer
{
    public ServerLoopDiagnostics LoopDiagnostics { get; } = new();
}
