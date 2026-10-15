using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MphRead.AvaloniaShared;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using ProjectPrime.Studio.Map;
using ProjectPrime.Studio.Protocol;
using ProjectPrime.Studio.Settings;
using ProjectPrime.Studio.Shell;

namespace ProjectPrime.Studio.Diagnostics;

internal static class StudioRadiantWorkflowChecks
{
    // Run inside the existing asset-free headless host. A desktop-owned launcher
    // captures real launch requests; fixture files are never executed.
    public static int Run(StudioPaths paths, string source, Action<Task> complete)
    {
        int checks = 0;
        void Check(bool condition,string label) { if(!condition)throw new InvalidOperationException(label);checks++; }
        string? oldEditor = Environment.GetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantExecutableVariable);
        string? oldDefault = Environment.GetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable);
        string tool = Path.Combine(paths.UserDataDirectory,"radiant-locator");File.WriteAllText(tool,"never execute");
        var launches = new List<ProcessStartInfo>();
        var settings = new StudioSettings { ReopenLastSession=false };
        StudioWindow? window = null;
        try
        {
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantExecutableVariable,tool);
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable,null);
            window = new StudioWindow(paths,settings,new(Guid.NewGuid(),StudioOpenKind.Home,SafeMode:true),
                radiantLauncher:start=>launches.Add(start));
            complete(window.InitializeAsync(false));
            window.Show();
            complete(window.HandleLaunchRequestAsync(new(Guid.NewGuid(),StudioOpenKind.Map,source)));
            Dispatcher.UIThread.RunJobs();
            var original = (MapStudioDocument)window.Documents.ActiveDocument!;
            Control Find(string name) => original.Host.GetLogicalDescendants().OfType<Control>().Single(control => control.Name == name);
            void Activate(Control control)
            {
                control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyDownEvent, Key=Key.Enter, Source=control });
                control.RaiseEvent(new KeyEventArgs { RoutedEvent=InputElement.KeyUpEvent, Key=Key.Enter, Source=control });
            }
            void Press(string name) => Activate(Find(name));
            Check(launches.Count==0,"opening/restoring a map never implicitly launches Radiant");
            Check(original.Host.GetLogicalDescendants().OfType<Control>().Any(b=>b.Name=="StudioEditGeometryInRadiant" && b.IsVisible),
                "installed Radiant has a discoverable primary geometry action");
            Check(Find("StudioProjectOverview").IsEffectivelyVisible
                && !Find("StudioSceneHierarchy").IsVisible && !Find("StudioNativeViewport").IsVisible
                && !Find("StudioNativeAuthoringToolbar").IsVisible && !Find("StudioNativeCommandBar").IsVisible
                && original.Host.GetLogicalDescendants().OfType<GridSplitter>().All(splitter=>!splitter.IsVisible),
                "Radiant projects open in project management with the full native authoring workspace hidden");
            Check(Find("StudioPersistedProjects").IsEffectivelyVisible
                && Find("StudioOverviewReviewRadiant").IsEffectivelyVisible
                && Find("StudioOverviewBuild").IsEffectivelyVisible
                && Find("StudioOverviewPlaytest").IsEffectivelyVisible,
                "project management exposes persisted projects and review, build and launch actions");
            Check(((TextBlock)Find("StudioProjectPanelTitle")).Text=="ENVIRONMENT"
                && original.Host.Document!.Selection.Count==0
                && !(((TextBlock)Find("StudioProjectStatus")).Text??"").Contains("RMB orbit",StringComparison.Ordinal),
                "project management starts with environment settings, no object selection and Radiant workflow guidance");
            foreach (var page in new[]
            {
                ("StudioProjectInspector", "INSPECTOR"), ("StudioProjectMaterials", "MATERIALS"),
                ("StudioProjectAssets", "ASSETS & MUSIC"), ("StudioProjectPartitioning", "PARTITIONING"),
                ("StudioProjectCollisionRepairs", "COLLISION REPAIRS"),
                ("StudioProjectHealth", "MAP HEALTH"), ("StudioProjectEnvironment", "ENVIRONMENT")
            })
            {
                Check(Find(page.Item1).IsEffectivelyVisible,"project settings navigation remains discoverable: "+page.Item2);
                Press(page.Item1);
                Check(((TextBlock)Find("StudioProjectPanelTitle")).Text==page.Item2
                    && Find("StudioProjectOverview").IsVisible,
                    "project settings remain usable without entering native geometry recovery: "+page.Item2);
            }
            Check(StudioGlobalSearchWindow.Search(window.Documents,window.Commands,settings,"Radiant").Any(x=>x.Title=="Edit Map In Radiant"),
                "primary geometry action is available through actual Studio command search");
            var document = original.Host.Document!;
            string json = document.Project.Definition.Serialize();
            int savedProjectCount = ((ListBox)Find("StudioPersistedProjects")).ItemCount;
            complete(window.Commands.ExecuteAsync(StudioCommand.EditGeometryInRadiant));
            string expectedSource = RadiantSourceWorkspace.SourcePath(Path.Combine(CustomRooms.UserMapDirectory,"netradiant-sources"),document.Project.Definition.MapId);
            Check(launches.Count==1 && launches[0].FileName==tool && launches[0].ArgumentList.Last()==expectedSource
                && File.Exists(expectedSource),"shell geometry command launches the active map's protected source: "
                    + ((TextBlock)Find("StudioProjectStatus")).Text);
            Check(ReferenceEquals(original.Host.Document,document) && json==document.Project.Definition.Serialize() && !original.Dirty,
                "launch preserves canonical document ownership, identity and clean state");
            string brokenProject = Path.Combine(CustomRooms.UserMapDirectory,"radiant-library-broken-fixture.json");
            File.WriteAllText(brokenProject,"{ invalid project JSON");
            original.Host.ShowNativeGeometryTools();
            Press("StudioReturnToProjectOverview");
            var savedProjects = (ListBox)Find("StudioPersistedProjects");
            Check(File.Exists(RadiantSourceWorkspace.SidecarPath(expectedSource))
                && File.Exists(expectedSource+".prime-source.json")
                && savedProjects.ItemCount==savedProjectCount+1
                && savedProjects.ItemsSource!.Cast<object>().Any(row=>(row.ToString()??"").StartsWith(Path.GetFileName(brokenProject),StringComparison.Ordinal)),
                "saved projects exclude owned Radiant sidecars while retaining malformed user projects");
            File.AppendAllText(expectedSource,"\n// independently saved external revision\n");
            string saved = File.ReadAllText(expectedSource);
            complete(window.Commands.ExecuteAsync(StudioCommand.EditGeometryInRadiant));
            Check(launches.Count==2 && File.ReadAllText(expectedSource)==saved,
                "reopening in Radiant never overwrites external edits");
            complete(window.Commands.ExecuteAsync(StudioCommand.NewMapWorkspace));
            var native = (MapStudioDocument)window.Documents.ActiveDocument!;
            Check(launches.Count==2,"explicit native workspace creation remains a process-free fallback");
            // The library action travels through the shell's ownership route. It must
            // select the existing tab rather than replace this second document.
            native.Host.OpenGeometryInRadiant(source);
            var wait = Stopwatch.StartNew();
            while(launches.Count<3 && wait.Elapsed<TimeSpan.FromSeconds(10)) { Dispatcher.UIThread.RunJobs();Thread.Sleep(1); }
            Check(ReferenceEquals(window.Documents.ActiveDocument,original) && window.Documents.Documents.Count==2
                && launches[2].ArgumentList.Last()==expectedSource && native.Host.Document!=document,
                "library Radiant action selects the existing owner and retains other tabs");
            complete(window.Commands.ExecuteAsync(StudioCommand.NewMapInRadiant));
            var created = (MapStudioDocument)window.Documents.ActiveDocument!;
            Check(launches.Count==4 && created!=original && created!=native
                && launches[3].ArgumentList.Last()!=expectedSource,
                "explicit new-map Radiant action creates and launches a separate owned source");
            complete(window.Commands.ExecuteAsync(StudioCommand.ReviewRadiantGeometryChanges));
            Check(launches.Count==4 && created.Host.Document!.History.CanUndo==false,
                "review action does not silently apply geometry or launch another process");
            complete(window.Commands.ExecuteAsync(StudioCommand.NativeGeometryFallback));
            Check(created.Host.GetLogicalDescendants().OfType<Expander>().Any(e=>e.Name=="StudioNativeGeometryFallback" && e.IsExpanded),
                "native geometry fallback remains directly accessible");
            var createdDocument=created.Host.Document!;
            Control CreatedFind(string name) => created.Host.GetLogicalDescendants().OfType<Control>().Single(control=>control.Name==name);
            Check(!CreatedFind("StudioProjectOverview").IsVisible
                && CreatedFind("StudioSceneHierarchy").IsEffectivelyVisible
                && CreatedFind("StudioNativeViewport").IsEffectivelyVisible
                && CreatedFind("StudioNativeAuthoringToolbar").IsEffectivelyVisible
                && CreatedFind("StudioNativeCommandBar").IsEffectivelyVisible
                && created.Host.GetLogicalDescendants().OfType<GridSplitter>().All(splitter=>splitter.IsVisible),
                "explicit native recovery restores the full hierarchy, viewport, menus, splitters and authoring toolbar");
            createdDocument.Selection.Add(createdDocument.Project.Definition.Spawns[0].Id);
            createdDocument.SelectionChanged();
            Activate(CreatedFind("StudioReturnToProjectOverview"));
            Check(CreatedFind("StudioProjectOverview").IsEffectivelyVisible
                && !CreatedFind("StudioNativeViewport").IsVisible && !CreatedFind("StudioSceneHierarchy").IsVisible
                && createdDocument.Selection.Count==0 && ReferenceEquals(created.Host.Document,createdDocument)
                && !createdDocument.History.CanUndo,
                "returning to project overview clears selection and preserves canonical document and history");
            checks += PlaytestChecks(paths,source,complete);
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable,"0");
            Check(!window.Commands[StudioCommand.EditGeometryInRadiant].CanExecute(null)
                && window.Commands[StudioCommand.NativeGeometryFallback].CanExecute(null),
                "rollback disables primary launches while retaining native editing");
            created.Host.ShowPanel("Environment");
            Check(!CreatedFind("StudioProjectOverview").IsVisible
                && CreatedFind("StudioSceneHierarchy").IsVisible && CreatedFind("StudioNativeViewport").IsVisible
                && CreatedFind("StudioNativeAuthoringToolbar").IsVisible,
                "disabled Radiant defaults retain the original native editor layout");
        }
        finally
        {
            if(window!=null)
            {
                complete(window.Documents.RequestCloseAllAsync(_=>Task.FromResult(StudioCloseDecision.Discard),_=>Task.FromResult<string?>(null)));
                complete(window.TryCloseAsync());
                window.Close();
            }
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantExecutableVariable,oldEditor);
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable,oldDefault);
        }
        return checks;
    }

    private static int PlaytestChecks(StudioPaths paths,string source,Action<Task> complete)
    {
        int checks=0;
        void Check(bool condition,string label) { if(!condition)throw new InvalidOperationException(label);checks++; }
        var fixture=MapProjectSerializer.Load(source).ToDefinition();
        fixture.Spawns.Clear();
        fixture.Spawns.Add(new(){Position=new[]{1f,2,3},Yaw=37,Team=-1});
        fixture.Spawns.Add(new(){Position=new[]{-4f,5,6},Yaw=123,Team=0});
        fixture.Spawns.Add(new(){Position=new[]{7f,8,-9},Yaw=271,Team=1});
        string path=Path.Combine(paths.UserDataDirectory,"playtest-spawns.json");
        MapProjectExport.Save(fixture,path);
        var launches=new List<MapProject>();
        var host=new AvaloniaMapStudioHost((project,_)=>{launches.Add(project);return Task.CompletedTask;});
        var window=new Window {Content=host,Width=1280,Height=800};
        string? oldDefault=Environment.GetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable);
        try
        {
            complete(host.OpenAsync(path));window.Show();Dispatcher.UIThread.RunJobs();
            var document=host.Document!;
            document.Edit("Playtest history fixture",d=>d.Description="history remains available");
            document.History.Undo();
            string json=document.Project.Definition.Serialize();
            var state=document.CurrentStateId;
            int history=document.History.CommandCount;
            string Spawns(MapDefinition definition)=>System.Text.Json.JsonSerializer.Serialize(definition.Spawns);
            string authored=Spawns(document.Project.Definition);
            Control Find(string name)=>host.GetLogicalDescendants().OfType<Control>().Single(c=>c.Name==name);
            Control Button(string label)=>host.GetLogicalDescendants().OfType<Control>()
                .Single(c=>c.GetType().Name=="PrimeButton" && (string?)c.GetType().GetProperty("Label")!.GetValue(c)==label);
            bool HasButton(string label)=>host.GetLogicalDescendants().OfType<Control>()
                .Any(c=>c.GetType().Name=="PrimeButton" && (string?)c.GetType().GetProperty("Label")!.GetValue(c)==label);
            void Activate(Control control)
            {
                control.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,Source=control});
                control.RaiseEvent(new KeyEventArgs {RoutedEvent=InputElement.KeyUpEvent,Key=Key.Enter,Source=control});
            }
            void Launch(Action action)
            {
                int previous=launches.Count;action();
                var wait=Stopwatch.StartNew();
                while(launches.Count==previous && wait.Elapsed<TimeSpan.FromSeconds(10)) {Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
                Dispatcher.UIThread.RunJobs();
                Check(launches.Count==previous+1,"playtest action delivers one detached launch snapshot");
            }
            void Authored()=>Check(Spawns(launches[^1].Definition)==authored,
                "overview playtest retains every authored spawn identity, position, team and yaw");
            void Unchanged()=>Check(ReferenceEquals(host.Document,document) && document.Project.Definition.Serialize()==json
                && document.CurrentStateId==state && document.History.CommandCount==history
                && document.History.CanRedo && !document.IsDirty,
                "playtest preserves canonical document, save state and undo/redo history");
            Activate(Find("StudioOverviewPlaytest"));
            Check(HasButton("FROM MAP SPAWNS") && !HasButton("FROM CAMERA") && !HasButton("FROM SELECTED SPAWN"),
                "overview exposes map starts without hidden camera or selection actions");
            Launch(()=>Activate(Button("FROM MAP SPAWNS")));Authored();Unchanged();
            Check(!ReferenceEquals(launches[^1].Definition.Spawns[0].Position,document.Project.Definition.Spawns[0].Position),
                "overview launch spawn coordinates are detached from the authoring document");
            complete(host.PlaytestAsync());Authored();Unchanged();
            foreach(var modifier in new[]{KeyModifiers.Control,KeyModifiers.Meta})
            {
                Launch(()=>((Control)host.Content!).RaiseEvent(new KeyEventArgs
                    {RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter,KeyModifiers=modifier,Source=host.Content}));
                Authored();Unchanged();
            }
            // Team starts remain explicit even when selection and viewport are hidden.
            Launch(()=>Activate(Button("FROM TEAM B SPAWN")));
            var selected=launches[^1].Definition.Spawns.Single();
            Check(selected.Team==1 && selected.Yaw==271 && selected.Position.SequenceEqual(new[]{7f,8,-9}),
                "overview explicit team start retains authored position and facing");Unchanged();
            host.ShowNativeGeometryTools();
            var viewport=Find("StudioNativeViewport").GetLogicalDescendants().OfType<Control>().Single(c=>c.GetType().Name=="MapViewport");
            var camera=(System.Numerics.Vector3)viewport.GetType().GetProperty("CameraPosition")!.GetValue(viewport)!;
            Activate(host.GetLogicalDescendants().OfType<Control>().Single(c=>c.GetValue(ControllerNav.NavIdProperty)=="studio.playtest"));
            Check(HasButton("FROM CAMERA") && HasButton("FROM SELECTED SPAWN") && !HasButton("FROM MAP SPAWNS"),
                "explicit geometry recovery retains camera and selected-spawn controls");
            complete(host.PlaytestAsync());
            selected=launches[^1].Definition.Spawns.Single();
            Check(selected.Position.SequenceEqual(new[]{camera.X,camera.Y,camera.Z}) && selected.Team==-1 && selected.Yaw==0,
                "explicit geometry recovery retains default camera playtesting");Unchanged();
            document.Selection.Add(document.Project.Definition.Spawns[1].Id);
            Launch(()=>Activate(Button("FROM SELECTED SPAWN")));
            selected=launches[^1].Definition.Spawns.Single();
            Check(selected.Team==0 && selected.Yaw==123 && selected.Position.SequenceEqual(new[]{-4f,5,6}),
                "explicit geometry recovery retains selected spawn playtesting");Unchanged();
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable,"0");
            host.ShowPanel("Environment");complete(host.PlaytestAsync());
            Check(launches[^1].Definition.Spawns.Single().Position.SequenceEqual(new[]{camera.X,camera.Y,camera.Z}),
                "disabled Radiant default retains legacy camera playtesting");Unchanged();
        }
        finally
        {
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable,oldDefault);
            complete(host.DisposeAsync(false).AsTask());window.Close();
        }
        return checks;
    }
}
