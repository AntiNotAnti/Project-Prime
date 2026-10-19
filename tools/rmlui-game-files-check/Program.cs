using MphRead.Mods.Launcher.Core;

int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new InvalidOperationException(label);
    checks++; Console.WriteLine("ROM AUTO IMPORT PASS " + label);
}
var authority = new Authority();
using (var controller = new NativeGameFilesController(authority, () => true))
{
    Check(controller.Choose() && authority.Installs == 0, "opening picker alone does not start extraction");
    var state = controller.Tick();
    Check(authority.Installs == 1 && authority.Path == "chosen.nds" && authority.Root == "original-root"
        && state.Busy && !state.Confirming, "valid file selection starts one import into captured folder without confirmation");
    controller.Tick(); controller.Tick();
    Check(!controller.Choose() && !controller.Confirm() && authority.Installs == 1,
        "duplicate input and frames cannot import twice");
    authority.State = authority.State with { Ready = true };
    authority.Install.TrySetResult(new(true, "Ready to play."));
    Until(controller, () => !controller.Snapshot().Busy);
    Check(controller.TryTakeInstalled() && !controller.TryTakeInstalled(),
        "automatic import publishes the installed receipt exactly once");
}
foreach (var pick in new NativeGameFilesPick?[] { null, new("", "", ""), new("bad.nds", "bad.nds", "", "Unsupported cartridge") })
{
    var fake = new Authority { Pick = Task.FromResult(pick) };
    using var controller = new NativeGameFilesController(fake, () => true);
    controller.Choose(); var state = controller.Tick();
    Check(fake.Installs == 0 && !state.Busy && !state.Confirming, "canceled empty or invalid selection never imports or opens confirmation");
}
foreach (bool busy in new[] { false, true })
{
    var fake = new Authority();
    using var controller = new NativeGameFilesController(fake, () => true);
    controller.Choose();
    fake.State = fake.State with { Root = busy ? "original-root" : "changed-root", SetupBusy = busy };
    controller.Tick();
    Check(fake.Installs == 0 && !controller.Snapshot().Confirming,
        "destination change or competing setup rejects automatic import");
}
{
    bool current = true; var fake = new Authority();
    using var controller = new NativeGameFilesController(fake, () => current);
    controller.Choose(); current = false;
    Check(controller.Tick().Closed && fake.Installs == 0, "retired page cannot import a returned selection");
}
{
    using var lifetime = new CancellationTokenSource(); var fake = new Authority();
    using var controller = new NativeGameFilesController(fake, () => true, lifetime.Token);
    controller.Choose(); lifetime.Cancel();
    Check(controller.Tick().Closed && fake.Installs == 0, "canceled lifetime cannot import a returned selection");
}
{
    bool foreground = false; var fake = new Authority();
    using var controller = new NativeGameFilesController(fake, () => true, canStartImport: () => foreground);
    controller.Choose();
    Check(controller.Tick().Busy && fake.Installs == 0, "covered setup defers picker completion without losing it");
    foreground = true;
    Check(controller.Tick().Busy && fake.Installs == 1 && !controller.Snapshot().Confirming,
        "returning to setup starts deferred import automatically");
}
{
    var picked = new TaskCompletionSource<NativeGameFilesPick?>(TaskCreationOptions.RunContinuationsAsynchronously);
    var fake = new Authority { Pick = picked.Task };
    using var controller = new NativeGameFilesController(fake, () => true);
    controller.Choose(); Check(controller.Cancel(), "picker wait can be canceled");
    picked.SetResult(new("late.nds", "late.nds", "test"));
    controller.Tick();
    Check(fake.Installs == 0 && !controller.Snapshot().Confirming, "late canceled picker completion stays retired");
}
{
    var fake = new Authority(); bool current = true;
    fake.OnRead = count => { if (count == 3) current = false; };
    using var controller = new NativeGameFilesController(fake, () => current);
    controller.Choose(); controller.Tick();
    Check(fake.Installs == 0 && !controller.Snapshot().Confirming,
        "ownership retirement during admission read prevents extraction");
}
{
    var fake = new Authority { OnRead = count => { if (count == 3) throw new IOException("private fixture detail"); } };
    using var controller = new NativeGameFilesController(fake, () => true);
    controller.Choose(); var state = controller.Tick();
    Check(fake.Installs == 0 && !state.Busy && !state.Confirming && !state.Status.Contains("private fixture detail"),
        "failed admission read leaves no pending confirmation or leaked exception");
}
{
    var fake = new Authority();
    using var controller = new NativeGameFilesController(fake, () => true);
    controller.Choose(); controller.Tick();
    fake.Install.SetResult(new(false, "Invalid cartridge"));
    Until(controller, () => !controller.Snapshot().Busy);
    Check(!controller.TryTakeInstalled() && controller.Choose(), "failed import can retry file selection without a confirmation step");
}
Console.WriteLine($"{checks} checks passed; synthetic authority only, no cartridge reads or writes.");

static void Until(NativeGameFilesController controller, Func<bool> done)
{
    var time = System.Diagnostics.Stopwatch.StartNew();
    while (!done())
    {
        if (time.ElapsedMilliseconds > 3000) throw new TimeoutException("controller completion");
        controller.Tick(); Thread.Sleep(1);
    }
}
sealed class Authority : INativeGameFilesAuthority
{
    public NativeGameFilesState State = new(false, "Choose game files", "original-root", true, false, 0);
    public Task<NativeGameFilesPick?> Pick = Task.FromResult<NativeGameFilesPick?>(new("chosen.nds", "chosen.nds", "test"));
    public TaskCompletionSource<NativeGameFilesResult> Install = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Installs, Reads; public string Path = "", Root = ""; public Action<int>? OnRead;
    public NativeGameFilesState Read() { OnRead?.Invoke(++Reads); return State; }
    public Task<NativeGameFilesPick?> PickAsync(CancellationToken token) => Pick;
    public Task<NativeGameFilesResult> InstallAsync(string path, string expectedRoot, Action<string> report, CancellationToken token)
    { Installs++; Path = path; Root = expectedRoot; report("Extracting files"); return Install.Task; }
    public Task<NativeGameFilesResult> PreviewsAsync(Action<string> report, CancellationToken token) => throw new InvalidOperationException("not used");
}
