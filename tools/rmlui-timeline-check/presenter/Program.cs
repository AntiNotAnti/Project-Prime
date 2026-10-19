using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

static string Repository(){var path=new DirectoryInfo(Environment.CurrentDirectory);while(path!=null){if(Directory.Exists(Path.Combine(path.FullName,"src/MphRead")))return path.FullName;path=path.Parent;}throw new DirectoryNotFoundException();}
int checks=0;void Check(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;}
var backend=new Memory();var host=new RmlUiHost();var pages=new RmlUiPageManager(host);using var page=new TheatrePlaybackPagePresenter(host,pages,backend);
var metrics=new TimelineMetrics(1280,720,1280,720,1);
Check(page.Send(TheatrePlaybackAction.CameraToolsOpen),"same existing CameraTools optional backend opens first");var tools=pages.Top;
Check(page.Send(TheatrePlaybackAction.TimelineOpen)&&pages.Top!=tools,"timeline borrows exact existing controller under a native modal");var timeline=pages.Top;
page.SetFocus("replay_timeline_key_1");Check(page.Send(TheatrePlaybackAction.TimelineActivate)&&page.CameraState.Focus?.Frame==10,"activation before native presentation cannot resolve fabricated marker slots");
host.SetField(timeline,"replay_timeline_frame","123");page.SetFocus("replay_timeline_seek");page.Send(TheatrePlaybackAction.TimelineActivate);
Check(backend.Seeks==0,"exact-frame keyboard action also requires a successfully presented native ticket");
Check(page.Paint(metrics),"actual presenter publishes exact prepared native bounds through injected host seam");
page.SetFocus("replay_timeline_key_1");Check(page.Send(TheatrePlaybackAction.TimelineActivate)&&page.CameraState.Focus?.Frame==20,"keyboard/controller/accessibility shared action resolves exact focused authored camera slot");
Check(page.Send(TheatrePlaybackAction.CameraToolsToggleSelection)&&page.CameraState.Selection.SequenceEqual(new uint[]{10,20}),"native timeline reuses CameraTools multiselect authority");
page.SetFocus("replay_timeline_key_2");page.Send(TheatrePlaybackAction.TimelineActivate);page.Send(TheatrePlaybackAction.CameraToolsRangeSelection);
Check(page.CameraState.Selection.SequenceEqual(new uint[]{10,20,40}),"focused key plus existing anchor-range command forms exact group without seeking");
Check(page.Paint(metrics),"selection redraw retains unchanged source pose and exact native layout");
page.SetFocus("replay_timeline_key_1");page.TimelinePointerButton(0,40,110,true,metrics,true);page.TimelinePointerMove(140,110,metrics,true);
Check(backend.Writes==0&&page.CameraState.Pending==null,"actual presenter pointer movement only paints ephemeral group ghosts");
page.TimelinePointerButton(0,140,110,false,metrics,true);var confirmation=pages.Top;
Check(confirmation!=timeline&&page.CameraState.Pending!.After.Select(row=>row.Frame).SequenceEqual(new uint[]{110,120,140})&&backend.Writes==0,
    "actual pointer release stages SAME original full-track groupMove confirmation");
Check(!page.SendFrom(TheatrePlaybackAction.TimelineActivate,timeline),"background timeline cannot activate a marker under exact save confirmation");
backend.Fail=true;Check(page.Send(TheatrePlaybackAction.CameraToolsConfirm)&&pages.Top==confirmation&&page.CameraState.Pending!=null,"original camera writer failure preserves same group confirmation for retry");
backend.Fail=false;Check(page.Send(TheatrePlaybackAction.CameraToolsConfirm)&&pages.Top==timeline&&backend.Writes==1,"existing confirmation retries original reviewed group through same backend once");
Check(page.Send(TheatrePlaybackAction.TimelineRefresh)&&page.Paint(metrics),"fresh explicit review restores timeline after saved group");
page.SetFocus("replay_timeline_key_999");Check(page.Send(TheatrePlaybackAction.TimelineActivate)&&page.CameraState.Focus?.Frame==110,"unbounded/missing authored marker id cannot choose an arbitrary frame");
page.SetFocus("replay_timeline_key_00");page.Send(TheatrePlaybackAction.TimelineActivate);Check(page.CameraState.Focus?.Frame==110,"noncanonical slot aliases cannot resolve a native key");
page.Paint(metrics);host.SetField(timeline,"replay_timeline_frame","1.5");page.SetFocus("replay_timeline_seek");page.Send(TheatrePlaybackAction.TimelineActivate);
Check(backend.Seeks==0,"exact-frame control rejects decimal input without seeking current or replacement transport");
page.Paint(metrics);host.SetField(timeline,"replay_timeline_frame","123");page.Send(TheatrePlaybackAction.TimelineActivate);
Check(backend.Seeks==1&&backend.Context.Camera.Frame==123&&!page.CameraState.Current,"exact native frame control borrows same Editor transport and retires old review");
Check(page.Send(TheatrePlaybackAction.TimelineRefresh)&&page.Paint(metrics),"explicit settled seek review reunites existing camera and lane snapshots");
page.SetFocus("replay_timeline_bookmarks_0");page.Send(TheatrePlaybackAction.TimelineActivate);
Check(backend.Seeks==2&&backend.LastEntry?.Id=="bookmark:one","singleton native lane activation uses existing exact Editor entry owner");
page.Send(TheatrePlaybackAction.TimelineRefresh);page.Paint(metrics);
page.SetFocus("replay_timeline_events_0");page.Send(TheatrePlaybackAction.TimelineActivate);
Check(backend.Seeks==2,"aggregated native event bin zooms/inspects counts without pretending to seek a single event");
Check(!page.Paint(metrics)||page.CameraState.Current,"zoomed projection remains owned by same paused replay context");
page.Send(TheatrePlaybackAction.TimelineReset);Check(page.Paint(metrics),"reset requires newly presented bounds before marker activation");
page.SetFocus("replay_timeline_key_0");backend.FilesValid=false;
page.TimelinePointerButton(0,130,110,true,metrics,true);page.TimelinePointerMove(230,110,metrics,true);
Check(page.CameraState.Pending==null&&!page.CameraState.Current&&backend.Writes==1,"strong source validation at actual drag boundary retires stale file authority before staging");
backend.FilesValid=true;page.Send(TheatrePlaybackAction.TimelineRefresh);page.Paint(metrics);
page.SetFocus("replay_timeline_key_0");page.TimelinePointerButton(0,130,110,true,metrics,true);page.TimelinePointerMove(230,110,metrics,true);
page.ReleaseTimelineInput();page.TimelinePointerButton(0,230,110,false,metrics,true);
Check(page.CameraState.Pending==null&&backend.Writes==1,"focus release cancels preview and cannot stage on later pointer-up");
Check(page.BackFixture()&&pages.Top==tools&&!host.IsAlive(timeline),"Back closes only borrowed timeline, preserving parent CameraTools controller");
Check(page.Send(TheatrePlaybackAction.TimelineOpen)&&page.Paint(metrics),"same original controller may reopen timeline after explicit refresh");timeline=pages.Top;
page.SetFocus("replay_timeline_key_0");backend.Context=backend.Context with{Owner=new object()};page.RefreshFixture();page.Send(TheatrePlaybackAction.TimelineActivate);
Check(backend.Writes==1&&!page.CameraState.Current,"same-path playback replacement cannot inherit old drawn key or file authority");
Check(page.Send(TheatrePlaybackAction.TimelineClose)&&pages.Top==tools,"stale timeline retains an accessible close route");
{
    var scrollBackend=new Memory();var scrollHost=new RmlUiHost();var scrollPages=new RmlUiPageManager(scrollHost);
    using var scrollPage=new TheatrePlaybackPagePresenter(scrollHost,scrollPages,scrollBackend);
    scrollPage.Send(TheatrePlaybackAction.CameraToolsOpen);scrollPage.Send(TheatrePlaybackAction.TimelineOpen);var scrollDoc=scrollPages.Top;
    scrollHost.ScrollY=120;scrollHost.SetField(scrollDoc,"replay_timeline_frame","123");scrollPage.SetFocus("replay_timeline_seek");
    scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==0,"actual presenter does not seek scrolled Go before a successful picture");
    Check(scrollPage.Paint(metrics),"actual presenter retains successful semantic-only ticket when camera scrolls above viewport");
    scrollPage.TimelinePointerMove(40,290,metrics,true);scrollPage.TimelinePointerButton(0,40,290,true,metrics,true);scrollPage.TimelinePointerButton(0,40,290,false,metrics,true);
    scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==1&&scrollBackend.Context.Camera.Frame==123,"ordinary native hover/click preserves visible semantic-only Go ticket and delegates exact frame once");
    scrollPage.Send(TheatrePlaybackAction.TimelineRefresh);scrollPage.Paint(metrics);
    scrollPage.SetFocus("replay_timeline_key_1");scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollPage.CameraState.Focus?.Frame==10,"actual presenter refuses offscreen semantic camera key despite valid shared ticket");
    scrollPage.Paint(metrics);scrollPage.SetFocus("replay_timeline_bookmarks_0");scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==2&&scrollBackend.LastEntry?.Id=="bookmark:one","actual presenter allows only currently visible published singleton lane under same offscreen camera");
    scrollPage.Send(TheatrePlaybackAction.TimelineRefresh);scrollPage.Paint(metrics);
    scrollPage.SetFocus("replay_timeline_seek");scrollHost.Hidden.Add("replay_timeline_seek");scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==2,"native Go disappearing after picture cannot use stored bounds");
    scrollPage.Paint(metrics);scrollHost.Hidden.Remove("replay_timeline_seek");scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==2,"Go becoming visible cannot inherit a ticket in which it was hidden");
    scrollPage.Paint(metrics);scrollHost.ScrollY=121;scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==2,"actual DOM scroll between swap and input rejects stale Go box before seeking");
    scrollPage.Paint(metrics);scrollPage.Send(TheatrePlaybackAction.TimelineActivate);
    Check(scrollBackend.Seeks==3,"new successfully presented visible Go reauthorizes same reviewed context after scroll");
    scrollPage.Send(TheatrePlaybackAction.TimelineRefresh);scrollPage.Paint(metrics);scrollPage.SetFocus("replay_timeline_key_0");
    scrollPage.TimelinePointerButton(0,30,-17,true,metrics,true);scrollPage.TimelinePointerMove(130,-17,metrics,true);scrollPage.TimelinePointerButton(0,130,-17,false,metrics,true);
    Check(scrollPage.CameraState.Pending==null&&scrollBackend.Writes==0,"actual presenter cannot stage pointer groupMove from semantic-only offscreen camera ticket");
}
string directory=Path.Combine(Repository(),"src/MphRead/Mods/Launcher/RmlUi/Pages/theatre");var markup=XDocument.Load(args.Length==2&&args[0]=="--timeline"?args[1]:Path.Combine(directory,"timeline.rml"));
Check(markup.Descendants().Single(element=>element.Attribute("id")?.Value=="replay_timeline_surface").Attribute("data-focus-scroll")?.Value=="none","authored pointer canvas preserves captured geometry when native focus changes; ordinary controls keep default nearest scrolling");
Check(markup.Descendants("button").Count(e=>e.Attribute("data-action")?.Value=="replay:timeline-activate")==833,"all64camera+768lane slots plus exact-frame control use real native button activation");
foreach(string action in markup.Descendants().Attributes("data-action").Select(a=>a.Value).Distinct())
    Check(RmlUiIntentRegistry.TryParseLegacy(action,timeline,1,out var intent)&&intent.Kind==RmlUiIntentKind.ReplayAction&&intent.Argument<135,"bounded authored timeline control action "+action);
var ids=markup.Descendants().Attributes("id").Select(a=>a.Value).ToHashSet();string source=File.ReadAllText(Path.Combine(directory,"TheatrePlaybackPagePresenter.Timeline.cs"));
foreach(Match id in Regex.Matches(source,"\"(replay_timeline_[a-z_]+)\""))if(!id.Groups[1].Value.EndsWith('_'))Check(ids.Contains(id.Groups[1].Value),"authored actual presenter target "+id.Groups[1].Value);
Check((int)TheatrePlaybackAction.CameraToolsNearest==123&&(int)TheatrePlaybackAction.TimelineOpen==124&&(int)TheatrePlaybackAction.TimelineActivate==134,"all124existing actions preserved plus exact11finite timeline names");
CurvePresenterChecks.Run();
RootHookChecks.Run();
WorkspaceMarkupChecks.Run(directory);
Console.WriteLine($"PASS {checks} private actual timeline presenter/activation/pointer/markup/retained-confirmation checks; native host seam only");

sealed class Memory:ITheatreCameraBackend,ITheatreCameraToolsBackend,ITheatreEditorBackend,ITheatreTimelineSeek
{
    private static readonly TheatreCameraPose Pose=new(1,2,3,0,0,0,1,80,0,-1,0,0);
    internal TheatreCameraToolsContext Context=new(new(1,new object(),new object(),0,"SYNTHETIC","SYNTHETIC","RECORDING","CAMERA",50,600,true,Pose,
        ImmutableArray.Create(new TheatreCameraKey(10,Pose),new TheatreCameraKey(20,Pose),new TheatreCameraKey(40,Pose)),false){PresentationRevision=1},
        new object(),"SOURCE","ANNOTATION",ImmutableArray<TheatreCameraMarker>.Empty,ImmutableArray<int>.Empty,new(false,true,false),new object());
    internal int Writes,Seeks;internal bool Fail,FilesValid=true;internal TheatreEditorEntry? LastEntry;
    public TheatreCameraContext Capture()=>Context.Camera;
    public bool IsCurrent(TheatreCameraContext context)=>context==Context.Camera;
    public void SetFreeCamera(TheatreCameraContext context)=>throw new InvalidOperationException();public void AdjustLens(TheatreCameraContext context,float fov,float roll)=>throw new InvalidOperationException();
    public void Preview(TheatreCameraContext context,TheatreCameraKey key)=>throw new InvalidOperationException();public void ToggleTrack(TheatreCameraContext context)=>throw new InvalidOperationException();public void Commit(TheatreCameraEdit edit)=>throw new InvalidOperationException();
    public TheatreCameraToolsContext CaptureTools()=>Context;
    public bool ToolsCurrent(TheatreCameraToolsContext context)=>ReferenceEquals(context.Owner,Context.Owner)&&context.Camera.Frame==Context.Camera.Frame
        &&context.Camera.SeekGeneration==Context.Camera.SeekGeneration&&context.Camera.Pose==Context.Camera.Pose&&context.Camera.Keys.SequenceEqual(Context.Camera.Keys);
    public void ValidateReviewedTools(TheatreCameraToolsContext context){if(!ToolsCurrent(context)||!FilesValid)throw new TheatreCameraStaleException("Source or exact camera sidecar changed.");}
    public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit edit)
    {ValidateReviewedTools(edit.Context);if(Fail)throw new IOException("Synthetic sharing error");Writes++;Context=Context with{Camera=Context.Camera with{Keys=edit.After,PresentationRevision=Context.Camera.PresentationRevision+1}};return new(true,"");}
    public void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey focused)=>throw new InvalidOperationException();public void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change)=>throw new InvalidOperationException();
    TheatreEditorContext ITheatreEditorBackend.Capture()
    {
        var camera=Context.Camera;
        var events=Enumerable.Range(0,2000).Select(index=>new TheatreEditorEntry("event:"+index,(uint)(index%600),(uint)(index%600),"EVENT","SYNTHETIC"))
            .Append(new TheatreEditorEntry("bookmark:one",35,35,"BOOKMARK","SYNTHETIC")).ToImmutableArray();
        return new(camera.SceneGeneration,Context.Owner,camera.Transport,camera.LogicalPath,camera.PlaybackPath,Context.SourceRevision,Context.AnnotationRevision,"REEL",camera.Frame,camera.Duration,camera.SeekGeneration,20,200,true,
            ImmutableArray<TheatreEditorPlayer>.Empty,events,ImmutableArray<TheatreEditorEntry>.Empty,ImmutableArray.Create(new TheatreEditorEntry("range:one",50,250,"RANGE","SYNTHETIC")),
            ImmutableArray.Create(new TheatreEditorEntry("reel:one",100,200,"REEL","SYNTHETIC")),ImmutableArray<TheatreEditorEntry>.Empty,new object());
    }
    public TheatreEditorLive ReadLive(TheatreEditorContext context)=>new(true,false,"",false,false,false,false);
    public void Seek(TheatreEditorContext context,TheatreEditorEntry exact,bool selectRange){LastEntry=exact;SeekTimelineFrame(context,exact.Frame);}
    public void SeekTimelineFrame(TheatreEditorContext context,uint frame)
    {if(context.Owner!=Context.Owner||context.Transport!=Context.Camera.Transport||frame>context.Duration)throw new TheatreEditorStaleException("Original seek owner changed.");Seeks++;Context=Context with{Camera=Context.Camera with{Frame=frame,SeekGeneration=Context.Camera.SeekGeneration+1,PresentationRevision=Context.Camera.PresentationRevision+1}};}
    public void InspectShot(TheatreEditorContext context,TheatreEditorEntry? previous,int direction,int player)=>throw new InvalidOperationException();public void ToggleHud(TheatreEditorContext context,TheatreEditorHud hud)=>throw new InvalidOperationException();
    public TheatreEditorContext Commit(TheatreEditorEdit reviewed)=>throw new InvalidOperationException();public void Retire(){}
}
