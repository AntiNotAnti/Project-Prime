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
    private sealed partial class CurrentReplayCamera : ITheatreCameraToolsBackend, ITheatreCameraMemoryBackend
    {
        private bool _memoryEditing,_preserveMemoryCapture;
        private sealed record ToolsFiles(ReplayReviewedSidecar Camera,ReplayReviewedSidecar Annotations,ReplayCameraTrackIdentity Identity,ImmutableArray<ReplayCameraKeyframe> NativeKeys,byte[] State,byte[] SavedState,long TrackRevision,ImmutableArray<TheatreCameraKey> ReviewedKeys);
        public TheatreCameraToolsContext CaptureTools()
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var camera=Capture();object owner=DemoPlayback.PlaybackOwner;
                // Keep the exact native keys: degree-valued UI poses cannot round-trip every radians bit pattern.
                var nativeKeys=ReplayCamera.Track.Keys.ToImmutableArray();
                if(!nativeKeys.Select(Key).SequenceEqual(camera.Keys))throw new TheatreCameraStaleException("The live camera keys changed during capture.");
                string source=ReplayNativeFileWitness.Source(camera.LogicalPath,camera.PlaybackPath);
                var bytes=ReplayReviewedMetadata.Read(camera.LogicalPath+".camera",ReplayCameraTrack.MaxSidecarBytes);
                var annotations=ReplayReviewedMetadata.Read(camera.LogicalPath+ReplayAnnotations.Extension);
                var doc=ReplayAnnotations.ReadReviewed(annotations,camera.Duration);
                var identity=ReplayCameraTrackIdentity.Create(camera.LogicalPath);
                byte[] savedState=ReplayCamera.Track.ExportState();var savedKeys=camera.Keys;
                if(!_preserveMemoryCapture&&!ReplayCamera.MemoryDocumentDirty
                    &&ReplayCameraDraftStore.TryRecover(camera.LogicalPath,identity,bytes.Revision,out var recovered))
                {
                    var recoveredTrack=new ReplayCameraTrack();
                    if(recoveredTrack.ImportState(recovered!)&&recoveredTrack.Keys.All(key=>key.Frame<=camera.Duration)
                        &&(recoveredTrack.WindowDuration is not uint windowDuration||windowDuration==camera.Duration))
                    {
                        ReplayCamera.Track.ImportState(recovered!);ReplayCamera.MemoryDocumentDirty=true;_memoryEditing=true;
                        nativeKeys=ReplayCamera.Track.Keys.ToImmutableArray();camera=camera with{Keys=nativeKeys.Select(Key).ToImmutableArray()};
                    }
                }
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
                    Motion(),new ToolsFiles(bytes,annotations,identity,nativeKeys,ReplayCamera.Track.ExportState(),savedState,ReplayCamera.Track.Revision,camera.Keys)){SavedKeys=savedKeys};
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
            return (_memoryEditing||Pose(native)==camera.Pose)&&context.Persisted is ToolsFiles files
                &&ReplayCamera.Track.Revision==files.TrackRevision&&camera.Keys==files.ReviewedKeys;
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
        private ReplayCameraTrack BuildMemoryDraft(TheatreCameraToolsEdit edit)
        {
            if(!ToolsCurrent(edit.Context)||edit.Context.Persisted is not ToolsFiles files
                ||!edit.Before.SequenceEqual(edit.Context.Camera.Keys)||edit.After.Length>1026
                ||edit.After.Any(k=>k.Frame>edit.Context.Camera.Duration||!TheatreCameraController.ValidPose(k.Pose))
                ||edit.After.Select(k=>k.Frame).Distinct().Count()!=edit.After.Length)
                throw new TheatreCameraStaleException("The source-bound camera document changed.");
            var draft=new ReplayCameraTrack();
            if(!draft.ImportState(files.State))throw new InvalidDataException(draft.LastError);
            foreach(var key in edit.Before)if(!edit.After.Any(k=>k.Frame==key.Frame)&&!draft.Remove(key.Frame))
                throw new InvalidOperationException("An inferred clip boundary cannot be removed. Replace it with an authored key first.");
            foreach(var key in edit.After)if(!edit.Before.Contains(key)&&!draft.Put(ToolsNativeKey(key,edit.Context,files)))
                throw new InvalidDataException(draft.LastError);
            return draft;
        }
        private TheatreCameraToolsContext? _memoryPreviewContext;
        // Keep only the discarded file record so a later picture-fence failure
        // can retry explicit refresh without retaining its scene/context.
        private ToolsFiles? _discardedMemory;
        public TheatreCameraToolsContext RebindMemory(TheatreCameraToolsContext current)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                if(current.Persisted is not ToolsFiles files||!ReferenceEquals(DemoPlayback.PlaybackOwner,current.Owner)
                    ||DemoPlayback.LogicalPath!=current.Camera.LogicalPath||DemoPlayback.PlaybackPath!=current.Camera.PlaybackPath
                    ||ReplayNativeFileWitness.Source(current.Camera.LogicalPath,current.Camera.PlaybackPath)!=current.SourceRevision)
                    throw new TheatreCameraStaleException("The original source or camera baseline changed. Unsaved keys and history are retained; discard to leave this document.");
                bool dirty=!ReferenceEquals(files,_discardedMemory)&&!files.State.SequenceEqual(files.SavedState);
                if(dirty?(ReplayReviewedMetadata.Read(files.Camera.Path,ReplayCameraTrack.MaxSidecarBytes).Revision!=files.Camera.Revision
                    ||!ReplayCamera.Track.Keys.SequenceEqual(files.NativeKeys)):ReplayCamera.MemoryDocumentDirty)
                    throw new TheatreCameraStaleException("The camera baseline changed while unsaved keys are present. Unsaved keys and history are retained; save or discard the owning document first.");
                // Explicit refresh can reload a clean document's newly saved
                // baseline. Dirty documents retain their exact original witness.
                _preserveMemoryCapture=dirty;
                try
                {
                    var next=CaptureTools();var rebound=(ToolsFiles)next.Persisted;
                    _memoryEditing=true;
                    return dirty?next with{SavedKeys=current.SavedKeys,
                        Persisted=rebound with{Camera=files.Camera,SavedState=files.SavedState}}:next;
                }
                finally{_preserveMemoryCapture=false;}
            }
        }
        public string RecoveryStatus(TheatreCameraToolsContext current)=>ReplayCameraDraftStore.Error(current.Camera.LogicalPath);
        public void DiscardMemory(TheatreCameraToolsContext current)
        {
            _discardedMemory=current.Persisted as ToolsFiles;
            ReplayCameraDraftStore.Retire(current.Camera.LogicalPath);
            CancelMemoryPreview();
            // Never apply a retired document to a new replay owner. A subsequent explicit capture loads its own sidecar.
            if(ReferenceEquals(DemoPlayback.PlaybackOwner,current.Owner)&&DemoPlayback.LogicalPath==current.Camera.LogicalPath)
            {
                ReplayCamera.MemoryDocumentDirty=false;
                try
                {
                    if(ReplayNativeFileWitness.Source(current.Camera.LogicalPath,current.Camera.PlaybackPath)==current.SourceRevision)
                        ReplayCamera.ReloadReviewedTrack(current.Camera.LogicalPath);
                    else ReplayCamera.Track.Clear();
                }
                catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException)
                {ReplayCamera.Track.Clear();}
            }
        }
        public void PreviewMemory(TheatreCameraToolsEdit edit)
        {
            _memoryEditing=true;_memoryPreviewContext=edit.Context;
            var draft=BuildMemoryDraft(edit);
            ReplayCamera.SetMemoryPreview(edit.Context.Owner,edit.Context.Camera.PresentationScene,
                edit.Context.Camera.Transport,edit.Context.Camera.SeekGeneration,edit.Context.Camera.Frame,draft);
        }
        public void CancelMemoryPreview()
        {
            var context=_memoryPreviewContext;_memoryPreviewContext=null;
            if(context!=null&&context.Camera.PresentationScene is Scene scene
                &&ReferenceEquals(DemoPlayback.PresentationScene,scene)&&ReferenceEquals(DemoPlayback.PlaybackOwner,context.Owner)
                &&DemoPlayback.Session.Transport.SeekGeneration==context.Camera.SeekGeneration&&ReplayController.CurrentFrame==context.Camera.Frame)
                scene.RestoreNativeReplayMemoryCamera(NativeKey(new(context.Camera.Frame,context.Camera.Pose)));
            ReplayCamera.ClearMemoryPreview();
        }
        public TheatreCameraToolsContext CommitMemory(TheatreCameraToolsEdit edit)
        {
            _memoryEditing=true;_memoryPreviewContext=edit.Context;
            var draft=BuildMemoryDraft(edit);var files=(ToolsFiles)edit.Context.Persisted;
            ReplayCamera.ApplyReviewedToolsState(edit.Context.Camera.LogicalPath,files.NativeKeys,draft.ExportState());
            ReplayCamera.SetMemoryPreview(edit.Context.Owner,edit.Context.Camera.PresentationScene,
                edit.Context.Camera.Transport,edit.Context.Camera.SeekGeneration,edit.Context.Camera.Frame,draft);
            return MemoryContext(edit.Context,draft,files);
        }
        private static TheatreCameraToolsContext MemoryContext(TheatreCameraToolsContext current,ReplayCameraTrack draft,ToolsFiles files)
        {
            byte[] state=draft.ExportState();
            ReplayCamera.MemoryDocumentDirty=!state.SequenceEqual(files.SavedState);
            if(ReplayCamera.MemoryDocumentDirty)ReplayCameraDraftStore.Queue(current.Camera.LogicalPath,files.Identity,files.Camera.Revision,state);
            else ReplayCameraDraftStore.Retire(current.Camera.LogicalPath);
            var native=draft.Keys.ToImmutableArray();
            var curve=ImmutableArray.CreateBuilder<TheatreCameraCurvePoint>();
            for(int i=0;i<64;i++)if(draft.Sample(current.Camera.Duration*(i/63d),out var point,current.Motion.ConstantSpeed))
                curve.Add(new((uint)(current.Camera.Duration*(i/63d)),point.Position.X,point.Position.Y,point.Position.Z,
                    OpenTK.Mathematics.MathHelper.RadiansToDegrees(point.Fov),OpenTK.Mathematics.MathHelper.RadiansToDegrees(point.Roll)));
            var reviewed=native.Select(Key).ToImmutableArray();
            return current with{Camera=current.Camera with{Keys=reviewed},
                Persisted=files with{NativeKeys=native,State=state,TrackRevision=ReplayCamera.Track.Revision,ReviewedKeys=reviewed},Curve=curve.ToImmutable()};
        }
        public TheatreCameraToolsContext RestoreMemory(TheatreCameraToolsContext current,TheatreCameraToolsContext target)
        {
            if(!ToolsCurrent(current)||current.Persisted is not ToolsFiles files||target.Persisted is not ToolsFiles old
                ||!ReferenceEquals(current.Owner,target.Owner)||current.SourceRevision!=target.SourceRevision)
                throw new TheatreCameraStaleException("Camera history belongs to a retired replay source.");
            _memoryEditing=true;
            var draft=new ReplayCameraTrack();if(!draft.ImportState(old.State))throw new InvalidDataException(draft.LastError);
            ReplayCamera.ApplyReviewedToolsState(current.Camera.LogicalPath,files.NativeKeys,old.State);
            ReplayCamera.SetMemoryPreview(current.Owner,current.Camera.PresentationScene,current.Camera.Transport,current.Camera.SeekGeneration,current.Camera.Frame,draft);
            return MemoryContext(current,draft,files);
        }
        public TheatreCameraToolsContext SaveMemory(TheatreCameraToolsContext current)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                ValidateTools(current);var files=(ToolsFiles)current.Persisted;
                var draft=new ReplayCameraTrack();if(!draft.ImportState(files.State))throw new InvalidDataException(draft.LastError);
                var result=ReplayReviewedMetadata.CommitBytes(files.Camera,draft.EncodeReviewed(files.Identity),()=>ValidateTools(current),maximum:ReplayCameraTrack.MaxSidecarBytes);
                if(!result.Verified)throw new IOException(result.Notice);
                if(!ToolsCurrent(current)||ReplayNativeFileWitness.Source(current.Camera.LogicalPath,current.Camera.PlaybackPath)!=current.SourceRevision)
                    throw new TheatreCameraStaleException("Camera saved, but the source changed before final verification. Unsaved document retained.");
                ReplayCameraDraftStore.Retire(current.Camera.LogicalPath);ReplayCamera.MemoryDocumentDirty=false;
                return current with{SavedKeys=current.Camera.Keys,Camera=current.Camera with{CameraRevision=result.Written.Revision},Persisted=files with{Camera=result.Written,SavedState=files.State}};
            }
        }
        public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit reviewed)
        {
            lock(ReplayAnnotationWitness.Gate)
            {
                var context=reviewed.Context;ValidateTools(context);
                if(!Enum.IsDefined(reviewed.Kind)||!reviewed.Before.SequenceEqual(context.Camera.Keys)||reviewed.After.Length>1026
                    ||reviewed.After.Select(key=>key.Frame).Distinct().Count()!=reviewed.After.Length
                    ||reviewed.After.Any(key=>key.Frame>context.Camera.Duration)||context.Persisted is not ToolsFiles files)
                    throw new TheatreCameraStaleException("The exact camera track edit is invalid or changed.");
                var draft=new ReplayCameraTrack();
                if(!draft.ImportState(files.State))throw new InvalidDataException(draft.LastError);
                foreach(var key in reviewed.Before)if(!reviewed.After.Any(after=>after.Frame==key.Frame)&&!draft.Remove(key.Frame))
                    throw new InvalidOperationException("An inferred clip boundary cannot be removed or moved. Edit an authored source key instead.");
                foreach(var key in reviewed.After)if(!reviewed.Before.Contains(key)&&!draft.Put(ToolsNativeKey(key,context,files)))throw new ArgumentException(draft.LastError);
                byte[] encoded=draft.EncodeReviewed(files.Identity);
                var result=ReplayReviewedMetadata.CommitBytes(files.Camera,encoded,()=>ValidateTools(context),maximum:ReplayCameraTrack.MaxSidecarBytes);
                if(!result.Verified)return new(false,result.Notice);
                // Disk publication may have happened even if ownership changed externally. Never apply an old track onto a new scene.
                try
                {
                    if(!ToolsCurrent(context)||ReplayNativeFileWitness.Source(context.Camera.LogicalPath,context.Camera.PlaybackPath)!=context.SourceRevision
                        ||ReplayReviewedMetadata.Read(files.Camera.Path,ReplayCameraTrack.MaxSidecarBytes).Revision!=result.Written.Revision
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
                if(key.Frame==original.Frame&&(key.Pose with{LookAt=original.Pose.LookAt,Interpolation=original.Pose.Interpolation,Ease=original.Pose.Ease,Channels=original.Pose.Channels})==original.Pose)
                    return ToolsNativePose(key,files.NativeKeys[i]);
            }
            // An older copied key which is absent from this review is a new insertion.
            return NativeKey(key);
        }
        private static ReplayCameraKeyframe ToolsNativePose(TheatreCameraKey key,ReplayCameraKeyframe native)=>native with
        {Frame=key.Frame,LookAtSlot=(sbyte)key.Pose.LookAt,Interpolation=(ReplayCameraInterpolation)key.Pose.Interpolation,Ease=(ReplayCameraEase)key.Pose.Ease,Curves=NativeChannels(key.Pose.Channels)};
        private static TheatreCameraMotion Motion()=>new(ReplayCamera.TrackConstantSpeed,ReplayCamera.TrackCollisionAvoidance,ReplayCamera.Director);
        private void ValidateTools(TheatreCameraToolsContext context)
        {
            if(!ToolsCurrent(context)||context.Persisted is not ToolsFiles files)throw new TheatreCameraStaleException("The reviewed camera tools owner or picture changed. Refresh.");
            try
            {
                if(DemoPlayback.LogicalPath!=context.Camera.LogicalPath
                    ||ReplayNativeFileWitness.Source(context.Camera.LogicalPath,context.Camera.PlaybackPath)!=context.SourceRevision
                    ||ReplayReviewedMetadata.Read(files.Camera.Path,ReplayCameraTrack.MaxSidecarBytes).Revision!=files.Camera.Revision
                    ||ReplayReviewedMetadata.Read(files.Annotations.Path).Revision!=context.AnnotationRevision)
                    throw new TheatreCameraStaleException("The reviewed source, camera keys or marker sidecar changed. Refresh.");
            }
            catch(Exception error)when(error is IOException or InvalidDataException or UnauthorizedAccessException)
            {throw new TheatreCameraStaleException(error.Message+" Refresh before editing.");}
        }
    }
}
#endif
