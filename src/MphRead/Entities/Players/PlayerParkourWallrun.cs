using System;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private Vector3 _modWallrunNormal, _modWallkickNormal, _modParkourIncomingContactSpeed;
    private int _modWallrunTicks = WallrunMovementV1.AirborneTicks;
    private float _modWallrunAppliedGravity;
    internal float ModParkourAppliedGravity => _scene.GameState.MovementModel == MovementModel.Parkour
        ? _modWallrunAppliedGravity : _gravity;
    private bool _modWallrunning, _modWallkickPending, _modParkourSafeSupport;
    private ulong _modWallkickFrame = ulong.MaxValue, _modWallrunGravityFrame = ulong.MaxValue;
    private readonly CollisionResult[] _modWallrunProbe = new CollisionResult[16];
    internal bool ModParkourWallrunActive => _modWallrunning;
    internal int ModParkourWallrunRemaining => _modWallrunTicks;
    internal bool ModParkourWallkickApplied(ulong frame) => _modWallkickFrame == frame;

    // Ability/form/stance cleanup cannot mint another airborne budget.
    private void ModCancelParkourWallrun()
    {
        _modWallrunning = _modWallkickPending = false;
        _modWallrunNormal = _modWallkickNormal = default;
    }
    private void ModResetParkourWallrunForLife()
    {
        ModCancelParkourWallrun();
        _modWallrunTicks = WallrunMovementV1.AirborneTicks;
        _modParkourSafeSupport = false;
        _modWallkickFrame = _modWallrunGravityFrame = ulong.MaxValue;
    }
    private bool ModWallrunBodyAllowed => ModParkourActive && !ModCrouched && !ModSliding
        && !Controls.Crouch.IsDown && !Flags1.TestFlag(PlayerFlags1.Standing);

    private bool ModWallrunInput(Vector3 normal)
    {
        if (!float.IsFinite(Speed.LengthSquared)) return false;
        float forward = Controls.MoveUp.IsDown ? Controls.AnalogScaleY(1) : 0;
        float strafe = Controls.MoveRight.IsDown ? Controls.AnalogScaleX(1)
            : Controls.MoveLeft.IsDown ? -Controls.AnalogScaleX(-1) : 0;
        if (forward <= 0 || MathF.Abs(strafe) < .000001f) return false;
        Vector3 wish = new Vector3(_field70, 0, _field74) * forward
            + new Vector3(-_field78, 0, -_field7C) * strafe;
        if (!float.IsFinite(wish.LengthSquared) || wish.LengthSquared < .000001f) return false;
        wish.Normalize();
        Vector3 tangent = new(-normal.Z, 0, normal.X);
        float along = Vector3.Dot(wish, tangent);
        float speed = Vector3.Dot(Speed, tangent) * NativeVelocityUnitsPerSecond;
        return Vector3.Dot(wish, normal) < -.000001f && MathF.Abs(along) > .000001f
            && speed * MathF.Sign(along) >= WallrunMovementV1.MinimumTangentSpeed;
    }

    // A run starts only at an actual native contact. The probe subsequently
    // confirms current geometry; it cannot attach a nearby airborne body.
    private bool ModProbeWallrun(Vector3 normal)
    {
        const float reach = .04f;
        if (!float.IsFinite(normal.LengthSquared) || normal.Y != 0
            || MathF.Abs(normal.LengthSquared - 1) > .00001f
            || !ModCanOccupyBiped(false)) return false;
        Span<Vector4> shapes = stackalloc Vector4[3];
        int shapeCount = GetMovementSweepShapes(shapes);
        Vector3 minimum = new(float.MaxValue), maximum = new(float.MinValue);
        for (int i = 0; i < shapeCount; i++)
        {
            Vector3 start = Position + shapes[i].Xyz, end = start - normal * reach;
            Vector3 margin = new(shapes[i].W + CollisionRecoverySkin);
            minimum = Vector3.ComponentMin(minimum, Vector3.ComponentMin(start, end) - margin);
            maximum = Vector3.ComponentMax(maximum, Vector3.ComponentMax(start, end) + margin);
        }
        using var candidates = CollisionDetection.GetCandidatesForLimits(Position, Position,
            0, minimum, maximum, includeEntities: true, _scene);
        for (int probe = 0; probe < shapeCount; probe++)
        {
            Vector3 center = Position + shapes[probe].Xyz;
            float radius = shapes[probe].W;
            Array.Clear(_modWallrunProbe);
            int count = CollisionDetection.CheckSphereBetweenPointsRobust(candidates, center,
                center - normal * reach, radius, _modWallrunProbe.Length,
                includeOffset: true, TestFlags.Players, _scene, _modWallrunProbe);
            if (count >= _modWallrunProbe.Length) return false;
            bool matched = false;
            for (int i = 0; i < count; i++)
            {
                ref CollisionResult hit = ref _modWallrunProbe[i];
                if (hit.EntityCollision != null || hit.Field0 != 0 || hit.Slipperiness != 0
                    || hit.Flags.TestFlag(CollisionFlags.Damaging) || hit.Terrain == Terrain.Lava
                    || !float.IsFinite(hit.Plane.LengthSquared)) return false;
                if (hit.Plane.Xyz == Vector3.UnitY) continue;
                Vector3 faceNormal = hit.Plane.Xyz;
                if (MathF.Abs(faceNormal.Y) > 1f / 4096
                    || MathF.Abs(faceNormal.LengthSquared - 1) > .001f) return false;
                faceNormal.Y = 0; faceNormal.Normalize();
                if (faceNormal != normal) return false;
                float gap = Vector3.Dot(center, hit.Plane.Xyz) - hit.Plane.W - radius;
                if (gap < -CollisionRecoverySkin || gap > CollisionRecoverySkin) return false;
                matched = true;
            }
            if (!matched) return false;
        }
        return true;
    }

    // Called after controls/aim update, before the native jump branch. A valid
    // fresh kick takes that input without spending native jump/SpaceJump flags.
    private void ModPrepareParkourWallrun()
    {
        if (_scene.GameState.MovementModel != MovementModel.Parkour) return;
        _modWallkickPending = false;
        if (!_modWallrunning || _modWallrunTicks <= 0 || !ModWallrunBodyAllowed
            || !ModWallrunInput(_modWallrunNormal) || !ModProbeWallrun(_modWallrunNormal))
        { _modWallrunning = false; return; }
        if (Controls.Jump.IsPressed)
        {
            _modWallkickNormal = _modWallrunNormal;
            _modWallkickPending = true;
            _modWallrunning = false;
            _modWallrunTicks = 0;
        }
    }

    private void ModApplyParkourWallkick(bool movementAllowed)
    {
        if (!_modWallkickPending) return;
        _modWallkickPending = false;
        // Native pad/teleport/external acceleration wins, just as it wins over
        // an ordinary jump accepted before ProcessMovement's trigger seam.
        if (!movementAllowed || !ModWallrunBodyAllowed) return;
        Vector3 before = Speed, units = before * NativeVelocityUnitsPerSecond;
        if (!float.IsFinite(units.LengthSquared)) return;
        Vector3 after = WallrunMovementV1.Kick(units, _modWallkickNormal);
        if (after == units || !float.IsFinite(after.LengthSquared)) return;
        Vector3 delta = after - units;
        Speed = new(delta.X == 0 ? before.X : before.X + delta.X / NativeVelocityUnitsPerSecond,
            delta.Y == 0 ? before.Y : before.Y + delta.Y / NativeVelocityUnitsPerSecond,
            delta.Z == 0 ? before.Z : before.Z + delta.Z / NativeVelocityUnitsPerSecond);
        _modWallkickFrame = _scene.FrameCount;
        ModCancelTraversalSlide();
        _timeSinceGrounded = 16;
        PlayHunterSfx(HunterSfx.Jump);
    }

    private float ModParkourGravity(float gravity, bool nativeOverride)
    {
        if (_scene.GameState.MovementModel != MovementModel.Parkour) return gravity;
        _modWallrunAppliedGravity = gravity;
        // Native messages own the persistent field and override tick. Observe
        // their flag before integration clears it; never scale scripted gravity.
        if (nativeOverride) { ModCancelParkourWallrun(); return gravity; }
        if (!_modWallrunning) return gravity;
        if (_modWallrunTicks <= 0 || !ModWallrunBodyAllowed || !ModWallrunInput(_modWallrunNormal)
            || !ModProbeWallrun(_modWallrunNormal))
        { _modWallrunning = false; return gravity; }
        if (_modWallrunGravityFrame != _scene.FrameCount)
        { _modWallrunGravityFrame = _scene.FrameCount; _modWallrunTicks--; }
        return _modWallrunAppliedGravity = gravity < 0 ? gravity * WallrunMovementV1.GravityScale : gravity;
    }

    // Called in the native standing-acceptance branch, including its small
    // resting-contact tolerance. Use incoming motion captured before clipping.
    private void ModObserveParkourSupport(in CollisionResult result)
    {
        if (ModParkourActive && Vector3.Dot(_modParkourIncomingContactSpeed, result.Plane.Xyz) <= .0005f
            && result.Field0 == 0 && result.EntityCollision == null
            && result.Slipperiness == 0 && !result.Flags.TestFlag(CollisionFlags.Damaging)
            && result.Terrain != Terrain.Lava && GroundSlopeMovement.Walkable(result.Plane.Xyz))
            _modParkourSafeSupport = true;
    }
    private void ModCompleteParkourWallrun(bool nativeJumpAccepted)
    {
        if (_scene.GameState.MovementModel != MovementModel.Parkour) return;
        if (_modParkourSafeSupport && !nativeJumpAccepted && Flags1.TestFlag(PlayerFlags1.Standing)
            && _modWallkickFrame != _scene.FrameCount)
        {
            _modWallrunTicks = WallrunMovementV1.AirborneTicks;
            _modWallrunning = false;
            return;
        }
        if (!ModWallrunBodyAllowed || _modWallrunTicks <= 0 || !_modParkourWallContact
            || _modParkourWallCorner || !ModWallrunInput(_modParkourWallNormal))
        { _modWallrunning = false; return; }
        if (_modWallrunning && _modWallrunNormal != _modParkourWallNormal)
        { _modWallrunning = false; return; }
        _modWallrunNormal = _modParkourWallNormal;
        _modWallrunning = ModProbeWallrun(_modWallrunNormal);
    }
}
