using System;

namespace MphRead.Mods.Network;

/// <summary>Server-owned snapshot cadence for the fixed-rate authoritative loop.</summary>
public readonly record struct SnapshotCadence
{
    public const int ServerTickRateHz = 60;
    public const int DefaultRateHz = 30;

    public int RateHz { get; }
    public int IntervalTicks => ServerTickRateHz / RateHz;

    public SnapshotCadence(int rateHz)
    {
        if (!IsSupported(rateHz)) throw new ArgumentOutOfRangeException(nameof(rateHz), "Snapshot rate must be 30 or 60 Hz.");
        RateHz = rateHz;
    }

    public bool IsDue(uint serverTick) => serverTick % (uint)IntervalTicks == 0;

    public static bool IsSupported(int rateHz) => rateHz is 30 or 60;
}
