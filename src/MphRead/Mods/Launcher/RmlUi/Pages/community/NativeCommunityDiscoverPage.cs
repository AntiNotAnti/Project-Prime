#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.RmlUi.Pages.Community;

/// <summary>Read-only native Community map catalog. All install/creator actions use legacy.</summary>
internal sealed class NativeCommunityDiscoverPage : IDisposable
{
    private readonly RmlUiHost _host;
    private readonly RmlUiPageManager _pages;
    private readonly NativeCommunityDiscoverController _controller;
    private RmlUiDocumentToken _page;
    private long _shownRevision;
    private bool _disposed;

    public bool OpenFullCommunityRequested { get; private set; }

    public NativeCommunityDiscoverPage(RmlUiHost host, RmlUiPageManager pages,
        INativeCommunityReader reader)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _pages = pages ?? throw new ArgumentNullException(nameof(pages));
        _controller = new NativeCommunityDiscoverController(reader);
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
        if (snapshot.Revision == _shownRevision) return;
        var data = new Dictionary<string, RmlUiBindingValue>(StringComparer.Ordinal)
        {
            ["native_community_status"] = RmlUiBindingValue.FromText(snapshot.Status),
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
        _pages.Present(_page, snapshot.Revision, data);
        _shownRevision = snapshot.Revision;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _controller.Dispose();
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
#endif
