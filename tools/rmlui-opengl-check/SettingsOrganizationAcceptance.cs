using System;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using OpenTK.Windowing.GraphicsLibraryFramework;

internal static class SettingsOrganizationAcceptance
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,MenuSettings settings,Action<string>? capture=null,int width=0,int height=0)
    {
        void Check(bool value,string label)
        {if(!value)throw new InvalidOperationException("SETTINGS ORGANIZATION: "+label);Console.WriteLine("SETTINGS ORGANIZATION PASS "+label);}
        var retained=pages.Top;
        using var page=new NativeFullSettingsPage(host,pages,settings,asModal:true);
        page.Open();host.Update();page.Refresh();host.Update();
        var type=typeof(NativeFullSettingsPage);
        T Get<T>(string name)=>(T)type.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(page)!;
        var controller=Get<NativeFullSettingsController>("_controller");
        ulong sequence=900000;
        void Send(RmlUiIntentKind kind,int argument)
        {Check(page.Handle(new(kind,argument,page.Document,sequence++)),"route "+kind+" / "+argument);host.Update();page.Refresh();host.Update();}
        bool HasBounds(string id)=>host.TryGetElementBounds(page.Document,id,out _,out _,out float w,out float h)&&w>0&&h>0;
        void Capture(string name)
        {
            if(width>0&&height>0)
            {
                Check(host.TryGetElementBounds(page.Document,"native_settings_back",out float x,out float y,out float w,out float h)
                    &&w>0&&h>0&&x>=-1&&y>=-1&&x+w<=width+1&&y+h<=height+1,
                    "Back stays inside the viewport on "+name);
            }
            capture?.Invoke("settings-"+name);
        }
        void Activate(string id)
        {
            Check(host.FocusDocument(page.Document,id),"focus destination "+id);
            host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
            Check(host.TryTakeIntent(out var intent)&&page.Handle(intent),"native destination action "+id);
            page.Refresh();host.Update();
        }
        Check(!HasBounds("native_settings_hud"),"HUD Studio is absent from the Settings overview toolbar");
        Activate("native_settings_overview_0");
        Check(Get<bool>("_categoryLanding")&&HasBounds("native_settings_hud"),"Display exposes HUD Studio as a main destination");
        Check(host.TryGetElementBounds(page.Document,"native_settings_hud",out _,out _,out float hudWidth,out float hudHeight)
            &&hudWidth>=120&&hudHeight>=60,"HUD Studio has a prominent Display card hit area");
        Capture("display-hud-studio");
        Activate("native_settings_hud");
        Check(page.TryTakeOpenHud(out var hudDraft)&&hudDraft.Elements.Count==15,"Display HUD Studio card hands off a complete HUD draft");
        Check(page.Back()&&Get<bool>("_overview"),"Display returns to the Settings overview after the detached HUD request");
        page.Refresh();host.Update();
        Check(Get<bool>("_overview")&&HasBounds("native_settings_overview_3"),"Settings opens a real native overview");
        Capture("overview");
        Send(RmlUiIntentKind.SettingsCategory,3);
        Check(Get<bool>("_categoryLanding")&&Get<NativeSettingRow[]>("_visible").Length==0
            &&HasBounds("native_settings_subpage_1"),"Controls first exposes focused section choices");
        Capture("controls-sections");
        Send(RmlUiIntentKind.SettingsAction,233);
        var movement=Get<NativeSettingRow[]>("_visible");
        Check(movement.Any(r=>r.Label=="Move up")&&movement.Any(r=>r.Label=="Move down")
            &&movement.All(r=>NativeSettingsCatalog.Tab(r)==1)&&!movement.Any(r=>r.Label=="Shoot"),
            "Movement contains movement binds across devices without weapon clutter");
        Check(HasBounds("native_settings_row_0")&&HasBounds("native_settings_back"),"section rows and Back have native geometry");
        Check(host.TryGetElementBounds(page.Document,"native_settings_advanced",out _,out float advancedY,out _,out float advancedH)
            &&host.TryGetElementBounds(page.Document,"native_settings_device",out _,out float deviceY,out _,out float deviceH)
            &&host.TryGetElementBounds(page.Document,"native_settings_subpages",out _,out float tabsY,out _,out _)
            &&advancedH>0&&deviceH>0&&advancedY+advancedH<=deviceY+1&&deviceY+deviceH<=tabsY+1,
            "Advanced options, controller selector and section tabs never overlap");
        Capture("movement");
        int captureIndex=Array.FindIndex(movement,r=>r.Label=="Move down");
        Send(RmlUiIntentKind.SettingsAction,32+captureIndex);
        Check(controller.Capturing&&pages.Top!=page.Document,"rebinding opens an actual focus-trapping modal");
        Check(page.KeyDown(Keys.Escape,0)&&!controller.Capturing&&!Get<bool>("_categoryLanding")&&!page.Closed,
            "first Escape cancels binding capture and keeps the Movement screen");
        host.Update();
        Check(page.KeyDown(Keys.Escape,0)&&Get<bool>("_categoryLanding")&&!page.Closed,
            "second Escape returns to Controls section choices");
        host.Update();
        Check(page.KeyDown(Keys.Escape,0)&&Get<bool>("_overview")&&!page.Closed,
            "third Escape returns to the Settings overview");
        Send(RmlUiIntentKind.SettingsCategory,3);Send(RmlUiIntentKind.SettingsAction,232);
        var aim=Get<NativeSettingRow[]>("_visible");
        int sensitivity=Array.FindIndex(aim,r=>r.Label=="Mouse Sensitivity");
        Capture("aim");
        string value=aim[sensitivity].Value;
        Check(!HasBounds("native_settings_restore_unsaved"),"successful autosave has no redundant draft restore button");
        controller.AutoApply=false;
        host.SetField(page.Document,"native_settings_value_"+sensitivity,value=="2"?"1":"2");page.Refresh();host.Update();
        typeof(NativeFullSettingsController).GetField("<AutoApplyFailed>k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(controller,true);
        typeof(NativeFullSettingsController).GetProperty("Status",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(controller,"Could not save: diagnostic write failure. Unsaved changes retained.");
        page.Refresh();host.Update();
        Check(controller.Dirty&&HasBounds("native_settings_restore_unsaved"),"save failure offers explicit recovery without dropping pending edits");
        Capture("save-failure-recovery");
        Send(RmlUiIntentKind.SettingsDiscard,0);
        Check(!controller.Dirty&&!controller.AutoApplyFailed&&!HasBounds("native_settings_restore_unsaved")&&aim[sensitivity].Value==value,
            "Restore Unsaved Changes clears failed draft and returns to saved values");
        controller.AutoApply=false;
        Check(controller.Stage(aim[sensitivity],value=="2"?"1":"2"),"stage a detached valid edit before calibration");
        controller.CalibrationAction(26);
        Check(controller.AutoApplyBlocked&&page.KeyDown(Keys.Escape,0)&&!controller.AutoApplyBlocked
            &&controller.Dirty&&!Get<bool>("_categoryLanding"),
            "Escape cancels innermost calibration and preserves pending valid edits");
        Send(RmlUiIntentKind.SettingsDiscard,0);controller.AutoApply=true;page.Refresh();host.Update();
        host.SetField(page.Document,"native_settings_value_"+sensitivity,"unfinished");
        Check(page.KeyDown(Keys.Escape,0)&&Get<bool>("_categoryLanding")&&aim[sensitivity].Value==value&&!controller.Dirty,
            "Escape discards an invalid unfinished number and still backs out one screen");
        Send(RmlUiIntentKind.SettingsAction,232);
        host.SetField(page.Document,"native_settings_value_"+sensitivity,"unfinished");
        Send(RmlUiIntentKind.SettingsClose,0);
        Check(Get<bool>("_categoryLanding")&&aim[sensitivity].Value==value&&!controller.Dirty,
            "visible Back also discards an unfinished invalid number and returns one screen");
        page.Back();host.Update();
        Check(page.KeyDown(Keys.Escape,0)&&page.Closed,"Escape closes Settings only after returning to overview");
        page.Dispose();
        Check(pages.Top==retained&&host.IsAlive(retained),"closing settings restores the exact retained workspace");
    }
}
