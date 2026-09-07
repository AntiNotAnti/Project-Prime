using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render
{
    /// <summary>Two completed simulation poses. No entity or model references are mutated.</summary>
    public sealed class SimulationPoseHistory
    {
        private Matrix4 _previous, _current;
        private ulong _tick;
        private long _epoch;
        private bool _valid;
        public bool HasSamples => _valid;
        public void Reset() => _valid = false;
        public void Capture(Matrix4 value, ulong tick, long epoch, bool discontinuity = false)
        {
            for (int row = 0; row < 4; row++)
                for (int column = 0; column < 4; column++)
                    if (!float.IsFinite(value[row, column])) { Reset(); return; }
            if (MathF.Abs(value.Determinant) < 0.000001f) { Reset(); return; }
            if (!_valid || discontinuity || epoch != _epoch || tick != _tick + 1
                || (value.Row3.Xyz - _current.Row3.Xyz).LengthSquared > 36)
                _previous = value;
            else _previous = _current;
            _current = value;
            _tick = tick;
            _epoch = epoch;
            _valid = true;
        }
        public Matrix4 Resolve(float alpha)
        {
            if (!_valid) return Matrix4.Identity;
            alpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
            if (alpha == 1) return _current;
            Vector3 position = Vector3.Lerp(_previous.Row3.Xyz, _current.Row3.Xyz, alpha);
            Vector3 scale = Vector3.Lerp(_previous.ExtractScale(), _current.ExtractScale(), alpha);
            Quaternion rotation = Quaternion.Slerp(_previous.ExtractRotation(), _current.ExtractRotation(), alpha);
            return Matrix4.CreateScale(scale) * Matrix4.CreateFromQuaternion(rotation) * Matrix4.CreateTranslation(position);
        }
        public Matrix4 Delta(float alpha) => !_valid || alpha >= 1 ? Matrix4.Identity : _current.Inverted() * Resolve(alpha);
    }
}
