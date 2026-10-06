using System;

namespace MphRead.Mods.Render;

internal static class GpuTimingSamplePolicy
{
    // A missing/invalid measurement is not a zero-duration GPU frame. Reject
    // disjoint/reset clocks and invalid periods rather than polluting the lows.
    internal static bool TryMilliseconds(ulong begin, ulong end, double nanosecondsPerTick,
        out double milliseconds)
    {
        milliseconds = 0;
        if (!double.IsFinite(nanosecondsPerTick) || nanosecondsPerTick <= 0 || end < begin)
            return false;
        double value = (end - begin) * nanosecondsPerTick / 1_000_000d;
        if (!double.IsFinite(value)) return false;
        milliseconds = value;
        return true;
    }
}
