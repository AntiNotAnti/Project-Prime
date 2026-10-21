using System;
using MphRead.Mods.Network;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private byte _modWallVisualCue;
    private ulong _modWallVisualReceived;
    private uint _modWallVisualFrame, _modWallVisualRevision;
    private bool _modWallVisualHasFrame, _modWallVisualHasRevision;
    private ulong _modWallKickVisualFrame = ulong.MaxValue;
    private Vector3 _modWallKickVisualNormal;
    private ulong _modWallVisualContactAfter;
    private bool _modWallVisualContactRetired;
    private bool ModWallAnimationBodyAllowed => ModParkourAnimationNativeBodyAllowed
        && (!ModCrouched || ModTucked) && !ModSliding;

    internal byte ModLocalWallVisualCue
    {
        get
        {
            if (!ModWallAnimationBodyAllowed || !_scene.MomentumOwnsPlayer(this)
                || _modMantling || ModGrappleOwnsMovement || Cheats.WalkThroughWalls) return 0;
            if (!Flags1.TestFlag(PlayerFlags1.Standing) && _modWallKickVisualFrame != ulong.MaxValue
                && _scene.FrameCount >= _modWallKickVisualFrame && _scene.FrameCount - _modWallKickVisualFrame < 12)
                return ParkourWallVisualCodec.Encode(3, _modWallKickVisualNormal);
            if (_modWallrunning) return ParkourWallVisualCodec.Encode(2, _modWallrunNormal);
            // Observe the completed native collision witness, including resting
            // contact at the speed cap. Never infer a wall from speed or Jump.
            if (_modParkourContactFrame == ulong.MaxValue || _scene.FrameCount < _modParkourContactFrame
                || _scene.FrameCount - _modParkourContactFrame > 1 || !_modParkourWallNear
                || _modWallVisualContactRetired && _modParkourContactFrame <= _modWallVisualContactAfter
                || _modParkourWallCorner || !ModWallStrafePictureInput(_modParkourWallNormal)) return 0;
            return ParkourWallVisualCodec.Encode(1, _modParkourWallNormal);
        }
    }
    private bool ModWallStrafePictureInput(Vector3 normal)
    {
        float forward = Controls.MoveUp.IsDown ? Controls.AnalogScaleY(1) : 0;
        float strafe = Controls.MoveRight.IsDown ? Controls.AnalogScaleX(1)
            : Controls.MoveLeft.IsDown ? -Controls.AnalogScaleX(-1) : 0;
        Vector3 wish = new Vector3(_field70, 0, _field74) * forward
            + new Vector3(-_field78, 0, -_field7C) * strafe;
        if (forward <= 0 || MathF.Abs(strafe) < .000001f || !float.IsFinite(wish.LengthSquared)
            || wish.LengthSquared < .000001f || !float.IsFinite(Speed.LengthSquared)) return false;
        wish.Normalize(); Vector3 tangent = new(-normal.Z, 0, normal.X);
        return Vector3.Dot(wish, normal) < -.000001f && MathF.Abs(Vector3.Dot(wish, tangent)) > .000001f
            && MathF.Abs(Vector3.Dot(Speed, tangent)) > .0001f;
    }
    internal byte ModPublishedWallVisualCue => _scene.MomentumOwnsPlayer(this) ? ModLocalWallVisualCue
        : ModWallAnimationBodyAllowed && (_scene.Services.IsReplica || _scene.FrameCount >= _modWallVisualReceived
            && _scene.FrameCount - _modWallVisualReceived <= 30) ? _modWallVisualCue : (byte)0;
    private void ModResetWallVisual()
    {
        _modWallVisualCue = 0; _modWallVisualReceived = 0;
        _modWallVisualHasFrame = _modWallVisualHasRevision = false;
        _modWallKickVisualFrame = ulong.MaxValue; _modWallKickVisualNormal = default;
        _modWallVisualContactRetired = false;
    }
    private void ModRetireWallPicture()
    {
        _modWallVisualCue = 0; _modWallKickVisualFrame = ulong.MaxValue;
        _modWallVisualContactRetired = true; _modWallVisualContactAfter = _scene.FrameCount;
    }
    // Called only at a successful native kick impulse, after all admission vetoes.
    private void ModRememberWallKickVisual(Vector3 normal)
    { _modWallKickVisualNormal = normal; _modWallKickVisualFrame = _scene.FrameCount + 1; }
    private void ModSetAcceptedWallVisual(byte cue)
    {
        _modWallVisualCue = ModWallAnimationBodyAllowed && ParkourWallVisualCodec.Valid(cue)
            && !ModGrappleState.Active ? cue : (byte)0;
        _modWallVisualReceived = _scene.FrameCount;
    }
    internal void ModApplyReplicatedWallVisual(byte cue, uint sourceFrame, bool local = false, uint slowRevision = 0)
    {
        if (local && !_scene.Services.IsReplica) return;
        if (slowRevision != 0)
        {
            if (_modWallVisualHasRevision && !NetLifecycleTracker.Newer(slowRevision, _modWallVisualRevision)) return;
            _modWallVisualHasRevision = true; _modWallVisualRevision = slowRevision;
        }
        else
        {
            if (_modWallVisualHasFrame && !NetLifecycleTracker.Newer(sourceFrame, _modWallVisualFrame)) return;
            _modWallVisualHasFrame = true; _modWallVisualFrame = sourceFrame;
        }
        ModSetAcceptedWallVisual(cue);
    }
    internal void ModRestoreWallVisual(byte cue)
    { ModResetWallVisual(); ModApplyReplicatedWallVisual(cue, 0); }
}
