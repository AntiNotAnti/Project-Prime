using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher;
using MphRead.Mods.MapGen;

static void Check(bool pass, string label)
{
    if (!pass) throw new InvalidOperationException(label);
    Console.WriteLine("RML COMMUNITY PASS " + label);
}
static NativeCommunityDiscoverController.Snapshot Wait(NativeCommunityDiscoverController c,
    Func<NativeCommunityDiscoverController.Snapshot, bool> predicate)
{
    for (int i = 0; i < 100; i++)
    {
        var state = c.Tick();
        if (predicate(state)) return state;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Community task did not complete in bounded acceptance time");
}
static NativeCommunityEntry Row(string label, char id = 'a')
    => new(Guid.NewGuid(), label, "Creator", new string(id, 64), 2, "Battle",
        2, 8);

var provider = new Stub();
using var community = new NativeCommunityDiscoverController(provider);
community.Refresh();
Check(community.Current().Busy && community.Current().Rows.Count == 0,
    "remote catalog work is pending without blocking the owner thread");
var old = provider.Pending;
provider.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(community.Search("  arena ") && provider.Search == "arena" && provider.Page == 1
    && provider.Sort == "updated", "search trims user text and restarts from page one");
old.SetResult(new(new[] { Row("STALE") }, 1, 1, 8));
provider.Pending.SetResult(new(new[] { Row("REAL MAP") }, 9, 1, 8));
var first = Wait(community, s => s.Rows.Count == 1);
Check(first.Rows[0].Name == "REAL MAP" && first.CanNext && !first.CanPrevious
    && first.SelectedEntry?.Name == "REAL MAP", "only current epoch's catalog is visible");
Check(community.Select(0) && !community.Select(7)
    && !community.Search(new string('x', 129)),
    "bounded selection and 128-character search are enforced");
provider.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(community.Page(1) && provider.Page == 2,
    "next page calls current paged API without a local catalog copy");
provider.Pending.SetResult(new(new[] { Row("SECOND PAGE", 'b') }, 9, 2, 8));
var second = Wait(community, s => s.Page == 2 && !s.Busy);
Check(second.Rows.Count == 1 && second.Rows[0].Name == "SECOND PAGE"
    && !second.CanNext && second.CanPrevious,
    "server total controls native page boundaries");
provider.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(community.Sort(0) && provider.Sort == "name" && provider.Page == 1,
    "changing sort resets the page and requests a new bounded result");
provider.Pending.SetResult(new(new[] { Row("ALPHA") }, 1, 1, 8));
Wait(community, s => !s.Busy && s.Rows.FirstOrDefault().Name == "ALPHA");
provider.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
community.Refresh();
community.Cancel();
provider.Pending.SetResult(new(new[] { Row("AFTER CANCEL") }, 1, 1, 8));
Thread.Sleep(30);
Check(community.Tick().Rows.FirstOrDefault().Name == "ALPHA",
    "late cancelled refresh cannot replace visible Community state");
Exception? crossThread = null;
Task.Run(() =>
{
    try { community.Tick(); }
    catch (Exception ex) { crossThread = ex; }
}).GetAwaiter().GetResult();
Check(crossThread is InvalidOperationException,
    "native Community presenter alone applies completed HTTP work");
provider.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
community.Refresh();
provider.Pending.SetException(new InvalidOperationException("sensitive service payload and ticket"));
var redacted = Wait(community, view => !view.Busy
    && view.Status.Contains("UNAVAILABLE", StringComparison.Ordinal));
Check(redacted.Rows.Count == 0
    && !redacted.Status.Contains("sensitive", StringComparison.OrdinalIgnoreCase),
    "catalog network failures never publish untrusted private service details");
Console.WriteLine("RML Community discovery ownership and read-only contracts complete.");

static NativeCommunityInstallView WaitInstall(NativeCommunityInstallController c,
    Func<NativeCommunityInstallView, bool> ready)
{
    for (int i = 0; i < 100; ++i)
    {
        var view = c.Tick();
        if (ready(view)) return view;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Community install fixture did not complete.");
}
var installation = new InstallStub();
using var flow = new NativeCommunityInstallController(installation);
var selectedMap = Row("EXACT PACKAGE", 'c');
Check(!NativeCommunityInstallController.IsVerifiedSelection(
        selectedMap with { LatestHash = new string('g', 64) })
    && !flow.Begin(selectedMap with { MapId = Guid.Empty }),
    "install rejects malformed hashes and missing immutable map IDs");
Check(flow.Begin(selectedMap) && !flow.Begin(Row("OTHER", 'd')),
    "only one exact published selection can own the install handoff");
var wrong = new NativeCommunityInstalled(Guid.NewGuid(), selectedMap.LatestHash,
    new MapDefinition { MapId = Guid.NewGuid(), Name = "UNTRUSTED" });
installation.Pending.SetResult(wrong);
Check(WaitInstall(flow, v => !v.Busy).CompletedMapId == null
    && installation.RegisterCalls == 0,
    "download results with mismatched selected map identity cannot register");

installation.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(flow.Begin(selectedMap), "validated map may retry installation");
var exact = new NativeCommunityInstalled(selectedMap.MapId, selectedMap.LatestHash,
    new MapDefinition { MapId = selectedMap.MapId, Name = "VERIFIED" });
installation.Pending.SetResult(exact);
var installedView = WaitInstall(flow, v => !v.Busy && v.CompletedMapId != null);
Check(installedView.CompletedMapId == selectedMap.MapId
    && installedView.CompletedHash == selectedMap.LatestHash
    && installation.RegisterCalls == 1 && installation.Last == exact,
    "matching package registers only on the UI owner");

installation.Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(flow.Begin(Row("CANCELLED", 'e')), "new install starts asynchronously");
flow.Cancel();
installation.Pending.SetResult(new NativeCommunityInstalled(selectedMap.MapId,
    selectedMap.LatestHash, new MapDefinition()));
Thread.Sleep(20);
Check(!flow.Tick().Busy && flow.Tick().CompletedMapId == null
    && installation.RegisterCalls == 1,
    "cancelled install handoff drops stale completion without stopping durable queue");
Exception? installCrossThread = null;
Task.Run(() =>
{
    try { flow.Tick(); }
    catch (Exception ex) { installCrossThread = ex; }
}).GetAwaiter().GetResult();
Check(installCrossThread is InvalidOperationException,
    "installed-map registration is restricted to UI owner thread");

Console.WriteLine("RML-37 COMMUNITY INSTALL ACCEPTANCE COMPLETE");

static NativeCreatorView WaitCreator(NativeCommunityCreatorController c,
    Func<NativeCreatorView, bool> ready)
{
    for (int i = 0; i < 160; ++i)
    {
        var view = c.Tick();
        if (ready(view)) return view;
        Thread.Sleep(10);
    }
    throw new TimeoutException("Creator fixture failed to settle.");
}
var creatorService = new CreatorStub();
using var creator = new NativeCommunityCreatorController(creatorService);
var ownedId = Guid.NewGuid();
var owned = new NativeCreatorEntry(ownedId, "Owned Arena", new string('a',64),
    "", 2, false, false, true);
creator.Refresh();
WaitCreator(creator, v => creatorService.BrowseCount == 1 && v.Busy);
var creatorOld = creatorService.BrowseResponse;
creatorService.BrowseResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
creator.Refresh();
WaitCreator(creator, v => creatorService.BrowseCount == 2 && v.Busy);
creatorOld.SetResult(new(new[] { owned with { Name = "Stale" } }, 1, 1));
creatorService.BrowseResponse.SetResult(new(new[] { owned }, 1, 1));
var ownView = WaitCreator(creator, v => !v.Busy && v.Connected);
Check(ownView.Selected == owned && ownView.Rows.Length == 1
    && ownView.Total == 1,
    "My Uploads ignores stale generations and displays only authenticated rows");
Check(creator.Can(NativeCreatorAction.Archive) && creator.Propose(NativeCreatorAction.Archive)
    && creator.Tick().Confirmation.Contains("ARCHIVE")
    && creatorService.ChangeCount == 0,
    "owned map archive requires a visible confirmation before mutation");
Check(creator.Dismiss() && creatorService.ChangeCount == 0,
    "dismissing creator confirmation never changes server state");
Check(creator.Propose(NativeCreatorAction.Archive) && creator.Confirm(),
    "confirmed owner-only lifecycle command enters authoritative preflight");
WaitCreator(creator, v => creatorService.ReadCount == 1 && v.Busy);
creatorService.ReadResponse.SetResult(owned);
WaitCreator(creator, v => creatorService.ChangeCount == 1 && v.Busy);
Check(creatorService.LastAction == NativeCreatorAction.Archive
    && creatorService.LastMapId == ownedId,
    "creator action is scoped to the exact owned project");
creatorService.BrowseResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
creatorService.ChangeResponse.SetResult(true);
WaitCreator(creator, v => creatorService.BrowseCount == 3 && v.Busy);
creatorService.BrowseResponse.SetResult(new(new[] { owned with { Archived = true } }, 1, 1));
var archived = WaitCreator(creator, v => !v.Busy && v.Connected && v.Selected?.Archived == true);
Check(!creator.Can(NativeCreatorAction.Archive)
    && creator.Can(NativeCreatorAction.Restore)
    && archived.Selected!.Archived,
    "archive state changes only after a fresh authenticated catalog read");
creatorService.ReadResponse = new(TaskCreationOptions.RunContinuationsAsynchronously);
Check(creator.Propose(NativeCreatorAction.Restore) && creator.Confirm(),
    "restore also requires explicit confirmation");
WaitCreator(creator, v => creatorService.ReadCount == 2 && v.Busy);
creatorService.ReadResponse.SetResult(owned with { LatestHash = new string('b',64) });
var staleOwner = WaitCreator(creator, v => !v.Busy);
Check(!staleOwner.Connected && staleOwner.Rows.Length == 0
    && creatorService.ChangeCount == 1
    && !staleOwner.Status.Contains("changed after confirmation", StringComparison.OrdinalIgnoreCase),
    "changed owned project rejects stale destructive confirmation without exposing service details");
Exception? creatorThread = null;
Task.Run(() =>
{
    try { creator.Tick(); }
    catch (Exception ex) { creatorThread = ex; }
}).GetAwaiter().GetResult();
Check(creatorThread is InvalidOperationException,
    "creator service completions may only mutate the UI owner");
Console.WriteLine("RML-38 CREATOR ACCEPTANCE COMPLETE");

sealed class Stub : INativeCommunityReader
{
    public TaskCompletionSource<NativeCommunityPage> Pending =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public string Search = "", Sort = "";
    public int Page;
    public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page,
        CancellationToken token)
    {
        Search = query; Sort = sort; Page = page;
        // Deliberately ignore cancellation: the owner model still must fence
        // a stale network result, even if a server responds after cancellation.
        if (Pending.Task.IsCompleted)
            Pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
        return Pending.Task;
    }
}

sealed class InstallStub : INativeCommunityInstaller
{
    public TaskCompletionSource<NativeCommunityInstalled> Pending =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int RegisterCalls;
    public NativeCommunityInstalled? Last;
    public Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry entry,
        CancellationToken cancellation) => Pending.Task;
    public void Register(NativeCommunityInstalled result)
    {
        RegisterCalls++;
        Last = result;
    }
}

sealed class CreatorStub : INativeCommunityCreatorAuthority
{
    public TaskCompletionSource<NativeCreatorPage> BrowseResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<NativeCreatorEntry?> ReadResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> ChangeResponse =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int BrowseCount, ReadCount, ChangeCount;
    public NativeCreatorAction LastAction;
    public Guid LastMapId;

    public Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token)
    {
        Interlocked.Increment(ref BrowseCount);
        var response = BrowseResponse;
        return response.Task.WaitAsync(token);
    }
    public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid id, CancellationToken token)
    {
        Interlocked.Increment(ref ReadCount);
        return ReadResponse.Task.WaitAsync(token);
    }
    public Task<bool> ExecuteAsync(NativeCreatorAction action, Guid id, CancellationToken token)
    {
        LastAction = action;
        LastMapId = id;
        Interlocked.Increment(ref ChangeCount);
        return ChangeResponse.Task.WaitAsync(token);
    }
}
