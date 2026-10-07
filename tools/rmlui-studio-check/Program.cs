using System.Runtime.InteropServices;
using System.Text.Json;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.StudioIntegration;

int checks = 0;
void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
var backend = new FakeStudioBackend();
using (var controller = new StudioController(backend))
{
    var initial = controller.Snapshot;
    controller.Pump();
    Check(ReferenceEquals(initial, controller.Snapshot), "Unchanged Studio snapshot identity/version drifted");
    Check(controller.Launch().Accepted && backend.Launches[^1] == (null, false), "Studio launch did not use the paired app without a document");
    Check(controller.Recover().Accepted && backend.Launches[^1] == (null, true), "Studio session recovery lost the existing recover flag");
    Check(!controller.OpenPath().Accepted && backend.Launches.Count == 2 && controller.Snapshot.Error.Length > 0,
        "Empty path was sent to the external application");
    controller.Pump();
    Check(controller.Snapshot.Error.Length > 0, "Periodic Studio refresh cleared the launch error");
    foreach (string path in new[] { "map.json", "package.ppmap", "replay.ppdemo", "clip.ppclip" })
        Check(controller.OpenPath(path).Accepted && backend.Launches[^1].Path == Path.GetFullPath(path), "Supported document path was not canonicalized");
    Check(!controller.OpenPath("other.txt").Accepted && backend.Launches.Count == 6, "Unsupported file was misrouted to Map Studio");
    backend.LaunchError = "The paired Studio installation could not start.";
    Check(!controller.Launch().Accepted && controller.Snapshot.Error == backend.LaunchError && !controller.Snapshot.Busy,
        "Paired-app launch failure was not retained as native UI feedback");
    backend.ThrowLaunch = true;
    Check(!controller.Recover().Accepted && !controller.Snapshot.Error.Contains("private-token"),
        "Unexpected launch failure escaped or exposed backend exception content");
    backend.ThrowLaunch = false; backend.LaunchError = "";
    controller.SetPath("private directory/map.json");
    Check(!JsonSerializer.Serialize(controller.Snapshot).Contains("private directory"), "Path draft escaped into a view snapshot");
    Check(!controller.SetPath("invalid\0path").Accepted && !controller.SetPath(new string('x',32768)).Accepted,
        "Invalid/NUL/oversize path drafts were accepted");
    Check(Task.Run(() => { try { controller.Launch(); return false; } catch(InvalidOperationException) { return true; } })
        .GetAwaiter().GetResult(), "Worker thread could launch Studio");
}

backend = new();
using (var controller = new StudioController(backend))
{
    controller.SetPath("draft.json");
    Check(controller.PickMap().Accepted && controller.Snapshot.Busy && !controller.Snapshot.CanLaunch, "Native picker did not acquire operation ownership");
    Check(!controller.PickMap().Accepted && !controller.Launch().Accepted, "Overlapping Studio external actions were allowed");
    Check(controller.Cancel().Accepted && backend.Picks[0].Token.IsCancellationRequested && controller.PathDraft == "draft.json",
        "Picker cancellation failed to retire its request or preserve the manual draft");
    backend.Picks[0].Complete(new("late.json")); controller.Pump();
    Check(backend.Launches.Count == 0 && controller.PathDraft == "draft.json", "Canceled file selection launched Studio");
    controller.PickMap();
    Task.Run(() => backend.Picks[^1].Complete(new("chosen.json"))).GetAwaiter().GetResult();
    Check(backend.Launches.Count == 0 && controller.Snapshot.Busy, "Worker completion opened an application before owner Pump");
    controller.Pump();
    Check(backend.Launches.Count == 1 && backend.LaunchThread == Environment.CurrentManagedThreadId
        && controller.PathDraft == "chosen.json" && !controller.Snapshot.Busy, "Current selection did not launch exactly once on its owner");
    controller.PickMap(); backend.Picks[^1].Complete(new("replay.ppdemo")); controller.Pump();
    Check(backend.Launches.Count == 1 && controller.Snapshot.Error.Contains("map"), "Map picker launched a non-map document");
    controller.PickMap(); backend.Picks[^1].Complete(new(Error:"Native dialog failed.")); controller.Pump();
    Check(controller.Snapshot.Error == "Native dialog failed." && !controller.Snapshot.Busy, "Picker failure was not visible and recoverable");
    controller.PickMap(); backend.Picks[^1].Complete(new()); controller.Pump();
    Check(controller.Snapshot.Status.Contains("No file selected") && controller.Snapshot.Error.Length == 0, "Picker cancellation was reported as a launch failure");
    for (int cycle = 0; cycle < 100; cycle++)
    {
        controller.PickMap(); controller.Cancel(); backend.Picks[^1].Complete(new("late.json")); controller.Pump();
        Check(backend.Launches.Count == 1 && !controller.Snapshot.Busy, "Repeated cancellation accepted a late selection");
    }
}
backend = new();
var retired = new StudioController(backend);
retired.PickMap(); retired.Dispose(); retired.Dispose(); backend.Picks[0].Complete(new("late.json")); retired.Pump();
Check(retired.Snapshot.Closed && !retired.Snapshot.CanLaunch && retired.PathDraft == "" && backend.Launches.Count == 0,
    "Retired controller accepted or retained a late launch");
backend = new() { PickerAvailable = false };
using (var controller = new StudioController(backend))
    Check(!controller.PickMap().Accepted && controller.Snapshot.Error.Contains("Enter a map project path"), "No-dialog platform lacked a usable typed path fallback");

string fixtures = Path.Combine(Path.GetTempPath(), "prime-studio-contract-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixtures);
try
{
    var engine = new LauncherStudioBackend();
    foreach (var file in new[] { (Name:"Map $(literal) λ.json", Kind:StudioDocumentKind.Map, Flag:"--map"),
        (Name:"package.ppmap",Kind:StudioDocumentKind.Map,Flag:"--map"), (Name:"replay.PPDEMO",Kind:StudioDocumentKind.Replay,Flag:"--replay"),
        (Name:"clip.ppclip",Kind:StudioDocumentKind.Clip,Flag:"--clip") })
    {
        string path = Path.Combine(fixtures,file.Name); File.WriteAllText(path,"fixture");
        var validation = engine.ValidatePath("\""+path+"\"");
        Check(validation.Valid && validation.Kind==file.Kind && validation.Path==path, "Existing launcher document routing lost Unicode/quoted path handling");
        var start = StudioApplicationLauncher.CreateStartInfo(Path.Combine(fixtures,"ProjectPrimeStudio"),validation.Path,recover:true);
        Check(!start.UseShellExecute && start.ArgumentList.SequenceEqual(new[] {file.Flag,path,"--recover"})
            && start.Environment.ContainsKey("PROJECT_PRIME_STUDIO_LAUNCH_SECRET"), "Existing Studio command arguments/authenticated environment were changed");
    }
    Check(!engine.ValidatePath(Path.Combine(fixtures,"missing.json")).Valid && !engine.ValidatePath("\0").Valid,
        "Real path validator accepted nonexistent/invalid files");
    string ignored = Path.Combine(fixtures,"unsupported.txt"); File.WriteAllText(ignored,"fixture");
    Check(!engine.ValidatePath(ignored).Valid, "Real path validator sent an unsupported file to Map Studio");
}
finally { Directory.Delete(fixtures,true); }

if (args.Length == 2 && args[0] == "--native")
{
    nint module = NativeLibrary.Load(Path.GetFullPath(args[1]));
    NativeLibrary.SetDllImportResolver(typeof(RmlUiHost).Assembly,(name,_,_)=>name=="ProjectPrime.RmlUi.Native"?module:0);
    try
    {
        foreach(var size in new[] {(W:640,H:320,D:1f),(W:1280,H:720,D:1f),(W:2560,H:1440,D:2f),(W:2560,H:1440,D:1f)})
        {
            using var host = new RmlUiHost();
            Check(host.Initialize(size.W,size.H,size.D,Path.Combine(AppContext.BaseDirectory,"rmlui"),RmlUiRenderBackend.DrawList),"Actual native Studio host did not initialize");
            using var pages = new RmlUiPageManager(host);
            backend = new() { LaunchError = "Paired Studio launch failure // retry" };
            using var presenter = new StudioPagePresenter(host,pages,new(backend));
            presenter.Open(); host.Update(); host.Render(size.W,size.H);
            Check(pages.Page==presenter.Document && pages.PageKey=="studio" && !host.IsVisible(host.HomeDocument),"Studio created a parallel manager or leaked baseline presentation");
            Check(host.TryGetElementBounds(presenter.Document,"studio_replay_library",out _,out _,out float replayWidth,out float replayHeight)
                && replayWidth>0 && replayHeight>0,"Replay Library entry has no real native layout");
            Check(host.FocusDocument(presenter.Document,"studio_replay_library"),"Replay Library entry cannot receive native focus");
            host.Update(); host.Render(size.W,size.H);
            Check(host.TryGetElementBounds(presenter.Document,"studio_replay_library",out float replayX,out float replayY,out replayWidth,out replayHeight)
                && replayX>=0 && replayY>=0 && replayX+replayWidth<=size.W && replayY+replayHeight<=size.H,
                "Replay Library entry cannot be reached inside the actual viewport");
            host.Input.Key(2,true); host.Input.Key(2,false); host.Update();
            Check(host.TryTakeIntent(out var replayIntent) && replayIntent.Kind==RmlUiIntentKind.Navigate
                && replayIntent.Argument==(int)RmlUiRouteArgument.Theatre && replayIntent.Document==presenter.Document
                && pages.Accept(replayIntent),"Replay Library DOM action did not emit the real typed Theatre route");
            void Click(string id)
            {
                Check(host.TryGetElementBounds(presenter.Document,id,out float x,out float y,out float w,out float h)
                    && w>0 && h>0 && float.IsFinite(x) && float.IsFinite(y),"Studio action has no real layout bounds: "+id);
                Check(host.FocusDocument(presenter.Document,id),"Studio action could not acquire focus: "+id);
                host.Update(); host.Render(size.W,size.H);
                Check(host.TryGetElementBounds(presenter.Document,id,out x,out y,out w,out h)
                    && x>=0 && y>=0 && x+w<=size.W && y+h<=size.H,"Studio action is not reachable after native focus: "+id);
                if(id=="studio_launch")
                {
                    host.Input.PointerMoved(x+w/2,y+h/2);
                    host.Input.PointerButton(0,x+w/2,y+h/2,true);host.Input.PointerButton(0,x+w/2,y+h/2,false);
                }
                else { host.Input.Key(2,true);host.Input.Key(2,false); }
                host.Update();
                Check(host.TryTakeIntent(out var intent) && presenter.Handle(intent),"Actual Studio DOM event did not cross typed intent boundary: "+id);
                host.Update();host.Render(size.W,size.H);
            }
            Click("studio_launch");
            Check(presenter.Controller.Snapshot.Error==backend.LaunchError && host.IsVisible(presenter.Document),"Launch failure left the native Studio page");
            Check(host.TryGetElementBounds(presenter.Document,"studio_error",out _,out _,out float errorWidth,out float errorHeight)
                && errorWidth>0 && errorHeight>0,"Launch error had no visible native document layout");
            host.SetField(presenter.Document,"studio_path","chosen.ppdemo");
            Click("studio_open_path");
            Check(backend.Launches[^1].Path==Path.GetFullPath("chosen.ppdemo"),"Native editable path did not reach the original replay launch contract");
            presenter.Refresh();
            Check(host.ReadField(presenter.Document,"studio_path")=="chosen.ppdemo","Periodic native Studio refresh erased the manual path");
            Click("studio_recover");
            Check(backend.Launches[^1].Recover,"Native recovery lost the recover command");
            Click("studio_pick_map");
            Click("studio_cancel");
            Check(!presenter.Controller.Snapshot.Busy && host.IsAlive(presenter.Document) && backend.Picks[0].Token.IsCancellationRequested,
                "Native cancel closed the page instead of retiring its picker request");
            Click("studio_pick_map");
            var token=presenter.Document;
            pages.OpenPage(new("replacement","pages/home/home.rml","deploy"));
            Check(!host.IsAlive(token) && presenter.Controller.Snapshot.Closed && backend.Picks[^1].Token.IsCancellationRequested,
                "Native document retirement did not cancel its picker ownership");
            int launches = backend.Launches.Count; backend.Picks[^1].Complete(new("late.json")); presenter.Refresh();
            Check(backend.Launches.Count==launches && pages.PageKey=="replacement","Late native Studio picker selection launched after route replacement");
        }
    }
    finally { NativeLibrary.Free(module); }
}
Console.WriteLine($"Studio entry checks passed: {checks} assertions covering existing launch arguments, native error recovery, picker ownership/cancellation, path handling and document retirement.");

sealed class FakeStudioBackend : IStudioEntryBackend
{
    public sealed class Pick(CancellationToken token)
    {
        public CancellationToken Token { get; } = token;
        public TaskCompletionSource<StudioPickResult> Completion { get; } = new();
        public void Complete(StudioPickResult result) => Completion.TrySetResult(result);
    }
    public bool PickerAvailable=true, ThrowLaunch;
    public string LaunchError="";
    public int LaunchThread;
    public List<(string? Path,bool Recover)> Launches { get; } = new();
    public List<Pick> Picks { get; } = new();
    public StudioAvailability Availability()=>new(true,true,PickerAvailable,"Paired test Studio ready");
    public StudioPathResult ValidatePath(string path)
    {
        StudioDocumentKind? kind=Path.GetExtension(path).ToLowerInvariant() switch {".json" or ".ppmap"=>StudioDocumentKind.Map,".ppdemo"=>StudioDocumentKind.Replay,".ppclip"=>StudioDocumentKind.Clip,_=>null};
        return kind.HasValue?new(true,Path.GetFullPath(path),kind.Value):new(false,Error:"Choose a supported Studio document.");
    }
    public StudioActionResult Launch(string? documentPath,bool recover)
    {
        if(ThrowLaunch)throw new InvalidOperationException("private-token");
        LaunchThread=Environment.CurrentManagedThreadId;Launches.Add((documentPath,recover));
        return new(LaunchError.Length==0,LaunchError);
    }
    public Task<StudioPickResult> PickMapAsync(CancellationToken cancellationToken)
    {var pick=new Pick(cancellationToken);Picks.Add(pick);return pick.Completion.Task;}
}
