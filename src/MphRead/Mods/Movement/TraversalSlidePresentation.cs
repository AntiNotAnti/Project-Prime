using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

internal enum TraversalSlidePhase { None, Entry, Glide, Exit }

// Completed-tick cosmetic state, never a gameplay slide entitlement.
// The caller supplies an accepted visual glide level, using physical slide
// state locally and completed-gait inference for eligible remote/replay bodies.
// The caller resets
// on position/life/form/presentation-generation discontinuities before capture.
// First capture, skipped/backward frames and explicit Reset seed the accepted
// level directly: history cannot invent an entry or exit that was not captured.
internal struct TraversalSlidePresentation
{
    internal const int EntryTicks = 6;
    internal const int ExitTicks = 6;
    internal const float DirectionBlend = .25f;
    private bool _ready, _gliding;
    private ulong _frame;
    private int _transitionTicks;
    private float _transitionStart, _previous, _current;
    private Vector2 _previousDirection, _direction;
    internal bool Ready => _ready;
    // This is the completed endpoint's phase. Picture poses use Sample, so a
    // phase boundary never discards the previous endpoint's remaining blend.
    internal TraversalSlidePhase Phase { get; private set; }
    internal void Reset() => this = default;

    // World travel is optional, with world X/Z in this vector's X/Y. Its magnitude does
    // not affect the envelope. Missing/invalid/zero travel retains direction;
    // a fresh lifetime defaults world +Z (+Y in this planar vector).
    internal void Capture(ulong frame, bool gliding, Vector2 worldTravel = default)
    {
        if (_ready && frame == _frame) return;
        bool continuous = _ready && _frame != ulong.MaxValue && frame == _frame + 1;
        if (!continuous)
        {
            _previous = _current = gliding ? 1 : 0;
            _previousDirection = _direction = Unit(worldTravel, Vector2.UnitY);
            _transitionStart = _current; _transitionTicks = 0;
            Phase = gliding ? TraversalSlidePhase.Glide : TraversalSlidePhase.None;
        }
        else
        {
            _previous = _current; _previousDirection = _direction;
            Vector2 target = Unit(worldTravel, _direction);
            // Angular interpolation turns through exact reversals instead of
            // repeatedly normalizing a vector that still points forwards.
            _direction = Turn(_direction, target, DirectionBlend);
            if (gliding != _gliding)
            {
                _transitionStart = _current; _transitionTicks = 0;
                Phase = gliding ? TraversalSlidePhase.Entry : TraversalSlidePhase.Exit;
            }
            if (Phase == TraversalSlidePhase.Entry)
            {
                float t = (float)++_transitionTicks / EntryTicks;
                _current = _transitionStart + (1 - _transitionStart) * Math.Min(1, t);
                if (_transitionTicks >= EntryTicks) { _current = 1; Phase = TraversalSlidePhase.Glide; }
            }
            else if (Phase == TraversalSlidePhase.Exit)
            {
                float t = (float)++_transitionTicks / ExitTicks;
                _current = _transitionStart * (1 - Math.Min(1, t));
                if (_transitionTicks >= ExitTicks) { _current = 0; Phase = TraversalSlidePhase.None; }
            }
        }
        _ready = true; _frame = frame; _gliding = gliding;
    }

    internal readonly float Sample(float alpha)
    {
        float amount = _previous + (_current - _previous) * Alpha(alpha);
        return amount * amount * (3 - 2 * amount);
    }
    internal readonly Vector2 SampleDirection(float alpha)
        => !_ready ? Vector2.UnitY : Turn(_previousDirection, _direction, Alpha(alpha));
    private static float Alpha(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
    private static Vector2 Turn(Vector2 from, Vector2 to, float amount)
    {
        if (amount <= 0) return from;
        if (amount >= 1) return to;
        float dot = Math.Clamp(Vector2.Dot(from, to), -1, 1);
        float cross = from.X * to.Y - from.Y * to.X;
        // A half-turn has two equally short arcs; choose positive rotation
        // consistently rather than letting signed zero choose its direction.
        float angle = dot < 0 && MathF.Abs(cross) < 1e-6f
            ? MathF.PI : MathF.Atan2(cross, dot);
        float turn = angle * amount;
        float cos = MathF.Cos(turn), sin = MathF.Sin(turn);
        return Unit(new(from.X * cos - from.Y * sin, from.X * sin + from.Y * cos), to);
    }
    private static Vector2 Unit(Vector2 value, Vector2 fallback)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y)) return fallback;
        // Scale first, so even finite, extremely large travel cannot overflow.
        float largest = Math.Max(MathF.Abs(value.X), MathF.Abs(value.Y));
        if (largest < 1e-6f) return fallback;
        value /= largest;
        return value / value.Length;
    }
}
