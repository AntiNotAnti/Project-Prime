using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Core;

/// <summary>Network/package work returns through Tick; installation and presentation mutations stay on the engine thread.</summary>
public sealed class CommunityController : IDisposable
{
    private sealed record Completion(uint Operation, object? Result, Exception? Error = null, bool Progress = false);
    private sealed record Catalog(CommunityMapProject[] Projects, string Message,
        CommunityMapRevision[]? Revisions = null, Guid? ReviewMap = null, bool ClearPublication = false,
        Guid? FavoriteMap = null, bool? Favorited = null);
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private readonly ICommunityBackend _backend;
    private readonly Guid _lifetime = Guid.NewGuid();
    private readonly ConcurrentQueue<Completion> _completed = new();
    private readonly object _completionGate = new();
    private CancellationTokenSource? _work;
    private Task? _worker;
    private uint _operation;
    private volatile bool _disposed;
    private bool _busy;
    private bool _publishing;
    private long _revision = 1;
    private CommunitySnapshot? _cached;
    private ImmutableArray<CommunityMapProject> _projects = ImmutableArray<CommunityMapProject>.Empty;
    private ImmutableArray<CommunityMapRevision> _revisions = ImmutableArray<CommunityMapRevision>.Empty;
    private CommunityMapProject? _selected;
    private CommunityMap? _package;
    private CommunityMapRevision? _selectedRevision;
    private CommunityTab _tab;
    private CommunitySort _sort = CommunitySort.RecentlyUpdated;
    private CommunityLifecycle _lifecycle;
    private CommunityPageState _state = CommunityPageState.Empty;
    private CommunityForm _form;
    private CommunityVisibility _visibility;
    private string _address, _search = "", _status = "Refresh to browse Community maps.", _error = "", _confirmation = "";
    private int _offset, _revisionOffset, _reportReason;
    private Action? _confirmed;
    private CommunityTransferProgress _progress;
    private ICommunityPublication? _publication;
    private CommunityPublishRequest? _publishRequest;
    private CommunityRevisionConflict? _conflict;
    private CommunityHostRequest? _hostRequest;
    private bool _installAndHost;

    public CommunityController(ICommunityBackend backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _address = backend.DefaultAddress;
    }

    public CommunitySnapshot Snapshot()
    {
        Verify();
        if (_cached?.Revision == _revision) return _cached;
        var maps = Filtered();
        _offset = Math.Min(_offset, Math.Max(0, (maps.Length - 1) / PageSize * PageSize));
        _revisionOffset = Math.Min(_revisionOffset, Math.Max(0, (_revisions.Length - 1) / 8 * 8));
        var package = _package;
        string installed = package == null ? "" : _backend.Installed(package.MapId) is not { } identity ? "Not installed"
            : identity.PackageHash.ToString() == package.Hash ? "Exact selected revision installed" : "Different revision installed";
        return _cached = new()
        {
            Lifetime = _lifetime, Revision = _revision, State = _state, Tab = _tab, Sort = _sort, Lifecycle = _lifecycle,
            Form = _form, Address = _address, Search = _search, Status = _status, Error = _error, Busy = _busy,
            Title = _selected?.DisplayName ?? _selected?.Name ?? "Select a Community map",
            Detail = package == null ? "Discover installed and published immutable map revisions."
                : $"{_selected?.Author ?? package.Author ?? "Unknown author"}\n{package.MinPlayers}–{package.MaxPlayers} players // {string.Join(", ", package.SupportedModes)}\n"
                    + $"Version {package.Version ?? "1"} // {package.Bytes / 1048576d:0.0} MiB // {Visibility(package)}\n"
                    + $"Selected exact package: {package.Hash}\nCurrent: {_selected?.CurrentHash ?? "No public release"}\nLatest: {_selected?.LatestHash}\n{installed}\n"
                    + (_selectedRevision?.ReleaseNotes ?? ""),
            HasSelection = package != null, Favorited = package?.Favorited == true, HasRevision = _selectedRevision != null, RevisionDeleted = _selectedRevision?.DeletedAt != null,
            SelectedPackageHash = package?.Hash ?? "", CanManage = _tab == CommunityTab.MyMaps && _selected != null,
            CanManageLifecycle = _tab == CommunityTab.MyMaps && _selected?.CanManageLifecycle == true,
            MapDeleted = _selected?.DeletedAt != null, MapArchived = _selected?.ArchivedAt != null,
            Confirmation = _confirmation, Visibility = _visibility, ReportReason = _reportReason,
            PublishSource = _publication == null ? "Choose a saved map project or .ppmap, then validate it before uploading."
                : $"{_publication.DisplayName}\nMap ID: {_publication.Identity.MapId}\nExact prepared package: {_publication.Identity.PackageHash}\n"
                    + (_publication.Existing == null ? "New Community map" : "Based on latest revision " + _publication.Existing.LatestRevision.RevisionNumber),
            CanPublish = _publication != null && _publishRequest != null && !_busy && _publication.Existing?.DeletedAt == null,
            Conflict = _conflict?.Message ?? "", ConflictCode = _conflict?.Code ?? "",
            First = maps.Length == 0 ? 0 : _offset + 1, Total = maps.Length, PreviousPage = _offset > 0, NextPage = _offset + PageSize < maps.Length,
            RevisionFirst = _revisions.Length == 0 ? 0 : _revisionOffset + 1, RevisionTotal = _revisions.Length,
            PreviousRevisionPage = _revisionOffset > 0, NextRevisionPage = _revisionOffset + 8 < _revisions.Length,
            Progress = _progress,
            Maps = maps.Skip(_offset).Take(PageSize).Select(p => new CommunityMapRow(p.MapId, p.DisplayName ?? p.Name,
                (p.Author ?? "Unknown author") + " // " + Lifecycle(p) + " // " + Visibility(Presentation(p))
                    + " // Revision " + PresentationRevision(p).RevisionNumber + " // " + Presentation(p).FavoriteCount + " favorites"
                    + (_backend.Installed(p.MapId)?.PackageHash.ToString() == Presentation(p).Hash ? " // Installed" : ""), p.MapId == _selected?.MapId)).ToImmutableArray(),
            Revisions = _revisions.Skip(_revisionOffset).Take(8).Select(r => new CommunityRevisionRow(r.RevisionNumber,
                "REVISION " + r.RevisionNumber + (r.Hash == _selected?.CurrentHash ? " // CURRENT" : ""),
                Visibility(r.Package) + " // " + r.CreatedAt.ToString("g") + " // " + (r.ReleaseNotes ?? ""),
                r.Hash == _selectedRevision?.Hash, r.DeletedAt != null)).ToImmutableArray()
        };
    }

    public CommunityActionResult SetAddress(string address)
    {
        Verify(); if (_busy) return Reject("Cancel the current Community operation before changing the service.");
        try { _backend.SetAddress(address); _address = string.IsNullOrWhiteSpace(address) ? _backend.DefaultAddress : address.Trim(); }
        catch (Exception ex) { return Reject(ex.Message); }
        _selected = null; _package = null; _projects = ImmutableArray<CommunityMapProject>.Empty; _offset = 0;
        _selectedRevision = null; _revisions = ImmutableArray<CommunityMapRevision>.Empty;
        _publication?.Dispose(); _publication = null; _publishRequest = null; _conflict = null; _form = CommunityForm.None;
        _confirmed = null; _confirmation = "";
        Touch(); return CommunityActionResult.Ok;
    }
    public void Refresh()
    {
        Verify();
        if (_busy) return;
        var tab = _tab;
        Start("Loading Community maps…", async (token, progress) => new Catalog(await _backend.BrowseAsync(tab, token).ConfigureAwait(false), "Community catalog refreshed."));
    }
    public void SetTab(CommunityTab tab)
    {
        Verify(); if (!Enum.IsDefined(tab) || _busy) return;
        _confirmed = null; _confirmation = "";
        _publication?.Dispose(); _publication = null; _publishRequest = null; _conflict = null;
        _tab = tab; _offset = 0; _selected = null; _package = null; _selectedRevision = null; _form = CommunityForm.None; Touch(); Refresh();
    }
    public void SetSort(CommunitySort sort) { Verify(); if (Enum.IsDefined(sort)) { _sort = sort; _offset = 0; Touch(); } }
    public void SetLifecycle(CommunityLifecycle lifecycle) { Verify(); if (Enum.IsDefined(lifecycle)) { _lifecycle = lifecycle; _offset = 0; Touch(); } }
    public void Search(string query) { Verify(); _search = query.Trim(); _offset = 0; Touch(); }
    public const int PageSize = 4;
    public void Page(int direction) { Verify(); if (direction is -1 or 1) { _offset = Math.Max(0, _offset + direction * PageSize); Touch(); } }
    public void RevisionPage(int direction) { Verify(); if (direction is -1 or 1) { _revisionOffset = Math.Max(0, _revisionOffset + direction * 8); Touch(); } }
    public CommunityActionResult SelectRow(int index, long expectedRevision)
    {
        Verify(); var snapshot = Snapshot();
        if (_busy || expectedRevision != _revision || index < 0 || index >= snapshot.Maps.Length) return Reject("The Community list changed. Select the map again.");
        _selected = _projects.First(p => p.MapId == snapshot.Maps[index].MapId); _package = Copy(Presentation(_selected));
        _selectedRevision = null; _revisions = ImmutableArray<CommunityMapRevision>.Empty; _form = CommunityForm.None; _error = ""; Touch(); return CommunityActionResult.Ok;
    }
    public CommunityActionResult SelectRevision(int index, long expectedRevision)
    {
        Verify(); var snapshot = Snapshot();
        if (_busy || expectedRevision != _revision || index < 0 || index >= snapshot.Revisions.Length) return Reject("The revision list changed. Select it again.");
        _selectedRevision = _revisions[_revisionOffset + index]; _package = Copy(_selectedRevision.Package); _error = ""; Touch(); return CommunityActionResult.Ok;
    }

    public CommunityActionResult Detail(CommunityDetailAction action, string expectedHash)
    {
        Verify();
        if (!Enum.IsDefined(action) || _busy) return Reject("Community is busy.");
        if (action == CommunityDetailAction.Publish) { OpenPublish(); return CommunityActionResult.Ok; }
        if (_selected == null || _package == null || _package.Hash != expectedHash) return Reject("The selected package changed. Review its details first.");
        var map = _selected; var package = _package; var tab = _tab;
        switch (action)
        {
            case CommunityDetailAction.Install:
            case CommunityDetailAction.InstallAndHost:
                try { _backend.ValidateInstallation(package); }
                catch (Exception ex) { return Reject(ex.Message); }
                _installAndHost = action == CommunityDetailAction.InstallAndHost;
                Start("Downloading and preparing exact map revision…", async (token, progress) => await _backend.PrepareInstallAsync(package, progress, token).ConfigureAwait(false));
                break;
            case CommunityDetailAction.Favorite:
                Start("Updating favorite…", async (token, progress) => { await _backend.FavoriteAsync(map.MapId, !package.Favorited, token).ConfigureAwait(false); return (await Reload(tab, token, "Favorite updated.").ConfigureAwait(false)) with { FavoriteMap = map.MapId, Favorited = !package.Favorited }; }); break;
            case CommunityDetailAction.Revisions:
                _form = CommunityForm.Revisions; _revisionOffset = 0;
                Start("Loading immutable revision history…", async (token, progress) => await _backend.RevisionsAsync(map.MapId, tab == CommunityTab.MyMaps, token).ConfigureAwait(false)); break;
            case CommunityDetailAction.Report: _form = CommunityForm.Report; _reportReason = 0; _error = ""; Touch(); break;
            case CommunityDetailAction.CopyLink: break;
            case CommunityDetailAction.Archive:
            case CommunityDetailAction.RestoreMap:
            case CommunityDetailAction.DeleteMap:
                if (_tab != CommunityTab.MyMaps || !map.CanManageLifecycle) return Reject("This account cannot manage the map lifecycle.");
                void Modify() => Start("Updating map lifecycle…", async (token, progress) => { await _backend.ModifyMapAsync(map.MapId, action, token).ConfigureAwait(false); return await Reload(tab, token, "Map lifecycle updated.").ConfigureAwait(false); });
                if (action == CommunityDetailAction.DeleteMap) Confirm("Move this map to Deleted Maps? Its exact packages remain available during the service restore window.", Modify);
                else Modify();
                break;
        }
        return CommunityActionResult.Ok;
    }

    public CommunityActionResult Creator(CommunityCreatorAction action, string expectedHash)
    {
        Verify();
        if (_busy || _tab != CommunityTab.MyMaps || _selected == null || _package == null || _package.Hash != expectedHash || !Enum.IsDefined(action))
            return Reject("Review a creator-owned exact revision before changing it.");
        var map = _selected; var package = _package; var tab = _tab;
        int revision = _selectedRevision?.RevisionNumber ?? map.LatestRevision.RevisionNumber;
        if (action is CommunityCreatorAction.DeleteRevision or CommunityCreatorAction.RestoreRevision && !map.CanManageLifecycle)
            return Reject("This account cannot manage revision lifecycle.");
        if (action == CommunityCreatorAction.DeleteRevision && _revisions.Count(r => r.DeletedAt == null) <= 1)
            return Reject("Keep at least one active revision. Delete the map instead.");
        void Modify() => Start("Updating immutable revision…", async (token, progress) =>
        {
            await _backend.ModifyAsync(map.MapId, package.Hash, revision, action, token).ConfigureAwait(false);
            return (await Reload(tab, token, "Revision updated.").ConfigureAwait(false)) with { Revisions = await _backend.RevisionsAsync(map.MapId, true, token).ConfigureAwait(false) };
        });
        if (action == CommunityCreatorAction.DeleteRevision) Confirm("Delete this exact revision? You can restore it during the service restore window.", Modify);
        else Modify();
        return CommunityActionResult.Ok;
    }

    public void ConfirmAction() { Verify(); if (_busy || _form != CommunityForm.Confirm || _confirmed == null) return; var action = _confirmed; _confirmed = null; _form = CommunityForm.None; action(); Touch(); }
    public void CancelForm()
    {
        Verify(); if (_busy) return;
        _confirmed = null; _confirmation = "";
        if (_publication != null && _conflict != null) { Conflict(CommunityConflictAction.Discard); _form = CommunityForm.None; Touch(); return; }
        _publication?.Dispose(); _publication = null; _publishRequest = null; _conflict = null; _form = CommunityForm.None; _error = ""; Touch();
    }
    public void OpenPublish() { Verify(); if (_busy) return; _publication?.Dispose(); _publication = null; _publishRequest = null; _form = CommunityForm.Publish; _visibility = CommunityVisibility.Published; _error = ""; Touch(); }
    public void OpenImport() { Verify(); if (!_busy) { _form = CommunityForm.Import; _error = ""; Touch(); } }
    public void SetVisibility(CommunityVisibility visibility) { Verify(); if (Enum.IsDefined(visibility) && !_busy) { _visibility = visibility; Touch(); } }
    public void SetReportReason(int reason) { Verify(); if (reason >= 0 && reason < MapCreatorCatalog.ReportReasons.Length) { _reportReason = reason; Touch(); } }
    public CommunityActionResult SubmitReport(string detail)
    {
        Verify();
        if (_busy || _selected == null || _package == null || detail.Length > 4000) return Reject("Select a map and use at most 4,000 report characters.");
        var map = _selected; var report = new MapReportRequest(_package.Version, MapCreatorCatalog.ReportReasons[_reportReason], detail);
        Start("Submitting report for moderation…", async (token, progress) => { await _backend.ReportAsync(map.MapId, report, token).ConfigureAwait(false); return "Report submitted for moderation."; }); return CommunityActionResult.Ok;
    }
    public CommunityActionResult PreparePublish(string path, string notes)
    {
        Verify(); if (_busy || notes.Length > 4000 || string.IsNullOrWhiteSpace(path)) return Reject("Choose a saved project/package and use at most 4,000 release-note characters.");
        _publication?.Dispose(); _publication = null; _publishRequest = null;
        _publishNotes = notes; _form = CommunityForm.Publish;
        Start("Validating and building immutable package…", async (token, progress) => await _backend.PreparePublicationAsync(path, progress, token).ConfigureAwait(false)); return CommunityActionResult.Ok;
    }
    private string _publishNotes = "";
    public CommunityActionResult Publish(string notes)
    {
        Verify();
        if (_busy || _publication == null || _publishRequest == null || notes.Length > 4000) return Reject("Validate a saved project/package before uploading it.");
        if (_publication.Existing?.DeletedAt != null) return Reject("Restore the deleted map before publishing another revision.");
        _publishRequest = _publishRequest with { ReleaseNotes = notes }; BeginPublish(); return CommunityActionResult.Ok;
    }
    public CommunityActionResult Import(string path)
    {
        Verify(); if (_busy || string.IsNullOrWhiteSpace(path)) return Reject("Choose a local .ppmap package first.");
        try { _backend.ValidateInstallation(null); } catch (Exception ex) { return Reject(ex.Message); }
        _installAndHost = false;
        Start("Validating and preparing local package…", async (token, progress) => await _backend.PrepareImportAsync(path, progress, token).ConfigureAwait(false)); return CommunityActionResult.Ok;
    }
    public void Conflict(CommunityConflictAction action)
    {
        Verify(); if (_busy || _conflict == null || _publication == null || _publishRequest == null || !Enum.IsDefined(action)) return;
        switch (action)
        {
            case CommunityConflictAction.ReviewLatest:
            case CommunityConflictAction.Discard:
                var publication = _publication; var mapId = publication.Identity.MapId; var tab = _tab;
                Start("Discarding pending upload and refreshing history…", async (token, progress) =>
                {
                    await _backend.DiscardPublicationAsync(publication, token).ConfigureAwait(false);
                    var catalog = await Reload(tab, token, "Pending upload discarded; Community refreshed.").ConfigureAwait(false);
                    return action == CommunityConflictAction.ReviewLatest
                        ? catalog with { ClearPublication = true, ReviewMap = mapId, Revisions = await _backend.RevisionsAsync(mapId, true, token).ConfigureAwait(false) }
                        : catalog with { ClearPublication = true };
                }); break;
            case CommunityConflictAction.PublishAnyway:
            case CommunityConflictAction.DraftBranch:
                if (_conflict.Code != "stale_parent") return;
                _publishRequest = _publishRequest with { ExistingMap = true, AllowStaleParent = true };
                if (action == CommunityConflictAction.DraftBranch) _visibility = CommunityVisibility.Draft;
                BeginPublish(); break;
            case CommunityConflictAction.UploadAsRevision:
                if (_conflict.Code != "map_already_exists" || _conflict.LatestHash == null) return;
                _publishRequest = _publishRequest with { ExistingMap = true, ExpectedParentHash = _conflict.LatestHash, AllowStaleParent = false };
                BeginPublish(); break;
        }
        Touch();
    }

    public string? PackageLink() { Verify(); return _package == null ? null : _address.TrimEnd('/') + "/packages/" + _package.Hash; }
    public bool TryTakeHostRequest(out CommunityHostRequest request)
    {
        Verify(); if (_hostRequest is not { } pending) { request = default; return false; }
        _hostRequest = null; request = pending; return true;
    }
    public void CancelWork()
    {
        Verify(); _operation++; _work?.Cancel(); _work?.Dispose(); _work = null; _busy = false; _state = CommunityPageState.Cancelled;
        if (_publishing && _publication != null) { DisposeAfterWorker(_publication); _publication = null; _publishRequest = null; }
        _publishing = false;
        _status = "Community operation cancelled."; _hostRequest = null; Touch();
    }
    public void Tick()
    {
        Verify();
        for (int i = 0; i < 64 && _completed.TryDequeue(out var completion); i++)
        {
            if (completion.Operation != _operation) { (completion.Result as IDisposable)?.Dispose(); continue; }
            if (completion.Progress) { if (_busy) { _progress = (CommunityTransferProgress)completion.Result!; Touch(); } continue; }
            _busy = false; _publishing = false;
            if (completion.Error != null)
            {
                if (completion.Error is CommunityRevisionConflictException conflict)
                { _conflict = conflict.Conflict; _form = CommunityForm.Conflict; _status = "Revision conflict needs your decision."; _state = CommunityPageState.Ready; }
                else if (completion.Error is OperationCanceledException) { _state = CommunityPageState.Cancelled; _status = "Operation cancelled."; }
                else { _error = completion.Error.Message; _state = completion.Error is HttpRequestException http
                    ? http.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden ? CommunityPageState.Unauthorized : CommunityPageState.Offline : CommunityPageState.Failed; }
                _work?.Dispose(); _work = null; Touch(); continue;
            }
            try { switch (completion.Result)
            {
                case Catalog catalog:
                    var copied = catalog.Projects.Select(Copy).ToImmutableArray();
                    if (copied.Select(p => p.MapId).Distinct().Count() != copied.Length) throw new InvalidDataException("Community returned duplicate map identities.");
                    _projects = copied;
                    if (_selected != null) _selected = _projects.FirstOrDefault(p => p.MapId == _selected.MapId) ?? _selected;
                    if (_package != null && _selected != null)
                    {
                        // Favorite metadata may change while the package anchor remains immutable.
                        var metadata = _selected.CurrentRevision?.Hash == _package.Hash ? _selected.CurrentRevision.Package
                            : _selected.LatestRevision.Hash == _package.Hash ? _selected.LatestRevision.Package : null;
                        if (metadata != null) _package = _package with { Favorited = metadata.Favorited, FavoriteCount = metadata.FavoriteCount };
                    }
                    if (_package != null && _package.MapId == catalog.FavoriteMap && catalog.Favorited is { } favorite)
                        _package = _package with { Favorited = favorite };
                    if (catalog.Revisions != null && catalog.ReviewMap == null && _selected != null)
                    {
                        var refreshedHistory = catalog.Revisions.Select(Copy).ToImmutableArray();
                        if (refreshedHistory.Any(r => r.Package.MapId != _selected.MapId)) throw new InvalidDataException("Community returned mismatched creator revision history.");
                        _revisions = refreshedHistory;
                        if (_selectedRevision != null) _selectedRevision = _revisions.FirstOrDefault(r => r.Hash == _selectedRevision.Hash) ?? _selectedRevision;
                        if (_package != null && _revisions.FirstOrDefault(r => r.Hash == _package.Hash) is { } metadata) _package = Copy(metadata.Package);
                    }
                    if (catalog.ClearPublication)
                    {
                        _publication?.Dispose(); _publication = null; _publishRequest = null; _conflict = null; _form = CommunityForm.None;
                        if (catalog.ReviewMap is { } reviewMap)
                        {
                            _selected = _projects.FirstOrDefault(p => p.MapId == reviewMap);
                            if (_selected != null)
                            {
                                var reviewHistory = (catalog.Revisions ?? Array.Empty<CommunityMapRevision>()).Select(Copy).ToImmutableArray();
                                if (reviewHistory.Any(r => r.Package.MapId != reviewMap)) throw new InvalidDataException("Community returned mismatched review history.");
                                _package = Copy(Presentation(_selected)); _selectedRevision = null; _revisions = reviewHistory;
                                _revisionOffset = 0; _form = CommunityForm.Revisions;
                            }
                            else { _package = null; _selectedRevision = null; _form = CommunityForm.Publish; }
                        }
                    }
                    _state = _projects.IsEmpty ? CommunityPageState.Empty : CommunityPageState.Ready; _status = catalog.Message; break;
                case CommunityMapRevision[] revisions:
                    var history = revisions.Select(Copy).ToImmutableArray();
                    if (history.Any(r => r.Package.MapId != _selected?.MapId) || history.Select(r => r.RevisionNumber).Distinct().Count() != history.Length)
                        throw new InvalidDataException("Community returned mismatched revision history.");
                    _revisions = history; _state = CommunityPageState.Ready; _status = "Immutable revision history loaded."; break;
                case ICommunityInstallation installation:
                    using (installation)
                    {
                        try { string room = installation.Commit(_work!.Token); if (_installAndHost) _hostRequest = new(room, installation.Identity); _status = "Installed exact map revision: " + room; _state = CommunityPageState.Ready; _form = CommunityForm.None; }
                        catch (Exception ex) { _error = ex.Message; _state = CommunityPageState.Failed; }
                    }
                    break;
                case ICommunityPublication publication:
                    _publication = publication; _publishRequest = new(publication.Existing != null, publication.Existing?.LatestHash, _publishNotes);
                    _status = "Exact package validated. Review visibility and release notes, then upload."; _state = CommunityPageState.Ready; break;
                case CommunityPublishResult published:
                    _publication?.Dispose(); _publication = null; _publishRequest = null; _conflict = null; _form = CommunityForm.None;
                    _selected = published.Project == null ? _selected : Copy(published.Project); _package = Copy(published.Package);
                    _selectedRevision = published.Revision == null ? null : Copy(published.Revision);
                    _status = "Map revision uploaded: " + (published.Revision?.RevisionNumber.ToString() ?? published.Package.Version ?? published.Package.Hash); _state = CommunityPageState.Ready; break;
                case string status: _status = status; _state = CommunityPageState.Ready; _form = CommunityForm.None; break;
            } }
            catch (Exception ex) { (completion.Result as IDisposable)?.Dispose(); _error = ex.Message; _state = CommunityPageState.Failed; }
            _work?.Dispose(); _work = null; Touch();
        }
    }
    public void Dispose()
    {
        VerifyOwner(); if (_disposed) return;
        lock (_completionGate) _disposed = true;
        _operation++; _work?.Cancel(); _work?.Dispose(); _work = null;
        // An active publisher borrows the prepared package until its worker completes.
        if (_publication != null) { if (_publishing) DisposeAfterWorker(_publication); else _publication.Dispose(); }
        _publication = null; _hostRequest = null;
        while (_completed.TryDequeue(out var completion)) (completion.Result as IDisposable)?.Dispose();
    }

    private void BeginPublish()
    {
        var publication = _publication!; var request = _publishRequest!; var visibility = _visibility;
        _publishing = true;
        Start("Uploading immutable map revision…", async (token, progress) =>
        {
            return await _backend.PublishAsync(publication, request, visibility, progress, token).ConfigureAwait(false);
        });
    }
    private async Task<Catalog> Reload(CommunityTab tab, CancellationToken token, string message) => new(await _backend.BrowseAsync(tab, token).ConfigureAwait(false), message);
    private void Start(string status, Func<CancellationToken, Action<CommunityTransferProgress>, Task<object?>> action)
    {
        if (_busy || _disposed) return;
        _work?.Dispose(); _work = new(); var token = _work.Token; uint operation = ++_operation;
        _busy = true; _state = CommunityPageState.Loading; _status = status; _error = ""; _progress = default; Touch();
        _worker = Task.Run(async () =>
        {
            object? result = null; Exception? error = null;
            try { result = await action(token, value => Enqueue(new(operation, value, Progress: true))).ConfigureAwait(false); } catch (Exception ex) { error = ex; }
            Enqueue(new(operation, result, error));
        });
    }
    private void Enqueue(Completion completion)
    {
        lock (_completionGate) { if (_disposed) (completion.Result as IDisposable)?.Dispose(); else _completed.Enqueue(completion); }
    }
    private void DisposeAfterWorker(IDisposable resource)
    {
        if (_worker == null || _worker.IsCompleted) resource.Dispose();
        else _ = _worker.ContinueWith(_ => resource.Dispose(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private void Confirm(string text, Action action) { _confirmation = text; _confirmed = action; _form = CommunityForm.Confirm; Touch(); }
    private CommunityMapProject[] Filtered()
    {
        var query = _projects.Where(p => (_tab != CommunityTab.MyMaps || _lifecycle switch
            { CommunityLifecycle.Active => p.ArchivedAt == null && p.DeletedAt == null, CommunityLifecycle.Archived => p.ArchivedAt != null && p.DeletedAt == null, CommunityLifecycle.Deleted => p.DeletedAt != null, _ => true })
            && (_search.Length == 0 || (p.Name + " " + p.DisplayName + " " + p.Author + " " + Presentation(p).Version + " " + string.Join(" ", Presentation(p).SupportedModes)).Contains(_search, StringComparison.OrdinalIgnoreCase)));
        return (_sort switch { CommunitySort.Favorites => query.OrderByDescending(p => Presentation(p).FavoriteCount),
            CommunitySort.RecentlyUpdated => query.OrderByDescending(p => p.UpdatedAt), _ => query.OrderBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase) })
            .ThenBy(p => p.DisplayName ?? p.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private CommunityMap Presentation(CommunityMapProject p) => PresentationRevision(p).Package;
    private CommunityMapRevision PresentationRevision(CommunityMapProject p) => _tab == CommunityTab.MyMaps ? p.LatestRevision : p.CurrentRevision ?? p.LatestRevision;
    private static string Visibility(CommunityMap p) => p.Draft ? "Draft" : p.Listed ? "Published" : "Unlisted";
    private static string Lifecycle(CommunityMapProject p) => p.DeletedAt != null ? "Deleted" : p.ArchivedAt != null ? "Archived" : "Active";
    private static CommunityMap Copy(CommunityMap p)
    {
        if (p == null || p.MapId == Guid.Empty || !MapHash256.TryParse(p.Hash, out _) || !MapHash256.TryParse(p.ContentHash, out _) || p.Bytes < 0)
            throw new InvalidDataException("Community returned an invalid immutable package identity.");
        return p with { SupportedModes = p.SupportedModes?.Select(m => m ?? "").ToArray() ?? Array.Empty<string>() };
    }
    private static CommunityMapRevision Copy(CommunityMapRevision r)
    {
        if (r == null || r.RevisionNumber < 1 || r.Package?.Hash != r.Hash) throw new InvalidDataException("Community returned an invalid revision identity.");
        return r with { Package = Copy(r.Package) };
    }
    private static CommunityMapProject Copy(CommunityMapProject p)
    {
        if (p == null) throw new InvalidDataException("Community returned an empty map identity.");
        var latest = Copy(p.LatestRevision); var current = p.CurrentRevision == null ? null : Copy(p.CurrentRevision);
        if (p.MapId != latest.Package.MapId || p.LatestHash != latest.Hash || current != null && (current.Package.MapId != p.MapId || p.CurrentHash != current.Hash))
            throw new InvalidDataException("Community returned a mismatched map/revision identity.");
        return p with { CurrentRevision = current, LatestRevision = latest };
    }
    private CommunityActionResult Reject(string text) { _error = text; Touch(); return CommunityActionResult.Reject(text); }
    private void Touch() { _revision++; _cached = null; }
    private void Verify() { VerifyOwner(); ObjectDisposedException.ThrowIf(_disposed, this); }
    private void VerifyOwner() { if (_owner != Environment.CurrentManagedThreadId) throw new InvalidOperationException("Community belongs to the engine thread."); }
}
