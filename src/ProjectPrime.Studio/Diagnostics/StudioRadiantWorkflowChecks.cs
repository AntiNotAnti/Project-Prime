using System.Diagnostics;
using Avalonia.Controls;
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
            Check(launches.Count==0,"opening/restoring a map never implicitly launches Radiant");
            Check(original.Host.GetLogicalDescendants().OfType<Control>().Any(b=>b.Name=="StudioEditGeometryInRadiant" && b.IsVisible),
                "installed Radiant has a discoverable primary geometry action");
            Check(StudioGlobalSearchWindow.Search(window.Documents,window.Commands,settings,"Radiant").Any(x=>x.Title=="Edit Geometry In Radiant"),
                "primary geometry action is available through actual Studio command search");
            var document = original.Host.Document!;
            string json = document.Project.Definition.Serialize();
            complete(window.Commands.ExecuteAsync(StudioCommand.EditGeometryInRadiant));
            string expectedSource = RadiantSourceWorkspace.SourcePath(Path.Combine(CustomRooms.UserMapDirectory,"netradiant-sources"),document.Project.Definition.MapId);
            Check(launches.Count==1 && launches[0].FileName==tool && launches[0].ArgumentList.Last()==expectedSource
                && File.Exists(expectedSource),"shell geometry command launches the active map's protected source");
            Check(ReferenceEquals(original.Host.Document,document) && json==document.Project.Definition.Serialize() && !original.Dirty,
                "launch preserves canonical document ownership, identity and clean state");
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
            Environment.SetEnvironmentVariable(CommunityCutoverPolicy.NetRadiantDefaultVariable,"0");
            Check(!window.Commands[StudioCommand.EditGeometryInRadiant].CanExecute(null)
                && window.Commands[StudioCommand.NativeGeometryFallback].CanExecute(null),
                "rollback disables primary launches while retaining native editing");
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
