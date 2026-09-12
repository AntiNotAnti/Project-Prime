using System;
using MphRead.Mods.Network;
using ProjectPrime.Server.Worker.Simulation;
using Xunit;

namespace MphRead.Tests;

public sealed class SimulationTimeTests
{
    [Fact]
    public void DurationsUseTheFixedSixtyHertzClock()
    {
        Assert.Equal(180u, SimDuration.FromSeconds(3).Ticks);
        Assert.Equal(6u, SimDuration.FromMilliseconds(100).Ticks);
        Assert.Equal(0u, SimDuration.FromMilliseconds(16).Ticks);
        Assert.Equal(1u, SimDuration.FromMilliseconds(17).Ticks);
        Assert.Equal(1.5, new SimDuration(90).TotalSeconds);
    }

    [Fact]
    public void NegativeAndOverflowingDurationsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SimDuration.FromSeconds(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => SimDuration.FromMilliseconds(-1));
        Assert.Throws<OverflowException>(() => SimDuration.FromSeconds(Int32.MaxValue));
    }

    [Fact]
    public void TickArithmeticPreservesUncheckedUintWrap()
    {
        SimTick beforeWrap = new(UInt32.MaxValue - 2);
        SimTick afterWrap = beforeWrap + new SimDuration(5);

        Assert.Equal(2u, afterWrap.Value);
        Assert.Equal(5u, afterWrap.ElapsedSince(beforeWrap).Ticks);
        Assert.True(afterWrap.HasElapsedSince(beforeWrap, new SimDuration(5)));
        Assert.False(beforeWrap.HasElapsedSince(afterWrap, SimDuration.Zero));
    }

    [Fact]
    public void SpectatorDurationsPreserveDelayRetentionAndDueBoundaries()
    {
        Assert.Equal(ObserverTimeline.MinimumBaselineRetention,
            ObserverTimeline.CalculateRetention(0));
        Assert.Equal(new SimDuration(1920), ObserverTimeline.CalculateRetention(30));
        Assert.True(ObserverTimeline.Due(new(20), new(UInt32.MaxValue - 39), new(60)));
        Assert.False(ObserverTimeline.Due(new(20), new(21), SimDuration.Zero));
    }

    [Fact]
    public void WorkerStatusCadenceRemainsTenHertz()
    {
        Assert.Equal(10, SimulationLane.StatusSnapshotRateHz);
        Assert.Equal(new SimDuration(6), SimulationLane.StatusSnapshotInterval);
    }
}
