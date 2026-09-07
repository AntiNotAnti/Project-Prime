using System;

namespace MphRead.Mods.Network
{
    /// <summary>A single-writer token bucket using the server's monotonic clock.</summary>
    public struct NetRateLimit
    {
        private readonly double _perSecond;
        private readonly double _capacity;
        private double _available;
        private double _lastUpdate;

        public NetRateLimit(double perSecond, double capacity, double now)
        {
            if (!Double.IsFinite(perSecond) || perSecond <= 0
                || !Double.IsFinite(capacity) || capacity < 1 || !Double.IsFinite(now))
            {
                throw new ArgumentOutOfRangeException(nameof(perSecond));
            }
            _perSecond = perSecond;
            _capacity = _available = capacity;
            _lastUpdate = now;
        }

        public bool Take(double now)
        {
            if (!Double.IsFinite(now) || now < _lastUpdate)
            {
                return false;
            }
            _available = Math.Min(_capacity, _available + (now - _lastUpdate) * _perSecond);
            _lastUpdate = now;
            if (_available < 1)
            {
                return false;
            }
            _available--;
            return true;
        }
    }
}
