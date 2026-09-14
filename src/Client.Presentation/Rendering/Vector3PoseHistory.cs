using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Typed two-sample history for presentation world-space vectors such as
    /// the local player's aim convergence point.
    /// </summary>
    internal sealed class Vector3PoseHistory
    {
        private Vector3 _previous;
        private Vector3 _current;
        private ulong _tick;
        private long _epoch;
        private bool _valid;

        public bool HasSamples => _valid;

        public void Reset() => _valid = false;

        public void Capture(Vector3 value, ulong tick, long epoch,
            bool discontinuity = false)
        {
            if (!IsFinite(value))
            {
                Reset();
                return;
            }
            if (!_valid || discontinuity || epoch != _epoch || tick != _tick + 1)
            {
                _previous = value;
            }
            else
            {
                _previous = _current;
            }
            _current = value;
            _tick = tick;
            _epoch = epoch;
            _valid = true;
        }

        public Vector3 Resolve(float alpha)
        {
            if (!_valid) return _current;
            alpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
            Vector3 resolved = _previous + (_current - _previous) * alpha;
            return IsFinite(resolved) ? resolved : _current;
        }

        private static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y)
                && float.IsFinite(value.Z);
    }
}
