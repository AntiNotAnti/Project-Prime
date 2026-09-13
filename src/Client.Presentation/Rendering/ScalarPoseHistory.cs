using System;

namespace MphRead.Mods.Render;

/// <summary>
/// Typed two-sample history for presentation scalars such as camera FOV.
/// Gameplay orientation remains on the newest simulation sample; this class
/// is intentionally not used for consumed input state.
/// </summary>
internal sealed class ScalarPoseHistory
{
    private float _previous;
    private float _current;
    private ulong _tick;
    private long _epoch;
    private bool _valid;

    public bool HasSamples => _valid;

    public void Reset() => _valid = false;

    public void Capture(float value, ulong tick, long epoch, bool discontinuity = false)
    {
        if (!float.IsFinite(value))
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

    public float Resolve(float alpha)
    {
        if (!_valid) return _current;
        alpha = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
        return _previous + (_current - _previous) * alpha;
    }
}
