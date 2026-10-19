using System;
using MphRead;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

internal sealed partial class ReplayWindowAcceptance
{
    // Adopt this file into tools/rmlui-opengl-check, then call after the bounded
    // range/current owner are retained and before the existing four-view checks.
    private void PlanRml98OverviewQualification()
    {
        ReplayCameraMode original=default;
        int subject=-1;
        Do("RML98 retain original camera and select reviewed native Overview",()=>
        {
            original=ReplayCamera.Mode;subject=DemoPlayback.PresentationScene!.Players.MainPlayerIndex;
            Activate("studio_refresh");
        });
        Wait("RML98 reviewed direct camera is current",()=>Read<object>(Page,"_workspaceCameraReview")!=null);
        Do("RML98 Overview through native typed action",()=>Activate("studio_camera_overview"));
        Wait("RML98 Overview owns a successful paused world picture",()=>ReplayCamera.Mode==ReplayCameraMode.Overview
            &&!ReplayController.IsSeeking&&_lastWorldFrame==_observedFrames);
        WaitFrames(3,"RML98 exact legacy Overview pose preserves canonical world and selected subject",()=>
        {
            Rml97SameWorld();var scene=DemoPlayback.PresentationScene!;
            Require(scene.Players.MainPlayerIndex==subject,"Overview retains selected recorded subject");
            var pose=scene.CaptureNativeReplayCamera(ReplayController.CurrentFrame,out _);
            var expected=scene.Players.Main.Position+Vector3.UnitY+new Vector3(0,30,15);
            Require((pose.Position-expected).Length<.001f,"Overview uses legacy subject-relative camera position");
            Require(!scene.NativeStudioPreviewGameHud,"Overview hides the game HUD without changing the stored preview preference");
            Capture("rml98-overview");
        });
        Do("RML98 native direct choice restores the original camera mode",()=>Activate("studio_camera_mode_"+original));
        Wait("RML98 original camera mode and successful world picture return",()=>ReplayCamera.Mode==original&&_lastWorldFrame==_observedFrames);
        Do("RML98 restore reviewed owner after Overview preview",()=>Activate("studio_refresh"));
    }
}
