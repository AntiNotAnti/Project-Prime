using System;
using System.Diagnostics;

namespace MphRead.Mods.Render;

internal enum TransitionGcPhase { SceneLoad, SceneEnd }
[Flags] internal enum TransitionGcDiagnosticPolicy { Keep = 0, SkipLoad = 1, SkipEnd = 2, SkipBoth = 3 }

// Diagnostic observer/policy remains internal. Desktop/server GC behavior is unchanged.
// Android skips the unconditional blocking compacting Gen2 collection at scene load;
// when the runtime reports significant memory pressure, request only an optimized
// nonblocking collection and let Mono decide when to do the work.
internal static class TransitionGcDiagnostics
{
    [ThreadStatic] internal static Action<object>? Observer;
    [ThreadStatic] internal static TransitionGcDiagnosticPolicy Policy;
    [ThreadStatic] private static Action<TransitionGcPhase>? _testCollector;

    internal static bool ShouldSkip(TransitionGcPhase phase, TransitionGcDiagnosticPolicy policy, bool observed)
        => observed && (policy & (phase == TransitionGcPhase.SceneLoad
            ? TransitionGcDiagnosticPolicy.SkipLoad : TransitionGcDiagnosticPolicy.SkipEnd)) != 0;

    // Pure policy helpers keep memory-pressure behavior testable without Android assets.
    internal static bool AndroidAvoidBlockingCollection(bool android, TransitionGcPhase phase)
        => android && phase == TransitionGcPhase.SceneLoad;

    internal static bool AndroidUnderMemoryPressure(long loadBytes, long highThresholdBytes)
        => highThresholdBytes > 0 && loadBytes >= highThresholdBytes - highThresholdBytes / 10;

    private static void OriginalCollection(TransitionGcPhase phase)
    {
        if (phase == TransitionGcPhase.SceneLoad)
            GC.Collect(generation: 2, GCCollectionMode.Forced, blocking: true, compacting: true);
        else GC.Collect();
    }
    internal static void Collect(TransitionGcPhase phase)
    {
        if (Observer is not { } observer)
        {
#if ANDROID
            if (AndroidAvoidBlockingCollection(OperatingSystem.IsAndroid(), phase))
            {
                // This happens only at room initialization, not during a frame. A blocking
                // compacting Gen2 was repeatedly observable as a long Android loading stall.
                // No GCMemoryInfo means no proactive collection: the runtime remains in charge.
                GCMemoryInfo memory = GC.GetGCMemoryInfo();
                if (AndroidUnderMemoryPressure(memory.MemoryLoadBytes, memory.HighMemoryLoadThresholdBytes))
                    GC.Collect(generation: 2, GCCollectionMode.Optimized,
                        blocking: false, compacting: false);
                return;
            }
#endif
            OriginalCollection(phase);
            return;
        }
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
        Check(AndroidAvoidBlockingCollection(true, TransitionGcPhase.SceneLoad),
            "Android scene load avoids unconditional blocking compaction");
        Check(!AndroidAvoidBlockingCollection(false, TransitionGcPhase.SceneLoad)
                && !AndroidAvoidBlockingCollection(true, TransitionGcPhase.SceneEnd),
            "desktop and scene-end collection policy remains unchanged");
        Check(!AndroidUnderMemoryPressure(0, 0)
                && !AndroidUnderMemoryPressure(500, 1000)
                && AndroidUnderMemoryPressure(900, 1000),
            "Android collections are requested only for known high memory pressure");
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
