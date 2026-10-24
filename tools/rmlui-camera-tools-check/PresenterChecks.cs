using System.Text.RegularExpressions;
using System.Collections.Immutable;
using System.Xml.Linq;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
internal static class PresenterChecks {
 private static string Repository(){var directory=new DirectoryInfo(Environment.CurrentDirectory);while(directory!=null){if(Directory.Exists(Path.Combine(directory.FullName,"src/MphRead")))return directory.FullName;directory=directory.Parent;}throw new DirectoryNotFoundException("Run from the repository or a nested directory.");}
 internal static void Run(){int checks=0;void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
 var memory=new Memory();var host=new RmlUiHost();var pages=new RmlUiPageManager(host);using var page=new TheatrePlaybackPagePresenter(host,pages,memory);
 Check(page.Send(TheatrePlaybackAction.CameraToolsOpen)&&pages.Top!=page.Document,"native tools optional owner opens foreground RML modal");var panel=pages.Top;
 Check(!page.Repeat(),"duplicate native tools action sequence rejected");
 Check(page.Send(TheatrePlaybackAction.CameraToolsMove)&&pages.Top!=panel&&memory.Commits==0,"native group move stages exact fulltrack review before persistence");var confirmation=pages.Top;
 Check(!page.SendFrom(TheatrePlaybackAction.CameraToolsConfirm,panel),"background tools modal cannot confirm track mutation");
 memory.Fail=true;Check(page.Send(TheatrePlaybackAction.CameraToolsConfirm)&&pages.Top==confirmation&&memory.Commits==0,"writer error preserves native confirmation for exact retry");memory.Fail=false;
 Check(page.Send(TheatrePlaybackAction.CameraToolsConfirm)&&pages.Top==panel&&memory.Commits==1,"native confirmation saves exact original move once");
 Check(!page.SendFrom(TheatrePlaybackAction.CameraToolsConfirm,confirmation),"retired confirmation cannot repeat saved group edit");
 memory.Publish();Check(page.Send(TheatrePlaybackAction.CameraToolsRefresh)&&page.Send(TheatrePlaybackAction.CameraToolsSelectAll)&&page.Send(TheatrePlaybackAction.CameraToolsCopy),"fresh successful picture reauthorizes native multiselect copy");
 Check(page.Send(TheatrePlaybackAction.CameraToolsRemoveSelected)&&pages.Top!=panel,"native selected removal opens review");confirmation=pages.Top;memory.Current=false;page.RefreshFixture();
 Check(host.Flags[(confirmation,"disabled:replay_camera_tools_confirm_apply")],"changed owner disables exact selected removal confirmation");
 Check(page.Send(TheatrePlaybackAction.CameraToolsCancel)&&memory.Context.Camera.Keys.Length==3,"native cancellation retains all stored keys");memory.Current=true;memory.Publish();page.Send(TheatrePlaybackAction.CameraToolsRefresh);
 Check(page.Send(TheatrePlaybackAction.CameraToolsClose)&&pages.Top==page.Document,"native tools close only owns its modal and clipboard");
 Check(!page.SendFrom(TheatrePlaybackAction.CameraToolsPreview,panel),"retired tools modal cannot seek current or replacement playback");
 string dir=Path.Combine(Repository(),"src/MphRead/Mods/Launcher/RmlUi/Pages/theatre");var assets=new[]{XDocument.Load(Path.Combine(dir,"camera-tools.rml")),XDocument.Load(Path.Combine(dir,"camera-tools-confirm.rml"))};
 string nativeSource=File.ReadAllText(Path.Combine(Repository(),"native/rmlui-poc/projectprime_rmlui.cpp"));
 var nativeTable=Regex.Match(nativeSource,@"const char\* replayaction_names\[\]\s*=\s*\{(.*?)\};",RegexOptions.Singleline);
 Check(nativeTable.Success,"actual native replay action table is present");
 var nativeNames=Regex.Matches(nativeTable.Groups[1].Value,"\"([^\"]+)\"").Select(match=>match.Groups[1].Value).ToArray();
 string nativeHeader=File.ReadAllText(Path.Combine(Repository(),"native/rmlui-poc/projectprime_rmlui_api.h"));
 int nativeCount=int.Parse(Regex.Match(nativeHeader,@"PP_RMLUI_REPLAY_ACTION_COUNT\s*=\s*(\d+)").Groups[1].Value);
 Check(nativeNames.Length==nativeCount&&nativeCount==RmlUiIntentRegistry.ReplayActionCount,"native ABI count and full managed replay table have equal bounds");
 for(int index=0;index<nativeNames.Length;index++)
  Check(RmlUiIntentRegistry.TryParseLegacy("replay:"+nativeNames[index],panel,1,out var nativeIntent)
    &&nativeIntent.Kind==RmlUiIntentKind.ReplayAction&&nativeIntent.Argument==index,
    "actual native replay action matches managed name and stable index: "+index+" "+nativeNames[index]);
 var ids=assets.SelectMany(a=>a.Descendants()).Attributes("id").Select(a=>a.Value).ToHashSet();string source=File.ReadAllText(Path.Combine(dir,"TheatrePlaybackPagePresenter.CameraTools.cs"));
 foreach(Match match in Regex.Matches(source,"\"(replay_camera_tools_[a-z_]+)\""))Check(ids.Contains(match.Groups[1].Value),"authored tools RML binding: "+match.Groups[1].Value);
 foreach(string action in assets.SelectMany(a=>a.Descendants()).Attributes("data-action").Select(a=>a.Value))Check(RmlUiIntentRegistry.TryParseLegacy(action,panel,1,out var intent)&&intent.Kind==RmlUiIntentKind.ReplayAction&&(intent.Argument is >=97 and <=124||intent.Argument==(int)TheatrePlaybackAction.CameraToolsTransform||intent.Argument==(int)TheatrePlaybackAction.CameraToolsHandles||intent.Argument==(int)TheatrePlaybackAction.CameraToolsCurveKey||intent.Argument==(int)TheatrePlaybackAction.CameraToolsSave||intent.Argument==(int)TheatrePlaybackAction.CameraToolsUndo||intent.Argument==(int)TheatrePlaybackAction.CameraToolsRedo||intent.Argument==(int)TheatrePlaybackAction.CameraToolsDiscard||intent.Argument>=(int)TheatrePlaybackAction.CameraToolsOrientationCurve&&intent.Argument<=(int)TheatrePlaybackAction.CameraToolsRollEase),"bounded appended tools action: "+action);
 var baseDirectory=Path.Combine(Repository(),"src/MphRead/Mods/Launcher/RmlUi/Pages/theatre");
 string main=File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.cs"));
 Check(main.IndexOf("return HandleCameraToolsIntent(intent);",StringComparison.Ordinal)<main.IndexOf("return HandleEditorIntent(intent);",StringComparison.Ordinal),"tools action family routes before preserved editor prefix");
 Check(main.Contains("PresentCameraTools();")&&main.IndexOf("CloseCameraTools()",StringComparison.Ordinal)<main.IndexOf("CloseEditor()",StringComparison.Ordinal),"actual presenter presents and retires nested tools before underlying editor owners");
 string basic=File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.Camera.cs"));
 Check(basic.Contains("_cameraBackend is not ITheatreCameraToolsBackend")&&basic.Contains("state.Pending != null"),"basic camera tools entry requires optional backend and no pending basic confirmation");
 Check(File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.Bookmarks.cs")).IndexOf("BackCameraTools()",StringComparison.Ordinal)<File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.Bookmarks.cs")).IndexOf("BackEditor()",StringComparison.Ordinal),"Back dismisses exact nested tools review before parent family");
 using(var absent=new TheatrePlaybackPagePresenter(new RmlUiHost(),new RmlUiPageManager(new RmlUiHost()),new BasicOnly()))Check(!absent.Send(TheatrePlaybackAction.CameraToolsOpen),"backend without optional tools cannot open a phantom native editor");
 Check((int)TheatrePlaybackAction.EditorToggleShotRays==96&&(int)TheatrePlaybackAction.CameraToolsOpen==97&&(int)TheatrePlaybackAction.CameraToolsNearest==123,"all frozen Replay prefixes preserved and27 tool commands appended");
 foreach(string id in new[]{"orientation_curve","orientation_ease","fov_curve","fov_ease","roll_curve","roll_ease"})
  Check(assets[0].Descendants().Any(e=>(string?)e.Attribute("id")=="replay_camera_tools_"+id&&((string?)e.Attribute("class"))?.Split(' ').Contains("play_action")==true)
    &&XDocument.Load(Path.Combine(dir,"studio-workspace.rml")).Descendants().Any(e=>(string?)e.Attribute("id")=="replay_camera_tools_"+id&&((string?)e.Attribute("class"))?.Split(' ').Contains("play_action")==true),"independent channel uses the shared authored button contract in both editors: "+id);
 foreach(string name in new[]{"camera-tools.rml","studio-workspace.rml"})
 {
  var document=XDocument.Load(Path.Combine(dir,name));
  foreach(string command in new[]{"save","undo","redo","discard"})
  {
   var controls=document.Descendants().Where(element=>(string?)element.Attribute("id")=="replay_camera_tools_"+command).ToArray();
   Check(controls.Length==1&&controls[0].Name.LocalName=="button"
     &&(string?)controls[0].Attribute("data-action")=="replay:camera-tools-"+command,
     "unique native memory control ID maps to its exact action in "+name+": "+command);
  }
 }
 foreach(var (name,action) in new[]{("orientation-curve",TheatrePlaybackAction.CameraToolsOrientationCurve),("orientation-ease",TheatrePlaybackAction.CameraToolsOrientationEase),
   ("fov-curve",TheatrePlaybackAction.CameraToolsFovCurve),("fov-ease",TheatrePlaybackAction.CameraToolsFovEase),
   ("roll-curve",TheatrePlaybackAction.CameraToolsRollCurve),("roll-ease",TheatrePlaybackAction.CameraToolsRollEase)})
  Check(RmlUiIntentRegistry.TryParseLegacy("replay:camera-tools-"+name,panel,1,out var intent)&&intent.Argument==(int)action,
    "appended independent action keeps its exact bounded native argument: "+name);
 var liveMemory=new DocumentMemory();var liveHost=new RmlUiHost();var livePages=new RmlUiPageManager(liveHost);
 using(var livePage=new TheatrePlaybackPagePresenter(liveHost,livePages,liveMemory))
 {
  Check(livePage.Send(TheatrePlaybackAction.CameraToolsOpen),"memory camera editor opens its owning document");var livePanel=livePages.Top;
  foreach(var action in new[]{TheatrePlaybackAction.CameraToolsOrientationCurve,TheatrePlaybackAction.CameraToolsOrientationEase,
    TheatrePlaybackAction.CameraToolsFovCurve,TheatrePlaybackAction.CameraToolsFovEase,TheatrePlaybackAction.CameraToolsRollCurve,TheatrePlaybackAction.CameraToolsRollEase})
   Check(livePage.Send(action)&&livePages.Top==livePanel&&liveMemory.Commits==0,"native independent control edits memory without a confirmation: "+action);
  Check(liveMemory.Context.Camera.Keys[0].Pose.Channels==new TheatreCameraChannelCurves(1,1,1,1,1,1),"actual native controls reach the same six-channel document");
  Check(livePage.Send(TheatrePlaybackAction.CameraToolsUndo)&&livePage.Send(TheatrePlaybackAction.CameraToolsRedo),"native independent edit participates in document undo/redo");
 }
 var tracked=new Memory();tracked.Context=tracked.Context with{Camera=tracked.Context.Camera with{Keys=tracked.Context.Camera.Keys.Select(k=>k with{Pose=k.Pose with{LookAt=1,Channels=new(2,4,2,4,2,4)}}).ToImmutableArray()}};
 var trackedHost=new RmlUiHost();var trackedPages=new RmlUiPageManager(trackedHost);
 using(var trackedPage=new TheatrePlaybackPagePresenter(trackedHost,trackedPages,tracked))
  Check(trackedPage.Send(TheatrePlaybackAction.CameraToolsOpen)&&trackedHost.Text[(trackedPages.Top,"replay_camera_tools_focus")].Contains("LOOK-AT OVERRIDES AUTHORED ORIENTATION"),
    "selected key details disclose look-at override and abrupt target motion instead of promising orientation smoothness");
 Console.WriteLine($"PASS {checks} actual presenter/markup/approved action staging checks; host seam only");
 }
}

internal sealed class BasicOnly:ITheatreCameraBackend {
 public TheatreCameraContext Capture()=>throw new InvalidOperationException(); public bool IsCurrent(TheatreCameraContext context)=>false;
 public void SetFreeCamera(TheatreCameraContext context)=>throw new InvalidOperationException(); public void AdjustLens(TheatreCameraContext context,float fov,float roll)=>throw new InvalidOperationException();
 public void Preview(TheatreCameraContext context,TheatreCameraKey key)=>throw new InvalidOperationException();public void ToggleTrack(TheatreCameraContext context)=>throw new InvalidOperationException();public void Commit(TheatreCameraEdit edit)=>throw new InvalidOperationException();
}
