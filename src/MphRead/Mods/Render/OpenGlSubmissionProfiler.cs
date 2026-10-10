using System;
using System.Diagnostics;
using System.Text.Json;

namespace MphRead.Mods.Render;

/// <summary>
/// Opt-in CPU-side OpenGL submission telemetry. This counts facade calls, not
/// driver-side draw calls or GPU execution time. Sampling is isolated to the
/// GL/render thread and makes no glGet* queries or pipeline syncs.
///
/// Use -glprofile or PROJECT_PRIME_GL_PROFILE=1. Every 240 world graphs,
/// print percentile CPU wall time and average GL submission request counts.
/// </summary>
internal static class OpenGlSubmissionProfiler
{
    private const int Window = 240;
    private static readonly bool _enabled =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_PROFILE") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-glprofile", StringComparison.OrdinalIgnoreCase));

    [ThreadStatic] private static bool _active;
    [ThreadStatic] private static long _start;
    [ThreadStatic] private static int _lists;
    [ThreadStatic] private static int _vboDraws;
    [ThreadStatic] private static int _immediate;
    [ThreadStatic] private static int _textureBinds;
    [ThreadStatic] private static int _textureBindsElided;
    [ThreadStatic] private static int _textureUnitsElided;
    [ThreadStatic] private static int _rasterStatesElided;
    [ThreadStatic] private static int _programBinds;
    [ThreadStatic] private static int _framebufferBinds;
    [ThreadStatic] private static int _stateRequests;
    [ThreadStatic] private static int _stateRequestsSkipped;
    [ThreadStatic] private static int _stencilRequests;
    [ThreadStatic] private static long[]? _windowTicks;
    [ThreadStatic] private static int _windowCount;
    [ThreadStatic] private static long _sumLists;
    [ThreadStatic] private static long _sumVboDraws;
    [ThreadStatic] private static long _sumImmediate;
    [ThreadStatic] private static long _sumTextureBinds;
    [ThreadStatic] private static long _sumTextureBindsElided;
    [ThreadStatic] private static long _sumTextureUnitsElided;
    [ThreadStatic] private static long _sumRasterStatesElided;
    [ThreadStatic] private static long _sumProgramBinds;
    [ThreadStatic] private static long _sumFramebufferBinds;
    [ThreadStatic] private static long _sumStencilRequests;
    [ThreadStatic] private static long _sumStateRequests;
    [ThreadStatic] private static long _sumStateRequestsSkipped;
    [ThreadStatic] private static long _sumPackets;
    [ThreadStatic] private static long _sumBatches;
    [ThreadStatic] private static long _sumSavedReplaySubmissions;

    // Reproducible before/after CPU + screenshot comparisons without switching
    // code versions. A mismatch can be diagnosed by forcing the original
    // opaque depth clear/replay within the same executable.
    internal static bool ForceLegacyDepthReplay { get; } =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_DEPTH_REPLAY") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-gllegacydepth", StringComparison.OrdinalIgnoreCase));

    // Same build A/B: `-gllegacystate` or the environment setting disables
    // scoped capability elision without disabling capture, sorting or telemetry.
    internal static bool ForceLegacyStateRequests { get; } =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_FORCE_STATE_REQUESTS") == "1"
        || Array.Exists(Environment.GetCommandLineArgs(), arg =>
            arg.Equals("-gllegacystate", StringComparison.OrdinalIgnoreCase));

    internal static bool Enabled => _enabled;

    internal static bool BeginWorldGraph()
    {
        if (!_enabled || _active)
            return false;
        _lists = _vboDraws = _immediate = _textureBinds = _programBinds = 0;
        _textureBindsElided = _textureUnitsElided = _rasterStatesElided = 0;
        _framebufferBinds = _stateRequests = _stateRequestsSkipped = _stencilRequests = 0;
        _start = Stopwatch.GetTimestamp();
        _active = true;
        return true;
    }

    internal static void NoteDisplayList() { if (_active) _lists++; }
    internal static void NoteVboDraw() { if (_active) _vboDraws++; }
    internal static void NoteImmediateBegin() { if (_active) _immediate++; }
    internal static void NoteTextureBind() { if (_active) _textureBinds++; }
    internal static void NoteTextureBindElided() { if (_active) _textureBindsElided++; }
    internal static void NoteTextureUnitElided() { if (_active) _textureUnitsElided++; }
    internal static void NoteRasterStateElided() { if (_active) _rasterStatesElided++; }
    internal static void NoteProgramBind() { if (_active) _programBinds++; }
    internal static void NoteFramebufferBind() { if (_active) _framebufferBinds++; }
    internal static void NoteStateRequest() { if (_active) _stateRequests++; }
    internal static void NoteStateRequestSkipped() { if (_active) _stateRequestsSkipped++; }
    internal static void NoteStencilFunc() { if (_active) _stencilRequests++; }

    internal static OpenGlWorldSubmissionSample EndWorldGraph(
        int packets, int batches, int savedOpaqueReplaySubmissions)
    {
        // Stop timing before constructing/printing statistics.
        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - _start);
        _active = false;
        var sample = new OpenGlWorldSubmissionSample(
            elapsed * 1000.0 / Stopwatch.Frequency, _lists, _immediate,
            _textureBinds, _programBinds, _framebufferBinds,
            _stateRequests, _stencilRequests, packets, batches,
            savedOpaqueReplaySubmissions);
        _windowTicks ??= new long[Window];
        _windowTicks[_windowCount++] = elapsed;
        _sumLists += _lists;
        _sumVboDraws += _vboDraws;
        _sumImmediate += _immediate;
        _sumTextureBinds += _textureBinds;
        _sumTextureBindsElided += _textureBindsElided;
        _sumTextureUnitsElided += _textureUnitsElided;
        _sumRasterStatesElided += _rasterStatesElided;
        _sumProgramBinds += _programBinds;
        _sumFramebufferBinds += _framebufferBinds;
        _sumStencilRequests += _stencilRequests;
        _sumStateRequests += _stateRequests;
        _sumStateRequestsSkipped += _stateRequestsSkipped;
        _sumPackets += packets;
        _sumBatches += batches;
        _sumSavedReplaySubmissions += savedOpaqueReplaySubmissions;

        if (_windowCount == Window)
        {
            // Fixed-size allocation only on an explicit profiling interval.
            long[] sorted = (long[])_windowTicks.Clone();
            Array.Sort(sorted);
            double tickMs = 1000.0 / Stopwatch.Frequency;
            Console.WriteLine(
                $"[glprofile] depth-mode={(ForceLegacyDepthReplay ? "original" : "optimized")}"
                + $" state-mode={(ForceLegacyStateRequests ? "legacy" : "scoped")}"
                + $" world CPU p50={sorted[PercentileIndex(Window, 50)] * tickMs:F3}ms"
                + $" p95={sorted[PercentileIndex(Window, 95)] * tickMs:F3}ms"
                + $" p99={sorted[PercentileIndex(Window, 99)] * tickMs:F3}ms"
                + $" avg-list={_sumLists / (double)Window:F1}"
                + $" avg-vboDraws={_sumVboDraws / (double)Window:F1}"
                + $" avg-immediate={_sumImmediate / (double)Window:F1}"
                + $" avg-texbind={_sumTextureBinds / (double)Window:F1}"
                + $" avg-texbindElided={_sumTextureBindsElided / (double)Window:F1}"
                + $" avg-texUnitElided={_sumTextureUnitsElided / (double)Window:F1}"
                + $" avg-rasterElided={_sumRasterStatesElided / (double)Window:F1}"
                + $" avg-program={_sumProgramBinds / (double)Window:F1}"
                + $" avg-fbo={_sumFramebufferBinds / (double)Window:F1}"
                + $" avg-stencil={_sumStencilRequests / (double)Window:F1}"
                + $" avg-enableDisable={_sumStateRequests / (double)Window:F1}"
                + $" avg-stateElided={_sumStateRequestsSkipped / (double)Window:F1}"
                + $" avg-packets={_sumPackets / (double)Window:F1}"
                + $" avg-batches={_sumBatches / (double)Window:F1}"
                + $" depth-replay-item-submissions-saved={_sumSavedReplaySubmissions}");
            Console.WriteLine("[glprofile-json] " + JsonSerializer.Serialize(new
            {
                schema = 1,
                samples = Window,
                worldCpuP95Ms = sorted[PercentileIndex(Window, 95)] * tickMs,
                worldCpuP99Ms = sorted[PercentileIndex(Window, 99)] * tickMs,
                averageDisplayLists = _sumLists / (double)Window,
                averageVboDraws = _sumVboDraws / (double)Window,
                averageMaterialBatches = _sumBatches / (double)Window,
                averageTextureBinds = _sumTextureBinds / (double)Window,
                averageTextureBindsElided = _sumTextureBindsElided / (double)Window,
                averageStateRequestsElided = _sumStateRequestsSkipped / (double)Window
            }));
            _windowCount = 0;
            _sumLists = _sumVboDraws = _sumImmediate = _sumTextureBinds = 0;
            _sumTextureBindsElided = _sumTextureUnitsElided
                = _sumRasterStatesElided = 0;
            _sumProgramBinds = _sumFramebufferBinds = _sumStencilRequests = 0;
            _sumStateRequests = _sumStateRequestsSkipped = _sumPackets
                = _sumBatches = _sumSavedReplaySubmissions = 0;
        }
        return sample;
    }

    internal static int PercentileIndex(int count, int percentile)
    {
        if (count <= 0 || percentile is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(count));
        return Math.Clamp((int)Math.Ceiling(count * (percentile / 100.0)) - 1,
            0, count - 1);
    }
}

internal readonly record struct OpenGlWorldSubmissionSample(
    double CpuMilliseconds, int DisplayListCalls, int ImmediateBegins,
    int TextureBindRequests, int ProgramBindRequests, int FramebufferBindRequests,
    int StateRequests, int StencilFuncRequests, int CapturedPackets,
    int MaterialBatches, int AvoidedOpaqueReplaySubmissions);
