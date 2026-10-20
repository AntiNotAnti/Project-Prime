using System;
using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private TraversalCrouchPresentation _modCrouchPresentation;
    private TraversalGaitPresentation _modCrouchGait;
    private TraversalSlidePresentation _modSlidePresentation;
    private Vector3 _modTraversalPicturePosition;
    private ulong _modTraversalPictureFrame = ulong.MaxValue;
    // Exact contribution applied by UpdateCameraFirst before the later native
    // animation/bob tick. Reconstructing it at capture would be one tick ahead.
    private float _modTraversalCameraWalkBob;
    private long _modCrouchVisualGeneration;
    internal void ModCaptureTraversalVisual()
    {
        if (_modCrouchVisualGeneration != FrameTiming.PresentationGeneration)
        { _modCrouchPresentation.Reset(); _modCrouchGait.Reset(); _modSlidePresentation.Reset(); }
        _modCrouchVisualGeneration = FrameTiming.PresentationGeneration;
        if (!ModCrouchBodyAllowed)
        { _modCrouchPresentation.Reset(); _modCrouchGait.Reset(); _modSlidePresentation.Reset(); return; }
        if (_scene.Services.IsReplica) _modCrouchPresentation.Reset();
        else _modCrouchPresentation.Capture(_scene.FrameCount, ModCrouched);
        float cos = MathF.Cos(MathHelper.DegreesToRadians(_gunViewBob));
        bool lowPose = _scene.Services.IsReplica ? ModCrouched
            : _modCrouchPresentation.Sample(1, ModCrouched) > 0;
        _modCrouchGait.Capture(_scene.FrameCount, Position,
            lowPose && ModIsWalkingClip(Biped1Anim), ModSliding,
            Biped1Frame, _bipedModel1.AnimInfo.FrameCount[0],
            _modTraversalCameraWalkBob, cos * Fixed.ToFloat(20));
        Vector3 travel = _scene.FrameCount > 0 && _modTraversalPictureFrame == _scene.FrameCount - 1
            ? Position - _modTraversalPicturePosition : Vector3.Zero;
        if (!float.IsFinite(travel.LengthSquared) || travel.LengthSquared > 16)
        { _modSlidePresentation.Reset(); travel = Vector3.Zero; }
        // Frozen/Prime/locked/attached/platform-owned bodies retain their
        // accepted crouch shape, but native movement owns their presentation.
        // Inferred low travel must not grant those bodies a new slide pose.
        bool slideAllowed = ModMomentumBodyEligible;
        if (!slideAllowed) _modSlidePresentation.Reset();
        bool inferred = _scene.Services.IsReplica || Mods.Network.NetHooks.IsPuppet(this);
        _modSlidePresentation.Capture(_scene.FrameCount,
            slideAllowed && (ModSliding || inferred && lowPose && _modCrouchGait.Gliding && Flags1.TestFlag(PlayerFlags1.Standing)),
            // Retain heading in world space, so changing aim/body yaw cannot
            // rotate the extended leg or lean away from accepted travel.
            new(travel.X, travel.Z));
        _modTraversalPicturePosition = Position; _modTraversalPictureFrame = _scene.FrameCount;
    }
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
        if (_scene.Services.IsReplica || !ModCrouchBodyAllowed
            || _modCrouchVisualGeneration != FrameTiming.PresentationGeneration) return 0;
        float t = FrameTiming.HighRefreshPresentation ? (float)alpha : 1;
        float canonical = FrameTiming.HighRefreshPresentation && CameraInfo.ModHasLiveDrawHistory(alpha)
            ? _modCrouchPresentation.SampleCanonical(t, ModCrouched) : ModCrouched ? 1 : 0;
        float visual = _modCrouchPresentation.Sample(t, ModCrouched);
        float standingEye = Fixed.ToFloat(Values.AimYOffset);
        float lowEye = TraversalStance.EyeOffset(ModBipedBottom, Fixed.ToFloat(Values.MaxPickupHeight),
            Fixed.ToFloat(Values.BipedColRadius), standingEye, true);
        return (canonical - visual) * (standingEye - lowEye) + ModCrouchDrawBobCorrection(alpha);
    }
}
