using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Pages.Hud;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;
using MphRead.Mods.Render.Hud;

internal sealed partial class ReplayWindowAcceptance
{
    private void PlanOwnedHudQualification()
    {
        NativeFullSettingsPage? settings=null;NativeFullSettingsController? controller=null;NativeHudEditorPage? hud=null;
        Dictionary<string,byte[]?>? bytes=null;Dictionary<NativeSettingRow,string>? live=null;string originalHud="",beforeNudge="";
        DoHeader("open actual Settings for owned-asset HUD qualification",()=>Activate("header_settings"));
        Wait("owned HUD parent Settings is foreground",()=>
        {
            settings=ReadStatic<NativeFullSettingsPage>("_fullSettings");controller=Read<NativeFullSettingsController>(settings,"_controller");
            if(!HeaderWindowFocused()||settings?.OwnsForeground!=true||controller==null)return false;
            string[] paths={Path.Combine(LauncherPrefs.Directory,"launcher.txt"),Path.Combine(LauncherPrefs.Directory,"controls.txt"),Path.GetFullPath(Path.Combine("Savedata","settings.json")),Path.Combine(LauncherPrefs.Directory,"Savedata","hud-profiles","active.json")};
            bytes=paths.ToDictionary(p=>p,p=>File.Exists(p)?File.ReadAllBytes(p):null);live=controller.Rows.ToDictionary(r=>r,r=>r.Read());originalHud=HudProfileStore.Serialize(HudProfiles.CopyCurrent());return true;
        });
        DoHeader("open owned HUD through actual native Edit HUD button",()=>Activate("native_settings_hud"));
        Wait("owned HUD wrapper presents generated asset bitmap and complete layout",()=>
        {
            hud=ReadStatic<NativeHudEditorPage>("_hudEditor");if(!HeaderWindowFocused()||hud?.OwnsForeground!=true)return false;
            var owner=Read<HudEditorController>(hud,"_controller");if(owner==null)return false;
            string directory=Path.Combine(LauncherPrefs.Directory,"rmlui-hud-preview-cache");
            var semantic=NativeSemanticReadback.Read(Host!,hud.Document);
            if(!Directory.Exists(directory)||!Directory.EnumerateFiles(directory,"*.tga").Any()||semantic.Label("hud_preview_warning").Length!=0)return false;
            Require(owner.Snapshot().Elements.Length==14&&owner.Snapshot().Error.Length==0,"owned native HUD exposes all fourteen elements without controller error");
            for(int i=0;i<14;i++)Require(Host!.TryGetElementBounds(hud.Document,"hud_element"+i,out _,out _,out float w,out float h)&&w>0&&h>0,"owned HUD element has real layout "+i);
            Require(Host!.TryGetElementBounds(hud.Document,"hud_preview",out _,out _,out float pw,out float ph)&&pw>0&&ph>0,"owned native HUD generated preview is laid out");return true;
        },45000);
        WaitHeaderFrames(3,"owned HUD publishes successful preview after asynchronous completion",()=>
        {
            Require(hud?.OwnsForeground==true&&NativeSemanticReadback.Read(Host!,hud.Document).Label("hud_preview_warning").Length==0,"owned HUD preview completion has no missing-ROM or asset warning");
        });
        Do("capture actual owned HUD bitmap and layout",()=>Capture("owned-hud-generated-preview"));
        // Crosshair (the initial selection) is intentionally centered and cannot be nudged.
        DoHeader("select movable health element through actual native HUD picker",()=>Activate("hud_pick1"));
        Wait("owned HUD health element selection is current",()=>hud?.OwnsForeground==true&&Read<HudEditorController>(hud,"_controller")?.Snapshot().Selected==1);
        DoHeader("unlock detached HUD elements through native toolbar",()=>Activate("hud_unlock_all"));
        Wait("owned HUD health element is movable",()=>hud?.OwnsForeground==true&&Read<HudEditorController>(hud,"_controller")?.Snapshot().Elements[1].Locked==false);
        DoHeader("nudge actual native HUD element",()=>{beforeNudge=Read<HudEditorController>(hud,"_controller")!.Snapshot().ProfileJson;Activate("hud_nudge_right");});
        Wait("owned HUD nudge remains detached",()=>
        {
            if(hud?.CanNavigateAway!=false||Read<HudEditorController>(hud,"_controller")!.Snapshot().ProfileJson==beforeNudge)return false;
            Require(HudProfileStore.Serialize(HudProfiles.CopyCurrent())==originalHud,"owned HUD nudge preserves live profile");return true;
        });
        DoHeader("return owned HUD draft through native Use in Settings",()=>Activate("hud_use"));
        Wait("owned HUD returns a guarded parent Settings draft",()=>settings?.OwnsForeground==true&&!settings.CanNavigateAway&&ReadStatic<NativeHudEditorPage>("_hudEditor")==null);
        DoHeader("discard owned HUD parent draft",()=>PressSettingsSemanticButton("DISCARD"));
        Wait("owned HUD discard preserves all runtime and persisted preferences",()=>
        {
            if(settings?.CanNavigateAway!=true)return false;
            Require(live!.All(p=>p.Key.Read()==p.Value)&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==originalHud&&bytes!.All(p=>p.Value==null?!File.Exists(p.Key):File.Exists(p.Key)&&File.ReadAllBytes(p.Key).AsSpan().SequenceEqual(p.Value)),"owned HUD edit/use/discard changes no runtime owners or preference bytes");return true;
        });
        PlanHeaderTransition(MainHeaderRoutes[0],"Home after owned native HUD qualification");
    }
}
