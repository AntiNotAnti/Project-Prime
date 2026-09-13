using System;
using System.Reflection;
using MphRead;
using MphRead.Mods.Render;
using Xunit;

namespace ProjectPrime.Renderer.Tests;

public sealed class FrameTimingTests
{
    [Fact]
    public void FixedRateUsesSharedSimulationTickAuthority()
    {
        Assert.Equal(SimTicks.Hz, FrameTiming.SimulationHz);
        Assert.Equal(1.0 / SimTicks.Hz, FrameTiming.StepSeconds);
    }

    [Theory]
    [InlineData(nameof(FrameTiming.Advance))]
    [InlineData(nameof(FrameTiming.Reset))]
    [InlineData(nameof(FrameTiming.ManualStep))]
    [InlineData(nameof(FrameTiming.ResetDiagnostics))]
    [InlineData(nameof(FrameTiming.CaptureDiagnostics))]
    public void RuntimeClockOperationsRequireAnOwnedInstance(string methodName)
    {
        MethodInfo method = typeof(FrameTiming).GetMethod(methodName)
            ?? throw new InvalidOperationException($"Missing {methodName}.");

        Assert.False(method.IsStatic);
    }

    [Theory]
    [InlineData(60, 600)]
    [InlineData(120, 600)]
    [InlineData(144, 599)]
    [InlineData(240, 600)]
    public void RefreshSchedulesPreserveGoldenFixedStepTrace(int refreshRate, int expectedSteps)
    {
        var timing = new FrameTiming();
        const int seconds = 10;

        for (int frame = 0; frame < refreshRate * seconds; frame++)
            timing.Advance(1.0 / refreshRate);

        Assert.Equal(expectedSteps, timing.TotalSteps);
        Assert.True(Math.Abs(timing.TotalSteps * FrameTiming.StepSeconds - seconds)
            <= FrameTiming.StepSeconds + 1e-12);
        Assert.Equal(0, timing.DroppedSteps);
        Assert.Equal(0, timing.Stalls);
    }

    [Fact]
    public void ClockInstancesDoNotShareSchedulingOrDiagnostics()
    {
        var first = new FrameTiming();
        var second = new FrameTiming();

        Assert.Equal(1, first.Advance(FrameTiming.StepSeconds));

        Assert.True(first.Active);
        Assert.False(second.Active);
        Assert.Equal(1, first.TotalFrames);
        Assert.Equal(0, second.TotalFrames);
        Assert.Equal(0, second.TotalSteps);
        Assert.Equal(1, second.RenderAlpha);
    }

    [Fact]
    public void StallAndInvalidElapsedValuesRunExactlyOneStepAndClearDebt()
    {
        var timing = new FrameTiming();
        Assert.Equal(0, timing.Advance(FrameTiming.StepSeconds / 2));
        long generation = timing.Discontinuities;

        Assert.Equal(1, timing.Advance(1));
        Assert.Equal(1, timing.Advance(-1));
        Assert.Equal(1, timing.Advance(double.NaN));

        Assert.Equal(3, timing.Stalls);
        Assert.Equal(generation + 3, timing.Discontinuities);
        Assert.Equal(0, timing.SimulationRemainderSeconds);
    }

    [Fact]
    public void CatchUpIsBoundedAndDropsOnlyWholeStepDebt()
    {
        var timing = new FrameTiming();

        int steps = timing.Advance(FrameTiming.StepSeconds * 12.5);

        Assert.Equal(FrameTiming.MaxCatchUpSteps, steps);
        Assert.Equal(7, timing.DroppedSteps);
        Assert.Equal(FrameTiming.StepSeconds / 2, timing.SimulationRemainderSeconds, 10);
        Assert.Equal(.5f, timing.RenderAlpha, 5);
        Assert.Equal(1, timing.Discontinuities);
    }

    [Fact]
    public void ResetAndManualStepPreserveLegacyCounterSemantics()
    {
        var timing = new FrameTiming();
        Assert.Equal(0, timing.Advance(FrameTiming.StepSeconds / 2));
        long frames = timing.TotalFrames;
        long steps = timing.TotalSteps;
        long generation = timing.Discontinuities;

        timing.Reset();
        Assert.False(timing.Active);
        Assert.Equal(1, timing.RenderAlpha);
        Assert.Equal(generation + 1, timing.Discontinuities);
        Assert.Equal(1, timing.ManualStep());

        Assert.True(timing.Active);
        Assert.Equal(1, timing.StepsThisFrame);
        Assert.Equal(frames, timing.TotalFrames);
        Assert.Equal(steps, timing.TotalSteps);
        Assert.Equal(0, timing.Advance(0));
    }
}
