#if !MPHREAD_SERVER && !ANDROID
using System;
using OpenTK.Graphics.OpenGL;
using DesktopGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

/// <summary>
/// Opt-in asynchronous GPU elapsed-time samples for the *world graph only*.
/// Requires GL3.3/ARB_timer_query; intentionally not used on GLES3, where
/// EXT_disjoint_timer_query and disjoint handling would be required. The
/// eight-query ring only reads completed results; it never waits for a query.
/// </summary>
internal static class OpenGlGpuProfiler
{
    private const int Slots = 8;
    private const int Window = 240;
    private static readonly bool _requested =
        OpenGlFrameProfiler.Enabled
        && (Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_GPU_TIMER") == "1"
            || Array.Exists(Environment.GetCommandLineArgs(), a =>
                a.Equals("-glgpu", StringComparison.OrdinalIgnoreCase)));

    [ThreadStatic] private static QueryRing? _ring;
    [ThreadStatic] private static bool _failed;

    internal static void ResetContext()
    {
        _ring = null;
        _failed = false;
    }
    internal static void BeginWorld()
    {
        if (!_requested || _failed) return;
        try { (_ring ??= new QueryRing()).Begin(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _failed = true;
            Console.WriteLine("[glgpu] query polling failed, disabling timing: "
                + ex.GetType().Name);
        }
    }
    internal static void EndWorld()
    {
        if (_failed) return;
        try { _ring?.End(); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _failed = true;
            Console.WriteLine("[glgpu] query completion failed, disabling timing: "
                + ex.GetType().Name);
        }
    }

    private sealed class QueryRing
    {
        private readonly int[] _queries = new int[Slots];
        private readonly double[] _samples = new double[Window];
        private int _read, _write, _pending, _sampleCount, _dropped;
        private bool _probed, _supported, _active;

        private static bool HasTimerExtension()
        {
            string version = DesktopGL.GetString(StringName.Version) ?? "";
            string extensions = DesktopGL.GetString(StringName.Extensions) ?? "";
            if (extensions.Contains("GL_ARB_timer_query", StringComparison.Ordinal))
                return true;
            // Valid OpenGL 3.3+ core timer queries, including 4.x contexts.
            int dot = version.IndexOf('.');
            if (dot < 1 || dot + 1 >= version.Length)
                return false;
            if (!int.TryParse(version[..dot], out int major))
                return false;
            int end = dot + 1;
            while (end < version.Length && char.IsDigit(version[end])) end++;
            if (!int.TryParse(version[(dot + 1)..end], out int minor))
                return false;
            return major > 3 || (major == 3 && minor >= 3);
        }

        private void Probe()
        {
            if (_probed) return;
            _probed = true;
            try
            {
                _supported = HasTimerExtension();
                if (_supported)
                {
                    for (int i = 0; i < Slots; i++)
                        _queries[i] = DesktopGL.GenQuery();
                    _supported = Array.TrueForAll(_queries, q => q != 0);
                }
                Console.WriteLine(_supported
                    ? "[glgpu] asynchronous world GPU timer active"
                    : "[glgpu] no GL3.3/ARB_timer_query support; report CPU/present instead");
            }
            catch (Exception ex)
            {
                _supported = false;
                Console.WriteLine("[glgpu] GPU timer unavailable: " + ex.GetType().Name);
            }
        }

        internal void Begin()
        {
            Probe();
            if (!_supported) return;
            DrainReady();
            if (_active || _pending == Slots)
            {
                _dropped++;
                return;
            }
            try
            {
                DesktopGL.BeginQuery(QueryTarget.TimeElapsed, _queries[_write]);
                _active = true;
            }
            catch (Exception ex)
            {
                _supported = false;
                Console.WriteLine("[glgpu] timer begin failed: " + ex.GetType().Name);
            }
        }

        internal void End()
        {
            if (!_active) return;
            try
            {
                DesktopGL.EndQuery(QueryTarget.TimeElapsed);
                _write = (_write + 1) % Slots;
                _pending++;
            }
            catch (Exception ex)
            {
                _supported = false;
                Console.WriteLine("[glgpu] timer end failed: " + ex.GetType().Name);
            }
            finally { _active = false; }
        }

        private void DrainReady()
        {
            while (_pending > 0)
            {
                int id = _queries[_read];
                DesktopGL.GetQueryObject(id,
                    GetQueryObjectParam.QueryResultAvailable, out int available);
                if (available == 0) break;
                DesktopGL.GetQueryObject(id, GetQueryObjectParam.QueryResult,
                    out long nanoseconds);
                double ms = Math.Max(0, nanoseconds) / 1_000_000.0;
                if (ms < 10_000)
                    _samples[_sampleCount++] = ms;
                _read = (_read + 1) % Slots;
                _pending--;
                if (_sampleCount == Window)
                {
                    double[] sorted = (double[])_samples.Clone();
                    Array.Sort(sorted);
                    Console.WriteLine("[glgpu] worldGPU p50/p95/p99="
                        + $"{P(sorted,50):F3}/{P(sorted,95):F3}/{P(sorted,99):F3}ms"
                        + $" queryQueue={_pending}/{Slots} dropped={_dropped}");
                    _sampleCount = 0;
                    _dropped = 0;
                }
            }
        }

        private static double P(double[] sorted, int percentile)
            => sorted[OpenGlSubmissionProfiler.PercentileIndex(
                sorted.Length, percentile)];
    }
}
#endif
