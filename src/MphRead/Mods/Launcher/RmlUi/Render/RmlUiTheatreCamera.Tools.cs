#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;

namespace MphRead.Mods.Launcher.RmlUi.Render;

internal static partial class RmlUiOpenGlSmoke
{
    private sealed partial class CurrentReplayCamera : ITheatreCameraToolsBackend
    {
        private sealed record ToolsFiles(ReplayReviewedSidecar Camera,ReplayReviewedSidecar Annotations,ReplayCameraTrackIdentity Identity,ImmutableArray<ReplayCameraKeyframe> NativeKeys,byte[] State);
        public TheatreCameraToolsContext CaptureTools()
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var camera=Capture();object owner=DemoPlayback.PlaybackOwner;
                // Keep the exact native keys: degree-valued UI poses cannot round-trip every radians bit pattern.
                var nativeKeys=ReplayCamera.Track.Keys.ToImmutableArray();
                if(!nativeKeys.Select(Key).SequenceEqual(camera.Keys))throw new TheatreCameraStaleException("The live camera keys changed during capture.");
                string source=ReplayNativeFileWitness.Source(camera.LogicalPath,camera.PlaybackPath);
                var bytes=ReplayReviewedMetadata.Read(camera.LogicalPath+".camera",8192);
                var annotations=ReplayReviewedMetadata.Read(camera.LogicalPath+ReplayAnnotations.Extension);
                var doc=ReplayAnnotations.ReadReviewed(annotations,camera.Duration);
                var identity=ReplayCameraTrackIdentity.Create(camera.LogicalPath);
                if(bytes.Revision!=camera.CameraRevision)throw new TheatreCameraStaleException("The saved camera changed during review.");
                var markers=ImmutableArray.CreateBuilder<TheatreCameraMarker>();
                void AddMarker(ReplayEventType? type,TheatreCameraMarkerKind kind)
                {
                    var marker=DemoPlayback.Events.Select((value,index)=>(value,index)).Where(row=>type==null||row.value.Type==type)
                        .OrderBy(row=>Math.Abs((long)row.value.Frame-camera.Frame)).ThenBy(row=>row.value.Frame)
                        .Select(row=>new TheatreCameraMarker("event:"+row.index,row.value.Frame,kind)).FirstOrDefault();
                    if(marker!=null)markers.Add(marker);
                }
                AddMarker(null,TheatreCameraMarkerKind.Event);AddMarker(ReplayEventType.Kill,TheatreCameraMarkerKind.Kill);
                var bookmark=doc.Bookmarks.OrderBy(row=>Math.Abs((long)row.Frame-camera.Frame)).ThenBy(row=>row.Frame).FirstOrDefault();
                if(bookmark!=null)markers.Add(new("bookmark:"+bookmark.Id.ToString("N"),bookmark.Frame,TheatreCameraMarkerKind.Bookmark));
                var slots=((Scene)camera.PresentationScene).Players.Items
                    .Where(player=>player.LoadFlags.TestFlag(LoadFlags.Active)&&player.LoadFlags.TestFlag(LoadFlags.Spawned))
                    .Select(player=>(int)player.SlotIndex).Where(slot=>slot>=0&&slot<8).Distinct().OrderBy(slot=>slot).ToImmutableArray();
                var result=new TheatreCameraToolsContext(camera,owner,source,annotations.Revision,markers.ToImmutable(),slots,
                    Motion(),new ToolsFiles(bytes,annotations,identity,nativeKeys,ReplayCamera.Track.ExportState()));
                var sampled=new ReplayCameraTrack();if(!sampled.ImportState(ReplayCamera.Track.ExportState()))throw new InvalidDataException(sampled.LastError);
                var curve=ImmutableArray.CreateBuilder<TheatreCameraCurvePoint>();
                for(int i=0;i<64;i++)if(sampled.Sample(camera.Duration*(i/63d),out var point,result.Motion.ConstantSpeed))
                    curve.Add(new((uint)(camera.Duration*(i/63d)),point.Position.X,point.Position.Y,point.Position.Z,OpenTK.Mathematics.MathHelper.RadiansToDegrees(point.Fov),OpenTK.Mathematics.MathHelper.RadiansToDegrees(point.Roll)));
                result=result with{Curve=curve.ToImmutable()};
                ValidateTools(result);return result;
            }
        }
        public void ValidateReviewedTools(TheatreCameraToolsContext context)
        {lock(ReplayAnnotationWitness.Gate){ValidateTools(context);}}
        public bool ToolsCurrent(TheatreCameraToolsContext context)
        {
            var camera=context.Camera;
            if(!GameplayLeaseCurrent||!DemoPlayback.IsActive||!ReplayController.IsPaused||ReplayController.IsSeeking||ReplayVideoExporter.Active
                ||ReplayCamera.BookmarkRequested||ReplayCamera.RestoreRequested||_gameplayScene!.LifetimeGeneration!=camera.SceneGeneration
                ||!ReferenceEquals(DemoPlayback.PlaybackOwner,context.Owner)||DemoPlayback.PlaybackPath!=camera.PlaybackPath
                ||!ReferenceEquals(DemoPlayback.Session.Transport,camera.Transport)||!ReferenceEquals(DemoPlayback.PresentationScene,camera.PresentationScene)
                ||DemoPlayback.Session.Transport.SeekGeneration!=camera.SeekGeneration||ReplayController.CurrentFrame!=camera.Frame
                ||ReplayController.DurationFrames!=camera.Duration||Motion()!=context.Motion||ReplayCamera.PlayTrack!=camera.TrackPlaying
                ||camera.PresentationScene is not Scene scene||!DemoPlayback.Owns(scene))return false;
            // Read the SAME successful-presentation cache as basic camera capture, using the already reviewed logical path.
            // No source/descriptor/sidecar I/O occurs while polling authority for native UI.
            if(!ReplayCamera.NativePresented.TryRead(scene,camera.Transport,camera.SeekGeneration,camera.Frame,camera.LogicalPath,camera.PlaybackPath,
                ReplayCamera.PresentedSettings(scene),out var saved))return false;
            var pose=saved!.Pose;
            var native=new ReplayCameraKeyframe(camera.Frame,pose.Position,pose.Rotation,pose.Fov,-1,pose.Roll,ReplayCamera.TrackInterpolation,ReplayCamera.TrackEase);
            return Pose(native)==camera.Pose&&context.Persisted is ToolsFiles files
                &&ReplayCamera.Track.Keys.SequenceEqual(files.NativeKeys)&&files.NativeKeys.Select(Key).SequenceEqual(camera.Keys);
        }
        public void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey focused)
        {lock(ReplayAnnotationWitness.Gate){ValidateTools(context);Preview(context.Camera,focused);}}
        public void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                ValidateTools(context);
                switch(change)
                {
                    case TheatreCameraMotionChange.ConstantSpeed:ReplayCamera.ToggleConstantSpeed();break;
                    case TheatreCameraMotionChange.Collision:ReplayCamera.TrackCollisionAvoidance=!ReplayCamera.TrackCollisionAvoidance;break;
                    case TheatreCameraMotionChange.Director:
                        ReplayCamera.SetProfile(ReplayPresentationProfile.Presentation);ReplayCamera.Director=!ReplayCamera.Director;
                        if(ReplayCamera.Director)ReplayCamera.PlayTrack=false;break;
                    default:throw new ArgumentException("Unknown camera motion option.");
                }
            }
        }
        public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit reviewed)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var context=reviewed.Context;ValidateTools(context);
                if(!Enum.IsDefined(reviewed.Kind)||!reviewed.Before.SequenceEqual(context.Camera.Keys)||reviewed.After.Length>64
                    ||reviewed.After.Select(key=>key.Frame).Distinct().Count()!=reviewed.After.Length
                    ||reviewed.After.Any(key=>key.Frame>context.Camera.Duration)||context.Persisted is not ToolsFiles files)
                    throw new TheatreCameraStaleException("The exact camera track edit is invalid or changed.");
                var draft=new ReplayCameraTrack();
                if(!draft.ImportState(files.State))throw new InvalidDataException(draft.LastError);
                foreach(var key in reviewed.Before)if(!reviewed.After.Any(after=>after.Frame==key.Frame)&&!draft.Remove(key.Frame))
                    throw new InvalidOperationException("An inferred clip boundary cannot be removed or moved. Edit an authored source key instead.");
                foreach(var key in reviewed.After)if(!reviewed.Before.Contains(key)&&!draft.Put(ToolsNativeKey(key,context,files)))throw new ArgumentException(draft.LastError);
                byte[] encoded=draft.EncodeReviewed(files.Identity);
                var result=ReplayReviewedMetadata.CommitBytes(files.Camera,encoded,()=>ValidateTools(context),maximum:8192);
                if(!result.Verified)return new(false,result.Notice);
                // Disk publication may have happened even if ownership changed externally. Never apply an old track onto a new scene.
                try
                {
                    if(!ToolsCurrent(context)||ReplayNativeFileWitness.Source(context.Camera.LogicalPath,context.Camera.PlaybackPath)!=context.SourceRevision
                        ||ReplayReviewedMetadata.Read(files.Camera.Path,8192).Revision!=result.Written.Revision
                        ||ReplayReviewedMetadata.Read(files.Annotations.Path).Revision!=context.AnnotationRevision)
                        return new(false,"Camera metadata was published, but its source, live owner or resulting bytes changed before verification. Inspect the saved camera and refresh.");
                }
                catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException)
                {return new(false,"Camera metadata was published, but final identity verification failed. Inspect the saved camera and refresh. "+error.Message);}
                try{ReplayCamera.ApplyReviewedToolsState(context.Camera.LogicalPath,files.NativeKeys,draft.ExportState());}
                catch(Exception error)when(error is InvalidOperationException or InvalidDataException)
                {return new(false,"Camera metadata was published, but live track ownership could not be verified. Close tools and inspect the saved camera. "+error.Message);}
                return new(true,result.Notice);
            }
        }
        private static ReplayCameraKeyframe ToolsNativeKey(TheatreCameraKey key,TheatreCameraToolsContext context,ToolsFiles files)
        {
            // Frame translation keeps the original pose object; enum edits keep the original frame.
            // Both retain native angle bits rather than converting unchanged display degrees back to radians.
            for(int i=0;i<context.Camera.Keys.Length;i++)
                if(ReferenceEquals(key.Pose,context.Camera.Keys[i].Pose))return ToolsNativePose(key,files.NativeKeys[i]);
            for(int i=0;i<context.Camera.Keys.Length;i++)
            {
                var original=context.Camera.Keys[i];
                if(key.Frame==original.Frame&&(key.Pose with{LookAt=original.Pose.LookAt,Interpolation=original.Pose.Interpolation,Ease=original.Pose.Ease})==original.Pose)
                    return ToolsNativePose(key,files.NativeKeys[i]);
            }
            // An older copied key which is absent from this review is a new insertion.
            return NativeKey(key);
        }
        private static ReplayCameraKeyframe ToolsNativePose(TheatreCameraKey key,ReplayCameraKeyframe native)=>native with
        {Frame=key.Frame,LookAtSlot=(sbyte)key.Pose.LookAt,Interpolation=(ReplayCameraInterpolation)key.Pose.Interpolation,Ease=(ReplayCameraEase)key.Pose.Ease};
        private static TheatreCameraMotion Motion()=>new(ReplayCamera.TrackConstantSpeed,ReplayCamera.TrackCollisionAvoidance,ReplayCamera.Director);
        private void ValidateTools(TheatreCameraToolsContext context)
        {
            if(!ToolsCurrent(context)||context.Persisted is not ToolsFiles files)throw new TheatreCameraStaleException("The reviewed camera tools owner or picture changed. Refresh.");
            try
            {
                if(DemoPlayback.LogicalPath!=context.Camera.LogicalPath
                    ||ReplayNativeFileWitness.Source(context.Camera.LogicalPath,context.Camera.PlaybackPath)!=context.SourceRevision
                    ||ReplayReviewedMetadata.Read(files.Camera.Path,8192).Revision!=files.Camera.Revision
                    ||ReplayReviewedMetadata.Read(files.Annotations.Path).Revision!=context.AnnotationRevision)
                    throw new TheatreCameraStaleException("The reviewed source, camera keys or marker sidecar changed. Refresh.");
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException)
            {throw new TheatreCameraStaleException(error.Message+" Refresh before editing.");}
        }
    }
}
#endif
