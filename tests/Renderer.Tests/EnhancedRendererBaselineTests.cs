using System;
using System.Text.Json;
using MphRead;
using MphRead.Mods;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedRendererBaselineTests
{
    [Fact]
    public void CpuWindowKeepsNewestBoundedSamplesAndUsesNearestRankP99()
    {
        var window = new RenderBaselineCpuWindow(capacity: 3);
        window.AddMilliseconds(1);
        window.AddMilliseconds(2);
        window.AddMilliseconds(3);
        window.AddMilliseconds(100);

        RenderBaselineCpuStatistics statistics = window.Snapshot();

        Assert.Equal(3, statistics.Capacity);
        Assert.Equal(3, statistics.SampleCount);
        Assert.Equal(1, statistics.DiscardedSamples);
        Assert.Equal(35, statistics.AverageMilliseconds);
        Assert.Equal(100, statistics.P99Milliseconds);
    }

    [Fact]
    public void CpuWindowCanExcludeAnUnsubmittedToolFrame()
    {
        var window = new RenderBaselineCpuWindow(capacity: 2);

        int result = window.Measure(() => 0, value => value != 0);

        Assert.Equal(0, result);
        Assert.Equal(0, window.Count);
    }

    [Fact]
    public void CaptureRecordsNeutralFrameQualityGeometryAndExplicitUnavailableMetrics()
    {
        var frame = Frame();
        object identity = new();
        DrawSubmission draw = frame.Acquire();
        draw.GeometryIdentity = identity;
        frame.CaptureMesh(identity, TriangleMesh());
        frame.Add(draw);
        frame.AddVisualLight(new RenderVisualLight(Vector3.Zero, Vector3.One,
            radius: 4, intensity: 2, priority: 10));
        frame.AddHudSceneSubmission(new RenderHudSceneSubmission(
            default, RenderPrimitive.Mesh, polygonId: 2, alpha: 1,
            Matrix4.Identity, matrixStackCount: 0, matrixStack: null,
            geometryIdentity: null, textureIdentity: null, LightInfo.Zero,
            Matrix4.Identity, Matrix4.Identity,
            inlineMesh: QuadMesh()));
        frame.Seal();
        var timings = new RenderBaselineCpuWindow(capacity: 4);
        timings.AddMilliseconds(4);
        timings.AddMilliseconds(6);
        var backend = new RenderBackendInfo("sdl-gpu", "metal", "msl", "bgra8",
            "vsync", true, true, true);

        RenderBaselineCapture capture = RenderBaselineMeasurement.Capture(
            frame, "hunter-closeup/sanctorus", 75, backend, timings,
            "captures/hunter-closeup.png");

        Assert.Equal(1, capture.SchemaVersion);
        Assert.Equal(1920, capture.Output.DrawableWidth);
        Assert.Equal(1440, capture.Output.SceneWidth);
        Assert.Equal(GraphicsPreset.Enhanced, capture.Quality.Preset);
        Assert.True(capture.Quality.CelShading);
        Assert.True(capture.Quality.Bloom);
        Assert.Equal(4, capture.Quality.RequestedMsaaSamples);
        Assert.Equal(8, capture.Quality.RequestedAnisotropySamples);
        Assert.Equal(1, capture.Quality.VisualLightCount);
        Assert.Equal(1, capture.Geometry.WorldSubmissionCount);
        Assert.Equal(1, capture.Geometry.HudSceneSubmissionCount);
        Assert.Equal(2, capture.Geometry.ResolvedSubmissionCount);
        Assert.Equal(3, capture.Geometry.LogicalTriangleCount);
        Assert.Equal(2, capture.Geometry.PlannedWorldStreamDrawCount);
        Assert.Equal(2, capture.Geometry.PlannedWorldTriangleCount);
        Assert.Null(capture.Geometry.GpuDrawCallCount);
        Assert.Null(capture.Device.GpuFrameMilliseconds);
        Assert.Null(capture.Device.GpuMemoryBytes);
        Assert.Equal(5, capture.CpuFrameTime.AverageMilliseconds);
        Assert.Equal(6, capture.CpuFrameTime.P99Milliseconds);

        string firstJson = capture.ToJson();
        Assert.Equal(firstJson, capture.ToJson());
        using JsonDocument document = JsonDocument.Parse(firstJson);
        Assert.Equal("enhanced", document.RootElement.GetProperty("quality")
            .GetProperty("preset").GetString());
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("device")
            .GetProperty("gpuFrameMilliseconds").ValueKind);
    }

    [Fact]
    public void CaptureCountsUnresolvedGeometryWithoutInventingTriangles()
    {
        var frame = Frame();
        DrawSubmission draw = frame.Acquire();
        draw.GeometryIdentity = new object();
        frame.Add(draw);
        frame.Seal();

        RenderBaselineCapture capture = RenderBaselineMeasurement.Capture(frame,
            "missing-geometry", 100,
            new RenderBackendInfo("test", "test", "test", "test", "test",
                true, true, true),
            new RenderBaselineCpuWindow());

        Assert.Equal(0, capture.Geometry.ResolvedSubmissionCount);
        Assert.Equal(1, capture.Geometry.UnresolvedSubmissionCount);
        Assert.Equal(0, capture.Geometry.LogicalTriangleCount);
        Assert.Equal(0, capture.Geometry.PlannedWorldStreamDrawCount);
    }

    [Fact]
    public void CaptureRequiresASealedFrameAndStableScenario()
    {
        var frame = Frame();
        var backend = new RenderBackendInfo("test", "test", "test", "test", "test",
            true, true, true);
        var timings = new RenderBaselineCpuWindow();

        Assert.Throws<InvalidOperationException>(() => RenderBaselineMeasurement.Capture(
            frame, "scene", 100, backend, timings));

        frame.Seal();
        Assert.Throws<ArgumentException>(() => RenderBaselineMeasurement.Capture(
            frame, " ", 100, backend, timings));
    }

    private static RenderFrame Frame()
    {
        var frame = new RenderFrame(capacity: 2, maximumCapacity: 4);
        var quality = new RenderQualitySnapshot(GraphicsPreset.Enhanced,
            TextureFilteringPreset.Enhanced, AnisotropyLevel.X8, MsaaLevel.X4,
            Bloom: true, DynamicVisualLights: true);
        frame.CaptureState(Matrix4.Identity, Matrix4.Identity, Matrix4.Identity,
            Matrix4.Identity, new Vector3(2, 3, 4),
            new Vector2i(1920, 1080), new Vector2i(1440, 810),
            Vector4.UnitW, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
            hasFog: false, Vector4.Zero, 0, 0,
            default(RenderFrameOptions) with
            {
                CelShading = true,
                Quality = quality
            });
        return frame;
    }

    private static CpuMesh TriangleMesh()
        => new(new[]
        {
            Vertex(0, 0), Vertex(1, 0), Vertex(0, 1)
        }, new[] { 0, 1, 2 });

    private static CpuMesh QuadMesh()
        => new(new[]
        {
            Vertex(0, 0), Vertex(1, 0), Vertex(1, 1), Vertex(0, 1)
        }, new[] { 0, 1, 2, 0, 2, 3 });

    private static RenderVertex Vertex(float x, float y)
        => new(new Vector3(x, y, 0), Vector4.One, Vector3.UnitZ, Vector2.Zero);
}
