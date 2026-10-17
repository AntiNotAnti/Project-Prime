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

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Native Community browse and verified installation; creator editing remains in Studio.</summary>
internal sealed class NativeCommunityDiscoverPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityDiscoverController _controller;
    private readonly NativeCommunityInstallController _install;
    private readonly NativeCommunityCreatorController _creator;
    private bool _creatorMode, _shownCreatorMode;
    private RmlUiDocumentToken _page;
    private long _shownRevision, _shownInstallRevision, _shownCreatorRevision, _presentationRevision;
    private bool _disposed;

    public bool OpenFullCommunityRequested { get; private set; }

    public NativeCommunityDiscoverPage(RmlUiHost host, RmlUiPageManager pages,
        INativeCommunityReader reader, INativeCommunityInstaller installer,
        INativeCommunityCreatorAuthority creator)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeCommunityDiscoverController(reader);
        _install = new NativeCommunityInstallController(installer);
        _creator = new NativeCommunityCreatorController(creator);
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
        if (_disposed || intent.Document != _page || _pages.Top != _page
            || !_pages.Accept(intent)) return false;
        switch (intent.Kind)
        {
            case RmlUiIntentKind.CommunityTab:
                if (intent.Argument is not (0 or 1)) return false;
                bool nextCreator = intent.Argument == 1;
                if (_creatorMode != nextCreator)
                {
                    if (nextCreator) { _install.Cancel(); _creator.Refresh(); }
                    else { _creator.Cancel(); _controller.Refresh(); }
                    _creatorMode = nextCreator;
                }
                break;
            case RmlUiIntentKind.CommunityRefresh:
                if (_creatorMode) _creator.Refresh();
                else _controller.Refresh();
                break;
            case RmlUiIntentKind.CommunitySearch:
                string query = _host.ReadField(_page, "native_community_search", 256);
                if (_creatorMode) return false;
                _controller.Search(query);
                break;
            case RmlUiIntentKind.CommunitySort:
                if (_creatorMode) return false;
                _controller.Sort(intent.Argument);
                break;
            case RmlUiIntentKind.CommunityPage:
                if (_creatorMode) _creator.MovePage(intent.Argument);
                else _controller.Page(intent.Argument);
                break;
            case RmlUiIntentKind.CommunitySelect:
                if (_creatorMode) _creator.Select(intent.Argument);
                else _controller.Select(intent.Argument);
                break;
            case RmlUiIntentKind.CommunityDetailAction:
                if (_creatorMode || intent.Argument != 0 || _controller.Current().Busy
                    || _controller.Current().SelectedEntry is not { } selected)
                    return false;
                try
                {
                    if (!GameFiles.Ready) { _install.FailPreflight(); break; }
                    // Same owner-side preflight as the established Avalonia path.
                    MapRuntimeUsage.RequireInstallationAllowed();
                    GameFiles.ApplyPaths();
                    _install.Begin(selected);
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
                OpenFullCommunityRequested = true;
                return true;
            default:
                return false;
        }
        Refresh();
        return true;
    }

    public void Refresh()
    {
        if (_disposed || _page == default) return;
        NativeCommunityDiscoverController.Snapshot snapshot = _controller.Tick();
        NativeCommunityInstallView install = _install.Tick();
        NativeCreatorView creator = _creator.Tick();
        if (snapshot.Revision == _shownRevision
            && install.Revision == _shownInstallRevision
            && creator.Revision == _shownCreatorRevision
            && _creatorMode == _shownCreatorMode) return;
        bool installedSelected = snapshot.SelectedEntry is { } current
            && install.CompletedMapId == current.MapId
            && install.CompletedHash.Equals(current.LatestHash,
                StringComparison.OrdinalIgnoreCase);
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
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
                RmlUiBindingValue.FromBoolean(_creatorMode),
            ["visible:native_community_install"] =
                RmlUiBindingValue.FromBoolean(!_creatorMode),
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
                || !GameFiles.Ready),
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
                    ? $"{item.Creator} // Revision {item.Revision} // {item.MinPlayers}–{item.MaxPlayers} players // {item.Modes}"
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
                    ? $"{row.Creator} // REV {row.Revision} // {row.MinPlayers}–{row.MaxPlayers} PLAYERS" : "");
            }
        }
        _pages.Present(_page, ++_presentationRevision, data);
        _shownRevision = snapshot.Revision;
        _shownInstallRevision = install.Revision;
        _shownCreatorRevision = creator.Revision;
        _shownCreatorMode = _creatorMode;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        _install.Dispose();
        _creator.Dispose();
        if (_page != default && _pages.Page == _page) _pages.ClosePage();
    }
}

/// <summary>
/// The existing v2 Community application facade owns HTTP and supports the
/// fallback legacy API. Initialize/configure it off the render thread to
/// avoid reintroducing synchronous tab-entry stalls.
/// </summary>
internal sealed class NativeCommunityReader : INativeCommunityReader
{
    public Task<NativeCommunityPage> BrowseAsync(string query, string sort, int page,
        CancellationToken cancellation)
        => Task.Run(async () =>
        {
            var api = new CommunityMapOperations(NetworkMapIdentity.ConfiguredDownloadSource());
            CommunityMapProjectPage result = await api.BrowseProjectsPageAsync(
                mine: false, favorites: false, sort: sort, query: query,
                page: page, pageSize: 8, cancellation: cancellation).ConfigureAwait(false);
            var items = result.Items
                .Where(p => p != null && p.MapId != Guid.Empty)
                .Select(p =>
                {
                    CommunityMap map = p.CurrentRevision?.Package ?? p.LatestRevision.Package;
                    return new NativeCommunityEntry(p.MapId, p.DisplayName ?? p.Name,
                        p.Author ?? "Unknown creator", map.Hash,
                        p.CurrentRevision?.RevisionNumber ?? p.LatestRevision.RevisionNumber,
                        string.Join(", ", map.SupportedModes), map.MinPlayers, map.MaxPlayers);
                }).ToArray();
            return new NativeCommunityPage(items, result.Total, result.Page, result.PageSize);
        }, cancellation);
}

//// Exact archive and transfer-queue pipeline shared with the current Community workspace.
internal sealed class NativeCommunityInstaller : INativeCommunityInstaller
{
    public async Task<NativeCommunityInstalled> InstallAsync(NativeCommunityEntry selected,
        CancellationToken cancellation)
    {
        if (!NativeCommunityInstallController.IsVerifiedSelection(selected) || !GameFiles.Ready)
            throw new InvalidOperationException("Invalid Community selection or game-file setup.");
        string endpoint = NetworkMapIdentity.ConfiguredDownloadSource();
        var api = new CommunityMapOperations(endpoint);
        CommunityMap map = await api.GetPackageAsync(selected.LatestHash, cancellation)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("Published Community revision is unavailable.");
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
        return new NativeCommunityInstalled(map.MapId, map.Hash, installed);
    }

    public void Register(NativeCommunityInstalled installed)
    {
        // The existing gameplay owner performs metadata registration on its
        // thread. Never publish a custom map while a match owns the scene.
        MapRuntimeUsage.RequireInstallationAllowed();
        Metadata.RegisterDownloadedMap(installed.Definition);
    }
}

internal sealed class NativeCommunityCreatorService : INativeCommunityCreatorAuthority
{
    private static CommunityMapOperations Operations() => new(
        NetworkMapIdentity.ConfiguredDownloadSource(),
        new DelegateCommunityCredentialSource(
            HunterLicenseClient.GetCommunityMapTicketAsync,
            HunterLicenseClient.RefreshCommunityMapTicketAsync));

    private static NativeCreatorEntry ToEntry(CommunityMapProject project)
        => new(project.MapId, project.DisplayName ?? project.Name,
            project.LatestHash, project.CurrentHash ?? "", project.RevisionCount,
            project.ArchivedAt != null, project.DeletedAt != null, project.CanManageLifecycle);

    public async Task<NativeCreatorPage> BrowseMineAsync(int page, CancellationToken token)
    {
        CommunityMapProjectPage data = await Operations().BrowseProjectsPageAsync(
            mine: true, favorites: false, sort: "updated", query: null,
            page: page, pageSize: 8, cancellation: token).ConfigureAwait(false);
        return new NativeCreatorPage(data.Items.Select(ToEntry).ToArray(),
            data.Total, data.Page);
    }

    public async Task<NativeCreatorEntry?> ReadOwnedAsync(Guid mapId, CancellationToken token)
    {
        CommunityMapProject? owned = await Operations().GetOwnedProjectAsync(mapId, token)
            .ConfigureAwait(false);
        return owned is null ? null : ToEntry(owned);
    }

    public async Task<bool> ExecuteAsync(NativeCreatorAction action, Guid mapId,
        CancellationToken token)
    {
        CommunityMapProject? outcome = action switch
        {
            NativeCreatorAction.Archive => await Operations().ArchiveProjectAsync(mapId, token)
                .ConfigureAwait(false),
            NativeCreatorAction.Restore => await Operations().RestoreProjectAsync(mapId, token)
                .ConfigureAwait(false),
            _ => null
        };
        return outcome != null && outcome.MapId == mapId;
    }
}
#endif
