using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
var backend=new Memory();using var control=new TheatreCameraToolsController(backend);
Check(control.Snapshot().Selection.SequenceEqual(new uint[]{10})&&control.Snapshot().Focus?.Frame==10,"initial selection is local first saved key");
Check(control.MoveFocus(1)&&control.ToggleSelection()&&control.Snapshot().Selection.SequenceEqual(new uint[]{10,20}),"toggle forms bounded multiple selection without engine preview");
Check(control.MoveFocus(1)&&control.SelectRange()&&control.Snapshot().Selection.SequenceEqual(new uint[]{10,20,40}),"range selection uses stable local anchor");
Check(control.SelectAll()&&control.Copy()&&backend.Commits==0&&backend.Previews==0,"multi-copy changes no persisted bytes or replay owner");
Check(control.ClearSelection()&&control.Destination("100")&&control.Stage(TheatreCameraToolsChange.Paste),"copied group can paste with no current selection");
Check(control.Snapshot().Pending!.After.Select(k=>k.Frame).SequenceEqual(new uint[]{10,20,40,100,110,130}),"paste translates key spacing intact");
backend.Fail=true;Check(!control.Confirm()&&control.Snapshot().Pending!=null&&control.Snapshot().Destination==100,"writer failure retains exact before/after and destination");backend.Fail=false;
Check(control.Confirm()&&backend.Context.Camera.Keys.Length==6&&!control.Snapshot().Current,"successful group paste retires pose authority until new picture");
Check(!control.Refresh(),"same presentation revision cannot reauthorize changed key track");backend.Publish();Check(control.Refresh()&&control.Snapshot().Current,"new successful picture plus explicit refresh reauthorizes");
Check(control.SelectAll()&&control.Destination("0")&&control.Stage(TheatreCameraToolsChange.Move),"whole key group may translate to frame zero");
Check(control.Snapshot().Pending!.After.Select(k=>k.Frame).SequenceEqual(new uint[]{0,10,30,90,100,120}),"group move preserves all relative gaps");control.Cancel();
Check(!control.Destination("-1")&&!control.Destination("1.0")&&!control.Destination("601"),"destination parser rejects negative decimal and past replay end");
Check(control.Destination("590")&&!control.Stage(TheatreCameraToolsChange.Move),"complete moved group must fit duration");
Check(control.ClearSelection()&&control.Nearest()&&control.Snapshot().Selection.SequenceEqual(new uint[]{40}),"nearest focus chooses key at reviewed frame");
Check(control.Destination("20")&&!control.Stage(TheatreCameraToolsChange.Move)&&backend.Commits==1,"collision cannot silently overwrite unselected key");
Check(control.Stage(TheatreCameraToolsChange.SnapKill)&&control.Snapshot().Pending!.After.Any(k=>k.Frame==55),"single snap uses reviewed nearest kill marker");control.Cancel();
Check(control.Stage(TheatreCameraToolsChange.SnapBookmark)&&control.Snapshot().Pending!.After.Any(k=>k.Frame==35),"bookmark snap uses immutable reviewed marker");control.Cancel();
Check(control.Stage(TheatreCameraToolsChange.SnapEvent)&&control.Snapshot().Pending!.After.Any(k=>k.Frame==45),"event snap uses immutable nearest event");control.Cancel();
Check(control.SelectAll()&&!control.Stage(TheatreCameraToolsChange.SnapKill),"snap requires exact single selection");
Check(control.Stage(TheatreCameraToolsChange.Interpolation)&&control.Snapshot().Pending!.After.All(k=>k.Pose.Interpolation==1),"interpolation cycle stages entire selected group");control.Cancel();
Check(control.Stage(TheatreCameraToolsChange.Ease)&&control.Snapshot().Pending!.After.All(k=>k.Pose.Ease==1),"easing cycle stages entire selected group");control.Cancel();
Check(control.Stage(TheatreCameraToolsChange.LookAt)&&control.Snapshot().Pending!.After.All(k=>k.Pose.LookAt==1),"look-at cycle uses reviewed available slot choices");control.Cancel();
Check(control.Stage(TheatreCameraToolsChange.Remove)&&control.Snapshot().Pending!.After.IsEmpty,"batch remove shows complete exact resulting track");
backend.Current=false;Check(!control.Confirm()&&backend.Context.Camera.Keys.Length==6,"owner retirement blocks confirmed batch removal");control.Cancel();backend.Current=true;Check(!control.Stage(TheatreCameraToolsChange.Remove),"authority does not silently return after stale owner");backend.Publish();Check(control.Refresh(),"explicit new review permits bounded editing again");
Check(control.Motion(TheatreCameraMotionChange.ConstantSpeed)&&!control.Snapshot().Current&&!control.Refresh(),"runtime speed toggle requires newer actual picture review");backend.Publish();Check(control.Refresh()&&backend.Context.Motion.ConstantSpeed,"constant speed is retained runtime option");
Check(control.Motion(TheatreCameraMotionChange.Collision),"collision control delegates current runtime owner");backend.Publish();Check(control.Refresh(),"collision after-picture refresh");
Check(control.Motion(TheatreCameraMotionChange.Director),"Director control delegates current runtime owner");backend.Publish();Check(control.Refresh(),"Director after-picture refresh");
Check(control.Preview()&&backend.Previews==1&&!control.Refresh(),"preview requests existing key owner and requires newer picture");backend.Publish();Check(control.Refresh(),"preview settles to an explicitly reviewed picture");
Check(control.ClearSelection()&&control.Stage(TheatreCameraToolsChange.Duplicate)&&control.Snapshot().Pending!.After.Any(k=>k.Frame==50),"duplicate preceding key uses reviewed playhead without requiring UI selection");control.Cancel();
Check(control.SelectAll()&&control.Stage(TheatreCameraToolsChange.Remove),"stage exact all-key removal before unknown publication proof");backend.Unverified=true;
Check(control.Confirm()&&control.Snapshot().Pending==null&&!control.Snapshot().Current&&control.Snapshot().Status.Contains("unverified"),"unverified publication retires pending edit without a false rollback or blind retry");
Check(!control.Confirm(),"completed publication cannot be repeated");backend.Unverified=false;backend.Publish();Check(control.Refresh(),"fresh explicit review after inspected publication");
Check(Task.Run(()=>{try{control.Snapshot();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"wrong owner thread rejected");
backend.Context=backend.Context with{SourceRevision="REPLACED"};Check(!control.Refresh(),"same-path source replacement cannot renew old tools context");control.Dispose();Check(control.Snapshot().ClipboardCount==0&&!control.Copy(),"dispose drops bounded clipboard and pending authority");
var capped=new Memory();capped.Context=capped.Context with{Camera=capped.Context.Camera with{Keys=Enumerable.Range(0,64).Select(frame=>new TheatreCameraKey((uint)frame,new(1,2,3,0,0,0,1,80,0,-1,0,0))).ToImmutableArray()}};
using(var maximum=new TheatreCameraToolsController(capped))Check(maximum.SelectAll()&&maximum.Copy()&&maximum.Destination("100")&&!maximum.Stage(TheatreCameraToolsChange.Paste)&&capped.Commits==0,"64-key capacity rejects complete overflowing paste before any persistence");
var rectangleSelection=new Memory();using(var box=new TheatreCameraToolsController(rectangleSelection))
{
 Check(box.SelectFrames(ImmutableArray.Create(40u,10u,40u))&&box.Snapshot().Selection.SequenceEqual(new[]{10u,40u})&&rectangleSelection.Commits==0,"rectangle selection deduplicates exact authored frames without persistence");
 Check(!box.SelectFrames(ImmutableArray.Create(999u))&&box.Snapshot().Selection.SequenceEqual(new[]{10u,40u})&&rectangleSelection.Commits==0,"unknown frame rejects whole rectangle selection atomically");
 Check(!box.Snapshot().Current,"unknown rectangle frame retires old review authority");rectangleSelection.Publish();Check(box.Refresh(),"explicit new picture review recovers rectangle selection after stale frame");
 Check(box.SelectFrames(ImmutableArray<uint>.Empty)&&box.Snapshot().Selection.IsEmpty,"empty rectangle clears selection");
 rectangleSelection.Current=false;Check(!box.SelectFrames(ImmutableArray.Create(20u))&&box.Snapshot().Selection.IsEmpty&&rectangleSelection.Commits==0,"source owner retirement refuses rectangle selection");
}
for(int value=0;value<5;value++)
{
 var memory=new Memory();memory.Context=memory.Context with{Camera=memory.Context.Camera with{Keys=memory.Context.Camera.Keys.Select(k=>k with{Pose=k.Pose with{Interpolation=(value+1)%5}}).ToImmutableArray()}};
 using var direct=new TheatreCameraToolsController(memory);Check(direct.SelectAll()&&direct.StageChoice(TheatreCameraToolsChange.Interpolation,value)&&direct.Snapshot().Pending!.After.All(k=>k.Pose.Interpolation==value)&&memory.Commits==0,"explicit interpolation stages exact reviewed choice "+value);direct.Cancel();
}
for(int value=0;value<4;value++)
{
 var memory=new Memory();memory.Context=memory.Context with{Camera=memory.Context.Camera with{Keys=memory.Context.Camera.Keys.Select(k=>k with{Pose=k.Pose with{Ease=(value+1)%4}}).ToImmutableArray()}};
 using var direct=new TheatreCameraToolsController(memory);Check(direct.SelectAll()&&direct.StageChoice(TheatreCameraToolsChange.Ease,value)&&direct.Snapshot().Pending!.After.All(k=>k.Pose.Ease==value)&&memory.Commits==0,"explicit easing stages exact reviewed choice "+value);direct.Cancel();
}
foreach(int value in new[]{-1,1,3})
{
 var memory=new Memory();memory.Context=memory.Context with{Camera=memory.Context.Camera with{Keys=memory.Context.Camera.Keys.Select(k=>k with{Pose=k.Pose with{LookAt=value==-1?1:-1}}).ToImmutableArray()}};
 using var direct=new TheatreCameraToolsController(memory);Check(direct.SelectAll()&&direct.StageChoice(TheatreCameraToolsChange.LookAt,value)&&direct.Snapshot().Pending!.After.All(k=>k.Pose.LookAt==value)&&memory.Commits==0,"explicit look-at uses reviewed available slot "+value);direct.Cancel();
}
var directBackend=new Memory();using(var direct=new TheatreCameraToolsController(directBackend))
{
 Check(direct.SelectAll(),"direct choice review selects saved keys");
 Check(!direct.StageChoice(TheatreCameraToolsChange.Interpolation,0)&&direct.Snapshot().Pending==null,"already-selected explicit choice rejects a no-op without confirmation");
 Check(!direct.StageChoice(TheatreCameraToolsChange.Interpolation,5)&&!direct.StageChoice(TheatreCameraToolsChange.Ease,-1)&&!direct.StageChoice(TheatreCameraToolsChange.LookAt,2)&&direct.Snapshot().Pending==null&&directBackend.Commits==0,"invalid direct choices cannot create a pending edit or write camera files");
 Check(direct.StageChoice(TheatreCameraToolsChange.Ease,3)&&direct.Confirm()&&directBackend.Commits==1,"direct selection commits only after existing explicit confirmation");
 Check(!direct.StageChoice(TheatreCameraToolsChange.Ease,1),"direct choices retain post-publication picture fence");
}
foreach(float fov in new[]{10f,150f})Check(new TheatreWorkspaceCameraValues(0,0,0,0,0,0,fov).ValidFov,"legacy direct camera FOV endpoint accepted "+fov);
foreach(float fov in new[]{9.99f,150.01f,float.NaN})Check(!new TheatreWorkspaceCameraValues(0,0,0,0,0,0,fov).ValidFov,"invalid direct camera FOV rejected "+fov);
Console.WriteLine($"PASS {checks} camera tools multiselect/copy/move/snap/settings/confirmation/owner checks");
PresenterChecks.Run();
sealed class Memory:ITheatreCameraToolsBackend,ITheatreCameraBackend {
 internal bool Current=true,Fail,Unverified;internal int Commits,Previews;
 private static TheatreCameraPose Pose=new(1,2,3,0,0,0,1,80,0,-1,0,0);
 internal TheatreCameraToolsContext Context=new(new(1,new object(),new object(),0,"SYNTHETIC","SYNTHETIC","RECORDING","CAMERA",50,600,true,Pose,
 ImmutableArray.Create(new TheatreCameraKey(10,Pose),new TheatreCameraKey(20,Pose),new TheatreCameraKey(40,Pose)),false){PresentationRevision=1},new object(),"SOURCE","ANNOTATION",
 ImmutableArray.Create(new TheatreCameraMarker("event",45,TheatreCameraMarkerKind.Event),new TheatreCameraMarker("kill",55,TheatreCameraMarkerKind.Kill),new TheatreCameraMarker("bookmark",35,TheatreCameraMarkerKind.Bookmark)),ImmutableArray.Create(1,3),new(false,true,false),new object());
 public TheatreCameraContext Capture()=>Context.Camera;
 public bool IsCurrent(TheatreCameraContext context)=>Current;
 public void SetFreeCamera(TheatreCameraContext context){}
 public void AdjustLens(TheatreCameraContext context,float fov,float roll){}
 public void Preview(TheatreCameraContext context,TheatreCameraKey key){}
 public void ToggleTrack(TheatreCameraContext context){}
 public void Commit(TheatreCameraEdit edit)=>throw new InvalidOperationException("No basic persistence in tools fixture.");
 public TheatreCameraToolsContext CaptureTools()=>Context;
 public void ValidateReviewedTools(TheatreCameraToolsContext context){if(!ToolsCurrent(context))throw new TheatreCameraStaleException("Original reviewed camera owner changed.");}
 public bool ToolsCurrent(TheatreCameraToolsContext context)=>Current;
 public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit reviewed){if(Fail)throw new IOException("Synthetic sharing failure");Commits++;Context=Context with{Camera=Context.Camera with{Keys=reviewed.After}};return new(!Unverified,Unverified?"Published but unverified; inspect recovery and refresh.":"");}
 public void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey focus)=>Previews++;
 public void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change){Context=Context with{Motion=change switch{TheatreCameraMotionChange.ConstantSpeed=>Context.Motion with{ConstantSpeed=!Context.Motion.ConstantSpeed},TheatreCameraMotionChange.Collision=>Context.Motion with{Collision=!Context.Motion.Collision},_=>Context.Motion with{Director=!Context.Motion.Director}}};}
 internal void Publish()=>Context=Context with{Camera=Context.Camera with{PresentationRevision=Context.Camera.PresentationRevision+1}};
}
