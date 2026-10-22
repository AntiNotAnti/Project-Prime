using System;
using System.Collections.Generic;

/// <summary>Pure bounds for one existing-harness capture mode; no scene/clock owner.</summary>
internal static class DisplayPosePlan
{
    internal const string OriginalScenario = "native-pose-v1";
    internal const string RetreatScenario = "native-pose-charge-retreat-v2";
    internal static string ResolveScenario(string? selected, bool poseCheck)
    {
        if (selected == null) return OriginalScenario;
        if (!poseCheck) throw new ArgumentException("-pose-scenario requires -posecheck.");
        if (selected != OriginalScenario && selected != RetreatScenario)
            throw new ArgumentException("-pose-scenario requires native-pose-v1 or native-pose-charge-retreat-v2.");
        return selected;
    }
    internal static string ResolveFocusControl(string? selected, bool poseCheck)
    {
        if (selected == null) return "none";
        if (!poseCheck || selected is not ("none" or "isolated" or "legacy"))
            throw new ArgumentException("-pose-focus-control requires -posecheck and none|isolated|legacy.");
        return selected;
    }
    // An explicit scenario input, never a position edit or draw-time recovery.
    internal static bool RetreatsDuringCharge(string scenario, int tick)
        => scenario == RetreatScenario && tick is >= 361 and <= 390;
    internal static readonly int[] OutputRates = { 60, 120, 144, 240, 360, 500 };
    internal static string ResolveSchedule(string? selected, bool poseCheck, int hz)
    {
        if (selected == null) return "steady";
        if (!poseCheck || selected is not ("steady" or "jitter144") || selected == "jitter144" && hz != 144)
            throw new ArgumentException("-pose-schedule requires -posecheck and steady|jitter144 (jitter requires -hz 144).");
        return selected;
    }
    // A fixed four-frame cycle, mean 1/144 s and maximum 1/96 s. It uses no
    // game RNG and admits at most one 60 Hz step per output, so input remains
    // prepared once per actual simulation step by the existing fixture.
    internal static double Elapsed(string schedule, int hz, int outputIndex)
        => (schedule == "jitter144" ? ((outputIndex % 4) switch { 0 => .5, 1 => 1.5, 2 => .75, _ => 1.25 }) : 1) / hz;
    internal const int WarmupTicks = 240;
    internal const int TotalTicks = 1440;
    internal static readonly IReadOnlyDictionary<int, string> Captures = new Dictionary<int, string>
    {
        [300] = "biped-motion", [420] = "charge", [468] = "charge-release",
        [560] = "alt-motion", [650] = "alt-attack", [870] = "unmorph",
        [970] = "remorph", [1021] = "death", [1250] = "respawn",
        [1355] = "scope", [1382] = "teleport", [1390] = "clock-reset", [1440] = "final"
    };
    internal static string Phase(int nextTick) => nextTick switch
    {
        <= 0 or > TotalTicks => throw new ArgumentOutOfRangeException(nameof(nextTick)),
        <= WarmupTicks => "warmup",
        <= 360 => "biped",
        <= 480 => "charge",
        <= 1320 => "alt-lifecycle",
        <= 1380 => "scope",
        _ => "fences"
    };
    // This predicts only whether to prepare the next scripted input. The actual
    // FrameTiming.Advance in RenderWindow remains the sole simulation scheduler.
    internal static int PreviewSteps(double accumulator, double elapsed, double step)
    {
        if (!double.IsFinite(accumulator) || !double.IsFinite(elapsed) || !double.IsFinite(step)
            || accumulator < 0 || elapsed <= 0 || step <= 0 || accumulator >= step || elapsed > step)
            throw new ArgumentException("Expected bounded 60 Hz accumulator and one scheduled output interval.");
        return accumulator + elapsed >= step ? 1 : 0;
    }
}
