#if (MPHREAD_RMLUI_POC || MPHREAD_RMLUI) && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Entities;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Hud;
using MphRead.Mods.Settings;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Settings;
public static class EngineSettingsBackendCheck
{
    public static void Run()
    {
        string cwd=Directory.GetCurrentDirectory(),prefs=LauncherPrefs.Directory,export=Paths.Export;
        string? environment=Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA");
        string fixture=Directory.CreateTempSubdirectory("prime-settings-adapter-").FullName;
        try
        {
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",fixture);
            Directory.SetCurrentDirectory(fixture);LauncherPrefs.Directory=fixture;
            Paths.SetPath("Export",Path.Combine(fixture,"export"));
            LauncherPrefs.ReplayAutoPrune=false;LauncherPrefs.DebugLogs=false;
            HudProfiles.Load(Path.Combine(fixture,"Savedata","hud-profiles"));
            var menu=new MenuSettings();var state=new SceneGameState(new());
            var bind=InputSettings.Bind(InputSettings.Bindings[0]);bind.Type=ButtonType.Mouse;bind.Key=Keys.W;bind.MouseButton=MouseButton.Right;
            GamepadRuntimeConfig.Current.Bindings.Preset="Custom";
            var backend=new EngineSettingsBackend(menu,state);
            var values=backend.Capture();
            void Check(bool accepted,string message){if(!accepted)throw new InvalidOperationException(message);Console.WriteLine("RMLSETTINGS PASS "+message);}
            Check(backend.Definitions.Count>250,"complete scalar, key/mouse, controller, touch and HUD schema available");
            Check(backend.Definitions.All(f=>values.ContainsKey(f.Id)),"every field captures an authoritative runtime value");
            Check(values["binding."+InputSettings.Bindings[0].Name]==$"{ButtonType.Mouse}:{Keys.W}:{MouseButton.Right}","unused key field is preserved in binding snapshot");
            var settings=new SettingsController(backend);
            foreach(var category in Enum.GetValues<SettingsCategory>())
            {
                settings.SelectCategory(category);
                Check(settings.Groups.Count<=192,category+" groups fit the native navigation");
                var reachable=new System.Collections.Generic.HashSet<string>();
                for(int group=0;group<settings.Groups.Count;group++)
                {
                    settings.SelectGroup(group);
                    var first=settings.Snapshot();
                    Console.WriteLine($"SETTINGS GROUP {category} / {settings.Group}: {first.PageCount} pages");
                    for(int page=0;page<first.PageCount;page++)
                    { foreach(var field in settings.Snapshot().Fields)reachable.Add(field.Definition.Id);settings.MovePage(1); }
                }
                Check(reachable.SetEquals(backend.Definitions.Where(f=>f.Category==category).Select(f=>f.Id)),category+" grouping retains every setting");
            }
            settings.SelectCategory(SettingsCategory.Display);
            Check(!settings.Set("prefs.MapServiceAddress","not-a-service"),"map service rejects invalid URL");
            Check(settings.Set("prefs.MapServiceAddress","https://maps.example.test/"),"map service stages in settings draft");
            Check(settings.Set("input.MouseSensitivity","0.37"),"mouse draft accepts production range");
            Check(InputSettings.MouseSensitivity!=.37f,"mouse draft does not mutate runtime");
            Check(settings.Set("pad.gamepad_look_x","1.75"),"controller sensitivity staged");
            Check(settings.Set("touch.Layout.Jump","0.75,0.65,1.25"),"touch position and scale staged");
            Check(settings.Apply(),"authoritative settings stores commit successfully");
            Check(Math.Abs(InputSettings.MouseSensitivity-.37f)<.0001f&&Math.Abs(GamepadOptions.LookX-1.75f)<.0001f,"saved input and controller values applied");
            Check(bind.Type==ButtonType.Mouse&&bind.Key==Keys.W&&bind.MouseButton==MouseButton.Right,"unmodified exact binding tuple survives apply");
            Check(PadBindings.Preset=="Custom","controller preset survives slot load ordering");
            Check(HudProfiles.CopyCurrent().WeaponCrosshairs.All(c=>c==null)&&HudProfiles.CopyCurrent().ZoomCrosshair==null,"unused crosshair overrides remain null across input saves");
            Check(File.ReadAllText("map-community.txt")=="https://maps.example.test/","Community uses persisted settings service address");
            using (var archive = new MemoryStream())
            {
                SettingsArchive.Export(fixture,archive,"ui-check");
                archive.Position=0;
                using var zip=new System.IO.Compression.ZipArchive(archive,System.IO.Compression.ZipArchiveMode.Read);
                var service=zip.GetEntry("map-community.txt");
                Check(service!=null,"Community service is included in settings export");
                using var reader=new StreamReader(service!.Open());
                Check(reader.ReadToEnd()=="https://maps.example.test/","settings export preserves the configured service");
            }
            Check(File.Exists("Savedata/settings.json")&&File.Exists("launcher.txt")&&File.Exists("controls.txt"),"legacy file names are retained");
            settings.Set("prefs.HighContrast","true");settings.Set("prefs.LargeText","true");settings.Set("prefs.TouchTargets","true");
            Check(settings.Apply()&&LauncherPrefs.HighContrast&&LauncherPrefs.LargeText&&LauncherPrefs.TouchTargets&&File.ReadAllText("launcher.txt").Contains("high_contrast=true"),"native accessibility preferences use the existing authoritative launcher store");
            File.AppendAllText("launcher.txt","future_extension=value\n");File.AppendAllText("controls.txt","future_input_extension=value\n");
            settings.Set("prefs.CombatFeedbackVolume","0.5");Check(settings.Apply(),"second existing-service save succeeds");
            Check(File.ReadAllText("launcher.txt").Contains("future_extension=value")&&File.ReadAllText("controls.txt").Contains("future_input_extension=value"),"forward preference keys are retained");
            settings.Set("pad.gamepad_lt_min","0.8");settings.Set("pad.gamepad_lt_max","0.5");
            Check(!settings.Apply()&&settings.Dirty&&GamepadOptions.LeftTriggerMin==0,"cross-field controller calibration rejects before runtime mutation");
            settings.Discard();
            byte[] previousMenu=File.ReadAllBytes("Savedata/settings.json");
            File.Delete("controls.txt");Directory.CreateDirectory("controls.txt");
            settings.Set("input.MouseSensitivity","0.8");
            Check(!settings.Apply()&&settings.Dirty&&Math.Abs(InputSettings.MouseSensitivity-.37f)<.0001f,"swallowed legacy input write failure is detected and runtime rolls back");
            Check(previousMenu.AsSpan().SequenceEqual(File.ReadAllBytes("Savedata/settings.json")),"multi-store failure restores prior menu bytes");
            Directory.Delete("controls.txt");
            settings.Discard();settings.Set("menu.GraphicsPreset","ultra");
            Check(settings.Draft["menu.ResolutionScale"]=="150"&&settings.Draft["menu.ShadowQuality"]=="high","existing shared graphics preset expands the draft");
            settings.Discard();settings.Set("pad.gamepad_preset","Southpaw");
            Check(settings.Draft["pad.gamepad_southpaw"]=="True"&&PadBindings.Preset=="Custom","controller preset expands detached bindings and stick assignment");
            settings.Discard();
            Check(settings.Set("hudOverride./weaponCrosshairs/4","true")&&settings.Set("hud/weaponCrosshairs/4/color","#123456")&&settings.Apply(),"new per-weapon crosshair overrides can be enabled and saved");
            Check(HudProfiles.CopyCurrent().WeaponCrosshairs[4]?.Color=="#123456"&&HudProfiles.CopyCurrent().WeaponCrosshairs[0]==null,"edited weapon override retains unrelated null targets");
            settings.Set("hud/weaponCrosshairs/4/overrideProperties","inherit");settings.Set("hud/weaponCrosshairs/4/gap","7");
            Check(settings.Apply()&&HudProfiles.CopyCurrent().WeaponCrosshairs[4]!.OverrideProperties!.SequenceEqual(new[]{"Gap"}),"editing inherited crosshair field marks only that property as overridden");
            var detached=backend.DraftHud(settings.Draft);string savedBase=detached.BasePreset;
            detached.Name="Detached editor";detached.BasePreset=savedBase=="Classic"?"Project Prime":"Classic";
            Check(settings.StageSnapshot(backend.StageHud(settings.Draft,detached))&&HudProfiles.CopyCurrent().Name!="Detached editor","HUD editor handoff remains detached until Apply");
            settings.SelectCategory(SettingsCategory.Hud);settings.RevertCategory();
            Check(!settings.Dirty&&backend.DraftHud(settings.Draft).BasePreset==savedBase,"category revert restores full HUD carrier including preset metadata");
            string style=backend.Definitions.First(f=>f.Id=="legacy.CrosshairStyle").Choices.First(v=>v!=settings.Draft["legacy.CrosshairStyle"]);
            settings.Set("legacy.CrosshairStyle",style);
            var explicitHud=backend.DraftHud(settings.Draft);explicitHud.Crosshair.Color="#654321";
            settings.StageSnapshot(backend.StageHud(settings.Draft,explicitHud));
            Check(backend.DraftHud(settings.Draft).Crosshair.Color=="#654321","HUD editor crosshair remains authoritative after earlier legacy style edit");
            string size=backend.Definitions.First(f=>f.Id=="legacy.CrosshairSize").Choices.First(v=>v!=settings.Draft["legacy.CrosshairSize"]);
            settings.Set("legacy.CrosshairSize",size);
            Check(settings.Apply()&&HudProfiles.CopyCurrent().Crosshair.Color=="#654321","later legacy size edit cannot replace an explicit HUD editor draft");
            string replay=Network.DemoLibrary.Directory;
            if(replay.StartsWith(fixture,StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(replay)!);File.WriteAllText(replay,"blocked replay directory");
                settings.Set("prefs.ReplayAutoPrune","true");settings.Set("prefs.ReplayStorageLimitGb","5");
                Check(settings.Apply()&&!settings.Dirty&&backend.ApplyWarning.Contains("Replay cleanup",StringComparison.Ordinal),"post-commit maintenance failure reports warning without undoing successful save");
            }
            Console.WriteLine("RMLSETTINGS CHECK PASS authoritative persistence, rollback and comprehensive schema");
        }
        finally
        {
            Directory.SetCurrentDirectory(cwd);LauncherPrefs.Directory=prefs;
            Paths.SetPath("Export",export);
            Environment.SetEnvironmentVariable("PROJECT_PRIME_USER_DATA",environment);
            Directory.Delete(fixture,recursive:true);
        }
    }
}
#endif
