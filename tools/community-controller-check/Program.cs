using System.Net;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.MapGen;

#if MPHREAD_RMLUI_POC
if (args is ["--native", var nativeLibrary, var nativeAssets]) { NativeCommunityCheck.Run(nativeLibrary, nativeAssets); return; }
#endif
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
void Drain(CommunityController controller)
{
    var timeout = DateTime.UtcNow.AddSeconds(5);
    while (controller.Snapshot().Busy) { controller.Tick(); if (DateTime.UtcNow > timeout) throw new TimeoutException("Community worker"); Thread.Sleep(1); }
    controller.Tick();
}
void Select(CommunityController controller, int index = 0) => Check(controller.SelectRow(index, controller.Snapshot().Revision).Accepted, "select exact copied catalog row");
void Detail(CommunityController controller, CommunityDetailAction action) => Check(controller.Detail(action, controller.Snapshot().SelectedPackageHash).Accepted, "accept " + action);
CommunityController New(FakeCommunityBackend backend) { var result = new CommunityController(backend); result.Refresh(); Drain(result); return result; }

var cacheBackend = new FakeCommunityBackend();
using (var cached = new CommunityController(cacheBackend))
{
    cached.Refresh(force: false); Drain(cached);
    Check(cacheBackend.Invalidations == 0, "opening reuses a warmed public catalog");
    cached.Refresh(); Drain(cached);
    Check(cacheBackend.Invalidations == 1, "explicit refresh invalidates the public catalog");
    Select(cached); Detail(cached, CommunityDetailAction.Favorite); Drain(cached);
    Check(cacheBackend.Invalidations == 2, "successful mutations invalidate cached catalog metadata");
}
var backend = new FakeCommunityBackend();
using (var controller = New(backend))
{
    var first = controller.Snapshot(); Check(first.State == CommunityPageState.Ready && first.Total == 17 && first.Maps.Length == CommunityController.PageSize, "bounded copied service catalog");
    Check(ReferenceEquals(first, controller.Snapshot()), "unchanged snapshot revision is cached");
    Check(Task.Run(() => { try { controller.Snapshot(); return false; } catch (InvalidOperationException) { return true; } }).Result, "foreign thread cannot access controller");
    controller.SetSort(CommunitySort.Name); Check(controller.Snapshot().Maps[0].Name == "Map 00", "name sort uses real catalog metadata");
    controller.Page(1); Check(controller.Snapshot().First == 5 && controller.Snapshot().Maps[0].Name == "Map 04", "next catalog page uses distinct authoritative identities");
    controller.Page(1);controller.Page(1);controller.Page(1); Check(controller.Snapshot().Maps.Length == 1 && !controller.Snapshot().NextPage, "last catalog page bounded");
    controller.Page(-1); Check(controller.Snapshot().First == 13, "previous page preserves signed command");
    controller.Search("Prime"); Check(controller.Snapshot().Total == 17, "mode-aware search matches supported modes");
    controller.Search("not present"); Check(controller.Snapshot().Maps.IsEmpty && controller.Snapshot().First == 0, "real empty search contains no fabricated cards");
    controller.Search(""); controller.SetSort(CommunitySort.Favorites); Check(controller.Snapshot().Maps[0].Name == "Map 16", "favorite count sort");
    controller.SetSort(CommunitySort.Name); var stale = controller.Snapshot().Revision; controller.Search("00");
    Check(!controller.SelectRow(0, stale).Accepted, "changed row revision cannot select another map");
    Select(controller); var selected = controller.Snapshot().SelectedPackageHash;
    backend.Projects[0].CurrentRevision!.Package.SupportedModes[0] = "MUTATED";
    Check(controller.Snapshot().Detail.Contains("Prime") && !controller.Snapshot().Detail.Contains("MUTATED"), "server arrays detached from snapshot state");
    backend.Projects[0] = backend.Projects[0] with { LatestHash = FakeCommunityBackend.Hash(99), LatestRevision = FakeCommunityBackend.Revision(backend.Projects[0].MapId, 2, 99) };
    controller.Refresh(); Drain(controller); Check(controller.Snapshot().SelectedPackageHash == selected, "refresh cannot silently switch selected exact package");
    Check(!controller.Detail(CommunityDetailAction.Install, FakeCommunityBackend.Hash(98)).Accepted, "detail action rejects stale package hash");
    backend.InstallBlocked = true; DetailFailure(); backend.InstallBlocked = false;
    void DetailFailure() => Check(!controller.Detail(CommunityDetailAction.Install, selected).Accepted && backend.InstallCalls == 0, "engine runtime installation fence precedes network work");
    Detail(controller, CommunityDetailAction.InstallAndHost); Drain(controller);
    Check(backend.LastInstallation!.CommitThread == Environment.CurrentManagedThreadId && backend.LastInstallation.Disposals == 1, "prepared installation commits once on owning engine thread");
    Check(controller.TryTakeHostRequest(out var request) && request.Identity.PackageHash.ToString() == selected && request.RoomKey == "room", "install-and-host returns exact installed package handoff");
    Check(!controller.TryTakeHostRequest(out _), "host handoff consumed once");
    Detail(controller, CommunityDetailAction.Favorite); Drain(controller); Check(backend.FavoriteCalls == 1 && controller.Snapshot().Favorited, "favorite mutation refreshes copied metadata without switching identity");
    Detail(controller, CommunityDetailAction.Favorite); Drain(controller); Check(!backend.LastFavorite, "second favorite command toggles actual updated state");
    Detail(controller, CommunityDetailAction.Report); controller.SetReportReason(3);
    Check(!controller.SubmitReport(new string('x', 4001)).Accepted && backend.ReportCalls == 0, "report detail limit before service mutation");
    Check(controller.SubmitReport("real issue").Accepted, "explicit report submit"); Drain(controller);
    Check(backend.LastReport?.Reason == MapCreatorCatalog.ReportReasons[3] && backend.LastReport.Details == "real issue", "report uses authoritative reason catalog and selected map");
    Check(controller.PackageLink()!.EndsWith("/packages/" + selected), "copy-link retains exact selected package");
    controller.OpenImport(); Check(controller.Import("/tmp/real.ppmap").Accepted, "local import delegates authoritative validator"); Drain(controller);
    Check(backend.ImportCalls == 1 && !controller.TryTakeHostRequest(out _), "local import does not invent host request");
    controller.SetTab(CommunityTab.MyMaps); Drain(controller); controller.Search(""); controller.SetLifecycle(CommunityLifecycle.Archived);
    Check(controller.Snapshot().Total == 1, "creator archived lifecycle filter"); controller.SetLifecycle(CommunityLifecycle.Deleted);
    Check(controller.Snapshot().Total == 1, "creator deleted lifecycle filter"); controller.SetLifecycle(CommunityLifecycle.Active);
    Check(controller.Snapshot().Total == 15, "creator active excludes archived/deleted"); controller.SetLifecycle(CommunityLifecycle.All); controller.Search("00"); Select(controller);
    Detail(controller, CommunityDetailAction.Revisions); Drain(controller);
    Check(controller.Snapshot().Revisions.Length == 8 && controller.Snapshot().RevisionTotal == 9, "immutable revision history is bounded and copied");
    controller.RevisionPage(1); Check(controller.Snapshot().RevisionFirst == 9 && controller.Snapshot().Revisions.Length == 1, "revision pagination uses signed action");
    Check(controller.SelectRevision(0, controller.Snapshot().Revision).Accepted, "exact revision selection");
    Check(controller.Snapshot().SelectedPackageHash == FakeCommunityBackend.Hash(109), "revision hash becomes explicit install/action identity");
    Check(controller.Creator(CommunityCreatorAction.Promote, controller.Snapshot().SelectedPackageHash).Accepted, "creator promote accepted"); Drain(controller);
    Check(backend.LastCreator?.Action == CommunityCreatorAction.Promote && backend.LastCreator?.Hash == FakeCommunityBackend.Hash(109) && backend.LastCreator?.Revision == 9, "creator promotion keeps matching number and hash");
    Detail(controller, CommunityDetailAction.Revisions); Drain(controller); controller.SelectRevision(0, controller.Snapshot().Revision);
    int mutations = backend.CreatorCalls; Check(controller.Creator(CommunityCreatorAction.DeleteRevision, controller.Snapshot().SelectedPackageHash).Accepted, "revision deletion opens confirmation");
    Check(controller.Snapshot().Form == CommunityForm.Confirm && backend.CreatorCalls == mutations, "delete confirmation does not mutate before explicit submit");
    controller.CancelForm(); Check(backend.CreatorCalls == mutations, "cancel deletes no revisions");
    controller.Creator(CommunityCreatorAction.DeleteRevision, controller.Snapshot().SelectedPackageHash); controller.ConfirmAction(); Drain(controller);
    Check(backend.CreatorCalls == mutations + 1 && backend.LastCreator?.Action == CommunityCreatorAction.DeleteRevision, "confirmed exact revision soft-delete delegates service");
    Detail(controller, CommunityDetailAction.Revisions); Drain(controller); controller.SelectRevision(0, controller.Snapshot().Revision);
    Check(controller.Creator(CommunityCreatorAction.RestoreRevision, controller.Snapshot().SelectedPackageHash).Accepted, "restore exact revision delegates service"); Drain(controller);
    Detail(controller, CommunityDetailAction.Archive); Drain(controller); Check(backend.LastMapAction == CommunityDetailAction.Archive, "creator archive authority");
    Detail(controller, CommunityDetailAction.RestoreMap); Drain(controller); Check(backend.LastMapAction == CommunityDetailAction.RestoreMap, "creator restore map authority");
    int mapCalls = backend.MapCalls; Detail(controller, CommunityDetailAction.DeleteMap); Check(backend.MapCalls == mapCalls, "map delete requires concrete confirmation");
    controller.ConfirmAction(); Drain(controller); Check(backend.MapCalls == mapCalls + 1 && backend.LastMapAction == CommunityDetailAction.DeleteMap, "confirmed map delete uses service restore policy");
}

void Prepare(CommunityController controller, FakeCommunityBackend b)
{
    controller.OpenPublish(); Check(controller.PreparePublish("/tmp/saved-project.json", "draft notes").Accepted, "explicit saved-project preparation"); Drain(controller);
    Check(controller.Snapshot().CanPublish && controller.Snapshot().PublishSource.Contains(b.LastPublication!.Identity.PackageHash.ToString()), "validated exact package identity review");
    Check(b.PublishCalls == 0, "validation never publishes automatically");
}
foreach (var conflictAction in new[] { CommunityConflictAction.PublishAnyway, CommunityConflictAction.DraftBranch, CommunityConflictAction.UploadAsRevision, CommunityConflictAction.ReviewLatest, CommunityConflictAction.Discard })
{
    var b = new FakeCommunityBackend(); using var c = New(b); Prepare(c, b);
    Check(!c.Publish(new string('x', 4001)).Accepted, "publish release-note limit");
    b.PublishError = new CommunityRevisionConflictException(new(conflictAction == CommunityConflictAction.UploadAsRevision ? "map_already_exists" : "stale_parent", b.Projects[0].MapId, FakeCommunityBackend.Hash(1), FakeCommunityBackend.Hash(2), FakeCommunityBackend.Hash(1), 2, true, "Server revision changed."));
    c.SetVisibility(CommunityVisibility.Unlisted); Check(c.Publish("final notes").Accepted, "publication explicitly starts upload"); Drain(c);
    Check(c.Snapshot().Form == CommunityForm.Conflict && b.LastPublication!.Disposals == 0, "optimistic conflict preserves exact prepared package");
    Check(b.LastPublishRequest?.ExpectedParentHash == b.Projects[0].LatestHash && b.LastPublishRequest.ReleaseNotes == "final notes", "upload captures validated latest parent and exact notes");
    b.PublishError = null; c.Conflict(conflictAction); Drain(c);
    if (conflictAction is CommunityConflictAction.ReviewLatest or CommunityConflictAction.Discard)
        { Check(b.DiscardCalls == 1 && b.PublishCalls == 1 && b.LastPublication!.Disposals == 1, "conflict discard removes pending upload and private package");
          if (conflictAction == CommunityConflictAction.ReviewLatest) Check(c.Snapshot().Form == CommunityForm.Revisions && c.Snapshot().RevisionTotal == 9, "review latest explicitly reopens refreshed immutable history"); }
    else
    {
        Check(b.PublishCalls == 2 && b.LastPublication!.Disposals == 1 && c.Snapshot().Form == CommunityForm.None, "conflict decision retries same immutable prepared package once");
        if (conflictAction == CommunityConflictAction.UploadAsRevision) Check(b.LastPublishRequest!.ExistingMap && b.LastPublishRequest.ExpectedParentHash == FakeCommunityBackend.Hash(2) && !b.LastPublishRequest.AllowStaleParent, "existing-map retry uses exact conflict parent");
        else Check(b.LastPublishRequest!.AllowStaleParent && b.LastPublishRequest.ExistingMap, "explicit stale-parent override only after conflict decision");
        if (conflictAction == CommunityConflictAction.DraftBranch) Check(b.LastVisibility == CommunityVisibility.Draft, "draft conflict branch remains draft");
        else Check(b.LastVisibility == CommunityVisibility.Unlisted, "upload respects selected visibility");
    }
}
{
    var b = new FakeCommunityBackend(); using var c = New(b);
    b.BrowseError = new HttpRequestException("expired narrow ticket", null, HttpStatusCode.Unauthorized); c.SetTab(CommunityTab.MyMaps); Drain(c);
    Check(c.Snapshot().State == CommunityPageState.Unauthorized, "auth failure stays visible with explicit retry");
    b.BrowseError = new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable); c.Refresh(); Drain(c);
    Check(c.Snapshot().State == CommunityPageState.Offline, "service offline state"); b.BrowseError = null; c.Refresh(); Drain(c);
    Check(c.Snapshot().State == CommunityPageState.Ready, "retry recovers catalog");
    b.Projects[0] = b.Projects[0] with { LatestHash = "malformed" }; c.Refresh(); Drain(c);
    Check(c.Snapshot().State == CommunityPageState.Failed && c.Snapshot().Error.Contains("identity"), "malformed service revision becomes error state without UI exception");
}
{
    var b = new FakeCommunityBackend(); using var c = New(b); Select(c);
    b.InstallGate = new(TaskCreationOptions.RunContinuationsAsynchronously); Detail(c, CommunityDetailAction.InstallAndHost);
    SpinWait.SpinUntil(() => b.InstallCalls > 0, 5000); c.CancelWork(); var prepared = new FakeInstallation(FakeCommunityBackend.Identity(b.Projects[0].CurrentRevision!.Package));
    b.InstallGate.SetResult(prepared); SpinWait.SpinUntil(() => b.InstallReturned, 5000); Thread.Sleep(20); c.Tick();
    Check(prepared.Disposals == 1 && prepared.CommitThread == 0 && !c.TryTakeHostRequest(out _), "cancelled stale installation is disposed without committing or hosting");
    c.Refresh(); Drain(c); b.StaleProgress?.Invoke(new(99, 100, "OLD DOWNLOAD")); c.Tick();
    Check(c.Snapshot().Progress.Stage != "OLD DOWNLOAD", "old operation progress cannot cross retry boundary");
}
{
    var b = new FakeCommunityBackend(); using var c = New(b); Prepare(c, b); b.PublishGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    c.Publish("upload"); SpinWait.SpinUntil(() => b.PublishCalls > 0, 5000); c.CancelWork();
    Check(b.LastPublication!.Disposals == 0, "cancelled upload keeps borrowed file until worker acknowledges");
    b.PublishGate.SetResult(new(b.Projects[0].LatestRevision.Package, b.Projects[0], b.Projects[0].LatestRevision));
    Check(SpinWait.SpinUntil(() => b.LastPublication!.Disposals == 1, 5000), "cancelled upload eventually disposes borrowed private package"); c.Tick();
    Check(!c.Snapshot().CanPublish && c.Snapshot().State == CommunityPageState.Cancelled, "late upload cannot revive cancelled page");
}
{
    var b = new FakeCommunityBackend(); var c = New(b); b.PrepareGate = new(TaskCreationOptions.RunContinuationsAsynchronously); c.OpenPublish(); c.PreparePublish("project", "");
    SpinWait.SpinUntil(() => b.PrepareCalls > 0, 5000); c.Dispose(); var late = new FakePublication(b.Projects[0]); b.PrepareGate.SetResult(late);
    Check(SpinWait.SpinUntil(() => late.Disposals == 1, 5000), "disposed route releases late prepared package");
    Check(!typeof(CommunitySnapshot).GetProperties().Any(p => p.Name.Contains("Token") || p.Name.Contains("Ticket") || p.Name.Contains("Password")), "presentation snapshot carries no account credential fields");
}
{
    var b = new FakeCommunityBackend(); using var c = New(b); c.SetTab(CommunityTab.MyMaps); Drain(c); c.Search("00"); Select(c);
    b.History = new[] { b.Projects[0].LatestRevision }; Detail(c, CommunityDetailAction.Revisions); Drain(c); c.SelectRevision(0, c.Snapshot().Revision);
    Check(!c.Creator(CommunityCreatorAction.DeleteRevision, c.Snapshot().SelectedPackageHash).Accepted && b.CreatorCalls == 0, "creator keeps at least one active immutable revision");
    c.CancelForm(); Detail(c, CommunityDetailAction.DeleteMap); c.SetTab(CommunityTab.Discover); Drain(c); c.ConfirmAction();
    Check(b.MapCalls == 0, "tab retirement cannot retain a lifecycle confirmation callback");
    Check(!c.SetAddress("https://user:secret@example.test/").Accepted, "existing endpoint authority rejects credential-bearing service URL");
}
{
    var b = new FakeCommunityBackend(); using var c = New(b); Prepare(c, b);
    b.PublishError = new CommunityRevisionConflictException(new("stale_parent", b.Projects[0].MapId, FakeCommunityBackend.Hash(1), FakeCommunityBackend.Hash(2), FakeCommunityBackend.Hash(1), 2, true, "Latest changed"));
    c.Publish(""); Drain(c); c.CancelForm(); Drain(c);
    Check(c.Snapshot().Form == CommunityForm.None && b.DiscardCalls == 1 && b.LastPublication!.Disposals == 1, "closing conflict discards pending upload without reopening retired modal");
}
Console.WriteLine($"Community controller: {checks} checks passed.");

internal sealed class FakeCommunityBackend : ICommunityBackend
{
    public int Invalidations;
    public void InvalidateCatalog() => Interlocked.Increment(ref Invalidations);
    public string DefaultAddress => "http://127.0.0.1:47800/";
    public CommunityMapProject[] Projects = Enumerable.Range(0, 17).Select(Project).ToArray();
    public CommunityMapRevision[]? History = null;
    public Exception? BrowseError, PublishError;
    public bool InstallBlocked, LastFavorite, InstallReturned;
    public int InstallCalls, ImportCalls, FavoriteCalls, ReportCalls, CreatorCalls, MapCalls, PrepareCalls, PublishCalls, DiscardCalls;
    public MapReportRequest? LastReport;
    public (string Hash, int Revision, CommunityCreatorAction Action)? LastCreator;
    public CommunityDetailAction LastMapAction;
    public CommunityPublishRequest? LastPublishRequest;
    public CommunityVisibility LastVisibility;
    public FakeInstallation? LastInstallation;
    public FakePublication? LastPublication;
    public TaskCompletionSource<ICommunityInstallation>? InstallGate;
    public TaskCompletionSource<ICommunityPublication>? PrepareGate;
    public TaskCompletionSource<CommunityPublishResult>? PublishGate;
    public Action<CommunityTransferProgress>? StaleProgress;
    public MapContentIdentity? Installed(Guid mapId) => LastInstallation?.Identity.MapId == mapId && LastInstallation.CommitThread != 0 ? LastInstallation.Identity : null;
    public void SetAddress(string address) { using var validate = new MapCommunityClient(address); }
    public void ValidateInstallation(CommunityMap? package) { if (InstallBlocked) throw new IOException("Loaded scene uses map assets."); }
    public Task<CommunityMapProject[]> BrowseAsync(CommunityTab tab, CancellationToken cancellation) => BrowseError == null ? Task.FromResult(Projects) : Task.FromException<CommunityMapProject[]>(BrowseError);
    public Task<CommunityMapRevision[]> RevisionsAsync(Guid map, bool authenticated, CancellationToken cancellation) => Task.FromResult(History ?? Enumerable.Range(1, 9).Select(i => Revision(map, i, 100 + i)).ToArray());
    public async Task<ICommunityInstallation> PrepareInstallAsync(CommunityMap package, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    { InstallCalls++; StaleProgress = progress; progress(new(5, 10, "Downloading")); LastInstallation = new(Identity(package)); var result = InstallGate == null ? LastInstallation : await InstallGate.Task; InstallReturned = true; return result; }
    public Task<ICommunityInstallation> PrepareImportAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation) { ImportCalls++; LastInstallation = new(Identity(Projects[0].LatestRevision.Package)); return Task.FromResult<ICommunityInstallation>(LastInstallation); }
    public Task FavoriteAsync(Guid map, bool favorite, CancellationToken cancellation)
    { FavoriteCalls++; LastFavorite = favorite; Projects = Projects.Select(p => p.MapId != map ? p : p with { CurrentRevision = p.CurrentRevision == null ? null : p.CurrentRevision with { Package = p.CurrentRevision.Package with { Favorited = favorite } }, LatestRevision = p.LatestRevision with { Package = p.LatestRevision.Package with { Favorited = favorite } } }).ToArray(); return Task.CompletedTask; }
    public Task ReportAsync(Guid map, MapReportRequest report, CancellationToken cancellation) { ReportCalls++; LastReport = report; return Task.CompletedTask; }
    public Task ModifyAsync(Guid map, string hash, int revision, CommunityCreatorAction action, CancellationToken cancellation) { CreatorCalls++; LastCreator = (hash, revision, action); return Task.CompletedTask; }
    public Task ModifyMapAsync(Guid map, CommunityDetailAction action, CancellationToken cancellation) { MapCalls++; LastMapAction = action; return Task.CompletedTask; }
    public Task<ICommunityPublication> PreparePublicationAsync(string path, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    { PrepareCalls++; LastPublication = new(Projects[0]); return PrepareGate == null ? Task.FromResult<ICommunityPublication>(LastPublication) : PrepareGate.Task; }
    public Task<CommunityPublishResult> PublishAsync(ICommunityPublication publication, CommunityPublishRequest request, CommunityVisibility visibility, Action<CommunityTransferProgress> progress, CancellationToken cancellation)
    { PublishCalls++; LastPublishRequest = request; LastVisibility = visibility; progress(new(8, 10, "Uploading")); return PublishError != null ? Task.FromException<CommunityPublishResult>(PublishError) : PublishGate?.Task ?? Task.FromResult(new CommunityPublishResult(Projects[0].LatestRevision.Package, Projects[0], Projects[0].LatestRevision)); }
    public Task DiscardPublicationAsync(ICommunityPublication publication, CancellationToken cancellation) { DiscardCalls++; return Task.CompletedTask; }
    public static string Hash(int n) => n.ToString("x64");
    public static MapContentIdentity Identity(CommunityMap package) => new(package.MapId, "room", MapHash256.Parse(package.ContentHash), MapHash256.Parse(package.Hash), true);
    public static CommunityMapRevision Revision(Guid id, int revision, int hash) => new(revision, Hash(hash), null, "creator", DateTimeOffset.UnixEpoch.AddDays(revision), new(Hash(hash), id, Hash(900), "room", "Map", "Author", "1", 1024) { SupportedModes = new[] { "Prime" } }, "Notes " + revision);
    public static CommunityMapProject Project(int n)
    {
        Guid id = new(n + 1, 0, 0, new byte[8]); var r = Revision(id, 1, n + 1); r = r with { Package = r.Package with { FavoriteCount = n, DisplayName = "Map " + n.ToString("00") } };
        return new(id, "creator", "room" + n, "Map " + n.ToString("00"), "Author", r.Hash, r.Hash, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(n), r, r,
            ArchivedAt: n == 15 ? DateTimeOffset.UnixEpoch : null, DeletedAt: n == 16 ? DateTimeOffset.UnixEpoch : null, CanManageLifecycle: true);
    }
}
internal sealed class FakeInstallation(MapContentIdentity identity) : ICommunityInstallation
{
    public MapContentIdentity Identity => identity;
    public int CommitThread, Disposals;
    public string Commit(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); CommitThread = Environment.CurrentManagedThreadId; return "room"; }
    public void Dispose() => Interlocked.Increment(ref Disposals);
}
internal sealed class FakePublication(CommunityMapProject project) : ICommunityPublication
{
    public MapContentIdentity Identity => FakeCommunityBackend.Identity(project.LatestRevision.Package);
    public string DisplayName => project.DisplayName!;
    public CommunityMapProject? Existing => project;
    public int Disposals;
    public void Dispose() => Interlocked.Increment(ref Disposals);
}
