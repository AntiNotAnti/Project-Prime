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
using MphRead.Sound;

/// <summary>Real native documents with isolated settings autosave and a fake window callback; never invokes maintenance.</summary>
internal static class NativeFullSettingsGlAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string root = Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA") ?? throw new InvalidOperationException("Explicit private user data required.");
        if (!Path.IsPathFullyQualified(root) || Path.GetFullPath(LauncherPrefs.Directory) != Path.GetFullPath(root))
            throw new InvalidOperationException("Settings GL fixture requires the explicit private preference directory.");
        const string scope = "Synthetic native full Settings and detached HUD editor, real DOM/layout/intents/preview renderer, fake window callback only. Settings autosave uses only this explicitly isolated preference directory; no maintenance, archive, account, gameplay or ROM acquisition effects. Screenshots require visual review.";
        string output = Path.Combine(root, "native-full-settings-gl", width + "x" + height); Directory.CreateDirectory(output);
        string[] stores = { Path.Combine(root,"launcher.txt"),Path.Combine(root,"controls.txt"),Path.GetFullPath(Path.Combine("Savedata","settings.json")),Path.Combine(root,"Savedata","hud-profiles","active.json"),Path.Combine(root,"Savedata","hud-profiles","active.json.bak") };
        var bytes = stores.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
        string liveHud = HudProfileStore.Serialize(HudProfiles.CopyCurrent());
        var hudStoreField=typeof(HudProfiles).GetField("_store",BindingFlags.Static|BindingFlags.NonPublic)!;
        object? hudStoreBefore=hudStoreField.GetValue(null);
        var isolatedHudStore=new HudProfileStore(Path.Combine(root,"Savedata","hud-profiles"));
        var settings = new MenuSettings(); var modes = new List<WindowStartMode>();
        byte[] settingsBefore = JsonSerializer.SerializeToUtf8Bytes(settings);
        MenuSettings? gameSettingsBefore = GameSettings.Current;
        byte[]? gameSettingsBytesBefore = gameSettingsBefore == null ? null : JsonSerializer.SerializeToUtf8Bytes(gameSettingsBefore);
        var featuresBefore = Features.Commit();
        var renderOptionsBefore = CaptureRenderOptions();
        int frameRateCapBefore = FrameTiming.FrameRateCap;
        Language languageBefore = Scene.Language;
        var mixerBefore = Enum.GetValues<AudioBus>().ToDictionary(bus => bus, AudioMixer.GetVolume);
        float sfxVolumeBefore = Sfx.Volume, musicVolumeBefore = Music.UserVolume;
        var respawnHunterField = typeof(RespawnChoice).GetField("_hunter",BindingFlags.Static|BindingFlags.NonPublic)!;
        var respawnColorField = typeof(RespawnChoice).GetField("_color",BindingFlags.Static|BindingFlags.NonPublic)!;
        object? respawnHunterBefore = respawnHunterField.GetValue(null), respawnColorBefore = respawnColorField.GetValue(null);
        Action restoreStylusPlacement=StylusZone.CapturePlacement();
        float stylusLeftBefore=StylusZone.SavedLeft,stylusTopBefore=StylusZone.SavedTop,stylusWidthBefore=StylusZone.SavedWidth;
        using var pages = new RmlUiPageManager(host);
        using var page = new NativeFullSettingsPage(host,pages,settings,modes.Add);
        var controller = (NativeFullSettingsController)typeof(NativeFullSettingsPage).GetField("_controller",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)!;
        var original = controller.Rows.Select(r=>r.Read()).ToArray();
        NativeHudEditorPage? hud = null; ulong sequence = 40000; int checks = 0;
        var geometry = new List<object>();
        string authored = File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"rmlui","pages","settings","native-full.rml"));
        var slots = System.Text.RegularExpressions.Regex.Matches(authored,"id=\"native_settings_row_([0-9]+)\"")
            .Select(match=>int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture)).ToArray();
        if(!slots.SequenceEqual(Enumerable.Range(0,NativeFullSettingsPage.Slots)))
            throw new InvalidOperationException("Shipped Settings row slots do not match the presenter capacity.");
        try
        {
            // Full Settings saves the active HUD profile as part of its real
            // autosave. Isolate that store before opening the page so every
            // path after the first mutation is covered by the cleanup guard.
            hudStoreField.SetValue(null,isolatedHudStore);
            host.Input.SetFramebufferScale(1,1); page.Open(); Settle();
            // Follow the actual Overview -> category landing -> section route.
            ClickIntent("native_settings_overview_0",RmlUiIntentKind.SettingsCategory,0,"Overview Display card");
            Check(Read().Label("native_settings_title")=="Display"&&Read().Label("native_settings_subpage_0")=="View",
                "Display category opens its View section chooser");
            ClickIntent("native_settings_subpage_0",RmlUiIntentKind.SettingsAction,232,"Display View section card");
            Check(Read().Label("native_settings_title")=="Display / View","native View form opens through its native intent");

            // Do the real native pointer cycle before synthetic sequence numbers.
            var window = controller.Rows.Single(r=>r.Category=="Display"&&r.Group=="Window");
            string saved = window.Value;
            int windowIndex=Array.IndexOf(VisibleRows(),window);
            Check(windowIndex>=0,"native Window Mode belongs to the active Display/View form");
            string windowControl="native_settings_cycle_"+windowIndex;
            Reveal(windowControl,false);
            Click(windowControl);
            Check(host.TryTakeIntent(out var cycle) && cycle.Kind==RmlUiIntentKind.SettingsAction && cycle.Argument==32+windowIndex
                && cycle.Document==page.Document && page.Handle(cycle) && !host.TryTakeIntent(out _), "actual native choice button emits and stages exactly one bounded intent");
            Frame(); Check(window.Value!=saved && !page.CanNavigateAway,"choice stages a guarded draft");
            Settle(500);
            Check(controller.ConfirmingVideo&&controller.CanKeepVideo&&modes.Count==1,
                "native window choice enters the acknowledged autosave display guard");
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(original)&&PreferenceFilesMatchSnapshot(),
                "guarded display preview leaves runtime owners and private preference files unchanged until Keep");
            var popup=pages.Top;
            Check(host.FocusDocument(popup,"native_settings_revert_video"),"native display guard focuses its visible Revert action");
            Frame();
            Check(host.TryGetElementBounds(popup,"native_settings_revert_video",out float revertX,out float revertY,out float revertW,out float revertH)
                &&revertW>0&&revertH>0,"native display guard exposes its Revert target");
            host.DiscardIntents();host.Input.PointerMoved(revertX+revertW/2,revertY+revertH/2);
            host.Input.PointerButton(0,revertX+revertW/2,revertY+revertH/2,true);host.Input.PointerButton(0,revertX+revertW/2,revertY+revertH/2,false);host.Update();
            Check(host.TryTakeIntent(out var revert)&&revert.Kind==RmlUiIntentKind.SettingsRevertVideo&&revert.Document==popup,
                "native display Revert button emits the owner-bound video intent");
            Check(page.Handle(revert)&&!host.TryTakeIntent(out _),"native display Revert intent returns to the saved mode");Frame();
            Check(!controller.ConfirmingVideo&&window.Value==saved&&controller.Rows.Select(r=>r.Read()).SequenceEqual(original)
                &&PreferenceFilesMatchSnapshot(),"reverting the guarded native choice preserves owner values and preference bytes");

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
            Settle(500);
            var fovRow=controller.Rows.Single(r=>r.Label=="Field Of View");int fovRowIndex=Array.IndexOf(controller.Rows.ToArray(),fovRow);
            string savedFov=fovRow.Value,originalFov=original[fovRowIndex];
            Check(!savedFov.Equals(originalFov,StringComparison.Ordinal)&&fovRow.Saved==savedFov&&fovRow.Read()==savedFov
                &&!controller.Dirty&&!controller.AutoApplyPending&&Read().Label("native_settings_status")=="Settings saved automatically.",
                "native pointer slider auto-saves its exact owner value after the quiet interval");
            Check(!PreferenceFilesMatchSnapshot(),"native slider autosave produces a private preference-file witness");
            bool backHandled=page.Back();Frame();
            Check(backHandled&&!page.Closed&&Read().Label("native_settings_title")=="Display",
                "clean autosaved Back returns from the View form to the Display section chooser");
            Check(controller.Stage(fovRow,originalFov)&&controller.FlushAutoApply()&&fovRow.Read()==originalFov&&fovRow.Saved==originalFov,
                "private fixture restores the original live Field Of View through the normal owner transaction");
            RestoreAutosavedOwners();
            Check(PreferenceFilesMatchSnapshot(),"native settings fixture restores exact private preference-file bytes after autosave witness");

            for(int category=0;category<NativeFullSettingsController.Categories.Length;category++)
            {
                Send(RmlUiIntentKind.SettingsCategory,category);if(category==3)Send(RmlUiIntentKind.SettingsAction,216);Settle();
                Check(Read().Label("native_settings_title")==NativeFullSettingsController.Categories[category],"native category title: "+NativeFullSettingsController.Categories[category]);
                foreach(string id in new[]{"native_settings_title","native_settings_status","native_settings_back","native_settings_category_"+category})Contained(page.Document,id,"category "+category);
                var subpages=NativeFullSettingsPage.Subpages(category);
                for(int pane=0;pane<Math.Max(1,subpages.Length);pane++)
                {
                    if(subpages.Length>0){Send(RmlUiIntentKind.SettingsAction,232+pane);Reveal("native_settings_subpage_"+pane,false);Contained(page.Document,"native_settings_subpage_"+pane,"meaningful "+subpages[pane]+" subpage");}
                    var displayedRows=VisibleRows();
                    bool advanced=(bool)typeof(NativeFullSettingsPage).GetField("_advanced",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(page)!;
                    var canonicalRows=controller.Rows.Where(r=>r.Category==NativeFullSettingsController.Categories[category]
                        &&(subpages.Length==0||NativeFullSettingsPage.RowSubpage(r)==pane)&&!r.HudOnly
                        &&!NativeSettingsCatalog.HasVisualEditor(r)&&(advanced||!r.Advanced))
                        .OrderBy(NativeSettingsCatalog.Group,StringComparer.Ordinal).ToArray();
                    Check(displayedRows.SequenceEqual(canonicalRows),"native pane presents the complete canonical owner row order: "+category+"/"+pane);
                    Check(displayedRows.Length<=NativeFullSettingsPage.Slots,"complete natural form including Advanced fits bounded authored row slots: "+displayedRows.Length);
                    NativeFullSettingsPage.RequireRowCapacity(displayedRows.Length);
                    if(displayedRows.Length==0)continue;
                    var firstRow=displayedRows[0];
                    string first=firstRow.Choices.Length>0||firstRow.IsBinding?"native_settings_cycle_0":"native_settings_value_0";
                    Reveal(first,false);Contained(page.Document,first,"first readable sectioned form control");
                    Check(Read().Label(first).Contains(NativeSemanticReadback.Normalize(firstRow.Label),StringComparison.Ordinal),"first interactive control names actual owner setting");
                    foreach(string section in displayedRows.Select(NativeSettingsCatalog.Group).Distinct())Check(Read().ContainsText(section),"natural form retains section heading without extra navigation: "+section);
                    if(displayedRows.Length>10)
                    {
                        int last=displayedRows.Length-1;string id=displayedRows[last].Choices.Length>0||displayedRows[last].IsBinding?"native_settings_cycle_"+last:"native_settings_value_"+last;
                        Reveal(id,true);Contained(page.Document,id,"natural native scrolling reaches last of "+displayedRows.Length+" settings");
                        Check(Read().Label(id).Contains(NativeSemanticReadback.Normalize(displayedRows[last].Label),StringComparison.Ordinal),"last natural-form control retains owner name");Capture("settings-"+category+"-"+pane+"-scrolled");
                    }
                }
                if(subpages.Length>0)Send(RmlUiIntentKind.SettingsAction,232);
                if(category is 0 or 1 or 2 or 3 or 6 or 7)Capture("settings-"+NativeFullSettingsController.Categories[category].ToLowerInvariant());
            }
            Send(RmlUiIntentKind.SettingsCategory,3);Send(RmlUiIntentKind.SettingsAction,232);
            var allKeyboardRows=controller.Rows.Where(r=>r.Group=="Keyboard bindings").ToArray();
            var keyboardRows=VisibleRows().Where(r=>r.Group=="Keyboard bindings").ToArray();
            Check(keyboardRows.Length>=2,"Aim section exposes two keyboard bindings for native conflict capture");
            foreach(var row in allKeyboardRows)controller.Stage(row,"Unknown");
            controller.Stage(keyboardRows[0],"F1");controller.Stage(keyboardRows[1],"F2");
            Send(RmlUiIntentKind.SettingsAction,232);
            SendRow(keyboardRows[0]);Check(page.KeyDown(Keys.F2,default),"native keyboard capture receives actual key adapter input");Frame();
            Check(Read().Label("native_settings_confirm")=="SWAP"&&Read().ContainsText("REPLACE")&&Read().ContainsText("KEEP BOTH"),"native conflict offers explicit Swap Replace Keep Both and Cancel");
            Capture("settings-binding-conflict");ClickPopupIntent("native_settings_conflict_keep",RmlUiIntentKind.SettingsAction,214,"native Keep Both conflict action");
            Check(keyboardRows[0].Value=="F2"&&keyboardRows[1].Value=="F2"&&!controller.BindingConflict,"native Keep Both stages only the proposal");
            SendRow(keyboardRows[0]);Check(page.KeyDown(Keys.Delete,default),"native Delete clears a listening binding");Frame();
            Check(keyboardRows[0].Value=="Unknown"&&!controller.Capturing,"native clear ends capture with unbound draft");Send(RmlUiIntentKind.SettingsDiscard);
            var nativeLayout=controller.Rows.Single(r=>r.Label=="Control layout");string layoutBefore=nativeLayout.Value;
            Send(RmlUiIntentKind.SettingsAction,232+NativeFullSettingsPage.RowSubpage(nativeLayout));
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
                var controllerBinding=VisibleRows().First(r=>r.BindingAction!=null);
                SendRow(controllerBinding);Check(controller.ControllerCapturing,"connected native controller capture is pending before License covers Settings");
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
            foreach(var row in allKeyboardRows)controller.Stage(row,"Unknown");controller.Stage(keyboardRows[0],"F1");controller.Stage(keyboardRows[1],"F2");
            Send(RmlUiIntentKind.SettingsAction,232);SendRow(keyboardRows[0]);
            Check(page.KeyDown(Keys.F2,default)&&controller.BindingConflict,"native duplicate-key proposal is pending before cover");
            var conflictValues=controller.Rows.Select(r=>r.Value).ToArray();
            pages.OpenModal(new("settings-conflict-cover","pages/license/native-overview.rml","native_license_close"));Frame();
            Check(!controller.BindingConflict&&!controller.Capturing&&controller.Rows.Select(r=>r.Value).SequenceEqual(conflictValues),"cover cancels pending conflict without implicit Swap Replace or Keep Both");
            typeof(NativeFullSettingsController).GetField("_operation",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(controller,System.Threading.Tasks.Task.FromResult("Covered operation completed."));Frame();
            Check(!controller.Busy&&controller.Status=="Covered operation completed.","covered page still polls completed operations without sampling input");
            pages.CloseModal();Frame();Send(RmlUiIntentKind.SettingsDiscard);
            foreach(var item in controller.Rows.Select((row,index)=>(row,index)))
                if(item.row.Read()!=original[item.index])Check(controller.Stage(item.row,original[item.index]),"fixture can restore autosaved setting owner: "+item.row.Label);
            if(controller.Dirty)Check(controller.FlushAutoApply(),"fixture restores autosaved engine owners through the normal Settings transaction");
            RestoreAutosavedOwners();
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(original)
                &&PreferenceFilesMatchSnapshot(),"covered-input lifecycle restores all autosaved engine owners and exact preference bytes");
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
            int beforeVideoTrial=modes.Count;
            Check(controller.Apply()&&controller.ConfirmingVideo&&controller.VideoSeconds is >=14 and <=15&&modes.Count==beforeVideoTrial+1,
                "fake window preview enters fifteen-second confirmation without engine commit");
            Check(!controller.CanKeepVideo&&!controller.KeepVideo()&&controller.ConfirmingVideo,"Keep rejects a display proposal before a usable native frame");
            Frame();Check(controller.CanKeepVideo,"successful foreground native frame acknowledges display proposal");
            var videoDialog=pages.Top;Contained(videoDialog,"native_settings_video","display confirmation card");Contained(videoDialog,"native_settings_video_countdown","display confirmation countdown");
            Check(Read().Label("native_settings_video_countdown").Contains("Reverting in",StringComparison.Ordinal),"display confirmation countdown is readable native status text"); Capture("settings-video-proposal");
            var videoCover=pages.OpenModal(new("settings-video-cover","pages/license/native-overview.rml","native_license_close"));
            typeof(NativeFullSettingsController).GetField("_videoDeadline",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(controller,DateTime.UtcNow.AddMilliseconds(-1));
            Frame();Check(!controller.ConfirmingVideo&&modes.Count==beforeVideoTrial+2&&modes[^1]==LauncherPrefs.WindowMode&&pages.Top==videoCover,"expired fake-window proposal rolls back while another native modal covers Settings");
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
            Check(!page.CanNavigateAway&&controller.AutoApplyPending&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==liveHud,
                "resumed Settings owns the detached HUD draft and reports automatic save pending");
            Send(RmlUiIntentKind.SettingsDiscard);
            Check(controller.Rows.Select(r=>r.Read()).SequenceEqual(original)&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==liveHud,
                "discard after native HUD roundtrip preserves all engine settings and original live HUD");
            NativeSettingsVisualAcceptance.Run(host,pages,new MenuSettings(),RestoreAutosavedOwners);
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
            Check(PreferenceFilesMatchSnapshot(),"native Settings and HUD functional fixture changes no preference owner bytes");
            Check(GL.GetError()==ErrorCode.NoError,"native Settings and HUD frames leave no GL error");
            File.WriteAllText(Path.Combine(output,"settings-summary.json"),JsonSerializer.Serialize(new{passed=true,checks,width,height,density=Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_CHECK_DENSITY")??"1",scope,geometry},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally
        {
            try{hud?.Dispose();}
            finally{RestoreFixtureState();}
        }

        NativeSemanticReadback Read()=>NativeSemanticReadback.Read(host,pages.Top);
        bool PreferenceFilesMatchSnapshot()=>bytes.All(p=>p.Value==null?!File.Exists(p.Key):File.Exists(p.Key)&&File.ReadAllBytes(p.Key).SequenceEqual(p.Value));
        void RestoreFixtureState()
        {
            var failures=new List<Exception>();
            try
            {
                controller.Discard();
                if(HudProfileStore.Serialize(HudProfiles.CopyCurrent())!=liveHud)
                    HudProfiles.Publish(HudProfileStore.Parse(liveHud));
                foreach(var item in controller.Rows.Select((row,index)=>(row,index)))
                    controller.Stage(item.row,original[item.index]);
                if(controller.Dirty&&!controller.ConfirmingVideo&&!controller.FlushAutoApply())
                    throw new InvalidOperationException("Private Settings fixture could not restore its original runtime owner values.");
                if(!controller.Rows.Select(r=>r.Read()).SequenceEqual(original))
                    throw new InvalidOperationException("Private Settings fixture runtime owners differ from their original snapshot.");
                if(HudProfileStore.Serialize(HudProfiles.CopyCurrent())!=liveHud)
                    throw new InvalidOperationException("Private Settings fixture HUD owner differs from its original snapshot.");
            }
            catch(Exception ex){failures.Add(ex);}
            finally
            {
                try{RestoreRuntimeOwners();}catch(Exception ex){failures.Add(ex);}
                try{RestorePreferenceFiles();}catch(Exception ex){failures.Add(ex);}
            }
            if(!PreferenceFilesMatchSnapshot())
                failures.Add(new InvalidOperationException("Private Settings fixture preference bytes differ from their original snapshot."));
            if(!OwnerSnapshotsMatch())
                failures.Add(new InvalidOperationException("Private Settings fixture runtime owners differ from their original snapshot."));
            if(failures.Count>0)throw new AggregateException("Private Settings fixture cleanup did not restore every captured owner.",failures);
        }
        void RestoreAutosavedOwners()
        {
            RestoreRuntimeOwners(keepHudStoreIsolated:true);
            RestorePreferenceFiles();
            if(!OwnerSnapshotsMatch(expectIsolatedHudStore:true))
                throw new InvalidOperationException("Private Settings fixture could not restore every runtime owner after autosave.");
        }
        void RestoreRuntimeOwners(bool keepHudStoreIsolated=false)
        {
            var failures=new List<Exception>();
            void Attempt(Action restore){try{restore();}catch(Exception ex){failures.Add(ex);}}
            Attempt(()=>RestoreMenuSettings(settings,settingsBefore));
            if(gameSettingsBefore!=null)
            {
                Attempt(()=>RestoreMenuSettings(gameSettingsBefore,gameSettingsBytesBefore!));
                Attempt(()=>GameSettings.Apply(gameSettingsBefore));
            }
            Attempt(()=>HudProfiles.Publish(HudProfileStore.Parse(liveHud)));
            Attempt(()=>Features.Load(featuresBefore));
            Attempt(()=>RestoreRenderOptions(renderOptionsBefore));
            Attempt(()=>FrameTiming.FrameRateCap=frameRateCapBefore);
            Attempt(()=>Scene.Language=languageBefore);
            foreach(var item in mixerBefore)Attempt(()=>AudioMixer.SetVolume(item.Key,item.Value));
            Attempt(()=>Sfx.Volume=sfxVolumeBefore);Attempt(()=>Music.SetUserVolume(musicVolumeBefore));
            Attempt(()=>respawnHunterField.SetValue(null,respawnHunterBefore));
            Attempt(()=>respawnColorField.SetValue(null,respawnColorBefore));
            Attempt(restoreStylusPlacement);
            Attempt(()=>typeof(GameSettings).GetProperty(nameof(GameSettings.Current),BindingFlags.Public|BindingFlags.Static)!
                .GetSetMethod(true)!.Invoke(null,new object?[]{gameSettingsBefore}));
            Attempt(()=>hudStoreField.SetValue(null,keepHudStoreIsolated?isolatedHudStore:hudStoreBefore));
            if(failures.Count>0)throw new AggregateException("Could not restore captured native Settings runtime owners.",failures);
        }
        bool OwnerSnapshotsMatch(bool expectIsolatedHudStore=false)
        {
            return JsonSerializer.SerializeToUtf8Bytes(settings).SequenceEqual(settingsBefore)
                &&(gameSettingsBefore==null?GameSettings.Current==null:ReferenceEquals(GameSettings.Current,gameSettingsBefore)
                    &&JsonSerializer.SerializeToUtf8Bytes(gameSettingsBefore).SequenceEqual(gameSettingsBytesBefore!))
                &&featuresBefore.All(item=>Features.Commit().TryGetValue(item.Key,out string? value)&&value==item.Value)
                &&RenderOptionsMatch(renderOptionsBefore)&&FrameTiming.FrameRateCap==frameRateCapBefore
                &&Scene.Language==languageBefore&&mixerBefore.All(item=>AudioMixer.GetVolume(item.Key)==item.Value)
                &&Sfx.Volume==sfxVolumeBefore&&Music.UserVolume==musicVolumeBefore
                &&Equals(respawnHunterField.GetValue(null),respawnHunterBefore)&&Equals(respawnColorField.GetValue(null),respawnColorBefore)
                &&StylusZone.SavedLeft==stylusLeftBefore&&StylusZone.SavedTop==stylusTopBefore&&StylusZone.SavedWidth==stylusWidthBefore
                &&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==liveHud
                &&ReferenceEquals(hudStoreField.GetValue(null),expectIsolatedHudStore?isolatedHudStore:hudStoreBefore);
        }
        static void RestoreMenuSettings(MenuSettings destination,byte[] snapshot)
        {
            var saved=JsonSerializer.Deserialize<MenuSettings>(snapshot)??throw new InvalidOperationException("Could not read the fixture MenuSettings snapshot.");
            foreach(var property in typeof(MenuSettings).GetProperties(BindingFlags.Instance|BindingFlags.Public)
                .Where(property=>property.CanRead&&property.SetMethod!=null))
                property.SetValue(destination,property.GetValue(saved));
        }
        void RestorePreferenceFiles()
        {
            foreach(var item in bytes)
            {
                if(item.Value==null){if(File.Exists(item.Key))File.Delete(item.Key);}
                else{Directory.CreateDirectory(Path.GetDirectoryName(item.Key)!);File.WriteAllBytes(item.Key,item.Value);}
            }
        }
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
        void Send(RmlUiIntentKind kind,int argument=0)
        {
            Check(page.Handle(new RmlUiIntent(kind,argument,page.Document,sequence++)),"typed native Settings action "+kind+"/"+argument);Frame();
        }
        void SendHud(RmlUiIntentKind kind,int argument){Check(hud!.Handle(new(kind,argument,pages.Top,sequence++)),"typed native HUD action "+kind+"/"+argument);Frame();}
        void ClickIntent(string id,RmlUiIntentKind kind,int argument,string label)
        {
            Click(id);
            Check(host.TryTakeIntent(out var intent)&&intent.Kind==kind&&intent.Argument==argument&&intent.Document==page.Document,
                "native pointer emits "+label+" intent");
            Check(page.Handle(intent)&&!host.TryTakeIntent(out _),"native "+label+" intent opens the requested Settings view");Frame();
        }
        void Click(string id)
        {
            Check(host.FocusDocument(page.Document,id),"native focus reaches "+id);Settle();Contained(page.Document,id,"pointer target");
            host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h);
            host.DiscardIntents();host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
        }
        void ClickPopupIntent(string id,RmlUiIntentKind kind,int argument,string label)
        {
            var target=pages.Top;
            Check(target!=page.Document&&host.IsAlive(target),"native Settings confirmation owns a distinct foreground modal");
            Check(host.FocusDocument(target,id),"native popup focus reaches "+id);Settle();Contained(target,id,"popup pointer target");
            Check(host.TryGetElementBounds(target,id,out float x,out float y,out float w,out float h),"native popup exposes its actual action bounds: "+id);
            host.DiscardIntents();host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);
            host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
            Check(host.TryTakeIntent(out var intent)&&intent.Kind==kind&&intent.Argument==argument&&intent.Document==target,
                "native popup pointer emits its owner-bound "+label+" intent");
            Check(page.Handle(intent)&&!host.TryTakeIntent(out _),"native "+label+" intent resolves the actual Settings confirmation");Frame();
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

    private sealed record RenderOptionsSnapshot(
        int ResolutionScale,int FieldOfView,bool Lighting,bool BrightSkins,PlayerSkinStyle BrightSkinStyle,
        PlayerOutlineStyle PlayerOutline,int PlayerOutlineWidth,bool CelShading,bool ShowFps,bool SmoothNativeHud,
        int CelBands,float CelEdge,bool Fog,bool TextureFiltering,bool TextureMipmaps,int TextureAnisotropy,
        TextureSamplingMode TextureSampling,TextureUpscaleMode TextureUpscale,GraphicsPreset Preset,
        AntiAliasingMode AntiAliasing,int SharpenStrength,bool Bloom,int BloomIntensity,ColorGradeProfile ColorGrade,
        int Gamma,int Contrast,int Saturation,bool EnhancedLighting,bool AdvancedMaterials,bool DeferredPbr,
        ShadowQuality Shadows,AmbientOcclusionQuality AmbientOcclusion,bool ContactShadows,bool EnhancedFog,
        bool VolumetricFog,bool InternalHdr,bool Reflections,bool DynamicGlow,bool TextureReplacements,
        bool CharacterModelReplacements,TextureAssetQuality TextureQuality,bool ShowCustomCosmetics,
        MphRead.Mods.Cosmetics.CosmeticEffectQuality CosmeticQuality);

    private static RenderOptionsSnapshot CaptureRenderOptions()=>new(
        RenderOptions.ResolutionScale,RenderOptions.FieldOfView,RenderOptions.Lighting,RenderOptions.BrightSkins,
        RenderOptions.BrightSkinStyle,RenderOptions.PlayerOutline,RenderOptions.PlayerOutlineWidth,
        RenderOptions.CelShading,RenderOptions.ShowFps,RenderOptions.SmoothNativeHud,RenderOptions.CelBands,
        RenderOptions.CelEdge,RenderOptions.Fog,RenderOptions.TextureFiltering,RenderOptions.TextureMipmaps,
        RenderOptions.TextureAnisotropy,RenderOptions.TextureSampling,RenderOptions.TextureUpscale,RenderOptions.Preset,
        RenderOptions.AntiAliasing,RenderOptions.SharpenStrength,RenderOptions.Bloom,RenderOptions.BloomIntensity,
        RenderOptions.ColorGrade,RenderOptions.Gamma,RenderOptions.Contrast,RenderOptions.Saturation,
        RenderOptions.EnhancedLighting,RenderOptions.AdvancedMaterials,RenderOptions.DeferredPbr,RenderOptions.Shadows,
        RenderOptions.AmbientOcclusion,RenderOptions.ContactShadows,RenderOptions.EnhancedFog,RenderOptions.VolumetricFog,
        RenderOptions.InternalHdr,RenderOptions.Reflections,RenderOptions.DynamicGlow,RenderOptions.TextureReplacements,
        RenderOptions.CharacterModelReplacements,RenderOptions.TextureQuality,RenderOptions.ShowCustomCosmetics,
        RenderOptions.CosmeticQuality);

    private static bool RenderOptionsMatch(RenderOptionsSnapshot snapshot)=>CaptureRenderOptions()==snapshot;

    private static void RestoreRenderOptions(RenderOptionsSnapshot snapshot)
    {
        RenderOptions.ResolutionScale=snapshot.ResolutionScale;RenderOptions.FieldOfView=snapshot.FieldOfView;
        RenderOptions.Lighting=snapshot.Lighting;RenderOptions.BrightSkins=snapshot.BrightSkins;
        RenderOptions.BrightSkinStyle=snapshot.BrightSkinStyle;RenderOptions.PlayerOutline=snapshot.PlayerOutline;
        RenderOptions.PlayerOutlineWidth=snapshot.PlayerOutlineWidth;RenderOptions.CelShading=snapshot.CelShading;
        RenderOptions.ShowFps=snapshot.ShowFps;RenderOptions.SmoothNativeHud=snapshot.SmoothNativeHud;
        RenderOptions.CelBands=snapshot.CelBands;RenderOptions.CelEdge=snapshot.CelEdge;RenderOptions.Fog=snapshot.Fog;
        RenderOptions.TextureFiltering=snapshot.TextureFiltering;RenderOptions.TextureMipmaps=snapshot.TextureMipmaps;
        RenderOptions.TextureAnisotropy=snapshot.TextureAnisotropy;RenderOptions.TextureSampling=snapshot.TextureSampling;
        RenderOptions.TextureUpscale=snapshot.TextureUpscale;RenderOptions.Preset=snapshot.Preset;
        RenderOptions.AntiAliasing=snapshot.AntiAliasing;RenderOptions.SharpenStrength=snapshot.SharpenStrength;
        RenderOptions.Bloom=snapshot.Bloom;RenderOptions.BloomIntensity=snapshot.BloomIntensity;
        RenderOptions.ColorGrade=snapshot.ColorGrade;RenderOptions.Gamma=snapshot.Gamma;RenderOptions.Contrast=snapshot.Contrast;
        RenderOptions.Saturation=snapshot.Saturation;RenderOptions.EnhancedLighting=snapshot.EnhancedLighting;
        RenderOptions.AdvancedMaterials=snapshot.AdvancedMaterials;RenderOptions.DeferredPbr=snapshot.DeferredPbr;
        RenderOptions.Shadows=snapshot.Shadows;RenderOptions.AmbientOcclusion=snapshot.AmbientOcclusion;
        RenderOptions.ContactShadows=snapshot.ContactShadows;RenderOptions.EnhancedFog=snapshot.EnhancedFog;
        RenderOptions.VolumetricFog=snapshot.VolumetricFog;RenderOptions.InternalHdr=snapshot.InternalHdr;
        RenderOptions.Reflections=snapshot.Reflections;RenderOptions.DynamicGlow=snapshot.DynamicGlow;
        RenderOptions.TextureReplacements=snapshot.TextureReplacements;
        RenderOptions.CharacterModelReplacements=snapshot.CharacterModelReplacements;
        RenderOptions.TextureQuality=snapshot.TextureQuality;RenderOptions.ShowCustomCosmetics=snapshot.ShowCustomCosmetics;
        RenderOptions.CosmeticQuality=snapshot.CosmeticQuality;
    }
}
