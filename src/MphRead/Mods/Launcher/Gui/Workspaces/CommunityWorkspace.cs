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
    private readonly CommunityTransferManager? _transfers;
    private Guid? _activeJobId;
    private readonly UiTabs _tabs = new(new[] { "DISCOVER", "INSTALLED", "FAVORITES", "DOWNLOADS" });
    private readonly UiList _list = new() { AutoSelectFirst = true, SpacingEms = 0.25 };
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
        // Screenshot/fixture mode never touches a user's transfer directory.
        _transfers = fixture == null ? CommunityTransferSession.Shared : null;
        if (_transfers != null) _transfers.Changed += QueueChanged;
        _refresh = new PrimeButton("REFRESH", () => _ = RefreshAsync(), compact: true);
        _install = new PrimeButton("INSTALL MAP", () => _ = InstallAsync(), primary: true, compact: true);
        _favorite = new PrimeButton("FAVORITE", () => _ = FavoriteAsync(), compact: true);
        _report = new PrimeButton("REPORT", Report, compact: true);
        _cancel = new PrimeButton("PAUSE DOWNLOAD", PauseCurrent, compact: true)
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
        if (_fixture == null)
        {
            _transfers?.StartPending(); // User-paused transfers stay paused.
            _ = RefreshAsync();
        }
        else RenderList();
    }

    public void OnDeactivated()
    {
        _active = false;
        _load?.Cancel();
        // The worker retains its verified partial bytes. Only the foreground
        // install handoff is cancelled when leaving this workspace.
        _transfer?.Cancel();
    }

    public void Refresh()
    {
        if (_active && _transfer == null) RenderList();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_transfers != null) _transfers.Changed -= QueueChanged;
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
        Guid? previousSelection = _selected?.MapId;
        _list.Clear();
        _list.IsVisible = true;
        _state.IsVisible = false;

        if (_tabs.Index == 3)
        {
            CommunityTransferJob[] receipts = _transfers?.ListJobs().ToArray()
                ?? Array.Empty<CommunityTransferJob>();
            if (receipts.Length == 0)
            {
                _state.Set(PrimeStateKind.Empty, "NO DOWNLOAD HISTORY",
                    "Downloads and creator uploads will appear here and survive a restart.");
                ShowEmptyDetails("TRANSFERS",
                    "No transfer jobs have been queued on this device.");
                return;
            }
            foreach (CommunityTransferJob receipt in receipts)
            {
                CommunityTransferJob selected = receipt;
                string caption = receipt.Download?.DisplayName
                    ?? receipt.Download?.Name ?? (receipt.Kind == CommunityTransferKind.Upload
                        ? "Creator upload" : "Community map");
                var row = new UiListRow(caption,
                    $"{receipt.Kind} · {receipt.State} · {receipt.AcknowledgedBytes / 1048576d:0.0}/{receipt.TotalBytes / 1048576d:0.0} MiB",
                    spacious: true) { Choice = receipt.Id };
                _list.Add(row, _ => ShowTransfer(selected));
            }
            _state.IsVisible = false;
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
        if (previousSelection is Guid remembered && _tabs.Index != 1
            && _projects.FirstOrDefault(p => p.MapId == remembered) is { } keep)
        {
            _list.SelectTag(remembered);
            ShowProject(keep);
        }
        // UiList.AutoSelectFirst already populates the details for the first
        // row, including an offline local map, without a second selection.

    }

    private void QueueChanged(CommunityTransferJob job)
    {
        // Transfer workers never own Avalonia controls.
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !_active) return;
            if (_activeJobId == job.Id)
            {
                _status.Text = $"{job.State} · {job.AcknowledgedBytes / 1048576d:0.0}/{job.TotalBytes / 1048576d:0.0} MiB";
                _cancel.IsVisible = job.State is CommunityTransferState.Downloading
                    or CommunityTransferState.Queued or CommunityTransferState.Retryable;
            }
            if (_tabs.Index == 3) RenderList();
        });
    }

    private void PauseCurrent()
    {
        if (_activeJobId is not Guid id || _transfers == null) return;
        _transfers.Pause(id);
        _transfer?.Cancel();
        _status.Text = "Download paused. Resume it from the Downloads tab.";
        _cancel.IsVisible = false;
    }

    private void ShowTransfer(CommunityTransferJob job)
    {
        _details.Children.Clear();
        _details.Children.Add(PrimeChrome.Eyebrow("COMMUNITY // TRANSFER HISTORY"));
        _details.Children.Add(PrimeChrome.Title(
            job.Download?.DisplayName ?? job.Download?.Name ??
            (job.Kind == CommunityTransferKind.Upload ? "CREATOR UPLOAD" : "MAP DOWNLOAD")));
        _details.Children.Add(new PrimeBadge(job.State.ToString().ToUpperInvariant()));
        _details.Children.Add(PrimeChrome.Text(
            $"Acknowledged {job.AcknowledgedBytes / 1048576d:0.0} / {job.TotalBytes / 1048576d:0.0} MiB",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        if (!string.IsNullOrWhiteSpace(job.Error))
            _details.Children.Add(PrimeChrome.Text(job.Error,
                PrimeTypography.BodySmall, PrimeTheme.WarningBrush));
        if (job.State is CommunityTransferState.Downloading
            or CommunityTransferState.Uploading or CommunityTransferState.Queued
            or CommunityTransferState.Preparing or CommunityTransferState.Retryable)
        {
            var pause = new PrimeButton("PAUSE", () => _transfers?.Pause(job.Id),
                compact: true);
            ControllerNav.Identify(pause, "community.transfer.pause");
            _details.Children.Add(pause);
        }
        if (job.State is CommunityTransferState.Paused
            or CommunityTransferState.Failed or CommunityTransferState.Retryable)
        {
            var resume = new PrimeButton("RESUME", () => _transfers?.Resume(job.Id),
                compact: true, primary: true);
            ControllerNav.Identify(resume, "community.transfer.resume");
            _details.Children.Add(resume);
        }
        if (job.State is not (CommunityTransferState.Completed or CommunityTransferState.Cancelled))
        {
            var cancel = new PrimeButton("CANCEL JOB", () => _transfers?.Cancel(job.Id),
                compact: true, danger: true);
            ControllerNav.Identify(cancel, "community.transfer.cancel");
            _details.Children.Add(cancel);
        }
        if (job.State == CommunityTransferState.Completed
            && job.Kind == CommunityTransferKind.Download)
            _details.Children.Add(PrimeChrome.Text(
                "Verified archive saved. Select the map from Discover to install it.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        if (job.State is CommunityTransferState.Completed
            or CommunityTransferState.Cancelled or CommunityTransferState.Failed
            or CommunityTransferState.Paused or CommunityTransferState.NeedsReview)
        {
            var forget = new PrimeButton("REMOVE FROM HISTORY", () =>
            {
                try { _transfers?.Forget(job.Id); RenderList(); }
                catch (InvalidOperationException)
                {
                    _status.Text = "This transfer is still finishing. Retry after it stops.";
                }
            }, compact: true);
            ControllerNav.Identify(forget, "community.transfer.forget");
            _details.Children.Add(forget);
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
        if (project == null || _transfer != null || _disposed || _transfers == null)
            return;
        CommunityMap map = project.CurrentRevision?.Package ?? project.LatestRevision.Package;
        using var cancel = new CancellationTokenSource();
        _transfer = cancel;
        _install.IsEnabled = false;
        _status.Text = "Checking verified Community archives…";
        try
        {
            if (!GameFiles.Ready)
                throw new IOException("Set up game files before installing Community maps.");
            GameFiles.ApplyPaths();
            MapRuntimeUsage.RequireInstallationAllowed();

            // The queue owns download bytes and persists its partials. Leaving
            // the page cancels only this foreground install handoff, never the
            // durable content job. No worker can report network map Ready.
            // Strict cache probes may hash hundreds of MiB. Keep this work
            // off the UI owner thread even when an exact receipt already exists.
            Guid id = await Task.Run(() => _transfers.EnqueueDownload(
                NetworkMapIdentity.ConfiguredDownloadSource(), map), cancel.Token);
            _activeJobId = id;
            _cancel.IsVisible = true;
            CommunityTransferJob result = await _transfers.WaitAsync(id);
            if (result.State != CommunityTransferState.Completed)
            {
                if (!cancel.IsCancellationRequested && !_disposed)
                    _status.Text = result.Error ?? $"Download {result.State}. Resume from Downloads.";
                return;
            }

            // The player may have entered a game while downloading. Do not
            // publish a map during active scene ownership; retain verified bytes
            // and offer installation on their next visit.
            if (cancel.IsCancellationRequested || !_active || _disposed)
            {
                if (!_disposed)
                    _status.Text = "Verified archive saved. Reopen Community to install.";
                return;
            }

            // The read lease protects the immutable cache entry from another
            // process's LRU cleanup throughout the private map install.
            using var archive = await Task.Run(() => _transfers.LeaseVerifiedArchive(id),
                cancel.Token)
                ?? throw new IOException("Verified Community archive is missing or changed.");
            MapDefinition installed = await Task.Run(() =>
                MapPackageInstaller.InstallLeased(archive, map.MapId, map.ContentHash,
                    map.Hash, CustomRooms.UserMapDirectory), cancel.Token);

            // Commit may finish just before navigation changes. Registration
            // must follow a completed installation to avoid stale map indices.
            Metadata.RegisterDownloadedMap(installed);
            if (!_disposed)
                _status.Text = $"{project.DisplayName ?? project.Name} installed and registered.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            if (!_disposed) _status.Text = "Install handoff cancelled. Download progress was retained.";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException
            or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            if (!_disposed) _status.Text = "Map installation failed: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_transfer, cancel)) _transfer = null;
            _activeJobId = null;
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
