using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Input;
using Avalonia.VisualTree;
using MphRead.Mods.MapEditor;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Rendering;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static void CheckAboutWindow(StudioWindow owner,StudioPaths paths,string output)
    {
        var type=typeof(StudioWindow).Assembly.GetType("ProjectPrime.Studio.Diagnostics.StudioAboutWindow",throwOnError:true)!;
        string summary=(string)type.GetProperty("LocalSummary")!.GetValue(null)!;
        string studio=(string)type.GetProperty("StudioVersion")!.GetValue(null)!;
        string engine=(string)type.GetProperty("EngineVersion")!.GetValue(null)!;
        Check(!string.IsNullOrWhiteSpace(studio)&&studio!="unknown"&&!string.IsNullOrWhiteSpace(engine)&&engine!="unknown"
            &&summary.Contains(studio,StringComparison.Ordinal)&&summary.Contains(engine,StringComparison.Ordinal)
            &&summary.Contains("Studio IPC:",StringComparison.Ordinal)&&!StudioGraphicsHost.HasDevice,
            "About exposes actual Studio/engine/IPC versions without creating graphics device");
        Check(owner.GetLogicalDescendants().OfType<MenuItem>().Any(item=>item.Header?.ToString()=="About Project Prime Studio…"),
            "native Help menu exposes About version window");
        var window=(Window)Activator.CreateInstance(type,paths)!;
        try
        {
            window.Show(owner);Avalonia.Threading.Dispatcher.UIThread.RunJobs();window.UpdateLayout();
            var text=window.GetVisualDescendants().OfType<TextBlock>().Single(block=>block.Name=="StudioVersionSummary");
            Check(text.IsEffectivelyVisible&&text.Bounds.Width>200&&text.Bounds.Height>70,"native About version summary allocates readable wrapped text");
            using var image=window.CaptureRenderedFrame()??throw new InvalidOperationException("About native window did not render.");
            CheckImageContent(image,"studio-about");image.Save(Path.Combine(output,"studio-about.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Captures.Add(new{route="studio-about",file="studio-about.png",pixelWidth=image.PixelSize.Width,pixelHeight=image.PixelSize.Height});
        }
        finally{window.Close();}
        Check(!StudioGraphicsHost.HasDevice,"About window closes without eagerly initializing GPU resources");
    }
    private static void CheckHotkeys(StudioSettings settings, StudioPaths paths)
    {
        string primary = OperatingSystem.IsMacOS() ? "Meta" : "Ctrl";
        Check(StudioHotkeys.Binding(settings, StudioCommand.Save) == primary + "+S", "native default save shortcut follows platform modifier");
        settings.CustomHotkeys[StudioCommand.Save.ToString()] = "Ctrl+Shift+F12";
        Check(StudioHotkeys.Match(settings, new KeyEventArgs { Key = Key.F12, KeyModifiers = KeyModifiers.Control | KeyModifiers.Shift }, [StudioCommand.Save]) == StudioCommand.Save,
            "custom shortcut resolves command through actual native key matcher");
        settings.CustomHotkeys[StudioCommand.SaveAs.ToString()] = "Ctrl+Shift+F12";
        Check(StudioHotkeys.Validate(settings.CustomHotkeys)?.Contains("same shortcut", StringComparison.Ordinal) == true,
            "duplicate normalized shortcuts are rejected before persistence");
        settings.CustomHotkeys.Remove(StudioCommand.SaveAs.ToString());
        settings.CustomHotkeys[StudioCommand.ReplayPlayPause.ToString()] = "";
        Check(StudioHotkeys.Match(settings, new KeyEventArgs { Key = Key.Space }, [StudioCommand.ReplayPlayPause]) is null,
            "empty override disables command binding");
        settings.CustomHotkeys[StudioCommand.Undo.ToString()] = "not-a-key";
        Check(StudioHotkeys.Match(settings, new KeyEventArgs { Key = Key.Z }, [StudioCommand.Undo]) is null
            && StudioHotkeys.Validate(settings.CustomHotkeys) is not null, "invalid gestures reject gracefully without taking down key handler");
        settings.CustomHotkeys.Remove(StudioCommand.Undo.ToString());
        settings.CustomHotkeys["99999"] = "Ctrl+F11";
        var store = new StudioSettingsStore(paths);
        Check(store.SaveSettings(settings), "custom native shortcuts persist atomically");
        var loaded = store.LoadSettings();
        Check(loaded.CustomHotkeys.GetValueOrDefault(StudioCommand.Save.ToString()) == "Ctrl+Shift+F12"
            && loaded.CustomHotkeys.GetValueOrDefault(StudioCommand.ReplayPlayPause.ToString()) == ""
            && !loaded.CustomHotkeys.ContainsKey("99999"), "custom shortcut round trip preserves disabled binding and removes unknown numeric command");
        settings.CustomHotkeys.Remove("99999");
    }

    private static void CheckPerformanceHud(StudioWindow window)
    {
        Control center = window.GetVisualDescendants().OfType<Control>().Single(control => control.Name == "StudioDockCenter");
        var bounds = center.Bounds;
        Check(!StudioGraphicsHost.HasDevice, "idle native shell has not created graphics device");
        window.TogglePerformanceHud(); PumpLayout(window);
        var hud = window.PerformanceHud ?? throw new InvalidOperationException("HUD toggle did not create its native overlay.");
        hud.RefreshSnapshot();
        Check(hud.IsSampling && hud.Snapshot is { ProcessWorkingSetBytes: > 0, ManagedBytes: > 0 }
            && hud.Snapshot.Sources.Any(source => source.Render is { GpuMilliseconds: null })
            && !StudioGraphicsHost.HasDevice, "idle HUD measures process memory and leaves unavailable GPU timing unknown without creating device");
        Check(center.Bounds == bounds, "performance HUD overlay does not change document viewport allocation");
        hud.IsVisible = false;
        Check(!hud.IsSampling, "hidden HUD stops sampling timer");
        hud.IsVisible = true; Check(hud.IsSampling, "visible attached HUD resumes sampling");
        window.TogglePerformanceHud();
        Check(window.PerformanceHud is null && !hud.IsSampling && hud.Snapshot is null,
            "HUD removal stops timer and releases captured source providers");
    }

    private static void CheckActualMapDockingAndSearch(StudioWindow window, MapStudioDocument document, StudioPaths paths)
    {
        var dock = document.DockHost ?? throw new InvalidOperationException("Canonical Map host has no native dock layout.");
        dock.Hide(StudioDockRegion.Left); PumpLayout(window);
        Check(!dock.IsRegionVisible(StudioDockRegion.Left) && !dock.CaptureLayout().LeftVisible,
            "actual hierarchy dock can hide and persists native layout state");
        dock.Show(StudioDockRegion.Left); dock.Detach(StudioDockRegion.Right); PumpLayout(window);
        Check(dock.CaptureLayout().Detached.Contains(StudioDockRegion.Right), "actual map inspector detaches into native desktop window");
        dock.RestoreDefaults(); PumpLayout(window);
        Check(dock.CaptureLayout().Detached.Count == 0 && dock.IsRegionVisible(StudioDockRegion.Right), "actual map dock restore reunites inspector and closes detached window");
        MapDocument map = document.Host.Document!;
        var item = MapObjects.All(map.Project.Definition).First();
        var result = StudioGlobalSearchWindow.Search(window.Documents, window.Commands, new StudioSettings(), item.Label)
            .First(match => match.Category == "Map object" && match.Title == item.Label);
        window.Documents.Select(null); result.Activate();
        Check(window.Documents.ActiveDocument == document && map.Selection.Contains(item.Id) && map.ActiveObjectId == item.Id,
            "global object search activates actual tab and canonical selection");
        var material = map.Project.Definition.Materials.First();
        var materialResult = StudioGlobalSearchWindow.Search(window.Documents, window.Commands, new StudioSettings(), material.Name)
            .First(match => match.Category == "Material");
        materialResult.Activate(); PumpLayout(window);
        Check(document.Host.GetVisualDescendants().OfType<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text == "MATERIAL BROWSER"),
            "global material search activates native material editor panel");
        string assetRoot = map.Project.Definition.BaseDirectory ?? Path.Combine(paths.UserDataDirectory, "map-projects");
        Directory.CreateDirectory(assetRoot);
        WriteFixtureMusic(Path.Combine(assetRoot, "acceptance-search.wav"));
        map.Edit("Search fixture asset",definition=>definition.Assets.Add(new(){Path="acceptance-search.wav",Kind="audio",Name="Acceptance search asset"}),MapChangeDomain.Metadata);
        var assetResult=StudioGlobalSearchWindow.Search(window.Documents,window.Commands,new StudioSettings(),"acceptance-search")
            .Single(match=>match.Category=="Asset");
        assetResult.Activate(); PumpLayout(window);
        Check(document.Host.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.IsEffectivelyVisible&&block.Text=="ASSETS & MUSIC"),
            "global asset search activates actual asset/music inspector");
        document.Host.Undo();
        document.Host.ShowPanel("Inspector");
        Check(new StudioSettingsStore(paths).LoadSettings().MapLayout.Detached.Count == 0,
            "actual map dock restoration persists independently from shell layout");
    }
}
