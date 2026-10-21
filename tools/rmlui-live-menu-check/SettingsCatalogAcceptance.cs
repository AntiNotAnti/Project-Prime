using System;
using System.IO;
using System.Linq;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Pages.Settings;

internal static class SettingsCatalogAcceptance
{
    internal static void Run(NativeFullSettingsController controller)
    {
        void Check(bool value,string message)
        {if(!value)throw new InvalidOperationException("SETTINGS CATALOG: "+message);Console.WriteLine("SETTINGS CATALOG PASS "+message);}
        Check(NativeFullSettingsPage.Subpages(3).SequenceEqual(new[]{"Aim","Movement","Weapons","Actions","Interface","Controller","Touch","Stylus"}),
            "Controls exposes focused action and device sections");
        Check(controller.Rows.All(row=>
        {
            int category=Array.IndexOf(NativeFullSettingsController.Categories,row.Category),tab=NativeSettingsCatalog.Tab(row);
            return category>=0&&tab>=0&&(NativeSettingsCatalog.Tabs(category).Length==0?tab==0:tab<NativeSettingsCatalog.Tabs(category).Length);
        }),"every canonical setting maps to exactly one valid section");
        var movement=controller.Rows.Where(r=>r.Group=="Keyboard bindings"&&(r.Label.StartsWith("Move ",StringComparison.Ordinal)||r.Label is "Jump" or "Crouch" or "Boost" or "Morph")).ToArray();
        Check(movement.Length>=8&&movement.All(r=>NativeSettingsCatalog.Tab(r)==1),"all keyboard movement actions share Movement");
        Check(controller.Rows.Where(r=>r.BindingAction is PadAction.Jump or PadAction.Crouch or PadAction.Morph).All(r=>NativeSettingsCatalog.Tab(r)==1),
            "controller movement alternatives join the same Movement section");
        foreach(string label in new[]{"Shoot","Zoom","Custom weapon slot","Custom weapon reload","Custom weapon optic","Next weapon","Prev weapon","Power beam"})
            Check(controller.Rows.Any(r=>r.Group=="Keyboard bindings"&&r.Label==label&&NativeSettingsCatalog.Tab(r)==2),"Weapons contains "+label);
        Check(controller.Rows.Where(r=>r.Group=="Keyboard bindings").All(r=>r.KeyboardBinding),"presentation keeps canonical mouse capture and conflict groups intact");
        Check(controller.Rows.Where(r=>r.Category=="Controls"&&r.Label is "Chat Key" or "Clip Key").All(r=>NativeSettingsCatalog.Tab(r)==4),
            "chat and clip shortcuts live in Interface");
        Check(controller.Rows.Where(r=>r.Group=="Outline colors"||r.Group=="Touch layout"||r.Group=="Stylus guide"&&r.Label=="Guide position / width")
            .All(NativeSettingsCatalog.HasVisualEditor),"duplicate raw placement and palette fields route to visual editors");
        Check(controller.Rows.Where(r=>r.Group=="Custom scope (all aim devices)").Count()==3
            &&controller.Rows.Where(r=>r.Group=="Custom scope (all aim devices)").All(r=>NativeSettingsCatalog.Tab(r)==0),
            "shared custom-scope settings remain distinct and discoverable under Aim");
        string markup=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"rmlui","pages","settings","native-full.rml"));
        Check(!markup.Contains("id=\"native_settings_apply\"",StringComparison.Ordinal)
            &&!markup.Contains(">DISCARD<",StringComparison.Ordinal)&&!markup.Contains("settings:reset-category",StringComparison.Ordinal),
            "autosave UI removes redundant Apply, Discard and category draft reset");
        Check(markup.Contains("id=\"native_settings_restore_unsaved\"",StringComparison.Ordinal),"save failures retain an explicit unsaved recovery control");
        Check(Enumerable.Range(0,NativeSettingsCatalog.TabSlots).All(i=>markup.Contains("id=\"native_settings_subpage_"+i+"\"",StringComparison.Ordinal)),
            "every authored semantic section has a native control");
    }
}
