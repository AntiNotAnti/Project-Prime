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
