using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

if(args.Length!=2)throw new ArgumentException("Supply the native bridge and complete asset root.");
nint module=NativeLibrary.Load(Path.GetFullPath(args[0]));
NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
int checks=0;
void Check(bool value,string name){if(!value)throw new InvalidOperationException(name);checks++;Console.WriteLine("PASS "+name);}
foreach(var size in new[]{(1280,720,2f),(640,320,1f),(640,360,1f),(2560,1440,2f),(1024,768,1f)})
{
    using var host=new RmlUiHost();
    Check(host.Initialize(size.Item1,size.Item2,size.Item3,Path.GetFullPath(args[1]),RmlUiRenderBackend.DrawList),"actual Settings layout initializes");
    var doc=host.OpenDocument("pages/settings/settings.rml",RmlUiDocumentLayer.Page);
    foreach(string id in new[]{"settings_hud_tools","settings_controller_tools","settings_profile_tools","settings_system","settings_maintenance","settings_credits","settings_restart"})
        host.SetBool(doc,"visible:"+id,false);
    for(int group=0;group<192;group++)host.SetBool(doc,"visible:settings_group_"+group,group==0);
    host.SetText(doc,"settings_group_0","General");
    host.SetText(doc,"settings_status","Changes are applied together when you choose Apply.");
    host.SetText(doc,"settings_paging","Page 1 of 2");
    for(int row=0;row<12;row++)
    {
        host.SetText(doc,"settings_label_"+row,"Field of view");
        host.SetText(doc,"settings_help_"+row,"60 to 120");
        host.SetField(doc,"settings_value_"+row,"78");
    }
    host.Update();
    (float X,float Y,float W,float H) Bounds(string id)
    {
        Check(host.TryGetElementBounds(doc,id,out float x,out float y,out float w,out float h)&&w>0&&h>0,"Settings positive geometry "+id);
        return(x,y,w,h);
    }
    bool Inside((float X,float Y,float W,float H) child,(float X,float Y,float W,float H) parent)=>
        child.X>=parent.X-1&&child.Y>=parent.Y-1&&child.X+child.W<=parent.X+parent.W+1&&child.Y+child.H<=parent.Y+parent.H+1;
    var workspace=Bounds("settings_workspace");var top=Bounds("topbar");var footer=Bounds("footerbar");
    Check(workspace.Y>=top.Y+top.H-1&&workspace.Y+workspace.H<=footer.Y+1,"Settings workspace stays between fixed chrome");
    var navigation=Bounds("settings_navigation");var content=Bounds("settings_content");
    Check(Inside(navigation,workspace)&&Inside(content,workspace),"Settings independent scroll viewports fit panel border");
    void FocusVisible(string id,string viewport)
    {
        Check(host.FocusDocument(doc,id),"Settings actual focus "+id);host.Update();
        Check(host.FocusedElement()==id&&Inside(Bounds(id),Bounds(viewport)),"Settings focus scrolls entire control into viewport "+id);
        var b=Bounds(id);host.Input.PointerMoved(b.X+b.W/2,b.Y+b.H/2);host.Update();
        Check(host.HoveredElement()==id,"Settings visible control receives actual pointer "+id);
    }
    string[] categories={"display","graphics","audio","controls","replays","profile","system","maintenance","credits","hud","controller","touch","online"};
    for(int category=0;category<categories.Length;category++)
    {
        string id="settings_"+categories[category]+"_tab";FocusVisible(id,"settings_navigation");
        host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
        Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.SettingsCategory&&intent.Argument==category,"Settings real typed category "+category);
    }
    FocusVisible("settings_back","settings_workspace");
    foreach(string id in new[]{"settings_search","settings_search_button","settings_clear_search","settings_value_0","settings_cycle_0","settings_value_11","settings_cycle_11","settings_previous","settings_next","settings_revert_category","settings_discard","settings_apply"})
        FocusVisible(id,id.StartsWith("settings_value_")||id.StartsWith("settings_cycle_")?"settings_fields":"settings_content");
    host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
    Check(host.TryTakeIntent(out var apply)&&apply.Kind==RmlUiIntentKind.SettingsApply,"Settings actual focused Apply emits typed command");
    var actionBounds=Bounds("settings_actions");var contentBounds=Bounds("settings_content");
    Console.WriteLine($"LAYOUT {size}: actions{actionBounds}, content{contentBounds}");
    Check(Inside(actionBounds,contentBounds),"Settings commit action row stays inside panel when focused");
}
Console.WriteLine($"PASS {checks} actual Settings compact/sidebar/field/action layout assertions; no settings service or persistence invoked");
