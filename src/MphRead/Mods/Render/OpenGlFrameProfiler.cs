#if !MPHREAD_SERVER && !ANDROID
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>
/// Optional desktop presentation telemetry. Render CPU, SwapBuffers wall
/// time, and displayed frame intervals are different quantities; each gets
/// a separate percentile distribution. In particular, a blocking vsync swap
/// is NOT GPU execution time. This never changes the presentation clock.
/// </summary>
internal static class OpenGlFrameProfiler
{
    private const int Window = 240;
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), a =>
            a.Equals("-glprofile", StringComparison.OrdinalIgnoreCase));
    private static readonly double[] _renderMs = new double[Window];
    private static readonly double[] _presentMs = new double[Window];
    private static readonly double[] _intervalMs = new double[Window];
    private static int _count;
    private static long _renderStart, _presentStart, _previousPresentEnd;
    private static double _renderElapsed;
    private static bool _renderActive, _presentActive;
    private static MphRead.Scene? _measuredScene;
    private static WeakReference<MphRead.Scene>? _previousScene;
    private static string _vsync = "unknown";
    private static bool _renderComplete;
    private static int _window;

    // Capture is a separate opt-in mode of this profiler: legacy 240-frame
    // streams remain schema 1. Capture mode suppresses their in-loop logging.
    private static readonly string? _capturePath = Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FRAME_CAPTURE");
    private static OpenGlFrameCapture? _capture = string.IsNullOrWhiteSpace(_capturePath) ? null : new(CaptureCapacity());
    private static object? _captureContext;
    private static long _simulationStart, _inputStart, _pollStart, _allocatedStart;
    private static double _simulationMs, _pollMs;
    private static int _gen0, _gen1, _gen2, _simulationSteps, _captureCap;
    private static bool _captureFrameActive;
    private static long _captureGeneration;
    private static bool _captureFocused;
    internal static bool Capturing => _capture != null;
    internal static bool Enabled => _enabled || Capturing;
    private static int CaptureCapacity() => int.TryParse(Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FRAME_CAPACITY"), out int n)
        ? Math.Clamp(n, 1, OpenGlFrameCapture.MaximumCapacity) : OpenGlFrameCapture.DefaultCapacity;

    internal static void BeginFrame(MphRead.Scene scene, bool focused)
    {
        if (_capture == null) return;
        if (_captureGeneration != FrameTiming.PresentationGeneration || _captureFocused != focused
            || _captureCap != FrameTiming.FrameRateCap) CancelFrame();
        _captureGeneration = FrameTiming.PresentationGeneration;
        _captureFocused = focused;
        _captureCap = FrameTiming.FrameRateCap;
        if (!focused || scene.FrameCount < 240) { CancelFrame(); return; }
        _allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        _gen0 = GC.CollectionCount(0); _gen1 = GC.CollectionCount(1); _gen2 = GC.CollectionCount(2);
        _simulationMs = 0; _simulationSteps = 0; _pollMs = 0; _inputStart = 0;
        _captureFrameActive = true;
    }
    internal static void BeginInputPoll()
    {
        if (!_captureFrameActive) return;
        _pollStart = _inputStart = Stopwatch.GetTimestamp();
    }
    internal static void EndInputPoll()
    {
        if (_captureFrameActive) _pollMs += ElapsedMs(_pollStart, Stopwatch.GetTimestamp());
    }
    internal static void BeginSimulation(int steps)
    {
        if (!_captureFrameActive) return;
        _simulationSteps = steps;
        _simulationStart = Stopwatch.GetTimestamp();
    }
    internal static void EndSimulation()
    {
        if (_captureFrameActive) _simulationMs += ElapsedMs(_simulationStart, Stopwatch.GetTimestamp());
    }
    internal static void StopCapture(string reason)
    {
        var capture = _capture;
        _capture = null; _captureFrameActive = false;
        if (capture == null) return;
        // EndScene/OnUnload is outside the measured loop. Never overwrite a receipt.
        try { capture.Write(_capturePath!, _captureContext, Stopwatch.Frequency, reason); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Console.Error.WriteLine("[glframe-capture] export failed: " + ex.Message); }
        _captureContext = null;
        CancelFrame();
    }


    internal static void ResetContext()
    {
        CancelFrame();
        _previousScene = null;
        _window = 0;
    }

    internal static double OnePercentLowFromP99Milliseconds(double p99Ms)
        => p99Ms > 0 && double.IsFinite(p99Ms) ? 1000.0 / p99Ms : 0.0;

    internal static void BeginRender(MphRead.Scene scene, string vsync = "unknown")
    {
        if (!Enabled) return;
        if (_previousScene != null && (!_previousScene.TryGetTarget(out var priorScene) || !ReferenceEquals(priorScene, scene))) CancelFrame();
        if (_previousScene == null) _previousScene = new(scene);
        else _previousScene.SetTarget(scene);
        _measuredScene = scene;
        if (_capture != null && _captureContext == null)
        {
            var identity = Mods.Diagnostics.RuntimeApplicationImage.Capture(typeof(MphRead.Scene).Assembly);
            _captureContext = new
            {
                assemblyMvid = typeof(MphRead.Scene).Module.ModuleVersionId,
                assemblySha256 = identity.Sha256, imageKind = identity.Kind,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                machine = Environment.MachineName,
                vendor = GraphicsApi.GetString(StringName.Vendor), renderer = GraphicsApi.GetString(StringName.Renderer),
                glVersion = GraphicsApi.GetString(StringName.Version), room = scene.Room?.Meta.Name,
                width = scene.Size.X, height = scene.Size.Y, cap = FrameTiming.FrameRateCap,
                vsync, renderScale = RenderOptions.ResolutionScale, shadows = RenderOptions.Shadows.ToString(),
                gpuTiming = "UNVERIFIED", lowLatencyProvider = "none; null provider",
                warmupSimulationTicks = 240,
                profiling = _enabled,
                flags = CaptureFlags()
            };
        }
        _vsync = vsync;
        _renderComplete = false;
        // Scene/window transitions may leave long idle intervals between
        // real gameplay presentations. Never include that idle time in the
        // next 240-frame percentile window.
        long now = Stopwatch.GetTimestamp();
        if (_previousPresentEnd != 0
            && now - _previousPresentEnd > Stopwatch.Frequency / 2)
        {
            _previousPresentEnd = 0;
            _count = 0;
            OpenGlPassProfiler.CancelFrame();
        }
        OpenGlPassProfiler.BeginFrame();
        _renderStart = now;
        _renderActive = true;
        _presentActive = false;
    }

    internal static void CancelFrame()
    {
        _capture?.Cancel();
        _captureFrameActive = false;
        _renderActive = false;
        _renderComplete = false;
        _presentActive = false;
        _measuredScene = null;
        _previousPresentEnd = 0;
        _count = 0;
        OpenGlPassProfiler.CancelFrame();
    }

    internal static void EndRender()
    {
        if (!_renderActive) return;
        _renderElapsed = ElapsedMs(_renderStart, Stopwatch.GetTimestamp());
        if (!double.IsFinite(_renderElapsed)) { CancelFrame(); return; }
        _renderActive = false;
        _renderComplete = true;
    }

    internal static void BeginPresent()
    {
        if (!Enabled || !_renderComplete) return;
        _presentStart = Stopwatch.GetTimestamp();
        _presentActive = true;
    }

    internal static void EndPresent()
    {
        if (!_presentActive) return;
        long end = Stopwatch.GetTimestamp();
        _presentActive = false;
        if (end <= _presentStart || (_previousPresentEnd != 0 && end <= _previousPresentEnd))
        { CancelFrame(); return; }
        if (_capture != null && _captureFrameActive && _previousPresentEnd != 0
            && _captureGeneration == FrameTiming.PresentationGeneration)
        {
            double interval = ElapsedMs(_previousPresentEnd, end);
            double swap = ElapsedMs(_presentStart, end);
            double Pass(OpenGlCpuPass pass) => OpenGlPassProfiler.FrameMilliseconds(pass);
            double ui = Pass(OpenGlCpuPass.Ui), upload = Pass(OpenGlCpuPass.TextureUpload);
            int gc0 = GC.CollectionCount(0) - _gen0, gc1 = GC.CollectionCount(1) - _gen1, gc2 = GC.CollectionCount(2) - _gen2;
            var sample = new OpenGlFrameSample(end, _capture.Segment, _captureGeneration,
                _measuredScene?.FrameCount ?? 0, interval, _simulationMs, _pollMs, _renderElapsed,
                ui, swap, Math.Max(0, interval - _simulationMs - _renderElapsed - swap),
                _inputStart > 0 ? ElapsedMs(_inputStart, end) : null,
                Pass(OpenGlCpuPass.Preparation), Pass(OpenGlCpuPass.DrawItems), Pass(OpenGlCpuPass.World),
                Pass(OpenGlCpuPass.ItemSetup), Pass(OpenGlCpuPass.GeometrySubmission), Pass(OpenGlCpuPass.Shadow),
                Pass(OpenGlCpuPass.Hud), upload, Pass(OpenGlCpuPass.Composite),
                OpenGlPassProfiler.FrameCounter(OpenGlFrameCounter.TextureUploadBytes)
                    + OpenGlPassProfiler.FrameCounter(OpenGlFrameCounter.UploadBytes),
                OpenGlPassProfiler.FrameCounter(OpenGlFrameCounter.BufferOrphans),
                Math.Max(0, GC.GetAllocatedBytesForCurrentThread() - _allocatedStart), gc0, gc1, gc2,
                _simulationSteps, FrameTiming.DroppedSteps, FrameTiming.FrameRateCap, _vsync,
                OpenGlFrameCapture.Attribute(interval, _simulationMs, _renderElapsed, ui, swap, upload, gc0 + gc1 + gc2 > 0));
            _capture.Add(in sample);
        }
        if (_enabled && _capture == null && _previousPresentEnd != 0)
        {
            // Sample interval includes scheduled frame pacing, simulation,
            // rendering and presentation. CPU render work and swap duration
            // are each reported separately so vsync stalls are identifiable.
            _renderMs[_count] = _renderElapsed;
            _presentMs[_count] = ElapsedMs(_presentStart, end);
            _intervalMs[_count] = ElapsedMs(_previousPresentEnd, end);
            if (++_count == Window)
            {
                double renderP95 = P(_renderMs, 95);
                double swapP95 = P(_presentMs, 95);
                double intervalP99 = P(_intervalMs, 99);
                Console.WriteLine("[glframe]"
                    + $" renderCPU p50/p95/p99={P(_renderMs,50):F3}/{renderP95:F3}/{P(_renderMs,99):F3}ms"
                    + $" presentWall p50/p95/p99={P(_presentMs,50):F3}/{swapP95:F3}/{P(_presentMs,99):F3}ms"
                    + $" frameInterval p50/p95/p99={P(_intervalMs,50):F3}/{P(_intervalMs,95):F3}/{intervalP99:F3}ms"
                    + $" interval-derived-1pct-low~={OnePercentLowFromP99Milliseconds(intervalP99):F1}fps"
                    + $" simHz={FrameTiming.MeasuredSimulationHz:F1}"
                    + $" drawHz={FrameTiming.MeasuredFrameHz:F1}"
                    + $" droppedSimSteps={FrameTiming.DroppedSteps}"
                    + $" stalls={FrameTiming.Stalls}");
                // Stable machine-readable fields; a period captures exactly
                // 240 *completed presentations* rather than an FPS estimate.
                // Do not treat presentWall as GPU execution time.
                Console.WriteLine("[glframe-json] " + JsonSerializer.Serialize(new
                {
                    schema = 1,
                    samples = Window,
                    window = ++_window,
                    warmup = _window == 1,
                    renderCpuP50Ms = P(_renderMs, 50),
                    presentWallP50Ms = P(_presentMs, 50),
                    presentWallP95Ms = P(_presentMs, 95),
                    frameIntervalP50Ms = P(_intervalMs, 50),
                    renderCpuP95Ms = renderP95,
                    renderCpuP99Ms = P(_renderMs, 99),
                    presentWallP99Ms = P(_presentMs, 99),
                    frameIntervalP95Ms = P(_intervalMs, 95),
                    frameIntervalP99Ms = intervalP99,
                    onePercentLowEstimateFps = OnePercentLowFromP99Milliseconds(intervalP99),
                    simulationHz = FrameTiming.MeasuredSimulationHz,
                    displayedHz = FrameTiming.MeasuredFrameHz,
                    droppedSimulationSteps = FrameTiming.DroppedSteps,
                    stalls = FrameTiming.Stalls,
                    // The hardware A/B validator must prove which branch
                    // was actually active during each 240-frame capture.
                    animationBlend = AnimationRollout.Enabled("PROJECT_PRIME_PLAYER_ANIMATION_BLEND"),
                    animationChannels = new {
                        hunter = AnimationRollout.Enabled("PROJECT_PRIME_PLAYER_ANIMATION_BLEND"),
                        boss = AnimationRollout.Enabled("PROJECT_PRIME_BOSS_ANIMATION_BLEND"),
                        preview = AnimationRollout.Enabled("PROJECT_PRIME_PREVIEW_ANIMATION_BLEND"),
                        material = AnimationRollout.Enabled("PROJECT_PRIME_MATERIAL_ANIMATION_BLEND"),
                        texcoord = AnimationRollout.Enabled("PROJECT_PRIME_TEXCOORD_ANIMATION_BLEND"),
                        world = AnimationRollout.Enabled("PROJECT_PRIME_WORLD_ANIMATION_BLEND")
                    },
                    vbo = DesktopRetainedGeometry.Enabled,
                    bindingCache = OpenGlScopedBindingCache.Enabled,
                    batching = DesktopRetainedGeometry.BatchEnabled,
                    context = new
                    {
                        machine = Environment.MachineName,
                        os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                        vendor = GraphicsApi.GetString(StringName.Vendor) ?? "",
                        playerCount = MphRead.Entities.PlayerEntity.PlayerCount,
                        vsync = _vsync,
                        vertexArrayApi = OpenGlContextPolicy.VaoApi.ToString(),
                        flags = new { palettePrep = OpenGlOptimizationPolicy.Enabled("PALETTE_PREP"), shadowGroups = OpenGlOptimizationPolicy.Enabled("SHADOW_GROUPS"), unlitShader = OpenGlOptimizationPolicy.Enabled("UNLIT_SHADER"), shadowPrep = OpenGlOptimizationPolicy.Enabled("SHADOW_PREP"), roomIdentity = OpenGlOptimizationPolicy.Enabled("ROOM_IDENTITY"), meshObject = OpenGlOptimizationPolicy.Enabled("MESH_OBJECT"), roomGroups = OpenGlOptimizationPolicy.Enabled("ROOM_GROUPS"), plainShader = OpenGlOptimizationPolicy.Enabled("PLAIN_SHADER"), shaderVariants = OpenGlOptimizationPolicy.Enabled("SHADER_VARIANTS"), depthKeep = OpenGlOptimizationPolicy.Enabled("DEPTH_KEEP"), proceduralBatch = OpenGlOptimizationPolicy.Enabled("PROCEDURAL_BATCH"), proceduralStream = OpenGlOptimizationPolicy.Enabled("PROCEDURAL_STREAM"), roomCache = OpenGlOptimizationPolicy.Enabled("ROOM_CACHE"), depthVertex = OpenGlOptimizationPolicy.Enabled("DEPTH_VERTEX"), depthPrep = OpenGlOptimizationPolicy.Enabled("DEPTH_PREP"), hudSprites = OpenGlOptimizationPolicy.Enabled("HUD_SPRITES"), hudGlyphs = OpenGlOptimizationPolicy.Enabled("HUD_GLYPHS"), glesUniforms = OpenGlOptimizationPolicy.Enabled("GLES_UNIFORMS"), depthBatch = OpenGlOptimizationPolicy.Enabled("DEPTH_BATCH"), depthShader = OpenGlOptimizationPolicy.Enabled("DEPTH_SHADER"), atlasEviction = OpenGlOptimizationPolicy.Enabled("ATLAS_EVICTION"), hudDiscard = OpenGlOptimizationPolicy.Enabled("HUD_DISCARD"), dynamicStream = OpenGlOptimizationPolicy.Enabled("DYNAMIC_STREAM"), hudSamplers = HudSamplerCache.Enabled, vbo = DesktopRetainedGeometry.Enabled, bindingCache = OpenGlScopedBindingCache.Enabled, batching = DesktopRetainedGeometry.BatchEnabled, vao = OpenGlContextPolicy.Vao, uniformRevisions = MatrixPaletteRevision.Enabled, staticCache = OpenGlOptimizationPolicy.Enabled("STATIC_CACHE"), shadowFast = OpenGlOptimizationPolicy.Enabled("SHADOW_FAST") },
                        room = _measuredScene?.Room?.Meta.Name ?? "",
                        width = _measuredScene?.Size.X ?? 0,
                        height = _measuredScene?.Size.Y ?? 0,
                        shadows = RenderOptions.Shadows.ToString(),
                        renderScale = RenderOptions.ResolutionScale,
                        cap = FrameTiming.FrameRateCap,
                        renderer = GraphicsApi.GetString(StringName.Renderer) ?? "",
                        glVersion = GraphicsApi.GetString(StringName.Version) ?? "",
                        assemblyMvid = typeof(MphRead.Scene).Module.ModuleVersionId.ToString("D")
                    }
                }));
                _count = 0;
            }
        }
        if (_previousPresentEnd != 0) OpenGlPassProfiler.EndFrame();
        else OpenGlPassProfiler.CancelFrame();
        _captureFrameActive = false;
        _renderComplete = false;
        _previousPresentEnd = end;
        // Profiler must not extend the lifetime of a scene after returning
        // to launcher/Studio or switching maps.
        _measuredScene = null;
    }

    private static System.Collections.Generic.Dictionary<string, string?> CaptureFlags()
    {
        var flags = new System.Collections.Generic.Dictionary<string, string?>();
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            if (entry.Key is string key && (key.StartsWith("PROJECT_PRIME_GL_", StringComparison.Ordinal)
                || key.EndsWith("_ANIMATION_BLEND", StringComparison.Ordinal))) flags[key] = entry.Value?.ToString();
        return flags;
    }

    private static double ElapsedMs(long begin, long end)
        => OpenGlFrameCapture.ElapsedMs(begin, end, Stopwatch.Frequency);

    private static double P(double[] source, int percentile)
    {
        double[] values = (double[])source.Clone();
        Array.Sort(values);
        return values[OpenGlSubmissionProfiler.PercentileIndex(values.Length, percentile)];
    }
}
#endif
