using System;
using System.Diagnostics;

namespace MphRead.Mods.Render;

internal enum TransitionGcPhase { SceneLoad, SceneEnd }
[Flags] internal enum TransitionGcDiagnosticPolicy { Keep = 0, SkipLoad = 1, SkipEnd = 2, SkipBoth = 3 }

// Explicit internal test harness only: no environment/settings/product switches.
// Null observer ignores diagnostic policy and follows the original GC calls.
internal static class TransitionGcDiagnostics
{
    [ThreadStatic] internal static Action<object>? Observer;
    [ThreadStatic] internal static TransitionGcDiagnosticPolicy Policy;
    [ThreadStatic] private static Action<TransitionGcPhase>? _testCollector;

    internal static bool ShouldSkip(TransitionGcPhase phase, TransitionGcDiagnosticPolicy policy, bool observed)
        => observed && (policy & (phase == TransitionGcPhase.SceneLoad
            ? TransitionGcDiagnosticPolicy.SkipLoad : TransitionGcDiagnosticPolicy.SkipEnd)) != 0;

    private static void OriginalCollection(TransitionGcPhase phase)
    {
        if (phase == TransitionGcPhase.SceneLoad)
            GC.Collect(generation: 2, GCCollectionMode.Forced, blocking: true, compacting: true);
        else GC.Collect();
    }
    internal static void Collect(TransitionGcPhase phase)
    {
        if (Observer is not { } observer) { OriginalCollection(phase); return; }
        var before = MemorySnapshot.Capture();
        bool skipped = ShouldSkip(phase, Policy, observed: true);
        TimeSpan pauseBefore = GC.GetTotalPauseDuration();
        long start = Stopwatch.GetTimestamp();
        if (!skipped)
        {
            if (_testCollector is { } fake) fake(phase);
            else OriginalCollection(phase);
        }
        double wallMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        double pauseDeltaMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
        observer(new CollectionObservation(phase.ToString(), Policy.ToString(), skipped, start,
            wallMs, pauseDeltaMs, before, MemorySnapshot.Capture()));
    }
    internal static StageScope MeasureStage(string name)
        => Observer is { } observer ? new(name, observer) : default;

    internal readonly struct StageScope : IDisposable
    {
        private readonly string? _name;
        private readonly Action<object>? _observer;
        private readonly MemorySnapshot? _before;
        private readonly long _start;
        private readonly TimeSpan _pauseBefore;
        internal StageScope(string name, Action<object> observer)
        {
            _name = name; _observer = observer; _before = MemorySnapshot.Capture();
            _pauseBefore = GC.GetTotalPauseDuration(); _start = Stopwatch.GetTimestamp();
        }
        public void Dispose()
        {
            if (_observer == null || !ReferenceEquals(_observer, Observer)) return;
            double wallMs = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            double pauseDeltaMs = (GC.GetTotalPauseDuration() - _pauseBefore).TotalMilliseconds;
            _observer(new StageObservation(_name!, Policy.ToString(), _start, wallMs, pauseDeltaMs,
                _before!, MemorySnapshot.Capture()));
        }
    }
    private sealed record CollectionObservation(string Phase, string Policy, bool Skipped,
        long StartTimestamp, double CollectionWallMs, double PauseDeltaMs, MemorySnapshot Before, MemorySnapshot After);
    private sealed record StageObservation(string Stage, string Policy, long StartTimestamp,
        double StageWallMs, double PauseDeltaMs, MemorySnapshot Before, MemorySnapshot After);
    private sealed record MemorySnapshot(long WorkingSetBytes, long ManagedLiveBytes, long GcCommittedBytes,
        long GcHeapSizeBytes, long GcFragmentedBytes, long GcCollectionIndex, int LatestGeneration,
        bool LatestConcurrent, bool LatestCompacted, double TotalPauseMs, double[] LatestPauseDurationsMs,
        int Gen0Collections, int Gen1Collections, int Gen2Collections)
    {
        internal static MemorySnapshot Capture()
        {
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            double[] pauses = new double[info.PauseDurations.Length];
            for (int i = 0; i < pauses.Length; i++) pauses[i] = info.PauseDurations[i].TotalMilliseconds;
            return new(Environment.WorkingSet, GC.GetTotalMemory(false), info.TotalCommittedBytes,
                info.HeapSizeBytes, info.FragmentedBytes, info.Index, info.Generation, info.Concurrent,
                info.Compacted, GC.GetTotalPauseDuration().TotalMilliseconds, pauses,
                GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        }
    }
    internal static int Check()
    {
        int failures = 0;
        void Check(bool condition, string name)
        { Console.WriteLine($"[transitiongccheck] {(condition ? "PASS" : "FAIL")}: {name}"); if (!condition) failures++; }
        foreach (var phase in Enum.GetValues<TransitionGcPhase>())
            Check(!ShouldSkip(phase, TransitionGcDiagnosticPolicy.SkipBoth, observed: false),
                "inactive observer cannot change " + phase + " collection policy");
        var priorObserver = Observer; var priorPolicy = Policy; var priorCollector = _testCollector;
        try
        {
            int collects = 0, records = 0;
            Observer = _ => records++;
            _testCollector = _ => collects++;
            foreach (var policy in Enum.GetValues<TransitionGcDiagnosticPolicy>())
            {
                Policy = policy; collects = records = 0;
                Collect(TransitionGcPhase.SceneLoad); Collect(TransitionGcPhase.SceneEnd);
                int expected = policy == TransitionGcDiagnosticPolicy.Keep ? 2
                    : policy == TransitionGcDiagnosticPolicy.SkipBoth ? 0 : 1;
                Check(collects == expected && records == 2, "diagnostic control observes both calls and skips only requested phase: " + policy);
            }
            var scope = MeasureStage("characterization");
            Observer = null; scope.Dispose();
            Check(records == 2, "removed observer invalidates an outstanding stage scope");
        }
        finally { Observer = priorObserver; Policy = priorPolicy; _testCollector = priorCollector; }
        return failures;
    }
}
