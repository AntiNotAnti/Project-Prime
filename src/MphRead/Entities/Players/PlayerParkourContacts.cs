using System;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private Vector3 _modParkourWallNormal;
    private bool _modParkourWallContact, _modParkourWallCorner;
    private ulong _modWallstrafeFrame = ulong.MaxValue, _modWallstrafeAttempt = ulong.MaxValue;
    private ulong _modParkourContactFrame = ulong.MaxValue;
    internal bool ModParkourWallstrafeApplied(ulong frame) => _modWallstrafeFrame == frame;
    private bool ModParkourActive => _scene.GameState.MovementModel == MovementModel.Parkour && ModMomentumEligible
        && !Cheats.WalkThroughWalls;

    private void ModResetParkourContacts()
    {
        _modParkourWallNormal = default;
        _modParkourWallContact = _modParkourWallCorner = false;
        _modParkourContactFrame = _modWallstrafeAttempt = _modWallstrafeFrame = ulong.MaxValue;
    }

    private void ModBeginParkourContacts()
    {
        if (_scene.GameState.MovementModel != MovementModel.Parkour) return;
        _modParkourContactFrame = _scene.FrameCount;
        _modParkourWallNormal = default;
        _modParkourWallContact = _modParkourWallCorner = false;
    }

    // Called only by the native positive-penetration response. Floors do not
    // manufacture a wall; an edge, mover, ramp, ceiling or second wall vetoes
    // the entire tick so a final tangent impulse cannot enter another plane.
    private void ModObserveParkourContact(in CollisionResult result)
    {
        if (!ModParkourActive) return;
        Vector3 normal = result.Plane.Xyz;
        if (result.EntityCollision != null || result.Field0 != 0 || result.Slipperiness != 0
            || result.Flags.TestFlag(CollisionFlags.Damaging) || result.Terrain == Terrain.Lava
            || !float.IsFinite(normal.LengthSquared) || MathF.Abs(normal.LengthSquared - 1) > .001f)
        { _modParkourWallCorner = true; return; }
        if (normal.Y == 1 && normal.X == 0 && normal.Z == 0) return;
        if (MathF.Abs(normal.Y) > 1f / 4096)
        { _modParkourWallCorner = true; return; }
        normal.Y = 0; normal.Normalize();
        if (_modParkourWallContact && normal != _modParkourWallNormal)
        { _modParkourWallCorner = true; return; }
        _modParkourWallNormal = normal;
        _modParkourWallContact = true;
    }

    private void ModApplyParkourWallstrafe()
    {
        if (!ModParkourActive || _modParkourContactFrame != _scene.FrameCount
            || _modWallstrafeAttempt == _scene.FrameCount) return;
        _modWallstrafeAttempt = _scene.FrameCount;
        if (!_modParkourWallContact || _modParkourWallCorner
            || ModSliding || ModCrouched && Flags1.TestFlag(PlayerFlags1.Standing)) return;
        float forward = Controls.MoveUp.IsDown ? Controls.AnalogScaleY(1) : 0;
        float strafe = Controls.MoveRight.IsDown ? Controls.AnalogScaleX(1)
            : Controls.MoveLeft.IsDown ? -Controls.AnalogScaleX(-1) : 0;
        Vector3 wish = new Vector3(_field70, 0, _field74) * forward
            + new Vector3(-_field78, 0, -_field7C) * strafe;
        if (wish.LengthSquared < 1e-8f || !float.IsFinite(wish.LengthSquared)) return;
        wish.Normalize();
        Vector3 before = Speed;
        if (!float.IsFinite(before.X) || !float.IsFinite(before.Y) || !float.IsFinite(before.Z)) return;
        Vector3 units = before * NativeVelocityUnitsPerSecond;
        Vector3 after = WallstrafeMovementV1.Step(units,
            forward, strafe, wish, _modParkourWallNormal, true, true, false);
        if (after == units) return;
        Vector3 delta = after - units;
        // Preserve all untouched native components without a unit round trip.
        Speed = new(delta.X == 0 ? before.X : before.X + delta.X / NativeVelocityUnitsPerSecond,
            before.Y, delta.Z == 0 ? before.Z : before.Z + delta.Z / NativeVelocityUnitsPerSecond);
        if (Speed.X != before.X || Speed.Z != before.Z) _modWallstrafeFrame = _scene.FrameCount;
    }
}
