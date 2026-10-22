using System;
using System.Buffers;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private PlayerGrappleState _modGrapple;
    private ushort _modGrappleNonce, _modGrappleCancelled, _modGrappleGeneration, _modGrappleLife, _modGrappleMatch;
    private ulong _modGrappleEpoch, _modGrappleSampleFrame = ulong.MaxValue, _modGrappleForceFrame = ulong.MaxValue;
    private ulong _modGrappleStarted, _modGrappleAcceptedInputFrame;
    private uint _modGrappleSourceFrame;
    private bool _modGrappleWasDown, _modGrappleHasSource, _modGrappleAdmissionPending;
    private int _modGrappleForceTicks, _modGrappleSelections, _modGrappleForces, _modGrappleGeometryQueries;
    internal PlayerGrappleState ModGrappleState => _modGrapple;
    internal ushort ModGrappleRequestNonce => _modGrappleNonce;
    internal int ModGrappleSelectionCount => _modGrappleSelections;
    internal int ModGrappleGeometryQueryCount => _modGrappleGeometryQueries;
    internal int ModGrappleForceCount => _modGrappleForces;
    internal bool ModGrappleOwnsMovement => _modGrapple.Active && Controls.Grapple.IsDown
        && _modGrapple.RequestNonce == _modGrappleNonce && _modGrappleCancelled != _modGrappleNonce;
    private bool ModGrappleControllerOwns => !_scene.Services.IsReplica
        && (_scene.MomentumOwnsPlayer(this) || NetSession.Active && NetSession.IsAuthority);
    private Vector3 ModGrappleOrigin => Position.AddY(ModBipedEyeOffset);
    private bool ModGrappleBodyAllowed => _scene.GameState.MovementModel == MovementModel.Parkour
        && _scene.MomentumMovementActive && ModMomentumBodyEligible
        && LoadFlags.TestFlag(LoadFlags.Spawned) && Hunter is >= Hunter.Samus and <= Hunter.Weavel
        && !Cheats.WalkThroughWalls && !ModCrouched && !ModSliding && !Controls.Crouch.IsDown
        && !WallClingHeld && !_modMantling && !_modMantlePending && !_modWallrunning && !_modWallkickPending;

    private void ModResetParkourGrappleForLife()
    {
        ModResetParkourAnimation();
        _modGrapple = default;
        _modGrappleNonce = _modGrappleCancelled = 0;
        _modGrappleWasDown = _modGrappleHasSource = _modGrappleAdmissionPending = false;
        _modGrappleSampleFrame = _modGrappleForceFrame = ulong.MaxValue;
        _modGrappleForceTicks = 0;
        _modGrappleGeneration = _modGrappleLife = _modGrappleMatch = 0; _modGrappleEpoch = 0;
    }
    private void ModEnsureGrappleIdentity()
    {
        if (!NetSession.Active || _scene.Services.IsReplica) return;
        ushort generation = NetPlayerLifecycle.Generation(SlotIndex), life = NetPlayerLifecycle.Get(SlotIndex);
        if (_modGrappleMatch == NetSession.CurrentMatchId && _modGrappleEpoch == NetSession.AuthorityEpoch
            && _modGrappleGeneration == generation && _modGrappleLife == life) return;
        ModResetParkourGrappleForLife();
        _modGrappleMatch = NetSession.CurrentMatchId; _modGrappleEpoch = NetSession.AuthorityEpoch;
        _modGrappleGeneration = generation; _modGrappleLife = life;
    }
    private void ModCancelParkourGrapple()
    {
        if (!ModGrappleControllerOwns) return;
        _modGrappleAdmissionPending = false;
        _modGrappleCancelled = _modGrappleNonce;
        if (!NetSession.Active || NetSession.IsAuthority)
            _modGrapple = PlayerGrappleState.Inactive(_modGrapple.RequestNonce);
    }
    // Capture and simulation both call this; a physical hold advances once on
    // the fixed scene clock, including the frames without an outgoing packet.
    internal void ModSampleGrappleControl()
    {
        if (_scene.Services.IsReplica || !_scene.MomentumOwnsPlayer(this)) return;
        ModEnsureGrappleIdentity();
        if (_modGrappleSampleFrame == _scene.FrameCount) return;
        _modGrappleSampleFrame = _scene.FrameCount;
        bool held = Controls.Grapple.IsDown;
        if (_scene.GameState.MovementModel != MovementModel.Parkour)
        { _modGrappleWasDown = held; ModCancelParkourGrapple(); return; }
        bool pressed = held && !_modGrappleWasDown;
        _modGrappleWasDown = held;
        if (pressed)
        {
            _modGrappleNonce = GrappleRequestOrdering.Next(_modGrappleNonce);
            _modGrappleCancelled = 0; _modGrappleForceTicks = 0;
            ModCancelParkourWallrun(); ModCancelParkourMantle();
            if (!ModGrappleBodyAllowed || Controls.Jump.IsDown)
                ModCancelParkourGrapple();
            else if (!NetSession.Active || NetSession.IsAuthority)
                _modGrappleAdmissionPending = true;
        }
        if (!held || !ModGrappleBodyAllowed || Controls.Jump.IsPressed) ModCancelParkourGrapple();
    }
    private void ModPrepareParkourGrapple()
    {
        if (!_modGrappleAdmissionPending) return;
        _modGrappleAdmissionPending = false;
        if (ModGrappleBodyAllowed && Controls.Grapple.IsDown && !Controls.Jump.IsDown)
            ModDecideGrapple(_modGrappleNonce, ModGunVector);
        else ModCancelParkourGrapple();
    }
    private void ModDecideGrapple(ushort nonce, Vector3 aim)
    {
        _modGrapple = PlayerGrappleState.Inactive(nonce);
        _modGrappleSelections++;
        if (ModGrappleBodyAllowed && ModTrySelectGrappleAnchor(ModGrappleOrigin, aim, out var anchor, out float length))
        {
            var accepted = new PlayerGrappleState(true, nonce, anchor, length);
            // A finite accepted pose can lie near the wire coordinate limit;
            // its nearby ray hit must still satisfy the complete state codec.
            if (!accepted.IsValid)
            { if (_scene.MomentumOwnsPlayer(this)) _modGrappleCancelled = nonce; return; }
            _modGrapple = accepted;
            _modGrappleStarted = _scene.FrameCount;
        }
        else if (_scene.MomentumOwnsPlayer(this)) _modGrappleCancelled = nonce;
    }
    // Only the jointly accepted, newest current-life pose/control seam calls
    // this on the scene thread. Recovered combat carriers never enter it.
    internal void ModObserveGrappleRequest(in IntentPacket intent)
    {
        if (_scene.Services.IsReplica || !NetSession.Active || !NetSession.IsAuthority
            || !NetRoomChange.GameplayReady || !NetSession.MatchesStream(intent.MatchId, intent.AuthorityEpoch)
            || !NetPlayerLifecycle.Matches(SlotIndex, intent.SlotGeneration, intent.LifeId)) return;
        ModEnsureGrappleIdentity();
        if (_modGrappleHasSource && !NetLifecycleTracker.Newer(intent.Frame, _modGrappleSourceFrame)) return;
        _modGrappleHasSource = true; _modGrappleSourceFrame = intent.Frame;
        var order = GrappleRequestOrdering.Compare(intent.GrappleRequestNonce, _modGrapple.RequestNonce);
        if (order is not (GrappleRequestOrder.Same or GrappleRequestOrder.Newer)) return;
        _modGrappleAcceptedInputFrame = _scene.FrameCount;
        bool held = (intent.Buttons & IntentButtons.Grapple) != 0;
        if (!held || (intent.Buttons & IntentButtons.Jump) != 0)
        {
            _modGrapple = PlayerGrappleState.Inactive(intent.GrappleRequestNonce);
            return;
        }
        if (order == GrappleRequestOrder.Same) return; // denial/release consumes the hold
        _modGrapple = PlayerGrappleState.Inactive(intent.GrappleRequestNonce);
        if ((intent.Buttons & IntentButtons.Jump) == 0 && ModGrappleBodyAllowed)
            ModDecideGrapple(intent.GrappleRequestNonce, intent.Aim);
    }
    private void ModMaintainParkourGrapple()
    {
        if (!ModGrappleControllerOwns) return;
        ModEnsureGrappleIdentity();
        if (!_modGrapple.Active || _scene.MomentumOwnsPlayer(this) && !ModGrappleOwnsMovement) return;
        bool authorityPuppet = NetSession.Active && NetSession.IsAuthority && !_scene.MomentumOwnsPlayer(this);
        if (!ModGrappleBodyAllowed || _scene.FrameCount - _modGrappleStarted >= GrappleMovementV1.MaxTicks
            || authorityPuppet && (!NetRoomChange.GameplayReady || !NetSession.RemoteIntentValid[SlotIndex]
                || _scene.FrameCount - _modGrappleAcceptedInputFrame > 30)
            || !ModGrappleLineClear(ModGrappleOrigin, _modGrapple.Anchor))
        {
            if (authorityPuppet) _modGrapple = PlayerGrappleState.Inactive(_modGrapple.RequestNonce);
            else ModCancelParkourGrapple();
        }
    }
    private void ModApplyParkourGrapple(bool movementAllowed, bool nativeJumpAccepted)
    {
        if (_scene.Services.IsReplica || !_scene.MomentumOwnsPlayer(this)) return;
        ModSampleGrappleControl();
        if (!movementAllowed || nativeJumpAccepted || Controls.Jump.IsPressed || !ModGrappleBodyAllowed)
        { ModCancelParkourGrapple(); return; }
        if (NetSession.Active && !NetSession.IsAuthority && NetSession.NetFrame - NetSession.SnapshotArrived > 30)
        { ModCancelParkourGrapple(); return; }
        if (!ModGrappleOwnsMovement || _modGrappleForceFrame == _scene.FrameCount) return;
        if (!ModGrappleLineClear(ModGrappleOrigin, _modGrapple.Anchor)
            || !GrappleMovementV1.TryConstrainVelocity(ModGrappleOrigin, _modGrapple.Anchor, _modGrapple.RopeLength,
                Speed * NativeVelocityUnitsPerSecond, _modGrappleForceTicks, out var constrained))
        { ModCancelParkourGrapple(); return; }
        _modGrappleForceFrame = _scene.FrameCount; _modGrappleForceTicks++; _modGrappleForces++;
        Speed = constrained / NativeVelocityUnitsPerSecond;
    }
    // Accepted state changes presentation/capability only. Same-life owner
    // snapshots never write Position, PrevPosition or velocity through this API.
    internal void ModApplyReplicatedGrapple(in PlayerGrappleState state, ushort generation, ushort life)
    {
        if (!state.IsValid) return;
        if (!_scene.Services.IsReplica)
        {
            ModEnsureGrappleIdentity();
            if (!NetSession.Active || !NetPlayerLifecycle.Matches(SlotIndex, generation, life)) return;
            var order = GrappleRequestOrdering.Compare(state.RequestNonce, _modGrapple.RequestNonce);
            if (state.RequestNonce == 0 && _modGrapple.RequestNonce != 0
                || order is GrappleRequestOrder.Older or GrappleRequestOrder.Ambiguous) return;
        }
        if (state.Active && (!_modGrapple.Active || state.RequestNonce != _modGrapple.RequestNonce))
            _modGrappleStarted = _scene.FrameCount;
        _modGrapple = state; _modGrappleGeneration = generation; _modGrappleLife = life;
    }
    internal void ModRestoreGrapplePresentation(in PlayerGrappleState state, ushort generation, ushort life)
    {
        ModResetParkourGrappleForLife();
        if (!_scene.Services.IsReplica) throw new InvalidOperationException("Grapple restore is passive only.");
        if (state.Active && (!_modGrapple.Active || state.RequestNonce != _modGrapple.RequestNonce))
            _modGrappleStarted = _scene.FrameCount;
        _modGrapple = state; _modGrappleGeneration = generation; _modGrappleLife = life;
    }
    private bool ModTrySelectGrappleAnchor(Vector3 origin, Vector3 aim, out Vector3 anchor, out float length)
    {
        anchor = default; length = 0;
        if (!GrappleMovementV1.Finite(origin) || !GrappleMovementV1.Finite(aim)
            || !float.IsFinite(aim.LengthSquared) || aim.LengthSquared < .0001f) return false;
        aim.Normalize();
        CollisionResult hit = default;
        if (!ModGrappleRay(origin, origin + aim * GrappleMovementV1.MaxRange, ref hit)
            || hit.EntityCollision != null || hit.Field0 != 0 || hit.Slipperiness != 0
            || hit.Flags.TestAny(CollisionFlags.Damaging | CollisionFlags.IgnorePlayers) || hit.Terrain == Terrain.Lava
            || !GrappleMovementV1.Finite(hit.Plane.Xyz) || MathF.Abs(hit.Plane.Xyz.LengthSquared - 1) > .001f) return false;
        // Native result.Position is the nearest face intersection, never a
        // plane-only projection or a client-supplied world point.
        Vector3 point = hit.Position;
        float distance = (point - origin).Length;
        if (!GrappleMovementV1.Finite(point) || !GrappleMovementV1.ValidAcceptedLength(distance)
            || !ModGrappleActorsClear(origin, point)) return false;
        anchor = point; length = distance; return true;
    }
    private bool ModGrappleRay(Vector3 from, Vector3 to, ref CollisionResult hit)
    {
        _modGrappleGeometryQueries++;
        using var candidates = CollisionDetection.GetCandidatesForLimits(from, to, 0, null, Vector3.Zero, true, _scene);
        if (candidates.Count > 8192) return false;
        long faces = 0;
        for (int i = 0; i < candidates.Count; i++)
            if ((faces += candidates[i].Entry.DataCount) > 4096) return false;
        return CollisionDetection.CheckBetweenPoints(candidates, from, to, TestFlags.None, _scene, ref hit);
    }
    private bool ModGrappleLineClear(Vector3 from, Vector3 anchor)
    {
        _modGrappleGeometryQueries++;
        Vector3 delta = anchor - from;
        float distance = delta.Length;
        if (!GrappleMovementV1.Finite(from) || !GrappleMovementV1.Finite(anchor)
            || !float.IsFinite(distance) || distance <= .0001f || distance > GrappleMovementV1.MaxRange) return false;
        Vector3 end = anchor - delta / distance * MathF.Min(.03f, distance / 2);
        // An over-budget query must refuse; distinguish clear from saturated.
        using var candidates = CollisionDetection.GetCandidatesForLimits(from, end, 0, null, Vector3.Zero, true, _scene);
        if (candidates.Count > 8192) return false;
        long faces = 0;
        for (int i = 0; i < candidates.Count; i++) if ((faces += candidates[i].Entry.DataCount) > 4096) return false;
        CollisionResult hit = default;
        return !CollisionDetection.CheckBetweenPoints(candidates, from, end, TestFlags.None, _scene, ref hit)
            && ModGrappleActorsClear(from, end);
    }
    private static bool ModGrappleSphereBlocks(Vector3 from, Vector3 to, Vector3 center, float radius)
    {
        if (!GrappleMovementV1.Finite(center) || !float.IsFinite(radius) || radius < 0) return true;
        Vector3 delta = to - from;
        float t = Math.Clamp(Vector3.Dot(center - from, delta) / delta.LengthSquared, 0, 1);
        return (from + delta * t - center).LengthSquared <= radius * radius;
    }
    private bool ModGrappleActorsClear(Vector3 from, Vector3 to)
    {
        int count = 0;
        foreach (DoorEntity door in _scene.GetDoorEntities())
        {
            if (++count > 256) return false;
            if (!door.Flags.TestFlag(DoorFlags.Open) && !door.ConnectorInactive
                && ModGrappleSphereBlocks(from, to, door.LockPosition, door.Radius + 1.25f)) return false;
        }
        foreach (ForceFieldEntity field in _scene.GetForceFieldEntities())
        {
            if (++count > 256) return false;
            if (field.Active && ModGrappleSphereBlocks(from, to, field.Position,
                MathF.Sqrt(field.Width * field.Width + field.Height * field.Height) + 1.25f)) return false;
        }
        foreach (PlayerEntity other in _scene.GetPlayerEntities())
        {
            if (++count > 256) return false;
            if (ReferenceEquals(this, other) || other.Health <= 0 || !other.ModIsInPlay) continue;
            if (ModGrappleSphereBlocks(from, to, other.Position.AddY((other.ModBipedTop + other.ModBipedBottom) / 2),
                MathF.Max(2f, other.ModBipedTop - other.ModBipedBottom))) return false;
        }
        foreach (HalfturretEntity turret in _scene.GetHalfturretEntities())
        {
            if (++count > 256) return false;
            if (turret.Health > 0 && ModGrappleSphereBlocks(from, to, turret.Position, 2f)) return false;
        }
        foreach (EnemyInstanceEntity enemy in _scene.GetEnemyInstanceEntities())
        {
            if (++count > 256) return false;
            CollisionResult hit = default;
            if (enemy.Health > 0 && CollisionDetection.CheckCylinderOverlapVolume(enemy.HurtVolume, from, to, .03f, ref hit)) return false;
        }
        return true;
    }
    private void ModDrawGrapple(Vector3 drawPosition)
    {
        if (!_modGrapple.Active || !_modGrapple.IsValid || _health <= 0 || IsAltForm || IsMorphing
            || !LoadFlags.TestFlag(LoadFlags.Spawned) || Mods.Headless.Active) return;
        if (!_scene.Services.IsReplica && _scene.MomentumOwnsPlayer(this) && !ModGrappleOwnsMovement) return;
        Vector3 from = drawPosition.AddY(ModBipedEyeOffset), to = _modGrapple.Anchor, delta = to - from;
        if (!GrappleMovementV1.Finite(from) || delta.LengthSquared < .0001f || delta.LengthSquared > 625) return;
        Vector3 side = Vector3.Cross(delta.Normalized(), Vector3.UnitY);
        if (side.LengthSquared < .0001f) side = Vector3.UnitX; else side.Normalize();
        side *= .015f;
        var points = ArrayPool<Vector3>.Shared.Rent(4);
        points[0] = from - side; points[1] = from + side; points[2] = to + side; points[3] = to - side;
        _scene.AddRenderItem(CullingMode.Neither, _scene.GetNextPolygonId(), new Vector4(.2f, .85f, 1, 1), RenderItemType.Quad, points, 4);
    }
}
