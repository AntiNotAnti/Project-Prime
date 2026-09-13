using System;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using Xunit;

namespace ProjectPrime.Renderer.Tests;

public sealed class FrameTimingTelemetryTests
{
    [Fact]
    public void PhaseSnapshotsUseBoundedNearestRankPercentiles()
    {
        var timing = new FrameTiming();
        for (int value = 1; value <= 5; value++)
        {
            var sample = new FramePhaseTimingSample(
                InputMilliseconds: value,
                SimulationMilliseconds: value + 10,
                ScenePreparationMilliseconds: value + 20,
                DrawListBuildMilliseconds: value + 30,
                RenderEncodeMilliseconds: value + 40,
                RenderSubmitMilliseconds: value + 50,
                PresentMilliseconds: value + 60,
                OverlayUiMilliseconds: value + 70,
                AfterFrameMilliseconds: value + 80,
                WholeFrameMilliseconds: value + 90,
                LegacyTotalRenderMilliseconds: value + 100,
                ElapsedSeconds: 0);
            timing.RecordRuntimeFrame(in sample);
        }

        FrameTimingDiagnosticsSnapshot snapshot = timing.CaptureDiagnostics();
        AssertPercentiles(snapshot.Input, 3, 5, 3);
        AssertPercentiles(snapshot.Simulation, 13, 15, 13);
        AssertPercentiles(snapshot.ScenePreparation, 23, 25, 23);
        AssertPercentiles(snapshot.DrawListBuild, 33, 35, 33);
        AssertPercentiles(snapshot.RenderEncode, 43, 45, 43);
        AssertPercentiles(snapshot.RenderSubmit, 53, 55, 53);
        AssertPercentiles(snapshot.Present, 63, 65, 63);
        AssertPercentiles(snapshot.OverlayUi, 73, 75, 73);
        AssertPercentiles(snapshot.AfterFrame, 83, 85, 83);
        AssertPercentiles(snapshot.WholeFrame, 93, 95, 93);
        AssertPercentiles(snapshot.LegacyTotalRender, 103, 105, 103);
        Assert.Equal(snapshot.LegacyTotalRender, snapshot.Render);
    }

    [Fact]
    public void InvalidPhasesAreUnavailableAndResetClearsAllWindows()
    {
        var timing = new FrameTiming();
        var sample = new FramePhaseTimingSample(
            InputMilliseconds: 1,
            SimulationMilliseconds: double.NaN,
            ScenePreparationMilliseconds: -1,
            DrawListBuildMilliseconds: double.PositiveInfinity,
            RenderEncodeMilliseconds: 2,
            RenderSubmitMilliseconds: double.NegativeInfinity,
            PresentMilliseconds: 3,
            OverlayUiMilliseconds: double.NaN,
            AfterFrameMilliseconds: -0.1,
            WholeFrameMilliseconds: 4,
            LegacyTotalRenderMilliseconds: 5,
            ElapsedSeconds: 0);
        timing.RecordRuntimeFrame(in sample);

        FrameTimingDiagnosticsSnapshot snapshot = timing.CaptureDiagnostics();
        Assert.Equal(1, snapshot.Input.Count);
        Assert.Equal(0, snapshot.Simulation.Count);
        Assert.Equal(0, snapshot.ScenePreparation.Count);
        Assert.Equal(0, snapshot.DrawListBuild.Count);
        Assert.Equal(1, snapshot.RenderEncode.Count);
        Assert.Equal(0, snapshot.RenderSubmit.Count);
        Assert.Equal(1, snapshot.Present.Count);
        Assert.Equal(0, snapshot.OverlayUi.Count);
        Assert.Equal(0, snapshot.AfterFrame.Count);
        Assert.Equal(1, snapshot.WholeFrame.Count);
        Assert.Equal(1, snapshot.LegacyTotalRender.Count);

        timing.ResetDiagnostics();
        snapshot = timing.CaptureDiagnostics();
        Assert.Equal(0, snapshot.Input.Count);
        Assert.Equal(0, snapshot.Simulation.Count);
        Assert.Equal(0, snapshot.ScenePreparation.Count);
        Assert.Equal(0, snapshot.DrawListBuild.Count);
        Assert.Equal(0, snapshot.RenderEncode.Count);
        Assert.Equal(0, snapshot.RenderSubmit.Count);
        Assert.Equal(0, snapshot.Present.Count);
        Assert.Equal(0, snapshot.OverlayUi.Count);
        Assert.Equal(0, snapshot.AfterFrame.Count);
        Assert.Equal(0, snapshot.WholeFrame.Count);
        Assert.Equal(0, snapshot.LegacyTotalRender.Count);
    }

    [Fact]
    public void MeanTracksOnlyTheBoundedRetainedWindow()
    {
        var sampler = new BoundedPercentileSampler(4);
        for (int value = 1; value <= 5; value++) sampler.Record(value);

        BoundedPercentileSnapshot snapshot = sampler.Snapshot();

        Assert.Equal(4, snapshot.Count);
        Assert.Equal(5, snapshot.TotalCount);
        Assert.Equal(1, snapshot.Evictions);
        Assert.Equal(3.5, snapshot.Mean);
    }

    [Fact]
    public void WarmedRecordingPathAllocatesNothing()
    {
        var timing = new FrameTiming();
        var sample = new FramePhaseTimingSample(
            InputMilliseconds: 1,
            SimulationMilliseconds: 2,
            ScenePreparationMilliseconds: 3,
            DrawListBuildMilliseconds: 4,
            RenderEncodeMilliseconds: 5,
            RenderSubmitMilliseconds: 6,
            PresentMilliseconds: 7,
            OverlayUiMilliseconds: 8,
            AfterFrameMilliseconds: 9,
            WholeFrameMilliseconds: 10,
            LegacyTotalRenderMilliseconds: 11,
            ElapsedSeconds: 1);
        timing.RecordRuntimeFrame(in sample);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100_000; i++) timing.RecordRuntimeFrame(in sample);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    private static void AssertPercentiles(BoundedPercentileSnapshot sample,
        double p50, double max, double mean)
    {
        Assert.Equal(5, sample.Count);
        Assert.Equal(p50, sample.P50);
        Assert.Equal(max, sample.P95);
        Assert.Equal(max, sample.P99);
        Assert.Equal(max, sample.P999);
        Assert.Equal(max, sample.Max);
        Assert.Equal(mean, sample.Mean);
    }
}
