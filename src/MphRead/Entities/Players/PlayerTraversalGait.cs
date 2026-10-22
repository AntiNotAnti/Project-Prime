using System;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private AnimationInfo? _modCrouchLowerAnimation;
    private static bool ModIsWalkingClip(PlayerAnimation clip) => clip is PlayerAnimation.WalkForward
        or PlayerAnimation.WalkBackward or PlayerAnimation.WalkLeft or PlayerAnimation.WalkRight;
    private bool ModCrouchGaitActive => ModCrouchBodyAllowed && ModMomentumBodyEligible && ModCrouchVisualAmount > 0
        && _modCrouchGait.Ready && _modCrouchVisualGeneration == FrameTiming.PresentationGeneration
        && (ModSlideVisualAmount > 0 || ModCrouchWalkAmount > 0);

    // A separate animation cursor belongs exclusively to the picture. Native
    // animation frames, canonical restoration and source-time muzzle reads do
    // not consume the procedural low step or the slide's neutral leg pose.
    private AnimationInfo ModCrouchLowerPose(Model model, out AnimationSamples.Sample? sample, AnimationInfo? detachedCursor = null)
    {
        // Dedicated low footwork uses the Hunter's native neutral skeleton.
        // Input/weapon/native animation cursors remain canonical.
        int index = (int)PlayerAnimation.Idle;
        NodeAnimationGroup group = model.AnimationGroups.Node[index];
        int count = group.FrameCount;
        var info = detachedCursor ?? (_modCrouchLowerAnimation ??= new());
        info.Index[0] = index; info.Frame[0] = 0; info.FrameCount[0] = count;
        info.Flags[0] = AnimFlags.None; info.Step[0] = 1;
        info.Node.Slot = 0; info.Node.Group = group;
        sample = new AnimationSamples.Sample(0, count > 1 ? 1 : 0, 0);
        return info;
    }

    private float ModCrouchDrawBobCorrection(double alpha, bool gun = false)
    {
        if (!ModCrouchGaitActive || _scene.Services.IsReplica
            || !CameraInfo.ModHasLiveDrawHistory(alpha)) return 0;
        float t = FrameTiming.HighRefreshPresentation ? (float)alpha : 1;
        float slide = _modSlidePresentation.Sample(t);
        float strength = Math.Max(_modCrouchWalk.Sample(t).Amount, slide);
        float scale = TraversalGaitPresentation.WalkingBobScale * (1 - slide);
        float visual = _modCrouchPresentation.Sample(t, ModCrouched);
        // Restore native bob gradually with the same start/stop envelope as
        // the feet, rather than changing it abruptly when the gait retires.
        return _modCrouchGait.SampleBob(t, gun) * (scale - 1) * visual * strength;
    }
}
