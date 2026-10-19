#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Native Community browse and verified installation; creator editing remains in Studio.</summary>
internal sealed class NativeCommunityDiscoverPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityDiscoverController _controller;
    private readonly NativeCommunityInstallController _install;
    private readonly NativeCommunityCreatorController _creator;
    private readonly NativeCommunityRevisionsPage _history;
    private readonly NativeCommunityTransfersPage _transfers;
    private readonly NativeCommunityReportPage _report;
    private readonly NativeCommunityImportPage _import;
    private readonly NativeCommunityPreview _preview;
    private long _shownPreviewRevision;
    private bool _creatorMode, _shownCreatorMode;
    private RmlUiDocumentToken _page;
    private long _shownRevision, _shownInstallRevision, _shownCreatorRevision, _presentationRevision;
    private bool _disposed;
    private readonly Func<string, bool> _clipboard;
    private string _notice = "";

    public bool TryTakeHostRequest(out NativeCommunityHostSelection? selection)
    {
        selection = null;
        return !_disposed && _pages.Top == _page && !_creatorMode
            && _install.TryTakeHostRequest(out selection);
    }

    public void ShowHostFailure(string status)
    {
        _notice = status;
        _shownRevision = -1;
        Refresh();
    }

    public bool Back()
    {
        if (_import.IsOpen) return _import.Back();
        if (_report.IsOpen) return _report.Back();
        if (_transfers.IsOpen) return _transfers.Back();
        if (_history.IsOpen)
        {
            bool handled = _history.Back();
            if (handled && !_history.IsOpen && _creatorMode) _creator.Refresh();
            return handled;
        }
        if (_disposed || !_creatorMode || _creator.Tick().Confirmation.Length == 0) return false;
        _creator.Dismiss(); Refresh(); return true;
    }

    public NativeCommunityDiscoverPage(RmlUiHost host, RmlUiPageManager pages,
        INativeCommunityReader reader, INativeCommunityInstaller installer,
        INativeCommunityCreatorAuthority creator, INativeCommunityRevisionsAuthority? revisions = null,
        INativeCommunityTransfersAuthority? transfers = null, Func<string, bool>? clipboard = null,
        INativeCommunityReportAuthority? reports = null, INativeCommunityImportAuthority? imports = null)
    {
        _clipboard = clipboard ?? CopyToClipboard;
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeCommunityDiscoverController(reader);
        _preview = new(reader as INativeCommunityPreviewSource, Path.Combine(
            MphRead.Mods.Platform.AppPaths.UserDataDirectory, "rmlui-media", "community-previews"));
        _install = new NativeCommunityInstallController(installer);
        _creator = new NativeCommunityCreatorController(creator);
        _history = new NativeCommunityRevisionsPage(host, pages, revisions ?? new NativeCommunityRevisionsService(), _install);
        _transfers = new NativeCommunityTransfersPage(host, pages, transfers ?? new CurrentCommunityTransfersService());
        _report = new NativeCommunityReportPage(host, pages, reports ?? reader as INativeCommunityReportAuthority ?? new NativeCommunityReader());
        _import = new NativeCommunityImportPage(host, pages, imports ?? new NativeCommunityImportService());
    }

    public void Open()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _page = _pages.OpenPage(new("community-discover",
            "pages/community/native-discover.rml", "native_community_search"));
        _controller.Refresh();
        Refresh();
    }

    public bool Handle(in RmlUiIntent intent)
    {
        if (_import.IsOpen) return _import.Handle(intent);
        if (_report.IsOpen) return _report.Handle(intent);
        if (_transfers.IsOpen) return _transfers.Handle(intent);
        if (_history.IsOpen)
        {
            bool handled = _history.Handle(intent);
            if (handled && !_history.IsOpen && _creatorMode) _creator.Refresh();
            return handled;
        }
        if (_disposed || intent.Document != _page || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.CommunityReport:
                var report = _controller.Tick();
                if (_creatorMode || report.Busy || _install.Tick().Busy
                    || report.Catalog == NativeCommunityCatalog.Installed || report.SelectedEntry is not { } reported) return false;
                return _report.Open(reported, report.Witness);
            case RmlUiIntentKind.CommunityTab:
                if (intent.Argument == 2)
                {
                    _install.Cancel();
                    _creator.Dismiss();
                    return _transfers.Open();
                }
                if (intent.Argument is not (0 or 1 or 3 or 4)) return false;
                bool nextCreator = intent.Argument == 1;
                _notice = "";
                _install.Cancel();
                if (nextCreator)
                {
                    _controller.Cancel();
                    if (!_creatorMode) _creator.Refresh();
                }
                else
                {
                    _creator.Cancel();
                    var next = intent.Argument switch { 3 => NativeCommunityCatalog.Favorites,
                        4 => NativeCommunityCatalog.Installed, _ => NativeCommunityCatalog.Discover };
                    bool same = _controller.Current().Catalog == next;
                    if (!_controller.Catalog(next)) return false;
                    if (_creatorMode || same) _controller.Refresh();
                }
                _creatorMode = nextCreator;
                break;
            case RmlUiIntentKind.CommunityRefresh:
                _notice = "";
                _install.Cancel();
                if (_creatorMode) _creator.Refresh();
                else _controller.Refresh();
                break;
            case RmlUiIntentKind.CommunitySearch:
                string query = _host.ReadField(_page, "native_community_search", 256);
                if (_creatorMode) return false;
                _install.Cancel();
                _controller.Search(query);
                break;
            case RmlUiIntentKind.CommunitySort:
                if (_creatorMode) return false;
                _install.Cancel();
                _controller.Sort(intent.Argument);
                break;
            case RmlUiIntentKind.CommunityPage:
                _install.Cancel();
                if (_creatorMode) _creator.MovePage(intent.Argument);
                else _controller.Page(intent.Argument);
                break;
            case RmlUiIntentKind.CommunitySelect:
                _notice = "";
                _install.Cancel();
                if (_creatorMode) _creator.Select(intent.Argument);
                else _controller.Select(intent.Argument);
                break;
            case RmlUiIntentKind.CommunityDetailAction:
                var catalog = _controller.Tick();
                _notice = "";
                if (intent.Argument == 2)
                {
                    if (_creatorMode) return false;
                    bool accepted = _controller.SetFavorite(catalog.Catalog != NativeCommunityCatalog.Favorites);
                    Refresh(); return accepted;
                }
                if (intent.Argument == 6)
                {
                    if (_creatorMode || catalog.Busy || catalog.Catalog == NativeCommunityCatalog.Installed
                        || catalog.SelectedEntry is not { } item
                        || !NativeCommunityInstallController.IsVerifiedSelection(item)) return false;
                    try
                    {
                        string link = new Uri(MapCommunityClient.ValidateBaseAddress(
                            NetworkMapIdentity.ConfiguredDownloadSource()), "packages/" + item.LatestHash.ToLowerInvariant()).AbsoluteUri;
                        bool copied = _clipboard(link);
                        if (copied) _host.SetClipboard(link);
                        _notice = copied ? "EXACT PACKAGE LINK COPIED" : "CLIPBOARD UNAVAILABLE // RETRY COPY LINK";
                    }
                    catch (Exception ex) when (ex is ArgumentException or UriFormatException or IOException or InvalidOperationException)
                    { _notice = "PACKAGE LINK UNAVAILABLE // CHECK COMMUNITY ADDRESS"; }
                    _shownRevision = -1; Refresh(); return true;
                }
                if (intent.Argument == 3)
                {
                    if (_creatorMode)
                    {
                        var owned = _creator.Tick();
                        if (owned.Busy || owned.Confirmation.Length != 0 || owned.Selected == null) return false;
                        return _history.Open(new(owned.Selected.MapId, true, owned.Selected.LatestHash) { AccountLease = owned.AccountLease });
                    }
                    var discover = _controller.Current();
                    if (discover.Busy || discover.Catalog == NativeCommunityCatalog.Installed
                        || discover.SelectedEntry is not { } project) return false;
                    return _history.Open(new(project.MapId, false, project.LatestHash));
                }
                if (_creatorMode || intent.Argument is not (0 or 1) || catalog.Busy
                    || (intent.Argument == 0 && catalog.Catalog == NativeCommunityCatalog.Installed)
                    || catalog.SelectedEntry is not { } selected
                    || selected.MinimumProtocol > NetConfig.ProtocolVersion)
                    return false;
                try
                {
                    if (!GameFiles.Ready) { _install.FailPreflight(); break; }
                    // Same owner-side preflight as the established Avalonia path.
                    MapRuntimeUsage.RequireInstallationAllowed();
                    GameFiles.ApplyPaths();
                    _install.Begin(selected, hostAfterInstall: intent.Argument == 1);
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException
                    or ArgumentException or UnauthorizedAccessException)
                {
                    _install.FailPreflight();
                }
                break;
            case RmlUiIntentKind.CommunityCancelWork:
                _install.Cancel();
                break;
            case RmlUiIntentKind.CommunityCreatorAction:
                if (!_creatorMode || intent.Argument is not (0 or 1)) return false;
                _creator.Propose(intent.Argument == 0
                    ? NativeCreatorAction.Archive : NativeCreatorAction.Restore);
                break;
            case RmlUiIntentKind.CommunityConfirm:
                if (!_creatorMode) return false;
                _creator.Confirm();
                break;
            case RmlUiIntentKind.CommunityCancel:
                if (!_creatorMode) return false;
                _creator.Dismiss();
                break;
            case RmlUiIntentKind.CommunityImport:
                if (_install.Tick().Busy) return false;
                return _import.Open();
            default:
                return false;
        }
        Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || _page == default) return;
        // History checks its account lease before the shared installer can register a completion.
        _report.Refresh();
        _import.Refresh();
        if (_import.TakeImported())
        {
            _creatorMode = false; _creator.Cancel();
            bool same = _controller.Current().Catalog == NativeCommunityCatalog.Installed;
            _controller.Catalog(NativeCommunityCatalog.Installed);
            if (same) _controller.Refresh();
        }
        _history.Refresh();
        _transfers.Refresh();
        NativeCommunityDiscoverController.Snapshot snapshot = _controller.Tick();
        NativeCommunityPreview.View preview = _preview.Read(!_creatorMode && !snapshot.Busy ? snapshot.SelectedEntry : null);
        NativeCommunityInstallView install = _install.Tick();
        NativeCreatorView creator = _creator.Tick();
        if (snapshot.Revision == _shownRevision
            && install.Revision == _shownInstallRevision
            && creator.Revision == _shownCreatorRevision
            && _creatorMode == _shownCreatorMode && preview.Revision == _shownPreviewRevision) return;
        bool installedSelected = snapshot.SelectedEntry is { } current
            && install.CompletedMapId == current.MapId
            && install.CompletedHash.Equals(current.LatestHash,
                StringComparison.OrdinalIgnoreCase);
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["image:native_community_preview"] = RmlUiBindingValue.FromText(preview.Image),
            ["visible:native_community_preview"] = RmlUiBindingValue.FromBoolean(preview.Image.Length > 0),
            ["native_community_preview_status"] = RmlUiBindingValue.FromText(preview.Status),
            ["native_community_notice"] = RmlUiBindingValue.FromText(_notice),
            ["class:native_community_public:selected"] = RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog == NativeCommunityCatalog.Discover),
            ["class:native_community_owned:selected"] = RmlUiBindingValue.FromBoolean(_creatorMode),
            ["class:native_community_favorites:selected"] = RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog == NativeCommunityCatalog.Favorites),
            ["class:native_community_installed:selected"] = RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog == NativeCommunityCatalog.Installed),
            ["native_community_favorite"] = RmlUiBindingValue.FromText(snapshot.Catalog == NativeCommunityCatalog.Favorites ? "REMOVE FAVORITE" : "ADD TO FAVORITES"),
            ["visible:native_community_favorite"] = RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog != NativeCommunityCatalog.Installed),
            ["visible:native_community_copy_link"] = RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog != NativeCommunityCatalog.Installed),
            ["visible:native_community_report"] = RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog != NativeCommunityCatalog.Installed),
            ["disabled:native_community_report"] = RmlUiBindingValue.FromBoolean(_creatorMode || snapshot.Busy || install.Busy || snapshot.SelectedEntry == null),
            ["disabled:native_community_favorite"] = RmlUiBindingValue.FromBoolean(_creatorMode || snapshot.Busy || snapshot.SelectedEntry == null),
            ["disabled:native_community_copy_link"] = RmlUiBindingValue.FromBoolean(_creatorMode || snapshot.Busy || snapshot.SelectedEntry == null),
            ["native_community_notes"] = RmlUiBindingValue.FromText(!_creatorMode && snapshot.SelectedEntry is { } notes ? notes.Description : ""),
            ["native_community_status"] = RmlUiBindingValue.FromText(
                _creatorMode ? creator.Confirmation.Length > 0
                    ? creator.Confirmation : creator.Status
                : install.Busy || installedSelected || (install.Status.Length > 0
                    && install.CompletedMapId == null)
                    ? install.Status : snapshot.Status),
            ["disabled:native_community_search"] =
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["disabled:native_community_find"] =
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["disabled:native_community_name_sort"] =
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["disabled:native_community_new_sort"] =
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["disabled:native_community_popular_sort"] =
                RmlUiBindingValue.FromBoolean(_creatorMode || snapshot.Catalog == NativeCommunityCatalog.Installed),
            ["visible:native_community_install"] =
                RmlUiBindingValue.FromBoolean(!_creatorMode && snapshot.Catalog != NativeCommunityCatalog.Installed),
            ["visible:native_community_install_host"] = RmlUiBindingValue.FromBoolean(!_creatorMode),
            ["native_community_install_host"] = RmlUiBindingValue.FromText(
                snapshot.Catalog == NativeCommunityCatalog.Installed ? "HOST INSTALLED MAP" : "INSTALL & HOST"),
            ["disabled:native_community_install_host"] = RmlUiBindingValue.FromBoolean(
                _creatorMode || install.Busy || snapshot.Busy || snapshot.SelectedEntry == null
                || !GameFiles.Ready || snapshot.SelectedEntry?.MinimumProtocol > NetConfig.ProtocolVersion),
            ["visible:native_community_archive"] =
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["visible:native_community_restore"] =
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["visible:native_community_creator_confirm"] =
                RmlUiBindingValue.FromBoolean(_creatorMode && creator.Confirmation.Length > 0),
            ["visible:native_community_creator_cancel"] =
                RmlUiBindingValue.FromBoolean(_creatorMode && creator.Confirmation.Length > 0),
            ["disabled:native_community_archive"] =
                RmlUiBindingValue.FromBoolean(!_creatorMode || creator.Busy
                    || creator.Selected == null || !creator.Selected.CanManage
                    || creator.Selected.Archived || creator.Selected.Deleted),
            ["disabled:native_community_restore"] =
                RmlUiBindingValue.FromBoolean(!_creatorMode || creator.Busy
                    || creator.Selected == null || !creator.Selected.CanManage
                    || (!creator.Selected.Archived && !creator.Selected.Deleted)),
            ["disabled:native_community_install"] = RmlUiBindingValue.FromBoolean(
                _creatorMode || install.Busy || snapshot.Busy || snapshot.SelectedEntry == null
                || !GameFiles.Ready || snapshot.SelectedEntry?.MinimumProtocol > NetConfig.ProtocolVersion),
            ["visible:native_community_history"] = RmlUiBindingValue.FromBoolean(_creatorMode || snapshot.Catalog != NativeCommunityCatalog.Installed),
            ["disabled:native_community_history"] = RmlUiBindingValue.FromBoolean(_creatorMode
                ? creator.Busy || creator.Selected == null || creator.Confirmation.Length != 0
                : snapshot.Busy || snapshot.SelectedEntry == null),
            ["visible:native_community_cancel_install"] =
                RmlUiBindingValue.FromBoolean(!_creatorMode && install.Busy),
            ["disabled:native_community_cancel_install"] =
                RmlUiBindingValue.FromBoolean(!install.Busy),
            ["native_community_title"] = RmlUiBindingValue.FromText(
                _creatorMode ? creator.Selected?.Name ?? "SELECT AN OWNED PROJECT"
                    : snapshot.SelectedEntry?.Name ?? "SELECT A COMMUNITY MAP"),
            ["native_community_description"] = RmlUiBindingValue.FromText(
                _creatorMode ? creator.Selected is { } owned
                    ? (owned.Archived ? "ARCHIVED" : owned.Deleted ? "DELETED" : "ACTIVE")
                        + " // " + owned.RevisionCount + " IMMUTABLE REVISIONS"
                        + " // " + (owned.CanManage ? "MANAGEABLE" : "READ ONLY")
                    : "Your authenticated map projects and revisions."
                : snapshot.SelectedEntry is { } item
                    ? item.Installed
                        ? $"{item.Creator} // VERSION {item.Version} // EXACT LOCAL PACKAGE\nREADY IN MAP PICKER // Host this installed revision or choose it in Offline Battle."
                        : $"{item.Creator} // Revision {item.Revision} // {item.MinPlayers}–{item.MaxPlayers} players // {item.Modes}\n{item.ArchiveBytes / 1048576.0:0.0} MiB // {item.FavoriteCount} FAVORITES // REQUIRES PROTOCOL {item.MinimumProtocol} (CURRENT {NetConfig.ProtocolVersion})"
                    : "Select a map to inspect its published immutable package."),
            ["native_community_hash"] = RmlUiBindingValue.FromText(
                _creatorMode ? creator.Selected?.LatestHash ?? ""
                    : snapshot.SelectedEntry is { } selected ? selected.LatestHash : ""),
            ["disabled:native_community_previous"] =
                RmlUiBindingValue.FromBoolean(_creatorMode
                    ? creator.Busy || !creator.CanPrevious
                    : snapshot.Busy || !snapshot.CanPrevious),
            ["disabled:native_community_next"] =
                RmlUiBindingValue.FromBoolean(_creatorMode
                    ? creator.Busy || !creator.CanNext
                    : snapshot.Busy || !snapshot.CanNext)
        };
        for (int i = 0; i < 8; i++)
        {
            bool visible = _creatorMode ? i < creator.Rows.Length : i < snapshot.Rows.Count;
            data["visible:native_community_row" + i] = RmlUiBindingValue.FromBoolean(visible);
            data["class:native_community_row" + i + ":selected"] =
                RmlUiBindingValue.FromBoolean(visible
                    && i == (_creatorMode ? creator.SelectedIndex : snapshot.Selected));
            if (_creatorMode)
            {
                NativeCreatorEntry? row = visible ? creator.Rows[i] : null;
                data["native_community_name" + i] =
                    RmlUiBindingValue.FromText(row?.Name ?? "");
                data["native_community_meta" + i] = RmlUiBindingValue.FromText(row is null
                    ? "" : (row.Archived ? "ARCHIVED" : row.Deleted ? "DELETED" : "ACTIVE")
                        + " // " + row.RevisionCount + " REVISIONS");
            }
            else
            {
                NativeCommunityEntry row = visible ? snapshot.Rows[i] : default;
                data["native_community_name" + i] =
                    RmlUiBindingValue.FromText(visible ? row.Name : "");
                data["native_community_meta" + i] = RmlUiBindingValue.FromText(visible
                    ? row.Installed ? $"{row.Creator} // INSTALLED // {row.Version}"
                        : $"{row.Creator} // REV {row.Revision} // {row.MinPlayers}–{row.MaxPlayers} PLAYERS" : "");
            }
        }
        _pages.Present(_page, ++_presentationRevision, data);
        _shownRevision = snapshot.Revision;
        _shownInstallRevision = install.Revision;
        _shownCreatorRevision = creator.Revision;
        _shownCreatorMode = _creatorMode;
        _shownPreviewRevision = preview.Revision;
    }

    private static unsafe bool CopyToClipboard(string value)
    {
        try { GLFW.SetClipboardString(null, value); return true; }
        catch (Exception) { return false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _preview.Dispose();
        _report.Dispose();
        _import.Dispose();
        _transfers.Dispose();
        _history.Dispose();
        _controller.Dispose();
        _install.Dispose();
        _creator.Dispose();
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
    }
}

//// Exact archive and transfer-queue pipeline shared with the current Community workspace.
internal sealed class NativeCommunityInstaller : INativeCommunityInstaller
{
    public async Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,
        CancellationToken cancellation)
        => await InstallAsync(selected, cancellation, GameFiles.Root).ConfigureAwait(false);

    public async Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,
        CancellationToken cancellation, string expectedRoot)
    {
        using var files = GameFiles.TryUseConfiguredFiles(expectedRoot);
        if (files == null || !NativeCommunityInstallController.IsVerifiedSelection(selected) || !GameFiles.Ready)
            throw new InvalidOperationException("Invalid Community selection or game-file setup.");
        if (selected.Installed)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!CustomRooms.Installed.TryGet(selected.MapId, out var existing)
                || !existing.Identity.PackageHash.ToString().Equals(selected.LatestHash, StringComparison.OrdinalIgnoreCase)
                || (!String.IsNullOrEmpty(selected.RoomKey)
                    && !existing.Identity.RoomKey.Equals(selected.RoomKey, StringComparison.OrdinalIgnoreCase))
                || !CustomRooms.Installed.HasExact(existing.Identity))
                throw new InvalidDataException("Installed Community revision changed.");
            var definition = MapDefinition.Load(existing.PackagePath);
            cancellation.ThrowIfCancellationRequested();
            return new(selected.MapId, selected.LatestHash, definition, existing.Identity);
        }
        string endpoint = NetworkMapIdentity.ConfiguredDownloadSource();
        var api = new CommunityMapOperations(endpoint);
        CommunityMap map = await api.GetPackageAsync(selected.LatestHash, cancellation)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Published Community revision is unavailable.");
        if (map.MinimumProtocol > NetConfig.ProtocolVersion)
            throw new InvalidDataException("Community package requires a newer protocol.");
        if (map.MapId != selected.MapId || !map.Hash.Equals(selected.LatestHash,
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Published Community package changed identity.");

        // The same durable queue is used by Avalonia and native RmlUi. No
        // independent receipt index, archive cache, or download manager.
        CommunityTransferManager queue = CommunityTransferSession.Shared;
        queue.StartPending();
        cancellation.ThrowIfCancellationRequested();
        Guid jobId = queue.EnqueueDownload(endpoint, map);
        CommunityTransferJob job = await queue.WaitAsync(jobId, cancellation)
            .ConfigureAwait(false);
        if (job.State != CommunityTransferState.Completed)
            throw new IOException("Verified Community download is not complete.");
        cancellation.ThrowIfCancellationRequested();
        MapRuntimeUsage.RequireInstallationAllowed();

        using var lease = queue.LeaseVerifiedArchive(jobId)
            ?? throw new IOException("Verified archive is unavailable.");
        MapDefinition installed = MapPackageInstaller.InstallLeased(lease, map.MapId,
            map.ContentHash, map.Hash, CustomRooms.UserMapDirectory);
        cancellation.ThrowIfCancellationRequested();
        var identity = MapContentIdentity.FromPackage(installed.BundlePath
            ?? throw new InvalidDataException("Installed package is unavailable."));
        return new NativeCommunityInstalled(map.MapId, map.Hash, installed, identity);
    }

    public void Register(NativeCommunityInstalled installed)
    {
        // The existing gameplay owner performs metadata registration on its
        // thread. Never publish a custom map while a match owns the scene.
        MapRuntimeUsage.RequireInstallationAllowed();
        Metadata.RegisterDownloadedMap(installed.Definition);
    }
}

internal sealed class NativeCommunityCreatorService : INativeCommunityCreatorAuthority, INativeCommunityCreatorAccountLease
{
    private object? _lease;
    public bool IsCurrentAccountLease(object? lease) => HunterLicenseClient.IsNativeAccountLeaseCurrent(lease);

    private static NativeCreatorEntry ToEntry(CommunityMapProject project)
        => new(project.MapId, project.DisplayName ?? project.Name,
            project.LatestHash, project.CurrentHash ?? "", project.RevisionCount,
            project.ArchivedAt != null, project.DeletedAt != null, project.CanManageLifecycle);

    public Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token)
        => HunterLicenseClient.WithNativeCommunityIdentityAsync(null, null, async (account, lease, credentials, ct) =>
        {
            var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource(), credentials);
            CommunityMapProjectPage data = await api.BrowseProjectsPageAsync(mine: true, favorites: false,
                sort: "updated", query: null, page: page, pageSize: 8, cancellation: ct).ConfigureAwait(false);
            Interlocked.Exchange(ref _lease, lease);
            return new NativeCreatorPage(data.Items.Select(ToEntry).ToArray(), data.Total, data.Page) { AccountLease = lease };
        }, token);

    public Task<NativeCreatorEntry?> ReadOwnedAsync(Guid mapId, CancellationToken token)
        => HunterLicenseClient.WithNativeCommunityIdentityAsync(Volatile.Read(ref _lease), null,
            async (account, lease, credentials, ct) =>
            {
                var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource(), credentials);
                CommunityMapProject? owned = await api.GetOwnedProjectAsync(mapId, ct).ConfigureAwait(false);
                return owned is null ? null : ToEntry(owned);
            }, token);

    public Task<bool> ExecuteAsync(NativeCreatorAction action, Guid mapId, CancellationToken token)
        => HunterLicenseClient.WithNativeCommunityIdentityAsync(Volatile.Read(ref _lease), null, async (account, lease, credentials, ct) =>
        {
            var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource(), credentials);
            CommunityMapProject? outcome = action switch
            {
                NativeCreatorAction.Archive => await api.ArchiveProjectAsync(mapId, ct).ConfigureAwait(false),
                NativeCreatorAction.Restore => await api.RestoreProjectAsync(mapId, ct).ConfigureAwait(false), _ => null
            };
            return outcome != null && outcome.MapId == mapId;
        }, token);
}
#endif
