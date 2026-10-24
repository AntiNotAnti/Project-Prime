using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Render.Hud;

internal static class NativeNestedAutoApplyAcceptance
{
    internal static void Run(RmlUiHost host, RmlUiPageManager pages, Action<string>? capture=null)
    {
        void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException("NESTED AUTO SETTINGS: " + label);
            Console.WriteLine("NESTED AUTO SETTINGS PASS " + label);
        }
        const BindingFlags fields = BindingFlags.NonPublic | BindingFlags.Instance;
        ulong sequence = 800000;
        string InlineId(NativeHudEditorPage page,string path)
        {
            var owner=(HudEditorController)typeof(NativeHudEditorPage).GetField("_controller",fields)!.GetValue(page)!;
            int row=owner.Snapshot().Properties.Select((property,index)=>(property,index)).Single(item=>item.property.Path==path).index;
            var presenter=(HudEditorPagePresenter)typeof(NativeHudEditorPage).GetField("_presenter",fields)!.GetValue(page)!;
            if(owner.Snapshot().Properties[row].Range!=null&&(int)typeof(HudEditorPagePresenter).GetField("_exactRow",fields)!.GetValue(presenter)! !=row)
                presenter.HandleIntent(new(RmlUiIntentKind.HudProperty,80+row,page.Document,sequence++));
            return "hud_property"+row+"_input";
        }
        var saved = new List<HudProfile>();
        var initial = HudProfileDefaults.Create("Project Prime");
        bool fail = false;
        int attempts=0;
        using (var page = new NativeHudEditorPage(host, pages, initial, asModal: true,
            autoSave: (profile, _) => { attempts++;if (fail) return false; saved.Add(profile.DeepClone()); return true; }))
        {
            page.Open(); host.Update();
            var controller = (HudEditorController)typeof(NativeHudEditorPage).GetField("_controller", fields)!.GetValue(page)!;
            void Intent(HudEditorAction action) => Check(page.Handle(new(RmlUiIntentKind.HudAction, (int)action, page.Document, sequence++)), "HUD action " + action);
            void Settle()
            {
                page.Refresh();
                typeof(NativeHudEditorPage).GetField("_lastEdit", fields)!.SetValue(page,
                    Stopwatch.GetTimestamp() - Stopwatch.Frequency);
                page.Refresh();
            }
            Check(host.FocusedElement() == "hud_cancel", "automatic HUD starts on Back without requiring Use or Apply");
            capture?.Invoke("hud-auto-apply");
            Intent(HudEditorAction.SectionLayout);
            host.SetField(page.Document, InlineId(page,"/elements/core.crosshair/scale"), "1.25");
            Settle();
            Check(saved.Count == 1 && saved[^1].Elements["core.crosshair"].Scale == 1.25f,
                "HUD numeric layout applies and saves after settled text without Apply Layout");
            for (int i = 0; i < 20; i++) page.Refresh();
            Check(saved.Count == 1, "unchanged HUD frames do not write profiles again");
            Intent(HudEditorAction.Undo); Settle();
            Check(saved.Count == 2 && saved[^1].Elements["core.crosshair"].Scale == initial.Elements["core.crosshair"].Scale,
                "automatic save retains undo history");
            Intent(HudEditorAction.Redo); Settle();
            Check(saved.Count == 3 && saved[^1].Elements["core.crosshair"].Scale == 1.25f, "automatic save retains redo history");
            host.SetField(page.Document, InlineId(page,"/elements/core.crosshair/scale"), "-");
            Settle();
            Check(!page.Closed && saved.Count == 3 && host.ReadField(page.Document, InlineId(page,"/elements/core.crosshair/scale")) == "-",
                "incomplete HUD text remains editable without publishing it");
            host.SetField(page.Document, InlineId(page,"/elements/core.crosshair/scale"), "1.5");
            fail = true; Settle();
            Check(saved.Count == 3 && !page.Closed, "failed automatic save keeps HUD open and retains edits");
            int failedAttempts=attempts;
            for (int i = 0; i < 20; i++) Settle();
            Check(saved.Count == 3&&attempts==failedAttempts, "failed HUD save never loops on every frame or quiet interval");
            fail = false;
            page.Back();
            Check(page.Closed && saved.Count == 4 && saved[^1].Elements["core.crosshair"].Scale == 1.5f,
                "Escape retries the retained HUD edit and returns directly to Settings");
        }
        int baseline = saved.Count;
        using (var page = new NativeHudEditorPage(host, pages, initial, asModal: true,
            autoSave: (profile, _) => { saved.Add(profile.DeepClone()); return true; }))
        {
            page.Open(); host.Update();
            page.Handle(new(RmlUiIntentKind.HudAction, (int)HudEditorAction.SectionLayout, page.Document, sequence++));
            host.SetField(page.Document, InlineId(page,"/elements/core.crosshair/scale"), "-");
            page.Back();
            Check(page.Closed && saved.Count == baseline, "Escape discards incomplete HUD text and returns without publishing malformed values");
        }
        baseline = saved.Count;
        using (var page = new NativeHudEditorPage(host, pages, initial, asModal: true,
            autoSave: (profile, _) => { saved.Add(profile.DeepClone()); return true; }))
        {
            page.Open(); host.Update();
            Check(page.Handle(new(RmlUiIntentKind.HudAction, (int)HudEditorAction.WorkspaceProfiles, page.Document, sequence++)),
                "profile workspace opens");
            host.Update();capture?.Invoke("hud-profiles-auto-apply");
            host.SetField(page.Document, "hud_name", "A typed profile name");
            for (int i = 0; i < 20; i++) page.Refresh();
            Check(host.ReadField(page.Document, "hud_name") == "A typed profile name", "HUD refresh never overwrites a name being typed");
            page.Back();
            Check(page.Closed && saved.Count == baseline + 1 && saved[^1].Name == "A typed profile name",
                "Escape flushes the final profile name once without an unsaved confirmation");
        }
        string savedOption="false";
        using (var page = new NativeHudEditorPage(host, pages, initial, asModal: true,
            options: new[] { new HudEditorOption("fixture", "Fixture HUD preference", "false", ImmutableArray.Create("false", "true")) },
            autoSave: (profile, options) => { saved.Add(profile.DeepClone()); savedOption=options[0].Value; return true; }))
        {
            page.Open(); host.Update();
            void Settle()
            {
                page.Refresh();
                typeof(NativeHudEditorPage).GetField("_lastEdit", fields)!.SetValue(page, Stopwatch.GetTimestamp()-Stopwatch.Frequency);
                page.Refresh();
            }
            void Search(string query)
            {
                host.SetField(page.Document,"hud_property_search",query);
                Check(page.Handle(new(RmlUiIntentKind.HudAction,(int)HudEditorAction.SearchProperties,page.Document,sequence++)),"search "+query);
            }
            Check(page.Handle(new(RmlUiIntentKind.HudAction,(int)HudEditorAction.SectionGlobal,page.Document,sequence++)),"open HUD-wide properties");
            Search("globalScale");
            host.SetField(page.Document,InlineId(page,"/globalScale"),"1.2");Settle();
            Check(saved[^1].GlobalScale==1.2f,"HUD property text applies and saves automatically without Apply Value");
            Search("Fixture HUD preference");
            host.SetField(page.Document,InlineId(page,"/options/fixture"),"true");Settle();
            Check(savedOption=="true","HUD-wide preference and profile share the automatic save boundary");
            Check(page.Handle(new(RmlUiIntentKind.HudElement,1,page.Document,sequence++)),"select health for pointer gesture");
            var controller=(HudEditorController)typeof(NativeHudEditorPage).GetField("_controller",fields)!.GetValue(page)!;
            page.SetCanvasSize(640,360);
            var center=controller.Bounds(1).Center;
            int writesBeforeDrag=saved.Count;
            Check(page.PointerDown(19,center.X,center.Y)&&page.PointerMove(19,center.X+12,center.Y,shift:true),"HUD pointer gesture edits the health placement");
            Settle();
            Check(saved.Count==writesBeforeDrag,"held HUD gesture never writes a whole profile");
            page.PointerUp(19);Settle();
            Check(saved.Count==writesBeforeDrag+1,"completed HUD gesture saves exactly once after settling");
            page.Back();
        }
        HudProfile? stagedAfterFailure=null;
        bool failRestore=true;int restoreAttempts=0;
        using(var page=new NativeHudEditorPage(host,pages,initial,asModal:true,
            autoSave:(profile,_)=>{restoreAttempts++;stagedAfterFailure=profile.DeepClone();return !failRestore;}))
        {
            page.Open();host.Update();
            page.Handle(new(RmlUiIntentKind.HudAction,(int)HudEditorAction.SectionLayout,page.Document,sequence++));
            host.SetField(page.Document,InlineId(page,"/elements/core.crosshair/scale"),"1.5");page.Refresh();
            typeof(NativeHudEditorPage).GetField("_lastEdit",fields)!.SetValue(page,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
            page.Refresh();
            Check(restoreAttempts==1&&stagedAfterFailure?.Elements["core.crosshair"].Scale==1.5f,"failed HUD save retains a parent draft for retry");
            page.Handle(new(RmlUiIntentKind.HudAction,(int)HudEditorAction.Undo,page.Document,sequence++));
            failRestore=false;page.Back();
            Check(page.Closed&&restoreAttempts==2&&stagedAfterFailure?.Elements["core.crosshair"].Scale==initial.Elements["core.crosshair"].Scale,
                "undo after failed save rebases the parent draft even when the HUD matches its last saved value");
        }
        HudInlineGlAcceptance.Run(host,pages,capture);
        NativeVisualAutoApplyAcceptance.Run(host,pages);
    }
}

// This fixture alone commits preferences; its caller runs in an explicit private
// user-data process. Runtime publication and files are the real outer transaction.
internal static class NativeVisualAutoApplyAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages)
    {
        string root=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")??"";
        if(!Path.IsPathFullyQualified(root)||Path.GetFullPath(LauncherPrefs.Directory)!=Path.GetFullPath(root)
            ||Path.GetFullPath(Directory.GetCurrentDirectory())!=Path.GetFullPath(root))
            throw new InvalidOperationException("Visual automatic-save fixture requires private user data.");
        HudProfiles.Load(Path.Combine(root,"Savedata","hud-profiles"));
        void Check(bool value,string label)
        {
            if(!value)throw new InvalidOperationException("VISUAL AUTO SETTINGS: "+label);
            Console.WriteLine("VISUAL AUTO SETTINGS PASS "+label);
        }
        using var controller=new NativeFullSettingsController(new MenuSettings()){AutoApply=true};
        var retained=pages.Top;
        ulong sequence=850000;
        void Quiet(NativeSettingsVisualEditor editor)
        {
            typeof(NativeFullSettingsController).GetField("_autoApplyChangedAt",BindingFlags.NonPublic|BindingFlags.Instance)!
                .SetValue(controller,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
            editor.Present();host.Update();
        }
        var color=controller.Rows.First(row=>row.Group=="Outline colors");
        using(var editor=new NativeSettingsVisualEditor(host,pages,controller,2))
        {
            host.Update();
            host.SetField(pages.Top,"settings_visual_color","#123456");editor.Present();
            Check(color.Dirty&&color.Read()!="#123456","outline hex stays detached until the quiet-time commit");
            Quiet(editor);
            Check(!controller.Dirty&&color.Read()=="#123456","outline hex automatically publishes the real palette and saved preference"
                +$" // value={color.Value}, read={color.Read()}, saved={color.Saved}, dirty={controller.Dirty}, failed={controller.AutoApplyFailed}, pending={controller.AutoApplyPending}, blocked={controller.AutoApplyBlocked}, status={controller.Status}");
            Check(File.Exists(Path.Combine(root,"controls.txt"))&&File.Exists(Path.Combine(root,"launcher.txt")),
                "visual automatic save uses the real private outer preference transaction");
            host.SetField(pages.Top,"settings_visual_color","#12");editor.Present();Quiet(editor);
            Check(color.Read()=="#123456"&&!controller.Dirty,"incomplete outline hex never publishes or becomes a saved draft");
            editor.Back();
            Check(editor.Closed&&pages.Top==retained&&color.Read()=="#123456","Escape discards incomplete hex and keeps the accepted palette");
        }
        using(var editor=new NativeSettingsVisualEditor(host,pages,controller,2))
        {
            host.Update();
            string before=color.Read();
            Check(host.TryGetElementBounds(pages.Top,"settings_visual_wheel",out float x,out float y,out float w,out float h)&&w>0&&h>0,
                "automatic outline wheel has real native pointer geometry");
            Check(editor.PointerDown(x+w*.9f,y+h/2)&&editor.PointerMove(x+w/2,y+h*.9f),"outline wheel stages a continuous pointer edit");
            Quiet(editor);
            Check(color.Dirty&&color.Read()==before,"held outline gesture remains detached beyond the automatic save interval");
            editor.ReleasePointer();Quiet(editor);
            string accepted=color.Read();
            Check(!controller.Dirty&&accepted!=before,"outline release automatically saves the completed gesture");
            editor.Back();
            Check(editor.Closed&&color.Read()==accepted,"Escape never restores the old palette after an automatic save");
        }
        foreach(int kind in new[]{0,1})
        {
            var row=controller.Rows.First(r=>r.Group==(kind==0?"Touch layout":"Stylus guide")&&(kind==0||r.Label=="Guide position / width"));
            string before=row.Read();
            using var editor=new NativeSettingsVisualEditor(host,pages,controller,kind);
            host.Update();
            Check(editor.Handle(new(RmlUiIntentKind.SettingsAction,227,pages.Top,sequence++)),"resize visual layout "+kind);
            Quiet(editor);
            string accepted=row.Read();
            Check(!controller.Dirty&&accepted!=before,"settled visual layout automatically saves through the outer controller "+kind);
            editor.Back();
            Check(editor.Closed&&pages.Top==retained&&row.Read()==accepted,"Escape retains the saved touch/stylus layout "+kind);
        }
    }
}
