using System;
using System.Diagnostics;

namespace MphRead
{
    /// <summary>
    /// Measures successful frame submissions over a bounded wall-clock window.
    /// The window begins at the first submission so scene construction and
    /// content loading can never depress the first displayed FPS sample.
    /// </summary>
    internal sealed class PresentedFrameRateCounter
    {
        private const double MinimumSampleSeconds = 0.5;
        private const double RollingWindowSeconds = 1.0;
        private const double ReportIntervalSeconds = 0.25;
        private const double IdleResetSeconds = 0.5;
        // FrameTiming caps explicit rates at 500 Hz. Two spare entries retain
        // a complete one-second interval at that ceiling without allocating
        // from the render thread. A faster display remains accurate over the
        // newest bounded subset.
        private const int Capacity = Mods.Render.FrameTiming.MaxCap + 2;
        private readonly long[] _timestamps = new long[Capacity];
        private int _start;
        private int _count;
        private long _lastTimestamp;
        private long _lastReport;

        public bool Record(long timestamp, out float framesPerSecond)
        {
            framesPerSecond = 0;
            if (_count == 0)
            {
                Restart(timestamp);
                return false;
            }

            if (timestamp <= _lastTimestamp
                || Stopwatch.GetElapsedTime(_lastTimestamp, timestamp).TotalSeconds
                    >= IdleResetSeconds)
            {
                Restart(timestamp);
                return false;
            }

            Add(timestamp);
            _lastTimestamp = timestamp;

            // Retain the oldest submission needed to span approximately one
            // second. This avoids the integer-frame boundary oscillation of
            // disjoint buckets and smooths the short submission bursts common
            // to queued Windows/VSync presentation.
            while (_count > 2
                && Stopwatch.GetElapsedTime(At(1), timestamp).TotalSeconds
                    >= RollingWindowSeconds)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }

            double sampleSeconds = Stopwatch.GetElapsedTime(At(0), timestamp).TotalSeconds;
            if (sampleSeconds < MinimumSampleSeconds
                || Stopwatch.GetElapsedTime(_lastReport, timestamp).TotalSeconds
                    < ReportIntervalSeconds)
            {
                return false;
            }

            framesPerSecond = (float)((_count - 1) / sampleSeconds);
            _lastReport = timestamp;
            return true;
        }

        private long At(int offset) => _timestamps[(_start + offset) % Capacity];

        private void Add(long timestamp)
        {
            if (_count == Capacity)
            {
                _start = (_start + 1) % Capacity;
                _count--;
            }
            _timestamps[(_start + _count) % Capacity] = timestamp;
            _count++;
        }

        private void Restart(long timestamp)
        {
            _start = 0;
            _count = 1;
            _timestamps[0] = timestamp;
            _lastTimestamp = timestamp;
            _lastReport = timestamp;
        }
    }
}
