using System.Diagnostics;
using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Render.Hud;

// Real native controls/documents and temporary runtime publication. A controlled
// resume callback substitutes only the live window/scene lease; the separate
// live-window qualification covers the production gameplay owner.
internal static class NativeLiveSettingsPreviewAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,int width,int height,Action<string>? capture=null)
    {
        const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
        string root=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")??"";
        if(!Path.IsPathFullyQualified(root)||Path.GetFullPath(Directory.GetCurrentDirectory())!=Path.GetFullPath(root))
            throw new InvalidOperationException("Live-preview fixture requires an isolated working directory.");
        var retained=pages.Top;
        void Check(bool value,string why)
        {if(!value)throw new InvalidOperationException("LIVE SETTINGS PREVIEW: "+why);Console.WriteLine("LIVE SETTINGS PREVIEW PASS "+why);}
        T Owner<T>(object page,string name)=>(T)page.GetType().GetField(name,fields)!.GetValue(page)!;
        var stores=new[]{Path.Combine(root,"launcher.txt"),Path.Combine(root,"controls.txt"),Path.Combine(root,"Savedata","settings.json"),Path.Combine(root,"Savedata","hud-profiles","active.json"),Path.Combine(root,"Savedata","hud-profiles","active.json.bak")};
        Dictionary<string,byte[]?> Files()=>stores.ToDictionary(path=>path,path=>File.Exists(path)?File.ReadAllBytes(path):null);
        bool SameFiles(Dictionary<string,byte[]?> before)=>before.All(item=>item.Value==null?!File.Exists(item.Key):File.Exists(item.Key)&&File.ReadAllBytes(item.Key).AsSpan().SequenceEqual(item.Value));
        void Settle(Action refresh,int ms=45)
        {var watch=Stopwatch.StartNew();do{refresh();host.Update();Thread.Sleep(3);}while(watch.ElapsedMilliseconds<ms);}
        void PointClick(RmlUiDocumentToken document,string id)
        {
            Check(host.TryGetElementBounds(document,id,out float x,out float y,out float w,out float h)&&w>0&&h>0,"native preview control has layout: "+id);
            Check(x>=-1&&y>=-1&&x+w<=width+1&&y+h<=height+1,"native preview control fits the viewport: "+id);
            host.DiscardIntents();host.Input.PointerMoved(x+w/2,y+h/2);host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);host.Update();
        }
        void MainClick(NativeFullSettingsPage page,string id)
        {
            Check(host.FocusDocument(page.Document,id),"native settings focus reaches "+id);Settle(page.Refresh);
            PointClick(page.Document,id);
            Check(host.TryTakeIntent(out var intent)&&intent.Document==page.Document&&page.Handle(intent),"actual native settings click dispatches "+id);
            host.Update();
        }
        void HudClick(NativeHudEditorPage page,string id)
        {
            Check(host.FocusDocument(page.Document,id),"native HUD focus reaches "+id);Settle(page.Refresh);
            PointClick(page.Document,id);
            Check(host.TryTakeIntent(out var intent)&&intent.Document==page.Document&&page.Handle(intent),"actual native HUD click dispatches "+id);
            host.Update();
        }
        void Expire()
        {
            typeof(SettingsPreview).GetField("_started",BindingFlags.Static|BindingFlags.NonPublic)!
                .SetValue(null,Stopwatch.GetTimestamp()-Stopwatch.Frequency*(SettingsPreview.DurationSeconds+1L));
            SettingsPreview.Tick();
        }
        using(var unavailable=new NativeFullSettingsPage(host,pages,new MenuSettings(),asModal:true,canPreview:false))
        {
            unavailable.Open();Settle(unavailable.Refresh);
            Check(NativeSemanticReadback.Read(host,unavailable.Document).ContainsText("PREVIEW"),"launcher settings explain live preview availability");
            PointClick(unavailable.Document,"native_settings_preview");
            Check(!host.TryTakeIntent(out _)&&!unavailable.TryTakePreview()&&!SettingsPreview.Active,"launcher preview is disabled without a playable scene");
        }
        using(var page=new NativeFullSettingsPage(host,pages,new MenuSettings(),asModal:true,canPreview:true))
        {
            page.Open();Settle(page.Refresh);MainClick(page,"native_settings_category_0");MainClick(page,"native_settings_subpage_0");
            var controller=Owner<NativeFullSettingsController>(page,"_controller");
            var visible=Owner<NativeSettingRow[]>(page,"_visible");int index=Array.FindIndex(visible,row=>row.Label=="Field Of View");
            Check(index>=0,"live-preview fixture reaches the real Field of View owner");
            var row=visible[index];string original=row.Read();int originalFov=RenderOptions.FieldOfView;string raw=original=="91"?"092":"091";
            string field="native_settings_value_"+index;
            Check(host.FocusDocument(page.Document,field),"native scroll reveals the FOV precision input before editing");Settle(page.Refresh);
            host.SetField(page.Document,field,raw);
            MainClick(page,"native_settings_preview");
            Check(page.TryTakePreview()&&!page.TryTakePreview(),"main preview request is consumed exactly once");
            var token=page.Document;string focus=host.FocusedElement();
            host.TryGetElementBounds(token,field,out float x,out float y,out float w,out float h);
            var files=Files();int resumed=0;
            void Resume(){resumed++;Check(page.SetPreviewVisible(true),"resume shows the retained Settings document");}
            for(int pass=0;pass<2;pass++)
            {
                SettingsPreview.Begin(page.ApplyPreview,Resume);
                try
                {
                Check(page.SetPreviewVisible(false),"live trial hides Settings without retiring its document");
                typeof(NativeFullSettingsController).GetField("_autoApplyChangedAt",fields)!.SetValue(controller,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
                Settle(page.Refresh);
                Check(SettingsPreview.Active&&SettingsPreview.Remaining is >0 and <=60&&host.IsAlive(token)&&!host.IsVisible(token)
                    &&row.Read()==raw&&RenderOptions.FieldOfView==int.Parse(raw),"sixty-second trial publishes real FOV while retaining hidden Settings");
                Check(SameFiles(files)&&host.ReadField(token,field)==raw,"hidden trial never autosaves or overwrites raw numeric input");
                if(pass==0)Check(PauseMenu.HandleEscape(null!),"production Escape preview gate consumes Escape before normal pause handling");else Expire();
                Settle(page.Refresh,800);
                Check(!SettingsPreview.Active&&resumed==pass+1&&page.Document==token&&page.OwnsForeground&&row.Read()==original&&RenderOptions.FieldOfView==originalFov&&SameFiles(files),"Escape/timeout restores runtime and the exact Settings document without persistence");
                Check(host.ReadField(token,field)==raw&&host.FocusedElement()==focus,"resume preserves the pending numeric text and focused control");
                Check(host.TryGetElementBounds(token,field,out float rx,out float ry,out float rw,out float rh)
                    &&Math.Abs(rx-x)<1&&Math.Abs(ry-y)<1&&Math.Abs(rw-w)<1&&Math.Abs(rh-h)<1,"resume preserves native form scroll and control geometry");
                }
                finally{SettingsPreview.End();}
            }
            capture?.Invoke("live-preview-settings-return");
            controller.Discard();page.Refresh();
        }
        using(var parent=new NativeFullSettingsPage(host,pages,new MenuSettings(),asModal:true,canPreview:true))
        {
            parent.Open();Settle(parent.Refresh);
            var parentController=Owner<NativeFullSettingsController>(parent,"_controller");
            var parentValues=parentController.Rows.Select(row=>row.Value).ToArray();
            string parentDraft=HudProfileStore.Serialize(parentController.CopyHud());
            var original=HudProfiles.CopyCurrent();string baseline=HudProfileStore.Serialize(original);int writes=0;
            using var hud=new NativeHudEditorPage(host,pages,original,asModal:true,options:parent.CopyHudOptions(),
                autoSave:(_,_)=>{writes++;return true;},canPreview:true);
            hud.Open();Settle(hud.Refresh);HudClick(hud,"hud_section_layout");
            var owner=Owner<HudEditorController>(hud,"_controller");
            var first=owner.Snapshot().Properties.Take(3).ToArray();
            Check(first.Any(property=>property.Label.Contains("Stacked dual-screen layout",StringComparison.OrdinalIgnoreCase))
                &&first.Any(property=>property.Label.Contains("Screen proportions",StringComparison.OrdinalIgnoreCase))
                &&first.Any(property=>property.Label.Contains("Stylus input mode",StringComparison.OrdinalIgnoreCase)),
                "Position shows all three real stacked-screen master controls before other settings");
            capture?.Invoke("hud-dual-screen-controls");
            for(int master=0;master<first.Length;master++)
            {
                host.SetField(hud.Document,"hud_property_search",first[master].Label.Split(" · ").Last());HudClick(hud,"hud_search");
                Check(owner.Snapshot().Properties.Length==1&&owner.Snapshot().Properties[0].Path==first[master].Path,
                    "section search reaches real stacked-screen master "+master);
                Check(host.FocusDocument(hud.Document,"hud_property0_input"),"native input reaches stacked-screen master "+master);Settle(hud.Refresh);
                capture?.Invoke("hud-dual-screen-control-"+master);
            }
            host.SetField(hud.Document,"hud_property_search","/elements/core.crosshair/scale");HudClick(hud,"hud_search");
            int scaleIndex=Array.FindIndex(owner.Snapshot().Properties.ToArray(),property=>property.Path=="/elements/core.crosshair/scale");
            Check(scaleIndex>=0,"live preview reaches the inline selected-element scale property");
            string scaleField="hud_property"+scaleIndex+"_input";
            Check(host.FocusDocument(hud.Document,scaleField),"native scroll reveals the inline HUD scale field");Settle(hud.Refresh);
            host.SetField(hud.Document,scaleField,"1.275");
            HudClick(hud,"hud_live_preview");
            Check(hud.TryTakePreview(out var proposal,out var options)&&proposal.Elements["core.crosshair"].Scale==1.275f,
                "native HUD preview captures pending numeric layout directly from its real field");
            Check(!hud.TryTakePreview(out _,out _)&&writes==0,"HUD preview request neither repeats nor invokes autosave");
            var token=hud.Document;string focus=host.FocusedElement();var files=Files();int resumed=0;
            for(int pass=0;pass<2;pass++)
            {
                SettingsPreview.Begin(()=>parent.ApplyHudPreview(proposal,options),()=>{resumed++;Check(hud.SetPreviewVisible(true),"resume shows the retained HUD document");});
                try
                {
                Check(hud.SetPreviewVisible(false),"live HUD trial hides the same native editor document");
                typeof(NativeHudEditorPage).GetField("_lastEdit",fields)!.SetValue(hud,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
                Settle(hud.Refresh);
                Check(SettingsPreview.Active&&HudProfiles.CopyCurrent().Elements["core.crosshair"].Scale==1.275f&&host.IsAlive(token)&&writes==0&&SameFiles(files),
                    "HUD trial publishes the actual runtime profile while hidden autosave remains suspended");
                if(pass==0)Check(PauseMenu.HandleEscape(null!),"Escape returns from a HUD trial through the production preview gate");else Expire();
                Settle(hud.Refresh,800);
                Check(!SettingsPreview.Active&&resumed==pass+1&&hud.Document==token&&hud.OwnsForeground&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==baseline&&writes==0&&SameFiles(files),
                    "HUD Escape/timeout restores runtime without losing editor identity or forcing a save");
                Check(parentController.Rows.Select(row=>row.Value).SequenceEqual(parentValues)&&HudProfileStore.Serialize(parentController.CopyHud())==parentDraft,
                    "HUD trial never leaks its proposal into the parent's retained settings draft");
                Check(host.ReadField(token,scaleField)=="1.275"&&host.FocusedElement()==focus,"HUD return preserves the exact pending layout field and focus");
                }
                finally{SettingsPreview.End();}
            }
            capture?.Invoke("live-preview-hud-return");
        }
        Check(pages.Top==retained&&host.IsAlive(retained),"all preview fixtures return to the original retained workspace");
    }
}
