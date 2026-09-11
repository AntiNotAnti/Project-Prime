using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AvaloniaButton = Avalonia.Controls.Button;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Network;

namespace MphRead.Mods.Launcher.Gui;

internal sealed record TheatrePresentationContext(
    TheatreState State,
    bool SupportsImport,
    bool SupportsExport,
    bool SupportsRename,
    string? PendingDeleteId,
    Action<string, Func<Task>> Run,
    Func<Task> Refresh,
    Func<Task> ImportWithPicker,
    Func<string, Task> ImportFromPath,
    Action<PrimeReplayEntry> Select,
    Func<PrimeReplayEntry, Task> Play,
    Action<int> SelectHighlight,
    Func<int, Task> PlayHighlight,
    Func<Task> PlayHighlightReel,
    Func<PrimeReplayEntry, Task> ExportWithPicker,
    Func<PrimeReplayEntry, string, Task> ExportToPath,
    Func<PrimeReplayEntry, string, Task> Rename,
    Func<PrimeReplayEntry, Task> Delete,
    Action<PrimeReplayEntry> RequestDelete,
    Action CancelDelete,
    bool SupportsReveal = false,
    Func<PrimeReplayEntry, Task>? Reveal = null,
    Func<string, Task<PrimePreviewImage?>>? LoadMapPreview = null);

/// <summary>Local replay presentation. File-picker authority remains in the shell.</summary>
internal static class TheatrePresentation
{
    public static Control Build(TheatrePresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TheatreState state = context.State;
        var root = Stack(PrimeControlFactory.PageHeading("Theatre", "REPLAYS",
            "Import, watch, and manage replays stored on this device."));
        if (state.Error != null)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Could not refresh replays.", "prime-body"),
                new Expander { Header = "Details", Content = Text(state.Error, "prime-muted") },
                Button("Retry", () => context.Run("Refresh replays", context.Refresh),
                    primary: true))));
        }

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (context.SupportsImport)
            actions.Children.Add(Button("Import Replay", () => context.Run(
                "Import replay", context.ImportWithPicker), primary: true));
        actions.Children.Add(Button("Refresh", () => context.Run(
            "Refresh replays", context.Refresh), quiet: true));
        root.Children.Add(actions);

        var importPath = Input("Import path");
        var exportPath = Input("Export destination");
        var advanced = Stack(Text("Raw paths and technical details are for advanced workflows only.",
            "prime-muted"));
        if (context.SupportsImport)
            advanced.Children.Add(Stack(Text("Import path", "prime-label"), importPath,
                Button("Import from path", () => context.Run("Import replay",
                    () => context.ImportFromPath(importPath.Text ?? "")))));
        if (context.SupportsExport)
            advanced.Children.Add(Stack(Text("Export destination", "prime-label"),
                exportPath));

        PrimeReplayEntry? selected = state.Selected ?? state.Replays.FirstOrDefault();
        if (selected != null)
        {
            if (context.SupportsExport)
            {
                PrimeReplayEntry captured = selected;
                advanced.Children.Add(Button("Export to path", () => context.Run(
                    "Export replay", () => context.ExportToPath(captured,
                        exportPath.Text ?? "")), quiet: true));
            }
            advanced.Children.Add(Text("Selected replay metadata", "prime-heading"));
            advanced.Children.Add(BuildTechnicalDetails(selected));
        }
        root.Children.Add(new Expander
        {
            Header = "Advanced",
            Content = advanced,
            IsExpanded = false
        });
        root.Children.Add(ResponsiveSplit(
            PrimeControlFactory.SectionPanel(BuildList(context, selected)),
            PrimeControlFactory.SectionPanel(BuildDetails(context, selected))));
        return root;
    }

    private static Control BuildList(TheatrePresentationContext context,
        PrimeReplayEntry? selected)
    {
        TheatreState state = context.State;
        var list = Stack(Text("Replays", "prime-heading"));
        if (state.Replays.Count == 0)
            list.Children.Add(Text(state.Loading ? "Loading replays…"
                : "No replays yet. Played matches can be recorded and reviewed here.",
                "prime-muted"));
        foreach (PrimeReplayEntry replay in state.Replays)
        {
            PrimeReplayEntry captured = replay;
            PrimeReplayPresentation card = PrimeReplayPresentation.From(replay);
            var content = Stack(Text(ReplayTitle(replay), "prime-heading"),
                Text(card.RecordedLine, "prime-body"),
                Text(card.Size, "prime-muted"));
            AvaloniaButton select = Button("", () => context.Select(captured), quiet: true);
            select.Content = content;
            select.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            select.HorizontalAlignment = HorizontalAlignment.Stretch;

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8
            };
            row.Children.Add(select);
            AvaloniaButton watch = Button("Watch", () => context.Run("Play replay",
                () => context.Play(captured)));
            watch.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(watch);
            Grid.SetColumn(watch, 1);
            list.Children.Add(PrimeControlFactory.SelectedRow(row,
                selected?.Id == replay.Id));
        }
        return list;
    }

    private static Control BuildDetails(TheatrePresentationContext context,
        PrimeReplayEntry? selected)
    {
        var detail = Stack(Text("Replay details", "prime-heading"));
        if (selected == null)
        {
            detail.Children.Add(Text("Select a replay to see its details.", "prime-muted"));
            return detail;
        }

        PrimeReplayEntry captured = selected;
        PrimeReplayPresentation card = PrimeReplayPresentation.From(selected);
        detail.Children.Add(Text(ReplayTitle(selected), "prime-title"));
        detail.Children.Add(Text($"Recorded {card.RecordedLine} · {card.Size}", "prime-muted"));
        if (context.LoadMapPreview != null && !String.IsNullOrWhiteSpace(captured.Room))
        {
            detail.Children.Add(PrimeControlFactory.PreviewStage(
                new TheatreMapPreview(captured.Room, context.LoadMapPreview)));
        }
        detail.Children.Add(Text("Full replay", "prime-label"));
        AvaloniaButton watchReplay = Button("Watch Replay", () => context.Run("Play replay",
            () => context.Play(captured)), primary: true);
        watchReplay.HorizontalAlignment = HorizontalAlignment.Left;
        watchReplay.MinWidth = 160;
        detail.Children.Add(watchReplay);
        detail.Children.Add(Text("Highlights", "prime-label"));
        if (context.State.HighlightMetadata == null)
            detail.Children.Add(Text("Analyzing authoritative replay events…", "prime-muted"));
        else if (!context.State.HighlightMetadata.IsAvailable)
            detail.Children.Add(Text(context.State.HighlightMetadata.Error
                ?? "Highlights are unavailable for this replay.", "prime-muted"));
        else if (context.State.Highlights.Count == 0)
            detail.Children.Add(Text("No highlight candidates were found.", "prime-muted"));
        else
        {
            for (int index = 0; index < context.State.Highlights.Count; index++)
            {
                int capturedIndex = index;
                ReplayHighlight highlight = context.State.Highlights[index];
                string time = $"{highlight.FocusFrame / 60 / 60:00}:{highlight.FocusFrame / 60 % 60:00}";
                AvaloniaButton row = Button($"{highlight.Label}    {time}",
                    () => context.SelectHighlight(capturedIndex), quiet: true);
                detail.Children.Add(PrimeControlFactory.SelectedRow(row,
                    context.State.SelectedHighlight == index));
            }
            int selectedHighlight = context.State.SelectedHighlight >= 0
                ? context.State.SelectedHighlight : 0;
            detail.Children.Add(Button("Play Highlight", () => context.Run(
                "Play highlight", () => context.PlayHighlight(selectedHighlight)),
                primary: true));
            detail.Children.Add(Button("Play Highlight Reel", () => context.Run(
                "Play highlight reel", context.PlayHighlightReel)));
        }
        detail.Children.Add(new Expander
        {
            Header = "Manage replay",
            Content = BuildManagementActions(context, captured),
            IsExpanded = false
        });
        return detail;
    }

    private static Control BuildManagementActions(TheatrePresentationContext context,
        PrimeReplayEntry replay)
    {
        var management = Stack(Text(
            "Secondary actions affect the local replay file.", "prime-muted"));
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (context.SupportsExport)
        {
            actions.Children.Add(Button("Export Replay", () => context.Run("Export replay",
                () => context.ExportWithPicker(replay)), quiet: true));
        }
        if (context.SupportsReveal && context.Reveal is { } reveal)
        {
            actions.Children.Add(Button("Show File", () => context.Run(
                "Show replay file", () => reveal(replay)), quiet: true));
        }
        if (actions.Children.Count > 0) management.Children.Add(actions);

        if (context.SupportsRename)
        {
            TextBox rename = Input("New replay name");
            management.Children.Add(Stack(Text("Rename", "prime-label"), rename,
                Button("Rename", () => context.Run("Rename replay",
                    () => context.Rename(replay, rename.Text ?? "")), quiet: true)));
        }

        var deleteActions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (context.PendingDeleteId == replay.Id)
        {
            management.Children.Add(Text("Delete this local replay?", "prime-muted"));
            deleteActions.Children.Add(Button("Confirm Delete", () => context.Run(
                "Delete replay", () => context.Delete(replay))));
            deleteActions.Children.Add(Button("Cancel", context.CancelDelete, quiet: true));
        }
        else
        {
            deleteActions.Children.Add(Button("Delete",
                () => context.RequestDelete(replay), quiet: true));
        }
        management.Children.Add(deleteActions);
        return management;
    }

    private static Control BuildTechnicalDetails(PrimeReplayEntry replay)
        => Stack(Text($"File name: {replay.FileName}", "prime-muted"),
            Text($"Replay path: {replay.Path}", "prime-muted"),
            Text($"Room key: {replay.Room}", "prime-muted"),
            Text($"Recorded value: {replay.Recorded:O}", "prime-muted"),
            Text($"Byte count: {replay.Bytes}", "prime-muted"));

    private static string ReplayTitle(PrimeReplayEntry replay)
        => String.IsNullOrWhiteSpace(replay.Room)
            ? "Replay"
            : PrimeGameText.MapName(replay.Room);

    private static Grid ResponsiveSplit(Control left, Control right)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ColumnDefinitions = new ColumnDefinitions("3*,2*"),
            RowDefinitions = new RowDefinitions("Auto"),
            ColumnSpacing = 16
        };
        grid.Children.Add(left);
        grid.Children.Add(right);
        Grid.SetColumn(right, 1);
        grid.SizeChanged += (_, args) =>
        {
            bool narrow = args.NewSize.Width > 0 && args.NewSize.Width < 960;
            grid.ColumnDefinitions = new ColumnDefinitions(narrow ? "*" : "3*,2*");
            grid.RowDefinitions = new RowDefinitions(narrow ? "Auto,Auto" : "Auto");
            grid.ColumnSpacing = narrow ? 0 : 16;
            grid.RowSpacing = narrow ? 12 : 0;
            Grid.SetColumn(right, narrow ? 0 : 1);
            Grid.SetRow(right, narrow ? 1 : 0);
        };
        return grid;
    }

    private static TextBox Input(string watermark) => new()
    {
        Watermark = watermark,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        MinHeight = 44
    };

    private static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { style } };

    private static AvaloniaButton Button(string label, Action action,
        bool primary = false, bool quiet = false)
        => PrimeControlFactory.Button(label, action, primary, quiet);

    /// <summary>
    /// Owns only the decoded bitmap for this rendered card. The map service
    /// and its bounded cache remain shell-owned and are supplied through the
    /// context hook.
    /// </summary>
    private sealed class TheatreMapPreview : ContentControl
    {
        private readonly string _roomKey;
        private readonly Func<string, Task<PrimePreviewImage?>> _load;
        private Bitmap? _bitmap;
        private int _loadGeneration;
        private bool _loadStarted;

        internal TheatreMapPreview(string roomKey,
            Func<string, Task<PrimePreviewImage?>> load)
        {
            _roomKey = roomKey;
            _load = load;
            Height = 220;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Content = Text("Loading map preview…", "prime-muted");
            AttachedToVisualTree += (_, _) => StartLoad();
            DetachedFromVisualTree += (_, _) =>
            {
                _loadStarted = false;
                Interlocked.Increment(ref _loadGeneration);
                DisposeBitmap();
            };
            StartLoad();
        }

        private void StartLoad()
        {
            if (_loadStarted) return;
            _loadStarted = true;
            int generation = Interlocked.Increment(ref _loadGeneration);
            Content = Text("Loading map preview…", "prime-muted");
            _ = LoadAsync(generation);
        }

        private async Task LoadAsync(int generation)
        {
            try
            {
                PrimePreviewImage? preview = await _load(_roomKey)
                    .ConfigureAwait(false);
                if (preview == null || generation != Volatile.Read(ref _loadGeneration)) return;
                using var stream = new MemoryStream(preview.Data, writable: false);
                Bitmap bitmap = new(stream);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != Volatile.Read(ref _loadGeneration))
                    {
                        bitmap.Dispose();
                        return;
                    }
                    Bitmap? previous = _bitmap;
                    _bitmap = bitmap;
                    Content = new Image
                    {
                        Source = bitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Stretch
                    };
                    previous?.Dispose();
                });
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        private void DisposeBitmap()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
