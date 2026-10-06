using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

internal static partial class Program
{
    private static async Task CheckMapEditorAsync(string output, string directory,bool loadedAssets=false)
    {
        Directory.CreateDirectory(directory);
        var paths = new StudioPaths(AppContext.BaseDirectory, directory);
        var window = new StudioWindow(paths, new StudioSettings(), new StudioOpenRequest(Guid.NewGuid(), StudioOpenKind.Home));
        await window.InitializeAsync(promptForRecovery: false);
        window.Show();
        try
        {
            await window.Commands.ExecuteAsync(StudioCommand.NewMapWorkspace);
            Check(window.Documents.ActiveDocument is MapStudioDocument, "New Map command creates the standalone canonical map editor");
            var document = (MapStudioDocument)window.Documents.ActiveDocument!;
            document.NewProject("ACCEPTANCE MAP", example: true);
            MapDocument canonical = document.Host.Document ?? throw new InvalidOperationException("Map editor did not create its canonical document.");
            Check(document.Dirty && document.CanSave && canonical.Project.Definition.Geometry.Count > 0,
                "new canonical project exposes geometry and real dirty/save lifecycle");
            CheckActualMapDockingAndSearch(window, document, paths);
            await CheckAssetBrowserAsync(document,paths,output);
            if(loadedAssets)await CheckLoadedAssetWorkflowAsync(window,document,paths,output);
            await CaptureMapVariantsAsync(window, document, output, "map-studio-default", fourViews: false);
            document.Host.ToggleFourViews();
            await CaptureMapVariantsAsync(window, document, output, "map-studio-four-view", fourViews: true);
            document.Host.ToggleFourViews();
            document.Host.ShowPanel("Materials");
            await CaptureMapVariantsAsync(window, document, output, "map-studio-materials", fourViews: false);
            Guid meshId = canonical.Project.Definition.Geometry[0].Id;
            float textureScale=canonical.Project.Definition.Materials[canonical.Project.Definition.Geometry[0].Material].TexScale;
            canonical.EditObjects("Prepare authored UV mesh", [meshId], definition =>
            {
                int index = definition.Geometry.FindIndex(geometry => geometry.Id == meshId);
                MapGeometry geometry = definition.Geometry[index];
                definition.Geometry[index] = MapMeshEditing.Convert(geometry, textureScale);
            });
            document.Host.SelectFaces(meshId, 0);
            Check(document.Host.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text?.StartsWith("UV · 1 selected faces", StringComparison.Ordinal) == true),
                "standalone UV panel operates on selected canonical mesh face");
            await CaptureMapVariantsAsync(window, document, output, "map-studio-uv", fourViews: false);
            foreach(string panel in new[]{"Gameplay analysis","Structural diff","Prefabs"})
            {
                document.Host.ShowPanel(panel);
                if(panel=="Gameplay analysis")await CheckGameplayAnalysisWorkflowAsync(window,document);
                await CaptureMapVariantsAsync(window,document,output,"map-studio-"+panel.ToLowerInvariant().Replace(' ','-'),fourViews:false);
            }
            await using (var community = new EmptyCommunityService())
            {
                await File.WriteAllTextAsync(Path.Combine(directory, "map-community.txt"), community.Address);
                document.Host.ShowPanel("Community");
                var deadline = System.Diagnostics.Stopwatch.StartNew();
                while (window.Jobs.Jobs.Any(job => job.State == ProjectPrime.Studio.Jobs.StudioJobState.Running) && deadline.Elapsed < TimeSpan.FromSeconds(5))
                    await Task.Delay(10);
                PumpLayout(window);
                Check(community.Requests > 0 && !window.Jobs.Jobs.Any(job => job.State == ProjectPrime.Studio.Jobs.StudioJobState.Running),
                    "native Community dashboard uses completed cancellable discovery job against isolated fixture service");
                await CaptureMapVariantsAsync(window, document, output, "map-studio-community", fourViews: false);
                Control close = document.Host.GetVisualDescendants().OfType<Control>().Single(control => control.GetType().Name == "PrimeButton"
                    && control.GetType().GetProperty("Label")?.GetValue(control)?.ToString() == "CLOSE" && control.IsEffectivelyVisible);
                Point point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window) ?? throw new InvalidOperationException("Community close button has no window origin.");
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                PumpLayout(window);
            }
            document.Host.ShowPanel("Inspector");
            await CheckCanonicalMapLifecycleAsync(window, document, canonical, paths);
        }
        catch(Exception error){Console.Error.WriteLine("Map editor acceptance failed before cleanup: "+error);throw;}
        finally
        {
            await window.Documents.RequestCloseAllAsync(_ => Task.FromResult(StudioCloseDecision.Discard), _ => Task.FromResult<string?>(null));
            await window.TryCloseAsync();
            window.Close();
            await window.DisposeResourcesAsync();
        }
    }

    private static async Task CheckGameplayAnalysisWorkflowAsync(StudioWindow window,MapStudioDocument document)
    {
        var map=document.Host.Document!;string before=map.Project.Definition.Serialize();var state=map.CurrentStateId;int history=map.History.CommandCount;
        Control analyze=document.Host.GetVisualDescendants().OfType<Control>().Single(control=>control.GetType().Name=="PrimeButton"
            &&control.GetType().GetProperty("Label")?.GetValue(control)?.ToString()=="ANALYZE COLLISION AND NAVIGATION");
        Click(window,analyze);var timer=System.Diagnostics.Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(30))
        {
            if(!window.Jobs.Jobs.Any(job=>job.State==ProjectPrime.Studio.Jobs.StudioJobState.Running)
                &&document.Host.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.Text?.StartsWith("Navigation · ",StringComparison.Ordinal)==true))break;
            await Task.Delay(10);PumpLayout(window);
        }
        Check(document.Host.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.Text?.StartsWith("Navigation · ",StringComparison.Ordinal)==true)
            &&map.CurrentStateId==state&&map.History.CommandCount==history&&map.Project.Definition.Serialize()==before,
            "actual gameplay analysis worker publishes collision/navigation results without changing authored map or history");
        if(map.Project.Definition.Spawns.Count>0)
        {
            Control select=document.Host.GetVisualDescendants().OfType<Control>().First(control=>control.GetType().Name=="PrimeButton"
                &&control.GetType().GetProperty("Label")?.GetValue(control)?.ToString()=="SELECT SPAWN");
            Click(window,select);
            Check(map.Selection.SetEquals([map.Project.Definition.Spawns[0].Id])&&map.History.CommandCount==history,
                "actual gameplay result action selects the canonical spawn without an authored edit");
            var hierarchy=document.Host.GetVisualDescendants().OfType<ListBox>().Single(list=>list.Items.Cast<object>()
                .Any(item=>item.GetType().Name=="HierarchyRow"));
            Guid[] selected=hierarchy.SelectedItems!.Cast<object>().Select(item=>(MapObject?)item.GetType().GetProperty("Object")!.GetValue(item))
                .Where(item=>item is not null).Select(item=>item!.Id).ToArray();
            Check(map.Selection.SetEquals(selected),"gameplay result selection also updates actual hierarchy selected rows");
        }
    }

    private static async Task CheckAssetBrowserAsync(MapStudioDocument document,StudioPaths paths,string output)
    {
        document.Host.OpenAssetBrowser();
        Window asset=document.AssetBrowserWindow??throw new InvalidOperationException("Map Asset Browser did not create its independent native window.");
        foreach((int width,int height,double scale) in new[]{(760,640,1d),(1200,900,1d),(760,640,2d)})
        {
            asset.Width=width;asset.Height=height;asset.SetRenderScaling(scale);asset.UpdateLayout();Avalonia.Threading.Dispatcher.UIThread.RunJobs();asset.UpdateLayout();
            Check(asset.IsVisible&&asset.GetVisualDescendants().OfType<TextBlock>().Any(block=>block.IsEffectivelyVisible&&block.Text=="ASSETS & MUSIC"),
                "independent asset window exposes native browser controls");
            using var image=asset.CaptureRenderedFrame()??throw new InvalidOperationException("Independent native Asset Browser did not render.");
            CheckImageContent(image,"map-studio-assets-window");string file=$"map-studio-assets-window-{width}x{height}"+(scale==1?"":"-2x")+".png";
            image.Save(Path.Combine(output,file),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Captures.Add(new{route="map-studio-assets-window",logicalWidth=width,logicalHeight=height,scale,file,pixelWidth=image.PixelSize.Width,pixelHeight=image.PixelSize.Height});
            await Task.Yield();
        }
        var state=new StudioSettingsStore(paths).LoadSettings().MapLayout.AssetsWindow;
        Check(state.Visible&&Math.Abs(state.Width-760)<1&&Math.Abs(state.Height-640)<1,
            "independent native Asset Browser persists window dimensions with map layout");
        asset.Close();
        Check(document.AssetBrowserWindow is null&&!new StudioSettingsStore(paths).LoadSettings().MapLayout.AssetsWindow.Visible,
            "closing native Asset Browser releases window and saves closed mode");
    }

    private static async Task CaptureMapVariantsAsync(StudioWindow window, MapStudioDocument document, string output, string route, bool fourViews)
    {
        foreach ((int width, int height, double scale) in new[] { (1280, 800, 1d), (1920, 1080, 1d), (1280, 800, 2d) })
        {
            window.Width = width;
            window.Height = height;
            window.SetRenderScaling(scale);
            PumpLayout(window);
            using(var diagnostic=window.CaptureRenderedFrame())diagnostic?.Save(Path.Combine(output,route+"-current.png"),new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            Control[] viewports = document.Host.GetVisualDescendants().OfType<Control>()
                .Where(control => control.GetType().Name == "MapViewport" && control.IsEffectivelyVisible && control.Bounds.Width > 0).ToArray();
            Check(viewports.Length == (fourViews ? 4 : 1), route + " hosts the expected canonical viewport count");
            foreach (Control viewport in viewports)
                CheckControlBounds(window, viewport, fourViews ? 140 : 300, fourViews ? 150 : 350,
                    route + " reserves useful unclipped viewport space");
            foreach (string label in new[] { "SAVE", "VALIDATE", "BUILD .PPMAP" })
            {
                Control? button = document.Host.GetVisualDescendants().OfType<Control>().FirstOrDefault(control =>
                    control.GetType().Name == "PrimeButton" && control.GetType().GetProperty("Label")?.GetValue(control)?.ToString() == label
                    && control.IsEffectivelyVisible);
                Check(button is not null, route + " exposes " + label + " toolbar action");
                CheckControlBounds(window, button!, 30, 10, route + " toolbar action remains within window");
            }
            using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Map editor Skia frame did not render.");
            string name = $"{route}-{width}x{height}" + (scale == 1 ? "" : "-2x") + ".png";
            bitmap.Save(Path.Combine(output, name), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            CheckImageContent(bitmap, route);
            Captures.Add(new { route, logicalWidth = width, logicalHeight = height, scale, viewportBackend = "canonical CPU",
                file = name, pixelWidth = bitmap.PixelSize.Width, pixelHeight = bitmap.PixelSize.Height });
            await Task.Yield();
        }
    }

    private static async Task CheckCanonicalMapLifecycleAsync(StudioWindow window, MapStudioDocument document, MapDocument canonical, StudioPaths paths)
    {
        string source = Path.Combine(paths.UserDataDirectory, "acceptance-map.json");
        await document.SaveAsync(source, CancellationToken.None);
        Check(!document.Dirty && document.Path == source && ReferenceEquals(canonical, document.Host.Document),
            "standalone Save As preserves canonical document owner and marks saved state clean");
        string savedBytes = File.ReadAllText(source);
        Guid geometry = canonical.Project.Definition.Geometry[0].Id;
        DocumentStateId savedState = canonical.CurrentStateId;
        canonical.TransformSelection([geometry], "Move", new(2, 0, 0), 0, 1, false);
        Check(document.Dirty && canonical.CurrentStateId != savedState, "canonical edit updates real Studio dirty state");
        document.Host.Undo();
        Check(!document.Dirty && canonical.CurrentStateId == savedState, "standalone Undo returns to saved identity");
        document.Host.Redo();
        Check(document.Dirty, "standalone Redo restores edited dirty identity");
        await document.Host.ValidateAsync();
        Check(canonical.Diagnostics.IsValid, "standalone validation consumes canonical authored map successfully; diagnostics="
            +System.Text.Json.JsonSerializer.Serialize(canonical.Diagnostics));
        await document.Host.BuildAsync(package: false);
        string runtime = Path.Combine(paths.UserDataDirectory, "runtime");
        Check(Directory.Exists(runtime) && Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories).Any(),
            "standalone runtime build publishes canonical outputs beneath Studio private runtime");
        await document.Host.BuildAsync(package: true);
        string package = Path.ChangeExtension(source, ".ppmap");
        Check(File.Exists(package), "standalone package action writes portable canonical package");
        MapContentIdentity identity = MapContentIdentity.FromPackage(package);
        Check(identity.MapId == canonical.Project.Definition.MapId && identity.ContentHash.ToString().Length == 64 && identity.PackageHash.ToString().Length == 64
            && File.ReadAllText(source) == savedBytes,
            "standalone package preserves canonical identity without silently saving dirty source");
        await document.FlushAutosaveAsync();
        string recovery = document.RecoveryPath ?? throw new InvalidOperationException("Map editor has no recovery path.");
        Check(File.Exists(recovery) && File.ReadAllText(source) == savedBytes,
            "Studio autosave preserves source bytes and writes private recovery");
        Check(MapDocument.ReadRecovery(recovery).Definition.Serialize() == canonical.Project.Definition.Serialize(),
            "Studio recovery uses exact canonical snapshot rather than a second map model");
        Check(recovery.StartsWith(paths.UserDataDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "recovery remains beneath Studio private user data");
        var restoredSavedMap = new MapStudioDocument(paths, window, window.Jobs);
        try
        {
            await restoredSavedMap.OpenRecoveryAsync(recovery);
            Check(restoredSavedMap.Dirty && restoredSavedMap.Path == source
                && restoredSavedMap.Host.Document!.Project.Definition.Serialize() == canonical.Project.Definition.Serialize()
                && File.ReadAllText(source) == savedBytes,
                "saved new-map recovery honors exact persisted recovery path and leaves source bytes unchanged");
            restoredSavedMap.Host.Undo();
            Check(!restoredSavedMap.Dirty && restoredSavedMap.Host.Document!.Project.Definition.Serialize() == MapProjectSerializer.Load(source).Definition.Serialize(),
                "recovered saved map preserves canonical undo to saved source identity");
        }
        finally { await restoredSavedMap.DisposeAsync(); }

        Check(!await window.Documents.RequestCloseAsync(document, _ => Task.FromResult(StudioCloseDecision.Cancel), _ => Task.FromResult<string?>(null))
            && window.Documents.Documents.Contains(document) && document.Dirty, "actual dirty Map close Cancel retains document");
        Check(await window.Documents.RequestCloseAsync(document, _ => Task.FromResult(StudioCloseDecision.Save), _ => Task.FromResult<string?>(null))
            && document.State == StudioDocumentState.Closed && File.ReadAllText(source) != savedBytes,
            "actual dirty Map close Save persists edit before releasing editor");

        var recovered = new MapStudioDocument(paths, window, window.Jobs);
        recovered.NewProject("UNSAVED RECOVERY", example: true);
        MapDocument unsaved = recovered.Host.Document!;
        unsaved.TransformSelection([unsaved.Project.Definition.Geometry[0].Id], "Move", new(3, 0, 0), 0, 1, false);
        string expected = unsaved.Project.Definition.Serialize();
        await recovered.FlushAutosaveAsync();
        string unsavedRecovery = recovered.RecoveryPath!;
        await recovered.DisposeAsync();
        var afterCrash = new MapStudioDocument(paths, window, window.Jobs);
        try
        {
            await afterCrash.OpenRecoveryAsync(unsavedRecovery);
            Check(afterCrash.Dirty && afterCrash.Path is null && afterCrash.Host.Document!.Project.Definition.Serialize() == expected,
                "unsaved new-map recovery survives disposed process owner and restores exact canonical state");
            string immediateRecovery=afterCrash.RecoveryPath!;
            Check(File.Exists(immediateRecovery),"unsaved restored editor persists its new recovery key before returning to session host");
            var immediateSession=new StudioSession{CleanExit=false};immediateSession.Documents.Add(new(afterCrash.Id.Value,afterCrash.Kind,afterCrash.Path,immediateRecovery));
            var recoveryStore=new StudioSettingsStore(paths);Check(recoveryStore.SaveSession(immediateSession),"new recovered draft key is immediately durable in unclean session snapshot");
            var secondCrash=new MapStudioDocument(paths,window,window.Jobs);
            try
            {
                await secondCrash.OpenRecoveryAsync(recoveryStore.LoadSession()!.Documents.Single().RecoveryPath!);
                Check(secondCrash.Dirty&&secondCrash.Path is null&&secondCrash.Host.Document!.Project.Definition.Serialize()==expected,
                    "second immediate process loss restores exact unsaved recovered contents without idle autosave interval");
            }
            finally{await secondCrash.DisposeAsync();}
            string restoredPath = Path.Combine(paths.UserDataDirectory, "restored-unsaved.json");
            await afterCrash.SaveAsync(restoredPath, CancellationToken.None);
            Check(!afterCrash.Dirty && MapProjectSerializer.Load(restoredPath).Definition.MapId == unsaved.Project.Definition.MapId,
                "restored unsaved map saves with original portable identity");
        }
        finally { await afterCrash.DisposeAsync(); }
    }
}
