using System.Diagnostics;
using Xunit;

namespace MphRead.Tests;

public sealed class PresentedFrameRateCounterTests
{
    [Fact]
    public void FirstPresentationStartsWindowWithoutIncludingPriorLoadTime()
    {
        var counter = new PresentedFrameRateCounter();
        long firstPresentation = Stopwatch.Frequency * 20L;

        Assert.False(counter.Record(firstPresentation, out _));

        long timestamp = firstPresentation;
        for (int i = 0; i < 30; i++)
        {
            timestamp = firstPresentation + (i + 1L) * Stopwatch.Frequency / 60;
            if (i < 29)
            {
                Assert.False(counter.Record(timestamp, out _));
            }
        }

        Assert.True(counter.Record(timestamp, out float fps));
        Assert.InRange(fps, 59.9f, 60.1f);
    }

    [Fact]
    public void RollingWindowRemainsAccurateAcrossReportBoundaries()
    {
        var counter = new PresentedFrameRateCounter();
        Assert.False(counter.Record(0, out _));

        int reports = 0;
        for (int frame = 1; frame <= 120; frame++)
        {
            long timestamp = frame * Stopwatch.Frequency / 60;
            if (counter.Record(timestamp, out float fps))
            {
                reports++;
                Assert.InRange(fps, 59.9f, 60.1f);
            }
        }

        Assert.True(reports >= 6);
    }

    [Theory]
    [InlineData(300)]
    [InlineData(400)]
    [InlineData(500)]
    public void HighRenderRatesRemainAccurate(int targetRate)
    {
        var counter = new PresentedFrameRateCounter();
        Assert.False(counter.Record(0, out _));

        float latest = 0;
        for (int frame = 1; frame <= targetRate * 2; frame++)
        {
            long timestamp = (long)Math.Round(frame * Stopwatch.Frequency
                / (double)targetRate);
            if (counter.Record(timestamp, out float fps)) latest = fps;
        }

        Assert.InRange(latest, targetRate - 0.5f, targetRate + 0.5f);
    }

    [Fact]
    public void QueuedPresentationBurstsUseTheFullElapsedSpan()
    {
        var counter = new PresentedFrameRateCounter();
        Assert.False(counter.Record(0, out _));

        float latest = 0;
        for (int frame = 1; frame <= 60; frame++)
        {
            // Model the alternating early/late callbacks produced by a queued
            // VSync present path while preserving a 60 Hz output cadence.
            double jitter = frame % 2 == 0 ? 0 : -0.004;
            long timestamp = (long)Math.Round((frame / 60.0 + jitter)
                * Stopwatch.Frequency);
            if (counter.Record(timestamp, out float fps)) latest = fps;
        }

        Assert.InRange(latest, 59.9f, 60.1f);
    }

    [Fact]
    public void IdleGapStartsANewPresentationWindow()
    {
        var counter = new PresentedFrameRateCounter();
        Assert.False(counter.Record(0, out _));
        Assert.False(counter.Record(Stopwatch.Frequency * 2, out _));

        float latest = 0;
        for (int frame = 1; frame <= 30; frame++)
        {
            long timestamp = Stopwatch.Frequency * 2
                + frame * Stopwatch.Frequency / 60;
            if (counter.Record(timestamp, out float fps)) latest = fps;
        }

        Assert.InRange(latest, 59.9f, 60.1f);
    }
}
