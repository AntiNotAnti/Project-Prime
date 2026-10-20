using MphRead.Formats;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private TraversalCrouchPresentation _modCrouchPresentation;
    private long _modCrouchVisualGeneration;
    internal void ModCaptureTraversalVisual()
    {
        if (!ModCrouchBodyAllowed || _scene.Services.IsReplica)
        { _modCrouchPresentation.Reset(); return; }
        if (_modCrouchVisualGeneration != FrameTiming.PresentationGeneration)
            _modCrouchPresentation.Reset();
        _modCrouchVisualGeneration = FrameTiming.PresentationGeneration;
        _modCrouchPresentation.Capture(_scene.FrameCount, ModCrouched);
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
        return (canonical - visual) * (standingEye - lowEye);
    }
}
