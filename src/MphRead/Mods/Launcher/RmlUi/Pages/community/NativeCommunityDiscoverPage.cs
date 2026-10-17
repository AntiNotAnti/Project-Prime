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
    private RmlUiDocumentToken _page;
    private long _shownRevision, _shownInstallRevision, _presentationRevision;
    private bool _disposed;

    public bool OpenFullCommunityRequested { get; private set; }

    public NativeCommunityDiscoverPage(RmlUiHost host, RmlUiPageManager pages,
        INativeCommunityReader reader, INativeCommunityInstaller installer)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeCommunityDiscoverController(reader);
        _install = new NativeCommunityInstallController(installer);
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
            case RmlUiIntentKind.CommunityRefresh:
                _controller.Refresh();
                break;
            case RmlUiIntentKind.CommunitySearch:
                string query = _host.ReadField(_page, "native_community_search", 256);
                _controller.Search(query);
                break;
            case RmlUiIntentKind.CommunitySort:
                _controller.Sort(intent.Argument);
                break;
            case RmlUiIntentKind.CommunityPage:
                _controller.Page(intent.Argument);
                break;
            case RmlUiIntentKind.CommunitySelect:
                _controller.Select(intent.Argument);
                break;
            case RmlUiIntentKind.CommunityDetailAction:
                if (intent.Argument != 0 || _controller.Current().Busy
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
        if (snapshot.Revision == _shownRevision
            && install.Revision == _shownInstallRevision) return;
        bool installedSelected = snapshot.SelectedEntry is { } current
            && install.CompletedMapId == current.MapId
            && install.CompletedHash.Equals(current.LatestHash,
                StringComparison.OrdinalIgnoreCase);
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["native_community_status"] = RmlUiBindingValue.FromText(
                install.Busy || installedSelected || (install.Status.Length > 0
                    && install.CompletedMapId == null)
                    ? install.Status : snapshot.Status),
            ["disabled:native_community_install"] = RmlUiBindingValue.FromBoolean(
                install.Busy || snapshot.Busy || snapshot.SelectedEntry == null
                || !GameFiles.Ready),
            ["visible:native_community_cancel_install"] =
                RmlUiBindingValue.FromBoolean(install.Busy),
            ["disabled:native_community_cancel_install"] =
                RmlUiBindingValue.FromBoolean(!install.Busy),
            ["native_community_title"] = RmlUiBindingValue.FromText(
                snapshot.SelectedEntry?.Name ?? "SELECT A COMMUNITY MAP"),
            ["native_community_description"] = RmlUiBindingValue.FromText(
                snapshot.SelectedEntry is { } item
                    ? $"{item.Creator} // Revision {item.Revision} // {item.MinPlayers}–{item.MaxPlayers} players // {item.Modes}"
                    : "Select a map to inspect its published immutable package."),
            ["native_community_hash"] = RmlUiBindingValue.FromText(
                snapshot.SelectedEntry is { } selected ? selected.LatestHash : ""),
            ["disabled:native_community_previous"] =
                RmlUiBindingValue.FromBoolean(snapshot.Busy || !snapshot.CanPrevious),
            ["disabled:native_community_next"] =
                RmlUiBindingValue.FromBoolean(snapshot.Busy || !snapshot.CanNext)
        };
        for (int i = 0; i < 8; i++)
        {
            bool visible = i < snapshot.Rows.Count;
            NativeCommunityEntry row = visible ? snapshot.Rows[i] : default;
            data["visible:native_community_row" + i] = RmlUiBindingValue.FromBoolean(visible);
            data["class:native_community_row" + i + ":selected"] =
                RmlUiBindingValue.FromBoolean(visible && i == snapshot.Selected);
            data["native_community_name" + i] = RmlUiBindingValue.FromText(visible ? row.Name : "");
            data["native_community_meta" + i] = RmlUiBindingValue.FromText(visible
                ? $"{row.Creator} // REV {row.Revision} // {row.MinPlayers}–{row.MaxPlayers} PLAYERS" : "");
        }
        _pages.Present(_page, ++_presentationRevision, data);
        _shownRevision = snapshot.Revision;
        _shownInstallRevision = install.Revision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
        _install.Dispose();
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
#endif
