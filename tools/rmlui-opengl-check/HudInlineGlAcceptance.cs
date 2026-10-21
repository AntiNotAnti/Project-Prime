using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Render.Hud;

internal static class HudInlineGlAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,Action<string>? capture=null)
    {
        void Check(bool value,string label)
        {
            if(!value)throw new InvalidOperationException("HUD INLINE GL: "+label);
            Console.WriteLine("HUD INLINE GL PASS "+label);
        }
        const BindingFlags fields=BindingFlags.NonPublic|BindingFlags.Instance;
        int writes=0;
        using var page=new NativeHudEditorPage(host,pages,HudProfileDefaults.Create("Project Prime"),asModal:true,
            options:new[]{new HudEditorOption("ds","Screen proportions","4:3",ImmutableArray.Create("4:3","5:3"),Section:1,Group:"Stacked dual screens")},
            autoSave:(_,_)=>{writes++;return true;});
        page.Open();host.Update();
        var owner=(HudEditorController)typeof(NativeHudEditorPage).GetField("_controller",fields)!.GetValue(page)!;
        var presenter=(HudEditorPagePresenter)typeof(NativeHudEditorPage).GetField("_presenter",fields)!.GetValue(page)!;
        void Refresh(){page.Refresh();host.Update();}
        void Settle()
        {
            Refresh();typeof(NativeHudEditorPage).GetField("_lastEdit",fields)!.SetValue(page,Stopwatch.GetTimestamp()-Stopwatch.Frequency);Refresh();
        }
        void Activate(string id)
        {
            Check(host.FocusDocument(page.Document,id),"focus native inline control "+id);
            host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
            Check(host.TryTakeIntent(out var intent)&&page.Handle(intent),"native inline control emits its stable row intent "+id);
            Check(!host.TryTakeIntent(out _),"one inline activation emits one intent");Refresh();
        }
        void Search(string query)
        {
            host.SetField(page.Document,"hud_property_search",query);Activate("hud_search");
        }
        capture?.Invoke("hud-inline-appearance");
        int choice=owner.Snapshot().Properties.Select((row,index)=>(row,index)).First(item=>!item.row.Choices.IsEmpty).index;
        string initial=owner.Snapshot().Properties[choice].Value;
        Activate("hud_property"+choice+"_next");
        Check(owner.Snapshot().Properties[choice].Value!=initial,"choice arrows edit their own row directly");
        Activate("hud_property"+choice+"_previous");
        Check(owner.Snapshot().Properties[choice].Value==initial,"previous arrow reverses the same row choice");
        Search("/crosshair/lengthX");
        Check(owner.Snapshot().Properties.Length==1,"Appearance search remains within its component section");
        const string input="hud_property0_input";
        Check(host.TryGetElementBounds(page.Document,"hud_property0_label",out float lx,out float ly,out float lw,out float lh)
            &&host.TryGetElementBounds(page.Document,input,out float ix,out float iy,out float iw,out float ih)&&lw>0&&lh>0&&iw>0&&ih>0
            &&(ix>=lx+lw-1||iy>=ly+lh-1),"the editable value sits beside its own label without overlap");
        host.FocusDocument(page.Document,input);host.SetField(page.Document,input,"5.");
        for(int i=0;i<20;i++)presenter.Present();
        Check(host.ReadField(page.Document,input)=="5."&&host.FocusedElement()==input,"native field text and focus survive repeated HUD refreshes");
        host.SetField(page.Document,input,"5.5");Settle();
        Check(owner.CopyDraft().Crosshair.LengthX==5.5f&&writes>0,"settled inline text automatically updates and saves the intended property");
        Activate("hud_property0_reset");
        Check(owner.CopyDraft().Crosshair.LengthX==HudProfileDefaults.Create("Project Prime").Crosshair.LengthX,"native per-row Reset restores that exact value without a bottom editor");
        host.SetField(page.Document,input,"5.5");Settle();
        int beforeInvalid=writes;host.SetField(page.Document,input,"unfinished");Settle();
        Check(writes==beforeInvalid&&host.ReadField(page.Document,input)=="unfinished","invalid inline text stays editable without changing saved values");
        Activate("hud_section_layout");
        Check(owner.Snapshot().InspectorSection==1&&owner.CopyDraft().Crosshair.LengthX==5.5f,
            "section navigation drops unfinished text while preserving accepted edits");
        capture?.Invoke("hud-inline-position");Search("Screen proportions");
        Check(owner.Snapshot().Properties.Length==1&&owner.Snapshot().Properties[0].Path=="/options/ds",
            "dual-screen proportions appear only in the Position section");
        Activate("hud_property0_next");Check(owner.CopyOptions()[0].Value=="5:3","dual-screen row exposes its 5:3 option inline");
        Activate("hud_property0_previous");Check(owner.CopyOptions()[0].Value=="4:3","dual-screen row returns directly to 4:3");
        Activate("hud_section_decorations");
        Check(owner.Snapshot().Properties.All(row=>row.Path.StartsWith("/decorations/"))
            &&owner.Snapshot().Properties.All(row=>!row.Label.StartsWith("Decorations",StringComparison.OrdinalIgnoreCase)),
            "Decorations shows its own artwork values with short labels");
        capture?.Invoke("hud-inline-decorations");
        int numeric=owner.Snapshot().Properties.Select((row,index)=>(row,index)).First(item=>item.row.Kind=="Number").index;
        host.SetField(page.Document,"hud_property"+numeric+"_input","unfinished");
        Activate("hud_property_next");
        Check(owner.Snapshot().PropertyPage==1&&owner.Snapshot().Error.Length==0,"page navigation discards only unfinished fields and shows the next row identities");
        page.Back();Check(page.Closed,"Escape returns from the inline HUD editor after flushing accepted values");
    }
}
