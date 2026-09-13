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
    public void CompletedWindowRestartsAtCurrentPresentation()
    {
        var counter = new PresentedFrameRateCounter();
        long halfSecond = Stopwatch.Frequency / 2;

        Assert.False(counter.Record(0, out _));
        Assert.True(counter.Record(halfSecond, out float first));
        Assert.Equal(2, first);
        Assert.False(counter.Record(halfSecond + Stopwatch.Frequency / 4, out _));
        Assert.True(counter.Record(halfSecond + Stopwatch.Frequency, out float second));
        Assert.Equal(2, second);
    }
}
