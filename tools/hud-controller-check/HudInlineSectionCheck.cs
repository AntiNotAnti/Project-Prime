using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render.Hud;

internal static class HudInlineSectionCheck
{
    internal static void Run(Action<bool,string> check)
    {
        using var controller=new HudEditorController(HudProfileDefaults.Create("Project Prime"),new HudBoundary(),new[]
        {
            new HudEditorOption("ds","Screen proportions","4:3",ImmutableArray.Create("4:3","5:3"),Section:1,Group:"Stacked dual screens"),
            new HudEditorOption("stack","Stacked dual-screen layout","false",ImmutableArray.Create("false","true"),Section:1,Group:"Stacked dual screens"),
            new HudEditorOption("stylus","Stylus input mode","false",ImmutableArray.Create("false","true"),Section:1,Group:"Stacked dual screens"),
            new HudEditorOption("fps","Show FPS","off",ImmutableArray.Create("off","on"))
        });
        controller.SelectElement(1);
        List<HudEditorProperty> Section(HudEditorAction action)
        {
            controller.Dispatch(action);var rows=new List<HudEditorProperty>();
            do
            {
                var state=controller.Snapshot();rows.AddRange(state.Properties);
                if(state.PropertyPage+1>=state.PropertyPageCount)break;
                controller.Dispatch(HudEditorAction.PropertyNext);
            }while(true);
            return rows;
        }
        var appearance=Section(HudEditorAction.SectionAppearance);
        check(appearance.All(row=>row.Path.StartsWith("/health/")||row.Path is "/smoothArtwork" or "/haloLowHealthSound" or "/elements/core.health/color" or "/elements/core.health/opacity"),
            "Appearance exposes only the selected component styling and artwork smoothing");
        var layout=Section(HudEditorAction.SectionLayout);
        controller.Dispatch(HudEditorAction.SectionLayout);
        check(controller.Snapshot().Properties.Take(3).Select(row=>row.Path).SequenceEqual(new[]{"/options/ds","/options/stack","/options/stylus"})
            &&layout.Select(row=>row.Path).Distinct().Count()==layout.Count,"all three stacked-screen controls share the first Position page without duplicate rows");
        check(layout.Any(row=>row.Path=="/options/ds")&&layout.All(row=>row.Path=="/independentGauges"||row.Path is "/options/ds" or "/options/stack" or "/options/stylus"
            ||row.Path.StartsWith("/elements/core.health/")&&!row.Path.EndsWith("/color")&&!row.Path.EndsWith("/opacity")),
            "Position contains geometry and matching dual-screen layout options without appearance values");
        var global=Section(HudEditorAction.SectionGlobal);
        check(global.Any(row=>row.Path=="/options/fps")&&!global.Any(row=>row.Path=="/options/ds"||row.Path is "/smoothArtwork" or "/textScale" or "/independentGauges"),
            "HUD-wide options never duplicate layout, artwork or lettering controls");
        var lettering=Section(HudEditorAction.SectionTypography);
        check(lettering.All(row=>row.Path.StartsWith("/typography/")||row.Path=="/textScale"),"Lettering contains fonts and text scale only");
        check(!lettering.Any(row=>row.Path=="/typography/paletteDefaultsVersion")&&lettering.All(row=>!row.Label.Contains("styles",StringComparison.OrdinalIgnoreCase)
            &&!row.Label.Contains("core.",StringComparison.Ordinal)),"Lettering uses friendly component names and hides migration metadata");
        controller.Dispatch(HudEditorAction.SearchProperties,"/typography/styles/core.health/font");
        var fontRow=controller.Snapshot().Properties.Single();
        check(fontRow.Value==HudFonts.All[0].Name&&fontRow.Choices.SequenceEqual(HudFonts.All.Select(font=>font.Name)),
            "typography font rows display registered names as their value and choices");
        check(controller.CyclePropertyRow(0,false)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[1].Id
            &&controller.CyclePropertyRow(0,true)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[0].Id,
            "font choice arrows map friendly names back to canonical profile ids in both directions");
        check(controller.ApplyPropertyRow(0,HudFonts.All[3].Name)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[3].Id,
            "friendly font names are accepted and stored as canonical ids");
        string fontProfile=controller.Snapshot().ProfileJson;
        check(!controller.ApplyPropertyRow(0,"Not a registered font")&&controller.Snapshot().ProfileJson==fontProfile,
            "unknown font names are rejected without changing profile data");
        check(controller.CyclePropertyRow(0,true)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[2].Id,
            "previous font choice uses the friendly-name chooser");
        check(controller.ApplyPropertyRow(0,HudFonts.All[1].Id)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[1].Id,
            "existing fontNN ids remain accepted as edit aliases");
        controller.Dispatch(HudEditorAction.Undo);
        check(controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[2].Id,"font edits retain authoring undo");
        controller.Dispatch(HudEditorAction.Redo);
        check(controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[1].Id,"font edits retain authoring redo");
        check(controller.ResetPropertyRow(0)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[0].Id
            &&controller.Snapshot().Properties[0].Value==HudFonts.All[0].Name,"font reset restores the canonical default and friendly display value");
        controller.Dispatch(HudEditorAction.SearchProperties,"/typography/globalFont");
        check(controller.Snapshot().Properties.Single().Value==HudFonts.All[0].Name
            &&controller.ApplyPropertyRow(0,HudFonts.All[2].Name)&&controller.CopyDraft().Typography.GlobalFont==HudFonts.All[2].Id,
            "shared typography font rows use the same friendly-name mapping");
        var fontRoundTrip=HudProfileStore.Parse(HudProfileStore.Serialize(controller.CopyDraft()));
        check(fontRoundTrip.Typography.Get("core.health").Font==HudFonts.All[0].Id
            &&fontRoundTrip.Typography.GlobalFont==HudFonts.All[2].Id,"profile serialization round-trips canonical font ids");
        var decorations=Section(HudEditorAction.SectionDecorations);
        check(decorations.All(row=>row.Path.StartsWith("/decorations/"))&&decorations.All(row=>!row.Label.StartsWith("Decorations",StringComparison.OrdinalIgnoreCase)
            &&!row.Label.Contains("styles",StringComparison.OrdinalIgnoreCase)),"decoration labels name the artwork without internal decorations/styles prefixes");
        check(decorations.First(row=>row.Path.EndsWith("/matchPalette")).Label.EndsWith("Match palette"),"inline labels use normal word case after the artwork name");
        controller.Dispatch(HudEditorAction.SearchProperties,"Show FPS");
        check(controller.Snapshot().Properties.IsEmpty,"section search cannot leak unrelated HUD-wide settings into Decorations");
        controller.Dispatch(HudEditorAction.SectionAppearance);controller.Dispatch(HudEditorAction.SearchProperties,"/elements/core.health/opacity");
        check(controller.Snapshot().Properties.Length==1&&controller.Snapshot().Properties[0].Label=="Opacity","element row labels omit redundant element paths");
        string before=controller.Snapshot().ProfileJson;
        check(!controller.ApplyPropertyRow(0,"2")&&controller.Snapshot().ProfileJson==before,"inline numeric validation rejects out-of-range edits atomically");
        check(controller.ApplyPropertyRow(0,"0.4")&&controller.CopyDraft().Elements["core.health"].Opacity==.4f,"inline numeric editing reaches the selected property");
        controller.Dispatch(HudEditorAction.Undo);
        check(controller.CopyDraft().Elements["core.health"].Opacity==1,"inline values preserve authoring undo");
        controller.Dispatch(HudEditorAction.Redo);
        check(controller.CopyDraft().Elements["core.health"].Opacity==.4f,"inline values preserve authoring redo");
        check(controller.ResetPropertyRow(0)&&controller.CopyDraft().Elements["core.health"].Opacity==1,"row reset restores only its canonical property");
        controller.Dispatch(HudEditorAction.SectionLayout);controller.Dispatch(HudEditorAction.SearchProperties,"Screen proportions");
        check(controller.CyclePropertyRow(0,false)&&controller.CopyOptions()[0].Value=="5:3"
            &&controller.CyclePropertyRow(0,true)&&controller.CopyOptions()[0].Value=="4:3","inline option arrows navigate both directions using the intended row");
        controller.SelectElement(0);
        check(Section(HudEditorAction.SectionAppearance).Any(row=>row.Path=="/nativeReticleOpacity"),"native reticle opacity belongs to Crosshair Appearance");
        controller.Dispatch(HudEditorAction.SectionGlobal);controller.Dispatch(HudEditorAction.SearchProperties,"nativeReticleOpacity");
        check(controller.Snapshot().Properties.IsEmpty,"HUD-wide search never duplicates native reticle opacity");
        controller.SelectElement(1);controller.Dispatch(HudEditorAction.SearchProperties,"nativeReticleOpacity");
        check(controller.Snapshot().Properties.IsEmpty,"other element appearance sections exclude native reticle opacity");
        controller.SelectElement(4);
        check(Section(HudEditorAction.SectionAppearance).Count(row=>row.Path is "/radarBackground" or "/radarOutlines")==2,
            "radar appearance owns its background and outline toggles");
        controller.SelectElement(1);
        check(Section(HudEditorAction.SectionAppearance).Any(row=>row.Path=="/haloLowHealthSound"),"health appearance owns its installed-character low-health cue");
        controller.SelectElement(12);
        check(Section(HudEditorAction.SectionAppearance).Any(row=>row.Path=="/haloLowHealthSound"),"health gauge appearance shares its low-health cue");
        controller.Dispatch(HudEditorAction.SectionGlobal);controller.Dispatch(HudEditorAction.SearchProperties,"haloLowHealthSound");
        check(controller.Snapshot().Properties.IsEmpty,"HUD-wide search excludes the health-specific alert");
        controller.Dispatch(HudEditorAction.SectionDecorations);controller.Dispatch(HudEditorAction.SearchProperties,"/decorations/styles/borders/opacity");
        check(controller.ApplyPropertyRow(0,"0.4")&&controller.ResetPropertyRow(0)
            &&controller.CopyDraft().Decorations.Get("borders").Opacity==1,"decoration row reset uses validated defaults with populated style entries");
        controller.Dispatch(HudEditorAction.SectionTypography);controller.Dispatch(HudEditorAction.SearchProperties,"/typography/styles/core.health/size");
        check(controller.ApplyPropertyRow(0,"2")&&controller.ResetPropertyRow(0)
            &&controller.CopyDraft().Typography.Get("core.health").Size==1,"lettering row reset uses validated defaults with populated style entries");
        controller.SelectElement(0);controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/opacity");
        check(controller.ApplyPropertyRow(0,"0.7"),"parent reticle part has an authored opacity to inherit");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/color");
        check(controller.ApplyPropertyRow(0,"#EE8844"),"parent reticle part has an authored color to inherit");
        controller.Dispatch(HudEditorAction.NextCrosshairTarget);controller.Dispatch(HudEditorAction.ToggleCrosshairOverride);
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/color");
        check(controller.ApplyPropertyRow(0,"#123456"),"part color can be edited through its own inline row");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/opacity");
        check(controller.ApplyPropertyRow(0,"0.4")&&controller.ResetPropertyRow(0)
            &&controller.CopyDraft().WeaponCrosshairs[0]!.DotStyle.Opacity==.7f
            &&controller.CopyDraft().WeaponCrosshairs[0]!.DotStyle.Color=="#123456"
            &&controller.CopyDraft().WeaponCrosshairs[0]!.OverrideProperties!.Contains("DotStyle"),
            "part row reset preserves other authored fields and the remaining weapon override");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/color");
        check(controller.ResetPropertyRow(0)&&controller.CopyDraft().WeaponCrosshairs[0]!.DotStyle.Color=="#EE8844"
            &&!controller.CopyDraft().WeaponCrosshairs[0]!.OverrideProperties!.Contains("DotStyle"),
            "resetting the final part override restores future inheritance");
        foreach(string field in new[]{"shotScale","chargeScale","zoomScale","nativeThickness"})
        {
            controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/"+field);string snapshot=controller.Snapshot().ProfileJson;
            check(!controller.ApplyPropertyRow(0,"5")&&controller.Snapshot().ProfileJson==snapshot,"inline "+field+" rejects values above the runtime limit without silently clamping");
        }
    }
}
