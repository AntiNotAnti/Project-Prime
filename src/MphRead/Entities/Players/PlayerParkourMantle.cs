using System;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Movement;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private bool _modMantling, _modMantlePending, _modMantleSeed, _modMantleSeedBlocked;
    private Vector3 _modMantleNormal, _modMantleSeedNormal, _modMantleStart, _modMantleRaised, _modMantleTarget;
    private float _modMantleSeedPlane;
    private int _modMantleStage, _modMantleTicks, _modMantleStalled;
    private Vector3 _modMantleBefore;
    private ulong _modMantleApplied = ulong.MaxValue, _modMantleComplete = ulong.MaxValue;
    internal bool ModParkourMantleActive => _modMantling;
    internal int ModParkourMantleTicks => _modMantleTicks;
    internal bool ModParkourMantleApplied(ulong frame) => _modMantleApplied == frame;
    internal bool ModParkourMantleCompleted(ulong frame) => _modMantleComplete == frame;

    private void ModCancelParkourMantle()
    {
        _modMantling = _modMantlePending = _modMantleSeed = false;
        _modMantleTicks = _modMantleStage = _modMantleStalled = 0;
    }

    private bool ModMantleBodyAllowed => ModParkourActive && !ModCrouched && !ModSliding
        && !Controls.Crouch.IsDown && !WallClingHeld && !_modWallrunning && !_modWallkickPending
        && Hunter is >= Hunter.Samus and <= Hunter.Weavel;
    private bool ModMantleInput(Vector3 normal) => Controls.MoveUp.IsDown && Controls.AnalogScaleY(1) > 0
        && Vector3.Dot(new Vector3(_field70, 0, _field74), normal) < -.5f;

    // Kept separate from wallrun's stricter whole-wall seed: finite top-edge
    // contacts cannot grant a mantle, but need not invalidate a real front face.
    private void ModObserveMantleContact(in CollisionResult hit, bool penetrating)
    {
        if (!ModParkourActive) return;
        if (hit.EntityCollision != null || hit.Slipperiness != 0 || hit.Flags.TestFlag(CollisionFlags.Damaging)
            || hit.Terrain == Terrain.Lava || !float.IsFinite(hit.Plane.LengthSquared))
        { _modMantleSeedBlocked = true; return; }
        if (hit.Field0 != 0 || MathF.Abs(hit.Plane.Y) > 1f / 4096 || !penetrating) return;
        Vector3 normal = hit.Plane.Xyz;
        if (MathF.Abs(normal.LengthSquared - 1) > .001f) { _modMantleSeedBlocked = true; return; }
        normal.Y = 0; normal.Normalize();
        if (_modMantleSeed && normal != _modMantleSeedNormal) _modMantleSeedBlocked = true;
        _modMantleSeed = true; _modMantleSeedNormal = normal; _modMantleSeedPlane = hit.Plane.W;
    }

    private void ModPrepareParkourMantle()
    {
        if (_scene.GameState.MovementModel != MovementModel.Parkour) return;
        _modMantlePending = false;
        if (_modMantling)
        {
            if (!ModMantleBodyAllowed || !ModMantleInput(_modMantleNormal)) ModCancelParkourMantle();
            return;
        }
        if (!Controls.Jump.IsPressed || !ModMantleBodyAllowed || !_modMantleSeed || _modMantleSeedBlocked
            || _scene.FrameCount == 0 || _modParkourContactFrame != _scene.FrameCount - 1
            || !ModMantleInput(_modMantleSeedNormal) || !MantleMovementV1.Finite(Speed)
            || Speed.Length * NativeVelocityUnitsPerSecond > 18 || !ModCanOccupyBiped(false)) return;
        float radius = Fixed.ToFloat(Values.BipedColRadius);
        float gap = Vector3.Dot(Position, _modMantleSeedNormal) - _modMantleSeedPlane - radius;
        if (gap < -CollisionRecoverySkin || gap > CollisionRecoverySkin) return;
        _modMantleNormal = _modMantleSeedNormal;
        // The bound is origin-to-origin. Clearing a face needs two radii plus
        // landing margin, rather than an arbitrary one-unit inward displacement.
        float travel = 2 * radius + gap + MantleMovementV1.Clearance;
        if (!float.IsFinite(travel) || travel > MantleMovementV1.MaxTravel) return;
        Vector3 inward = Position - _modMantleNormal * travel;
        float feet = Position.Y + ModBipedBottom;
        CollisionResult top = default;
        if (!CollisionDetection.CheckBetweenPoints(inward.WithY(feet + MantleMovementV1.MaxRise + .01f),
            inward.WithY(feet + .05f), TestFlags.Players, _scene, ref top)
            || top.EntityCollision != null || top.Field0 != 0 || top.Plane.Xyz != Vector3.UnitY
            || top.Slipperiness != 0 || top.Flags.TestFlag(CollisionFlags.Damaging) || top.Terrain == Terrain.Lava
            || !float.IsFinite(top.Plane.W)) return;
        float rise = top.Plane.W - feet;
        if (rise <= .05f || rise + MantleMovementV1.Clearance > MantleMovementV1.MaxRise) return;
        _modMantleStart = Position;
        _modMantleTarget = inward.WithY(top.Plane.W - ModBipedBottom);
        _modMantleRaised = Position.WithY(_modMantleTarget.Y + MantleMovementV1.Clearance);
        if (!ModMantleSupported(_modMantleTarget) || !ModMantleRoute(Position, _modMantleRaised)
            || !ModMantleRoute(_modMantleRaised, _modMantleTarget.AddY(MantleMovementV1.Clearance))
            || !ModMantleRoute(_modMantleTarget.AddY(MantleMovementV1.Clearance), _modMantleTarget)) return;
        _modMantlePending = true;
    }

    private void ModApplyParkourMantle(bool movementAllowed)
    {
        if (!_modMantling && !_modMantlePending) return;
        if (!movementAllowed || !ModMantleBodyAllowed || !ModMantleInput(_modMantleNormal))
        { ModCancelParkourMantle(); return; }
        if (_modMantlePending)
        {
            _modMantlePending = false; _modMantling = true;
            _modMantleStage = _modMantleTicks = _modMantleStalled = 0;
        }
        if (_modMantleTicks >= MantleMovementV1.Ticks || _modMantleStalled >= 3
            || (Position - _modMantleStart).Xz.Length > MantleMovementV1.MaxTravel + CollisionRecoverySkin
            || Position.Y - _modMantleStart.Y > MantleMovementV1.MaxRise + MantleMovementV1.Clearance + CollisionRecoverySkin
            || !ModMantleSupported(_modMantleTarget))
        { ModCancelParkourMantle(); return; }
        Vector3 target = _modMantleStage == 0 ? _modMantleRaised
            : _modMantleStage == 1 ? _modMantleTarget.AddY(MantleMovementV1.Clearance) : _modMantleTarget.AddY(-.01f);
        Vector3 delta = target - Position;
        float maximum = _modMantleStage == 0 ? MantleMovementV1.LiftStep
            : _modMantleStage == 1 ? MantleMovementV1.AcrossStep : MantleMovementV1.SettleStep;
        if (!MantleMovementV1.Finite(delta) || !ModMantleRoute(Position,
            _modMantleStage == 2 ? _modMantleTarget : target))
        { ModCancelParkourMantle(); return; }
        if (delta.Length > maximum) delta *= maximum / delta.Length;
        _modMantleBefore = Position;
        // This runs after native gravity and before native Position += Speed/2.
        // No Position/PrevPosition write; native collision owns every step.
        Speed = delta * 2;
        _modMantleApplied = _scene.FrameCount; _modMantleTicks++;
    }

    private void ModCompleteParkourMantle()
    {
        if (!_modMantling || _modMantleApplied != _scene.FrameCount) return;
        if (!ModMantleBodyAllowed) { ModCancelParkourMantle(); return; }
        _modMantleStalled = (Position - _modMantleBefore).LengthSquared < .000001f ? _modMantleStalled + 1 : 0;
        if (_modMantleStage == 0 && (Position - _modMantleRaised).LengthSquared < .00001f) _modMantleStage = 1;
        else if (_modMantleStage == 1 && (Position - _modMantleTarget.AddY(MantleMovementV1.Clearance)).LengthSquared < .00001f)
            _modMantleStage = 2;
        if (_modMantleStage == 2 && _modParkourSafeSupport && Flags1.TestFlag(PlayerFlags1.Standing)
            && MathF.Abs(Position.Y - _modMantleTarget.Y) <= CollisionRecoverySkin
            && (Position - _modMantleTarget).Xz.Length <= CollisionRecoverySkin && ModMantleSupported(Position))
        { _modMantleComplete = _scene.FrameCount; ModCancelParkourMantle(); }
    }

    private bool ModMantleSupported(Vector3 origin)
    {
        float radius = Fixed.ToFloat(Values.BipedColRadius) + MantleMovementV1.Clearance;
        Vector3 foot = origin.AddY(ModBipedBottom);
        using var candidates = CollisionDetection.GetCandidatesForLimits(foot, foot, radius + .05f,
            null, Vector3.Zero, includeEntities: true, _scene);
        if (candidates.Count > 8192) return false;
        Span<Vector3> vertices = stackalloc Vector3[64];
        candidates.SeenFaces.Clear(); int faces = 0; bool supported = false;
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.EntityCollision != null) return false;
            if (candidate.Collision.Info is not MphCollisionInfoBase info) return false;
            for (int j = 0; j < candidate.Entry.DataCount; j++)
            {
                var face = info.RuntimeData[info.RuntimeDataIndices[candidate.Entry.DataStartIndex + j]];
                if (face.IgnorePlayers || !candidates.SeenFaces.Add(face)) continue;
                if (++faces > 4096 || face.PointIndexCount < 3 || face.PointIndexCount > vertices.Length) return false;
                Vector4 plane = info.Planes[face.PlaneIndex];
                for (int p = 0; p < face.PointIndexCount; p++)
                    vertices[p] = info.Points[info.RuntimePointIndices[face.PointStartIndex + p]] + candidate.Collision.Translation;
                bool unsafeFace = face.Slipperiness != 0 || face.Terrain == Terrain.Lava
                    || face.Flags.TestFlag(CollisionFlags.Damaging);
                if (unsafeFace)
                {
                    Vector3 minimum = foot - new Vector3(radius, CollisionRecoverySkin, radius);
                    Vector3 maximum = foot + new Vector3(radius, CollisionRecoverySkin, radius);
                    for (int p = 1; p < face.PointIndexCount - 1; p++)
                        if (MantleMovementV1.TriangleOverlapsBox(vertices[0], vertices[p], vertices[p + 1], minimum, maximum))
                            return false;
                }
                else if (plane.Xyz == Vector3.UnitY
                    && MantleMovementV1.SupportsDisk(vertices[..face.PointIndexCount], foot, radius)) supported = true;
            }
        }
        return supported;
    }

    private bool ModMantleRoute(Vector3 from, Vector3 to)
    {
        if (!MantleMovementV1.Finite(from) || !MantleMovementV1.Finite(to)
            || !ModCanOccupyBiped(false, origin: from) || !ModCanOccupyBiped(false, origin: to)) return false;
        float radius = Fixed.ToFloat(Values.BipedColRadius);
        Vector3 tangent = new(-_modMantleNormal.Z, 0, _modMantleNormal.X);
        Vector3 Local(Vector3 v) => new(Vector3.Dot(v, tangent), v.Y, Vector3.Dot(v, _modMantleNormal));
        Vector3 a = Local(from), b = Local(to);
        Vector3 minimum = Vector3.ComponentMin(a, b) + new Vector3(-radius, ModBipedBottom, -radius);
        Vector3 maximum = Vector3.ComponentMax(a, b) + new Vector3(radius, ModBipedTop, radius);
        Vector3 worldMin = Vector3.ComponentMin(from, to) + new Vector3(-radius * 1.5f, ModBipedBottom, -radius * 1.5f);
        Vector3 worldMax = Vector3.ComponentMax(from, to) + new Vector3(radius * 1.5f, ModBipedTop, radius * 1.5f);
        using var candidates = CollisionDetection.GetCandidatesForLimits(from, to, 0, worldMin, worldMax,
            includeEntities: true, _scene);
        if (candidates.Count > 8192) return false;
        candidates.SeenFaces.Clear(); int faces = 0;
        Span<Vector3> vertices = stackalloc Vector3[64];
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            if (candidate.Collision.Info is not MphCollisionInfoBase info) return false;
            for (int j = 0; j < candidate.Entry.DataCount; j++)
            {
                var face = info.RuntimeData[info.RuntimeDataIndices[candidate.Entry.DataStartIndex + j]];
                if (face.IgnorePlayers || candidate.EntityCollision == null && !candidates.SeenFaces.Add(face)) continue;
                if (++faces > 4096 || face.PointIndexCount < 3 || face.PointIndexCount > vertices.Length) return false;
                for (int p = 0; p < face.PointIndexCount; p++)
                {
                    Vector3 world = ModClearanceWorldPoint(candidate, info.Points[info.RuntimePointIndices[face.PointStartIndex + p]]);
                    if (!MantleMovementV1.Finite(world)) return false;
                    vertices[p] = Local(world);
                }
                // Triangle fans over-cover concave polygons, which can refuse a
                // valid route but cannot create a hole in the clearance proof.
                for (int p = 1; p < face.PointIndexCount - 1; p++)
                    if (MantleMovementV1.TriangleOverlapsBox(vertices[0], vertices[p], vertices[p + 1], minimum, maximum))
                        return false;
            }
        }
        return true;
    }
}
