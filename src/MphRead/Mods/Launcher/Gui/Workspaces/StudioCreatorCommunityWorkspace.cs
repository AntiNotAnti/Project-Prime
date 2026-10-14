#if MPHREAD_AVALONIA
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// PR-14 opt-in standalone creator Community surface. The editor provides only
/// a frozen publication intent; uploads, catalog reads and transfer receipts
/// belong to shared Community services, not MapDocument or MapStudioScreen.
/// Advanced lifecycle management remains reachable via the legacy fallback
/// until the deferred cross-platform GUI acceptance is complete.
/// </summary>
internal sealed class StudioCreatorCommunityWorkspace : UserControl, IDisposable
{
    private readonly CommunityMapOperations _operations;
    private readonly CommunityTransferManager _queue;
    private readonly CommunityStudioPublishIntent? _intent;
    private readonly string _endpoint;
    private readonly Action _close;
    private readonly Action _advanced;
    private readonly UiTabs _tabs = new(new[] { "MY UPLOADS", "TRANSFERS", "PUBLISH" });
    private readonly UiList _list = new() { AutoSelectFirst = true, SpacingEms = 0.25 };
    private readonly StackPanel _details = new() { Spacing = 8 };
    private readonly TextBlock _status = PrimeChrome.Text(
        "Community creator workspace.", PrimeTypography.BodySmall,
        PrimeTheme.TextSecondaryBrush);
    private readonly TextBox _notes = new()
    {
        AcceptsReturn = true, Height = 72, MaxLength = 2000,
        PlaceholderText = "Release notes (optional)"
    };
    private CommunityMapProject[] _projects = Array.Empty<CommunityMapProject>();
    private CancellationTokenSource? _operation;
    private bool _disposed;

    internal StudioCreatorCommunityWorkspace(
        string endpoint, CommunityStudioPublishIntent? intent,
        Action close, Action advanced,
        CommunityTransferManager? queue = null,
        CommunityMapOperations? operations = null)
    {
        _endpoint = endpoint;
        _intent = intent;
        _close = close;
        _advanced = advanced;
        _queue = queue ?? CommunityTransferSession.Shared;
        _operations = operations ?? new CommunityMapOperations(endpoint,
            new DelegateCommunityCredentialSource(
                HunterLicenseClient.GetCommunityMapTicketAsync,
                HunterLicenseClient.RefreshCommunityMapTicketAsync));
        _queue.Changed += OnJobChanged;
        _queue.StartPending(); // Retry interrupted jobs, never user-paused jobs.
        DetachedFromVisualTree += (_, _) => Dispose();

        var root = new Grid
        {
            MinWidth = 730, MinHeight = 490, MaxWidth = 1000,
            RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 10
        };
        var heading = PrimeChrome.Stack(
            PrimeChrome.Eyebrow("STUDIO // COMMUNITY CREATOR HUB"),
            PrimeChrome.Title("COMMUNITY"),
            PrimeChrome.Text(
                "Manage immutable map revisions and queued uploads independently of the editor. "
                + "This creator workspace is a preview; historical manager remains available.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        var refresh = new PrimeButton("REFRESH", () => _ = RefreshAsync(), compact: true);
        ControllerNav.Identify(refresh, "studio.creator.refresh");
        root.Children.Add(PrimeChrome.Columns("*,Auto", heading, refresh));

        Grid.SetRow(_tabs, 1);
        root.Children.Add(_tabs);
        var listScroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var detailsScroll = new ScrollViewer
        {
            Content = _details,
            VerticalScrollBarVisibility =
                Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        var content = PrimeChrome.Columns("1.05*,1*", new PrimePanel(listScroll)
        {
            Padding = new Thickness(8)
        }, new PrimePanel(detailsScroll) { Padding = new Thickness(12) });
        Grid.SetRow(content, 2);
        root.Children.Add(content);

        var advanced = new PrimeButton("ADVANCED REVISIONS",
            () => { Dispose(); _advanced(); }, compact: true);
        var exit = new PrimeButton("CLOSE",
            () => { Dispose(); _close(); }, compact: true);
        ControllerNav.Identify(advanced, "studio.creator.advanced");
        ControllerNav.Identify(exit, "studio.creator.close");
        var footer = PrimeChrome.Columns("*,Auto,Auto", _status, advanced, exit);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        Content = root;

        _tabs.Changed += (_, _) => _ = RefreshAsync();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        _operation?.Cancel();
        _operation?.Dispose();
        var cancel = new CancellationTokenSource();
        _operation = cancel;
        _list.Clear();
        _details.Children.Clear();
        if (_tabs.Index == 1)
        {
            ShowTransfers();
            return;
        }
        if (_tabs.Index == 2)
        {
            ShowPublish();
            return;
        }

        _status.Text = "Loading creator-owned Community revisions…";
        try
        {
            var maps = await _operations.BrowseProjectsAsync(
                mine: true, favorites: false, sort: "updated",
                cancellation: cancel.Token);
            if (cancel.IsCancellationRequested || _disposed) return;
            _projects = maps;
            if (maps.Length == 0)
            {
                _details.Children.Add(PrimeChrome.Title("NO CREATOR MAPS"));
                _details.Children.Add(PrimeChrome.Text(
                    "Use Publish to queue a frozen copy of your current project. "
                    + "Installed and historical maps are not modified.",
                    PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            }
            foreach (CommunityMapProject project in maps)
            {
                CommunityMapProject selected = project;
                var row = new UiListRow(project.DisplayName ?? project.Name,
                    $"Latest r{project.LatestRevision.RevisionNumber} · "
                    + (project.ArchivedAt != null ? "ARCHIVED" :
                        project.DeletedAt != null ? "DELETED" : "ACTIVE"),
                    spacious: true)
                { Choice = project.MapId };
                _list.Add(row, _ => ShowProject(selected));
            }
            _status.Text = $"{maps.Length} creator-owned map(s). "
                + "Updates are immutable revisions.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or HttpRequestException
            or ArgumentException or InvalidDataException or NotSupportedException)
        {
            if (!cancel.IsCancellationRequested && !_disposed)
            {
                _status.Text = "Creator catalog unavailable. Transfers remain accessible offline.";
                _details.Children.Add(PrimeChrome.Text(ex.Message,
                    PrimeTypography.BodySmall, PrimeTheme.WarningBrush));
            }
        }
    }

    private void ShowProject(CommunityMapProject project)
    {
        _details.Children.Clear();
        _details.Children.Add(PrimeChrome.Eyebrow("CREATOR // IMMUTABLE REVISIONS"));
        _details.Children.Add(PrimeChrome.Title(project.DisplayName ?? project.Name));
        _details.Children.Add(PrimeChrome.Text(
            $"MapId {project.MapId} · {project.RevisionCount} revision(s)",
            PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush));
        _details.Children.Add(PrimeChrome.Text(
            $"Latest exact package: {project.LatestHash}",
            PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush));
        _details.Children.Add(PrimeChrome.Text(
            "Publishing a new revision never rewrites a previous map package. "
            + "Use Advanced Revisions for visibility, rollback or conflicts.",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
    }

    private void ShowTransfers()
    {
        _status.Text = "Durable downloads and creator uploads, including offline history.";
        var jobs = _queue.ListJobs();
        if (jobs.Count == 0)
            _details.Children.Add(PrimeChrome.Text(
                "No transfers yet.", PrimeTypography.BodySmall,
                PrimeTheme.TextSecondaryBrush));
        foreach (CommunityTransferJob receipt in jobs)
        {
            var job = receipt;
            _list.Add(new UiListRow(
                job.Download?.DisplayName ?? (job.Kind == CommunityTransferKind.Upload
                    ? "Creator upload" : "Map download"),
                $"{job.State} · {job.AcknowledgedBytes / 1048576d:0.0} / "
                    + $"{job.TotalBytes / 1048576d:0.0} MiB", spacious: true)
                { Choice = job.Id },
                _ => ShowTransfer(job));
        }
    }

    private void ShowTransfer(CommunityTransferJob job)
    {
        _details.Children.Clear();
        _details.Children.Add(PrimeChrome.Title(job.State.ToString().ToUpperInvariant()));
        _details.Children.Add(PrimeChrome.Text(
            "Exact SHA-256: " + job.PackageHash,
            PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush));
        if (!string.IsNullOrWhiteSpace(job.Error))
            _details.Children.Add(PrimeChrome.Text(job.Error,
                PrimeTypography.BodySmall, PrimeTheme.WarningBrush));
        if (job.State == CommunityTransferState.NeedsReview)
            _details.Children.Add(PrimeChrome.Text(
                "Server revision changed. Resolve the parent conflict explicitly "
                + "in Advanced Revisions. No automatic overwrite will occur.",
                PrimeTypography.BodySmall, PrimeTheme.WarningBrush));
        if (job.State is CommunityTransferState.Paused
            or CommunityTransferState.Retryable or CommunityTransferState.Failed)
            _details.Children.Add(new PrimeButton("RESUME",
                () => { _queue.Resume(job.Id); ShowTransfers(); }, compact: true));
        if (job.State is CommunityTransferState.Queued
            or CommunityTransferState.Preparing or CommunityTransferState.Uploading
            or CommunityTransferState.Downloading or CommunityTransferState.Retryable)
            _details.Children.Add(new PrimeButton("PAUSE",
                () => { _queue.Pause(job.Id); ShowTransfers(); }, compact: true));
    }

    private void ShowPublish()
    {
        if (_intent == null)
        {
            _details.Children.Add(PrimeChrome.Title("NO MAP SELECTED"));
            _details.Children.Add(PrimeChrome.Text(
                "Open a Map Studio project and return to queue a frozen revision.",
                PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
            return;
        }
        _list.Add(new UiListRow(_intent.RuntimeName,
            "Captured immutable source · new package on queue", spacious: true)
            { Choice = _intent.MapId },
            _ => { /* Publish options stay visible in the detail pane. */ });
        _details.Children.Add(PrimeChrome.Title(_intent.RuntimeName));
        _details.Children.Add(PrimeChrome.Text(
            "Frozen authoring snapshot · MapId " + _intent.MapId,
            PrimeTypography.DataSmall, PrimeTheme.TextSecondaryBrush));
        _details.Children.Add(_notes);
        _details.Children.Add(PrimeChrome.Text(
            "The server assigns an immutable revision. If another revision "
            + "appears first, the queue stops for review instead of replacing it.",
            PrimeTypography.BodySmall, PrimeTheme.TextSecondaryBrush));
        foreach (var (name, visibility) in new[]
        {
            ("QUEUE PRIVATE DRAFT", CommunityPublishVisibility.Draft),
            ("QUEUE UNLISTED", CommunityPublishVisibility.Unlisted),
            ("QUEUE PUBLISHED", CommunityPublishVisibility.Published)
        })
        {
            var mode = visibility;
            var button = new PrimeButton(name, () => _ = QueueAsync(mode),
                compact: true, primary: mode == CommunityPublishVisibility.Draft);
            ControllerNav.Identify(button, "studio.creator.publish." + mode);
            _details.Children.Add(button);
        }
    }

    private async Task QueueAsync(CommunityPublishVisibility visibility)
    {
        if (_intent == null || _disposed || _operation == null) return;
        var token = _operation.Token;
        _status.Text = "Preparing immutable .ppmap from a frozen project…";
        try
        {
            var receipt = await _intent.QueueAsync(
                _endpoint, _operations, _queue, visibility, _notes.Text, token);
            if (_disposed || token.IsCancellationRequested) return;
            _status.Text = "Queued exact package " + receipt.PackageHash[..12]
                + "… Transfer continues if Studio closes. Check Transfers.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
            or HttpRequestException or ArgumentException or MapAuthoringException)
        {
            if (!_disposed && !token.IsCancellationRequested)
                _status.Text = "Publication was not queued: " + ex.Message;
        }
    }

    private void OnJobChanged(CommunityTransferJob job)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || _tabs.Index != 1) return;
            ShowTransfers();
            _status.Text = $"{job.Kind} {job.State} · "
                + $"{job.AcknowledgedBytes / 1048576d:0.0} MiB";
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.Changed -= OnJobChanged;
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = null;
    }
}
#endif
