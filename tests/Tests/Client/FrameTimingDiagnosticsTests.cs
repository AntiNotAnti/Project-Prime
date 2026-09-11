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
    }

    [Fact]
    public void RuntimeDiagnosticsCaptureTimingStepsAndGcBaseline()
    {
        FrameTiming.ResetDiagnostics();
        FrameTiming.RecordRuntimeFrame(1, 2, 1);
        FrameTiming.RecordRuntimeFrame(3, 4, 1);

        FrameTimingDiagnosticsSnapshot snapshot = FrameTiming.CaptureDiagnostics();
        Assert.Equal(2, snapshot.Simulation.Count);
        Assert.Equal(1, snapshot.Simulation.P50);
        Assert.Equal(3, snapshot.Simulation.Max);
        Assert.Equal(2, snapshot.Render.P50);
        Assert.Equal(4, snapshot.Render.Max);
        Assert.True(double.IsFinite(snapshot.GcAllocatedBytesPerSecond));

        FrameTiming.ResetDiagnostics();
        snapshot = FrameTiming.CaptureDiagnostics();
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
