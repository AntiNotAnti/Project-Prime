using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
}
