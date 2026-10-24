using System.Collections.Immutable;
using System.Text.Json;
using MphRead;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
using OpenTK.Mathematics;
int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
bool Reject(Action action){try{action();return false;}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException){return true;}}
string dir=Path.Combine(Path.GetTempPath(),"prime-camera-tools-owner-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);string source=Path.Combine(dir,"SYNTHETIC.ppdemo");File.WriteAllBytes(source,new byte[]{1,2,3,4,5,6,7,8});
void Publish(){var scene=DemoPlayback.PresentationScene!;var transport=DemoPlayback.Session.Transport;transport.IsSeeking=false;
ReplayCamera.NativePresented.Publish(scene,transport,transport.SeekGeneration,transport.CurrentFrame,DemoPlayback.LogicalPath!,DemoPlayback.PlaybackPath!,ReplayCamera.PresentedSettings(scene),
Matrix4.LookAt(new Vector3(9,8,7),new Vector3(9,8,6),Vector3.UnitY),Matrix4.CreatePerspectiveFieldOfView(MathHelper.DegreesToRadians(80),16f/9,.1f,1000));}
TheatreCameraToolsEdit Move(TheatreCameraToolsContext context)=>new(TheatreCameraToolsChange.Move,context,ImmutableArray.Create(context.Camera.Keys[0].Frame),context.Camera.Keys,context.Camera.Keys.SetItem(0,context.Camera.Keys[0] with{Frame=15}));
try{
DemoPlayback.LogicalPath=DemoPlayback.PlaybackPath=source;
var channels=new ReplayCameraChannelCurves(ReplayCameraChannelMode.Smooth,ReplayCameraChannelEase.InOut,
    ReplayCameraChannelMode.Bezier,ReplayCameraChannelEase.In,ReplayCameraChannelMode.Hold,ReplayCameraChannelEase.None);
var track=new ReplayCameraTrack();track.Put(new(10,Vector3.One,Quaternion.Identity,1.2f,Curves:channels));track.Put(new(20,new Vector3(2),Quaternion.Identity,1.4f));Check(track.Save(source),"retained owner writes synthetic source-bound independent-curve camera");
var identity=ReplayCameraTrackIdentity.Create(source);byte[] encoded=track.EncodeReviewed(identity);
Check(encoded.SequenceEqual(File.ReadAllBytes(source+".camera")),"reviewed binary encoder matches retained Save format byte-for-byte");
var annotations=new ReplayAnnotationDocument{Bookmarks=new(){new(Guid.NewGuid(),35,"NEAR BOOKMARK",DateTime.UnixEpoch)}};File.WriteAllText(source+ReplayAnnotations.Extension,JsonSerializer.Serialize(annotations));
Publish();var backend=RmlUiOpenGlSmoke.Fixture();var context=backend.CaptureTools();
Check(context.Camera.Pose.X==9&&context.Camera.Pose.Y==8&&context.Camera.Pose.Z==7&&Math.Abs(context.Camera.Pose.Fov-80)<.01f,"tools use actual matrix-decoded successful picture, not raw key poses");
Check(context.Camera.Keys[0].Pose.Channels==new TheatreCameraChannelCurves(2,4,3,2,4,1),
    "native-to-reviewed conversion preserves all six orientation/lens curve fields");
Check(context.Markers.Length==3&&context.Markers.Any(m=>m.Frame==35)&&context.LookAtSlots.SequenceEqual(new[]{1,3}),"explicit capture bounds nearest marker choices and actual metadata target slots");
int reads=DemoPlayback.LogicalReads;bool allCurrent=true;for(int i=0;i<20;i++)allCurrent&=backend.ToolsCurrent(context);Check(allCurrent,"readonly current owner fence remains stable across repeated native polling");Check(DemoPlayback.LogicalReads==reads,"readonly ToolsCurrent never resolves logicalpath or reads files");
byte[] originalSource=File.ReadAllBytes(source);DateTime stamp=File.GetLastWriteTimeUtc(source);File.WriteAllBytes(source,originalSource.Reverse().ToArray());File.SetLastWriteTimeUtc(source,stamp);
Check(Reject(()=>backend.CommitTools(Move(context)))&&File.ReadAllBytes(source+".camera").SequenceEqual(encoded),"same-size same-stamp source ABA rejects before camera publication");File.WriteAllBytes(source,originalSource);File.SetLastWriteTimeUtc(source,stamp);
string annotationPath=source+ReplayAnnotations.Extension;byte[] originalAnnotations=File.ReadAllBytes(annotationPath);DateTime annStamp=File.GetLastWriteTimeUtc(annotationPath);annotations.Bookmarks[0]=annotations.Bookmarks[0] with{Frame=36};File.WriteAllText(annotationPath,JsonSerializer.Serialize(annotations));File.SetLastWriteTimeUtc(annotationPath,annStamp);
Check(Reject(()=>backend.CommitTools(Move(context))),"same-stamp marker-sidecar change rejects saved snap authority");File.WriteAllBytes(annotationPath,originalAnnotations);File.SetLastWriteTimeUtc(annotationPath,annStamp);
string camera=source+".camera";DateTime cameraStamp=File.GetLastWriteTimeUtc(camera);track.Put(track.Keys[0] with{Position=new Vector3(3)});File.WriteAllBytes(camera,track.EncodeReviewed(identity));File.SetLastWriteTimeUtc(camera,cameraStamp);
Check(Reject(()=>backend.CommitTools(Move(context)))&&ReplayCamera.Track.Keys[0].Frame==10,"camera sidecar ABA cannot overwrite reviewed key owner or live track");File.WriteAllBytes(camera,encoded);File.SetLastWriteTimeUtc(camera,cameraStamp);
context=backend.CaptureTools();var move=Move(context);var saved=backend.CommitTools(move);
var readback=new ReplayCameraTrack();Check(saved.Verified&&readback.Load(source)&&readback.Keys[0].Frame==15&&ReplayCamera.Track.Keys[0].Frame==15,"verified publication updates retained file and SAME in-memory track with exact keys");
Check(Reject(()=>backend.CommitTools(move)),"old context cannot repeat track edit after original keys changed");
Check(readback.Keys[0].Curves==channels&&ReplayCamera.Track.Keys[0].Curves==channels,
    "translated native key preserves every curve byte through atomic sidecar publication");
var memoryBackend=(ITheatreCameraMemoryBackend)backend;
var beforeChannels=backend.CaptureTools();byte[] beforeChannelFile=File.ReadAllBytes(camera);
var editedKey=beforeChannels.Camera.Keys[0] with{Pose=beforeChannels.Camera.Keys[0].Pose with{Channels=new(2,4,3,3,4,1)}};
var curveEdit=new TheatreCameraToolsEdit(TheatreCameraToolsChange.FovEase,beforeChannels,ImmutableArray.Create(editedKey.Frame),
    beforeChannels.Camera.Keys,beforeChannels.Camera.Keys.SetItem(0,editedKey));
var channelDocument=memoryBackend.CommitMemory(curveEdit);
Check(ReplayCamera.Track.Keys[0].Curves==channels with{FovEase=ReplayCameraChannelEase.Out}
    &&BitConverter.SingleToInt32Bits(ReplayCamera.Track.Keys[0].Fov)==BitConverter.SingleToInt32Bits(readback.Keys[0].Fov)
    &&File.ReadAllBytes(camera).SequenceEqual(beforeChannelFile),
    "independent channel memory edit retains exact native lens bits and never writes the source sidecar");
var undoChannels=memoryBackend.RestoreMemory(channelDocument,beforeChannels);
Check(ReplayCamera.Track.Keys[0].Curves==channels,"native undo restores all curve metadata exactly");
var redoChannels=memoryBackend.RestoreMemory(undoChannels,channelDocument);
var savedChannels=memoryBackend.SaveMemory(redoChannels);
var channelReadback=new ReplayCameraTrack();
Check(channelReadback.Load(source)&&channelReadback.Keys[0].Curves==channels with{FovEase=ReplayCameraChannelEase.Out}
    &&File.ReadAllBytes(source).SequenceEqual(originalSource),
    "native redo/save persists independent curves without changing the replay archive");
memoryBackend.CancelMemoryPreview();
var cleanReview=backend.CaptureTools();
ReplayCamera.CommitReviewedKeyframe(source,new(30,new Vector3(4),Quaternion.Identity,1.3f,Curves:channels),remove:false);
Publish();
var refreshed=memoryBackend.RebindMemory(cleanReview);
Check(refreshed.Camera.Keys.Any(key=>key.Frame==30)&&refreshed.SavedKeys.SequenceEqual(refreshed.Camera.Keys)
    &&refreshed.Camera.CameraRevision!=cleanReview.Camera.CameraRevision&&!ReplayCamera.MemoryDocumentDirty,
    "clean explicit refresh adopts a newly basic-saved native key as its real saved baseline");
var refreshKey=refreshed.Camera.Keys[0];
var refreshEdit=new TheatreCameraToolsEdit(TheatreCameraToolsChange.Transform,refreshed,ImmutableArray.Create(refreshKey.Frame),
    refreshed.Camera.Keys,refreshed.Camera.Keys.SetItem(0,refreshKey with{Pose=refreshKey.Pose with{X=refreshKey.Pose.X+1}}));
var dirtyRefresh=memoryBackend.CommitMemory(refreshEdit);
Publish();var dirtyRebound=memoryBackend.RebindMemory(dirtyRefresh);
Check(dirtyRebound.Camera.Keys.SequenceEqual(dirtyRefresh.Camera.Keys)&&dirtyRebound.SavedKeys.SequenceEqual(refreshed.SavedKeys)
    &&ReplayCamera.MemoryDocumentDirty,"dirty refresh keeps original saved baseline and exact unsaved native keys");
Check(Reject(()=>memoryBackend.RebindMemory(refreshed)),"clean stale facade cannot adopt another active dirty document");
byte[] savedRefreshFile=File.ReadAllBytes(camera);var externalRefresh=new ReplayCameraTrack();
Check(externalRefresh.Load(source)&&externalRefresh.Put(new(40,new Vector3(5),Quaternion.Identity,1.1f))&&externalRefresh.Save(source),
    "external saved baseline fixture changes actual camera bytes");
byte[] dirtyState=ReplayCamera.Track.ExportState();
Check(Reject(()=>memoryBackend.RebindMemory(dirtyRebound))&&ReplayCamera.Track.ExportState().SequenceEqual(dirtyState),
    "dirty refresh rejects changed saved sidecar without losing native document state");
File.WriteAllBytes(camera,savedRefreshFile);
memoryBackend.DiscardMemory(dirtyRebound);Publish();
var discardedRefresh=memoryBackend.RebindMemory(dirtyRebound);
Check(discardedRefresh.Camera.Keys.SequenceEqual(refreshed.Camera.Keys)
    &&discardedRefresh.SavedKeys.SequenceEqual(discardedRefresh.Camera.Keys)&&!ReplayCamera.MemoryDocumentDirty,
    "explicit discard then refresh reloads saved baseline through the same facade");
Check(memoryBackend.RebindMemory(dirtyRebound).SavedKeys.SequenceEqual(discardedRefresh.SavedKeys),
    "discarded old-context refresh remains retryable after a successful backend capture");
File.WriteAllBytes(source,originalSource.Reverse().ToArray());File.SetLastWriteTimeUtc(source,stamp);
Check(Reject(()=>memoryBackend.RebindMemory(discardedRefresh)),"clean refresh retains full source ABA hash fence");
File.WriteAllBytes(source,originalSource);File.SetLastWriteTimeUtc(source,stamp);
object refreshOwner=DemoPlayback.PlaybackOwner;DemoPlayback.PlaybackOwner=new object();
Check(Reject(()=>memoryBackend.RebindMemory(discardedRefresh)),"clean refresh cannot rebind a restarted playback owner");
DemoPlayback.PlaybackOwner=refreshOwner;
context=backend.CaptureTools();backend.ChangeMotion(context,TheatreCameraMotionChange.ConstantSpeed);
Check(!backend.ToolsCurrent(context)&&Reject(()=>backend.CaptureTools()),"motion toggle invalidates actual presented settings until world picture publication");Publish();context=backend.CaptureTools();Check(!context.Motion.ConstantSpeed,"explicit new-picture capture reviews runtime speed change");
backend.ChangeMotion(context,TheatreCameraMotionChange.Collision);Check(Reject(()=>backend.CaptureTools()),"collision toggle cannot authorize old picture");Publish();context=backend.CaptureTools();Check(!context.Motion.Collision,"collision new-picture review");
backend.ChangeMotion(context,TheatreCameraMotionChange.Director);Check(Reject(()=>backend.CaptureTools()),"Director toggle cannot authorize old picture");Publish();context=backend.CaptureTools();Check(context.Motion.Director,"Director new-picture review");
ReplayVideoExporter.Active=true;Check(!backend.ToolsCurrent(context)&&Reject(()=>backend.CommitTools(Move(context))),"existing exporter owns camera and blocks advanced persistence");ReplayVideoExporter.Active=false;
DemoPlayback.Session.Transport.SeekGeneration++;Check(!backend.ToolsCurrent(context)&&Reject(()=>backend.CommitTools(Move(context))),"same-frame newer seek retires exact tools authority");Publish();context=backend.CaptureTools();DemoPlayback.PlaybackOwner=new object();Check(!backend.ToolsCurrent(context)&&Reject(()=>backend.CommitTools(Move(context))),"same-path restarted player cannot inherit old tool review");
// Actual binary publication helper with deterministic final-validation races.
var reviewed=ReplayReviewedMetadata.Read(camera,8192);byte[] outside=(byte[])reviewed.Bytes!.Clone();outside[20]^=1;
var outcome=ReplayReviewedMetadata.CommitBytes(reviewed,encoded,()=>{},()=>File.WriteAllBytes(camera,outside),maximum:8192);
Check(!outcome.Verified&&outcome.RecoveryPath!=null&&File.ReadAllBytes(outcome.RecoveryPath).SequenceEqual(outside),"binary camera race preserves exact displaced bytes and reports unverified publication");
File.Delete(camera);var absent=ReplayReviewedMetadata.Read(camera,8192);
Check(Reject(()=>ReplayReviewedMetadata.CommitBytes(absent,encoded,()=>{},()=>File.WriteAllBytes(camera,outside),maximum:8192))&&File.ReadAllBytes(camera).SequenceEqual(outside),"missing camera publication never overwrites externally created destination");File.WriteAllBytes(camera,encoded);
string descriptor=Path.Combine(dir,"SYNTHETIC.ppclip"),cache=Path.Combine(dir,"CACHE.ppdemo");File.WriteAllBytes(cache,new byte[]{5,4,3});var clip=new ReplayVirtualClipDocument(1,source,10,80,DateTime.UnixEpoch,"CLIP");File.WriteAllText(descriptor,JsonSerializer.Serialize(clip));
DemoPlayback.LogicalPath=descriptor;DemoPlayback.PlaybackPath=cache;var virtualTrack=new ReplayCameraTrack();virtualTrack.Put(new(10,Vector3.One,Quaternion.Identity,1.2f));Check(virtualTrack.Save(descriptor),"retained virtual camera binds source hash plus reviewed clip range");
var frozen=ReplayCameraTrackIdentity.Create(descriptor);Publish();context=backend.CaptureTools();DateTime descriptorStamp=File.GetLastWriteTimeUtc(descriptor);File.WriteAllText(descriptor,JsonSerializer.Serialize(clip with{StartFrame=11}));File.SetLastWriteTimeUtc(descriptor,descriptorStamp);
Check(Reject(()=>backend.CommitTools(Move(context))),"bounded descriptor SHA closes same-size same-stamp clip range ABA");
File.WriteAllBytes(descriptor+".camera",virtualTrack.EncodeReviewed(frozen));Check(!new ReplayCameraTrack().Load(descriptor),"reviewed encoding keeps original frozen source identity instead of silently rebinding old keys to changed descriptor");
Check(!Directory.EnumerateFiles(dir,".reviewed-*").Any(),"camera source tests clean only their proposed temporary files");
Console.WriteLine($"PASS {checks} actual-source camera tools identity/matrix/cache/binary-publication/owner checks; synthetic bytes only");
}finally{Directory.Delete(dir,true);}
