using System.Collections.Immutable;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render.Hud;

internal static class HudInlineSectionCheck
{
    internal static void Run(Action<bool,string> check)
    {
        List<HudEditorProperty> ReadRows(HudEditorController editor, HudEditorAction section)
        {
            editor.Dispatch(section);var rows=new List<HudEditorProperty>();
            do
            {
                var state=editor.Snapshot();rows.AddRange(state.Properties);
                if(state.PropertyPage+1>=state.PropertyPageCount)break;
                editor.Dispatch(HudEditorAction.PropertyNext);
            }while(true);
            return rows;
        }
        using(var autoEnable=new HudEditorController(HudProfileDefaults.Create("Project Prime"),new HudBoundary()))
        {
            autoEnable.SelectElement(1);autoEnable.Dispatch(HudEditorAction.SectionTypography);
            autoEnable.Dispatch(HudEditorAction.SearchProperties,"/typography/styles/core.health/font");
            check(!autoEnable.CopyDraft().Typography.Get("core.health").Enabled
                &&autoEnable.ApplyPropertyRow(0,HudFonts.All[3].Name)
                &&autoEnable.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[3].Id
                &&autoEnable.CopyDraft().Typography.Get("core.health").Enabled,
                "choosing a friendly per-element font enables that typography style in the same edit");
            autoEnable.Dispatch(HudEditorAction.Undo);
            check(autoEnable.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[0].Id
                &&!autoEnable.CopyDraft().Typography.Get("core.health").Enabled,
                "undo restores both the prior face and disabled state as one authoring edit");
            autoEnable.Dispatch(HudEditorAction.Redo);
            check(autoEnable.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[3].Id
                &&autoEnable.CopyDraft().Typography.Get("core.health").Enabled,
                "redo restores the selected face and enabled state together");
            autoEnable.Dispatch(HudEditorAction.SectionGlobal);
            autoEnable.Dispatch(HudEditorAction.SearchProperties,"/typography/gameFont");
            check(autoEnable.ApplyPropertyRow(0,HudFonts.All[2].Name)
                &&autoEnable.CopyDraft().Typography.GameFont==HudFonts.All[2].Id
                &&autoEnable.CopyDraft().Typography.UseGameFont&&!autoEnable.CopyDraft().Typography.UseGlobalFont,
                "choosing a game font activates its category override");
            autoEnable.Dispatch(HudEditorAction.SearchProperties,"/typography/interfaceFont");
            check(autoEnable.ApplyPropertyRow(0,HudFonts.All[4].Name)
                &&autoEnable.CopyDraft().Typography.InterfaceFont==HudFonts.All[4].Id
                &&autoEnable.CopyDraft().Typography.UseInterfaceFont&&!autoEnable.CopyDraft().Typography.UseGlobalFont,
                "choosing an interface font activates its category override");
        }
        using(var ordered=new HudEditorController(HudProfileDefaults.Create("Project Prime"),new HudBoundary(),new[]
        {
            new HudEditorOption("ds","Screen proportions","4:3",ImmutableArray.Create("4:3","5:3"),Section:2,Group:"Stacked dual screens"),
            new HudEditorOption("stack","Stacked dual-screen orderedLayout","false",ImmutableArray.Create("false","true"),Section:2,Group:"Stacked dual screens"),
            new HudEditorOption("stylus","Stylus input mode","false",ImmutableArray.Create("false","true"),Section:2,Group:"Stacked dual screens"),
            new HudEditorOption("fps","Show FPS","off",ImmutableArray.Create("off","on")),
            new HudEditorOption("touch","Weapon-select touch target size","1",ImmutableArray.Create("1","2"),Group:"Touchscreen")
        }))
        {
            var reticle=ReadRows(ordered,HudEditorAction.SectionAppearance);
            var reticlePaths=reticle.Select(row=>row.Path).ToArray();
            check(reticle.Take(8).Select(row=>row.Path).SequenceEqual(new[]{"/crosshair/enabled","/crosshair/native","/crosshair/color","/crosshair/scale",
                "/crosshair/gap","/crosshair/thickness","/crosshair/lengthX","/crosshair/lengthY"}),
                "crosshair appearance opens with the enabled, native and core shape controls");
            int lastReticle=Array.FindLastIndex(reticlePaths,path=>path.StartsWith("/crosshair/",StringComparison.Ordinal));
            int firstMarker=Array.FindIndex(reticlePaths,path=>path.StartsWith("/hitMarker/",StringComparison.Ordinal));
            int lastMarker=Array.FindLastIndex(reticlePaths,path=>path.StartsWith("/hitMarker/",StringComparison.Ordinal));
            int theme=Array.FindIndex(reticlePaths,path=>path=="/weaponCrosshairTheme");
            int firstPart=Array.FindIndex(reticlePaths,path=>path.StartsWith("/crosshair/",StringComparison.Ordinal)&&path.IndexOf('/',11)>=0);
            int lastMain=Array.FindLastIndex(reticlePaths,path=>path.StartsWith("/crosshair/",StringComparison.Ordinal)&&path.IndexOf('/',11)<0);
            check(reticlePaths.Distinct().Count()==reticlePaths.Length&&firstPart>lastMain&&lastReticle<firstMarker
                &&lastMarker-firstMarker+1==reticle.Count(row=>row.Path.StartsWith("/hitMarker/",StringComparison.Ordinal))
                &&theme>lastReticle&&theme<firstMarker,
                "reticle paths remain unique and complete, part styles and theme precede a contiguous hit-marker group");
            ordered.Dispatch(HudEditorAction.SectionAppearance);
            check(ordered.ApplyPropertyRow(3,"1.5")&&ordered.Snapshot().Property?.Path=="/crosshair/scale"
                &&ordered.CopyDraft().Crosshair.Scale==1.5f,"the reordered scale row still edits its intended profile path");
            ordered.Dispatch(HudEditorAction.Undo);
            check(ordered.CopyDraft().Crosshair.Scale==1,"undo follows the selected reordered property");
            ordered.Dispatch(HudEditorAction.Redo);
            check(ordered.CopyDraft().Crosshair.Scale==1.5f,"redo follows the selected reordered property");
            check(ordered.ResetPropertyRow(3)&&ordered.CopyDraft().Crosshair.Scale==1,"row reset follows the reordered scale property");
            ordered.Dispatch(HudEditorAction.SearchProperties,"/crosshair/native");
            check(ordered.ApplyPropertyRow(0,"true"),"switch reticle artwork to native mode");
            reticle=ReadRows(ordered,HudEditorAction.SectionAppearance);
            check(reticle.Take(8).Select(row=>row.Path).SequenceEqual(new[]{"/crosshair/enabled","/crosshair/native","/crosshair/color","/crosshair/scale",
                "/crosshair/nativeThickness","/crosshair/nativeOriginalColors","/crosshair/nativeAnimations","/nativeReticleOpacity"})
                &&!reticle.Any(row=>row.Path=="/crosshair/lengthX"),
                "native reticle filtering keeps its mode-specific controls on the first page");

            ordered.SelectElement(1);
            var health=ReadRows(ordered,HudEditorAction.SectionAppearance);
            ordered.Dispatch(HudEditorAction.SectionAppearance);
            check(ordered.Snapshot().Properties.Take(8).All(row=>row.Path.StartsWith("/health/",StringComparison.Ordinal))
                &&health.Any(row=>row.Path=="/haloLowHealthSound")&&health.FindIndex(row=>row.Path=="/haloLowHealthSound")>health.FindIndex(row=>row.Path.StartsWith("/health/",StringComparison.Ordinal)),
                "health controls lead Appearance before its alert and element fallback rows");
            ordered.SelectElement(2);
            ReadRows(ordered,HudEditorAction.SectionAppearance);
            ordered.Dispatch(HudEditorAction.SectionAppearance);
            check(ordered.Snapshot().Properties.Take(8).All(row=>row.Path.StartsWith("/ammo/",StringComparison.Ordinal)),
                "ammunition controls lead Appearance before fallback styling");
            ordered.SelectElement(4);
            ReadRows(ordered,HudEditorAction.SectionAppearance);
            ordered.Dispatch(HudEditorAction.SectionAppearance);
            check(ordered.Snapshot().Properties.Take(4).Select(row=>row.Path).SequenceEqual(new[]{"/radar/style","/radar/orientation","/radar/rangeScale","/radar/radiusScale"})
                &&ordered.Snapshot().Properties.All(row=>!row.Path.StartsWith("/radar/elevation",StringComparison.Ordinal)
                    &&!row.Path.StartsWith("/radar/sweep",StringComparison.Ordinal)&&!row.Path.EndsWith("/trailSamples",StringComparison.Ordinal)),
                "radar style, orientation, range and size lead the first Appearance page");
            ordered.SelectElement(1);
            var orderedLayout=ReadRows(ordered,HudEditorAction.SectionLayout);
            ordered.Dispatch(HudEditorAction.SectionLayout);
            var layoutFirst=ordered.Snapshot().Properties.Select(row=>row.Path).ToArray();
            check(Array.IndexOf(layoutFirst,"/elements/core.health/enabled")<Array.IndexOf(layoutFirst,"/elements/core.health/anchor")
                &&Array.IndexOf(layoutFirst,"/elements/core.health/anchor")<Array.IndexOf(layoutFirst,"/elements/core.health/offsetX")
                &&Array.IndexOf(layoutFirst,"/elements/core.health/offsetX")<Array.IndexOf(layoutFirst,"/elements/core.health/offsetY")
                &&Array.IndexOf(layoutFirst,"/elements/core.health/offsetY")<Array.IndexOf(layoutFirst,"/elements/core.health/scale")
                &&Array.IndexOf(layoutFirst,"/elements/core.health/scale")<Array.IndexOf(layoutFirst,"/elements/core.health/layer")
                &&orderedLayout.FindIndex(row=>row.Path=="/elements/core.health/contexts")>orderedLayout.FindIndex(row=>row.Path=="/elements/core.health/scale"),
                "Position keeps visible, anchor, coordinates and scale ahead of advanced orderedLayout controls");
            var orderedLettering=ReadRows(ordered,HudEditorAction.SectionTypography);
            int font=orderedLettering.FindIndex(row=>row.Path=="/typography/styles/core.health/font");
            int size=orderedLettering.FindIndex(row=>row.Path=="/typography/styles/core.health/size");
            int outline=orderedLettering.FindIndex(row=>row.Path=="/typography/styles/core.health/outline");
            check(font>=0&&size>font&&outline>size&&orderedLettering.All(row=>row.Path.StartsWith("/typography/styles/core.health/",StringComparison.Ordinal)),
                "Lettering keeps a text style together with font and size before outline details");
            ordered.SelectElement(4);
            ordered.Dispatch(HudEditorAction.SectionDecorations);
            ordered.Dispatch(HudEditorAction.SearchProperties,"/decorations/styles/radar/matchPalette");
            check(ordered.ApplyPropertyRow(0,"false"),"enable a radar decoration color override");
            var decoration=ReadRows(ordered,HudEditorAction.SectionDecorations);
            int palette=decoration.FindIndex(row=>row.Path=="/decorations/styles/radar/matchPalette");
            int color=decoration.FindIndex(row=>row.Path=="/decorations/styles/radar/color");
            int opacity=decoration.FindIndex(row=>row.Path=="/decorations/styles/radar/opacity");
            int lineScale=decoration.FindIndex(row=>row.Path=="/decorations/styles/radar/lineScale");
            check(palette>=0&&color>palette&&opacity>color&&lineScale>opacity&&decoration.All(row=>row.Path.StartsWith("/decorations/styles/radar/",StringComparison.Ordinal)),
                "decorative palette, color and opacity stay together before line tuning");
            var orderedGlobal=ReadRows(ordered,HudEditorAction.SectionGlobal);
            ordered.Dispatch(HudEditorAction.SectionGlobal);
            var globalFirst=ordered.Snapshot().Properties.Select(row=>row.Path).ToArray();
            int lastGlobalBasic=new[]{"/globalScale","/globalOpacity","/textScale","/iconScale","/accentColor","/safeArea"}
                .Max(path=>orderedGlobal.FindIndex(row=>row.Path==path));
            int firstAdvanced=orderedGlobal.FindIndex(row=>row.Path.StartsWith("/typography/",StringComparison.Ordinal)
                ||row.Path.StartsWith("/decorations/",StringComparison.Ordinal)||row.Label.StartsWith("Touchscreen",StringComparison.Ordinal));
            check(globalFirst.Take(3).SequenceEqual(new[]{"/options/ds","/options/stack","/options/stylus"})
                &&globalFirst.Skip(3).Take(5).SequenceEqual(new[]{"/globalScale","/globalOpacity","/textScale","/iconScale","/accentColor"})
                &&orderedGlobal.Select(row=>row.Path).Distinct().Count()==orderedGlobal.Count
                &&lastGlobalBasic>=0&&lastGlobalBasic<firstAdvanced&&orderedGlobal.Any(row=>row.Path=="/options/fps")
                &&orderedGlobal.Any(row=>row.Path=="/options/touch"),
                "HUD-wide keeps stacked controls first, shared basics next and fonts, decorations and touch settings later");
        }
        using var controller=new HudEditorController(HudProfileDefaults.Create("Project Prime"),new HudBoundary(),new[]
        {
            new HudEditorOption("ds","Screen proportions","4:3",ImmutableArray.Create("4:3","5:3"),Section:2,Group:"Stacked dual screens"),
            new HudEditorOption("stack","Stacked dual-screen layout","false",ImmutableArray.Create("false","true"),Section:2,Group:"Stacked dual screens"),
            new HudEditorOption("stylus","Stylus input mode","false",ImmutableArray.Create("false","true"),Section:2,Group:"Stacked dual screens"),
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
        check(appearance.All(row=>row.Path.StartsWith("/health/")||row.Path is "/haloLowHealthSound" or "/elements/core.health/color" or "/elements/core.health/opacity"),
            "Appearance exposes only the selected component styling and artwork");
        check(appearance.Single(row=>row.Path=="/elements/core.health/opacity").Range is {Minimum:0,Maximum:1,Step:.01f}
            &&appearance.Single(row=>row.Path=="/elements/core.health/color").Kind=="Color",
            "numeric and color rows carry typed editor metadata for native controls");
        var layout=Section(HudEditorAction.SectionLayout);
        check(layout.All(row=>row.Path=="/independentGauges"||row.Path.StartsWith("/elements/core.health/")&&!row.Path.EndsWith("/color")&&!row.Path.EndsWith("/opacity")),
            "Position contains only selected component geometry without shared or appearance values");
        var global=Section(HudEditorAction.SectionGlobal);
        controller.Dispatch(HudEditorAction.SectionGlobal);
        check(controller.Snapshot().Properties.Take(3).Select(row=>row.Path).SequenceEqual(new[]{"/options/ds","/options/stack","/options/stylus"})
            &&global.Select(row=>row.Path).Distinct().Count()==global.Count,"shared stacked-screen controls share the first HUD-wide page without duplicate rows");
        check(global.Any(row=>row.Path=="/options/fps")&&global.Any(row=>row.Path=="/smoothArtwork")
            &&global.Any(row=>row.Path=="/textScale")&&global.Any(row=>row.Path=="/options/ds")&&!global.Any(row=>row.Path=="/independentGauges"),
            "HUD-wide owns shared smoothing, lettering and screen preferences without element geometry");
        var lettering=Section(HudEditorAction.SectionTypography);
        check(lettering.All(row=>row.Path.StartsWith("/typography/styles/core.health/")),"Lettering contains only the selected element's typography style");
        check(!lettering.Any(row=>row.Path=="/typography/paletteDefaultsVersion")&&lettering.All(row=>!row.Label.Contains("styles",StringComparison.OrdinalIgnoreCase)
            &&!row.Label.Contains("core.",StringComparison.Ordinal)),"Lettering uses friendly component names and hides migration metadata");
        controller.Dispatch(HudEditorAction.SearchProperties,"/typography/styles/core.health/font");
        var fontRow=controller.Snapshot().Properties.Single();
        check(fontRow.Value==HudFonts.All[0].Name&&fontRow.Choices.SequenceEqual(HudFonts.All.Select(font=>font.Name)),
            "typography font rows display registered names as their value and choices");
        check(controller.CyclePropertyRow(0,false)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[1].Id
            &&controller.CopyDraft().Typography.Get("core.health").Enabled
            &&controller.CyclePropertyRow(0,true)&&controller.CopyDraft().Typography.Get("core.health").Font==HudFonts.All[0].Id,
            "font choice arrows map friendly names, enable the style and store canonical profile ids");
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
        controller.Dispatch(HudEditorAction.SectionGlobal);controller.Dispatch(HudEditorAction.SearchProperties,"/typography/globalFont");
        check(controller.Snapshot().Properties.Single().Value==HudFonts.All[0].Name
            &&controller.ApplyPropertyRow(0,HudFonts.All[2].Name)&&controller.CopyDraft().Typography.GlobalFont==HudFonts.All[2].Id
            &&controller.CopyDraft().Typography.UseGlobalFont,
            "shared typography font rows use friendly names and activate the shared font override");
        controller.Dispatch(HudEditorAction.Undo);
        check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[0].Id&&!controller.CopyDraft().Typography.UseGlobalFont,
            "shared font undo restores both the prior face and scope switch in one edit");
        controller.Dispatch(HudEditorAction.Redo);
        check(controller.CopyDraft().Typography.GlobalFont==HudFonts.All[2].Id&&controller.CopyDraft().Typography.UseGlobalFont,
            "shared font redo restores the selected face and active scope together");
        var fontRoundTrip=HudProfileStore.Parse(HudProfileStore.Serialize(controller.CopyDraft()));
        check(fontRoundTrip.Typography.Get("core.health").Font==HudFonts.All[0].Id
            &&fontRoundTrip.Typography.GlobalFont==HudFonts.All[2].Id,"profile serialization round-trips canonical font ids");
        var decorations=Section(HudEditorAction.SectionDecorations);
        check(decorations.All(row=>row.Path.StartsWith("/decorations/styles/health/"))&&decorations.All(row=>!row.Label.StartsWith("Decorations",StringComparison.OrdinalIgnoreCase)
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
        controller.Dispatch(HudEditorAction.SectionGlobal);controller.Dispatch(HudEditorAction.SearchProperties,"Screen proportions");
        check(controller.CyclePropertyRow(0,false)&&controller.CopyOptions()[0].Value=="5:3"
            &&controller.CyclePropertyRow(0,true)&&controller.CopyOptions()[0].Value=="4:3","inline option arrows navigate both directions using the intended row");
        controller.SelectElement(0);
        controller.Dispatch(HudEditorAction.SectionAppearance);controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/native");
        int nativeRow=controller.Snapshot().Properties.Select((row,index)=>(row,index)).Single(item=>item.row.Path=="/crosshair/native").index;
        check(controller.ApplyPropertyRow(nativeRow,"true"),"select original reticle artwork to edit its opacity");
        check(Section(HudEditorAction.SectionAppearance).Any(row=>row.Path=="/nativeReticleOpacity"),"original reticle opacity belongs to Crosshair Appearance when original artwork is active");
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
        controller.Dispatch(HudEditorAction.SectionGlobal);controller.Dispatch(HudEditorAction.SearchProperties,"/decorations/styles/borders/opacity");
        check(controller.ApplyPropertyRow(0,"0.4")&&controller.ResetPropertyRow(0)
            &&controller.CopyDraft().Decorations.Get("borders").Opacity==1,"decoration row reset uses validated defaults with populated style entries");
        controller.SelectElement(1);controller.Dispatch(HudEditorAction.SectionTypography);controller.Dispatch(HudEditorAction.SearchProperties,"/typography/styles/core.health/size");
        check(controller.ApplyPropertyRow(0,"2")&&controller.ResetPropertyRow(0)
            &&controller.CopyDraft().Typography.Get("core.health").Size==1,"lettering row reset uses validated defaults with populated style entries");
        controller.SelectElement(0);controller.Dispatch(HudEditorAction.SectionAppearance);controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/native");
        nativeRow=controller.Snapshot().Properties.Select((row,index)=>(row,index)).Single(item=>item.row.Path=="/crosshair/native").index;
        check(controller.ApplyPropertyRow(nativeRow,"false"),"custom reticle controls return when original artwork is disabled");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/motion");
        check(controller.Snapshot().Properties.Single().Kind=="Choice"&&controller.Snapshot().Properties[0].Help.Contains("Static",StringComparison.Ordinal)
            &&controller.Snapshot().Properties[0].Help.Contains("Dynamic",StringComparison.Ordinal),
            "crosshair motion explains the relationship between reticle motion and fixed-weapon presentation");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/segments");
        check(controller.Snapshot().Properties.Single().Range is {Minimum:12,Maximum:128,Step:1},
            "discrete crosshair counts expose the validated integer slider range");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/opacity");
        check(controller.ApplyPropertyRow(0,"0.7"),"parent reticle part has an authored opacity to inherit");
        controller.Dispatch(HudEditorAction.SearchProperties,"/crosshair/dotStyle/customColor");
        check(controller.ApplyPropertyRow(0,"true"),"enable part color override before choosing its ink");
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
