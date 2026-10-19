using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;

int checks = 0;
void Check(bool value, string name)
{
    if (!value) throw new InvalidOperationException(name);
    checks++; Console.WriteLine("RML STUDIO PASS " + name);
}
void Pump(NativeStudioEntryController controller, Func<bool> done)
{
    for (int step = 0; step < 1000; step++)
    { _ = controller.Current(); if (done()) return; Thread.Sleep(1); }
    throw new InvalidOperationException("Timed out waiting for owned Studio completion");
}
var gateway = new Fake();
using (var studio = new NativeStudioEntryController(gateway))
{
    Check(!studio.Current().Busy && gateway.LaunchCalls == 0 && gateway.OpenCalls == 0,
        "opening native workspace only reads the detached catalog");
    gateway.AllowLaunch = false;
    Check(!studio.Launch() && !studio.Current().Closed && gateway.LaunchCalls == 1
        && studio.Current().Status.Contains("UNAVAILABLE"), "launch refusal retains native workspace");
    gateway.AllowLaunch = true;
    Check(studio.Launch() && studio.Current().Closed && gateway.LaunchCalls == 2, "explicit launch forwards once to retained editor");
    Check(!studio.Launch() && gateway.LaunchCalls == 2, "closed page cannot duplicate editor handoff");
}
using (var studio = new NativeStudioEntryController(new Fake()))
{ studio.Back(); Check(studio.Current().Closed && !studio.Launch(), "idle Back closes without editor action"); }
using (var studio = new NativeStudioEntryController(new Fake { ThrowOnLaunch = true }))
{ Check(!studio.Launch() && studio.Current().Status.Contains("FAILED"), "launch exception has bounded native error"); }
using (var studio = new NativeStudioEntryController(new Fake()))
{
    Exception? wrongThread = null;
    Task.Run(() => { try { studio.Launch(); } catch (Exception error) { wrongThread = error; } }).GetAwaiter().GetResult();
    Check(wrongThread is InvalidOperationException, "editor commands reject worker-thread execution");
}
var many = new Fake { Rows = Enumerable.Range(0, 17).Select(i => new NativeStudioLibraryRow("opaque-" + i, "PROJECT " + i, "SAVED", true, true, false)).ToArray() };
using (var studio = new NativeStudioEntryController(many))
{
    Check(ReferenceEquals(studio.Current(), studio.Current()), "unchanged native ticks reuse immutable workspace snapshot");
    bool immutable = false;
    try { ((IList<NativeStudioTemplate>)studio.Current().Templates)[0] = new("changed", "CHANGED"); }
    catch (NotSupportedException) { immutable = true; }
    Check(immutable && studio.Current().Templates[0].Id == "box", "presenter cannot mutate controller template authority");
    Check(studio.Current().Rows.Count == 8 && studio.Current().PageCount == 3, "catalog paginates without silently truncating projects");
    Check(!studio.Page(-1) && !studio.SelectEntry(8), "page and row bounds reject stale/out-of-range selection");
    Check(studio.Page(1) && studio.SelectEntry(2) && studio.Current().SelectedIndex == 2, "selection belongs to current native page");
    Check(studio.Duplicate(studio.Current().SelectedIndex) && many.LastKey == "opaque-10", "duplicate delegates exact selected opaque project key");
    Check(studio.Page(1) && studio.Current().Rows.Count == 1 && studio.Current().SelectedIndex == 0, "new page resets selection safely");
    Check(!studio.Page(1) && !studio.OpenSelected(1), "last partial page cannot address old rows");
    Check(studio.OpenSelected(0) && many.LastKey == "opaque-16", "open delegates selected project without creating a document");
    Check(!studio.Create("NEW", "missing") && many.CreateCalls == 0, "unknown creation template never reaches editor");
    Check(studio.SelectTemplate(1) && studio.Create("MY_ARENA", studio.Current().Templates[1].Id)
        && many.LastTemplate == "duel" && many.LastName == "MY_ARENA", "name and native-selected template reach existing editor creator");
    Check(!studio.OpenPath(" ") && !studio.OpenPath("bad\0path"), "empty/NUL paths are rejected before handoff");
}
var firstRead = new TaskCompletionSource<IReadOnlyList<NativeStudioLibraryRow>>();
var nextRead = new TaskCompletionSource<IReadOnlyList<NativeStudioLibraryRow>>();
int reads = 0;
var delayed = new Fake { Read = (_, _) => (++reads == 1 ? firstRead : nextRead).Task };
using (var studio = new NativeStudioEntryController(delayed))
{
    Check(studio.Current().Busy && !studio.Launch(), "pending read cannot start competing editor command");
    studio.Back(); Check(!studio.Current().Busy && !studio.Current().Closed, "Back cancels pending read while retaining workspace");
    Check(studio.Refresh(), "cancelled read can be replaced");
    firstRead.SetResult(new[] { new NativeStudioLibraryRow("stale", "STALE", "") });
    Check(studio.Current().Busy && studio.Current().Rows.Count == 0, "retired read result cannot overwrite next request");
    nextRead.SetResult(new[] { new NativeStudioLibraryRow("fresh", "FRESH", "") });
    Pump(studio, () => !studio.Current().Busy);
    Check(studio.Current().Rows.Single().Key == "fresh", "only current generation publishes catalog rows");
    var recoveryRead = new TaskCompletionSource<IReadOnlyList<NativeStudioLibraryRow>>();
    delayed.Read = (_, _) => recoveryRead.Task;
    studio.Refresh(true); studio.Back();
    Check(studio.Current().Recoveries && studio.Current().Rows.Count == 0,
        "cancelled mode switch never relabels old project rows as recoveries");
    recoveryRead.SetResult(new[] { new NativeStudioLibraryRow("retired-recovery", "STALE", "") });
    Check(studio.Current().Rows.Count == 0, "cancelled recovery completion cannot resurrect old list");
}
var picker = new Fake { PickSource = new() };
using (var studio = new NativeStudioEntryController(picker))
{
    Check(studio.Pick() && studio.Current().Busy, "explicit picker uses cancellable native operation");
    studio.Back(); picker.PickSource.SetResult(new("late.json"));
    Check(!studio.Current().Closed && picker.OpenCalls == 0 && picker.LastPickToken.IsCancellationRequested,
        "cancelled OS picker result cannot open editor");
    picker.PickSource = new(); Check(studio.Pick(), "new picker lifetime can start after cancellation");
    picker.PickSource.SetResult(new("chosen.json"));
    Pump(studio, () => !studio.Current().Busy);
    Check(picker.OpenCalls == 1 && picker.LastPath == "chosen.json" && picker.LastOpenThread == Environment.CurrentManagedThreadId,
        "picker completion launches only through owner-thread Pump");
}
var retiredPicker = new Fake { PickSource = new() };
var retired = new NativeStudioEntryController(retiredPicker);
retired.Pick(); retired.Dispose(); retiredPicker.PickSource.SetResult(new("retired.json"));
Check(retiredPicker.OpenCalls == 0 && retiredPicker.LastPickToken.IsCancellationRequested, "document retirement cancels late picker and retains no live editor handle");
var recovery = new Fake { Read = (recoveries, _) => Task.FromResult<IReadOnlyList<NativeStudioLibraryRow>>(
    recoveries ? new[] { new NativeStudioLibraryRow("recovery-owner-key", "UNSAVED", "RECOVERY", true, false, false) } : Array.Empty<NativeStudioLibraryRow>()) };
using (var studio = new NativeStudioEntryController(recovery))
{
    studio.Refresh(true); Check(studio.Current().Recoveries && studio.Current().Rows.Count == 1, "recovery list is an explicit separate source");
    Check(!studio.Duplicate(0) && !studio.RequestDelete(0), "recovery snapshot cannot become a duplicate/delete command");
    Check(studio.OpenSelected(0) && recovery.LastKey == "recovery-owner-key", "restore delegates original recovery key to sole retained editor owner");
}
string root = Path.Combine(Path.GetTempPath(), "prime-native-studio-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string user = Path.Combine(root, "user"); Directory.CreateDirectory(user);
    string path = Path.Combine(user, "arena.json"); File.WriteAllText(path, "AAAA");
    string asset = Path.Combine(user, "texture.bin"); File.WriteAllText(asset, "asset");
    var stamp = NativeStudioProjectFiles.Capture(path, user);
    Check(NativeStudioProjectFiles.Matches(stamp), "user-project deletion snapshot fingerprints real file bytes and stamp");
    Check(!NativeStudioProjectFiles.Delete(stamp, _ => true) && File.Exists(path), "current editor path ownership vetoes deletion");
    File.WriteAllText(path, "BBBB"); File.SetLastWriteTimeUtc(path, new DateTime(stamp.ModifiedUtcTicks, DateTimeKind.Utc));
    Check(!NativeStudioProjectFiles.Matches(stamp) && !NativeStudioProjectFiles.Delete(stamp, _ => false) && File.Exists(path),
        "same-size same-timestamp replacement cannot satisfy confirmed file hash");
    var deletion = new Fake { Rows = new[] { new NativeStudioLibraryRow("file", "ARENA", "SAVED", true, true, true) }, DeleteFile = path, UserRoot = user };
    using (var studio = new NativeStudioEntryController(deletion))
    {
        Check(studio.RequestDelete(0) && deletion.DeleteCalls == 0 && File.Exists(path), "requesting delete creates confirmation without deleting");
        studio.SelectTemplate(1);
        Check(studio.Current().PendingDeleteName.Length == 0 && !studio.ConfirmDelete(), "changing template retires unrelated delete confirmation");
        studio.RequestDelete(0); deletion.PickSource = new(); studio.Pick();
        Check(studio.Current().PendingDeleteName.Length == 0, "picker launch clears pending delete before asynchronous work");
        studio.Back(); deletion.PickSource.SetResult(new(null)); _ = studio.Current();
        studio.RequestDelete(0); studio.OpenPath("bad\0path");
        Check(studio.Current().PendingDeleteName.Length == 0 && !studio.ConfirmDelete(), "unrelated typed-path command clears old delete even when path is invalid");
        studio.RequestDelete(0);
        studio.CancelDelete(); Check(!studio.ConfirmDelete() && File.Exists(path), "cancelled confirmation cannot delete file");
        studio.RequestDelete(0); deletion.Owned = true;
        Check(!studio.ConfirmDelete() && File.Exists(path), "editor ownership acquired after confirmation vetoes deletion");
        deletion.Owned = false; studio.RequestDelete(0);
        File.WriteAllText(path, "CCCC");
        Check(!studio.ConfirmDelete() && File.Exists(path), "changed file after confirmation is retained");
        studio.RequestDelete(0);
        Check(studio.ConfirmDelete() && !File.Exists(path) && File.Exists(asset), "confirmed unchanged user file deletes only JSON, retaining assets");
        Check(!studio.ConfirmDelete(), "confirmed deletion ticket is single use");
    }
    string outside = Path.Combine(root, "outside.json"); File.WriteAllText(outside, "outside");
    bool refused = false; try { NativeStudioProjectFiles.Capture(outside, user); } catch (IOException) { refused = true; }
    Check(refused && File.Exists(outside), "catalog boundary refuses external/built-in project deletion");
    string package = Path.Combine(user, "map.ppmap"); File.WriteAllText(package, "package");
    refused = false; try { NativeStudioProjectFiles.Capture(package, user); } catch (IOException) { refused = true; }
    Check(refused && File.Exists(package), "package deletion is not inferred from writable project deletion");
    string link = Path.Combine(user, "alias.json"); File.CreateSymbolicLink(link, outside);
    refused = false; try { NativeStudioProjectFiles.Capture(link, user); } catch (IOException) { refused = true; }
    Check(refused && File.Exists(outside), "symlink project cannot redirect confirmed deletion");
    string directoryLink = Path.Combine(user, "linked"); Directory.CreateSymbolicLink(directoryLink, root);
    refused = false; try { NativeStudioProjectFiles.Capture(Path.Combine(directoryLink, "outside.json"), user); } catch (IOException) { refused = true; }
    Check(refused && File.Exists(outside), "symlink ancestor cannot escape user map ownership");
    string recoveryDirectory = Path.Combine(user, ".autosave"); Directory.CreateDirectory(recoveryDirectory);
    string protectedRecovery = Path.Combine(recoveryDirectory, "recovery.json"); File.WriteAllText(protectedRecovery, "recovery");
    refused = false; try { NativeStudioProjectFiles.Capture(protectedRecovery, user); } catch (IOException) { refused = true; }
    Check(refused && File.Exists(protectedRecovery), "recovery writer files cannot become saved-project deletion targets");
}
finally { Directory.Delete(root, recursive: true); }
string[] names = { "launch", "pick-map", "open-path", "recover", "cancel", "refresh", "previous-page", "next-page", "open-selected", "create-project", "project-library", "duplicate", "delete", "confirm-delete", "cancel-delete" };
string[] editorNames = { "edit-current", "editor-back", "editor-undo", "editor-redo", "editor-save", "editor-move", "editor-rotate", "editor-scale", "editor-frame-all", "editor-frame-selection", "editor-perspective", "editor-top", "editor-front", "editor-side", "editor-previous", "editor-next", "editor-select-0", "editor-select-1", "editor-select-2", "editor-select-3", "editor-select-4", "editor-select-5", "editor-select-6", "editor-select-7" };
string[] sliceNames = {"project-change-save","project-change-preserve","project-change-cancel","project-change-overwrite","project-change-keep-file","editor-create-box","editor-create-wedge","editor-create-prism","editor-create-convex","editor-create-mesh","editor-create-spawn","editor-create-pickup","editor-create-jump-pad","editor-create-link","editor-duplicate","editor-delete","editor-confirm-delete","editor-cancel-delete","property-previous","property-next","property-apply","property-reset","property-cycle-0","property-cycle-1","property-cycle-2","property-cycle-3","property-cycle-4","property-cycle-5","property-cycle-6","property-cycle-7"};
string[] resourceNames = { "resource-materials", "resource-assets", "resource-objects", "material-previous", "material-next", "material-add", "material-duplicate", "material-assign-selection", "material-select-usages", "material-delete-unused", "material-confirm-delete", "material-cancel-delete", "material-property-previous", "material-property-next", "material-property-apply", "material-property-reset", "material-property-cycle-0", "material-property-cycle-1", "material-property-cycle-2", "material-property-cycle-3", "material-property-cycle-4", "material-property-cycle-5", "material-property-cycle-6", "material-property-cycle-7", "material-select-0", "material-select-1", "material-select-2", "material-select-3", "material-select-4", "material-select-5", "material-select-6", "material-select-7", "asset-previous", "asset-next", "asset-rename", "asset-remove-unused", "asset-confirm-remove", "asset-cancel-remove", "asset-cycle-channel", "asset-select-0", "asset-select-1", "asset-select-2", "asset-select-3", "asset-select-4", "asset-select-5", "asset-select-6", "asset-select-7", "asset-bind-channel", "asset-clear-channel", "resource-refresh-cache", "asset-find-usages", "material-use-as-replacement", "material-replace-usages", "resource-reset-selection" };
string[] modelingNames = {"object-mode", "face-mode", "edge-mode", "vertex-mode", "tool-move", "tool-rotate", "tool-scale", "axis-free", "axis-x", "axis-y", "axis-z", "axis-xy", "axis-xz", "axis-yz", "toggle-local", "snap-apply", "pivot-cycle", "cursor-apply", "component-select-all", "component-clear", "component-linked", "component-grow", "component-shrink", "component-boundary", "component-previous", "component-next", "component-select-0", "component-select-1", "component-select-2", "component-select-3", "component-select-4", "component-select-5", "component-select-6", "component-select-7", "convert-to-mesh", "apply-component-transform", "uv-apply", "uv-fit64", "uv-density", "uv-copy", "uv-paste", "uv-rotate-positive", "uv-rotate-negative", "uv-flip-u", "uv-flip-v", "uv-explicit", "uv-projected", "uv-checker-toggle", "selection-mode-cycle", "gesture-cancel", "reset-uv-draft"};
string[] assetIoNames = { "asset-choose-source", "asset-review-import", "asset-apply-import", "asset-review-replace", "asset-apply-replace", "asset-review-export", "asset-confirm-export", "asset-confirm-overwrite", "asset-cancel-io", "asset-reload-source" };
for (int i = 0; i < 200; i++)
{
    string action = "studio:" + (i < names.Length ? names[i] : i < 23 ? "select-entry-" + (i - 15) : i < 31 ? "select-template-" + (i - 23) : i <55 ?editorNames[i - 31]:i<85?sliceNames[i-55]:i<139?resourceNames[i-85]:i<190?modelingNames[i-139]:assetIoNames[i-190]);
    Check(RmlUiIntentRegistry.TryParseLegacy(action, new(1, 1), (ulong)i + 1, out var intent)
        && intent.Kind == RmlUiIntentKind.StudioAction && intent.Argument == i
        && RmlUiIntentRegistry.ToLegacy(intent) == action, "typed Studio action round-trips stable index " + i);
}
Check(!RmlUiIntentRegistry.IsValid(RmlUiIntentKind.StudioAction, -1) && !RmlUiIntentRegistry.IsValid(RmlUiIntentKind.StudioAction, 224), "Studio action bounds exclude unassigned values");
var entryDocument = System.Xml.Linq.XDocument.Load(Path.Combine(AppContext.BaseDirectory, "native-entry.rml"));
var mapPlaceholder = entryDocument.Descendants("button").Single(button => (string?)button.Attribute("id") == "native_studio_map");
Check((string?)mapPlaceholder.Attribute("data-action") == "studio:launch" && mapPlaceholder.Value == "OPEN MAP STUDIO",
    "Main chooser Map card requests the separate placeholder workspace");
Check(entryDocument.Descendants("button").Where(button => button.Attribute("data-action") != null)
    .Select(button => button.Attribute("data-action")!.Value).Order().SequenceEqual(new[] { "route:theatre", "studio:cancel", "studio:launch", "studio:pick-map" }),
    "Chooser exposes bounded workspace launch requests, Studio Replay route and Back");
Check(!entryDocument.Descendants().Any(element => ((string?)element.Attribute("id")) is "native_studio_map_workspace" or "native_studio_publish" or "native_studio_edit_current" or "native_studio_path"),
    "Map project import library editor and publish controls are absent from product hub");
EditorControllerAcceptance.Run(Check);
StudioDetailsChecks.Run(Check);
StudioResourceControllerChecks.Run(Check);
StudioModelingControllerChecks.Run(Check);
BuildChecks.Run(Check);
Console.WriteLine($"RML STUDIO {checks} checks passed; source-linked controller/file guard/typed intents, no application or GL launch");

sealed class Fake : INativeStudioLauncher
{
    public int LaunchCalls, OpenCalls, CreateCalls, DeleteCalls, LastOpenThread;
    public bool AllowLaunch, ThrowOnLaunch, Owned;
    public string? LastKey, LastPath, LastName, LastTemplate, DeleteFile, UserRoot;
    public CancellationToken LastPickToken;
    public TaskCompletionSource<NativeStudioPickResult>? PickSource;
    public IReadOnlyList<NativeStudioLibraryRow> Rows = Array.Empty<NativeStudioLibraryRow>();
    public Func<bool, CancellationToken, Task<IReadOnlyList<NativeStudioLibraryRow>>>? Read;
    private readonly Dictionary<string, NativeStudioFileStamp> _deletes = new();
    public bool PickerAvailable => true;
    public IReadOnlyList<NativeStudioTemplate> Templates() => new[] { new NativeStudioTemplate("box", "BOX"), new NativeStudioTemplate("duel", "DUEL") };
    public bool LaunchMapStudio() { LaunchCalls++; if (ThrowOnLaunch) throw new IOException("private path"); return AllowLaunch; }
    public Task<IReadOnlyList<NativeStudioLibraryRow>> ReadLibraryAsync(bool recoveries, CancellationToken token) => Read?.Invoke(recoveries, token) ?? Task.FromResult(Rows);
    public Task<NativeStudioPickResult> PickMapAsync(CancellationToken token) { LastPickToken = token; return PickSource!.Task; }
    public NativeStudioResult OpenPath(string path) { OpenCalls++; LastPath = path; LastOpenThread = Environment.CurrentManagedThreadId; return new(true, "OPEN REQUESTED"); }
    public NativeStudioResult OpenEntry(string key) { OpenCalls++; LastKey = key; return new(true, "OPEN REQUESTED"); }
    public NativeStudioResult CreateProject(string name, string templateId) { CreateCalls++; LastName = name; LastTemplate = templateId; return new(true, "CREATE REQUESTED"); }
    public NativeStudioResult DuplicateEntry(string key) { LastKey = key; return new(true, "DUPLICATE REQUESTED"); }
    public NativeStudioDeleteRequest? PrepareDelete(string key)
    {
        if (Owned) return null;
        string ticket = Guid.NewGuid().ToString("N"); _deletes[ticket] = NativeStudioProjectFiles.Capture(DeleteFile!, UserRoot!);
        return new(ticket, "ARENA");
    }
    public NativeStudioResult Delete(NativeStudioDeleteRequest request)
    {
        DeleteCalls++; bool success = _deletes.Remove(request.Ticket, out var stamp) && NativeStudioProjectFiles.Delete(stamp, _ => Owned);
        return new(success, success ? "DELETED" : "CHANGED OR OWNED");
    }
}
