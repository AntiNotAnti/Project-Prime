using System;
using System.Diagnostics;

namespace MphRead
{
    /// <summary>
    /// Measures successful presentations over a bounded wall-clock window.
    /// The window begins at the first presentation so scene construction and
    /// content loading can never depress the first displayed FPS sample.
    /// </summary>
    internal sealed class PresentedFrameRateCounter
    {
        private const double SampleSeconds = 0.5;
        private bool _initialized;
        private long _windowStart;
        private int _frames;

        public bool Record(long timestamp, out float framesPerSecond)
        {
            if (!_initialized)
            {
                _initialized = true;
                _windowStart = timestamp;
                _frames = 0;
                framesPerSecond = 0;
                return false;
            }

            _frames++;
            double elapsed = Stopwatch.GetElapsedTime(_windowStart, timestamp).TotalSeconds;
            if (elapsed < SampleSeconds)
            {
                framesPerSecond = 0;
                return false;
            }

            framesPerSecond = elapsed > 0 ? (float)(_frames / elapsed) : 0;
            _frames = 0;
            _windowStart = timestamp;
            return true;
        }
    }
}
