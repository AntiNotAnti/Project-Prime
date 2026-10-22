using System;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Read-only completed-travel classification and exact captured native bob.
// Dedicated walking and slide presenters own their separate visual targets.
internal struct TraversalGaitPresentation
{
    internal const float WalkingBobScale = .3f;
    private bool _ready, _inferredGlide;
    private ulong _frame;
    private Vector3 _position;
    private float _previousBob, _bob, _previousGunBob, _gunBob;
    internal bool Ready => _ready;
    internal bool Moving { get; private set; }
    internal bool Gliding { get; private set; }
    internal void Reset() => this = default;

    internal void Capture(ulong frame, Vector3 position, bool walking, bool sliding, float bob, float gunBob, float? acceptedSpeed = null)
    {
        if (_ready && frame == _frame) return;
        Vector3 delta = position - _position;
        bool continuous = _ready && _frame != ulong.MaxValue && frame == _frame + 1
            && float.IsFinite(delta.LengthSquared) && delta.LengthSquared < 16;
        float distance = continuous ? delta.Xz.Length : 0;
        _previousBob = continuous ? _bob : bob;
        _previousGunBob = continuous ? _gunBob : gunBob;
        Moving = continuous && distance > .00001f;
        // Remote/replay bodies have no slide timer. This classification grants
        // no physical slide, jump or input entitlement and never queries a world.
        // Snapshot velocity is already expressed in units/second by the
        // caller. Held or batched snapshot positions must not pulse this level.
        float speed = acceptedSpeed ?? distance * FrameTiming.SimulationHz;
        bool validSpeed = float.IsFinite(speed) && speed >= 0;
        // Accepted remote speed can straddle the entry threshold in adjacent
        // snapshots. Retire an inferred glide at walking speed, so that narrow
        // band cannot restart both cosmetic envelopes every other capture.
        float threshold = TraversalMovementProfileV4.CrouchSpeed
            * (acceptedSpeed.HasValue && _inferredGlide ? 1 : 1.05f);
        bool inferred = continuous && walking && validSpeed && speed > threshold;
        _inferredGlide = acceptedSpeed.HasValue && inferred;
        Gliding = sliding || inferred;
        _ready = true; _frame = frame; _position = position;
        _bob = bob; _gunBob = gunBob;
    }

    internal readonly float SampleBob(float alpha, bool gun = false)
    {
        float t = float.IsFinite(alpha) ? Math.Clamp(alpha, 0, 1) : 1;
        return gun ? _previousGunBob + (_gunBob - _previousGunBob) * t
            : _previousBob + (_bob - _previousBob) * t;
    }
}
