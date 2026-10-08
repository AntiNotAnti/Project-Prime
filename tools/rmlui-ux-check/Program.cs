using System.Runtime.InteropServices;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Presenters;
using MphRead.Mods.Launcher.RmlUi.Pages.Offline;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Launcher.RmlUi.Settings;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Network;
using SkiaSharp;

if(args.Length is <3 or >4)throw new ArgumentException("Supply bridge, asset root, preview directory, and optionally an extracted-game paths file.");
if(args.Length==4){Paths.UpdatePaths(Path.GetFullPath(args[3]));Paths.ChooseMphPath();}
string fixture=Directory.CreateTempSubdirectory("prime-ux-fixture-").FullName;
Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",fixture);
Environment.SetEnvironmentVariable("PROJECT_PRIME_UI_PERF",Path.Combine(fixture,"unused.json"));
string previous=Directory.GetCurrentDirectory(),assets=Path.GetFullPath(args[1]),output=Path.GetFullPath(args[2]);
nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
if(OperatingSystem.IsMacOS())NativeLibrary.SetDllImportResolver(typeof(SKBitmap).Assembly,(name,_,_)=>name=="libSkiaSharp"?NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory,"runtimes/osx/native/libSkiaSharp.dylib")):0);
Directory.CreateDirectory(output);Directory.SetCurrentDirectory(fixture);
LauncherPrefs.Directory=GameFiles.Root=fixture;Paths.SetPath("Export",Path.Combine(fixture,"export"));LauncherPrefs.DebugLogs=false;LauncherPrefs.ReplayAutoPrune=false;
HudProfiles.Load(Path.Combine(fixture,"Savedata","hud-profiles"));
int checks=0;
void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);checks++;}
try
{
 foreach(var size in new[]{(1280,720,1f),(2560,1440,2f),(960,600,1f)})
 {
  using var host=new RmlUiHost();Check(host.Initialize(size.Item1,size.Item2,size.Item3,assets,RmlUiRenderBackend.DrawList),"initialize");
  using var pages=new RmlUiLauncherPages(host);
  pages.SetText("player_name","JARRETT");pages.SetText("hunter_name","SAMUS");pages.SetText("build_version","UI REVIEW");pages.SetBool("reduce_motion",true);
  pages.SetText("home_friends_online","3");pages.SetText("home_invites_count","1");pages.SetText("home_requests_count","2");
  pages.SetBool("home_social_empty",false);pages.SetText("home_social_empty_text","");
  for(int i=0;i<3;i++){pages.SetBool($"home_friend{i}_visible",true);pages.SetText($"home_friend{i}_name","HUNTER "+(i+1));pages.SetText($"home_friend{i}_state",i==0?"IN LOBBY // JOINABLE":"ONLINE");}
  pages.ShowBaseline();
  void Draw(string name){pages.Flush();host.Update();pages.AfterUpdate();host.Render(size.Item1,size.Item2);SoftwarePreview.Save(new RmlUiDrawListReader().Capture(),size.Item1,size.Item2,Path.Combine(output,$"{name}-{size.Item1}x{size.Item2}.png"));}
  void Fits(RmlUiDocumentToken doc,string id)
  {
   Check(host.TryGetElementBounds(doc,id,out float x,out float y,out float w,out float h)&&w>0&&h>0,"positive bounds "+id);
   Check(x>=0&&y>=0&&x+w<=size.Item1+1&&y+h<=size.Item2+1,$"{id} outside {size}: {x},{y},{w},{h}");
  }
  Draw("home");foreach(var id in new[]{"nav_play","nav_hunters","nav_community","nav_studio","profile"})Fits(pages.Document,id);
  if(size.Item1>1180){foreach(var id in new[]{"home_signal_panel","home_social_activity","home_friend0","home_friend1","home_friend2"})Fits(pages.Document,id);}
  pages.SetBool("activity_selector_open",true);Draw("activities");Fits(pages.Document,"drawer_training");Fits(pages.Document,"drawer_adventure");
  RmlSplashPage.Open(host,pages);Draw("splash");Check(Directory.GetFiles(Path.Combine(fixture,"rmlui-thumbnail-cache"),"*.tga").Length>0,"original splash art decoded");Fits(pages.Manager.Page,"splash_continue");
  Check(host.FocusDocument(pages.Manager.Page,"splash_continue"),"splash initial action focus");
  host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
  Check(host.TryTakeIntent(out var start)&&start.Kind==RmlUiIntentKind.Navigate&&start.Argument==0,"splash emits Home route");
  pages.ShowBaseline();
  using(var hunters=new RmlHunterSelectionPresenter(host,backend:new FakeHunters()))
  {
   hunters.Open();hunters.Update();Draw("hunters");
   foreach(var id in new[]{"hunter_close","hunter_apply","hunter_cancel","hunter_preview_space","hunter_cosmetics_save","hunter_preview_death"})Fits(hunters.Document,id);
   Check(host.FocusDocument(hunters.Document,"hunter_close"),"Hunter close focus");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
   Check(host.TryTakeIntent(out var close)&&hunters.Handle(close)&&!hunters.Active,"Hunter closes using native action");
  }
  pages.Suspend();
  using(var settings=new SettingsPagePresenter(host,pages.Manager,new MenuSettings(),new SceneGameState(new()),()=>{},()=>{}))
  {
   settings.Open();pages.PresentChrome(settings.Document);Draw("settings");Fits(settings.Document,"settings_apply");Fits(settings.Document,"settings_back");
  }
  using(var offline=new OfflinePagePresenter(host,pages.Manager,new OfflineController(new MenuSettings(),Array.Empty<string>())))
  {
   offline.Open();pages.PresentChrome(offline.Document);Draw("offline");Fits(offline.Document,"offline_start");
   offline.FocusTraining();Draw("aim-lab");Fits(offline.Document,"offline_training_start");
  }
  var community=pages.Manager.OpenPage(new("community","pages/community/browser.rml","community_search"));
  pages.PresentChrome(community);
  foreach(string id in new[]{"community_lifecycle_filters","community_error","community_transfer","community_cancel_work"})host.SetBool(community,"visible:"+id,false);
  host.SetText(community,"community_count","1–4 of 14 maps");host.SetText(community,"community_title","Selected arena");host.SetText(community,"community_detail","Creator // Version 2\nPublished // Revision 1\nReady to install");
  for(int i=0;i<8;i++){host.SetBool(community,"visible:community_row_"+i,i<4);host.SetText(community,"community_name_"+i,"Community arena "+(i+1));host.SetText(community,"community_meta_"+i,"Creator // Published // Revision 1");}
  host.SetText(community,"community_status","Ready // Community catalog refreshed.");
  Draw("community");foreach(var id in new[]{"community_previous","community_next","community_select_3","community_detail_9"})Fits(community,id);
  host.SetBool(community,"visible:community_lifecycle_filters",true);Draw("community-owned");Fits(community,"community_detail_9");Fits(community,"community_select_3");
  pages.ShowBaseline(RmlUiMenuPage.Lobby);pages.SetText("lobby_name","JARRETT'S LOBBY");pages.SetText("lobby_map","TRANSFER LOCK");pages.SetText("lobby_mode_name","BATTLE");pages.SetText("lobby_player_count","8 / 8");pages.SetText("lobby_chat_history","Jarrett: Ready for the next round?\nHunter: Ready!");
  for(int i=0;i<8;i++){pages.SetBool($"slot{i}_occupied",true);pages.SetText($"slot{i}_name","HUNTER "+i);pages.SetText($"slot{i}_hunter","SAMUS");}
  Directory.CreateDirectory(ThumbnailGenerator.CacheDirectory);
  using(var thumbnail=new SKBitmap(320,180))
  {
   thumbnail.Erase(new SKColor(20,60,90));
   using var file=File.Create(ThumbnailGenerator.PathFor("TRANSFER LOCK"));thumbnail.Encode(file,SKEncodedImageFormat.Png,100);
  }
  bool previewReady=false;
  new LobbyMapPreview().Present("TRANSFER LOCK",pages.SetText,(id,value)=>{pages.SetBool(id,value);if(id=="lobby_map_image_ready")previewReady=value;});
  Check(previewReady,"lobby decodes cached map preview");
  Draw("lobby");foreach(var id in new[]{"lobby_match_rules","lobby_map_preview","lobby_chat_input","lobby_chat_send"})Fits(pages.Document,id);

  Check(!host.TryGetElementBounds(pages.Document,"footer_settings",out _,out _,out _,out _),"only one Settings entry");
  Fits(pages.Document,"header_settings");
  host.TryGetElementBounds(pages.Document,"lobby_match_panel",out _,out float panelY,out _,out float panelH);
  host.TryGetElementBounds(pages.Document,"lobby_map_preview",out _,out float previewY,out _,out _);
  Check(previewY>=panelY+panelH,"map preview does not overlap rules");
  var backend=new FakeLobby();using var lobby=new LobbySessionController(backend);
  RmlUiLobbyBindings.Present(pages,lobby.Snapshot());Draw("lobby-occupied");
  Check(!host.TryGetElementBounds(pages.Document,"lobby_slot7",out _,out _,out _,out float emptyH)||emptyH==0,"empty floating labels cannot cover main hunter");
  Check(host.FocusDocument(pages.Document,"lobby_map_preview"),"map preview is keyboard selectable");
  host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
  Check(host.TryTakeIntent(out var mapOpen)&&mapOpen.Kind==RmlUiIntentKind.LobbyMapOpen,"map preview opens typed picker");
  using(var maps=new RmlLobbyMapPicker(host,lobby,ThumbnailGenerator.MultiplayerRooms()))
  {
   maps.Open();Draw("map-picker");var doc=host.CurrentInputDocument;
   foreach(var id in new[]{"map_close","map_previous","map_next","map_choice0","map_choice5"})Fits(doc,id);
   Check(host.FocusDocument(doc,"map_next"),"map next focus");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
   Check(host.TryTakeIntent(out var next)&&next.Kind==RmlUiIntentKind.LobbyMapNext&&maps.Handle(next),"map paging uses native action");
   Check(host.FocusDocument(doc,"map_close"),"map close focus");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
   Check(host.TryTakeIntent(out var closeMap)&&maps.Handle(closeMap)&&!maps.Active,"map picker closes");
  }
  if(args.Length==4)
  {
   using var selectable=new RmlLobbyMapPicker(host,lobby,new[]{"AD1 TRANSFER LOCK BT","AD2 ALINOS PERCH"});
   selectable.Open();host.Update();var doc=host.CurrentInputDocument;
   Check(host.FocusDocument(doc,"map_choice1"),"compatible map card focus");
   host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
   Check(host.TryTakeIntent(out var selectMap)&&selectable.Handle(selectMap)&&!selectable.Active,"compatible map applies and closes");
   Check(backend.Commands.Any(c=>c.Kind==LobbyIntentKind.UpdateRules&&c.Match?.RoomKey=="AD2 ALINOS PERCH"),"map picker dispatches validated rules");
  }
  using(var strip=new RmlHunterSelectionPresenter(host,backend:new FakeHunters(),compact:true))
  {
   strip.Open();Draw("hunter-strip");for(int i=0;i<7;i++)Fits(strip.Document,"hunter_model"+i);
   Check(host.FocusDocument(strip.Document,"hunter_choice3"),"hunter card focus");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
   Check(host.TryTakeIntent(out var choose)&&strip.Handle(choose)&&!strip.Active,"hunter card selects and closes");
  }
  backend.State=backend.State with { Phase=SessionPhase.Starting, CountdownSeconds=2, StartStage=StartStage.Countdown };
  RmlUiLobbyBindings.Present(pages,lobby.Snapshot());Draw("countdown");Fits(pages.Document,"lobby_countdown_number");
  using(var settings=new SettingsPagePresenter(host,pages.Manager,new MenuSettings(),new SceneGameState(new()),()=>{},()=>{},editHud:()=>{}))
  { settings.Open();pages.PresentChrome(settings.Document);host.FocusDocument(settings.Document,"settings_hud_tab");host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
    if(host.TryTakeIntent(out var hudTab))settings.HandleAction(hudTab);Draw("hud-settings");Fits(settings.Document,"settings_hud_edit"); }
  Console.WriteLine($"PASS native UX layout {size}");
 }
 Console.WriteLine($"PASS {checks} native UX layout/input checks; PNGs rasterize the actual RmlUi draw list without a game renderer.");
}
finally{Directory.SetCurrentDirectory(previous);Directory.Delete(fixture,true);NativeLibrary.Free(module);}
