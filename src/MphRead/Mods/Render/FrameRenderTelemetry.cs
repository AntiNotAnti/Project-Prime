using System;
using System.Diagnostics;

namespace MphRead.Mods.Render;

// Opt-in thread-local CPU timing. Native acquisition/submission/presentation
// counters are reported separately; these spans may include that work and
// must not be added to those native counters as if they were disjoint.
internal static class FrameRenderTelemetry
{
    internal enum Phase { Extraction, RoomVisibility, RetainedExtraction, CommandRecording }
    internal readonly record struct Sample(double ExtractionMs, double RoomVisibilityMs,
        double RetainedExtractionMs, double CommandRecordingMs);
    [ThreadStatic] private static bool _enabled;
    [ThreadStatic] private static Sample _sample;
    internal static void Begin() { _sample = default; _enabled = true; }
    internal static Sample End() { _enabled = false; return _sample; }
    internal static Scope Measure(Phase phase) => new(phase, _enabled ? Stopwatch.GetTimestamp() : 0);
    internal readonly struct Scope : IDisposable
    {
        private readonly Phase _phase;
        private readonly long _start;
        internal Scope(Phase phase, long start) { _phase = phase; _start = start; }
        public void Dispose()
        {
            if (_start == 0) return;
            double ms = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            _sample = _phase switch
            {
                Phase.Extraction => _sample with { ExtractionMs = _sample.ExtractionMs + ms },
                Phase.RoomVisibility => _sample with { RoomVisibilityMs = _sample.RoomVisibilityMs + ms },
                Phase.RetainedExtraction => _sample with { RetainedExtractionMs = _sample.RetainedExtractionMs + ms },
                _ => _sample with { CommandRecordingMs = _sample.CommandRecordingMs + ms }
            };
        }
    }
}
