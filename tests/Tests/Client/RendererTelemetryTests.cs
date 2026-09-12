using System;
using System.IO;
using System.Runtime.CompilerServices;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class RendererTelemetryTests
{
    [Fact]
    public void AccumulatorCapturesLifecycleCountersWithExplicitDefinitions()
    {
        var telemetry = new RenderTelemetryAccumulator(capacity: 2);

        telemetry.BeginFrame();
        telemetry.MarkAttempted();
        telemetry.RenderPass();
        telemetry.IndexedDraw();
        telemetry.PrimitiveDraw();
        telemetry.PipelineBind();
        telemetry.SamplerBind(descriptorCount: 3);
        telemetry.VertexUniform(16);
        telemetry.FragmentUniform(32);
        telemetry.UploadScheduled(100);
        telemetry.DeviceCacheEntryCount(4);
        telemetry.SessionCacheEntryPeak(6);
        telemetry.MarkEncoded();
        telemetry.MarkSubmitted();
        telemetry.CommitScheduledUploads();
        telemetry.MarkFenceCommitted();

        RenderTelemetrySnapshot sample = telemetry.CompleteFrame();

        Assert.True(sample.Attempted);
        Assert.True(sample.Encoded);
        Assert.True(sample.Submitted);
        Assert.True(sample.FenceCommitted);
        Assert.Equal(1, sample.RenderPassCount);
        Assert.Equal(1, sample.IndexedDrawCount);
        Assert.Equal(1, sample.PrimitiveDrawCount);
        Assert.Equal(2, sample.DrawCallCount);
        Assert.Equal(1, sample.PipelineBindCount);
        Assert.Equal(1, sample.SamplerBindCount);
        Assert.Equal(3, sample.TextureBindCount);
        Assert.Equal(16, sample.VertexUniformBytes);
        Assert.Equal(32, sample.FragmentUniformBytes);
        Assert.Equal(100, sample.UploadScheduledBytes);
        Assert.Equal(100, sample.UploadCommittedBytes);
        Assert.Equal(4, sample.DeviceCacheEntryCount);
        Assert.Equal(6, sample.SessionCacheEntryPeak);
        Assert.Equal(sample, telemetry.Latest);
    }

    [Fact]
    public void SessionPeakIsMonotonicAndRingRemainsBounded()
    {
        var telemetry = new RenderTelemetryAccumulator(capacity: 2);

        telemetry.BeginFrame();
        telemetry.MarkAttempted();
        telemetry.SessionCacheEntryPeak(10);
        RenderTelemetrySnapshot first = telemetry.CompleteFrame();

        telemetry.BeginFrame();
        telemetry.MarkAttempted();
        telemetry.SessionCacheEntryPeak(3);
        RenderTelemetrySnapshot second = telemetry.CompleteFrame();

        telemetry.BeginFrame();
        telemetry.MarkAttempted();
        RenderTelemetrySnapshot third = telemetry.CompleteFrame();

        Assert.Equal(10, first.SessionCacheEntryPeak);
        Assert.Equal(10, second.SessionCacheEntryPeak);
        Assert.Equal(10, third.SessionCacheEntryPeak);
        Assert.Equal(2, telemetry.Count);
        Assert.Equal(1, telemetry.DroppedSamples);
        Assert.True(telemetry.TryDequeue(out RenderTelemetrySnapshot retainedSecond));
        Assert.True(telemetry.TryDequeue(out RenderTelemetrySnapshot retainedThird));
        Assert.Equal(second.FrameNumber, retainedSecond.FrameNumber);
        Assert.Equal(third.FrameNumber, retainedThird.FrameNumber);
        Assert.False(telemetry.TryDequeue(out _));
    }

    [Fact]
    public void BaselineRetainsBackendCountersWithoutInventingGpuMetrics()
    {
        var frame = new RenderFrame(capacity: 1, maximumCapacity: 1);
        frame.CaptureState(Matrix4.Identity, Matrix4.Identity, Matrix4.Identity,
            Matrix4.Identity, Vector3.Zero, new Vector2i(640, 480),
            new Vector2i(320, 240), Vector4.UnitW, Vector3.Zero, Vector3.Zero,
            Vector3.Zero, Vector3.Zero, hasFog: false, Vector4.Zero, 0, 0,
            default(RenderFrameOptions) with
            {
                Quality = new RenderQualitySnapshot(GraphicsPreset.Enhanced,
                    TextureFilteringPreset.Enhanced, AnisotropyLevel.X4,
                    MsaaLevel.X4, Bloom: true, DynamicVisualLights: true)
            });
        frame.Seal();

        var sample = new RenderTelemetrySnapshot(
            FrameNumber: 12, Attempted: true, Encoded: true, Submitted: true,
            FenceCommitted: true, RenderPassCount: 4, IndexedDrawCount: 2,
            PipelineBindCount: 3, SamplerBindCount: 2, TextureBindCount: 5,
            VertexUniformBytes: 64, FragmentUniformBytes: 96,
            UploadScheduledBytes: 128, UploadCommittedBytes: 128,
            DeviceCacheEntryCount: 7, SessionCacheEntryPeak: 11,
            GpuFrameMilliseconds: null,
            GpuFrameTimeStatus: "unavailable: SDL GPU timestamp queries are not exposed",
            GpuMemoryBytes: null,
            GpuMemoryStatus: "unavailable: SDL GPU memory budgeting is not exposed")
        {
            PrimitiveDrawCount = 1
        };

        RenderBaselineCapture capture = RenderBaselineMeasurement.Capture(
            frame, "telemetry", 100,
            new RenderBackendInfo("sdl-gpu", "test", "msl", "rgba8",
                "vsync", true, true, true),
            new RenderBaselineCpuWindow(), telemetry: sample);

        Assert.Equal(sample, capture.Telemetry);
        Assert.Equal(3, capture.Geometry.GpuDrawCallCount);
        Assert.Contains("SDL draw API calls", capture.Geometry.GpuDrawCallStatus);
        Assert.Null(capture.Device.GpuFrameMilliseconds);
        Assert.Null(capture.Device.GpuMemoryBytes);
        Assert.Contains("telemetry", capture.ToJson());
    }

    [Fact]
    public void FailedPostAcquireResizeRetiresTheOwnedCommandBuffer()
    {
        string source = ReadRepositoryFile(
            "src/Renderer/Backends/SdlGpu/SdlGpuBackend.cs");
        int resizeTry = source.IndexOf("try\n            {\n                if (width !=",
            StringComparison.Ordinal);
        int submitEmpty = source.IndexOf("SubmitEmpty(commandBuffer);",
            resizeTry, StringComparison.Ordinal);
        int rethrow = source.IndexOf("throw;", submitEmpty,
            StringComparison.Ordinal);
        Assert.True(resizeTry >= 0);
        Assert.True(submitEmpty > resizeTry);
        Assert.True(rethrow > submitEmpty);
    }

    private static string ReadRepositoryFile(string relativePath,
        [CallerFilePath] string sourcePath = "")
    {
        string testsDirectory = Path.GetDirectoryName(sourcePath)!;
        string repository = Path.GetFullPath(Path.Combine(testsDirectory,
            "../../.."));
        return File.ReadAllText(Path.Combine(repository, relativePath));
    }
}
