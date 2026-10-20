using System;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Completed-position travel drives a cosmetic gait. Neither sampling nor extra
// pictures advance it; native animation clocks and source facts stay untouched.
internal struct TraversalGaitPresentation
{
    internal const float StrideScale = .65f;
    internal const float SwingLiftScale = .4f;
    internal const float WalkingBobScale = .3f;
    private bool _ready, _walking;
    private ulong _frame;
    private Vector3 _position;
    private double _previousPhase, _phase;
    private float _previousBob, _bob, _previousGunBob, _gunBob;
    internal bool Ready => _ready;
    internal bool Moving { get; private set; }
    internal bool Gliding { get; private set; }
    internal void Reset() => this = default;

    internal void Capture(ulong frame, Vector3 position, bool walking, bool sliding,
        int nativeFrame, int frameCount, float bob, float gunBob)
    {
        if (_ready && frame == _frame) return;
        float distance = _ready ? (position - _position).Xz.Length : 0;
        bool continuous = _ready && frame == _frame + 1 && float.IsFinite(distance) && distance <= 4;
        if (!continuous)
        {
            _phase = frameCount > 0 ? (double)nativeFrame / frameCount : 0;
            _previousPhase = _phase;
            _previousBob = bob; _previousGunBob = gunBob;
        }
        else
        {
            _previousPhase = _phase;
            _previousBob = _bob; _previousGunBob = _gunBob;
            if (walking && !_walking && frameCount > 0)
                _previousPhase = _phase = (double)nativeFrame / frameCount;
        }
        Moving = continuous && distance > .00001f;
        // Remote/replay bodies do not carry a slide timer. This read-only
        // classification also gives other fast, low grounded motion a glide;
        // it grants no physical slide, jump, or input entitlement.
        Gliding = sliding || walking && Moving
            && distance * FrameTiming.SimulationHz > TraversalMovementProfileV4.CrouchSpeed * 1.05f;
        if (continuous && walking && Moving && !Gliding && frameCount > 1)
        {
            double cycleDistance = TraversalMovementProfileV4.GroundSpeed
                * StrideScale * frameCount / (FrameTiming.SimulationHz / 2f);
            _phase += distance / cycleDistance;
            double whole = Math.Floor(_phase);
            _phase -= whole; _previousPhase -= whole;
        }
        _ready = true; _frame = frame; _position = position; _walking = walking;
        _bob = bob; _gunBob = gunBob;
    }

    internal readonly float SampleFrame(float alpha, int frameCount)
    {
        double phase = _previousPhase + (_phase - _previousPhase) * Alpha(alpha);
        phase -= Math.Floor(phase);
        return (float)(phase * Math.Max(0, frameCount));
    }
    internal readonly float SampleBob(float alpha, bool gun = false)
        => gun ? _previousGunBob + (_gunBob - _previousGunBob) * Alpha(alpha)
            : _previousBob + (_bob - _previousBob) * Alpha(alpha);
    private static float Alpha(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
}
