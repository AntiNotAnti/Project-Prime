using System;
using System.Runtime.CompilerServices;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class BeamProjectileEntity
{
    [InlineArray(10)]
    private struct DrawTrail { private Vector3 _first; }
    private struct DrawPose
    {
        internal Vector3 Position, Back, Direction, Up, Velocity;
        internal DrawTrail Trail;
    }
    private DrawPose _beamPreviousDraw, _beamCurrentDraw, _beamRenderDraw;
    private Matrix4 _beamRenderTransform;
    private EntityBase? _beamDrawOwner;
    private BeamFlags _beamDrawFlags;
    private int _beamDrawFunction;
    private float _beamDrawAge;
    private bool _beamDrawValid, _beamDrawCarry, _beamDrawActive, _beamDrawAnchor;
    private bool _beamDrawSampled;
    private Vector3 _beamAnchorPosition;
    internal float ModDrawTravelBoundSquared
    {
        get
        {
            // Projectile velocity is already a per-step displacement. Fast
            // legitimate shots must not be mistaken for entity teleports.
            float speed = MathF.Max(Velocity.Length, _beamCurrentDraw.Velocity.Length);
            float bound = speed + 1f / 4096;
            return float.IsFinite(bound) ? MathF.Max(16f, bound * bound) : 16f;
        }
    }

    // The existing entity history owns tick, scene, room and timeline
    // continuity. These value snapshots add only the beam's authored shape.
    internal void ModCaptureBeamDrawState(bool reset)
    {
        bool same = _beamDrawValid && ReferenceEquals(Owner, _beamDrawOwner)
            && Flags == _beamDrawFlags && DrawFuncId == _beamDrawFunction && Age >= _beamDrawAge;
        _beamPreviousDraw = _beamCurrentDraw;
        _beamCurrentDraw = new DrawPose { Position = Position, Back = BackPosition,
            Direction = Direction, Up = Up, Velocity = Velocity };
        for (int i = 0; i < 10; i++) _beamCurrentDraw.Trail[i] = PastPositions[i];
        _beamDrawCarry = !reset && same;
        if (!_beamDrawCarry) _beamPreviousDraw = _beamCurrentDraw;
        _beamDrawOwner = Owner; _beamDrawFlags = Flags; _beamDrawFunction = DrawFuncId; _beamDrawAge = Age;
        _beamDrawValid = true;
        _beamDrawActive = _beamDrawAnchor = false;
    }

    internal bool ModPrepareBeamDrawPose()
    {
        _beamDrawSampled = _beamDrawActive = false;
        if (!_beamDrawCarry || Flags.TestFlag(BeamFlags.Collided) || Flags.TestFlag(BeamFlags.Continuous)
            || !ReferenceEquals(Owner, _beamDrawOwner) || Flags != _beamDrawFlags
            || DrawFuncId != _beamDrawFunction || Age != _beamDrawAge
            || Position != _beamCurrentDraw.Position || BackPosition != _beamCurrentDraw.Back
            || Direction != _beamCurrentDraw.Direction || Up != _beamCurrentDraw.Up || Velocity != _beamCurrentDraw.Velocity
            || !ModTrySampleCapturedDrawTransform(out _beamRenderTransform, out double alpha)) return false;
        for (int i = 0; i < 10; i++) if (PastPositions[i] != _beamCurrentDraw.Trail[i]) return false;
        float t = (float)Math.Clamp(alpha, 0, 1);
        if (t >= 1 || !Finite(_beamPreviousDraw) || !Finite(_beamCurrentDraw)) return false;
        _beamRenderDraw.Position = _beamRenderTransform.Row3.Xyz;
        _beamRenderDraw.Back = Vector3.Lerp(_beamPreviousDraw.Back, _beamCurrentDraw.Back, t);
        _beamRenderDraw.Velocity = Vector3.Lerp(_beamPreviousDraw.Velocity, _beamCurrentDraw.Velocity, t);
        if (!CameraInfo.ModInterpolateDirection(_beamPreviousDraw.Direction, _beamCurrentDraw.Direction, t,
                out _beamRenderDraw.Direction)
            || !CameraInfo.ModInterpolateDirection(_beamPreviousDraw.Up, _beamCurrentDraw.Up, t,
                out _beamRenderDraw.Up)) return false;
        for (int i = 0; i < 10; i++)
            _beamRenderDraw.Trail[i] = Vector3.Lerp(_beamPreviousDraw.Trail[i], _beamCurrentDraw.Trail[i], t);
        _beamDrawSampled = _beamDrawActive = true;
        return true;
    }

    private static bool Finite(in DrawPose pose)
    {
        bool Valid(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        if (!Valid(pose.Position) || !Valid(pose.Back) || !Valid(pose.Direction) || !Valid(pose.Up) || !Valid(pose.Velocity)) return false;
        for (int i = 0; i < 10; i++) if (!Valid(pose.Trail[i])) return false;
        return true;
    }
    internal void ModFinishBeamDrawPose() => _beamDrawActive = _beamDrawAnchor = false;
    private void ClearBeamDrawState()
    {
        _beamPreviousDraw = _beamCurrentDraw = _beamRenderDraw = default;
        _beamRenderTransform = default;
        _beamDrawOwner = null;
        _beamDrawValid = _beamDrawCarry = _beamDrawSampled = _beamDrawActive = _beamDrawAnchor = false;
    }
    internal Vector3 ModBeamDrawPosition => _beamDrawAnchor ? _beamAnchorPosition : _beamDrawActive ? _beamRenderDraw.Position : Position;
    internal Vector3 ModBeamDrawBack => _beamDrawActive && !_beamDrawAnchor ? _beamRenderDraw.Back : BackPosition;
    internal Vector3 ModBeamDrawDirection => _beamDrawActive && !_beamDrawAnchor ? _beamRenderDraw.Direction : Direction;
    internal Vector3 ModBeamDrawVelocity => _beamDrawActive && !_beamDrawAnchor ? _beamRenderDraw.Velocity : Velocity;
    private Vector3 BeamDrawUp => _beamDrawActive && !_beamDrawAnchor ? _beamRenderDraw.Up : Up;
    internal Vector3 ModBeamDrawPast(int index) => _beamDrawAnchor
        ? index == 0 ? _beamAnchorPosition : PastPositions[index]
        : _beamDrawActive ? _beamRenderDraw.Trail[index] : PastPositions[index];

    private Vector3 ContinuousDrawStart()
    {
        if (!_scene.Services.IsReplica && !MphRead.Mods.SpectatorMode.IsSpectating && !DemoPlayback.IsActive
            && !MphRead.Mods.KillCam.Active && Owner is PlayerEntity player && ReferenceEquals(player, _scene.Players.Main)
            && (!NetSession.Active || player.SlotIndex == NetHooks.LocalSlot)
            && player.ModGetFirstPersonEffectTransform(out Matrix4 muzzle))
            return _scene.ModWorldSpaceViewModelPoint(muzzle.Row3.Xyz);
        return ModBeamDrawPast(8);
    }

    private Vector3 ContinuousDrawEndpoint(Vector3? confirmed)
    {
        if (confirmed.HasValue || _scene.Services.IsReplica || NetSession.Active || Target == null)
            return confirmed ?? ModBeamDrawPosition;
        Target.GetPosition(out Vector3 position);
        Vector3 delta;
        if (Target is PlayerEntity player)
        { player.ModGetDrawActorPose(out Vector3 actor, out _); delta = actor - player.Position; }
        else delta = Target.SimulationDrawPosition - Target.Position;
        return position + delta;
    }
}
