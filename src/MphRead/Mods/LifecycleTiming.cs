using System;
using System.Diagnostics;
using System.Threading;

namespace MphRead.Mods;

/// <summary>
/// Low-cost lifecycle markers for reports where "slow" otherwise has no boundary.
/// The in-memory debug ring is available before disk logging is attached, so early
/// startup markers survive crashes without forcing file I/O onto the critical path.
/// </summary>
internal static class LifecycleTiming
{
    private static long _startup;
    private static long _shutdown;
    private static int _firstFrame;

    internal static void Begin()
    {
        long now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _startup, now, 0) == 0)
            DebugLog.Line("startup", "process startup begin");
    }

    internal static void Startup(string stage)
    {
        long started = Volatile.Read(ref _startup);
        if (started == 0) { Begin(); started = Volatile.Read(ref _startup); }
        DebugLog.Line("startup",
            $"{stage} +{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms");
    }

    internal static void FirstFrame()
    {
        if (Interlocked.Exchange(ref _firstFrame, 1) == 0)
            Startup("first presented shell frame");
    }

    internal static void BeginShutdown(string reason)
    {
        long now = Stopwatch.GetTimestamp();
        if (Interlocked.CompareExchange(ref _shutdown, now, 0) == 0)
            DebugLog.Line("shutdown", $"begin ({reason})");
    }

    internal static void Shutdown(string stage)
    {
        long started = Volatile.Read(ref _shutdown);
        if (started == 0)
        {
            BeginShutdown("implicit");
            started = Volatile.Read(ref _shutdown);
        }
        DebugLog.Line("shutdown",
            $"{stage} +{Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0} ms");
    }
}
