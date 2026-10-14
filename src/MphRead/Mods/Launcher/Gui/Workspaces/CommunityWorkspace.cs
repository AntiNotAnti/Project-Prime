#if MPHREAD_AVALONIA
using System;
using System.IO;
using System.Net.Http;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Player-facing Community browser. Independent of the Map Editor and safe to
/// construct on Android. Map publishing remains in Studio until its dedicated
/// creator workspace is built; the map/lobby protocol is unchanged.
/// </summary>
internal sealed class CommunityWorkspace : UserControl, IPrimeWorkspace, IDisposable
{
    private readonly PrimeOverlayHost _overlays;
    private readonly CommunityMapProject[]? _fixture;
    private readonly UiTabs _tabs = new(new[] { "DISCOVER", "INSTALLED", "FAVORITES", "DOWNLOADS" });
    private readonly UiList _list = new() { AutoSelectFirst = false, SpacingEms = 0.25 };
    private readonly StackPanel _details = new() { Spacing = 8 };
    private readonly TextBox _search = new() { PlaceholderText = "Search maps or creators" };
    private readonly PrimeStatePanel _state = new(PrimeStateKind.Loading, "CONTACTING COMMUNITY",
        "Fetching the published map catalog.");
    private readonly TextBlock _status = PrimeChrome.Text("Community ready.", PrimeTypography.BodySmall,
        PrimeTheme.TextSecondaryBrush);
    private readonly PrimeButton _refresh;
    private readonly PrimeButton _install;
    private readonly PrimeButton _favorite;
    private readonly PrimeButton _report;
    private readonly PrimeButton _cancel;
    private CommunityMapProject[] _projects = Array.Empty<CommunityMapProject>();
    private CommunityMapProject? _selected;
    private CancellationTokenSource? _load;
    private CancellationTokenSource? _transfer;
    private bool _active;
    private bool _disposed;

    internal CommunityWorkspace(PrimeOverlayHost overlays,
        CommunityMapProject[]? fixture = null)
    {
        _overlays = overlays;
        _fixture = fixture;
        _refresh = new PrimeButton("REFRESH", () => _ = RefreshAsync(), compact: true);
        _install = new PrimeButton("INSTALL MAP", () => _ = InstallAsync(), primary: true, compact: true);
        _favorite = new PrimeButton("FAVORITE", () => _ = FavoriteAsync(), compact: true);
        _report = new PrimeButton("REPORT", Report, compact: true);
        _cancel = new PrimeButton("CANCEL DOWNLOAD", () => _transfer?.Cancel(), compact: true)
        {
            IsVisible = false
        };
        ControllerNav.Identify(_refresh, "community.refresh");
        ControllerNav.Identify(_install, "community.install");
        ControllerNav.Identify(_favorite, "community.favorite");
        ControllerNav.Identify(_report, "community.report");
        ControllerNav.Identify(_cancel, "community.cancel");

        var root = new Grid
        {
            Margin = PrimeMetrics.PageMargin,
            RowDefinitions = new("Auto,Auto,*,Auto"),
            RowSpacing = 12
        };
        var heading = PrimeChrome.Stack(
            PrimeChrome.Eyebrow("COMMUNITY // PLAYER LIBRARY"),
            PrimeChrome.Title("COMMUNITY HUB"),
            PrimeChrome.Text("Discover, install, and manage community arenas without opening Map Studio.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        root.Children.Add(PrimeChrome.Columns("*,Auto", heading, _refresh));

        var navigation = PrimeChrome.Columns("Auto,*", _tabs, _search);
        Grid.SetRow(navigation, 1);
        root.Children.Add(navigation);

        var listStage = new Grid();
        listStage.Children.Add(_list);
        listStage.Children.Add(_state);
        var listPanel = new PrimePanel(listStage, raised: true) { Padding = new Thickness(10) };
        var detailView = new ScrollViewer
        {
            Content = _details,
            HorizontalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var detailsPanel = new PrimePanel(detailView, raised: true)
        {
            Padding = new Thickness(18, 14)
        };
        var body = PrimeChrome.Columns("1.3*,1*", listPanel, detailsPanel);
        Grid.SetRow(body, 2);
        root.Children.Add(body);

        var footer = PrimeChrome.Columns("*,Auto", _status, _cancel);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;

        _tabs.Changed += (_, _) =>
        {
            _selected = null;
            _search.Text = "";
            if (_active) _ = RefreshAsync();
            else RenderList();
        };
        _search.TextChanged += (_, _) => RenderList();
        if (_fixture != null) _projects = _fixture;
        RenderList();
    }

    private static CommunityMapOperations Operations()
        => new(NetworkMapIdentity.ConfiguredDownloadSource(),
            new DelegateCommunityCredentialSource(
                HunterLicenseClient.GetCommunityMapTicketAsync,
                HunterLicenseClient.RefreshCommunityMapTicketAsync));

    public void OnActivated()
    {
        if (_disposed) return;
        _active = true;
        if (_fixture == null) _ = RefreshAsync();
        else RenderList();
    }

    public void OnDeactivated()
    {
        _active = false;
        _load?.Cancel();
        _transfer?.Cancel(); // No detached install without PR-03's durable worker.
    }

    public void Refresh()
    {
        if (_active && _transfer == null) RenderList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        OnDeactivated();
        Content = null;
    }

    private async Task RefreshAsync()
    {
        if (!_active || _disposed) return;
        _load?.Cancel();
        if (_tabs.Index is 1 or 3)
        {
            // Installed archives and foreground transfers work offline.
            _load = null;
            _refresh.IsEnabled = true;
            RenderList();
            return;
        }
        if (_fixture != null)
        {
            _load = null;
            _projects = _fixture;
            _refresh.IsEnabled = true;
            RenderList();
            return;
        }
        using var cancel = new CancellationTokenSource();
        _load = cancel;
        _refresh.IsEnabled = false;

        _state.Set(PrimeStateKind.Loading, "LOADING COMMUNITY",
            "Contacting the configured Community catalog.", visible: true);
        try
        {
            CommunityMapProject[] result = await Operations().BrowseProjectsAsync(
                mine: false, favorites: _tabs.Index == 2, sort: "updated",
                cancellation: cancel.Token);
            if (cancel.IsCancellationRequested || !_active || _disposed) return;
            _projects = result;
            _status.Text = $"{result.Length} Community maps listed.";
            RenderList();
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or HttpRequestException or ArgumentException
            or InvalidDataException or NotSupportedException)
        {
            if (!_active || cancel.IsCancellationRequested) return;
            _projects = Array.Empty<CommunityMapProject>();
            _state.Set(PrimeStateKind.Error, "COMMUNITY UNAVAILABLE",
                ex.Message + " Installed maps remain available offline.");
            _list.Clear();
            _status.Text = "Community catalog unavailable. Select Refresh to retry.";
        }
        finally
        {
            if (ReferenceEquals(_load, cancel))
            {
                _load = null;
                _refresh.IsEnabled = true;
            }
        }
    }

    private bool Matches(string title, string author)
    {
        string needle = _search.Text?.Trim() ?? "";
        return needle.Length == 0 || (title + " " + author)
            .Contains(needle, StringComparison.OrdinalIgnoreCase);
    }

    private void RenderList()
    {
        if (_disposed) return;
        _list.Clear();
        _list.IsVisible = _tabs.Index != 3;
        _state.IsVisible = false;

        if (_tabs.Index == 3)
        {
            _state.Set(PrimeStateKind.Neutral, "DOWNLOAD ACTIVITY",
                _transfer == null
                    ? "No active download. Persistent transfer history, pause and resume arrive with the transfer-manager phase."
                    : "An exact map is being downloaded, validated, built and installed.");
            ShowEmptyDetails("DOWNLOADS",
                "Active transfer progress appears below. Interrupted downloads are not yet persistent across restarts.");
            return;
        }

        int rows = 0;
        if (_tabs.Index == 1)
        {
            // The local installed index is authoritative. No network fetch and
            // no assumption that a map remains publicly listed.
            foreach (MapDefinition map in CustomRooms.Definitions)
            {
                if (map.BundlePath == null
                    || !CustomRooms.Installed.TryGet(map.MapId, out InstalledMapIdentity installed)
                    || !Matches(map.InGameName ?? map.Name, map.Author ?? "")) continue;
                MapDefinition captured = map;
                InstalledMapIdentity exact = installed;
                var row = new UiListRow(map.InGameName ?? map.Name,
                    $"{map.Author ?? "Local author"} · installed · {map.Version ?? "1"}", spacious: true)
                { Choice = map.MapId };
                _list.Add(row, _ => ShowLocal(captured, exact));
                rows++;
            }
        }
        else
        {
            foreach (CommunityMapProject project in _projects)
            {
                CommunityMap map = project.CurrentRevision?.Package ?? project.LatestRevision.Package;
                if (!Matches(project.DisplayName ?? project.Name, project.Author ?? "")) continue;
                CommunityMapProject captured = project;
                var row = new UiListRow(project.DisplayName ?? project.Name,
                    $"{project.Author ?? "Unknown author"} · {map.MinPlayers}–{map.MaxPlayers} players · ★ {map.FavoriteCount}",
                    spacious: true) { Choice = project.MapId };
                _list.Add(row, _ => ShowProject(captured));
                rows++;
            }
        }
        if (rows == 0)
        {
            _state.Set(PrimeStateKind.Empty,
                _tabs.Index == 1 ? "NO INSTALLED MAPS" :
                _tabs.Index == 2 ? "NO FAVORITES" : "NO COMMUNITY MAPS",
                _tabs.Index == 1 ? "Install a map from Discover to play it." :
                _tabs.Index == 2 ? "Favorite a map in Discover to see it here." :
                "No published maps match the current search or catalog.",
                visible: true);
            ShowEmptyDetails("NO MAP SELECTED",
                "Select a Community map to inspect compatibility and available actions.");
            return;
        }
        _state.IsVisible = false;
        CommunityMapProject? keep = _selected;
        if (keep != null && _tabs.Index != 1 && _projects.Any(p => p.MapId == keep.MapId))
        {
            _list.SelectTag(keep.MapId);
            ShowProject(keep);
        }
        else
        {
            ShowEmptyDetails("SELECT A MAP",
                "Choose a map to view its author, supported modes, installed revision and download actions.");
        }
    }

    private void ShowEmptyDetails(string title, string description)
    {
        _details.Children.Clear();
        _details.Children.Add(PrimeChrome.Eyebrow("COMMUNITY // DETAILS"));
        _details.Children.Add(PrimeChrome.Title(title));
        _details.Children.Add(PrimeChrome.Text(description,
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
    }

    private void ShowLocal(MapDefinition map, InstalledMapIdentity installed)
    {
        _selected = null;
        ShowEmptyDetails(map.InGameName ?? map.Name,
            $"Local installed archive · {map.Author ?? "Unknown author"} · {map.Version ?? "1"}");
        _details.Children.Add(new PrimeBadge("READY IN MAP PICKER"));
        _details.Children.Add(PrimeChrome.Text(
            $"Exact package {installed.Identity.PackageHash.ToString()[..12]}…",
            PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush));
        _details.Children.Add(PrimeChrome.Text(
            "Choose this arena in the existing Create Lobby map rotation or offline map picker.",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
    }

    private void ShowProject(CommunityMapProject project)
    {
        _selected = project;
        CommunityMap map = project.CurrentRevision?.Package ?? project.LatestRevision.Package;
        bool installed = CustomRooms.Installed.TryGet(project.MapId, out InstalledMapIdentity local);
        bool exact = installed && local!.Identity.PackageHash.ToString() == map.Hash;
        _details.Children.Clear();
        _details.Children.Add(PrimeChrome.Eyebrow("COMMUNITY // MAP DETAILS"));
        _details.Children.Add(PrimeChrome.Title(project.DisplayName ?? project.Name));
        _details.Children.Add(PrimeChrome.Text(
            $"By {project.Author ?? "Unknown author"} · {map.Bytes / 1048576.0:0.0} MiB · revision {project.CurrentRevision?.RevisionNumber ?? project.LatestRevision.RevisionNumber}",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        _details.Children.Add(new PrimeBadge(exact ? "EXACT REVISION INSTALLED"
            : installed ? "DIFFERENT REVISION INSTALLED" : "NOT INSTALLED"));
        _details.Children.Add(PrimeChrome.Text(
            $"Players: {map.MinPlayers}–{map.MaxPlayers} · Modes: {string.Join(", ", map.SupportedModes)}",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        _details.Children.Add(PrimeChrome.Text(
            $"Requires protocol {map.MinimumProtocol} · Current protocol {NetConfig.ProtocolVersion}",
            PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush));
        _install.Label = exact ? "INSTALLED" : installed ? "INSTALL THIS REVISION" : "DOWNLOAD & INSTALL";
        _install.IsEnabled = !exact && map.MinimumProtocol <= NetConfig.ProtocolVersion && _transfer == null;
        _favorite.Label = map.Favorited ? "REMOVE FAVORITE" : "FAVORITE";
        _details.Children.Add(_install);
        _details.Children.Add(_favorite);
        _details.Children.Add(_report);
        _details.Children.Add(PrimeChrome.Text(
            "After installing, select the arena in Create Lobby → Map Rotation. Publishing remains available from Map Studio.",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
    }

    private async Task InstallAsync()
    {
        CommunityMapProject? project = _selected;
        if (project == null || _transfer != null || _disposed) return;
        CommunityMap map = project.CurrentRevision?.Package ?? project.LatestRevision.Package;
        using var cancel = new CancellationTokenSource();
        _transfer = cancel;
        _cancel.IsVisible = true;
        _install.IsEnabled = false;
        _status.Text = "Preparing exact map download…";
        try
        {
            if (!GameFiles.Ready)
                throw new IOException("Set up game files before installing Community maps.");
            GameFiles.ApplyPaths();
            MapRuntimeUsage.RequireInstallationAllowed();
            MapDefinition installed = await Operations().InstallAsync(map,
                CustomRooms.UserMapDirectory, cancel.Token,
                (received, total) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (_disposed || cancel.IsCancellationRequested) return;
                    _status.Text = $"Downloading {received / 1048576.0:0.0}/{total / 1048576.0:0.0} MiB";
                }));
            if (cancel.IsCancellationRequested || _disposed) return;
            Metadata.RegisterDownloadedMap(installed);
            _status.Text = $"{project.DisplayName ?? project.Name} installed and registered.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            _status.Text = "Download cancelled. No incomplete map was registered.";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException
            or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            _status.Text = "Map installation failed: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_transfer, cancel)) _transfer = null;
            _cancel.IsVisible = false;
            if (_active) RenderList();
        }
    }

    private async Task FavoriteAsync()
    {
        CommunityMapProject? project = _selected;
        if (project == null) return;
        CommunityMap map = project.CurrentRevision?.Package ?? project.LatestRevision.Package;
        using var cancel = new CancellationTokenSource();
        try
        {
            await Operations().UseClientAsync(true, async client =>
            {
                await client.SetFavoriteAsync(project.MapId, !map.Favorited, cancel.Token);
                return true;
            }, cancel.Token);
            if (!_active || _disposed) return;
            _status.Text = map.Favorited ? "Favorite removed." : "Map favorited.";
            await RefreshAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
        {
            if (_active) _status.Text = "Could not update favorite: " + ex.Message;
        }
    }

    private void Report()
    {
        CommunityMapProject? project = _selected;
        if (project == null) return;
        CommunityMap map = project.CurrentRevision?.Package ?? project.LatestRevision.Package;
        var reasons = new ComboBox
        {
            ItemsSource = MapCreatorCatalog.ReportReasons,
            SelectedIndex = 0,
            MinWidth = 230
        };
        var explanation = new TextBox
        {
            PlaceholderText = "Describe the issue (optional)",
            MinWidth = 260
        };
        async Task Submit()
        {
            _overlays.Close();
            try
            {
                await Operations().UseClientAsync(true, async client =>
                {
                    await client.ReportAsync(project.MapId, new MapReportRequest(
                        map.Version, MapCreatorCatalog.ReportReasons[Math.Max(0, reasons.SelectedIndex)],
                        explanation.Text ?? ""), CancellationToken.None);
                    return true;
                }, CancellationToken.None);
                if (_active) _status.Text = "Report submitted for moderator review.";
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
            {
                if (_active) _status.Text = "Could not submit report: " + ex.Message;
            }
        }
        _overlays.Show(new PrimePanel(PrimeChrome.Stack(
            PrimeChrome.Title("REPORT COMMUNITY MAP"),
            PrimeChrome.Text(project.DisplayName ?? project.Name),
            reasons, explanation,
            new PrimeButton("SUBMIT REPORT", () => _ = Submit(), primary: true),
            new PrimeButton("CANCEL", _overlays.Close))), PrimeModalSize.Medium);
    }
}
#endif
