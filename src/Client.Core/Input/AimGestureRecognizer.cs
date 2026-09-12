using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Input
{
    /// <summary>
    /// Source-local DS-style aim gestures. Each pointer source owns a distinct
    /// instance so touch and stylus timing cannot contaminate one another. The
    /// pointer flick itself is shared with the Morph Ball input detector and
    /// emits at most one direction per contact.
    /// </summary>
    public sealed class AimGestureRecognizer
    {
        internal const long TapMaxMs = 250;
        internal const long DoubleTapGapMs = 300;
        internal const float TapSlopDp = 16f;
        internal const float DoubleTapSpreadDp = 70f;

        private readonly MorphBallPointerFlickDetector _flickDetector = new();
        private bool _down;
        private long _downTime;
        private float _downX;
        private float _downY;
        private bool _tapMoved;
        private long _lastTapTime;
        private float _lastTapX;
        private float _lastTapY;
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
            _flickDetector.Density = ValidDensity();
            _flickDetector.Begin(x, y, timestamp);
        }

        /// <returns>True when this movement completed a flick.</returns>
        public bool PointerMove(float x, float y, long timestamp)
        {
            if (!Enabled || !_down) return false;
            timestamp = NormalizeTime(timestamp);
            float density = ValidDensity();
            float dx = x - _downX;
            float dy = y - _downY;
            float slop = TapSlopDp * density;
            if (dx * dx + dy * dy > slop * slop) _tapMoved = true;
            if (!FlickEnabled)
                return false;
            _flickDetector.Density = density;
            if (!_flickDetector.Move(x, y, timestamp, out Vector2 direction))
                return false;
            _flickPending = true;
            _flickX = direction.X;
            _flickY = direction.Y;
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
            _flickDetector.End();
        }

        public bool TakeDoubleTap()
        {
            bool value = _doubleTapPending;
            _doubleTapPending = false;
            return value;
        }

        /// <summary>
        /// Change flick eligibility without requiring an active pointer to be
        /// lifted. Re-enabling starts a fresh timing window at the pointer's
        /// current position, so movement from before the form change cannot
        /// become a delayed Morph Ball boost.
        /// </summary>
        public void SetFlickEnabled(bool enabled, float x, float y,
            long timestamp = 0)
        {
            FlickEnabled = enabled;
            _flickPending = false;
            _flickDetector.Reset();
            if (enabled && _down)
            {
                _flickDetector.Density = ValidDensity();
                _flickDetector.Begin(x, y, NormalizeTime(timestamp));
            }
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
            _flickDetector.Reset();
            if (clearHistory)
            {
                _lastTapTime = 0;
            }
        }

        private float ValidDensity()
            => float.IsFinite(Density) && Density > 0 ? Density : 1;

        private static long NormalizeTime(long timestamp)
            => timestamp > 0 ? timestamp : Environment.TickCount64;
    }
}
