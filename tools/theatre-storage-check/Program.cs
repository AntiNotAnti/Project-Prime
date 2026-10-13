using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;

Headless.Enter(); // Must precede all UI feedback/music references.
NativeFilePicker.Suppressed = true;
AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = true }).SetupWithoutStarting();
AvaloniaSynchronizationContext.InstallIfNeeded();
const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
string directory = Path.Combine(Path.GetTempPath(), "prime-theatre-storage-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
// Seed only owned paths, avoiding path discovery/user files and external accounts.
var paths = (Dictionary<string, string>)typeof(Paths).GetField("_allPaths", statics)!.GetValue(null)!;
paths["Export"] = directory;
Directory.CreateDirectory(DemoLibrary.Directory);
string source = Path.Combine(DemoLibrary.Directory, "fixture.ppdemo");
File.WriteAllBytes(source, DemoFile.Magic.Concat(new byte[] { 1, NetConfig.ProtocolVersion }).ToArray());
int passed = 0, failed = 0;
void Require(bool value) { if (!value) throw new InvalidOperationException("contract failed"); }
T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, instance)!.GetValue(owner)!;
void Select(TheatreWorkspace view, string path) => typeof(TheatreWorkspace).GetField("_selected", instance)!.SetValue(view, path);
void Click(TheatreWorkspace view, string name)
{
    var button = Field<HubNavButton>(view, name);
    var handler = (EventHandler)typeof(HubNavButton).GetField("Click", instance)!.GetValue(button)!;
    handler(button, EventArgs.Empty); // Exact installed action callback, no native input/window.
}
int Waiters()
{
    var slots = (SemaphoreSlim)typeof(ReplayStorageJobs).GetField("Slots", statics)!.GetValue(null)!;
    var head = typeof(SemaphoreSlim).GetField("m_asyncHead", instance) ?? typeof(SemaphoreSlim).GetField("_asyncHead", instance);
    if (head == null) throw new InvalidOperationException("SDK semaphore witness field missing");
    int count = 0; object? node = head.GetValue(slots);
    while (node != null)
    {
        if (++count > 100000) throw new InvalidOperationException("invalid waiter graph");
        node = node.GetType().GetField("Next", instance | BindingFlags.Public)!.GetValue(node);
    }
    return count;
}
bool Idle()
{
    var slots = (SemaphoreSlim)typeof(ReplayStorageJobs).GetField("Slots", statics)!.GetValue(null)!;
    return Waiters() == 0 && slots.CurrentCount == 2;
}
void Pump(Func<bool> complete)
{
    var watch = System.Diagnostics.Stopwatch.StartNew();
    while (watch.Elapsed < TimeSpan.FromSeconds(10))
    {
        Dispatcher.UIThread.RunJobs();
        if (complete()) { Dispatcher.UIThread.RunJobs(); if (complete()) return; }
        Thread.Sleep(1);
    }
    Require(false);
}
void Check(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.GetType().Name}: {ex.Message}"); }
}
void Blocked(Action test)
{
    using var release = new ManualResetEventSlim(); using var started = new CountdownEvent(2);
    Task<bool> Block() => ReplayStorageJobs.Run(() => { started.Signal(); release.Wait(); return true; });
    Task<bool>[] workers = [Block(), Block()];
    try { Require(started.Wait(TimeSpan.FromSeconds(10))); test(); }
    finally { release.Set(); Pump(() => workers.All(t => t.IsCompleted) && Idle()); Task.WaitAll(workers); }
}
try
{
    Check("favorite callbacks coalesce library scan ownership", () => Blocked(() => {
        using var view = new TheatreWorkspace(manageStorage: false); Select(view, source);
        for (int i = 0; i < 128; i++) Click(view, "_favorite");
        int pending = Waiters(); Console.WriteLine($"favorite waiter graph: {pending}");
        Require(pending == 1);
        Require(!File.Exists(source + ".favorite")); // Every favorite mutation still executes.
    }));
    Check("file export callbacks admit one task with clear busy feedback", () => Blocked(() => {
        using var view = new TheatreWorkspace(manageStorage: false); Select(view, source);
        for (int i = 0; i < 128; i++) Click(view, "_export");
        int pending = Waiters(); Console.WriteLine($"export waiter graph: {pending}");
        Require(pending == 1);
        Require(Field<TextBlock>(view, "_status").Text?.Contains("already", StringComparison.OrdinalIgnoreCase) == true);
    }));
    // An unused drawing-disabled control is retained independently of storage.
    // Record the WeakReference observation without attributing it to this producer.
    WeakReference unused = UnusedWorkspace();
    Pump(Idle); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    Console.WriteLine($"INCONCLUSIVE full-view collection: unused workspace retained={unused.IsAlive}");
    Check("disposed workspace cancels queued scan and clears request ownership", () => Blocked(() => {
        WeakReference reference = QueueAndDispose(source);
        Pump(() => !Reloading(reference));
        Require(Waiters() == 0);
        Require(ReloadReferencesCleared(reference));
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Console.WriteLine($"disposed workspace retained outside cleared queue: {reference.IsAlive}; waiters: {Waiters()}");
    }));
    Check("coalesced scan publishes latest selection and favorite state", () => {
        string second = Path.Combine(DemoLibrary.Directory, "latest.ppdemo");
        File.WriteAllBytes(second, File.ReadAllBytes(source));
        using var view = new TheatreWorkspace(manageStorage: false);
        Blocked(() => {
            Select(view, source); Click(view, "_favorite");
            typeof(TheatreWorkspace).GetField("_selectionGeneration", instance)!.SetValue(view, 1L);
            Select(view, second);
            for (int i = 0; i < 127; i++) Click(view, "_favorite");
            Require(Waiters() == 1);
        });
        Require(Field<string>(view, "_selected") == second);
        var entries = Field<System.Collections.IEnumerable>(view, "_entries").Cast<object>().ToArray();
        var latest = entries.Single(e => (string)e.GetType().GetProperty("Path")!.GetValue(e)! == second);
        Require((bool)latest.GetType().GetProperty("Favorite")!.GetValue(latest)!);
        Require(Field<TextBlock>(view, "_summary").Text != "SCANNING LIBRARY...");
    });
    Check("accepted export writes one exact copy and restores controls", () => {
        string exports = Path.Combine(DemoLibrary.Directory, "exports");
        string sentinel = Path.Combine(exports, "user-owned.bin");
        Directory.CreateDirectory(exports); File.WriteAllBytes(sentinel, [9, 8, 7]);
        int before = Directory.GetFiles(exports, "*.ppdemo").Length;
        byte[] expected = File.ReadAllBytes(source);
        using var view = new TheatreWorkspace(manageStorage: false);
        Select(view, source);
        Blocked(() => {
            Click(view, "_export");
            Require(!Field<HubNavButton>(view, "_export").IsEnabled);
            typeof(TheatreWorkspace).GetMethod("SetActionState", instance)!.Invoke(view, [true, false]);
            Require(!Field<HubNavButton>(view, "_export").IsEnabled);
            for (int i = 0; i < 127; i++) Click(view, "_export");
            Require(Waiters() == 1);
        });
        string[] copies = Directory.GetFiles(exports, "*.ppdemo");
        Require(copies.Length == before + 1 && copies.All(path => File.ReadAllBytes(path).SequenceEqual(expected)));
        Require(File.ReadAllBytes(sentinel).SequenceEqual(new byte[] { 9, 8, 7 }));
        Require(Field<HubNavButton>(view, "_export").IsEnabled && Field<HubNavButton>(view, "_export").Label == "EXPORT");
    });
    Check("export I/O failure restores admission and allows retry", () => {
        string missing = Path.Combine(directory, "missing.ppdemo");
        using var view = new TheatreWorkspace(manageStorage: false); Select(view, missing);
        Blocked(() => { Click(view, "_export"); Require(Waiters() == 1); });
        Require(Field<HubNavButton>(view, "_export").IsEnabled);
        Require(Field<TextBlock>(view, "_status").Text?.Contains("failed", StringComparison.OrdinalIgnoreCase) == true);
        File.WriteAllBytes(missing, File.ReadAllBytes(source));
        Blocked(() => { Click(view, "_export"); Require(Waiters() == 1); });
        Require(Field<TextBlock>(view, "_status").Text?.StartsWith("EXPORTED", StringComparison.Ordinal) == true);
    });
    Check("accepted export survives disposal without stale feedback or deletion", () => {
        string exports = Path.Combine(DemoLibrary.Directory, "exports");
        int before = Directory.GetFiles(exports, "*.ppdemo").Length;
        using var view = new TheatreWorkspace(manageStorage: false); Select(view, source);
        Blocked(() => {
            Click(view, "_export"); view.Dispose();
            Field<TextBlock>(view, "_status").Text = "retired";
            Click(view, "_favorite"); Click(view, "_export");
            Require(Waiters() == 1);
        });
        Require(Directory.GetFiles(exports, "*.ppdemo").Length == before + 1);
        Require(Field<TextBlock>(view, "_status").Text == "retired");
    });
}
finally { Pump(Idle); Directory.Delete(directory, recursive: true); }
Console.WriteLine($"{passed} PASS / {failed} FAIL");
return failed == 0 ? 0 : 1;

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference QueueAndDispose(string source)
{
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var view = new TheatreWorkspace(manageStorage: false);
    typeof(TheatreWorkspace).GetField("_selected", flags)!.SetValue(view, source);
    var button = (HubNavButton)typeof(TheatreWorkspace).GetField("_favorite", flags)!.GetValue(view)!;
    var click = (EventHandler)typeof(HubNavButton).GetField("Click", flags)!.GetValue(button)!;
    for (int i = 0; i < 128; i++) click(button, EventArgs.Empty);
    view.Dispose(); return new WeakReference(view);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static bool Reloading(WeakReference reference)
{
    object? target = reference.Target;
    if (target == null) return false;
    var field = typeof(TheatreWorkspace).GetField("_reloading", BindingFlags.Instance | BindingFlags.NonPublic);
    return field != null && (bool)field.GetValue(target)!;
}

[MethodImpl(MethodImplOptions.NoInlining)]
static WeakReference UnusedWorkspace()
{
    var view = new TheatreWorkspace(manageStorage: false);
    view.Dispose(); return new WeakReference(view);
}

[MethodImpl(MethodImplOptions.NoInlining)]
static bool ReloadReferencesCleared(WeakReference reference)
{
    object? target = reference.Target;
    if (target == null) return true;
    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    var request = ((long, long, string?, string?))typeof(TheatreWorkspace).GetField("_reloadRequest", flags)!.GetValue(target)!;
    return typeof(TheatreWorkspace).GetField("_reloadCancellation", flags)!.GetValue(target) == null
        && request == default;
}
