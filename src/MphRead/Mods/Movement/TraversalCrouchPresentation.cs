using System;

namespace MphRead.Mods.Movement;

// A bounded cosmetic timeline. Never read by collision, input, weapon facts or
// replay checkpoints. Sampling is pure, so extra pictures cannot advance it.
internal struct TraversalCrouchPresentation
{
    internal const int TransitionTicks = 6;
    private bool _ready, _previousCanonical, _canonical;
    private ulong _frame;
    private float _previous, _current;

    internal void Reset() => this = default;
    internal void Capture(ulong frame, bool crouched)
    {
        float target = crouched ? 1 : 0;
        if (_ready && frame == _frame) return;
        if (!_ready || frame != _frame + 1)
        { _previous = _current = target; _previousCanonical = crouched; }
        else
        {
            _previous = _current;
            _previousCanonical = _canonical;
            _current += Math.Clamp(target - _current, -1f / TransitionTicks, 1f / TransitionTicks);
        }
        _canonical = crouched; _ready = true; _frame = frame;
    }
    internal readonly float SampleCanonical(float alpha, bool fallback)
    {
        if (!_ready) return fallback ? 1 : 0;
        float t = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
        return (_previousCanonical ? 1 : 0) * (1 - t) + (_canonical ? 1 : 0) * t;
    }
    internal readonly float Sample(float alpha, bool fallback)
    {
        if (!_ready) return fallback ? 1 : 0;
        float t = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
        float amount = _previous + (_current - _previous) * t;
        return amount * amount * (3 - 2 * amount);
    }
}
