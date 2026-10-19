#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Linq;
using MphRead.Entities;
using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
namespace MphRead.Mods.Launcher.RmlUi.Render;
internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayCamera : ITheatreWorkspaceCameraBackend, ITheatreWorkspaceOverviewBackend,ITheatreWorkspaceCameraChoicesBackend
    {
        public void SetOverview(TheatreCameraContext context)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context);
                ReplayCamera.Director=false;ReplayCamera.PlayTrack=false;ReplayCamera.LookAtSlot=-1;
                ReplayCamera.SetMode(ReplayCameraMode.Overview);
            }
        }
        public TheatreWorkspaceCameraValues ReadDirectCamera(TheatreCameraContext context)
        {
            Validate(context);
            var p=context.Pose;
            var facing=Vector3.Transform(-Vector3.UnitZ,new Quaternion(p.Qx,p.Qy,p.Qz,p.Qw));
            return new(p.X,p.Y,p.Z,MathHelper.RadiansToDegrees(MathF.Atan2(-facing.X,-facing.Z)),
                MathHelper.RadiansToDegrees(MathF.Asin(Math.Clamp(facing.Y,-1,1))),p.Roll,p.Fov);
        }
        public TheatreWorkspacePreview ReadPreview(TheatreCameraContext context)
        {
            Validate(context);var scene=(Scene)context.PresentationScene;
            return new(scene.NativeStudioPreviewGameHud,scene.NativeStudioPreviewOverlay,scene.NativeStudioPreviewPath);
        }
        public void ApplyDirectCamera(TheatreCameraContext context,TheatreWorkspaceCameraValues v)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context);
                foreach(float value in new[]{v.X,v.Y,v.Z,v.Yaw,v.Pitch,v.Roll,v.Fov})
                    if(!float.IsFinite(value))throw new ArgumentException("Camera values must be finite numbers.");
                if(Math.Abs(v.X)>100000||Math.Abs(v.Y)>100000||Math.Abs(v.Z)>100000||v.Pitch < -89.9f||v.Pitch > 89.9f
                    ||Math.Abs(v.Yaw)>36000||Math.Abs(v.Roll)>360||!v.ValidFov)
                    throw new ArgumentException("Use position within ±100000, pitch ±89.9°, roll ±360° and FOV 10–150°.");
                var rotation=(Quaternion.FromAxisAngle(Vector3.UnitY,MathHelper.DegreesToRadians(v.Yaw))
                    *Quaternion.FromAxisAngle(Vector3.UnitX,MathHelper.DegreesToRadians(v.Pitch))).Normalized();
                ReplayCamera.PlayTrack=false;
                ReplayCamera.FieldOfView=v.Fov;ReplayCamera.Roll=v.Roll;
                ((Scene)context.PresentationScene).UseNativeReplayFreeCamera(new(context.Frame,new(v.X,v.Y,v.Z),rotation,
                    MathHelper.DegreesToRadians(v.Fov),(sbyte)ReplayCamera.LookAtSlot,MathHelper.DegreesToRadians(v.Roll)));
            }
        }
        public TheatreWorkspaceCameraChoices ReadChoices(TheatreCameraContext context)
        {
            Validate(context);var scene=(Scene)context.PresentationScene;
            var slots=scene.Players.Items.Where(p=>p.LoadFlags.TestFlag(LoadFlags.Active)&&p.LoadFlags.TestFlag(LoadFlags.Spawned))
                .Select(p=>(int)p.SlotIndex).Where(slot=>slot>=0&&slot<8).Distinct().OrderBy(slot=>slot).ToImmutableArray();
            return new((int)ReplayCamera.TrackInterpolation,(int)ReplayCamera.TrackEase,ReplayCamera.LookAtSlot,scene.Players.Main.SlotIndex,slots);
        }
        public void ApplyChoices(TheatreCameraContext context,int interpolation,int ease,int lookAt,int playerSlot)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                Validate(context);var available=ReadChoices(context);
                if(interpolation is <0 or >4||ease is <0 or >3||lookAt!= -1&&!available.PlayerSlots.Contains(lookAt)
                    ||playerSlot!=available.PlayerSlot&&!available.PlayerSlots.Contains(playerSlot))throw new ArgumentException("Choose a spawned player and a supported camera mode from the reviewed picture.");
                ReplayCamera.TrackInterpolation=(ReplayCameraInterpolation)interpolation;ReplayCamera.TrackEase=(ReplayCameraEase)ease;
                ReplayCamera.LookAtSlot=lookAt;
                if(playerSlot!=available.PlayerSlot)SpectatorMode.Watch(playerSlot);
            }
        }
        public void SetPreview(TheatreCameraContext context,TheatreWorkspacePreview preview)
        {
            lock(ReplayAnnotationWitness.Gate)
            {Validate(context);((Scene)context.PresentationScene).SetNativeStudioPreview(preview.GameHud,preview.ReplayOverlay,preview.CameraPath);}
        }
    }
}
#endif
