using MphRead.Hud.Network;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using Xunit;

namespace MphRead.Tests;

public sealed class FrameTimingDiagnosticsTests
{
    [Fact]
    public void PercentileWindowIsBoundedAndUsesNearestRank()
    {
        var sampler = new BoundedPercentileSampler(4);
        sampler.Record(1);
        sampler.Record(2);
        sampler.Record(3);
        sampler.Record(4);
        sampler.Record(5);

        BoundedPercentileSnapshot snapshot = sampler.Snapshot();
        Assert.Equal(4, snapshot.Count);
        Assert.Equal(1, snapshot.Evictions);
        Assert.Equal(3, snapshot.P50);
        Assert.Equal(5, snapshot.P95);
        Assert.Equal(5, snapshot.P99);
        Assert.Equal(5, snapshot.Max);
        Assert.Equal(3.5, snapshot.Mean);
    }

    [Fact]
    public void RuntimeDiagnosticsCaptureTimingStepsAndGcBaseline()
    {
        var timing = new FrameTiming();
        timing.ResetDiagnostics();
        var first = new FramePhaseTimingSample(
            InputMilliseconds: .5,
            SimulationMilliseconds: 1,
            ScenePreparationMilliseconds: double.NaN,
            DrawListBuildMilliseconds: 1.25,
            RenderEncodeMilliseconds: 1.5,
            RenderSubmitMilliseconds: 1.75,
            PresentMilliseconds: 1.8,
            OverlayUiMilliseconds: 1.9,
            AfterFrameMilliseconds: 1.95,
            WholeFrameMilliseconds: 2.5,
            LegacyTotalRenderMilliseconds: 2,
            ElapsedSeconds: 1);
        var second = new FramePhaseTimingSample(
            InputMilliseconds: 2.5,
            SimulationMilliseconds: 3,
            ScenePreparationMilliseconds: double.NaN,
            DrawListBuildMilliseconds: 3.25,
            RenderEncodeMilliseconds: 3.5,
            RenderSubmitMilliseconds: 3.75,
            PresentMilliseconds: 3.8,
            OverlayUiMilliseconds: 3.9,
            AfterFrameMilliseconds: 3.95,
            WholeFrameMilliseconds: 4.5,
            LegacyTotalRenderMilliseconds: 4,
            ElapsedSeconds: 1);
        timing.RecordRuntimeFrame(in first);
        timing.RecordRuntimeFrame(in second);

        FrameTimingDiagnosticsSnapshot snapshot = timing.CaptureDiagnostics();
        Assert.Equal(2, snapshot.Simulation.Count);
        Assert.Equal(1, snapshot.Simulation.P50);
        Assert.Equal(3, snapshot.Simulation.Max);
        Assert.Equal(2, snapshot.Simulation.Mean);
        Assert.Equal(2, snapshot.Render.P50);
        Assert.Equal(4, snapshot.Render.Max);
        Assert.True(double.IsFinite(snapshot.GcAllocatedBytesPerSecond));

        timing.ResetDiagnostics();
        snapshot = timing.CaptureDiagnostics();
        Assert.Equal(0, snapshot.Simulation.Count);
        Assert.Equal(0, snapshot.Render.Count);
        Assert.Equal(0, snapshot.DroppedSteps);
        Assert.Equal(0, snapshot.Stalls);
    }

    [Fact]
    public void AdvancedNetworkToggleRemainsTheSingleDevelopmentOverlaySwitch()
    {
        bool previous = NetworkHealthSettings.Advanced;
        try
        {
            NetworkHealthSettings.Advanced = false;
            Assert.False(NetworkHealthSettings.Advanced);
            NetworkHealthSettings.Advanced = true;
            Assert.True(NetworkHealthSettings.Advanced);
        }
        finally
        {
            NetworkHealthSettings.Advanced = previous;
        }
    }
}
