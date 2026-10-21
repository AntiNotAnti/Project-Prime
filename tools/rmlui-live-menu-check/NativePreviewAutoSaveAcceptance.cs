using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Render.Hud;

internal static class NativePreviewAutoSaveAcceptance
{
    internal static void Run(string root,MenuSettings settings)
    {
        void Check(bool pass,string label)
        {if(!pass)throw new InvalidOperationException("PREVIEW AUTO SAVE: "+label);Console.WriteLine("PREVIEW AUTO SAVE PASS "+label);}
        using var controller=new NativeFullSettingsController(settings){AutoApply=true};
        var fov=controller.Rows.Single(row=>row.Label=="Field Of View");
        string original=fov.Read();
        controller.Stage(fov,original=="90"?"91":"90");
        var draft=controller.Rows.Select(row=>row.Value).ToArray();
        var hud=controller.CopyHud();hud.SafeArea=.125f;
        var options=controller.CopyHudOptions().Select(option=>option.Label switch
        {
            "Stacked dual-screen layout" or "Stylus input mode" => option with {Value="true"},
            "Screen proportions (4:3 / 5:3)" => option with {Value="5:3"},
            _=>option
        }).ToArray();
        var files=Directory.EnumerateFiles(root,"*",SearchOption.AllDirectories).ToDictionary(path=>path,File.ReadAllBytes);
        string savedHud=HudProfileStore.Serialize(HudProfiles.CopyCurrent());
        bool beforeStack=DsLayout.Enabled,beforeStylus=PointerInput.StylusMode;string beforeAspect=DsLayout.ScreenAspect;
        int resumed=0;
        try
        {
            SettingsPreview.Begin(()=>controller.ApplyHudPreview(hud,options),()=>{resumed++;controller.ResumeAfterPreview();});
            Check(SettingsPreview.Active&&SettingsPreview.Remaining is >0 and <=60,"preview begins with a sixty-second deadline");
            Check(DsLayout.Enabled&&PointerInput.StylusMode&&DsLayout.ScreenAspect=="5:3"&&DsLayout.Active
                &&HudProfiles.CopyCurrent().SafeArea==.125f&&fov.Read()==fov.Value, $"HUD override and layout options publish only to live runtime: stack={DsLayout.Enabled}, stylus={PointerInput.StylusMode}, aspect={DsLayout.ScreenAspect}, safe={HudProfiles.CopyCurrent().SafeArea}, fov={fov.Read()}/{fov.Value}");
            Check(controller.Rows.Select(row=>row.Value).SequenceEqual(draft),"HUD trial does not replace the parent's pending values");
            typeof(NativeFullSettingsController).GetField("_autoApplyChangedAt",BindingFlags.NonPublic|BindingFlags.Instance)!
                .SetValue(controller,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
            for(int i=0;i<20;i++)controller.TickAutoApply();
            Check(files.All(file=>File.ReadAllBytes(file.Key).SequenceEqual(file.Value)),"active preview blocks automatic preference writes");
            typeof(SettingsPreview).GetField("_started",BindingFlags.Static|BindingFlags.NonPublic)!
                .SetValue(null,Stopwatch.GetTimestamp()-Stopwatch.Frequency*61L);
            SettingsPreview.Tick();
            Check(!SettingsPreview.Active&&resumed==1&&DsLayout.Enabled==beforeStack&&PointerInput.StylusMode==beforeStylus
                &&DsLayout.ScreenAspect==beforeAspect&&HudProfileStore.Serialize(HudProfiles.CopyCurrent())==savedHud&&fov.Read()==original,
                "timeout restores exact runtime HUD, stylus and stacked layout values");
            typeof(NativeFullSettingsController).GetField("_autoApplyChangedAt",BindingFlags.NonPublic|BindingFlags.Instance)!
                .SetValue(controller,Stopwatch.GetTimestamp()-Stopwatch.Frequency);
            controller.TickAutoApply();
            Check(fov.Read()==original&&controller.Rows.Select(row=>row.Value).SequenceEqual(draft)
                &&files.All(file=>File.ReadAllBytes(file.Key).SequenceEqual(file.Value)),"return keeps previewed drafts detached even after the quiet-time interval");
            Check(controller.FlushAutoApply()&&!controller.Dirty,"explicit Back/navigation save can accept the previewed values");
        }
        finally{SettingsPreview.End();controller.Discard();}
    }
}
