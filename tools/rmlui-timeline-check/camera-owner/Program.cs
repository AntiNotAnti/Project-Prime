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
CameraPathCacheChecks.Run(Check);
var large=new ReplayCameraTrack();for(uint i=0;i<1024;i++)Check(large.Put(new(i*10,new Vector3(i,i%5,0),Quaternion.Identity,1.2f)),"large bounded key "+i);
Check(!large.Put(new(20000,Vector3.Zero,Quaternion.Identity,1.2f)),"1025th authored key rejected");
byte[] state=large.ExportState();var imported=new ReplayCameraTrack();Check(state.Length>8192&&imported.ImportState(state)&&imported.Keys.SequenceEqual(large.Keys),"1024-key portable state round-trips above legacy 8192 bound");
Check(large.Save(source)&&new ReplayCameraTrack().Load(source),"v6 scalable sidecar round trip");
var crop=large.Crop(25,9995);var cropImported=new ReplayCameraTrack();Check(cropImported.ImportState(crop.ExportState()),"large cropped state import");
for(int i=0;i<100;i++){double f=i*95.125;large.Sample(f+25,out var a,true);cropImported.Sample(f,out var b,true);Check(a.Position==b.Position&&a.Fov==b.Fov,"large crop evaluator exact "+i);}
File.Delete(source+".camera");

DemoPlayback.LogicalPath=DemoPlayback.PlaybackPath=source;
var track=new ReplayCameraTrack();track.Put(new(10,Vector3.One,Quaternion.Identity,1.2f));track.Put(new(20,new Vector3(2),Quaternion.Identity,1.4f));Check(track.Save(source),"retained owner writes synthetic source-bound v3 camera");
var identity=ReplayCameraTrackIdentity.Create(source);byte[] encoded=track.EncodeReviewed(identity);
Check(encoded.SequenceEqual(File.ReadAllBytes(source+".camera")),"reviewed binary encoder matches retained Save format byte-for-byte");
for(byte version=1;version<=5;version++)
{
 using var body=new MemoryStream();using var writer=new BinaryWriter(body);
 writer.Write(0x4D435046u);writer.Write(version);
 if(version>=3){writer.Write(identity.LogicalReplayId);writer.Write(identity.SourceContentHash);writer.Write(identity.StartFrame);writer.Write(identity.EndFrame);}
 else{writer.Write(new FileInfo(source).Length);writer.Write(File.GetLastWriteTimeUtc(source).Ticks);}
 if(version==5){writer.Write(10u);writer.Write(20u);}
 writer.Write((ushort)track.Keys.Count);
 foreach(var key in track.Keys)
 {
  writer.Write(key.Frame);writer.Write(key.Position.X);writer.Write(key.Position.Y);writer.Write(key.Position.Z);
  writer.Write(key.Rotation.X);writer.Write(key.Rotation.Y);writer.Write(key.Rotation.Z);writer.Write(key.Rotation.W);writer.Write(key.Fov);writer.Write(key.LookAtSlot);
  if(version>=2){writer.Write(key.Roll);writer.Write((byte)key.Interpolation);writer.Write((byte)key.Ease);}
  if(version>=4){writer.Write(false);writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(false);writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(0f);writer.Write(0f);}
 }
 writer.Flush();byte[] legacyBody=body.ToArray();writer.Write(System.Security.Cryptography.SHA256.HashData(legacyBody));writer.Flush();File.WriteAllBytes(source+".camera",body.ToArray());
 var legacy=new ReplayCameraTrack();Check(legacy.Load(source)&&legacy.Keys.Count==2&&legacy.Sample(version==5?5:15,out _),"legacy sidecar v"+version+" migration");
}
File.WriteAllBytes(source+".camera",encoded);
foreach(var portable in new[]{track,track.Crop(12,18)})
{
 byte[] modern=portable.ExportState();bool window=modern[5]==1;int countOffset=window?14:6;
 using var body=new MemoryStream();using var writer=new BinaryWriter(body);
 byte[] header=modern[..countOffset];header[4]=1;writer.Write(header);writer.Write((ushort)track.Keys.Count);
 writer.Write(modern.AsSpan(countOffset+4,modern.Length-32-countOffset-4));writer.Flush();byte[] legacy=body.ToArray();
 writer.Write(System.Security.Cryptography.SHA256.HashData(legacy));writer.Flush();var migration=new ReplayCameraTrack();
 Check(migration.ImportState(body.ToArray())&&migration.ExportState().SequenceEqual(modern),"legacy v1 portable state migrates without changing crop or key bits");
}
// Steady-state evaluation is allocation-free after warming every bounded segment.
for(int i=0;i<1023;i++)large.Sample(i*10+5.5,out _,true);
long ticks=System.Diagnostics.Stopwatch.GetTimestamp();long allocated=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<100000;i++)large.Sample((i%1023)*10+5.5,out _,true);
long elapsed=System.Diagnostics.Stopwatch.GetTimestamp()-ticks;
long cameraAllocated=GC.GetAllocatedBytesForCurrentThread()-allocated;Console.WriteLine($"Camera allocation: {cameraAllocated} bytes");
Check(cameraAllocated<4096,"cached high-refresh evaluator does not allocate per-sample tables");
Console.WriteLine($"Camera cached 1024-key evaluator: {elapsed*1e9/System.Diagnostics.Stopwatch.Frequency/100000:0.0} ns/sample (synthetic CPU mean; no GPU or device claim)");

var annotations=new ReplayAnnotationDocument{Bookmarks=new(){new(Guid.NewGuid(),35,"NEAR BOOKMARK",DateTime.UnixEpoch)}};File.WriteAllText(source+ReplayAnnotations.Extension,JsonSerializer.Serialize(annotations));
Publish();var backend=RmlUiOpenGlSmoke.Fixture();var context=backend.CaptureTools();
Check(context.Camera.Pose.X==9&&context.Camera.Pose.Y==8&&context.Camera.Pose.Z==7&&Math.Abs(context.Camera.Pose.Fov-80)<.01f,"tools use actual matrix-decoded successful picture, not raw key poses");
Check(context.Markers.Length==3&&context.Markers.Any(m=>m.Frame==35)&&context.LookAtSlots.SequenceEqual(new[]{1,3}),"explicit capture bounds nearest marker choices and actual metadata target slots");
int reads=DemoPlayback.LogicalReads;bool allCurrent=true;for(int i=0;i<20;i++)allCurrent&=backend.ToolsCurrent(context);Check(allCurrent,"readonly current owner fence remains stable across repeated native polling");Check(DemoPlayback.LogicalReads==reads,"readonly ToolsCurrent never resolves logicalpath or reads files");
var memory=(ITheatreCameraMemoryBackend)backend;
byte[] sourceBefore=File.ReadAllBytes(source),cameraBefore=File.ReadAllBytes(source+".camera");
uint frameBefore=ReplayController.CurrentFrame;long generationBefore=DemoPlayback.Session.Transport.SeekGeneration;
var memoryMove=Move(context);memory.PreviewMemory(memoryMove);
Check(ReplayCamera.TryMemoryPreview(DemoPlayback.PresentationScene!,out _)&&ReplayCamera.Track.Keys[0].Frame==10
 &&ReplayController.CurrentFrame==frameBefore&&DemoPlayback.Session.Transport.SeekGeneration==generationBefore
 &&File.ReadAllBytes(source+".camera").SequenceEqual(cameraBefore),"live camera-only preview changes neither world clock nor disk nor committed keys");
memory.CancelMemoryPreview();
var changed=memory.CommitMemory(memoryMove);
Check(ReplayCamera.MemoryDocumentDirty&&!ReplayCamera.FixtureLegacySave()&&Reject(()=>ReplayCamera.CommitReviewedKeyframe(source,track.Keys[0],true))
 &&File.ReadAllBytes(source+".camera").SequenceEqual(cameraBefore),"both legacy keyboard and basic native persistence refuse dirty advanced documents");
ReplayCamera.ReloadReviewedTrack(source);Check(ReplayCamera.Track.Keys[0].Frame==15,"basic capture reload cannot discard dirty advanced keys");
Check(changed.Camera.Keys[0].Frame==15&&backend.ToolsCurrent(changed)&&File.ReadAllBytes(source+".camera").SequenceEqual(cameraBefore),"release commits memory once with no sidecar publication");
context=memory.RestoreMemory(changed,context);
ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
Check(context.Camera.Keys[0].Frame==10&&backend.ToolsCurrent(context),"native memory history restores source-bound key bits");
memory.CancelMemoryPreview();

// Restart recovery uses the same exact source and original sidecar baseline; conflicting files are never adopted.
var recoveryEdit=memory.CommitMemory(Move(context));ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
Check(File.Exists(source+".camera.draft")&&File.ReadAllBytes(source+".camera").SequenceEqual(encoded),"committed memory draft gains atomic background recovery without publishing camera");
ReplayCamera.MemoryDocumentDirty=false;ReplayCamera.ClearMemoryPreview();ReplayCamera.ReloadReviewedTrack(source);Publish();
var restarted=RmlUiOpenGlSmoke.Fixture();var recoveredContext=restarted.CaptureTools();
Check(recoveredContext.Camera.Keys[0].Frame==15&&recoveredContext.SavedKeys[0].Frame==10&&ReplayCamera.MemoryDocumentDirty,"restart automatically restores only source-bound recovery and retains saved baseline");
DemoPlayback.Session.Transport.Seek(100);Publish();var rebound=((ITheatreCameraMemoryBackend)restarted).RebindMemory(recoveredContext);
Check(rebound.Camera.Frame==100&&rebound.Camera.Keys[0].Frame==15&&restarted.ToolsCurrent(rebound),"dirty native document rebinds across seek generation without reload or hash weakening");
((ITheatreCameraMemoryBackend)restarted).DiscardMemory(rebound);ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
Check(!File.Exists(source+".camera.draft")&&!ReplayCamera.MemoryDocumentDirty&&ReplayCamera.Track.Keys[0].Frame==10,"discard retires queued recovery and restores original saved keys");
var recoverState=track.ExportState();
ReplayCameraDraftStore.Queue(source,identity,"ORIGINAL",recoverState);ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
Check(!ReplayCameraDraftStore.TryRecover(source,identity,"CHANGED",out _),"changed original sidecar revision refuses recovery");
var wrongIdentity=identity with{SourceContentHash=System.Security.Cryptography.SHA256.HashData(new byte[]{99})};
Check(!ReplayCameraDraftStore.TryRecover(source,wrongIdentity,"ORIGINAL",out _),"changed source content hash refuses recovery");
for(int attempt=0;attempt<100;attempt++){ReplayCameraDraftStore.Queue(source,identity,"ORIGINAL",recoverState);ReplayCameraDraftStore.Retire(source);}
Check(!ReplayCameraDraftStore.TryRecover(source,identity,"ORIGINAL",out _),"retirement immediately tombstones recovery before background deletion");
ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
Check(!File.Exists(source+".camera.draft"),"100 queue-versus-retire races cannot resurrect a draft after Save or Discard");
ReplayCameraDraftStore.Queue(source,identity,"ORIGINAL",recoverState);ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
byte[] malformed=File.ReadAllBytes(source+".camera.draft");for(int bad=5;bad<10;bad++)malformed[bad]=255;
System.Security.Cryptography.SHA256.HashData(malformed.AsSpan(0,malformed.Length-32)).CopyTo(malformed.AsSpan(malformed.Length-32));
File.WriteAllBytes(source+".camera.draft",malformed);Check(!ReplayCameraDraftStore.TryRecover(source,identity,"ORIGINAL",out _),"malformed checksummed recovery string is rejected without blocking tools");
ReplayCameraDraftStore.Retire(source);ReplayCameraDraftStore.PendingWrites(source).GetAwaiter().GetResult();
DemoPlayback.Session.Transport.CurrentFrame=50;Publish();backend=RmlUiOpenGlSmoke.Fixture();context=backend.CaptureTools();
long firstRevision=context.Camera.PresentationRevision;Publish();
Check(backend.CaptureTools().Camera.PresentationRevision>firstRevision&&backend.ToolsCurrent(context),"actual retained paused presented cache may advance picture IDs while identical pose/owner remains reviewed");
backend.ValidateReviewedTools(context);
Check(backend.ToolsCurrent(context),"explicit same-owner source validation does not require identical advancing picture IDs");
byte[] originalSource=File.ReadAllBytes(source);DateTime stamp=File.GetLastWriteTimeUtc(source);File.WriteAllBytes(source,originalSource.Reverse().ToArray());File.SetLastWriteTimeUtc(source,stamp);
Check(Reject(()=>backend.CommitTools(Move(context)))&&File.ReadAllBytes(source+".camera").SequenceEqual(encoded),"same-size same-stamp source ABA rejects before camera publication");File.WriteAllBytes(source,originalSource);File.SetLastWriteTimeUtc(source,stamp);
string annotationPath=source+ReplayAnnotations.Extension;byte[] originalAnnotations=File.ReadAllBytes(annotationPath);DateTime annStamp=File.GetLastWriteTimeUtc(annotationPath);annotations.Bookmarks[0]=annotations.Bookmarks[0] with{Frame=36};File.WriteAllText(annotationPath,JsonSerializer.Serialize(annotations));File.SetLastWriteTimeUtc(annotationPath,annStamp);
Check(Reject(()=>backend.CommitTools(Move(context))),"same-stamp marker-sidecar change rejects saved snap authority");File.WriteAllBytes(annotationPath,originalAnnotations);File.SetLastWriteTimeUtc(annotationPath,annStamp);
string camera=source+".camera";DateTime cameraStamp=File.GetLastWriteTimeUtc(camera);track.Put(track.Keys[0] with{Position=new Vector3(3)});File.WriteAllBytes(camera,track.EncodeReviewed(identity));File.SetLastWriteTimeUtc(camera,cameraStamp);
Check(Reject(()=>backend.CommitTools(Move(context)))&&ReplayCamera.Track.Keys[0].Frame==10,"camera sidecar ABA cannot overwrite reviewed key owner or live track");File.WriteAllBytes(camera,encoded);File.SetLastWriteTimeUtc(camera,cameraStamp);
context=backend.CaptureTools();var move=Move(context);var saved=backend.CommitTools(move);
var readback=new ReplayCameraTrack();Check(saved.Verified&&readback.Load(source)&&readback.Keys[0].Frame==15&&ReplayCamera.Track.Keys[0].Frame==15,"verified publication updates retained file and SAME in-memory track with exact keys");
Check(Reject(()=>backend.CommitTools(move)),"old context cannot repeat track edit after original keys changed");
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
