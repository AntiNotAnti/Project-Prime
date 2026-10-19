using GamepadState = MphRead.Mods.Input.GamepadState;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;
using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;

/// <summary>Real native documents with detached drafts and a fake window callback; never commits settings or invokes maintenance.</summary>
internal static class NativeFullSettingsGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? throw new InvalidOperationException("Explicit private user data required.");
        if (!Path.IsPathFullyQualified(root) || Path.GetFullPath(LauncherPrefs.Directory) != Path.GetFullPath(root))
            throw new InvalidOperationException("Settings GL fixture requires the explicit private preference directory.");
        const string scope = "Synthetic native full Settings and detached HUD editor, real DOM/layout/intents/preview renderer, fake window callback only. No settings Apply commit, maintenance, archive, account, gameplay or ROM acquisition effects. Screenshots require visual review.";
        string output = Path.Combine(root, "native-full-settings-gl", width + "x" + height); Directory.CreateDirectory(output);
        string[] stores = { Path.Combine(root,"launcher.txt"),Path.Combine(root,"controls.txt"),Path.GetFullPath(Path.Combine("Savedata","settings.json")),Path.Combine(root,"Savedata","hud-profiles","active.json") };
        var bytes = stores.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
        string liveHud = HudProfileStore.Serialize(HudProfiles.CopyCurrent());
        var settings = new MenuSettings(); var modes = new List<WindowStartMode>();
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeFullSettingsPage(host,pages,settings,modes.Add);
        var controller = (NativeFullSettingsController)typeof(NativeFullSettingsPage).GetField("_controller",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)!;
        var original = controller.Rows.Select(r=>r.Read()).ToArray();
        NativeHudEditorPage? hud = null; ulong sequence = 40000; int checks = 0;
        var geometry = new List<object>();
        host.Input.SetFramebufferScale(1,1); page.Open(); Settle();
        try
        {
            // Do the real native pointer cycle before synthetic sequence numbers.
            var window = controller.Rows.Single(r=>r.Category=="Display"&&r.Group=="Window");
            string saved = window.Value;
            Click("native_settings_cycle_0");
            Check(host.TryTakeIntent(out var cycle) && cycle.Kind==RmlUiIntentKind.SettingsAction && cycle.Argument==32
                && cycle.Document==page.Document && page.Handle(cycle) && !host.TryTakeIntent(out _), "actual native choice button emits and stages exactly one bounded intent");
            Frame(); Check(window.Value!=saved && !page.CanNavigateAway,"choice stages a guarded draft");
            // Native events must precede synthetic sequence numbers used by the remaining fixture.
            int initialFovIndex=Array.FindIndex(VisibleRows(),r=>r.Label=="Field Of View");
            string sliderId="native_settings_slider_"+initialFovIndex;
            Reveal(sliderId,false);Contained(page.Document,sliderId,"actual native FOV range pointer target");
            host.TryGetElementBounds(page.Document,sliderId,out float sx,out float sy,out float sw,out float sh);
            string beforeDrag=VisibleRows()[initialFovIndex].Value;host.DiscardIntents();
            // The compact range's 24dp outer center lies on the 12dp track's
            // top edge. Grab the actual styled thumb, not empty input background.
            float rangeDensity=float.Parse(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY")??"1",CultureInfo.InvariantCulture);
            float normalized=float.Parse(host.ReadField(page.Document,sliderId),CultureInfo.InvariantCulture)/1000;
            float inset=Math.Min(sw*.25f,12*rangeDensity),pointerY=sy+14*rangeDensity;
            float pointerStart=sx+inset+normalized*(sw-2*inset),pointerEnd=sx+inset+.88f*(sw-2*inset);
            host.Input.PointerMoved(pointerStart,pointerY);host.Update();
            Check(host.HoveredElement()==sliderId,"actual native pointer reaches the visible FOV slider thumb");
            host.Input.PointerButton(0,pointerStart,pointerY,true);host.Update();
            host.Input.PointerMoved(pointerEnd,pointerY);host.Update();
            host.Input.PointerButton(0,pointerEnd,pointerY,false);host.Update();
            File.WriteAllText(Path.Combine(output,"fov-pointer-witness.json"),JsonSerializer.Serialize(new{sliderId,sx,sy,sw,sh,rangeDensity,normalized,pointerStart,pointerEnd,pointerY,beforeDrag,nativeValue=host.ReadField(page.Document,sliderId),hover=host.HoveredElement()},new JsonSerializerOptions{WriteIndented=true}));
            bool sawRangeIntent=false;while(host.TryTakeIntent(out var rangeIntent))
            {
                Check(rangeIntent.Kind==RmlUiIntentKind.SettingsAction&&rangeIntent.Argument==32+initialFovIndex&&rangeIntent.Document==page.Document,"native range change emits the bounded FOV row intent");
                Check(page.Handle(rangeIntent),"actual range intent stages through native page acceptance");sawRangeIntent=true;
            }
            Frame();Check(sawRangeIntent&&VisibleRows()[initialFovIndex].Value!=beforeDrag,"pointer drag changes the native FOV draft");
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(original)&&bytes.All(p=>p.Value==null?!File.Exists(p.Key):File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"native slider leaves runtime owners and preference bytes unchanged before Apply");
            bool backHandled=page.Back();Frame();
            Check(backHandled&&!page.Closed&&Read().ContainsText("Unsaved changes"),"dirty Back stays on Settings with an explicit resolution message");
            Send(RmlUiIntentKind.SettingsDiscard);
            Check(page.CanNavigateAway&&window.Value==saved&&controller.Rows.Select(r=>r.Read()).SequenceEqual(original),"Discard restores draft and leaves engine statics unchanged");

            for(int category=0;category<NativeFullSettingsController.Categories.Length;category++)
            {
                Send(RmlUiIntentKind.SettingsCategory,category);if(category==3)Send(RmlUiIntentKind.SettingsAction,216);Settle();
                Check(Read().Label("native_settings_title")==NativeFullSettingsController.Categories[category],"native category title: "+NativeFullSettingsController.Categories[category]);
                foreach(string id in new[]{"native_settings_title","native_settings_apply","native_settings_status","native_settings_category_"+category})Contained(page.Document,id,"category "+category);
                var subpages=NativeFullSettingsPage.Subpages(category);
                for(int pane=0;pane<Math.Max(1,subpages.Length);pane++)
                {
                    if(subpages.Length>0){Send(RmlUiIntentKind.SettingsAction,232+pane);Reveal("native_settings_subpage_"+pane,false);Contained(page.Document,"native_settings_subpage_"+pane,"meaningful "+subpages[pane]+" subpage");}
                    var rows=controller.Rows.Where(r=>r.Category==NativeFullSettingsController.Categories[category]&&(subpages.Length==0||NativeFullSettingsPage.RowSubpage(r)==pane)).ToArray();
                    Check(rows.Length<=180,"complete natural form fits bounded row slots");
                    if(rows.Length==0)continue;
                    string first=rows[0].Choices.Length>0||rows[0].IsBinding?"native_settings_cycle_0":"native_settings_value_0";
                    Reveal(first,false);Contained(page.Document,first,"first readable sectioned form control");
                    Check(Read().Label(first).Contains(NativeSemanticReadback.Normalize(rows[0].Label),StringComparison.Ordinal),"first interactive control names actual owner setting");
                    foreach(string section in rows.Select(r=>r.Group).Distinct())Check(Read().ContainsText(section),"natural form retains section heading without extra navigation: "+section);
                    if(rows.Length>10)
                    {
                        int last=rows.Length-1;string id=rows[last].Choices.Length>0||rows[last].IsBinding?"native_settings_cycle_"+last:"native_settings_value_"+last;
                        Reveal(id,true);Contained(page.Document,id,"natural native scrolling reaches last of "+rows.Length+" settings");
                        Check(Read().Label(id).Contains(NativeSemanticReadback.Normalize(rows[last].Label),StringComparison.Ordinal),"last natural-form control retains owner name");Capture("settings-"+category+"-"+pane+"-scrolled");
                    }
                }
                if(subpages.Length>0)Send(RmlUiIntentKind.SettingsAction,232);
                if(category is 0 or 1 or 2 or 3 or 6 or 7)Capture("settings-"+NativeFullSettingsController.Categories[category].ToLowerInvariant());
            }
            Send(RmlUiIntentKind.SettingsCategory,3);Send(RmlUiIntentKind.SettingsAction,232);
            var keyboardRows=controller.Rows.Where(r=>r.Group=="Keyboard bindings").ToArray();
            foreach(var row in keyboardRows)controller.Stage(row,"Unknown");
            controller.Stage(keyboardRows[0],"F1");controller.Stage(keyboardRows[1],"F2");
            Send(RmlUiIntentKind.SettingsAction,232);
            SendRow(keyboardRows[0]);Check(page.KeyDown(Keys.F2,default),"native keyboard capture receives actual key adapter input");Frame();
            Check(Read().Label("native_settings_confirm")=="SWAP"&&Read().ContainsText("REPLACE")&&Read().ContainsText("KEEP BOTH"),"native conflict offers explicit Swap Replace Keep Both and Cancel");
            Capture("settings-binding-conflict");Send(RmlUiIntentKind.SettingsAction,214);
            Check(keyboardRows[0].Value=="F2"&&keyboardRows[1].Value=="F2"&&!controller.BindingConflict,"native Keep Both stages only the proposal");
            SendRow(keyboardRows[0]);Check(page.KeyDown(Keys.Delete,default),"native Delete clears a listening binding");Frame();
            Check(keyboardRows[0].Value=="Unknown"&&!controller.Capturing,"native clear ends capture with unbound draft");Send(RmlUiIntentKind.SettingsDiscard);
            Send(RmlUiIntentKind.SettingsAction,233);
            var nativeLayout=controller.Rows.Single(r=>r.Label=="Control layout");string layoutBefore=nativeLayout.Value;
            SendRow(nativeLayout);
            Check(nativeLayout.Value!=layoutBefore&&!page.CanNavigateAway,"native named controller layout is an explicit staged choice");Capture("settings-controller-layout");
            Send(RmlUiIntentKind.SettingsDiscard);
            Reveal("native_settings_device",false);Contained(page.Document,"native_settings_device","scrollable native controller device selector");
            Check(Read().Label("native_settings_device").Contains("CONTROLLER:"),"native current-device selector names its editing target");

            // A connected button is essential: a disconnected default snapshot
            // would cancel even the old buggy hidden capture and miss this regression.
            var snapshotField=typeof(GamepadManager).GetField("_snapshot",BindingFlags.Static|BindingFlags.NonPublic)!;
            var snapshotBefore=(GamepadSnapshot)snapshotField.GetValue(null)!;
            try
            {
                var idle=snapshotBefore with{State=new GamepadState{Connected=true,Buttons=GamepadButtons.None}};
                snapshotField.SetValue(null,idle);
                Send(RmlUiIntentKind.SettingsAction,233);
                var draftBeforeCover=controller.Rows.Select(r=>r.Value).ToArray();
                SendRow(controller.Rows.First(r=>r.BindingAction!=null));Check(controller.ControllerCapturing,"connected native controller capture is pending before License covers Settings");
                var cover=pages.OpenModal(new("settings-input-cover","pages/license/native-overview.rml","native_license_close"));
                snapshotField.SetValue(null,idle with{State=new GamepadState{Connected=true,Buttons=GamepadButtons.B}});
                Frame();
                Check(!controller.Capturing&&!controller.BindingConflict&&controller.Rows.Select(r=>r.Value).SequenceEqual(draftBeforeCover)
                    &&controller.Status.Contains("covered"),"cover cancels capture before polling connected button and preserves complete draft");
                Check(!page.KeyDown(Keys.Delete,default)&&!page.Handle(new(RmlUiIntentKind.SettingsAction,213,page.Document,sequence++)),"covered page rejects delayed keyboard and conflict resolution input");
                Check(pages.Top==cover&&pages.CloseModal(),"synthetic License cover retires through actual native page manager");Frame();
                Check(!controller.Capturing&&!controller.BindingConflict&&Read().ContainsText("Settings was covered"),"returning foreground shows cancellation and cannot resurrect capture");
                Capture("settings-covered-capture-return");
            }
            finally{snapshotField.SetValue(null,snapshotBefore);if(pages.Top!=page.Document&&pages.ModalCount>0)pages.CloseModal();}
            foreach(var row in keyboardRows)controller.Stage(row,"Unknown");controller.Stage(keyboardRows[0],"F1");controller.Stage(keyboardRows[1],"F2");
            Send(RmlUiIntentKind.SettingsAction,232);SendRow(keyboardRows[0]);
            Check(page.KeyDown(Keys.F2,default)&&controller.BindingConflict,"native duplicate-key proposal is pending before cover");
            var conflictValues=controller.Rows.Select(r=>r.Value).ToArray();
            pages.OpenModal(new("settings-conflict-cover","pages/license/native-overview.rml","native_license_close"));Frame();
            Check(!controller.BindingConflict&&!controller.Capturing&&controller.Rows.Select(r=>r.Value).SequenceEqual(conflictValues),"cover cancels pending conflict without implicit Swap Replace or Keep Both");
            typeof(NativeFullSettingsController).GetField("_operation",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(controller,System.Threading.Tasks.Task.FromResult("Covered operation completed."));Frame();
            Check(!controller.Busy&&controller.Status=="Covered operation completed.","covered page still polls completed operations without sampling input");
            pages.CloseModal();Frame();Send(RmlUiIntentKind.SettingsDiscard);
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(original)
                &&bytes.All(p=>p.Value==null?!File.Exists(p.Key):File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"covered-input lifecycle changes no engine statics or preference bytes");
            controller.CalibrationAction(26);page.CancelInputCapture();
            Check(typeof(NativeFullSettingsController).GetField("_calibration",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(controller)==null,"native focus-loss cancellation retires unfinished calibration");

            host.SetField(page.Document,"native_settings_search","Imperialist Zoom Amount");Send(RmlUiIntentKind.SettingsAction,29);Settle();
            var found=controller.Rows.Single(r=>r.Label=="Imperialist Zoom Amount");int foundIndex=Array.IndexOf(VisibleRows(),found);
            Check(controller.Category==3&&host.FocusedElement()=="native_settings_value_"+foundIndex,"search reveals owning subpage and focuses actual precision control");
            Send(RmlUiIntentKind.SettingsCategory,0);
            host.SetField(page.Document,"native_settings_search","Field Of View");Send(RmlUiIntentKind.SettingsAction,29);Settle();
            int fovIndex=Array.FindIndex(VisibleRows(),r=>r.Label=="Field Of View");
            Check(host.FocusedElement()=="native_settings_value_"+fovIndex,"FOV search focuses its actual typed precision input");
            host.SetField(page.Document,"native_settings_slider_"+fovIndex,"1000");Send(RmlUiIntentKind.SettingsAction,32+fovIndex);
            Check(controller.Rows.Single(r=>r.Label=="Field Of View").Value=="120","native tuning slider stages exact owner limit without a separate Stage button");
            Send(RmlUiIntentKind.SettingsDiscard);
            host.SetField(page.Document,"native_settings_value_"+fovIndex,"not-a-number");
            Send(RmlUiIntentKind.SettingsClose);
            Check(!page.Closed&&Read().ContainsText("Enter a whole number"),"invalid text keeps focusable native Settings and explains actual owner range");
            Send(RmlUiIntentKind.SettingsDiscard);
            // Window remains within the same natural Display form.
            controller.Stage(window,window.Choices.First(c=>c!=window.Saved));
            Check(controller.Apply()&&controller.ConfirmingVideo&&controller.VideoSeconds is >=14 and <=15&&modes.Count==1,
                "fake window preview enters fifteen-second confirmation without engine commit");
            Check(!controller.CanKeepVideo&&!controller.KeepVideo()&&controller.ConfirmingVideo,"Keep rejects a display proposal before a usable native frame");
            Frame();Check(controller.CanKeepVideo,"successful foreground native frame acknowledges display proposal");Contained(page.Document,"native_settings_video","display confirmation card");Contained(page.Document,"native_settings_video_countdown","display confirmation countdown");
            Check(Read().Label("native_settings_video_countdown").Contains("Reverting in",StringComparison.Ordinal),"display confirmation countdown is readable native status text"); Capture("settings-video-proposal");
            var videoCover=pages.OpenModal(new("settings-video-cover","pages/license/native-overview.rml","native_license_close"));
            typeof(NativeFullSettingsController).GetField("_videoDeadline",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(controller,DateTime.UtcNow.AddMilliseconds(-1));
            Frame();Check(!controller.ConfirmingVideo&&modes.Count==2&&modes[^1]==LauncherPrefs.WindowMode&&pages.Top==videoCover,"expired fake-window proposal rolls back while another native modal covers Settings");
            pages.CloseModal();Frame();Send(RmlUiIntentKind.SettingsDiscard);
            controller.Stage(window,window.Choices.First(c=>c!=window.Saved));
            Check(controller.Apply()&&controller.ConfirmingVideo,"second fake display trial begins for focus-loss guard");
            int beforeFocusRollback=modes.Count;page.CancelInputCapture();Frame();
            Check(!controller.ConfirmingVideo&&!controller.CanKeepVideo&&modes.Count==beforeFocusRollback+1&&modes[^1]==LauncherPrefs.WindowMode,"focus loss rolls back an unacknowledged display proposal");
            Send(RmlUiIntentKind.SettingsDiscard);

            Send(RmlUiIntentKind.SettingsAction,3);
            Check(page.TryTakeOpenHud(out HudProfile draft)&&!page.TryTakeOpenHud(out _),"Settings emits a single detached HUD draft handoff");
            page.SuspendView(); hud=new NativeHudEditorPage(host,pages,draft);hud.Open();Settle(180);
            Check(hud.HasLivePage&&hud.OwnsForeground&&hud.CanNavigateAway,"native HUD wrapper owns new foreground document with clean copied draft");
            Check(host.TryGetElementBounds(hud.Document,"hud_canvas",out float canvasX,out float canvasY,out float canvasW,out float canvasH)&&canvasW>0&&canvasH>0,"native HUD editable canvas has actual dimensions");
            hud.SetCanvasSize(canvasW,canvasH);Settle(250);
            SendHud(RmlUiIntentKind.HudElement,1);
            var hudController=(HudEditorController)typeof(NativeHudEditorPage).GetField("_controller",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(hud)!;
            var state=hudController.Snapshot();var bounds=state.Elements[1].Bounds;
            float x=bounds.X+bounds.Width/2,y=bounds.Y+bounds.Height/2;
            Check(hud.PointerDown(17,x,y)&&hud.PointerMove(17,x+24,y+12),"native HUD wrapper forwards canvas-local drag into existing layout controller");hud.PointerUp(17);Frame();
            Check(!hud.CanNavigateAway&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==liveHud,"HUD drag dirties only detached profile");
            var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<1800&&Read().Label("hud_preview_warning").Length==0){Frame();Thread.Sleep(10);}
            string warning=Read().Label("hud_preview_warning");
            string previewDirectory=Path.Combine(root,"rmlui-hud-preview-cache");
            bool bitmap=Directory.Exists(previewDirectory)&&Directory.EnumerateFiles(previewDirectory,"*.tga").Any();
            Capture("hud-detached-preview");
            File.WriteAllText(Path.Combine(output,"hud-preview-witness.json"),JsonSerializer.Serialize(new{bitmap,warning,scope="Actual HUD preview builder output; no owned ROM supplied by this fixture. Native-art fallback warning is preserved; schematic preview is not a claim of ROM-art parity."},new JsonSerializerOptions{WriteIndented=true}));
            Check(bitmap||warning.Length>0,"actual preview completes with a generated bitmap or honest native error witness");
            Check(hud.Back()&&!hud.Closed&&pages.Top!=hud.Document,"dirty HUD Back opens native apply/discard/keep-editing boundary");
            Capture("hud-unsaved-boundary");
            SendHud(RmlUiIntentKind.HudAction,(int)HudEditorAction.Undo);Check(pages.Top==hud.Document&&!hud.Closed,"Keep Editing closes HUD confirmation without losing draft");
            SendHud(RmlUiIntentKind.HudAction,(int)HudEditorAction.Use);
            Check(hud.TryTakeAccepted(out var accepted)&&hud.Closed,"native HUD accepts one detached profile");
            hud.Dispose();hud=null;page.AcceptHud(accepted);page.ResumeView();Settle();
            Check(!page.CanNavigateAway&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==liveHud,"resumed Settings owns HUD draft and still requires Apply");
            Send(RmlUiIntentKind.SettingsDiscard);
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(original)&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==liveHud,
                "discard after native HUD roundtrip preserves all engine settings and original live HUD");
            NativeSettingsVisualAcceptance.Run(host,pages,new MenuSettings());
            var retainedPage=pages.Page;
            using(var overlay=new NativeFullSettingsPage(host,pages,new MenuSettings(),asModal:true))
            {
                overlay.Open();host.Update();
                Check(overlay.OwnsForeground&&pages.Page==retainedPage&&host.IsAlive(retainedPage)&&!page.OwnsForeground,"modal Settings preserves the exact underlying workspace document");
                Check(overlay.Handle(new(RmlUiIntentKind.SettingsAction,3,overlay.Document,sequence++)),"modal Settings accepts native HUD command");
                bool tookModalHud=overlay.TryTakeOpenHud(out var draftForModal);Check(tookModalHud,"modal Settings emits detached HUD handoff");
                overlay.SuspendView();
                using(var hudOverlay=new NativeHudEditorPage(host,pages,draftForModal,asModal:true))
                {
                    hudOverlay.Open();host.Update();
                    Check(hudOverlay.OwnsForeground&&pages.Page==retainedPage&&host.IsAlive(retainedPage),"modal HUD preserves the same underlying workspace document");
                    Check(hudOverlay.Back()&&hudOverlay.Closed,"clean modal HUD Back retires without mutating parent draft");
                }
                overlay.ResumeView();host.Update();
                Check(overlay.OwnsForeground&&pages.Page==retainedPage&&host.IsAlive(retainedPage),"resumed modal Settings retains underlying workspace identity");
            }
            Check(pages.Page==retainedPage&&page.OwnsForeground&&host.IsAlive(retainedPage),"closing modal Settings restores the original foreground document");
            Check(bytes.All(p=>p.Value==null?!File.Exists(p.Key):File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value)),"native Settings and HUD functional fixture changes no preference owner bytes");
            Check(GL.GetError()==ErrorCode.NoError,"native Settings and HUD frames leave no GL error");
            File.WriteAllText(Path.Combine(output,"settings-summary.json"),JsonSerializer.Serialize(new{passed=true,checks,width,height,density=Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY")??"1",scope,geometry},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{hud?.Dispose();}

        NativeSemanticReadback Read()=>NativeSemanticReadback.Read(host,pages.Top);
        void Check(bool pass,string label)
        {
            if(!pass)
            {
                try
                {
                    var actual=new List<object>();var document=pages.Top;
                    foreach(string id in Enumerable.Range(0,180).SelectMany(i=>new[]{"native_settings_label_"+i,"native_settings_cycle_"+i,"native_settings_value_"+i,"native_settings_row_"+i})
                        .Concat(new[]{"native_settings_title","native_settings_status","native_settings_apply","native_settings_controller_identity","native_settings_video","native_settings_video_countdown"}))
                    {
                        bool exists=host.TryGetElementBounds(document,id,out float x,out float y,out float w,out float h);actual.Add(new{id,exists,x,y,w,h});
                    }
                    string semantic=Read().RawJson;
                    File.WriteAllText(Path.Combine(output,"failure.semantic.json"),semantic);
                    File.WriteAllText(Path.Combine(output,"failure-summary.json"),JsonSerializer.Serialize(new{passed=false,label,checks,width,height,scope,category=controller.Category,
                        stagedRows=controller.Rows.Select(r=>new{r.Category,r.Group,r.Label,r.Value}),geometry,actualBounds=actual},new JsonSerializerOptions{WriteIndented=true}));
                    ScreenCapture.SaveWindow(width,height,Path.Combine(output,"failure.png"));
                }
                catch(Exception diagnostic){Console.WriteLine("NATIVE SETTINGS GL diagnostic: "+diagnostic.Message);}
                throw new InvalidOperationException("NATIVE SETTINGS GL: "+label);
            }
            checks++;Console.WriteLine("NATIVE SETTINGS GL PASS "+label);
        }
        void Frame()
        {
            if(hud!=null)hud.Refresh();else page.Refresh();host.Update();
            var doc=hud?.Document??page.Document;if(host.IsAlive(doc)){host.SetBool(doc,"class:screen:reduce-motion",true);host.SetBool(doc,"class:@document:reduce-motion",true);}
            host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.025f,.05f,.08f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);if(hud==null)page.AcknowledgeUsableFrame();
        }
        void Settle(int ms=65){var watch=Stopwatch.StartNew();do{Frame();Thread.Sleep(5);}while(watch.ElapsedMilliseconds<ms);}
        NativeSettingRow[] VisibleRows()=>(NativeSettingRow[])typeof(NativeFullSettingsPage).GetField("_visible",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)!;
        void SendRow(NativeSettingRow row){int i=Array.IndexOf(VisibleRows(),row);Check(i>=0,"requested control belongs to current sectioned form");Send(RmlUiIntentKind.SettingsAction,32+i);}
        void Send(RmlUiIntentKind kind,int argument=0){Check(page.Handle(new(kind,argument,page.Document,sequence++)),"typed native Settings action "+kind+"/"+argument);Frame();}
        void SendHud(RmlUiIntentKind kind,int argument){Check(hud!.Handle(new(kind,argument,pages.Top,sequence++)),"typed native HUD action "+kind+"/"+argument);Frame();}
        void Click(string id)
        {
            Check(host.FocusDocument(page.Document,id),"native focus reaches "+id);Settle();Contained(page.Document,id,"pointer target");
            host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h);
            host.DiscardIntents();host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
        }
        void Reveal(string id,bool bottom)
        {
            // Flush category/subpage pending focus before requesting this target.
            Frame();
            // FocusDocument uses the production native ScrollIntoView path. A fixed
            // delay races RmlUi smooth scrolling on long forms; wait for its result.
            Check(host.FocusDocument(page.Document,id),"native group control remains focusable: "+id);
            var watch=Stopwatch.StartNew();float lastX=float.NaN,lastY=float.NaN;int stable=0;
            while(watch.ElapsedMilliseconds<2000)
            {
                Frame();
                bool bounded=host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h)&&w>0&&h>0&&x>=-1&&y>=-1&&x+w<=width+1&&y+h<=height+1;
                using var semantic=JsonDocument.Parse(Read().RawJson);
                bool visible=semantic.RootElement.GetProperty("nodes").EnumerateArray().Any(n=>n.GetProperty("id").GetString()==id&&!n.GetProperty("offscreen").GetBoolean()&&n.GetProperty("focused").GetBoolean());
                stable=bounded&&visible&&Math.Abs(x-lastX)<.1f&&Math.Abs(y-lastY)<.1f?stable+1:0;lastX=x;lastY=y;
                if(stable>=3)break;Thread.Sleep(5);
            }
            Check(stable>=3,"native focus scroll settles on a fully visible stable target: "+id);
            if(bottom)
            {
                host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h);host.DiscardIntents();
                host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
                if(id.StartsWith("native_settings_cycle_",StringComparison.Ordinal))
                {
                    int row=int.Parse(id["native_settings_cycle_".Length..],CultureInfo.InvariantCulture);
                    bool hit=false;while(host.TryTakeIntent(out var intent)){Check(intent.Document==page.Document&&intent.Kind==RmlUiIntentKind.SettingsAction&&intent.Argument==32+row,"real pointer hits the exact revealed last control");hit=true;}
                    Check(hit,"revealed last choice emits its native pointer action");
                }
                else Check(host.FocusedElement()==id,"real pointer focuses the revealed last precision input");
                host.DiscardIntents();
            }
        }
        void Contained(RmlUiDocumentToken document,string id,string label)
        {
            bool exists=host.TryGetElementBounds(document,id,out float x,out float y,out float w,out float h);
            geometry.Add(new{label,id,x,y,w,h,exists});
            Check(exists&&w>0&&h>0&&x>=-1&&y>=-1&&x+w<=width+1&&y+h<=height+1,label+" fits framebuffer: "+id+" ["+x+","+y+","+w+","+h+"]");
        }
        void Capture(string name)
        {
            Frame();string path=Path.Combine(output,name+".png");Check(ScreenCapture.SaveWindow(width,height,path),"capture "+name);
            FinalCompositeCapture.WriteEvidence(path,width,height,scope);File.WriteAllText(path+".semantic.json",Read().RawJson);
        }
    }
}
