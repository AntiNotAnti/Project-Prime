using System;
using System.Collections.Generic;
using System.Reflection;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
namespace MphRead.Mods.Launcher.Gui;
internal sealed partial class SettingsView
{
    private bool _previewEditorDraft;
    private void PreviewHud(HudProfile profile)
    {
        var previous=_hudDraft;var color=_stylusGuideColor;
        try { _previewEditorDraft=true; _hudDraft=profile; _stylusGuideColor=profile.AccentColor; PreviewDraft(); }
        finally { _previewEditorDraft=false; _hudDraft=previous; _stylusGuideColor=color; }
    }
    private void PreviewDraft()
    {
        if (!_inGame || SettingsPreview.Active) return;
        var hud=HudProfiles.CopyCurrent();
        var restore=new List<Action> { Mods.Input.StylusZone.CapturePlacement() };
        var touch=Mods.Input.TouchSettings.CopyLayout();
        restore.Add(()=>Mods.Input.TouchSettings.ReplaceLayout(touch));
        var instances=Mods.Input.TouchSettings.CopyInstances();
        restore.Add(()=>Mods.Input.TouchSettings.ReplaceInstances(instances));
        var outlines=OutlineColorPalette.CopyCurrent();
        restore.Add(()=>OutlineColorPalette.Publish(outlines));
        foreach(var cue in Enum.GetValues<Mods.Sound.CombatFeedbackCue>())
        {
            var selected=Mods.Sound.CombatFeedbackAudio.GetSelection(cue);
            restore.Add(()=>Mods.Sound.CombatFeedbackAudio.SetSelection(cue,selected));
        }
        // Snapshot only presentation/input preference owners at the edit boundary.
        // Never capture simulation, scene, player or network state.
        void Capture(Type type, object? target)
        {
            var flags=BindingFlags.Public | (target==null?BindingFlags.Static:BindingFlags.Instance);
            foreach(var p in type.GetProperties(flags))
                if(p.GetIndexParameters().Length==0 && p.GetMethod?.IsPublic==true && p.SetMethod?.IsPublic==true
                    && (p.PropertyType.IsValueType || p.PropertyType==typeof(string)))
                { var value=p.GetValue(target); restore.Add(()=>p.SetValue(target,value)); }
            foreach(var f in type.GetFields(flags))
                if(!f.IsInitOnly && !f.IsLiteral && (f.FieldType.IsValueType || f.FieldType==typeof(string)))
                { var value=f.GetValue(target); restore.Add(()=>f.SetValue(target,value)); }
        }
        Capture(typeof(MenuSettings),_settings);
        foreach(var type in new[]{typeof(RenderOptions),typeof(FrameTiming),typeof(Features),typeof(Radar),
            typeof(InputSettings),typeof(Mods.Input.PointerInput),typeof(Mods.Input.DsLayout),
            typeof(Mods.Input.StylusZone),typeof(Mods.Input.TouchSettings),typeof(LauncherPrefs),typeof(Mods.Network.DemoClip)})
            Capture(type,null);
        void Restore()
        {
            foreach(var action in restore) action();
            Mods.GameSettings.Apply(_settings);
            HudProfiles.Publish(hud);
            if(_players!=null)InputSettings.ApplyToPlayers(_players); else InputSettings.ApplyToPlayers();
        }
        try
        {
            SettingsPreview.Begin(() =>
            {
                try { Commit(preview:true); }
                catch { Restore(); throw; }
                return Restore;
            }, () => { });
        }
        catch(Exception ex) { _saveError.Text="Preview failed: "+ex.Message; _saveError.IsVisible=true; }
    }
}
