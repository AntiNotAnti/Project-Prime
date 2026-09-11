using System;
using System.Diagnostics;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class NetClockTests
{
    private const double TickRate = 60;

    [Fact]
    public void SteadyLowRttSamplesTrackThePresentationTimeline()
    {
        var clock = new NetClock();
        for (int index = 0; index < 8; index++)
            Observe(clock, 10 + index * 0.5, 20 + index % 2, 100);

        double estimate = clock.EstimateServerTick(Timestamp(15));
        Assert.InRange(Math.Abs(estimate - (15 * TickRate + 100)), 0, 1.5);
        Assert.Equal(8, clock.Metrics.Rtt.Count);
    }

    [Fact]
    public void NormalRttJitterRemainsEligibleForTheRobustAggregate()
    {
        var clock = new NetClock();
        double[] rtts = [18, 25, 21, 28, 20, 24, 19, 26];
        for (int index = 0; index < rtts.Length; index++)
            Observe(clock, 10 + index * 0.5, rtts[index], 200);

        double estimate = clock.EstimateServerTick(Timestamp(15));
        Assert.InRange(Math.Abs(estimate - (15 * TickRate + 200)), 0, 2);
    }

    [Fact]
    public void SingleHighRttOutlierDoesNotPerturbLowRttClock()
    {
        var clock = Seed(clockOffset: 100);
        double before = clock.EstimateServerTick(Timestamp(14));

        // The server observed this reply at the far end of a highly
        // asymmetric path. Its midpoint-derived offset is intentionally bad.
        Observe(clock, 14, 500, 100, forwardFraction: 1);
        double after = clock.EstimateServerTick(Timestamp(15));

        Assert.True(after >= before);
        Assert.InRange(Math.Abs(after - (15 * TickRate + 100)), 0, 3);
    }

    [Fact]
    public void BurstOfHighRttOutliersKeepsTheRecentLowRttPopulation()
    {
        var clock = Seed(clockOffset: 300);
        for (int index = 0; index < 8; index++)
            Observe(clock, 14 + index * 0.25, 350, 300, forwardFraction: 1);

        double estimate = clock.EstimateServerTick(Timestamp(16));
        Assert.InRange(Math.Abs(estimate - (16 * TickRate + 300)), 0, 5);
    }

    [Fact]
    public void SystematicPathAsymmetryIsBoundedAndDoesNotCauseAClockSnap()
    {
        var clock = new NetClock();
        for (int index = 0; index < 8; index++)
            Observe(clock, 10 + index * 0.5, 100, 400, forwardFraction: .8);

        double estimate = clock.EstimateServerTick(Timestamp(15));
        // A midpoint estimator cannot identify a systematic asymmetry. The
        // robust clock preserves the bounded bias rather than pretending it can
        // recover an unavailable one-way delay.
        Assert.InRange(Math.Abs(estimate - (15 * TickRate + 400)), 0, 3);
    }

    [Fact]
    public void RolloverAndStaleRepliesUseSerialTickOrder()
    {
        var clock = new NetClock();
        long sent = Timestamp(20);
        long roundTrip = Stopwatch.Frequency / 60;
        Assert.True(clock.Observe(sent, sent + roundTrip, UInt32.MaxValue - 2));
        double before = clock.EstimateServerTick(sent + Stopwatch.Frequency);

        long wrappedSent = sent + Stopwatch.Frequency;
        Assert.True(clock.Observe(wrappedSent, wrappedSent + roundTrip, 3));
        double after = clock.EstimateServerTick(wrappedSent + Stopwatch.Frequency);
        Assert.True(after > before);
        Assert.False(clock.Observe(wrappedSent + Stopwatch.Frequency * 2,
            wrappedSent + Stopwatch.Frequency * 2 + roundTrip, UInt32.MaxValue - 20));
    }

    [Fact]
    public void InvalidAndStalePingTimestampsAreRejected()
    {
        var clock = new NetClock();
        long sent = Timestamp(10);
        long received = sent + Stopwatch.Frequency / 20;
        Assert.True(clock.Observe(sent, received, 700));
        Assert.False(clock.Observe(sent - 1, received - 1, 701));
        Assert.False(clock.Observe(received, sent, 702));
        Assert.False(clock.Observe(sent, sent + Stopwatch.Frequency * 31, 703));
        Assert.True(clock.Synchronized);
    }

    [Fact]
    public void EstimateNeverMovesBackwardDuringALargeNegativeCorrection()
    {
        var clock = new NetClock();
        Observe(clock, 10, 20, 800);
        double previous = clock.EstimateServerTick(Timestamp(11));
        Observe(clock, 11.01, 20, 760);

        double immediate = clock.EstimateServerTick(Timestamp(11.02));
        double later = clock.EstimateServerTick(Timestamp(12));
        Assert.True(immediate >= previous);
        Assert.True(later >= immediate);
        Assert.Equal(later, clock.EstimateServerTick(Timestamp(11.5)));
    }

    [Fact]
    public void SustainedPathChangesConvergeWithinBoundedProductionWindow()
    {
        foreach (double targetOffset in new[] { 112d, 88d })
        {
            var clock = Seed(clockOffset: 100);
            double previous = clock.EstimateServerTick(Timestamp(14));
            double eighthSampleError = 0;

            // One sample per simulated second models the slowest normal
            // production cadence. Old low-RTT samples must not hold the
            // robust target for the entire 16-sample ring lifetime.
            for (int index = 0; index < 16; index++)
            {
                Observe(clock, 14 + index, 20, targetOffset);
                double estimate = clock.EstimateServerTick(Timestamp(15 + index));
                Assert.True(estimate >= previous);
                previous = estimate;

                double expected = (15 + index) * TickRate + targetOffset;
                double error = Math.Abs(estimate - expected);
                if (index == 7) eighthSampleError = error;
                if (index == 15) Assert.InRange(error, 0, 1.5);
            }

            Assert.InRange(eighthSampleError, 0, 2.5);
        }
    }

    [Fact]
    public void PermanentRttIncreaseWithOffsetShiftConvergesWithoutAcceptingTransientOutlier()
    {
        var transient = Seed(clockOffset: 100);
        Observe(transient, 14, 100, 112);
        double transientEstimate = transient.EstimateServerTick(Timestamp(15));
        Assert.InRange(Math.Abs(transientEstimate - (15 * TickRate + 100)), 0, 2);

        foreach (double targetOffset in new[] { 112d, 88d })
        {
            var clock = Seed(clockOffset: 100);
            double previous = clock.EstimateServerTick(Timestamp(14));
            double eighthSampleError = 0;

            // A persistent 20 -> 100 ms path change is accepted only after
            // three consistent samples, then must converge within eight
            // one-second production samples in either direction.
            for (int index = 0; index < 8; index++)
            {
                Observe(clock, 14 + index, 100, targetOffset);
                double estimate = clock.EstimateServerTick(Timestamp(15 + index));
                Assert.True(estimate >= previous);
                previous = estimate;

                double expected = (15 + index) * TickRate + targetOffset;
                double error = Math.Abs(estimate - expected);
                if (index == 7) eighthSampleError = error;
            }

            Assert.InRange(eighthSampleError, 0, 1.5);
        }
    }

    [Fact]
    public void AcceptedRttRegimeIgnoresOutlierWithoutFallingBackToOldPopulation()
    {
        foreach (double targetOffset in new[] { 112d, 88d })
        {
            var clock = Seed(clockOffset: 100);
            for (int index = 0; index < 4; index++)
                Observe(clock, 14 + index, 100, targetOffset);

            double before = clock.EstimateServerTick(Timestamp(18));
            double expectedBefore = 18 * TickRate + targetOffset;
            Assert.InRange(Math.Abs(before - expectedBefore), 0, 2.5);

            // The active 100 ms regime is established while old 20 ms
            // samples still remain in the ring. A 500 ms reply must not make
            // the aggregate fall back to those old samples.
            Observe(clock, 18, 500, targetOffset, forwardFraction: 1);
            double after = clock.EstimateServerTick(Timestamp(19));
            Assert.True(after >= before);
            Assert.InRange(Math.Abs(after - (19 * TickRate + targetOffset)), 0, 1.5);
        }
    }

    [Fact]
    public void ResetDropsTheOldEpochAndRecoversImmediately()
    {
        var clock = Seed(clockOffset: 900);
        Assert.True(clock.Synchronized);
        clock.EstimateServerTick(Timestamp(15));
        Observe(clock, 14, 100, 900);
        Assert.True(clock.Metrics.SmoothedRttMs > 0);
        Assert.True(clock.Metrics.JitterMs > 0);

        clock.Reset();
        Assert.False(clock.Synchronized);
        Assert.Equal(0, clock.Metrics.Rtt.Count);
        Assert.Equal(0, clock.Metrics.Rtt.PercentileCount);
        Assert.Equal(0, clock.Metrics.SmoothedRttMs);
        Assert.Equal(0, clock.Metrics.JitterMs);
        Observe(clock, 100, 20, -250);
        double estimate = clock.EstimateServerTick(Timestamp(100));
        Assert.InRange(Math.Abs(estimate - (100 * TickRate - 250)), 0, 1.5);

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        clock.Reset();
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void ObserveUsesFixedStorageAfterInitialization()
    {
        var clock = Seed(clockOffset: 50);
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = true;
        for (int index = 0; index < 64; index++)
            accepted &= TryObserve(clock, 20 + index * 0.25, 20 + index % 3, 50);
        Assert.True(accepted);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static NetClock Seed(double clockOffset)
    {
        var clock = new NetClock();
        for (int index = 0; index < 8; index++)
            Observe(clock, 10 + index * 0.5, 20 + index % 2, clockOffset);
        return clock;
    }

    private static void Observe(NetClock clock, double sentSeconds, double rttMs,
        double clockOffset, double forwardFraction = .5)
        => Assert.True(TryObserve(clock, sentSeconds, rttMs, clockOffset, forwardFraction));

    private static bool TryObserve(NetClock clock, double sentSeconds, double rttMs,
        double clockOffset, double forwardFraction = .5)
    {
        long sent = Timestamp(sentSeconds);
        long received = sent + Timestamp(rttMs / 1000);
        double serverSeconds = sentSeconds + rttMs / 1000 * forwardFraction;
        uint serverTick = unchecked((uint)Math.Round(serverSeconds * TickRate + clockOffset));
        return clock.Observe(sent, received, serverTick);
    }

    private static long Timestamp(double seconds)
        => checked((long)Math.Round(seconds * Stopwatch.Frequency));
}
