#if !MPHREAD_SERVER && !ANDROID
using System;
using System.Diagnostics;
using System.Text.Json;

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

    internal static bool Enabled => _enabled;

    internal static double OnePercentLowFromP99Milliseconds(double p99Ms)
        => p99Ms > 0 && double.IsFinite(p99Ms) ? 1000.0 / p99Ms : 0.0;

    internal static void BeginRender()
    {
        if (!_enabled) return;
        _renderStart = Stopwatch.GetTimestamp();
        _renderActive = true;
        _presentActive = false;
    }

    internal static void CancelFrame()
    {
        _renderActive = false;
        _presentActive = false;
    }

    internal static void EndRender()
    {
        if (!_renderActive) return;
        _renderElapsed = ElapsedMs(_renderStart, Stopwatch.GetTimestamp());
        _renderActive = false;
    }

    internal static void BeginPresent()
    {
        if (!_enabled) return;
        _presentStart = Stopwatch.GetTimestamp();
        _presentActive = true;
    }

    internal static void EndPresent()
    {
        if (!_presentActive) return;
        long end = Stopwatch.GetTimestamp();
        _presentActive = false;
        if (_previousPresentEnd != 0)
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
                    vbo = DesktopRetainedGeometry.Enabled,
                    bindingCache = OpenGlScopedBindingCache.Enabled
                }));
                _count = 0;
            }
        }
        _previousPresentEnd = end;
    }

    private static double ElapsedMs(long begin, long end)
        => Math.Max(0, end - begin) * 1000.0 / Stopwatch.Frequency;

    private static double P(double[] source, int percentile)
    {
        double[] values = (double[])source.Clone();
        Array.Sort(values);
        return values[OpenGlSubmissionProfiler.PercentileIndex(values.Length, percentile)];
    }
}
#endif
