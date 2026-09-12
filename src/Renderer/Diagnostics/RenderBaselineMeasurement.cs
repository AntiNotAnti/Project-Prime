using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MphRead.Mods;

namespace MphRead
{
    /// <summary>
    /// A bounded, single-owner CPU frame-time window for renderer baselines.
    /// Sampling stays separate from simulation timing; callers record the
    /// elapsed time around one presentation/render submission and materialize
    /// statistics only when a baseline artifact is written.
    /// </summary>
    public sealed class RenderBaselineCpuWindow
    {
        public const int DefaultCapacity = 3600;

        private readonly double[] _milliseconds;
        private int _next;
        private int _count;

        public RenderBaselineCpuWindow(int capacity = DefaultCapacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _milliseconds = new double[capacity];
        }

        public int Capacity => _milliseconds.Length;
        public int Count => _count;
        public long DiscardedSamples { get; private set; }

        public void Add(TimeSpan elapsed) => AddMilliseconds(elapsed.TotalMilliseconds);

        /// <summary>
        /// Measure one render-tool frame without requiring a timing hook in a
        /// backend or in the gameplay window loop.
        /// </summary>
        public T Measure<T>(Func<T> renderFrame)
            => Measure(renderFrame, static _ => true);

        /// <summary>
        /// Measure one render-tool frame and retain it only when the caller's
        /// submission predicate succeeds. This keeps acquire failures and
        /// skipped pictures out of a submitted-frame baseline.
        /// </summary>
        public T Measure<T>(Func<T> renderFrame, Predicate<T> include)
        {
            if (renderFrame == null) throw new ArgumentNullException(nameof(renderFrame));
            if (include == null) throw new ArgumentNullException(nameof(include));
            long started = Stopwatch.GetTimestamp();
            T result = renderFrame();
            double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (include(result)) AddMilliseconds(elapsed);
            return result;
        }

        public void AddMilliseconds(double milliseconds)
        {
            if (!double.IsFinite(milliseconds) || milliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(milliseconds));

            if (_count == _milliseconds.Length)
            {
                DiscardedSamples++;
            }
            else
            {
                _count++;
            }
            _milliseconds[_next] = milliseconds;
            _next = (_next + 1) % _milliseconds.Length;
        }

        public RenderBaselineCpuStatistics Snapshot()
        {
            if (_count == 0)
            {
                return new RenderBaselineCpuStatistics(Capacity, 0, DiscardedSamples,
                    AverageMilliseconds: null, P99Milliseconds: null);
            }

            var samples = new double[_count];
            int oldest = (_next - _count + _milliseconds.Length) % _milliseconds.Length;
            double sum = 0;
            for (int i = 0; i < _count; i++)
            {
                double value = _milliseconds[(oldest + i) % _milliseconds.Length];
                samples[i] = value;
                sum += value;
            }
            Array.Sort(samples);
            int p99Index = Math.Max(0, (int)Math.Ceiling(samples.Length * 0.99) - 1);
            return new RenderBaselineCpuStatistics(Capacity, _count, DiscardedSamples,
                Round(sum / samples.Length), Round(samples[p99Index]));
        }

        private static double Round(double value)
            => Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }

    public sealed record RenderBaselineCpuStatistics(
        int Capacity,
        int SampleCount,
        long DiscardedSamples,
        double? AverageMilliseconds,
        double? P99Milliseconds);

    /// <summary>
    /// Optional backend measurements. Null values remain explicit in JSON and
    /// must carry a status explaining why the device metric was unavailable.
    /// </summary>
    public sealed record RenderBaselineDeviceMetrics(
        double? GpuFrameMilliseconds,
        string GpuFrameTimeStatus,
        long? GpuMemoryBytes,
        string GpuMemoryStatus,
        int? EffectiveMsaaSamples,
        int? EffectiveAnisotropySamples)
    {
        public static RenderBaselineDeviceMetrics Unavailable { get; } = new(
            GpuFrameMilliseconds: null,
            GpuFrameTimeStatus: "unavailable: backend timing queries are not exposed",
            GpuMemoryBytes: null,
            GpuMemoryStatus: "unavailable: backend memory budgeting is not exposed",
            EffectiveMsaaSamples: null,
            EffectiveAnisotropySamples: null);

        public static RenderBaselineDeviceMetrics FromTelemetry(
            RenderTelemetrySnapshot telemetry)
            => new(telemetry.GpuFrameMilliseconds,
                telemetry.GpuFrameTimeStatus,
                telemetry.GpuMemoryBytes,
                telemetry.GpuMemoryStatus,
                EffectiveMsaaSamples: null,
                EffectiveAnisotropySamples: null);
    }

    public sealed record RenderBaselineOutput(
        int DrawableWidth,
        int DrawableHeight,
        int SceneWidth,
        int SceneHeight,
        int ConfiguredRenderScalePercent);

    public sealed record RenderBaselineQuality(
        GraphicsPreset Preset,
        bool CelShading,
        bool Bloom,
        bool DynamicVisualLights,
        int VisualLightCount,
        int RequestedMsaaSamples,
        int RequestedAnisotropySamples,
        int? EffectiveMsaaSamples,
        int? EffectiveAnisotropySamples);

    /// <summary>
    /// Counts derived from the sealed frontend frame. Planned stream draws and
    /// triangles include repeated visits through the six world passes, but do
    /// not pretend to include backend post-process, bloom, or overlay draws.
    /// </summary>
    public sealed record RenderBaselineGeometry(
        int WorldSubmissionCount,
        int HudSceneSubmissionCount,
        int ResolvedSubmissionCount,
        int UnresolvedSubmissionCount,
        long LogicalTriangleCount,
        long PlannedWorldStreamDrawCount,
        long PlannedWorldTriangleCount,
        long? GpuDrawCallCount,
        string GpuDrawCallStatus);

    public sealed record RenderBaselineCapture(
        int SchemaVersion,
        string Scenario,
        string? Screenshot,
        RenderBackendInfo Backend,
        RenderBaselineOutput Output,
        RenderBaselineQuality Quality,
        RenderBaselineCpuStatistics CpuFrameTime,
        RenderBaselineDeviceMetrics Device,
        RenderBaselineGeometry Geometry)
    {
        private static readonly JsonSerializerOptions _jsonOptions = CreateJsonOptions();

        /// <summary>
        /// Optional backend counters are retained verbatim. Null GPU time and
        /// memory remain explicit because SDL does not expose those metrics.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public RenderTelemetrySnapshot? Telemetry { get; init; }

        public string ToJson()
            => JsonSerializer.Serialize(this, _jsonOptions) + "\n";

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
            return options;
        }
    }

    /// <summary>
    /// Materializes a deterministic VE0 metadata artifact from one sealed
    /// backend-neutral frame and a bounded timing sample window.
    /// </summary>
    public static class RenderBaselineMeasurement
    {
        public const int SchemaVersion = 1;

        public static RenderBaselineCapture Capture(RenderFrame frame, string scenario,
            int configuredRenderScalePercent, RenderBackendInfo backend,
            RenderBaselineCpuWindow cpuFrameTimes, string? screenshot = null,
            RenderBaselineDeviceMetrics? device = null,
            RenderTelemetrySnapshot? telemetry = null)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (!frame.IsSealed)
                throw new InvalidOperationException("Renderer baseline metadata requires a sealed frame.");
            if (string.IsNullOrWhiteSpace(scenario))
                throw new ArgumentException("A stable scenario identifier is required.", nameof(scenario));
            if (configuredRenderScalePercent is < 1 or > 100)
                throw new ArgumentOutOfRangeException(nameof(configuredRenderScalePercent));
            if (cpuFrameTimes == null) throw new ArgumentNullException(nameof(cpuFrameTimes));

            RenderBaselineDeviceMetrics resolvedDevice
                = device ?? (telemetry is RenderTelemetrySnapshot sample
                    ? RenderBaselineDeviceMetrics.FromTelemetry(sample)
                    : RenderBaselineDeviceMetrics.Unavailable);
            ValidateDeviceMetrics(resolvedDevice);

            RenderQualitySnapshot quality = frame.Options.Quality;
            return new RenderBaselineCapture(
                SchemaVersion,
                scenario.Trim(),
                screenshot,
                backend,
                new RenderBaselineOutput(
                    frame.DrawableSize.X,
                    frame.DrawableSize.Y,
                    frame.SceneTargetSize.X,
                    frame.SceneTargetSize.Y,
                    configuredRenderScalePercent),
                new RenderBaselineQuality(
                    quality.GraphicsPreset,
                    frame.Options.CelShading,
                    quality.Bloom,
                    quality.DynamicVisualLights,
                    frame.VisualLights.Count,
                    quality.MsaaSampleCount,
                    quality.AnisotropySamples,
                    resolvedDevice.EffectiveMsaaSamples,
                    resolvedDevice.EffectiveAnisotropySamples),
                cpuFrameTimes.Snapshot(),
                resolvedDevice,
                MeasureGeometry(frame, telemetry))
            {
                Telemetry = telemetry
            };
        }

        private static RenderBaselineGeometry MeasureGeometry(RenderFrame frame,
            RenderTelemetrySnapshot? telemetry)
        {
            int resolved = 0;
            int unresolved = 0;
            long logicalTriangles = 0;
            long plannedDraws = 0;
            long plannedTriangles = 0;

            for (int i = 0; i < frame.Submissions.Count; i++)
            {
                DrawSubmission submission = frame.Submissions[i];
                if (TryResolve(frame, submission, out CpuMesh mesh))
                {
                    resolved++;
                    logicalTriangles = checked(logicalTriangles + mesh.TriangleIndexCount / 3);
                }
                else unresolved++;
            }

            for (int i = 0; i < frame.HudSceneItems.Count; i++)
            {
                RenderHudSceneSubmission submission = frame.HudSceneItems[i];
                if (TryResolve(frame, submission, out CpuMesh mesh))
                {
                    resolved++;
                    logicalTriangles = checked(logicalTriangles + mesh.TriangleIndexCount / 3);
                }
                else unresolved++;
            }

            foreach (RenderWorldPass pass in RenderWorldPlan.Passes)
            {
                System.Collections.Generic.IReadOnlyList<DrawSubmission> submissions
                    = RenderWorldPlan.GetSubmissions(frame, pass.Kind);
                for (int i = 0; i < submissions.Count; i++)
                {
                    DrawSubmission submission = submissions[i];
                    if (!TryResolve(frame, submission, out CpuMesh mesh)) continue;
                    RenderMeshStreams streams = RenderWorldPlan.GetMeshStreams(
                        submission, frame.Options);
                    if ((streams & RenderMeshStreams.Triangles) != 0
                        && mesh.TriangleIndexCount > 0)
                    {
                        plannedDraws++;
                        plannedTriangles = checked(plannedTriangles
                            + mesh.TriangleIndexCount / 3);
                    }
                    if ((streams & RenderMeshStreams.Lines) != 0
                        && mesh.LineIndexCount > 0)
                    {
                        plannedDraws++;
                    }
                }
            }

            int? gpuDrawCallCount = telemetry is RenderTelemetrySnapshot sample
                && sample.Encoded ? sample.DrawCallCount : null;
            string gpuDrawCallStatus = telemetry is not RenderTelemetrySnapshot
                ? "unavailable: backend post-process and overlay draw counters are not exposed"
                : telemetry.Value.Encoded
                    ? "measured: SDL draw API calls encoded for this frame"
                    : "unavailable: SDL frame was not encoded";

            return new RenderBaselineGeometry(
                frame.Submissions.Count,
                frame.HudSceneItems.Count,
                resolved,
                unresolved,
                logicalTriangles,
                plannedDraws,
                plannedTriangles,
                GpuDrawCallCount: gpuDrawCallCount,
                GpuDrawCallStatus: gpuDrawCallStatus);
        }

        private static bool TryResolve(RenderFrame frame, DrawSubmission submission,
            out CpuMesh mesh)
        {
            if (submission.Primitive == RenderPrimitive.Mesh
                && submission.GeometryIdentity == null)
            {
                mesh = null!;
                return false;
            }
            object identity = submission.Primitive == RenderPrimitive.Mesh
                ? submission.GeometryIdentity!
                : submission;
            if (frame.MeshResources.TryGetValue(identity, out CpuMesh? resolved)
                && resolved != null)
            {
                mesh = resolved;
                return true;
            }
            mesh = null!;
            return false;
        }

        private static bool TryResolve(RenderFrame frame, RenderHudSceneSubmission submission,
            out CpuMesh mesh)
        {
            if (submission.InlineMesh != null)
            {
                mesh = submission.InlineMesh;
                return true;
            }
            if (submission.GeometryIdentity != null
                && frame.MeshResources.TryGetValue(submission.GeometryIdentity, out CpuMesh? resolved)
                && resolved != null)
            {
                mesh = resolved;
                return true;
            }
            mesh = null!;
            return false;
        }

        private static void ValidateDeviceMetrics(RenderBaselineDeviceMetrics metrics)
        {
            if (metrics.GpuFrameMilliseconds is double gpuMs
                && (!double.IsFinite(gpuMs) || gpuMs < 0))
                throw new ArgumentOutOfRangeException(nameof(metrics));
            if (metrics.GpuMemoryBytes is long bytes && bytes < 0)
                throw new ArgumentOutOfRangeException(nameof(metrics));
            if (string.IsNullOrWhiteSpace(metrics.GpuFrameTimeStatus)
                || string.IsNullOrWhiteSpace(metrics.GpuMemoryStatus))
                throw new ArgumentException("Device metric status fields are required.", nameof(metrics));
            if (metrics.EffectiveMsaaSamples is int msaa && msaa < 1)
                throw new ArgumentOutOfRangeException(nameof(metrics));
            if (metrics.EffectiveAnisotropySamples is int anisotropy && anisotropy < 1)
                throw new ArgumentOutOfRangeException(nameof(metrics));
        }
    }
}
