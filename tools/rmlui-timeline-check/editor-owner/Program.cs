using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Replay;
int checks=0;void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
bool Reject(Action action){try{action();return false;}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException){return true;}}
string temp=Path.Combine(Path.GetTempPath(),"prime-editor-draft-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
string path=Path.Combine(temp,"SYNTHETIC.ppdemo");File.WriteAllBytes(path,new byte[]{1,2,3,4,5,6,7,8});
DemoPlayback.LogicalPath=DemoPlayback.PlaybackPath=path;
DemoPlayback.Events=new[]{new ReplayEvent(10,ReplayEventType.WeaponFired,0,1,0),new ReplayEvent(12,ReplayEventType.Damage,0,1,70),new ReplayEvent(30,ReplayEventType.Kill,0,1),new ReplayEvent(35,ReplayEventType.WeaponFired,0,1,1),new ReplayEvent(37,ReplayEventType.Headshot,0,1),new ReplayEvent(40,ReplayEventType.Kill,0,1),new ReplayEvent(500,ReplayEventType.MatchEnded)};
DemoPlayback.Metadata=new(){Players=new[]{new ReplayPlayerInfo(0,0,0,"Hunter"),new ReplayPlayerInfo(1,1,1,"Target")}};
try {
var backend=RmlUiOpenGlSmoke.Fixture();
var readyScene=DemoPlayback.PresentationScene;var readyTransport=DemoPlayback.Session.Transport;var readyOwner=DemoPlayback.PlaybackOwner;
var readyFiles=Directory.GetFiles(temp).ToDictionary(file=>file,file=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))));
DemoPlayback.PresentationScene=null;
try
{
    Check(Reject(()=>backend.Capture()),"prepared recording without owned presentation cannot create an editor review");
    Check(ReferenceEquals(readyTransport,DemoPlayback.Session.Transport)&&ReferenceEquals(readyOwner,DemoPlayback.PlaybackOwner)
        &&Directory.GetFiles(temp).Length==readyFiles.Count&&readyFiles.All(pair=>File.Exists(pair.Key)&&Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key)))==pair.Value),
        "rejected early editor review preserves exact transport, player and authored file bytes");
}
finally{DemoPlayback.PresentationScene=readyScene;}
var context=backend.Capture();
Check(context.Owner==readyOwner&&context.Transport==readyTransport,"same backend captures successfully after owned presentation becomes ready");
Check(context.SourceRevision.Contains(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))),"strong physical source SHA");
Check(context.Events.Length==7&&context.Highlights.Length>0&&context.Analytics.Any(e=>e.Id=="player:0"),"actual ReplayStudio events/highlights/analytics capture");
var analytics=ReplayStudio.Analytics(DemoPlayback.Events,600);Check(analytics.TotalKills==2&&analytics.TotalDamage==70&&analytics.WeaponUsage.Sum(w=>w.Shots)==2,"actual retained analytics explicit-input values");
var changed=ReplayStudio.Highlights(new[]{new ReplayEvent(500,ReplayEventType.Objective)},600);Check(changed.All(h=>h.Kind==ReplayHighlightKind.Objective),"same-size explicit event snapshot bypasses default cache");
using var controller=new TheatreEditorController(backend);
Check(controller.SetName("MY RANGE")&&controller.Stage(TheatreEditorEditKind.AddSelection)&&!File.Exists(path+ReplayReels.Extension),"staged addition writes no bytes before confirmation");
Check(!controller.NextTab()&&controller.Snapshot().Pending!=null,"pending edit blocks unrelated draft changes");
Check(controller.Confirm()&&ReplayReels.Segments(path).Single().Name=="MY RANGE","actual atomic retained reel owner saves reviewed marks");
Check(!Directory.EnumerateFiles(temp,".reviewed-*").Any(),"atomic commit removes scratch file");
Check(controller.Stage(TheatreEditorEditKind.AddSelection),"stage second range");
DemoPlayback.Session.Transport.SetMarkOut(90);Check(!controller.Confirm()&&ReplayReels.Segments(path).Count==1&&controller.Snapshot().Name=="MY RANGE","stale marks reject save and preserve draft");controller.Cancel();Check(controller.Refresh(),"explicit refresh renews new mark review");
Check(controller.Stage(TheatreEditorEditKind.AddSelection)&&controller.Confirm()&&ReplayReels.Segments(path).Count==2,"retry after new review adds once");
for(int i=0;i<3;i++)Check(controller.NextTab(),"cycle native view");
Check(controller.Snapshot().Tab==TheatreEditorTab.Reel&&controller.Stage(TheatreEditorEditKind.MoveDown),"exact reorder staged");var before=ReplayReels.Segments(path).ToArray();
Check(controller.Confirm()&&ReplayReels.Segments(path)[1].Id==before[0].Id,"reviewed exact reorder preserves IDs");
Check(controller.Stage(TheatreEditorEditKind.CycleCamera)&&controller.Confirm()&&ReplayReels.Segments(path)[0].Camera==ReplaySegmentCamera.FirstPerson,"exact saved per-segment camera update");
Check(controller.Stage(TheatreEditorEditKind.Trim)&&controller.Confirm(),"trim uses captured paused marks");
context=backend.Capture();var reviewed=ReplayReviewedMetadata.Read(path+ReplayReels.Extension);byte[] original=File.ReadAllBytes(reviewed.Path);DateTime stamp=File.GetLastWriteTimeUtc(reviewed.Path);
var doc=ReplayReels.ReadReviewed(reviewed,path,600);doc.Segments[0]=doc.Segments[0] with{Name="XY RANGE"};File.WriteAllText(reviewed.Path,JsonSerializer.Serialize(doc,new JsonSerializerOptions{WriteIndented=true}));File.SetLastWriteTimeUtc(reviewed.Path,stamp);
Check(Reject(()=>backend.Seek(context,context.Reel[0],false)),"same-stamp reel SHA ABA refuses reviewed entry");File.WriteAllBytes(reviewed.Path,original);File.SetLastWriteTimeUtc(reviewed.Path,stamp);
Check(Reject(()=>ReplayReviewedMetadata.Commit(reviewed,doc,()=>throw new InvalidOperationException("OWNER REPLACED")))&&File.ReadAllBytes(reviewed.Path).SequenceEqual(original),"owner failure preserves original document");
int verify=0;Check(Reject(()=>ReplayReviewedMetadata.Commit(reviewed,doc,()=>{if(++verify==2)throw new InvalidOperationException("OWNER CHANGED AFTER TEMP WRITE");}))&&File.ReadAllBytes(reviewed.Path).SequenceEqual(original)&&!Directory.EnumerateFiles(temp,".reviewed-*").Any(),"post-temp validation failure rolls back original and deletes scratch");
File.WriteAllText(reviewed.Path,"{broken");Check(Reject(()=>backend.Capture()),"invalid saved reel refuses review instead of silently reset");File.WriteAllBytes(reviewed.Path,original);
var invalid=doc.Segments[0];doc.Segments.Add(invalid);File.WriteAllText(reviewed.Path,JsonSerializer.Serialize(doc));Check(Reject(()=>backend.Capture()),"duplicate reel IDs rejected");doc.Segments.RemoveAt(doc.Segments.Count-1);doc.Segments[0]=invalid with{EndFrame=601};File.WriteAllText(reviewed.Path,JsonSerializer.Serialize(doc));Check(Reject(()=>backend.Capture()),"out-of-duration range rejected");File.WriteAllBytes(reviewed.Path,original);
File.WriteAllBytes(reviewed.Path,new byte[ReplayReviewedMetadata.MaximumBytes+1]);Check(Reject(()=>backend.Capture()),"oversize sidecar rejected");File.WriteAllBytes(reviewed.Path,original);
context=backend.Capture();string ann=path+ReplayAnnotations.Extension;var named=ReplayAnnotations.AddHighlight(path,100,150,"NAMED");context=backend.Capture();
var removal=new TheatreEditorEdit(TheatreEditorEditKind.RemoveNamedHighlight,context,context.NamedHighlights.Single(),context.NamedHighlights,ImmutableArray<TheatreEditorEntry>.Empty);
var committed=backend.Commit(removal);Check(ReplayAnnotations.Highlights(path).Count==0&&committed.AnnotationRevision!=context.AnnotationRevision,"exact named highlight atomic removal returns actual revision");
context=backend.Capture();byte[] src=File.ReadAllBytes(path);DateTime sourceStamp=File.GetLastWriteTimeUtc(path);File.WriteAllBytes(path,src.Reverse().ToArray());File.SetLastWriteTimeUtc(path,sourceStamp);
Check(Reject(()=>backend.Seek(context,context.Events[0],false)),"same-length same-stamp physical source ABA rejects action");File.WriteAllBytes(path,src);File.SetLastWriteTimeUtc(path,sourceStamp);
// Denied file access keeps both the exact draft and the original bytes available for retry.
using(var locked=new FileStream(reviewed.Path,FileMode.Open,FileAccess.Read,FileShare.None))
Check(Reject(()=>backend.Capture()),"sharing violation cannot reinterpret saved reel as an empty document");
context=backend.Capture();var oldTransport=DemoPlayback.Session.Transport;
var exactEdit=new TheatreEditorEdit(TheatreEditorEditKind.ClearReel,context,null,context.Reel,ImmutableArray<TheatreEditorEntry>.Empty);
oldTransport.Seek(oldTransport.CurrentFrame,false);oldTransport.IsSeeking=false;
Check(Reject(()=>backend.Commit(exactEdit))&&ReplayReels.Segments(path).Count==context.Reel.Length,"same-frame newer seek generation rejects persistent confirmation");
context=backend.Capture();oldTransport.ClipIn=null;oldTransport.ClipOut=2;
backend.Seek(context,context.Reel[0],true);
Check(oldTransport.ClipIn==context.Reel[0].Frame&&oldTransport.ClipOut==context.Reel[0].EndFrame,"entry range selection replaces inverted preexisting mark clamps safely");oldTransport.IsSeeking=false;
context=backend.Capture();backend.Seek(context,context.Highlights[0],false);
Check(oldTransport.CurrentFrame==context.Highlights[0].FocusFrame,"automatic highlight seeks retained focus frame rather than preroll");oldTransport.IsSeeking=false;
string descriptor=Path.Combine(temp,"SYNTHETIC.ppclip"),cached=Path.Combine(temp,"CACHED.ppdemo");File.WriteAllBytes(cached,new byte[]{8,9,10});
var clip=new ReplayVirtualClipDocument(1,path,10,80,DateTime.UnixEpoch,"CLIP");File.WriteAllText(descriptor,JsonSerializer.Serialize(clip));
DemoPlayback.LogicalPath=descriptor;DemoPlayback.PlaybackPath=cached;context=backend.Capture();DateTime descriptorStamp=File.GetLastWriteTimeUtc(descriptor);
File.WriteAllText(descriptor,JsonSerializer.Serialize(clip with{StartFrame=11}));File.SetLastWriteTimeUtc(descriptor,descriptorStamp);
Check(Reject(()=>backend.Seek(context,context.Events[0],false)),"same-size same-stamp virtual descriptor ABA rejects reviewed editor action");File.WriteAllText(descriptor,JsonSerializer.Serialize(clip));File.SetLastWriteTimeUtc(descriptor,descriptorStamp);
context=backend.Capture();DateTime cacheStamp=File.GetLastWriteTimeUtc(cached);File.WriteAllBytes(cached,new byte[]{9,8,10});File.SetLastWriteTimeUtc(cached,cacheStamp);
Check(Reject(()=>backend.Seek(context,context.Events[0],false)),"same-stamp virtual materialization replacement rejects original reviewed editor");
DemoPlayback.LogicalPath=DemoPlayback.PlaybackPath=path;oldTransport.CurrentFrame=50;oldTransport.ClipIn=10;oldTransport.ClipOut=80;
context=backend.Capture();ReplayVideoExporter.Rendering=true;Check(Reject(()=>backend.Seek(context,context.Events[0],false))&&Reject(()=>backend.ToggleHud(context,TheatreEditorHud.Analytics)),"existing exporter owns clock and presentation settings");ReplayVideoExporter.Rendering=false;
context=backend.Capture();backend.InspectShot(context,null,0,-1);var transport=DemoPlayback.Session.Transport;Check(transport.IsSeeking&&SpectatorMode.Watched==0,"shot inspection uses original transport and actor");
Check(!backend.ReadLive(context).Diagnostic.Contains("ACTUAL DIAGNOSTIC"),"pending seek never reads unrelated frame evidence");transport.IsSeeking=false;Check(backend.ReadLive(context).Diagnostic.Contains("ACTUAL DIAGNOSTIC 35"),"same exact settled seek publishes retained diagnostic");
var own=ReplayCombatDiagnostics.Selected;transport.Seek(50,false);backend.ReadLive(context);Check(ReplayCombatDiagnostics.Selected==null&&transport.IsSeeking,"new seek retires evidence without cancelling current seek");
transport.IsSeeking=false;context=backend.Capture();backend.InspectShot(context,null,0,-1);transport.IsSeeking=false;backend.ReadLive(context);ReplayCombatDiagnostics.Selected=new(999);backend.ReadLive(context);backend.Retire();Check(ReplayCombatDiagnostics.Selected==new ReplayCombatDiagnostics.Selection(999),"closing editor preserves another diagnostic selection");
context=backend.Capture();backend.InspectShot(context,null,0,-1);
var comparison=RmlUiOpenGlSmoke.ComparisonProbe(backend);comparison.Active=true;
int comparisonPolls=comparison.Polls,comparisonStops=comparison.StopCalls,reviewRetires=comparison.ReviewRetires;
int diagnosticReads=DemoPlayback.PresentationScene!.ReplayPoses!.Reads;var selectedDiagnostic=ReplayCombatDiagnostics.Selected;
var newer=new ReplayTransport();DemoPlayback.Session.Transport=newer;
var staleReview=backend.ReadLive(context);var stillStale=backend.ReadLive(context);
Check(!newer.IsSeeking&&!staleReview.Current&&!stillStale.Current,"transport replacement retires pending inspection without stealing replacement");
Check(comparison.Polls==comparisonPolls+2&&comparison.StopCalls==comparisonStops&&comparison.Active&&comparison.ReviewRetires==reviewRetires+2,
    "stale edit review polls active comparison B without stopping its independent lifetime");
Check(DemoPlayback.PresentationScene.ReplayPoses.Reads==diagnosticReads&&ReplayCombatDiagnostics.Selected==selectedDiagnostic,
    "stale edit review never reads another transport's combat facts or clears another diagnostic selection");
backend.Retire();
Check(comparison.StopCalls==comparisonStops+1&&!comparison.Active,"explicit editor retirement stops comparison B exactly once");
DemoPlayback.Session.Transport=transport;transport.IsSeeking=false;
context=backend.Capture();var seekBackend=(ITheatreTimelineSeek)backend;
long seekBefore=transport.SeekGeneration;seekBackend.SeekTimelineFrame(context,123);
Check(transport.CurrentFrame==123&&transport.SeekGeneration==seekBefore+1&&transport.IsPaused&&transport.IsSeeking,"actual timeline exact-frame seek uses reviewed original transport without resuming it");
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,124))&&transport.SeekGeneration==seekBefore+1,"same-frame/ongoing seek cannot reuse the old exact timeline receipt");transport.IsSeeking=false;
context=backend.Capture();seekBefore=transport.SeekGeneration;
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,601))&&transport.SeekGeneration==seekBefore,"out-of-duration exact frame cannot start a seek");
transport.SetMarkOut(transport.ClipOut!.Value+1);
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,200))&&transport.SeekGeneration==seekBefore,"changed reviewed selection marks cannot lend authority to timeline seek");
context=backend.Capture();byte[] timelineSource=File.ReadAllBytes(path);DateTime timelineStamp=File.GetLastWriteTimeUtc(path);File.WriteAllBytes(path,timelineSource.Reverse().ToArray());File.SetLastWriteTimeUtc(path,timelineStamp);
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,200))&&transport.SeekGeneration==seekBefore,"actual strong source SHA rejects same-size same-stamp exact timeline seek ABA");File.WriteAllBytes(path,timelineSource);File.SetLastWriteTimeUtc(path,timelineStamp);
context=backend.Capture();ReplayVideoExporter.Rendering=true;
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,200))&&transport.SeekGeneration==seekBefore,"timeline cannot steal existing export render clock");ReplayVideoExporter.Rendering=false;
context=backend.Capture();var replacementTransport=new ReplayTransport();DemoPlayback.Session.Transport=replacementTransport;
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,200))&&!replacementTransport.IsSeeking&&transport.SeekGeneration==seekBefore,"exact timeline seek cannot resolve or mutate replacement Current transport");DemoPlayback.Session.Transport=transport;
context=backend.Capture();DemoPlayback.ReplaceOwner();
Check(Reject(()=>seekBackend.SeekTimelineFrame(context,200))&&transport.SeekGeneration==seekBefore,"same-path restarted player cannot inherit exact timeline seek receipt");
context=backend.Capture();DemoPlayback.ReplaceOwner();Check(!backend.ReadLive(context).Current&&Reject(()=>backend.Seek(context,context.Events[0],false)),"same-path replacement player rejects old editor");
Console.WriteLine($"PASS {checks} actual source-linked ReplayStudio/editor/reel/atomic/inspection checks; temporary synthetic bytes only");
} finally {Directory.Delete(temp,true);}
ControllerChecks.Run();
PublicationChecks.Run();
