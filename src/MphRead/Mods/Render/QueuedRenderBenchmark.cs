#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using MphRead.Mods.Input;

namespace MphRead.Mods.Render;

// Exercises normal queued rendering. Unlike ModernRenderBenchmark, this does
// not call Finish/WaitIdle/read pixels once per frame or freeze simulation.
internal static class QueuedRenderBenchmark
{
    private readonly record struct FrameSample(long FrameId, double StartMs, double FrameIntervalMs,
        int SimulationSteps, double SimulationMs, double DrawCpuMs, FrameRenderTelemetry.Sample CpuPhases,
        double PresentBoundaryMs, double? NativeAcquireMs, double? NativeSubmitMs, double? NativePresentMs,
        double SoftwareWaitMs, int? DrawCalls, int? RenderPasses, int? PipelineCreations,
        double? PipelineCreationMs, long? UploadedBytes, long? AtlasLiveBytes, long? AtlasReservedBytes);

    internal static int Run(string room, string output, int sampleCount = 30000,
        int width = 1920, int height = 1080, int scale = 100, string cap = "display")
    {
        try
        {
            if (sampleCount < 30 || sampleCount > 100000)
                throw new ArgumentOutOfRangeException(nameof(sampleCount), "Use 30–100000 samples; 30000 is the default tail-pacing run.");
            if (width < 16 || height < 16 || width > 16384 || height > 16384 || scale < 25 || scale > 100)
                throw new ArgumentOutOfRangeException(nameof(width), "Use dimensions 16–16384 and scale 25–100.");
            int frameCap = FrameTiming.ParseCap(cap, int.MinValue);
            if (frameCap == int.MinValue)
                throw new ArgumentException("FPS must be display, unlimited, or a supported numeric cap.", nameof(cap));
            GraphicsTimingPolicy.Enable(); // must precede optional device-feature negotiation
            var requestedBackend = GraphicsBackendPolicy.Requested;
            var settings = DesktopGlContext.Settings(background: false);
            settings.ClientSize = new Vector2i(width, height);
            settings.Title = "Project Prime queued renderer benchmark";
            using var window = new NativeWindow(settings);
            using var graphics = new DesktopGraphicsSession(window);
            bool modern = ModernGraphicsCompat.Active;
            int deviceGeneration = ModernGraphicsCompat.DeviceGeneration;
            var identity = modern ? ModernGraphicsCompat.DeviceIdentity
                : (GraphicsBackend.OpenGL, GraphicsApi.GetString(OpenTK.Graphics.OpenGL.StringName.Renderer),
                    GraphicsApi.GetString(OpenTK.Graphics.OpenGL.StringName.Version));
            bool requestedVSync = frameCap == FrameTiming.DisplayRate;
            if (modern) ModernGraphicsCompat.SetVSync(requestedVSync);
            else window.Context.SwapInterval = requestedVSync ? 1 : 0;
            double refresh = MonitorRefreshRate(window);
            string presentMode = modern ? ModernGraphicsCompat.ActivePresentMode : $"SwapInterval({(requestedVSync ? 1 : 0)})";
            FrameTiming.FrameRateCap = frameCap;
            bool presentationPaced = requestedVSync || (modern && ModernGraphicsCompat.PresentationBlocks);
            double selectedCadence = QueuedFramePacing.SelectedCadenceHz(frameCap, refresh, presentationPaced);
            FrameTiming.SetPresentationCadence(refresh, presentationPaced, numericBudget: true);
            RenderOptions.ApplyGraphicsPreset(GraphicsPreset.Extreme);
            RenderOptions.ResolutionScale = scale;
            var frames = new List<FrameSample>(sampleCount);
            var gpuFrames = new List<ModernGraphicsCompat.GpuFrameSample>(sampleCount);
            var scene = new Scene(window.FramebufferSize, SyntheticInput.CreateKeyboard(), SyntheticInput.CreateMouse(), _ => { }, () => { });
            ModernGraphicsCompat.PerformanceSample? startup = null, memory = null;
            double sceneLoadMs;
            long totalStart = Stopwatch.GetTimestamp();
            const int warmupFrames = 180;
            long droppedStart = 0, failedStart = 0;
            bool gpuTimingAvailable = false;
            try
            {
                if (modern) ModernGraphicsCompat.BeginPerformanceSample();
                long loadStart = Stopwatch.GetTimestamp();
                scene.AddPlayer(Hunter.Samus);
                scene.AddRoom(room, GameMode.Battle, playerCount: 1);
                scene.OnLoad();
                GameState.MatchTime = -1;
                sceneLoadMs = Stopwatch.GetElapsedTime(loadStart).TotalMilliseconds;
                if (modern) startup = ModernGraphicsCompat.EndPerformanceSample();
                FrameTiming.Reset();
                long previousStart = Stopwatch.GetTimestamp();
                for (int i = 0; i < warmupFrames + sampleCount; i++)
                {
                    long frameStart = Stopwatch.GetTimestamp();
                    double elapsedSeconds = Stopwatch.GetElapsedTime(previousStart, frameStart).TotalSeconds;
                    previousStart = frameStart;
                    NativeWindow.ProcessWindowEvents(false);
                    if (!window.Exists || window.IsExiting)
                        throw new OperationCanceledException("Benchmark window closed before the requested sample count.");
                    DesktopGraphicsSession.Resize(window);
                    if (modern && ModernGraphicsCompat.DeviceGeneration != deviceGeneration)
                        throw new InvalidOperationException("Graphics device was reconstructed during the benchmark; restart to collect one comparable device generation.");
                    if (scene.Size != window.FramebufferSize)
                    { scene.Size = window.FramebufferSize; scene.OnResize(); }
                    if (scene.Size.X <= 0 || scene.Size.Y <= 0)
                        throw new OperationCanceledException("Benchmark surface minimized; restart with a visible surface for comparable data.");
                    if (i == warmupFrames)
                    {
                        droppedStart = ModernGraphicsCompat.GpuTimingDroppedSamples;
                        failedStart = ModernGraphicsCompat.GpuTimingFailedSamples;
                        gpuTimingAvailable = modern && ModernGraphicsCompat.GpuTimingAvailable;
                    }
                    if (modern) ModernGraphicsCompat.BeginPerformanceSample();
                    FrameRenderTelemetry.Begin();
                    long simulationStart = Stopwatch.GetTimestamp();
                    int steps = FrameTiming.Advance(elapsedSeconds);
                    for (int step = 0; step < steps; step++) scene.OnSimulationFrame();
                    double simulationMs = Stopwatch.GetElapsedTime(simulationStart).TotalMilliseconds;
                    if (modern) ModernGraphicsCompat.BeginGpuFrameTiming(i);
                    long drawStart = Stopwatch.GetTimestamp();
                    scene.OnDrawFrame();
                    if (!scene.OnRenderFrame()) throw new InvalidOperationException("Scene stopped during benchmark.");
                    double drawMs = Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
                    var cpu = FrameRenderTelemetry.End();
                    long presentStart = Stopwatch.GetTimestamp();
                    DesktopGraphicsSession.Present(window);
                    if (modern && !ModernGraphicsCompat.CanReleaseNativeResources)
                        throw new InvalidOperationException("Graphics device failed during benchmark submission or presentation; timings are not comparable.");
                    double presentMs = Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
                    scene.AfterRenderFrame();
                    long waitStart = Stopwatch.GetTimestamp();
                    WaitForFrameBudget(frameStart, frameCap);
                    double softwareWaitMs = Stopwatch.GetElapsedTime(waitStart).TotalMilliseconds;
                    ModernGraphicsCompat.PerformanceSample? counters = modern
                        ? ModernGraphicsCompat.EndPerformanceSample(includeResourceStorage: false) : null;
                    if (i >= warmupFrames)
                        frames.Add(new(i, Stopwatch.GetElapsedTime(totalStart, frameStart).TotalMilliseconds,
                            elapsedSeconds * 1000, steps, simulationMs, drawMs, cpu, presentMs,
                            counters?.SurfaceAcquireMs, counters?.QueueSubmitMs, counters?.PresentCallMs,
                            softwareWaitMs, counters?.CoreDraws, counters?.CoreRenderPasses,
                            counters?.PipelinesCreated, counters?.PipelineCreationMs,
                            counters?.TextureUploadBytes, counters?.AtlasLiveBytes, counters?.AtlasReservedBytes));
                    if (modern)
                        while (ModernGraphicsCompat.TryTakeGpuFrameSample(out var gpu))
                            if (gpu.FrameId >= warmupFrames) gpuFrames.Add(gpu);
                }
                // Finish the last timing samples by polling without a GPU wait.
                // Incomplete samples remain explicitly missing; the queue never
                // blocks the measured run or fabricates completion timestamps.
                long drainStart = Stopwatch.GetTimestamp();
                while (modern && gpuFrames.Count < sampleCount && Stopwatch.GetElapsedTime(drainStart).TotalMilliseconds < 250)
                {
                    while (ModernGraphicsCompat.TryTakeGpuFrameSample(out var gpu))
                        if (gpu.FrameId >= warmupFrames) gpuFrames.Add(gpu);
                    Thread.Sleep(1);
                }
                if (modern) { ModernGraphicsCompat.BeginPerformanceSample(); memory = ModernGraphicsCompat.EndPerformanceSample(); }
            }
            finally
            {
                FrameRenderTelemetry.End();
                try { scene.DoCleanup(); }
                finally { scene.ReleaseRenderResources(!modern || ModernGraphicsCompat.CanReleaseNativeResources); }
            }
            long dropped = ModernGraphicsCompat.GpuTimingDroppedSamples - droppedStart;
            long failed = ModernGraphicsCompat.GpuTimingFailedSamples - failedStart;
            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schemaVersion = 1, measurementMode = "queued-live-simulation",
                requestedBackend = requestedBackend.ToString(), actualBackend = identity.Item1.ToString(),
                adapter = identity.Item2, driver = identity.Item3,
                platform = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                room, preset = "Extreme", requestedSize = new { width, height },
                actualFramebuffer = new { width = scene.Size.X, height = scene.Size.Y }, scale,
                requestedCap = cap, actualFrameCap = FrameTiming.CapString(frameCap), requestedVSync,
                actualPresentMode = presentMode, reportedMonitorRefreshHz = refresh, selectedPresentationCadenceHz = selectedCadence,
                warmupFrames, sampleCount, sceneLoadMs, startup, storageEstimate = memory,
                cpuFrameIntervals = Distribution(frames.Select(x => x.FrameIntervalMs), frameIntervals: true),
                cpuDraw = Distribution(frames.Select(x => x.DrawCpuMs)),
                nativeAcquire = modern ? Distribution(frames.Select(x => x.NativeAcquireMs!.Value)) : null,
                nativeSubmit = modern ? Distribution(frames.Select(x => x.NativeSubmitMs!.Value)) : null,
                nativePresent = modern ? Distribution(frames.Select(x => x.NativePresentMs!.Value)) : null,
                gpuTiming = new { available = gpuTimingAvailable, samples = gpuFrames.Count,
                    missing = sampleCount - gpuFrames.Count, dropped, failed, distribution = Distribution(gpuFrames.Select(x => x.Milliseconds)) },
                notes = "CPU intervals are between frame starts, not display scanout timestamps. Only interval statistics report FPS; reciprocal GPU/phase time is not delivered FPS. Tail sample counts are explicit; short smoke runs cannot establish p99.9. PresentBoundary includes submission, promotion and native present; native spans overlap extraction/recording and must not be added to them. RoomVisibility measures portal/room visibility; frustum tests embedded in packet construction remain in Extraction. GPU time spans timestamped frame commands across queued submissions, excluding CPU presentation and preceding queued work. No per-frame GPU completion wait. Readback uses an eight-slot nonblocking ring; unsupported, invalid, dropped and unfinished measurements are missing, never zero. OpenGL-only unavailable native counters are null. Storage estimates are tracked allocation sizes, not residency. SceneLoadMs measures AddPlayer/AddRoom/OnLoad after Scene construction. Numeric software waits consume the remaining whole-frame budget, including present. Simulation is live with fixed 60 Hz steps and no synthetic user motion; choose stress maps/replays for workload coverage. Do not compare with serialized frozen-scene benchmark JSON.",
                frames, gpuFrames
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"QUEUEDRENDERBENCH PASS {frames.Count} CPU / {gpuFrames.Count} GPU samples: {path}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("QUEUEDRENDERBENCH FAIL " + ex); return 1; }
    }

    private static object? Distribution(IEnumerable<double> source, bool frameIntervals = false)
    {
        double[] values = source.Where(double.IsFinite).Order().ToArray();
        if (values.Length == 0) return null;
        double Percentile(double p) => values[Math.Clamp((int)Math.Ceiling(values.Length * p) - 1, 0, values.Length - 1)];
        double mean = values.Average();
        double slow1 = values.Skip((int)(values.Length * .99)).Average();
        double slow01 = values.Skip((int)(values.Length * .999)).Average();
        var summary = new Dictionary<string, object?>
        {
            ["count"] = values.Length, ["averageMs"] = mean, ["medianMs"] = Percentile(.5),
            ["p99Ms"] = Percentile(.99), ["p999Ms"] = Percentile(.999),
            ["onePercentTailSamples"] = values.Length - (int)(values.Length * .99),
            ["pointOnePercentTailSamples"] = values.Length - (int)(values.Length * .999)
        };
        if (frameIntervals)
        {
            summary["averageFps"] = mean > 0 ? 1000 / mean : null;
            summary["onePercentLowFps"] = slow1 > 0 ? 1000 / slow1 : null;
            summary["pointOnePercentLowFps"] = slow01 > 0 ? 1000 / slow01 : null;
        }
        return summary;
    }

    private static void WaitForFrameBudget(long frameStart, int frameCap)
    {
        if (frameCap <= 0) return;
        while (true)
        {
            double ms = QueuedFramePacing.RemainingMilliseconds(frameStart, Stopwatch.GetTimestamp(), Stopwatch.Frequency, frameCap);
            if (ms <= 0) return;
            if (ms >= 2) Thread.Sleep((int)ms - 1);
            else Thread.SpinWait(32);
        }
    }

    private static unsafe double MonitorRefreshRate(NativeWindow window)
    {
        try
        {
            var monitor = Monitors.GetMonitorFromWindow(window).Handle.ToUnsafePtr<OpenTK.Windowing.GraphicsLibraryFramework.Monitor>();
            var mode = GLFW.GetVideoMode(monitor);
            return mode == null ? 0 : mode->RefreshRate;
        }
        catch { return 0; }
    }
}
#endif
