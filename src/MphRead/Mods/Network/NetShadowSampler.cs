using System;
using System.Diagnostics;
using MphRead.Entities;
using OpenTK.Mathematics;

namespace MphRead.Mods.Network;

public enum ShadowSamplingMode { Off, Production, Study, Full }

/// <summary>Deterministic diagnostic sampling. Never participates in shot timing or damage.</summary>
public static class NetShadowSampler
{
    public static ShadowSamplingMode Mode { get; set; } = ShadowSamplingMode.Production;
    public static long ShadowEligible, ShadowSampled, ShadowSkipped, ShadowSimulationSteps,
        ShadowSimulationTicks, ShadowSimulationMaxTicks;

    public static bool Configure(string value)
    {
        if (!Enum.TryParse(value, true, out ShadowSamplingMode mode) || !Enum.IsDefined(mode)) return false;
        Mode = mode; return true;
    }

    public static bool ShouldSampleShadow(in ShotKey key)
    {
        if (Mode == ShadowSamplingMode.Off) return false;
        if (Mode == ShadowSamplingMode.Full) return true;
        // Explicit integer mixing is stable across runtimes, processes and platforms.
        // HashCode/record.GetHashCode are deliberately not used.
        ulong value = key.AuthorityEpoch;
        Mix(ref value, key.MatchId); Mix(ref value, (uint)key.ShooterSlot);
        Mix(ref value, key.Generation); Mix(ref value, key.LifeId); Mix(ref value, key.ShotId);
        return (value & (Mode == ShadowSamplingMode.Study ? 3UL : 15UL)) == 0;
    }

    private static void Mix(ref ulong value, ulong part)
    {
        unchecked
        {
            value ^= part + 0x9e3779b97f4a7c15UL;
            value = (value ^ (value >> 30)) * 0xbf58476d1ce4e5b9UL;
            value = (value ^ (value >> 27)) * 0x94d049bb133111ebUL;
            value ^= value >> 31;
        }
    }

    internal static ShadowOutcome CompareShot(in ShotKey key, PlayerEntity shooter,
        Vector3 origin, Vector3 direction, double hard, double allowed)
    {
        ShadowEligible++;
        if (!ShouldSampleShadow(key)) { ShadowSkipped++; return ShadowOutcome.NotSampled; }
        ShadowSampled++;
        long start = Stopwatch.GetTimestamp();
        try { return NetHistoricalTrace.CompareShot(shooter, origin, direction, hard, allowed); }
        finally
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            ShadowSimulationTicks += elapsed;
            ShadowSimulationMaxTicks = Math.Max(ShadowSimulationMaxTicks, elapsed);
        }
    }

    public static void Reset() => ShadowEligible = ShadowSampled = ShadowSkipped = ShadowSimulationSteps
        = ShadowSimulationTicks = ShadowSimulationMaxTicks = 0;
}
