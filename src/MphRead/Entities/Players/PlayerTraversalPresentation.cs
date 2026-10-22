using System;
using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private TraversalCrouchPresentation _modCrouchPresentation;
    private bool _modTuckCameraSuppressed;
    private TraversalGaitPresentation _modCrouchGait;
    private TraversalSlidePresentation _modSlidePresentation;
    private TraversalWalkPresentation _modCrouchWalk;
    private float _modCrouchWalkReach;
    private Vector3 _modTraversalPicturePosition;
    private ulong _modTraversalPictureFrame = ulong.MaxValue;
    // Exact contribution applied by UpdateCameraFirst before the later native
    // animation/bob tick. Reconstructing it at capture would be one tick ahead.
    private float _modTraversalCameraWalkBob;
    private long _modCrouchVisualGeneration;
    internal void ModCaptureTraversalVisual()
    {
        if (_modCrouchVisualGeneration != FrameTiming.PresentationGeneration)
        { _modCrouchPresentation.Reset(); _modCrouchGait.Reset(); _modSlidePresentation.Reset(); _modCrouchWalk.Reset(); }
        _modCrouchVisualGeneration = FrameTiming.PresentationGeneration;
        if (!ModCrouchBodyAllowed)
        { _modCrouchPresentation.Reset(); _modCrouchGait.Reset(); _modSlidePresentation.Reset(); _modCrouchWalk.Reset(); return; }
        // Consume the completed cosmetic playout clock, never a draw/viewer
        // clock or burst-sized raw snapshot displacement. Canonical body facts
        // remain untouched; accepted velocity classifies remote low travel.
        bool inferred = _scene.Services.IsReplica || NetHooks.IsPuppet(this);
        Vector3 position = Position;
        if (_scene.Services.IsReplica)
        {
            if (_scene.ReplayPoses?.Sample(SlotIndex, 1f, out Vector3 replayPosition, out _) == true)
                position = replayPosition;
        }
        else if (inferred && NetSmoothing.SampleCompletedPresentation(SlotIndex, out Vector3 remotePosition, out bool alt))
            position = _scene.PlayerReplication.InFormFor(this, remotePosition, alt);
        float? acceptedSpeed = inferred ? Speed.Xz.Length * NativeVelocityUnitsPerSecond : null;
        if (_scene.Services.IsReplica) _modCrouchPresentation.Reset();
        else _modCrouchPresentation.Capture(_scene.FrameCount, ModCrouched);
        if (!ModCrouched && _modCrouchPresentation.Sample(0, false) == 0)
            _modTuckCameraSuppressed = false;
        float cos = MathF.Cos(MathHelper.DegreesToRadians(_gunViewBob));
        bool lowPose = _scene.Services.IsReplica ? ModCrouched
            : _modCrouchPresentation.Sample(1, ModCrouched) > 0;
        _modCrouchGait.Capture(_scene.FrameCount, position,
            lowPose && ModIsWalkingClip(Biped1Anim), ModSliding,
            _modTraversalCameraWalkBob, cos * Fixed.ToFloat(20), acceptedSpeed);
        Vector3 travel = _scene.FrameCount > 0 && _modTraversalPictureFrame == _scene.FrameCount - 1
            ? position - _modTraversalPicturePosition : Vector3.Zero;
        if (!float.IsFinite(travel.LengthSquared) || travel.LengthSquared > 16)
        { _modSlidePresentation.Reset(); travel = Vector3.Zero; }
        // Frozen/Prime/locked/attached/platform-owned bodies retain their
        // accepted crouch shape, but native movement owns their presentation.
        // Inferred low travel must not grant those bodies a new slide pose.
        bool slideAllowed = ModMomentumBodyEligible;
        if (!slideAllowed) _modSlidePresentation.Reset();
        _modSlidePresentation.Capture(_scene.FrameCount,
            slideAllowed && (ModSliding || inferred && lowPose && _modCrouchGait.Gliding && Flags1.TestFlag(PlayerFlags1.Standing)),
            // Retain heading in world space, so changing aim/body yaw cannot
            // rotate the extended leg or lean away from accepted travel.
            new(travel.X, travel.Z));
        var skeleton = CrouchSkeletons.GetValue(_bipedModel1.Model, value => new(value));
        _modCrouchWalkReach = skeleton.Reach * Metadata.HunterScales[Hunter];
        Vector3 normal = _modRampSupportFrame == _scene.FrameCount && GroundSlopeMovement.Walkable(_modRampSupport)
            ? _modRampSupport : Vector3.UnitY;
        if (!slideAllowed) _modCrouchWalk.Reset();
        _modCrouchWalk.Capture(_scene.FrameCount, position,
            slideAllowed && lowPose && Flags1.TestFlag(PlayerFlags1.Standing)
                && ModIsWalkingClip(Biped1Anim) && !_modCrouchGait.Gliding,
            normal, _modCrouchWalkReach,
            heldWalking: inferred && acceptedSpeed is > .00001f);
        _modTraversalPicturePosition = position; _modTraversalPictureFrame = _scene.FrameCount;
    }
    private float ModCrouchWalkAmount => _modCrouchWalk.Sample(FrameTiming.HighRefreshPresentation
        ? (float)FrameTiming.PresentationAlpha : 1).Amount;
    private float ModSlideVisualAmount => _modCrouchVisualGeneration == FrameTiming.PresentationGeneration
        && ModCrouchBodyAllowed && ModMomentumBodyEligible ? _modSlidePresentation.Sample(FrameTiming.HighRefreshPresentation
            ? (float)FrameTiming.PresentationAlpha : 1) : 0;
    private float ModCrouchVisualAmount => _scene.Services.IsReplica || !ModCrouchBodyAllowed
        || _modCrouchVisualGeneration != FrameTiming.PresentationGeneration
        ? ModCrouched ? 1 : 0
        : _modCrouchPresentation.Sample(FrameTiming.HighRefreshPresentation ? (float)FrameTiming.PresentationAlpha : 1, ModCrouched);
    // Replace only the instantaneous stance contribution in the rendered camera.
    // Translation, aim, native CameraInfo, collision and shot origins stay exact.
    private float ModCrouchDrawEyeCorrection(double alpha)
    {
        if (_scene.Services.IsReplica || !ModCrouchBodyAllowed || _modTuckCameraSuppressed
            || _modCrouchVisualGeneration != FrameTiming.PresentationGeneration) return 0;
        float t = FrameTiming.HighRefreshPresentation ? (float)alpha : 1;
        float canonical = FrameTiming.HighRefreshPresentation && CameraInfo.ModHasLiveDrawHistory(alpha)
            ? _modCrouchPresentation.SampleCanonical(t, ModCrouched) : ModCrouched ? 1 : 0;
        float visual = _modCrouchPresentation.Sample(t, ModCrouched);
        float standingEye = Fixed.ToFloat(Values.AimYOffset);
        float lowEye = TraversalStance.EyeOffset(ModNativeBipedBottom, Fixed.ToFloat(Values.MaxPickupHeight),
            Fixed.ToFloat(Values.BipedColRadius), standingEye, true);
        return (canonical - visual) * (standingEye - lowEye) + ModCrouchDrawBobCorrection(alpha);
    }
}
