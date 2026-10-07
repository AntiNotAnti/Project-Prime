#if !ANDROID && !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;

namespace MphRead.Mods.Render;

// Check-owned CPU observations only. No graphics handle or callback is retained.
internal sealed class ShaderDiagnosticReadbackObservation
{
    internal const int MaximumReadbackMarkers = 128;
    internal const int MaximumScopeMarkers = 1024;
    internal const int MaximumProcessMarkers = 2048;
    private readonly object _gate = new();
    private Scope? _scope;
    private Readback? _readback;
    private long _scopeSequence;
    private int _processAttempts;

    private sealed class Empty : IDisposable
    {
        internal static readonly Empty Instance = new();
        public void Dispose() { }
    }

    private sealed class Scope : IDisposable
    {
        internal readonly ShaderDiagnosticReadbackObservation Owner;
        internal readonly long Ordinal, Started;
        internal readonly int Thread;
        internal readonly TextWriter Writer;
        internal readonly Func<long> Ticks;
        internal readonly Func<DateTimeOffset> Utc;
        internal int Attempts, Readbacks;
        internal Scope(ShaderDiagnosticReadbackObservation owner, long ordinal, long started,
            TextWriter writer, Func<long> ticks, Func<DateTimeOffset> utc)
            => (Owner, Ordinal, Started, Thread, Writer, Ticks, Utc)
                = (owner, ordinal, started, Environment.CurrentManagedThreadId, writer, ticks, utc);
        public void Dispose()
        {
            try
            {
                lock (Owner._gate)
                {
                    if (!ReferenceEquals(Owner._scope, this)) return;
                    Owner._readback = null;
                    Owner._scope = null;
                }
            }
            catch { } // Scope teardown cannot replace an original rendering failure.
        }
    }

    private sealed class Readback : IDisposable
    {
        internal readonly ShaderDiagnosticReadbackObservation Owner;
        internal readonly Scope Scope;
        internal readonly int Ordinal, X, Y, Width, Height;
        internal readonly long Started;
        internal int Attempts;
        internal Readback(ShaderDiagnosticReadbackObservation owner, Scope scope,
            int ordinal, long started, int x, int y, int width, int height)
            => (Owner, Scope, Ordinal, Started, X, Y, Width, Height)
                = (owner, scope, ordinal, started, x, y, width, height);
        public void Dispose()
        {
            try { lock (Owner._gate) { if (ReferenceEquals(Owner._readback, this)) Owner._readback = null; } }
            catch { } // Even a failed observation sink/clock leaves original cleanup in charge.
        }
    }

    private sealed class Combined : IDisposable
    {
        private IDisposable? _first, _second;
        internal Combined(IDisposable? first, IDisposable? second) => (_first, _second) = (first, second);
        public void Dispose()
        {
            // Reverse construction order. Each lease is detached once, even if
            // another diagnostic implementation unexpectedly throws on disposal.
            IDisposable? second = Interlocked.Exchange(ref _second, null);
            IDisposable? first = Interlocked.Exchange(ref _first, null);
            try { second?.Dispose(); } catch { }
            try { first?.Dispose(); } catch { }
        }
    }

    internal static IDisposable Combine(IDisposable? first, IDisposable? second)
    {
        try { return new Combined(first, second); }
        catch
        {
            try { second?.Dispose(); } catch { }
            try { first?.Dispose(); } catch { }
            return Empty.Instance;
        }
    }

    internal IDisposable BeginLayeredScope(string? diagnostic, string? validation, string? gpuValidation,
        TextWriter? writer = null, Func<long>? ticks = null, Func<DateTimeOffset>? utc = null)
    {
        // Default and nonexact opt-ins perform no clock sampling or sink access.
        if (!ShaderDiagnosticPolicy.Enabled(diagnostic, validation, gpuValidation)) return Empty.Instance;
        try
        {
            lock (_gate)
            {
                if (_scope != null || _processAttempts >= MaximumProcessMarkers) return Empty.Instance;
                ticks ??= Stopwatch.GetTimestamp;
                utc ??= static () => DateTimeOffset.UtcNow;
                return _scope = new Scope(this, ++_scopeSequence, ticks(), writer ?? Console.Out, ticks, utc);
            }
        }
        catch { return Empty.Instance; }
    }

    internal IDisposable BeginReadback(int x, int y, int width, int height)
    {
        try
        {
            if (Volatile.Read(ref _scope) == null) return Empty.Instance;
            lock (_gate)
            {
                Scope? scope = _scope;
                if (scope == null || scope.Thread != Environment.CurrentManagedThreadId || _readback != null
                    || scope.Attempts >= MaximumScopeMarkers || _processAttempts >= MaximumProcessMarkers)
                    return Empty.Instance;
                return _readback = new Readback(this, scope, ++scope.Readbacks, scope.Ticks(), x, y, width, height);
            }
        }
        catch { return Empty.Instance; }
    }

    internal bool Mark(string phase, string edge)
    {
        try
        {
            if (Volatile.Read(ref _scope) == null || Volatile.Read(ref _readback) == null) return false;
            if (edge is not ("START" or "DONE") || !KnownPhase(phase)) return false;
            lock (_gate)
            {
                Scope? scope = _scope;
                Readback? readback = _readback;
                if (scope == null || readback == null || !ReferenceEquals(readback.Scope, scope)
                    || scope.Thread != Environment.CurrentManagedThreadId
                    || readback.Attempts >= MaximumReadbackMarkers
                    || scope.Attempts >= MaximumScopeMarkers || _processAttempts >= MaximumProcessMarkers)
                    return false;
                // Count attempts before either clock or sink can throw, so broken
                // diagnostics cannot create an unbounded retry lane.
                int sequence = ++scope.Attempts;
                readback.Attempts++;
                _processAttempts++;
                long ticks = scope.Ticks();
                if (ticks < scope.Started || ticks < readback.Started) return false;
                DateTimeOffset utc = scope.Utc().ToUniversalTime();
                double elapsed = (ticks - scope.Started) * 1000.0 / Stopwatch.Frequency;
                double readElapsed = (ticks - readback.Started) * 1000.0 / Stopwatch.Frequency;
                scope.Writer.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"DIAGNOSTIC renderwindowcheck layered-pbr-readback scope={scope.Ordinal} read={readback.Ordinal} phase={phase} {edge} utc={utc:O} monotonicTicks={ticks} frequency={Stopwatch.Frequency} elapsedMs={elapsed:F3} readElapsedMs={readElapsed:F3} sequence={sequence} thread={scope.Thread} rect={readback.X},{readback.Y},{readback.Width},{readback.Height}"));
                return true;
            }
        }
        catch { return false; } // Never replace an original exception or cross a native callback boundary.
    }

    private static bool KnownPhase(string phase) => phase is "read-target" or "readback-buffer"
        or "encoder-begin" or "texture-copy" or "command-record" or "flush-commands"
        or "active-pass-end" or "encoder-finish" or "geometry-writes" or "uniform-writes"
        or "retained-uniform-writes" or "retained-pbr-writes" or "indirect-writes"
        or "checked-submit" or "observe-submit" or "reset-cursors" or "map-async"
        or "poll-wait" or "map-status" or "mapped-range" or "pixel-unpack" or "readback-cleanup";
}
#endif
