using System;
using System.Diagnostics;

namespace MphRead.Mods.Network
{
    /// <summary>
    /// Estimates a continuous server tick from validated ping replies. Offset
    /// includes the unavoidable symmetric-path assumption; it is a hint for
    /// interpolation, never authority to choose arbitrary historical hitboxes.
    /// </summary>
    public sealed class NetClock
    {
        private bool _synchronized;
        private uint _lastServerTick;
        private double _unwrappedServerTick;
        private double _offsetTicks;
        public NetMetrics Metrics { get; } = new();
        public bool Synchronized => _synchronized;

        public bool Observe(long sentAt, long receivedAt, uint serverTick)
        {
            if (sentAt < 0 || receivedAt < sentAt
                || (_synchronized && serverTick != _lastServerTick
                    && !Sequence32.IsNewer(serverTick, _lastServerTick)))
            {
                return false;
            }
            double elapsed = (receivedAt - sentAt) / (double)Stopwatch.Frequency;
            if (elapsed > NetConfig.TimeoutSeconds)
            {
                return false;
            }
            double midpointTicks = (sentAt / (double)Stopwatch.Frequency + elapsed / 2) * 60;
            _unwrappedServerTick = !_synchronized ? serverTick
                : _unwrappedServerTick + unchecked(serverTick - _lastServerTick);
            double offset = _unwrappedServerTick - midpointTicks;
            _offsetTicks = !_synchronized ? offset : _offsetTicks + (offset - _offsetTicks) / 8;
            _lastServerTick = serverTick;
            _synchronized = true;
            Metrics.RecordRtt(elapsed * 1000);
            return true;
        }

        public double EstimateServerTick(long now)
        {
            return now * (60.0 / Stopwatch.Frequency) + _offsetTicks;
        }
    }
}
