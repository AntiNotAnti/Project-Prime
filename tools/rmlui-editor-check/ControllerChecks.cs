using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
internal static partial class ControllerChecks {
internal static void Run(){int checks=0;void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
var memory=new MemoryEditor();using var controller=new TheatreEditorController(memory);
Check(controller.Snapshot().Entries.Length==2,"initial full event browser");int captures=memory.Captures;controller.Snapshot();controller.Snapshot();Check(memory.Captures==captures,"readonly snapshot never captures files");
Check(controller.NextPlayer()&&controller.Snapshot().Entries.Length==1,"actor or target player filter");Check(controller.NextPlayer()&&controller.Snapshot().Entries.Length==1,"victim player sees relevant event");controller.NextPlayer();
for(int i=0;i<4;i++)controller.NextFilter();Check(controller.Snapshot().Entries.Length==1,"shot event filter");for(int i=0;i<4;i++)controller.NextFilter();
Check(controller.Move(-1)&&controller.Snapshot().SelectedIndex==1&&controller.Move(1)&&controller.Snapshot().SelectedIndex==0,"bounded previous/next wrap");
Check(controller.Seek(false)&&memory.Seeks==1&&controller.InspectShot(0)&&controller.ToggleHud(TheatreEditorHud.Combat),"browser delegates same retained owners");
controller.SetName("RETRY NAME");Check(controller.Stage(TheatreEditorEditKind.AddSelection)&&memory.Commits==0,"review stages immutable marked range");
Check(!controller.NextTab()&&!controller.Move(1),"pending confirmation fences navigation");memory.Fail=true;Check(!controller.Confirm()&&controller.Snapshot().Pending!=null&&controller.Snapshot().Name=="RETRY NAME","write error retains exact edit and entered name for retry");memory.Fail=false;
Check(controller.Confirm()&&memory.Context.Reel.Single().Name=="RETRY NAME","retry commits original immutable name and range");
controller.NextTab();Check(controller.Stage(TheatreEditorEditKind.AddHighlight)&&controller.Confirm()&&memory.Context.Reel.Length==2,"selected automatic highlight append");
Check(controller.Stage(TheatreEditorEditKind.AddTopHighlights)&&controller.Confirm()&&memory.Context.Reel.Length==3,"bounded top highlights append");
controller.NextTab();Check(controller.Stage(TheatreEditorEditKind.RemoveNamedHighlight),"named-highlight remove review");controller.Cancel();Check(memory.Context.NamedHighlights.Length==1,"cancel preserves named highlight");
Check(controller.Stage(TheatreEditorEditKind.RemoveNamedHighlight)&&controller.Confirm()&&memory.Context.NamedHighlights.IsEmpty,"exact named-highlight removal");controller.NextTab();
Check(controller.Stage(TheatreEditorEditKind.MoveDown)&&controller.Confirm(),"move selected reel entry later");controller.Move(1);Check(controller.Stage(TheatreEditorEditKind.MoveUp)&&controller.Confirm(),"move selected reel entry earlier");
Check(controller.Stage(TheatreEditorEditKind.Trim)&&controller.Confirm()&&controller.Stage(TheatreEditorEditKind.CycleCamera)&&controller.Confirm(),"trim and camera edits staged separately");
Check(controller.Stage(TheatreEditorEditKind.Remove)&&controller.Confirm()&&memory.Context.Reel.Length==2,"single exact reel removal");
Check(controller.Stage(TheatreEditorEditKind.ClearReel),"whole reel clear review");memory.Current=false;Check(!controller.Confirm()&&memory.Context.Reel.Length==2,"owner loss cannot clear saved reel");controller.Cancel();memory.Current=true;Check(!controller.Stage(TheatreEditorEditKind.ClearReel),"stale authority does not silently resume");Check(controller.Refresh()&&controller.Stage(TheatreEditorEditKind.ClearReel)&&controller.Confirm()&&memory.Context.Reel.IsEmpty,"explicit refresh reauthorizes clear");
memory.Rendering=true;Check(!controller.Seek(false)&&!controller.ToggleHud(TheatreEditorHud.Network)&&!controller.Stage(TheatreEditorEditKind.AddSelection),"export rendering blocks editor mutations");memory.Rendering=false;
Check(controller.SetName("PUBLISH ONCE")&&controller.Stage(TheatreEditorEditKind.AddSelection),"stage unverified-outcome proof");memory.Unverified=true;
int beforePublication=memory.Commits;
Check(controller.Confirm()&&controller.Snapshot().Pending==null&&!controller.Snapshot().Live.Current&&controller.Snapshot().Status.Contains("unverified")&&memory.Commits==beforePublication+1,"published unverified outcome retires pending command and authority with explicit recovery notice");
Check(!controller.Confirm()&&!controller.Stage(TheatreEditorEditKind.AddSelection)&&memory.Commits==beforePublication+1,"unverified publication cannot be blindly repeated or counted as rollback");memory.Unverified=false;
Check(controller.Refresh()&&controller.Snapshot().Live.Current,"explicit fresh review authorizes after inspected publication");
memory.Context=memory.Context with{SourceRevision="REPLACED"};Check(!controller.Refresh()&&!controller.Snapshot().Live.Current,"source replacement cannot renew old editor");
Check(Task.Run(()=>{try{controller.Snapshot();return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult(),"controller rejects wrong owner thread");controller.Dispose();Check(memory.Retired==1&&!controller.Refresh(),"dispose retires inspection without owning transport");
Console.WriteLine($"PASS {checks} native editor controller draft/retry/confirmation/lifetime checks");
RunPresenter();RunWorkspaceSeekReview();}
private static void RunPresenter(){int checks=0;void Check(bool value,string text){if(!value)throw new Exception(text);checks++;}
var memory=new MemoryEditor();var host=new RmlUiHost();var pages=new RmlUiPageManager(host);using var page=new TheatrePlaybackPagePresenter(host,pages,memory);
Check(page.Send(TheatrePlaybackAction.Studio)&&pages.Top!=page.Document,"old reserved Studio intent opens native workspace");var panel=pages.Top;
Check(!page.Repeat(),"native sequence duplicate rejected");Check(page.Send(TheatrePlaybackAction.EditorAddSelectionToReel)&&pages.Top!=panel&&memory.Commits==0,"native stage opens exact edit modal");var confirmation=pages.Top;
Check(!page.SendFrom(TheatrePlaybackAction.EditorConfirm,panel),"background page cannot confirm saved metadata");memory.Current=false;page.RefreshFixture();Check(host.Flags[(confirmation,"disabled:replay_editor_confirm_apply")],"retired owner disables native confirmation");
Check(page.Send(TheatrePlaybackAction.EditorConfirm)&&memory.Commits==0,"stale confirmation keeps original bytes");Check(page.Send(TheatrePlaybackAction.EditorCancel)&&pages.Top==panel,"cancel closes exact confirmation first");memory.Current=true;
Check(page.Send(TheatrePlaybackAction.EditorRefresh)&&page.Send(TheatrePlaybackAction.EditorAddSelectionToReel)&&page.Send(TheatrePlaybackAction.EditorConfirm)&&memory.Commits==1,"fresh native review commits once");
Check(!page.SendFrom(TheatrePlaybackAction.EditorConfirm,confirmation),"retired confirmation token cannot replay edit");
foreach(var action in new[]{TheatrePlaybackAction.EditorTab,TheatrePlaybackAction.EditorPlayer,TheatrePlaybackAction.EditorEventFilter,TheatrePlaybackAction.EditorPrevious,TheatrePlaybackAction.EditorNext,TheatrePlaybackAction.EditorPreviousShot,TheatrePlaybackAction.EditorShotAtPlayhead,TheatrePlaybackAction.EditorNextShot,TheatrePlaybackAction.EditorToggleAnalyticsHud,TheatrePlaybackAction.EditorToggleNetworkHud,TheatrePlaybackAction.EditorToggleCombatHud,TheatrePlaybackAction.EditorToggleShotRays})Check(page.Send(action),"actual presenter action "+action);
Check(page.Send(TheatrePlaybackAction.EditorClose)&&pages.Top==page.Document&&memory.Retired==1,"native close releases only inspection owner");Check(!page.SendFrom(TheatrePlaybackAction.EditorSeek,panel),"retired editor document ignored");
string sourceRoot=AppContext.BaseDirectory;
while(!Directory.Exists(Path.Combine(sourceRoot,"src/MphRead")))sourceRoot=Directory.GetParent(sourceRoot)?.FullName??throw new IOException("Repository sources are unavailable.");
string folder=Path.Combine(sourceRoot,"src/MphRead/Mods/Launcher/RmlUi/Pages/theatre");var assets=new[]{XDocument.Load(Path.Combine(folder,"editor.rml")),XDocument.Load(Path.Combine(folder,"editor-confirm.rml"))};
var ids=assets.SelectMany(a=>a.Descendants()).Attributes("id").Select(a=>a.Value).ToHashSet();string source=File.ReadAllText(Path.Combine(folder,"TheatrePlaybackPagePresenter.Editor.cs"));
foreach(Match match in Regex.Matches(source,"\"(replay_editor_[a-z_]+)\""))Check(ids.Contains(match.Groups[1].Value),"authored native binding "+match.Groups[1].Value);
foreach(string action in assets.SelectMany(a=>a.Descendants()).Attributes("data-action").Select(a=>a.Value))
Check(RmlUiIntentRegistry.TryParseLegacy(action,panel,1,out var intent)&&intent.Kind==RmlUiIntentKind.ReplayAction
    &&(intent.Argument is >=68 and <=96
        ||action=="replay:spatial-open"&&intent.Argument==(int)TheatrePlaybackAction.SpatialOpen
        ||action=="replay:studio-tools-open"&&intent.Argument==(int)TheatrePlaybackAction.StudioToolsOpen)
    &&RmlUiIntentRegistry.ToLegacy(intent)==action,"bounded actual authored editor or explicit optional-tool entry action "+action);
Check((int)TheatrePlaybackAction.ExportOpenOutput==67&&(int)TheatrePlaybackAction.EditorOpen==68&&(int)TheatrePlaybackAction.EditorToggleShotRays==96,"old Replay values preserved; exact29 appended commands");
Console.WriteLine($"PASS {checks} actual source-linked presenter/RML/registry checks with native host seam");}
internal sealed class MemoryEditor:ITheatreEditorBackend {
internal bool Current=true,Rendering,Fail,Unverified;internal int Captures,Commits,Retired,Seeks;
internal TheatreEditorContext Context=new(1,new object(),new object(),"SYNTHETIC.ppdemo","SYNTHETIC.ppdemo","SOURCE","ANNOTATION","REEL",10,600,0,10,80,true,
ImmutableArray.Create(new TheatreEditorPlayer(0,"Hunter"),new TheatreEditorPlayer(1,"Target")),
ImmutableArray.Create(new TheatreEditorEntry("event:0",10,10,"SHOT","",0,10,Target:1),new TheatreEditorEntry("event:1",200,200,"JOIN","",2,5)),
ImmutableArray.Create(new TheatreEditorEntry("highlight:0",0,100,"KILL","",0,FocusFrame:30)),
ImmutableArray.Create(new TheatreEditorEntry(Guid.NewGuid().ToString("N"),0,20,"NAMED","")),ImmutableArray<TheatreEditorEntry>.Empty,
ImmutableArray.Create(new TheatreEditorEntry("analytics",0,0,"TOTAL","")),new object());
public TheatreEditorContext Capture(){Captures++;return Context with{PublicationVerified=true,PublicationNotice=""};}
public TheatreEditorLive ReadLive(TheatreEditorContext context)=>new(Current,Rendering,"SYNTHETIC",false,false,false,false);
public void Seek(TheatreEditorContext context,TheatreEditorEntry exact,bool range)=>Seeks++;
public void InspectShot(TheatreEditorContext context,TheatreEditorEntry? previous,int direction,int player){}
public void ToggleHud(TheatreEditorContext context,TheatreEditorHud hud){}
public TheatreEditorContext Commit(TheatreEditorEdit reviewed){if(Fail)throw new IOException("SYNTHETIC SHARING VIOLATION");Commits++;Context=reviewed.Kind==TheatreEditorEditKind.RemoveNamedHighlight?Context with{NamedHighlights=reviewed.After}:Context with{Reel=reviewed.After};return Context with{PublicationVerified=!Unverified,PublicationNotice=Unverified?"Metadata published but unverified; recovery preserved. Refresh.":""};}
public void Retire()=>Retired++;
}
}
