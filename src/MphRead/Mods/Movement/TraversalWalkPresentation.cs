using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

internal readonly record struct TraversalWalkSample(float Phase, float Amount, Vector3 Direction, Vector3 Normal);

// Completed travel drives a dedicated low step. During the support portion of
// a straight step the ankle retreats by exactly the body's accepted travel,
// so the supporting foot stays planted instead of skating with the body.
internal struct TraversalWalkPresentation
{
    internal const float SupportFraction = .62f;
    internal const float CycleReach = 1.35f;
    internal const float FootLift = .06f;
    private bool _ready, _hasHeading;
    private ulong _frame;
    private Vector3 _position, _previousDirection, _direction, _previousNormal, _normal;
    private double _previousPhase, _phase;
    private float _previousAmount, _amount;
    internal void Reset() => this = default;

    internal void Capture(ulong frame, Vector3 position, bool walking, Vector3 normal, float legReach, bool heldWalking = false)
    {
        if (_ready && _frame == frame) return;
        Vector3 delta = position - _position;
        bool continuous = _ready && _frame != ulong.MaxValue && frame == _frame + 1
            && float.IsFinite(delta.LengthSquared) && delta.LengthSquared < 16;
        normal = GroundSlopeMovement.Walkable(normal) ? normal.Normalized() : Vector3.UnitY;
        if (!continuous)
        {
            _previousPhase = _phase = 0; _previousAmount = _amount = 0; _hasHeading = false;
            _previousDirection = _direction = Vector3.UnitZ;
            _previousNormal = _normal = normal;
        }
        else
        {
            _previousPhase = _phase; _previousAmount = _amount;
            _previousDirection = _direction; _previousNormal = _normal;
            float distance = delta.Xz.Length;
            bool validReach = float.IsFinite(legReach) && legReach > .01f;
            // A remote source may hold a position while its accepted walking
            // level stays true. Keep its blend, but invent no stride distance.
            bool moving = walking && (distance > .00001f || heldWalking) && validReach;
            _amount = Math.Clamp(_amount + (moving ? 1 : -1) / 6f, 0, 1);
            _normal = normal;
            if (moving && distance > .00001f)
            {
                Vector3 heading = delta.WithY(0).Normalized();
                if (!_hasHeading || _previousAmount == 0)
                    _previousDirection = _direction = heading;
                else
                {
                    float from = MathF.Atan2(_direction.X, _direction.Z);
                    float to = MathF.Atan2(heading.X, heading.Z);
                    float turn = MathF.IEEERemainder(to - from, MathF.Tau) * .35f;
                    _direction = new(MathF.Sin(from + turn), 0, MathF.Cos(from + turn));
                }
                _hasHeading = true;
                _phase += distance / (legReach * CycleReach);
                double whole = Math.Floor(_phase);
                _phase -= whole; _previousPhase -= whole;
            }
        }
        _ready = true; _frame = frame; _position = position;
    }

    internal readonly TraversalWalkSample Sample(float alpha)
    {
        float t = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
        double phase = _previousPhase + (_phase - _previousPhase) * t;
        phase -= Math.Floor(phase);
        Vector3 direction = Vector3.Lerp(_previousDirection, _direction, t);
        direction = direction.LengthSquared > 1e-8f ? direction.Normalized() : Vector3.UnitZ;
        Vector3 normal = Vector3.Lerp(_previousNormal, _normal, t);
        normal = normal.LengthSquared > 1e-8f ? normal.Normalized() : Vector3.UnitY;
        return new((float)phase, _previousAmount + (_amount - _previousAmount) * t, direction, normal);
    }

    internal static Vector2 Step(float phase, bool right, float legReach)
    {
        float p = phase + (right ? .5f : 0);
        p -= MathF.Floor(p);
        float span = legReach * CycleReach * SupportFraction;
        if (p <= SupportFraction) return new(span * (.5f - p / SupportFraction), 0);
        float swing = (p - SupportFraction) / (1 - SupportFraction);
        // Match support velocity at both ends of swing. The small airborne
        // follow-through replaces the old instantaneous horizontal reversal.
        float ease = swing * swing * swing * (10 + swing * (-15 + 6 * swing));
        float cycle = legReach * CycleReach;
        float along = cycle * (ease - (1 - SupportFraction) * swing - SupportFraction * .5f);
        float lift = MathF.Sin(MathF.PI * swing);
        return new(along, lift * lift * legReach * FootLift);
    }
}
