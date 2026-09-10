using System;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Source-local DS-style aim gestures. Each pointer source owns a distinct
    /// instance so touch and stylus timing/cooldowns cannot contaminate one another.
    /// </summary>
    public sealed class AimGestureRecognizer
    {
        private const int Capacity = 12;
        internal const float FlickDistanceDp = 50f;
        internal const long FlickWindowMs = 120;
        internal const long FlickCooldownMs = 350;
        internal const long TapMaxMs = 250;
        internal const long DoubleTapGapMs = 300;
        internal const float TapSlopDp = 16f;
        internal const float DoubleTapSpreadDp = 70f;

        private readonly float[] _x = new float[Capacity];
        private readonly float[] _y = new float[Capacity];
        private readonly long[] _time = new long[Capacity];
        private int _count;
        private int _newest = -1;
        private bool _down;
        private long _downTime;
        private float _downX;
        private float _downY;
        private bool _tapMoved;
        private long _lastTapTime;
        private float _lastTapX;
        private float _lastTapY;
        private long _lastFlickTime;
        private bool _doubleTapPending;
        private bool _flickPending;
        private float _flickX;
        private float _flickY;

        public bool Enabled { get; set; } = true;
        public bool DoubleTapEnabled { get; set; } = true;
        public bool FlickEnabled { get; set; } = true;
        public float Density { get; set; } = 1;

        public void PointerDown(float x, float y, long timestamp)
        {
            if (!Enabled) return;
            timestamp = NormalizeTime(timestamp);
            _down = true;
            _downTime = timestamp;
            _downX = x;
            _downY = y;
            _tapMoved = false;
            ResetSamples();
            Add(x, y, timestamp);
        }

        /// <returns>True when this movement completed a flick and should not become look.</returns>
        public bool PointerMove(float x, float y, long timestamp)
        {
            if (!Enabled || !_down) return false;
            timestamp = NormalizeTime(timestamp);
            float density = ValidDensity();
            float dx = x - _downX;
            float dy = y - _downY;
            float slop = TapSlopDp * density;
            if (dx * dx + dy * dy > slop * slop) _tapMoved = true;
            Add(x, y, timestamp);
            if (!FlickEnabled || timestamp - _lastFlickTime < FlickCooldownMs)
                return false;
            (float distance, float fx, float fy) = Displacement(timestamp);
            if (distance <= FlickDistanceDp * density) return false;
            _flickPending = true;
            _flickX = fx / distance;
            _flickY = fy / distance;
            _lastFlickTime = timestamp;
            ResetSamples();
            Add(x, y, timestamp);
            return true;
        }

        public void PointerUp(float x, float y, long timestamp)
        {
            if (!Enabled || !_down) return;
            timestamp = NormalizeTime(timestamp);
            _down = false;
            if (DoubleTapEnabled && !_tapMoved
                && timestamp - _downTime <= TapMaxMs)
            {
                float spread = DoubleTapSpreadDp * ValidDensity();
                float dx = _downX - _lastTapX;
                float dy = _downY - _lastTapY;
                if (_lastTapTime != 0
                    && timestamp - _lastTapTime <= DoubleTapGapMs
                    && dx * dx + dy * dy <= spread * spread)
                {
                    _doubleTapPending = true;
                    _lastTapTime = 0;
                }
                else
                {
                    _lastTapTime = timestamp;
                    _lastTapX = _downX;
                    _lastTapY = _downY;
                }
            }
            ResetSamples();
        }

        public bool TakeDoubleTap()
        {
            bool value = _doubleTapPending;
            _doubleTapPending = false;
            return value;
        }

        public (bool Fired, float X, float Y) TakeFlick()
        {
            bool value = _flickPending;
            _flickPending = false;
            return (value, _flickX, _flickY);
        }

        public void Cancel(bool clearHistory = true)
        {
            _down = false;
            _doubleTapPending = false;
            _flickPending = false;
            ResetSamples();
            if (clearHistory)
            {
                _lastTapTime = 0;
                _lastFlickTime = 0;
            }
        }

        private void Add(float x, float y, long timestamp)
        {
            _newest = (_newest + 1) % Capacity;
            _x[_newest] = x;
            _y[_newest] = y;
            _time[_newest] = timestamp;
            if (_count < Capacity) _count++;
        }

        private (float Distance, float X, float Y) Displacement(long timestamp)
        {
            if (_count < 2) return default;
            float newestX = _x[_newest];
            float newestY = _y[_newest];
            float best = 0;
            float bestX = 0;
            float bestY = 0;
            for (int i = 1; i < _count; i++)
            {
                int index = (_newest - i + Capacity) % Capacity;
                if (i > 1 && timestamp - _time[index] > FlickWindowMs) break;
                float dx = newestX - _x[index];
                float dy = newestY - _y[index];
                float square = dx * dx + dy * dy;
                if (square > best)
                {
                    best = square;
                    bestX = dx;
                    bestY = dy;
                }
            }
            return (MathF.Sqrt(best), bestX, bestY);
        }

        private void ResetSamples()
        {
            _count = 0;
            _newest = -1;
        }

        private float ValidDensity()
            => float.IsFinite(Density) && Density > 0 ? Density : 1;

        private static long NormalizeTime(long timestamp)
            => timestamp > 0 ? timestamp : Environment.TickCount64;
    }
}
