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
    // Exact contribution applied by UpdateCameraFirst before the later native
    // animation/bob tick. Reconstructing it at capture would be one tick ahead.
    private float _modTraversalCameraWalkBob;
    private long _modCrouchVisualGeneration;
    internal void ModCaptureTraversalVisual()
    {
        if (_modCrouchVisualGeneration != FrameTiming.PresentationGeneration)
        { _modCrouchPresentation.Reset(); _modCrouchGait.Reset(); }
        _modCrouchVisualGeneration = FrameTiming.PresentationGeneration;
        if (!ModCrouchBodyAllowed)
        { _modCrouchPresentation.Reset(); _modCrouchGait.Reset(); return; }
        if (_scene.Services.IsReplica) _modCrouchPresentation.Reset();
        else _modCrouchPresentation.Capture(_scene.FrameCount, ModCrouched);
        float cos = MathF.Cos(MathHelper.DegreesToRadians(_gunViewBob));
        bool lowPose = _scene.Services.IsReplica ? ModCrouched
            : _modCrouchPresentation.Sample(1, ModCrouched) > 0;
        _modCrouchGait.Capture(_scene.FrameCount, Position,
            lowPose && ModIsWalkingClip(Biped1Anim), ModSliding,
            Biped1Frame, _bipedModel1.AnimInfo.FrameCount[0],
            _modTraversalCameraWalkBob, cos * Fixed.ToFloat(20));
    }
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
