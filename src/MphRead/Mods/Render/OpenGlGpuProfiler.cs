#if !MPHREAD_SERVER
using System;
using System.Text.Json;
#if !ANDROID
using OpenTK.Graphics.OpenGL;
using DriverGL = OpenTK.Graphics.OpenGL.GL;
#endif

namespace MphRead.Mods.Render;

/// <summary>Sequential elapsed queries, shared bounded ring, never waits for GPU completion.</summary>
internal static class OpenGlGpuProfiler
{
    private const int Slots = 64;
    private static readonly bool Requested = OpenGlSubmissionProfiler.Enabled &&
        (Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_GPU_TIMER") == "1" ||
        Array.Exists(Environment.GetCommandLineArgs(), a => a.Equals("-glgpu", StringComparison.OrdinalIgnoreCase)));
    [ThreadStatic] private static QueryRing? _ring;
    [ThreadStatic] private static bool _failed;
    // Reset is for context loss: old names must never be deleted in a new context.
    internal static void ResetContext() { _ring = null; _failed = false; }
    internal static void ReleaseCurrentContext()
    {
        try { _ring?.Release(); }
        finally { ResetContext(); }
    }
    internal static Scope Measure(FrameRenderPassKind pass)
    {
        if (!Requested || _failed) return default;
        try
        {
            QueryRing ring = _ring ??= new QueryRing();
            return ring.Begin(pass) ? new Scope(ring) : default;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        { Fail(ex); return default; }
    }
    private static void Fail(Exception ex)
    {
        _failed = true;
        Console.WriteLine("[glgpupass-json] " + JsonSerializer.Serialize(new { schema = 1, supported = false, reason = ex.GetType().Name, p95Ms = (double?)null }));
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly QueryRing? _owner;
        internal Scope(QueryRing owner) => _owner = owner;
        public void Dispose()
        {
            if (_owner == null || _owner != _ring || _failed) return;
            try { _owner.End(); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException) { Fail(ex); }
        }
    }
    internal sealed class QueryRing
    {
#if ANDROID
        private readonly GlesGpuTimer _es = new();
#endif
        private readonly int[] _ids = new int[Slots];
        private readonly FrameRenderPassKind[] _passes = new FrameRenderPassKind[Slots];
        private readonly OpenGlSampleWindow[] _samples = Array.ConvertAll(Enum.GetValues<FrameRenderPassKind>(), _ => new OpenGlSampleWindow());
        private int _read, _write, _pending, _dropped;
        private bool _active;
        private readonly bool _supported;
        internal QueryRing()
        {
#if ANDROID
            _supported = GlesGpuTimer.Supported;
            if (_supported)
                for (int i = 0; i < Slots; i++) _ids[i] = _es.Create();
#else
            _supported = OpenGlContextPolicy.Current.TimerQueries;
            if (_supported)
                for (int i = 0; i < Slots; i++) _ids[i] = DriverGL.GenQuery();
#endif
            Console.WriteLine("[glgpupass-json] " + JsonSerializer.Serialize(new
            {
                schema = 1, supported = _supported, samples = 0,
                reason = _supported ? "async-pass-timers" : "timer-entry-points-unavailable",
                p50Ms = (double?)null, p95Ms = (double?)null, p99Ms = (double?)null
            }));
        }
        internal bool Begin(FrameRenderPassKind pass)
        {
#if ANDROID
            GlEs.FlushPending();
#endif
            if (!_supported) return false;
            // Never let a nested rejected scope end the outer query.
            if (_active) { _dropped++; return false; }
            Drain();
            if (_pending == Slots) { _dropped++; return false; }
#if ANDROID
            _es.Begin(_ids[_write]);
#else
            DriverGL.BeginQuery(QueryTarget.TimeElapsed, _ids[_write]);
#endif
            _passes[_write] = pass; _active = true;
            return true;
        }
        internal void End()
        {
            if (!_active) return;
#if ANDROID
            GlEs.FlushPending();
            _es.End();
#else
            DriverGL.EndQuery(QueryTarget.TimeElapsed);
#endif
            _active = false; _write = (_write + 1) % Slots; _pending++;
        }
        internal void Release()
        {
            if (_active) End();
            for (int i = 0; i < Slots; i++)
            {
#if ANDROID
                _es.Delete(_ids[i]);
#else
                if (_ids[i] != 0) DriverGL.DeleteQuery(_ids[i]);
#endif
                _ids[i] = 0;
            }
        }
        private void Drain()
        {
#if ANDROID
            if (_es.Disjoint())
            {
                _dropped += _pending;
                // Discard every outstanding query and partial timing window.
                // Deleting a query is asynchronous and never reads its result.
                Release();
                _read = _write = _pending = 0;
                foreach (var sample in _samples) sample.Reset();
                for (int i = 0; i < Slots; i++) _ids[i] = _es.Create();
                Console.WriteLine("[glgpupass-json] " + JsonSerializer.Serialize(new
                { schema = 1, supported = true, disjoint = true, samples = 0, droppedQueries = _dropped, p95Ms = (double?)null }));
                return;
            }
#endif
            while (_pending > 0)
            {
#if ANDROID
                if (!_es.Available(_ids[_read])) break;
                long ns = _es.Nanoseconds(_ids[_read]);
#else
                DriverGL.GetQueryObject(_ids[_read], GetQueryObjectParam.QueryResultAvailable, out int available);
                if (available == 0) break;
                DriverGL.GetQueryObject((uint)_ids[_read], GetQueryObjectParam.QueryResult, out ulong unsignedNs);
                long ns = unsignedNs <= long.MaxValue ? (long)unsignedNs : -1;
#endif
                FrameRenderPassKind pass = _passes[_read];
                OpenGlSampleWindow samples = _samples[(int)pass];
                if (ns < 0 || ns >= 10_000_000_000L || !samples.Add(ns / 1_000_000.0)) _dropped++;
                _read = (_read + 1) % Slots; _pending--;
                if (samples.Count == 240)
                {
                    // Retain the world-only schema fields for existing A/B readers.
                    using JsonDocument summary = JsonDocument.Parse(JsonSerializer.Serialize(samples.Summary()));
                    var stats = summary.RootElement;
                    double p95 = stats.GetProperty("p95Ms").GetDouble(), p99 = stats.GetProperty("p99Ms").GetDouble();
                    Console.WriteLine("[glgpupass-json] " + JsonSerializer.Serialize(new
                    {
                        schema = 1, supported = true, pass = pass.ToString(), samples = 240,
                        p50Ms = stats.GetProperty("p50Ms").GetDouble(), p95Ms = p95, p99Ms = p99,
                        worldGpuP95Ms = pass == FrameRenderPassKind.World ? (double?)p95 : null,
                        worldGpuP99Ms = pass == FrameRenderPassKind.World ? (double?)p99 : null,
                        droppedQueries = _dropped
                    }));
                    if (pass == FrameRenderPassKind.World)
                        Console.WriteLine("[glgpu-json] " + JsonSerializer.Serialize(new
                        { schema = 1, samples = 240, worldGpuP95Ms = p95, worldGpuP99Ms = p99, droppedQueries = _dropped }));
                    samples.Reset();
                }
            }
        }
    }
}
#endif
