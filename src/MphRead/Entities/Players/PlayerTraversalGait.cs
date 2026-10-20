using System;
using MphRead.Mods.Movement;
using MphRead.Mods.Render;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private AnimationInfo? _modCrouchLowerAnimation;
    private static bool ModIsWalkingClip(PlayerAnimation clip) => clip is PlayerAnimation.WalkForward
        or PlayerAnimation.WalkBackward or PlayerAnimation.WalkLeft or PlayerAnimation.WalkRight;
    private bool ModCrouchGaitActive => ModCrouchBodyAllowed && ModCrouchVisualAmount > 0
        && _modCrouchGait.Ready && _modCrouchVisualGeneration == FrameTiming.PresentationGeneration
        && (ModIsWalkingClip(Biped1Anim) || _modCrouchGait.Gliding || ModSlideVisualAmount > 0);

    // A separate animation cursor belongs exclusively to the picture. Native
    // animation frames, canonical restoration and source-time muzzle reads do
    // not consume the shortened gait or the slide's stable idle leg pose.
    private AnimationInfo ModCrouchLowerPose(Model model, out AnimationSamples.Sample? sample)
    {
        bool still = _modCrouchGait.Gliding || !_modCrouchGait.Moving;
        int index = still ? (int)PlayerAnimation.Idle : (int)Biped1Anim;
        NodeAnimationGroup group = model.AnimationGroups.Node[index];
        int count = group.FrameCount;
        float alpha = FrameTiming.HighRefreshPresentation ? (float)FrameTiming.PresentationAlpha : 1;
        float frame = still ? 0 : _modCrouchGait.SampleFrame(alpha, count);
        int from = Math.Clamp((int)frame, 0, Math.Max(0, count - 1));
        int to = count > 1 ? (from + 1) % count : from;
        var info = _modCrouchLowerAnimation ??= new();
        info.Index[0] = index; info.Frame[0] = from; info.FrameCount[0] = count;
        info.Flags[0] = AnimFlags.None; info.Step[0] = 1;
        info.Node.Slot = 0; info.Node.Group = group;
        sample = new AnimationSamples.Sample(from, to, frame - from);
        return info;
    }

    private float ModCrouchDrawBobCorrection(double alpha, bool gun = false)
    {
        if (!ModCrouchGaitActive || _scene.Services.IsReplica
            || !CameraInfo.ModHasLiveDrawHistory(alpha)) return 0;
        float t = FrameTiming.HighRefreshPresentation ? (float)alpha : 1;
        float scale = _modCrouchGait.Gliding ? 0 : TraversalGaitPresentation.WalkingBobScale;
        float visual = _modCrouchPresentation.Sample(t, ModCrouched);
        return _modCrouchGait.SampleBob(t, gun) * (scale - 1) * visual;
    }
}
