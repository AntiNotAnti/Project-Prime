using System.Text.RegularExpressions;
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
 var ids=assets.SelectMany(a=>a.Descendants()).Attributes("id").Select(a=>a.Value).ToHashSet();string source=File.ReadAllText(Path.Combine(dir,"TheatrePlaybackPagePresenter.CameraTools.cs"));
 foreach(Match match in Regex.Matches(source,"\"(replay_camera_tools_[a-z_]+)\""))Check(ids.Contains(match.Groups[1].Value),"authored tools RML binding: "+match.Groups[1].Value);
 foreach(string action in assets.SelectMany(a=>a.Descendants()).Attributes("data-action").Select(a=>a.Value))Check(RmlUiIntentRegistry.TryParseLegacy(action,panel,1,out var intent)&&intent.Kind==RmlUiIntentKind.ReplayAction&&(intent.Argument is >=97 and <=124||intent.Argument==(int)TheatrePlaybackAction.CameraToolsTransform||intent.Argument==(int)TheatrePlaybackAction.CameraToolsHandles||intent.Argument==(int)TheatrePlaybackAction.CameraToolsCurveKey),"bounded appended tools action: "+action);
 var baseDirectory=Path.Combine(Repository(),"src/MphRead/Mods/Launcher/RmlUi/Pages/theatre");
 string main=File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.cs"));
 Check(main.IndexOf("return HandleCameraToolsIntent(intent);",StringComparison.Ordinal)<main.IndexOf("return HandleEditorIntent(intent);",StringComparison.Ordinal),"tools action family routes before preserved editor prefix");
 Check(main.Contains("PresentCameraTools();")&&main.IndexOf("CloseCameraTools()",StringComparison.Ordinal)<main.IndexOf("CloseEditor()",StringComparison.Ordinal),"actual presenter presents and retires nested tools before underlying editor owners");
 string basic=File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.Camera.cs"));
 Check(basic.Contains("_cameraBackend is not ITheatreCameraToolsBackend")&&basic.Contains("state.Pending != null"),"basic camera tools entry requires optional backend and no pending basic confirmation");
 Check(File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.Bookmarks.cs")).IndexOf("BackCameraTools()",StringComparison.Ordinal)<File.ReadAllText(Path.Combine(baseDirectory,"TheatrePlaybackPagePresenter.Bookmarks.cs")).IndexOf("BackEditor()",StringComparison.Ordinal),"Back dismisses exact nested tools review before parent family");
 using(var absent=new TheatrePlaybackPagePresenter(new RmlUiHost(),new RmlUiPageManager(new RmlUiHost()),new BasicOnly()))Check(!absent.Send(TheatrePlaybackAction.CameraToolsOpen),"backend without optional tools cannot open a phantom native editor");
 Check((int)TheatrePlaybackAction.EditorToggleShotRays==96&&(int)TheatrePlaybackAction.CameraToolsOpen==97&&(int)TheatrePlaybackAction.CameraToolsNearest==123,"all frozen Replay prefixes preserved and27 tool commands appended");
 Console.WriteLine($"PASS {checks} actual presenter/markup/approved action staging checks; host seam only");
 }
}

internal sealed class BasicOnly:ITheatreCameraBackend {
 public TheatreCameraContext Capture()=>throw new InvalidOperationException(); public bool IsCurrent(TheatreCameraContext context)=>false;
 public void SetFreeCamera(TheatreCameraContext context)=>throw new InvalidOperationException(); public void AdjustLens(TheatreCameraContext context,float fov,float roll)=>throw new InvalidOperationException();
 public void Preview(TheatreCameraContext context,TheatreCameraKey key)=>throw new InvalidOperationException();public void ToggleTrack(TheatreCameraContext context)=>throw new InvalidOperationException();public void Commit(TheatreCameraEdit edit)=>throw new InvalidOperationException();
}
