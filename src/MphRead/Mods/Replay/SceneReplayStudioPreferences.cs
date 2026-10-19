#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Entities;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;

namespace MphRead;
public partial class Scene
{
    /// <summary>Transfer only presentation booleans during the existing owned replica replacement callback.</summary>
    internal void CopyNativeStudioPreviewFrom(Scene previous)
    {
        if(!DemoPlayback.Owns(this)||Services is not ReplaySceneServices current||previous.Services is not ReplaySceneServices old
            ||current.Session.CurrentPath is not{} currentPath||old.Session.CurrentPath is not{} oldPath
            ||!ReplayPathComparer.Same(currentPath,oldPath)||current.Session.Metadata is not{} a||old.Session.Metadata is not{} b
            ||a.MapHash!=b.MapHash||!StringComparer.OrdinalIgnoreCase.Equals(a.RoomKey,b.RoomKey)
            ||a.CustomMapIdentity.HasValue!=b.CustomMapIdentity.HasValue
            ||(a.CustomMapIdentity is{} identity&&!identity.Matches(b.CustomMapIdentity!.Value))
            ||a.OriginRecordingFrame!=b.OriginRecordingFrame||a.LeadInFrames!=b.LeadInFrames)return;
        if(NativeStudioPreviews.TryGetValue(previous,out var from))
        {
            var target=NativeStudioPreviews.GetOrCreateValue(this);
            target.Hud=from.Hud;target.Overlay=from.Overlay;target.Path=from.Path;
        }
    }

    /// <summary>Owner-thread immutable UI snapshot; no canonical world fields are added.</summary>
    internal ReplayStudioPresentationPreferences CaptureNativeStudioPreferences(bool whatShooterSaw)
    {
        RequireStudioPreferencesOwner();
        NativeStudioPreviews.TryGetValue(this,out var preview);
        ReplayStudioPreferencePose? free=null;
        if(ReplayCamera.Mode==ReplayCameraMode.Free)
        {
            if(!ReplayCameraPresentationMath.TryDecode(_viewMatrix,_perspectiveMatrix,out var pose))
                throw new InvalidOperationException("Present the free camera before saving its preferences.");
            free=new(pose.Position.X,pose.Position.Y,pose.Position.Z,pose.Rotation.X,pose.Rotation.Y,pose.Rotation.Z,pose.Rotation.W,pose.Fov,pose.Roll);
        }
        return ReplayStudioPresentationPreferencesStore.Freeze(new(ReplayCamera.Mode,Players.Main.SlotIndex,ReplayCamera.FieldOfView,
            preview?.Hud??true,preview?.Overlay??false,ReplayCamera.TrackConstantSpeed,
            ReplayCamera.TrackCollisionAvoidance,whatShooterSaw,free));
    }
    /// <returns>The persisted combat view preference for the editor's owner.</returns>
    internal bool ApplyNativeStudioPreferences(ReplayStudioPresentationPreferences preferences)
    {
        RequireStudioPreferencesOwner();
        var value=ReplayStudioPresentationPreferencesStore.Freeze(preferences);
        // A saved subject may have left the recording at the current frame.
        // Keep the current actual subject rather than inventing a player.
        for(int index=0;index<Players.Items.Count;index++)
        {
            var player=Players.Items[index];
            if(player.SlotIndex==value.PlayerSlot&&player.LoadFlags.TestFlag(LoadFlags.Active)&&player.LoadFlags.TestFlag(LoadFlags.Spawned))
            {Players.MainPlayerIndex=index;break;}
        }
        ReplayCamera.EnsureTrack();
        if(value.Camera==ReplayCameraMode.Free&&value.FreePose is{} pose)
            UseNativeReplayFreeCamera(new(Mods.Network.ReplayController.CurrentFrame,new(pose.X,pose.Y,pose.Z),
                new(pose.Qx,pose.Qy,pose.Qz,pose.Qw),pose.Fov,-1,pose.Roll));
        else ReplayCamera.SetMode(value.Camera);
        ReplayCamera.FieldOfView=value.Fov;
        ReplayCamera.Roll=value.Camera==ReplayCameraMode.Free&&value.FreePose is{} savedPose
            ?MathHelper.RadiansToDegrees(savedPose.Roll):0;
        ReplayCamera.LookAtSlot=-1;
        ReplayCamera.Director=false;ReplayCamera.PlayTrack=false;
        ReplayCamera.TrackConstantSpeed=value.ConstantSpeed;ReplayCamera.TrackCollisionAvoidance=value.CollisionAvoidance;
        SetNativeStudioPreview(value.GameHud,value.ReplayOverlay,NativeStudioPreviewPath);
        return value.WhatShooterSaw;
    }
    private void RequireStudioPreferencesOwner()
    {
        if(!DemoPlayback.Owns(this)||!ReferenceEquals(DemoPlayback.PresentationScene,this)||ReplayVideoExporter.Active)
            throw new InvalidOperationException("The Studio replay presentation owner changed or video export owns presentation.");
    }
}
#endif
