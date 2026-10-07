#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.IO;
using System.Threading;
using MphRead.Entities;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Render.Hud;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
public static class SettingsNativePageCheck
{
    // Standalone check process only: authoritative globals are intentionally exercised.
    public static void Run(string assets)
    {
        string previous=Directory.GetCurrentDirectory(),prefs=LauncherPrefs.Directory,export=Paths.Export;
        string fixture=Directory.CreateTempSubdirectory("prime-settings-native-").FullName;
        try
        {
            Directory.SetCurrentDirectory(fixture);LauncherPrefs.Directory=fixture;
            Paths.SetPath("Export",Path.Combine(fixture,"export"));
            LauncherPrefs.DebugLogs=false;LauncherPrefs.ReplayAutoPrune=false;
            HudProfiles.Load(Path.Combine(fixture,"Savedata","hud-profiles"));
            var menu=new MenuSettings();var state=new SceneGameState(new());
            using var host=new RmlUiHost();
            if(!host.Initialize(1280,720,1,assets,RmlUiRenderBackend.DrawList))throw new InvalidOperationException("Native Settings host initialization failed.");
            using var pages=new RmlUiPageManager(host);
            bool closed=false,left=false;
            using var presenter=new SettingsPagePresenter(host,pages,menu,state,()=>closed=true,()=>{});
            presenter.Open();host.Update();host.Render(1280,720);
            void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);Console.WriteLine("RMLSETTINGS NATIVE PASS "+message);}
            void Click(string id)
            {
                presenter.Refresh();host.Update();Check(host.FocusDocument(pages.Top,id),"focus "+id);
                host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
                Check(host.TryTakeIntent(out var intent)&&presenter.HandleAction(intent),"typed DOM action "+id);
                host.Update();host.Render(1280,720);
            }
            var document=presenter.Document;
            Check(host.TryGetElementBounds(document,"settings_value_0",out _,out _,out float fieldWidth,out float fieldHeight)&&fieldWidth>150&&fieldHeight>=25,"native settings input has usable positive layout width");
            Check(host.TryGetElementBounds(document,"settings_apply",out _,out _,out float applyWidth,out float applyHeight)&&applyWidth>100&&applyHeight>=30,"native apply button has usable positive layout width");
            Check(host.ReadField(document,"settings_value_0")==menu.FieldOfView,"native text field contains authoritative field of view");
            string fov=menu.FieldOfView;
            host.SetField(document,"settings_value_0","90");Click("settings_apply");
            Check(presenter.Controller.PendingVideoConfirmation&&menu.FieldOfView=="90"&&!File.Exists("Savedata/settings.json"),"native apply previews video without writing settings");
            Check(!presenter.Controller.KeepVideo(),"native preview cannot be kept before usable renderer present");
            Click("settings_video_revert");
            Check(!presenter.Controller.PendingVideoConfirmation&&menu.FieldOfView==fov&&pages.ModalCount==0,"native video revert restores previous configuration and modal focus");
            Click("settings_profile_tab");Click("settings_open_glyphs");
            Check(pages.ModalCount==1,"native profile name opens glyph picker");
            host.SetField(pages.Top,"settings_glyph_name","AB");Click("settings_glyph_1");
            string glyphName=host.ReadField(pages.Top,"settings_glyph_name");
            Check(glyphName.Contains('!')&&LauncherPrefs.PlayerName!=glyphName,"glyph insertion uses native editable text and stays detached");
            Click("settings_glyph_use");Check(pages.ModalCount==0&&presenter.Controller.Draft["prefs.PlayerName"]==glyphName,"validated glyph name stages through the Settings transaction");
            Click("settings_controls_tab");
            host.SetField(document,"settings_search","mouse");Click("settings_search_button");
            Check(presenter.Controller.Snapshot().Fields.Count>0&&presenter.Controller.Snapshot().Fields[0].Definition.Id.StartsWith("input.",StringComparison.Ordinal),"native search presents control fields");
            Click("settings_clear_search");
            var binding=InputSettings.Bind(InputSettings.Bindings[0]);var oldMouse=binding.MouseButton;
            host.SetField(document,"settings_capture_id",InputSettings.Bindings[0].Name);Click("settings_capture_button");
            Thread.Sleep(210);Check(presenter.TryCaptureKey(Keys.Space),"physical key capture consumed while binding modal is active");
            Check(presenter.Controller.Dirty&&binding.Key!=Keys.Space&&pages.ModalCount==0,"captured key remains a detached draft");
            Click("settings_apply");
            Check(binding.Key==Keys.Space&&binding.MouseButton==oldMouse&&!presenter.Controller.Dirty,"captured key save preserves unused mouse field");
            presenter.Controller.Set("input.MouseSensitivity","0.42");presenter.Refresh();
            presenter.RequestLeave(()=>left=true);
            Check(!left&&pages.ModalCount==1,"route navigation waits for dirty draft decision");
            Click("settings_close_discard");
            Check(left&&!closed&&pages.Page==default,"discard closes Settings and executes the queued route once");
            Check(!presenter.TryCaptureKey(Keys.A),"retired Settings capture cannot consume future input");
            Console.WriteLine("RMLSETTINGS NATIVE CHECK PASS actual documents, typed intents, text drafts, capture, video rollback and navigation guard");
        }
        finally{Directory.SetCurrentDirectory(previous);LauncherPrefs.Directory=prefs;Paths.SetPath("Export",export);Directory.Delete(fixture,true);}
    }
}
#endif
