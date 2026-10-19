using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

int checks=0;
void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;}
bool Reject(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
var backend=new Memory();using var camera=new TheatreCameraToolsController(backend);
var state=new TheatreTimelineViewState();var document=new RmlUiDocumentToken(2,8);
var metrics=new TimelineMetrics(1280,720,640,360,2);var box=new TimelineBox(100,80,600,24);
void Paint(bool successful=true,TimelineMetrics? alternate=null,TimelineBox? alternateBox=null)
{
    var snapshot=camera.Snapshot();var area=alternateBox??box;
    var keys=snapshot.Context.Camera.Keys.Where(key=>key.Frame>=state.Window.First&&key.Frame<=state.Window.Last)
        .Select(key=>new TimelineKeyBox(key.Frame,new(Math.Clamp(state.Window.X(key.Frame,area)-4,area.X,area.X+area.Width-8),area.Y+2,8,20))).ToImmutableArray();
    var ticket=state.Prepare(snapshot,document,alternate??metrics,area,keys,true);state.Presented(ticket,document,alternate??metrics,successful);
}
double WindowX(uint frame)=>state.Window.X(frame,box)/2;
state.Review(camera.Snapshot());Check(state.Window==new TimelineWindow(0,600)&&state.Zoom==1,"initial exact full-recording frame window");
Check(state.HitKey(55,45,camera.Snapshot(),document,metrics,true)==null,"native marker cannot be hit before a successfully presented DOM ticket");
var initialKeys=camera.Snapshot().Context.Camera.Keys.Select(key=>new TimelineKeyBox(key.Frame,new(state.Window.X(key.Frame,box)-4,82,8,20))).ToImmutableArray();
var oldPrepared=state.Prepare(camera.Snapshot(),document,metrics,box,initialKeys,true);
var newPrepared=state.Prepare(camera.Snapshot(),document,metrics,box,initialKeys,true);
Check(!state.Presented(oldPrepared,document,metrics,true),"older draw completion cannot publish a newer prepared DOM ticket");
Check(state.Presented(newPrepared,document,metrics,true),"only the exact prepared native picture ticket can be published");
Paint();Check(state.HitKey(55,45,camera.Snapshot(),document,metrics,true)==10,"one client-to-framebuffer conversion hits exact drawn key at density2");
Check(state.HitKey(110,90,camera.Snapshot(),document,metrics,true)==null,"framebuffer coordinates are not mistakenly scaled a second time");
var overlapping=initialKeys.SetItem(1,initialKeys[1] with{Box=initialKeys[0].Box});
var overlapTicket=state.Prepare(camera.Snapshot(),document,metrics,box,overlapping,true);state.Presented(overlapTicket,document,metrics,true);
Check(state.NativeKeyAt("replay_timeline_key_1",55,45,camera.Snapshot(),document,metrics,true)==20,"native focused marker id chooses actual hit target even when dense key DOM bounds overlap");
Check(state.NativeKeyAt("replay_timeline_key_1",100,45,camera.Snapshot(),document,metrics,true)==null,"focused key cannot start a drag from outside its successfully drawn actual bounds");
Paint();
Check(camera.SelectAll()&&state.Begin(20,WindowX(20),45,camera.Snapshot(),document,metrics,true),"drag freezes actual existing CameraTools multiselection");
Check(state.Move(WindowX(120),45,camera.Snapshot(),document,metrics,true)&&state.Preview.SequenceEqual(new uint[]{110,120,140})&&backend.Writes==0,"ephemeral group preview preserves exact offsets without sidecar writes");
Paint();Check(state.Dragging,"identical paused owner and DOM geometry survive a subsequent successful picture");
var proposal=state.Release(WindowX(120),45,camera.Snapshot(),document,metrics,true);
Check(proposal is{Destination:110}&&proposal.Selection.SequenceEqual(new uint[]{10,20,40})&&!state.Dragging&&backend.Writes==0,"release returns an exact reviewed destination rather than editing keys");
Check(camera.Destination(proposal!.Destination.ToString(CultureInfo.InvariantCulture))&&camera.Stage(TheatreCameraToolsChange.Move)&&backend.Writes==0,"proposal uses the existing groupMove staging authority and full before/after confirmation");
Check(camera.Snapshot().Pending!.After.Select(key=>key.Frame).SequenceEqual(proposal.PreviewFrames),"timeline proposal and actual retained groupMove produce exactly the same frames");camera.Cancel();
Check(camera.ClearSelection()&&camera.ToggleSelection(),"restore one selected existing key without seeking");
Check(state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true)&&state.Move(WindowX(20),45,camera.Snapshot(),document,metrics,true)&&!state.PreviewValid,"collision preview refuses overwriting an unselected occupied frame");
Check(state.Release(WindowX(20),45,camera.Snapshot(),document,metrics,true)==null&&backend.Writes==0,"invalid collision yields no confirmation proposal or writer call");
Check(state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true)&&state.Release(WindowX(10)+1,45,camera.Snapshot(),document,metrics,true)==null,"sub-threshold pointer travel remains selection only");
Check(state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true),"begin focus cancellation case");
Check(!state.Move(WindowX(50),45,camera.Snapshot(),document,metrics,false)&&!state.Dragging,"window or native surface focus loss cancels ephemeral drag");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);
Check(!state.Move(WindowX(50),45,camera.Snapshot(),new(2,9),metrics,true),"new modal/page document cannot inherit pointer authority");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);
Check(!state.Move(WindowX(50),45,camera.Snapshot(),document,metrics with{ClientWidth=800},true),"client scale change cancels drag before destination mapping");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);Paint(alternateBox:box with{X=101});
Check(!state.Dragging,"actual DOM lane movement cancels drag even when framebuffer size is unchanged");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);Paint(successful:false);
Check(!state.Dragging&&state.HitKey(WindowX(10),45,camera.Snapshot(),document,metrics,true)==null,"failed native presentation clears all unpublished geometry authority");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);
Check(!state.Move(double.NaN,45,camera.Snapshot(),document,metrics,true)&&!state.Dragging,"nonfinite pointer input cannot create a destination");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);
Check(!state.Move(WindowX(50),60,camera.Snapshot(),document,metrics,true),"leaving the actual camera lane cancels rather than scrubbing another lane");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);camera.SelectAll();
Check(!state.Move(WindowX(50),45,camera.Snapshot(),document,metrics,true),"selection changes during drag retire the frozen group proposal");
state.ZoomBy(1);Check(state.Zoom==2&&state.Window==new TimelineWindow(0,300)&&state.HitKey(55,45,camera.Snapshot(),document,metrics,true)==null,"zoom changes exact frame window and retires old marker geometry");
state.Pan(1);Check(state.Window==new TimelineWindow(50,350),"pan uses half the drawn span with deterministic edge clamping");
state.Center();Check(state.Window==new TimelineWindow(0,300),"center returns to reviewed playhead without seeking");
for(int i=0;i<10;i++)state.ZoomBy(1);Check(state.Zoom==16&&state.Window.Span==60,"retained zoom cap and minimum60-frame window are bounded");
state.Reset();Check(state.Window==new TimelineWindow(0,600),"reset restores exact full range without replacing transport");
Paint();state.Begin(10,WindowX(10),45,camera.Snapshot(),document,metrics,true);backend.Current=false;
Check(!state.Move(WindowX(50),45,camera.Snapshot(),document,metrics,true)&&!state.Dragging,"actual CameraTools stale owner latch retires timeline drag");
backend.Current=true;Check(!camera.Snapshot().Current,"timeline cannot silently renew controller authority after owner loss");
Check(camera.Refresh(),"explicit retained review required after source owner inspection");state.Review(camera.Snapshot());
var context=camera.Snapshot().Context;var editor=Editor(context);
var lanes=new TheatreTimelineLanes(context,editor);
Check(lanes.Project(state.Window,TimelineLane.Events).Length<=64&&lanes.Project(state.Window,TimelineLane.Events).Sum(row=>row.Count)==2000,"readonly event density bins preserve all2000 exact reviewed entries in at most64 glyphs");
Check(lanes.Project(state.Window,TimelineLane.Events).Any(row=>row.Count>1&&row.Exact==null),"aggregated markers cannot masquerade as one actionable source entry");
Check(lanes.Project(state.Window,TimelineLane.Bookmarks) is{Length:1} bookmarks&&bookmarks[0].Exact!.Id=="bookmark:one","bookmark lane uses the existing exact Editor entry identity");
Check(lanes.Project(new(100,300),TimelineLane.Ranges).Single() is{First:100,Last:250,Count:1},"range lane clips drawn extent to exact visible window while preserving source identity");
Check(lanes.Project(state.Window,TimelineLane.Reel).Single().Exact!.Id=="reel:one","reel lane borrows existing reviewed segment owner");
Check(Reject(()=>new TheatreTimelineLanes(context,editor with{Owner=new object()}))&&Reject(()=>new TheatreTimelineLanes(context,editor with{SourceRevision="changed"}))
    &&Reject(()=>new TheatreTimelineLanes(context,editor with{SeekGeneration=editor.SeekGeneration+1})),"lanes cannot combine different owner/source/same-frame seek snapshots");
Check(backend.Writes==0&&backend.Previews==0,"all timeline selection, zoom, preview, cancellation and projections create no recording writes, replay clock or seek");
Check(Task.Run(()=>Reject(()=>state.Reset())).GetAwaiter().GetResult(),"timeline state is confined to actual presenter owner thread");
// Same retained reviewed context and successful-picture ticket, with the camera
// lane scrolled away while an exact-frame or singleton lane control is visible.
using(var semanticCamera=new TheatreCameraToolsController(new Memory()))
{
    var semantic=new TheatreTimelineViewState();var snapshot=semanticCamera.Snapshot();semantic.Review(snapshot);
    var hiddenCamera=new TimelineBox(100,-200,600,24);var clip=new TimelineBox(20,20,1240,680);var seek=new TimelineBox(300,600,160,60);
    var keys=snapshot.Context.Camera.Keys.Select(key=>new TimelineKeyBox(key.Frame,new(semantic.Window.X(key.Frame,hiddenCamera)-4,-198,8,20))).ToImmutableArray();
    var glyph=new TheatreTimelineLanes(snapshot.Context,Editor(snapshot.Context)).Project(semantic.Window,TimelineLane.Bookmarks).Single();
    var marker=new TimelineLaneButton("replay_timeline_bookmarks_0",new(50,300,20,24),glyph);
    TimelineRenderTicket? Prepare(TimelineBox? control=null,ImmutableArray<TimelineKeyBox>? alternateKeys=null)=>semantic.Prepare(snapshot,document,metrics,hiddenCamera,
        alternateKeys??keys,true,ImmutableArray.Create(marker),clip,control??seek);
    void Published(){var ticket=Prepare();Check(semantic.Presented(ticket,document,metrics,true),"scrolled semantic ticket publishes only after exact successful native frame");}
    var prepared=Prepare();
    Check(prepared!=null&&!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek,clip),"preparing visible Go with offscreen camera does not grant unpresented semantic authority");
    Check(!semantic.Presented(prepared,document,metrics,false)&&!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek,clip),"failed native swap leaves visible Go unauthorized");
    Published();
    Check(semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek,clip),"visible exact Go works after successful swap while camera is offscreen under same reviewed owner");
    Check(semantic.Activate("replay_timeline_key_0",snapshot,document,metrics,true)==null,"offscreen camera key cannot be semantically activated");
    Check(semantic.Activate(marker.Id,snapshot,document,metrics,true)?.Lane==glyph,"only published fully visible singleton lane remains semantically actionable");
    Check(semantic.ActivationBoundsCurrent(marker.Id,marker.Box,clip)&&!semantic.ActivationBoundsCurrent(marker.Id,marker.Box with{Y=301},clip),"semantic marker compares current actual DOM bounds with successful-picture target");
    Check(semantic.HitKey(keys[0].Box.X/2,-98,snapshot,document,metrics,true)==null
        &&semantic.NativeKeyAt("replay_timeline_key_0",keys[0].Box.X/2,-98,snapshot,document,metrics,true)==null
        &&!semantic.Begin(10,keys[0].Box.X/2,-98,snapshot,document,metrics,true)
        &&!semantic.Move(100,100,snapshot,document,metrics,true)
        &&semantic.Release(100,100,snapshot,document,metrics,true)==null,"offscreen camera cannot enter any pointer hit/begin/move/release path");
    Check(!semantic.PointerBoundsCurrent(hiddenCamera,clip),"offscreen camera cannot inherit pointer geometry from semantic-only ticket");
    Check(!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek with{Y=590},clip),"Go moving between successful picture and invocation is stale");
    Check(!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek,clip with{Y=21}),"scroll clipping region changing before invocation is stale");
    foreach(var changed in new[]{metrics with{ClientWidth=800},metrics with{FramebufferWidth=1400},metrics with{Density=1}})
        Check(!semantic.SeekPresentedCurrent(snapshot,document,changed,true,seek,clip),"client/framebuffer/DPI change rejects semantic authority");
    Check(!semantic.SeekPresentedCurrent(snapshot,new(2,9),metrics,true,seek,clip)
        &&!semantic.SeekPresentedCurrent(snapshot,document,metrics,false,seek,clip),"foreign modal/document or lost focus rejects exact Go");
    Check(!semantic.SeekPresentedCurrent(snapshot with{Context=snapshot.Context with{Camera=snapshot.Context.Camera with{SeekGeneration=1}}},document,metrics,true,seek,clip),"changed camera/seek context cannot inherit semantic ticket");
    var absent=semantic.Prepare(snapshot,document,metrics,hiddenCamera,keys,true,ImmutableArray.Create(marker),clip,null);
    semantic.Presented(absent,document,metrics,true);
    Check(!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek,clip),"hidden or missing native Go is absent from successful ticket");
    var clipped=seek with{Y=690};var clippedTicket=Prepare(clipped);semantic.Presented(clippedTicket,document,metrics,true);
    Check(!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,clipped,clip),"Go partially inside scroll clipping border is not fully visible or actionable");
    Check(Prepare(alternateKeys:keys.RemoveAt(0))==null,"semantic-only ticket still requires the complete expected saved-key index mapping");
    Published();semantic.Invalidate("focus retired");
    Check(!semantic.SeekPresentedCurrent(snapshot,document,metrics,true,seek,clip),"focus/top/source retirement cannot revive old semantic Go ticket");
}
if(args.Length==2&&args[0]=="--dom-proof")
{
    using var proof=JsonDocument.Parse(File.ReadAllText(args[1]));Check(proof.RootElement.GetProperty("passed").GetBoolean(),"actual native DOM geometry proof must itself have passed");
    foreach(var actual in proof.RootElement.GetProperty("cases").EnumerateArray())
    {
        TimelineBox Bounds(JsonElement array)=>new(array[0].GetDouble(),array[1].GetDouble(),array[2].GetDouble(),array[3].GetDouble());
        using var actualCamera=new TheatreCameraToolsController(new Memory());var actualState=new TheatreTimelineViewState();var snapshot=actualCamera.Snapshot();actualState.Review(snapshot);
        var frame=actual.GetProperty("framebuffer");var client=actual.GetProperty("client");var actualMetrics=new TimelineMetrics(frame[0].GetInt32(),frame[1].GetInt32(),client[0].GetInt32(),client[1].GetInt32(),actual.GetProperty("density").GetSingle());
        var geometry=actual.GetProperty("afterGoFocus");var actualCameraBox=Bounds(geometry.GetProperty("camera"));var actualClip=Bounds(geometry.GetProperty("clip"));var actualSeek=Bounds(geometry.GetProperty("seek"));
        var actualKeys=snapshot.Context.Camera.Keys.Select(key=>new TimelineKeyBox(key.Frame,new(Math.Clamp(actualState.Window.X(key.Frame,actualCameraBox)-4,actualCameraBox.X,actualCameraBox.X+actualCameraBox.Width-8),actualCameraBox.Y+2,8,actualCameraBox.Height-4))).ToImmutableArray();
        var ticket=actualState.Prepare(snapshot,document,actualMetrics,actualCameraBox,actualKeys,true,actualDomClip:actualClip,actualDomSeek:actualSeek);
        Check(ticket!=null&&!actualState.SeekPresentedCurrent(snapshot,document,actualMetrics,true,actualSeek,actualClip),"actual native compact scrolled geometry does not authorize Go before successful presentation");
        Check(actualState.Presented(ticket,document,actualMetrics,true)&&actualState.SeekPresentedCurrent(snapshot,document,actualMetrics,true,actualSeek,actualClip),"actual native compact scrolled Go geometry authorizes same reviewed owner only after exact successful-picture publication");
        Check(actualState.Activate("replay_timeline_key_0",snapshot,document,actualMetrics,true)==null
            &&!actualState.PointerBoundsCurrent(actualCameraBox,actualClip),"actual native offscreen camera geometry cannot authorize marker activation or pointer editing");
        var hidden=actual.GetProperty("hiddenGo");Check(hidden.GetProperty("seek").ValueKind==JsonValueKind.Null,"actual native hidden Go does not expose visible DOM bounds");
        var hiddenTicket=actualState.Prepare(snapshot,document,actualMetrics,actualCameraBox,actualKeys,true,actualDomClip:actualClip);actualState.Presented(hiddenTicket,document,actualMetrics,true);
        Check(!actualState.SeekPresentedCurrent(snapshot,document,actualMetrics,true,actualSeek,actualClip),"actual native hidden-Go geometry cannot authorize retained seek control");
    }
}
if(args.Length==2&&args[0]=="--surface-proof")
{
    using var proof=JsonDocument.Parse(File.ReadAllText(args[1]));Check(proof.RootElement.GetProperty("passed").GetBoolean(),"actual native surface geometry proof must itself pass");
    foreach(var actual in proof.RootElement.GetProperty("cases").EnumerateArray())
    {
        TimelineBox Bounds(JsonElement array)=>new(array[0].GetDouble(),array[1].GetDouble(),array[2].GetDouble(),array[3].GetDouble());
        using var actualCamera=new TheatreCameraToolsController(new Memory());actualCamera.SelectAll();var snapshot=actualCamera.Snapshot();var actualState=new TheatreTimelineViewState();actualState.Review(snapshot);
        int density=actual.GetProperty("density").GetInt32();var actualMetrics=new TimelineMetrics(640*density,360*density,640,360,density);
        var before=Bounds(actual.GetProperty("beforeCanvasFocus").GetProperty("camera"));var after=Bounds(actual.GetProperty("afterCanvasFocus").GetProperty("camera"));
        var panel=Bounds(actual.GetProperty("beforeCanvasFocus").GetProperty("panel"));var actualClip=new TimelineBox(panel.X+density,panel.Y+density,panel.Width-2*density,panel.Height-2*density);
        ImmutableArray<TimelineKeyBox> Keys(TimelineBox area)=>snapshot.Context.Camera.Keys.Select(key=>new TimelineKeyBox(key.Frame,new(Math.Clamp(actualState.Window.X(key.Frame,area)-4,area.X,area.X+area.Width-8),area.Y+2,8,area.Height-4))).ToImmutableArray();
        var ticket=actualState.Prepare(snapshot,document,actualMetrics,before,Keys(before),true,actualDomClip:actualClip);
        Check(actualState.Presented(ticket,document,actualMetrics,true),"actual native pre-focus camera geometry publishes exact existing source ticket");
        double x=actualState.Window.X(20,before)/density,y=(before.Y+before.Height/2)/density;
        Check(actualState.Begin(20,x,y,snapshot,document,actualMetrics,true),"actual native pre-focus geometry supports retained selected-key pointer press");
        if(actual.GetProperty("variant").GetString()=="before")
        {
            Check(!actualState.PointerBoundsCurrent(after,actualClip),"actual old native canvas focus changes exact geometry and must remain rejected");
            var changed=actualState.Prepare(snapshot,document,actualMetrics,after,Keys(after),true,actualDomClip:actualClip);actualState.Presented(changed,document,actualMetrics,true);
            Check(!actualState.Dragging,"unchanged production geometry guard cancels preview after actual old native focus scroll");
        }
        else
        {
            Check(actualState.PointerBoundsCurrent(after,actualClip),"actual candidate native canvas focus preserves exact successful-picture pointer bounds");
            var unchanged=actualState.Prepare(snapshot,document,actualMetrics,after,Keys(after),true,actualDomClip:actualClip);actualState.Presented(unchanged,document,actualMetrics,true);
            Check(actualState.Dragging&&actualState.Move(actualState.Window.X(120,after)/density,y,snapshot,document,actualMetrics,true)
                &&actualState.Preview.SequenceEqual(new uint[]{110,120,140}),"existing retained group drag previews exact offsets using actual preserved native canvas geometry");
            actualState.Invalidate("focus released");Check(!actualState.Dragging&&actualState.Release(x,y,snapshot,document,actualMetrics,true)==null,"existing focus-release guard still cancels candidate drag without persistence or staging");
        }
    }
}
var namedEvents=editor with{Events=ImmutableArray.Create(
    new TheatreEditorEntry("event:kill",10,10,"Kill",""),new TheatreEditorEntry("event:damage",20,20,"Damage",""),
    new TheatreEditorEntry("event:shot",30,30,"WeaponFired",""),new TheatreEditorEntry("event:spawn",40,40,"PlayerSpawn",""),
    new TheatreEditorEntry("event:objective",50,50,"FlagCapture",""))};
var split=new TheatreTimelineLanes(context,namedEvents);
Check(split.Project(new(0,600),TimelineLane.Kills).Single().Exact!.Id=="event:kill"&&split.Project(new(0,600),TimelineLane.Shots).Single().Exact!.Id=="event:shot","split combat tracks retain exact recorded event identities");
Check(split.Project(new(0,600),TimelineLane.Audio).Sum(g=>g.Count)==3&&split.Project(new(0,600),TimelineLane.Objectives).Single().Exact!.Id=="event:objective","audio track projects recorded event sounds and objective track remains separate");
var rangeTrack=new TheatreTimelineLanes(context,namedEvents with{MarkIn=100,MarkOut=200}).Project(new(0,600),TimelineLane.ExportRange).Single();
Check(rangeTrack.First==100&&rangeTrack.Last==200&&rangeTrack.Exact==null,"export range track is a read-only projection and cannot impersonate an owned source event");
Console.WriteLine($"PASS {checks} private timeline DOM-ticket/density/groupMove/readonly-lane source checks; native geometry only when supplied, no world claims");

static TheatreEditorContext Editor(TheatreCameraToolsContext camera)
{
    var pose=camera.Camera;var events=Enumerable.Range(0,2000).Select(index=>new TheatreEditorEntry("event:"+index,(uint)(index%600),(uint)(index%600),"EVENT","SYNTHETIC"))
        .Append(new("bookmark:one",35,35,"BOOKMARK","SYNTHETIC")).ToImmutableArray();
    return new(pose.SceneGeneration,camera.Owner,pose.Transport,pose.LogicalPath,pose.PlaybackPath,camera.SourceRevision,camera.AnnotationRevision,"REEL",
        pose.Frame,pose.Duration,pose.SeekGeneration,20,200,true,ImmutableArray<TheatreEditorPlayer>.Empty,events,ImmutableArray<TheatreEditorEntry>.Empty,
        ImmutableArray.Create(new TheatreEditorEntry("range:one",50,250,"RANGE","SYNTHETIC")),ImmutableArray.Create(new TheatreEditorEntry("reel:one",50,250,"REEL","SYNTHETIC")),
        ImmutableArray<TheatreEditorEntry>.Empty,new object());
}
sealed class Memory:ITheatreCameraToolsBackend
{
    internal bool Current=true;internal int Writes,Previews;
    private static readonly TheatreCameraPose Pose=new(1,2,3,0,0,0,1,80,0,-1,0,0);
    internal TheatreCameraToolsContext Context=new(new(1,new object(),new object(),0,"SYNTHETIC","SYNTHETIC","RECORDING","CAMERA",50,600,true,Pose,
        ImmutableArray.Create(new TheatreCameraKey(10,Pose),new TheatreCameraKey(20,Pose),new TheatreCameraKey(40,Pose)),false){PresentationRevision=1},
        new object(),"SOURCE","ANNOTATION",ImmutableArray<TheatreCameraMarker>.Empty,ImmutableArray<int>.Empty,new(false,true,false),new object());
    public TheatreCameraToolsContext CaptureTools()=>Context;
    public bool ToolsCurrent(TheatreCameraToolsContext context)=>Current;
    public void ValidateReviewedTools(TheatreCameraToolsContext context){if(!Current)throw new TheatreCameraStaleException("Reviewed files changed.");}
    public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit reviewed){Writes++;throw new InvalidOperationException("Timeline must not persist any metadata.");}
    public void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey focused){Previews++;throw new InvalidOperationException("Timeline must not seek while dragging.");}
    public void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change)=>throw new InvalidOperationException("Timeline does not change runtime modes.");
}
