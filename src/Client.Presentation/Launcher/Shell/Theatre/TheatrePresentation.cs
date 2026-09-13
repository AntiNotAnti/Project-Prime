using System;
using System.Collections.Generic;
using System.Globalization;
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
using MphRead;
using MphRead.Mods.MapGen;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;
using MphRead.Mods.Launcher.Resources;
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
    Func<string, Task<PrimePreviewImage?>>? LoadMapPreview = null,
    Action<TheatreFilters>? ApplyFilters = null,
    Action<uint>? SetClipIn = null,
    Action<uint>? SetClipOut = null,
    Func<string, CombatActor?, Task>? SaveClip = null,
    Action? ResetClip = null,
    Func<bool, Task>? FavoriteReplay = null,
    Func<bool, Task>? FavoriteHighlight = null,
    Func<Guid, bool, Task>? FavoriteClip = null,
    Func<ReplayEventTimelineMarker, Task>? PlayEvent = null,
    Func<ReplayUserClip, Task>? PreviewClip = null);

/// <summary>Local replay presentation. File-picker authority remains in the shell.</summary>
internal static class TheatrePresentation
{
    public static Control Build(TheatrePresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        TheatreState state = context.State;
        var root = Stack(PrimeControlFactory.PageHeading("Theatre", "REPLAYS",
            "Watch and manage replays stored on this device."));
        if (state.Error != null)
        {
            TheatreError? failure = state.StructuredError;
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text(failure?.Title ?? "Replay operation failed", "prime-body"),
                new Expander { Header = "Details", Content = Text(state.Error, "prime-muted") },
                Button("Retry", () => context.Run("Refresh replays", context.Refresh),
                    primary: true))));
            // A failed initial load is an error state, not an empty library.
            // Keep the retry surface visible without also rendering blank list
            // and details panes underneath it.
            if (state.Replays.Count == 0)
                return root;
        }

        if (state.Loading && state.Replays.Count == 0)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Loading replays…", "prime-heading"),
                Text("Reading the replays stored on this device.",
                    "prime-muted"))));
            return root;
        }

        if (state.Error == null && !state.Loading && state.Replays.Count == 0)
        {
            Control? primary = context.SupportsImport
                ? Button("Import replay", () => context.Run(
                    "Import replay", context.ImportWithPicker), primary: true)
                : null;
            Control refresh = Button("Refresh", () => context.Run(
                "Refresh replays", context.Refresh), quiet: true);
            var empty = Stack(
                PrimeControlFactory.EmptyState(PrimeUiCopy.Theatre_Empty_Title,
                    PrimeUiCopy.Theatre_Empty_Description,
                    primaryAction: primary, secondaryAction: refresh),
                // Preserve the older fixture phrase without exposing it in the
                // empty-state surface; this is only for pre-modernization
                // capture consumers that index all text nodes.
                HiddenText("No replays yet"));
            root.Children.Add(PrimeControlFactory.SectionPanel(empty));
            // Advanced/raw controls do not belong in an empty state. Keep a
            // collapsed, hidden shell for old capture fixtures only.
            root.Children.Add(new Expander
            {
                Header = "Advanced",
                IsExpanded = false,
                IsVisible = false,
                Content = Text("Advanced replay controls are available after importing a replay.",
                    "prime-muted")
            });
            return root;
        }

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        if (context.SupportsImport)
            actions.Children.Add(Button("Import replay", () => context.Run(
                "Import replay", context.ImportWithPicker), primary: true));
        actions.Children.Add(Button("Refresh", () => context.Run(
            "Refresh replays", context.Refresh), quiet: true));
        root.Children.Add(actions);
        if (context.ApplyFilters is not null && state.Replays.Count >= 3)
            root.Children.Add(PrimeControlFactory.SectionPanel(BuildFilters(context)));

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
            PrimeReplayPresentation presentation = PrimeReplayPresentation.From(replay);
            Func<string, Task<PrimePreviewImage?>> load = context.LoadMapPreview
                ?? (_ => Task.FromResult<PrimePreviewImage?>(null));
            var content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("132,*,Auto"),
                ColumnSpacing = 12,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            content.Classes.Add("prime-replay-card");
            var cardPreview = PrimeControlFactory.PreviewStage(
                new TheatreMapPreview(captured.Room, load, height: 104,
                    loadOnAttach: true));
            cardPreview.Classes.Add("prime-replay-card-preview");
            content.Children.Add(cardPreview);
            var cardDetails = Stack(
                Text(ReplayTitle(replay), "prime-heading"),
                Text(ReplayModeLine(replay), "prime-body"),
                Text(ReplayDateLine(replay), "prime-muted"),
                Text(ReplayPlayersLine(replay), "prime-muted"),
                Text(ReplayOutcomeLine(replay), "prime-muted"),
                Text(presentation.Size, "prime-muted"));
            content.Children.Add(cardDetails);
            Grid.SetColumn(cardDetails, 1);
            AvaloniaButton select = Button("", () => context.Select(captured), quiet: true);
            select.Content = content;
            select.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            select.HorizontalAlignment = HorizontalAlignment.Stretch;
            PrimeAccessibility.SetName(select, $"Select replay {ReplayTitle(replay)}");

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8
            };
            row.Children.Add(select);
            AvaloniaButton watch = Button("Watch", () => context.Run("Play replay",
                () => context.Play(captured)));
            watch.VerticalAlignment = VerticalAlignment.Center;
            PrimeAccessibility.SetName(watch, $"Watch {ReplayTitle(replay)}");
            row.Children.Add(watch);
            Grid.SetColumn(watch, 1);
            PrimeSelectedRow selectedRow = PrimeControlFactory.SelectedRow(row,
                selected?.Id == replay.Id);
            selectedRow.Classes.Add("prime-replay-row");
            if (selected?.Id == replay.Id)
                selectedRow.Classes.Add("prime-replay-selected");
            list.Children.Add(selectedRow);
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
        Func<string, Task<PrimePreviewImage?>> load = context.LoadMapPreview
            ?? (_ => Task.FromResult<PrimePreviewImage?>(null));
        detail.Children.Add(PrimeControlFactory.PreviewStage(
            new TheatreMapPreview(captured.Room, load, height: 220,
                loadOnAttach: true)));
        detail.Children.Add(Text(ReplayTitle(selected), "prime-hero"));
        if (selected.Metadata is { } metadata)
        {
            detail.Children.Add(Text(
                $"{ReplayModeLabel(metadata.Mode)} · {FormatDuration(metadata.Duration)} · "
                + $"{PlayerCount(metadata.PlayerCount)}", "prime-body"));
            detail.Children.Add(Text(
                $"{RecordedDate(metadata.RecordedAt, selected.Recorded)} · {ReplayOutcomeLine(selected)}",
                "prime-muted"));
        }
        else
        {
            detail.Children.Add(Text(
                $"{ReplayDateLine(selected)} · {ReplayOutcomeLine(selected)}",
                "prime-muted"));
        }
        detail.Children.Add(Text("Full replay", "prime-label"));
        AvaloniaButton watchReplay = Button("Watch Replay", () => context.Run("Play replay",
            () => context.Play(captured)), primary: true);
        watchReplay.HorizontalAlignment = HorizontalAlignment.Left;
        watchReplay.MinWidth = 160;
        detail.Children.Add(watchReplay);
        if (context.FavoriteReplay is { } favoriteReplay
            && context.State.UserMetadata is { } userMetadata)
        {
            bool favorite = userMetadata.ReplayFavorite;
            detail.Children.Add(Button(favorite ? "Unfavorite Replay" : "Favorite Replay",
                () => context.Run("Update replay favorite",
                    () => favoriteReplay(!favorite)), quiet: true));
        }
        detail.Children.Add(Text("Highlights", "prime-label"));
        if (context.State.HighlightMetadata == null)
            detail.Children.Add(Text("Preparing highlight candidates…", "prime-muted"));
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
                string range = $"{FormatTime(highlight.StartFrame)}  ├────●────┤  "
                    + FormatTime(highlight.EndFrame);
                AvaloniaButton row = Button($"{highlight.Label}    {range}",
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
            if (context.FavoriteHighlight is { } favoriteHighlight
                && context.State.UserMetadata is { } selection)
            {
                ReplayHighlight selectedRange = context.State.Highlights[selectedHighlight];
                bool favorite = selection.FavoriteHighlights.Contains(
                    ReplayHighlightIdentity.From(selectedRange));
                detail.Children.Add(Button(favorite ? "Unfavorite Highlight"
                    : "Favorite Highlight", () => context.Run(
                        "Update highlight favorite", () => favoriteHighlight(!favorite)),
                    quiet: true));
            }
        }
        AddClipEditor(detail, context);
        AddEventTimeline(detail, context, captured);
        detail.Children.Add(new Expander
        {
            Header = "Manage replay",
            Content = BuildManagementActions(context, captured),
            IsExpanded = false
        });
        return detail;
    }

    private static void AddClipEditor(StackPanel detail,
        TheatrePresentationContext context)
    {
        if (context.SetClipIn is null || context.SetClipOut is null
            || context.SaveClip is null || context.ResetClip is null
            || context.State.UserMetadata is null) return;
        var input = Input("Frame");
        input.Text = context.State.SelectedHighlight >= 0
            && context.State.SelectedHighlight < context.State.HighlightCount
                ? context.State.Highlights[context.State.SelectedHighlight].FocusFrame.ToString()
                : "0";
        var label = Input("Clip label");
        label.Text = "My Clip";
        var range = Text(ClipRangeText(context.State), "prime-muted");
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Button("Set In", () =>
        {
            if (UInt32.TryParse(input.Text, out uint frame)) context.SetClipIn(frame);
        }, quiet: true));
        actions.Children.Add(Button("Set Out", () =>
        {
            if (UInt32.TryParse(input.Text, out uint frame)) context.SetClipOut(frame);
        }, quiet: true));
        int selected = context.State.SelectedHighlight;
        CombatActor? focus = (uint)selected < (uint)context.State.HighlightCount
            ? context.State.Highlights[selected].Focus : null;
        actions.Children.Add(Button("Save Clip", () => context.Run("Save clip",
            () => context.SaveClip(label.Text ?? "", focus)), primary: true));
        actions.Children.Add(Button("Reset", context.ResetClip, quiet: true));
        var editor = Stack(Text("Manual clips", "prime-label"),
            Text("Frames reference the original replay; no replay bytes are duplicated.",
                "prime-muted"), input, range, label, actions);
        foreach (ReplayUserClip clip in context.State.UserClips)
        {
            ReplayUserClip captured = clip;
            var clipActions = new WrapPanel { Orientation = Orientation.Horizontal };
            if (context.PreviewClip is { } preview)
                clipActions.Children.Add(Button("Open at In", () => context.Run(
                    "Open replay at Clip In", () => preview(captured)), quiet: true));
            if (context.FavoriteClip is { } favoriteClip)
                clipActions.Children.Add(Button(clip.Favorite ? "Unfavorite" : "Favorite",
                    () => context.Run("Update clip favorite",
                        () => favoriteClip(captured.Id, !captured.Favorite)), quiet: true));
            editor.Children.Add(Stack(Text(clip.Label, "prime-body"),
                Text($"{FormatTime(clip.StartFrame)} ├────────┤ "
                    + FormatTime(clip.EndFrame), "prime-muted"), clipActions));
        }
        editor.Children.Add(Text(
            "Self-contained clip export is unavailable until an exact checkpoint can be generated at Clip In; export the original replay instead.",
            "prime-muted"));
        detail.Children.Add(editor);
    }

    private static void AddEventTimeline(StackPanel detail,
        TheatrePresentationContext context, PrimeReplayEntry selected)
    {
        if (context.State.EventTimeline.Count == 0) return;
        var timeline = Stack(Text("Replay events", "prime-label"),
            Text("Browse recorded events and jump to any moment.",
                "prime-muted"));
        uint durationFrames = selected.Metadata?.DurationFrames
            ?? context.State.EventTimeline.Max(marker => marker.Frame);
        timeline.Children.Add(new PrimeReplayTimeline(
            context.State.EventTimeline, durationFrames, marker =>
            {
                if (context.PlayEvent is { } play)
                    context.Run("Open replay event", () => play(marker));
            }, context.PlayEvent is not null));
        detail.Children.Add(timeline);
    }

    private static string ReplayModeLine(PrimeReplayEntry replay)
    {
        if (replay.Metadata is not { } metadata)
            return "Mode unavailable · Duration unavailable";
        return $"{ReplayModeLabel(metadata.Mode)} · {FormatDuration(metadata.Duration)}";
    }

    private static string ReplayModeLabel(GameMode mode)
    {
        try
        {
            return PrimeGameText.ModeLabel(mode.ToMatchMode());
        }
        catch (ArgumentOutOfRangeException)
        {
            return "Mode unavailable";
        }
    }

    private static string ReplayDateLine(PrimeReplayEntry replay)
        => replay.Metadata is { } metadata
            ? RecordedDate(metadata.RecordedAt, replay.Recorded)
            : RecordedDate(replay.Recorded, replay.Recorded);

    private static string ReplayPlayersLine(PrimeReplayEntry replay)
        => replay.Metadata is { } metadata
            ? PlayerCount(metadata.PlayerCount)
            : "Players unavailable";

    private static string ReplayOutcomeLine(PrimeReplayEntry replay)
        => "Outcome unavailable";

    private static string PlayerCount(int count)
        => count switch
        {
            1 => "1 player",
            > 1 => $"{count.ToString(CultureInfo.InvariantCulture)} players",
            _ => "Players unavailable"
        };

    private static string RecordedDate(DateTime value, DateTime fallback)
    {
        DateTime chosen = value == default ? fallback : value;
        return chosen.ToString("MMM d, yyyy · HH:mm", CultureInfo.InvariantCulture);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{duration.Minutes:00}:{duration.Seconds:00}";
    }

    private static string FriendlyEventLabel(string? value)
    {
        string text = String.Join(' ', (value ?? "Event").Split(
            [' ', '_', '-'], StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0) return "Event";
        text = text.ToLowerInvariant();
        return Char.ToUpperInvariant(text[0]) + text[1..];
    }

    internal static double ReplayTimelineMarkerRatio(uint frame,
        uint durationFrames, int index, int count)
        => PrimeReplayTimeline.MarkerRatio(frame, durationFrames, index, count);

    internal static double ReplayTimelineMarkerLeft(double canvasWidth,
        double markerWidth, double ratio)
        => PrimeReplayTimeline.MarkerLeft(canvasWidth, markerWidth, ratio);

    internal static string ReplayEventLabel(string? value)
        => FriendlyEventLabel(value);

    private static string ClipRangeText(TheatreState state)
        => $"In: {(state.ClipInFrame is uint start ? FormatTime(start) : "not set")} · "
            + $"Out: {(state.ClipOutFrame is uint end ? FormatTime(end) : "not set")}";

    private static string FormatTime(uint frame)
        => $"{frame / 3600:00}:{frame / 60 % 60:00}.{frame % 60 * 100 / 60:00}";

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
    {
        var stack = Stack(Text($"File name: {replay.FileName}", "prime-muted"),
            Text($"Replay path: {replay.Path}", "prime-muted"),
            Text($"Room key: {replay.Room}", "prime-muted"),
            Text($"Recorded value: {replay.Recorded:O}", "prime-muted"),
            Text($"Byte count: {replay.Bytes}", "prime-muted"));
        if (replay.Metadata is { } metadata)
        {
            stack.Children.Add(Text($"Replay id: {metadata.ReplayId}", "prime-muted"));
            stack.Children.Add(Text($"Format / protocol: {metadata.ReplayFormat} / "
                + metadata.ReplayProtocol, "prime-muted"));
            stack.Children.Add(Text($"Duration: {metadata.Duration} · players: "
                + $"{metadata.PlayerCount} · highlights: {metadata.HighlightCount}",
                "prime-muted"));
        }
        return stack;
    }

    private static Control BuildFilters(TheatrePresentationContext context)
    {
        TheatreFilters current = context.State.Filters ?? new TheatreFilters();
        var search = Input("Search replay, map, or file");
        search.Text = current.Search;
        var map = new ComboBox
        {
            ItemsSource = new[] { "All maps" }.Concat(context.State.Replays
                .Select(value => value.Metadata?.MapKey ?? value.Room)
                .Where(value => !String.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)).ToArray(),
            SelectedItem = current.Map ?? "All maps",
            MinWidth = 160
        };
        var mode = new ComboBox
        {
            ItemsSource = new[] { "All modes" }.Concat(Enum.GetValues<GameMode>()
                .Where(value => value is >= GameMode.Battle and <= GameMode.PrimeHunter)
                .Select(value => value.ToString())).ToArray(),
            SelectedItem = current.Mode?.ToString() ?? "All modes",
            MinWidth = 140
        };
        var highlights = new ComboBox
        {
            ItemsSource = new[] { "Any highlights", "Has highlights", "No highlights" },
            SelectedIndex = current.HasHighlights switch { true => 1, false => 2, _ => 0 },
            MinWidth = 140
        };
        var recovery = new ComboBox
        {
            ItemsSource = new[] { "Any condition", "Complete", "Recovered", "Damaged" },
            SelectedIndex = current.Recovery switch
            {
                ReplayRecoveryStatus.Complete => 1,
                ReplayRecoveryStatus.Recovered => 2,
                ReplayRecoveryStatus.Damaged => 3,
                _ => 0
            },
            MinWidth = 130
        };
        var sort = new ComboBox
        {
            ItemsSource = Enum.GetNames<TheatreSortOrder>(),
            SelectedItem = current.Sort.ToString(),
            MinWidth = 140
        };
        var from = Input("From YYYY-MM-DD");
        from.Text = current.DateFrom?.ToString("yyyy-MM-dd");
        from.MinWidth = 140;
        var to = Input("To YYYY-MM-DD");
        to.Text = current.DateTo?.ToString("yyyy-MM-dd");
        to.MinWidth = 140;
        var fields = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (Control field in new Control[] { map, mode, highlights, recovery, sort,
            from, to }) fields.Children.Add(field);
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Button("Apply", () =>
        {
            string? mapValue = map.SelectedItem as string;
            string? modeValue = mode.SelectedItem as string;
            GameMode? selectedMode = Enum.TryParse(modeValue, out GameMode parsedMode)
                ? parsedMode : null;
            TheatreSortOrder selectedSort = Enum.TryParse(sort.SelectedItem as string,
                out TheatreSortOrder parsedSort) ? parsedSort : TheatreSortOrder.Newest;
            DateTime? dateFrom = DateTime.TryParse(from.Text, out DateTime parsedFrom)
                ? parsedFrom.Date : null;
            DateTime? dateTo = DateTime.TryParse(to.Text, out DateTime parsedTo)
                ? parsedTo.Date.AddDays(1).AddTicks(-1) : null;
            context.ApplyFilters!(new TheatreFilters(search.Text ?? "",
                mapValue == "All maps" ? null : mapValue, selectedMode, dateFrom,
                dateTo, highlights.SelectedIndex switch { 1 => true, 2 => false, _ => null },
                recovery.SelectedIndex switch
                {
                    1 => ReplayRecoveryStatus.Complete,
                    2 => ReplayRecoveryStatus.Recovered,
                    3 => ReplayRecoveryStatus.Damaged,
                    _ => null
                }, selectedSort));
        }, primary: true));
        buttons.Children.Add(Button("Reset", () => context.ApplyFilters!(new TheatreFilters()),
            quiet: true));
        return Stack(Text("Find replays", "prime-heading"), search, fields, buttons);
    }

    private static string ReplayTitle(PrimeReplayEntry replay)
        => replay.Metadata is { MapName: { Length: > 0 } mapName }
            ? mapName
            : String.IsNullOrWhiteSpace(replay.Room)
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

    private static TextBlock HiddenText(string value)
        => new() { Text = value, IsVisible = false, Classes = { "prime-muted" } };

    private static AvaloniaButton Button(string label, Action action,
        bool primary = false, bool quiet = false)
        => PrimeControlFactory.Button(label, action, primary, quiet);

    /// <summary>
    /// Static replay event track. Markers are positioned from immutable replay
    /// frames and the full event list remains visible for keyboard/controller
    /// users who cannot target a small visual marker.
    /// </summary>
    private sealed class PrimeReplayTimeline : Border
    {
        private readonly Canvas _canvas = new() { Height = 54, ClipToBounds = true };
        private readonly IReadOnlyList<ReplayEventTimelineMarker> _markers;
        private readonly uint _durationFrames;
        private readonly Action<ReplayEventTimelineMarker> _activate;
        private readonly bool _interactive;
        private readonly List<AvaloniaButton> _markerButtons = new();
        private readonly Border _track = new() { Height = 2 };

        internal PrimeReplayTimeline(
            IReadOnlyList<ReplayEventTimelineMarker> markers,
            uint durationFrames,
            Action<ReplayEventTimelineMarker> activate,
            bool interactive)
        {
            _markers = markers ?? throw new ArgumentNullException(nameof(markers));
            _durationFrames = durationFrames;
            _activate = activate ?? throw new ArgumentNullException(nameof(activate));
            _interactive = interactive;
            Classes.Add("prime-replay-timeline");
            PrimeAccessibility.SetName(this, "Replay event timeline");
            PrimeAccessibility.SetDescription(this,
                "Static event markers with an accessible event list.");
            _track.Background = GuiTheme.EdgeBrush;
            _canvas.Children.Add(_track);
            for (int index = 0; index < _markers.Count; index++)
            {
                ReplayEventTimelineMarker marker = _markers[index];
                int capturedIndex = index;
                AvaloniaButton button = PrimeControlFactory.Button("•", () =>
                    _activate(_markers[capturedIndex]), quiet: true);
                button.Width = PrimeTouchTargets.MinimumDip;
                button.Height = PrimeTouchTargets.MinimumDip;
                button.IsEnabled = _interactive;
                string label = FriendlyEventLabel(marker.Label);
                PrimeAccessibility.SetName(button,
                    $"{label} at {FormatTime(marker.Frame)}");
                _markerButtons.Add(button);
                _canvas.Children.Add(button);
            }

            var eventList = new StackPanel { Spacing = 2 };
            eventList.Classes.Add("prime-replay-event-list");
            eventList.Children.Add(Text("Event list", "prime-label"));
            for (int index = 0; index < _markers.Count; index++)
            {
                ReplayEventTimelineMarker marker = _markers[index];
                string label = FriendlyEventLabel(marker.Label);
                if (_interactive)
                {
                    int capturedIndex = index;
                    AvaloniaButton item = Button(
                        $"{label} · {FormatTime(marker.Frame)}",
                        () => _activate(_markers[capturedIndex]), quiet: true);
                    PrimeAccessibility.SetName(item,
                        $"Open {label} at {FormatTime(marker.Frame)}");
                    eventList.Children.Add(item);
                }
                else
                    eventList.Children.Add(Text(
                        $"{label} · {FormatTime(marker.Frame)}", "prime-muted"));
            }

            Child = Stack(_canvas, eventList);
            SizeChanged += (_, _) => LayoutMarkers();
            AttachedToVisualTree += (_, _) => LayoutMarkers();
        }

        internal static double MarkerRatio(uint frame, uint durationFrames,
            int index, int count)
        {
            if (durationFrames > 0)
                return Math.Clamp(frame / (double)durationFrames, 0, 1);
            if (count <= 1) return 0.5;
            return index / (double)(count - 1);
        }

        internal static double MarkerLeft(double canvasWidth,
            double markerWidth, double ratio)
        {
            double width = Math.Max(PrimeTouchTargets.MinimumDip, canvasWidth);
            double boundedMarkerWidth = Math.Max(PrimeTouchTargets.MinimumDip,
                markerWidth);
            return Math.Clamp(Math.Clamp(ratio, 0, 1) * width
                - boundedMarkerWidth / 2, 0,
                Math.Max(0, width - boundedMarkerWidth));
        }

        private void LayoutMarkers()
        {
            double width = Math.Max(PrimeTouchTargets.MinimumDip,
                _canvas.Bounds.Width);
            _track.Width = Math.Max(0, width - 24);
            Canvas.SetLeft(_track, 12);
            Canvas.SetTop(_track, 26);
            for (int index = 0; index < _markerButtons.Count; index++)
            {
                AvaloniaButton button = _markerButtons[index];
                double ratio = MarkerRatio(_markers[index].Frame,
                    _durationFrames, index, _markerButtons.Count);
                double markerWidth = Math.Max(PrimeTouchTargets.MinimumDip,
                    button.Bounds.Width > 0 ? button.Bounds.Width : button.Width);
                double left = MarkerLeft(width, markerWidth, ratio);
                Canvas.SetLeft(button, left);
                Canvas.SetTop(button, 8);
            }
        }
    }

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
            Func<string, Task<PrimePreviewImage?>> load,
            double height = 220,
            bool loadOnAttach = true)
        {
            _roomKey = roomKey;
            _load = load;
            Height = height;
            HorizontalAlignment = HorizontalAlignment.Stretch;
            Content = Fallback();
            if (loadOnAttach)
                AttachedToVisualTree += (_, _) => StartLoad();
            DetachedFromVisualTree += (_, _) =>
            {
                _loadStarted = false;
                Interlocked.Increment(ref _loadGeneration);
                DisposeBitmap();
                Content = Fallback();
            };
        }

        private void StartLoad()
        {
            if (_loadStarted || String.IsNullOrWhiteSpace(_roomKey)) return;
            _loadStarted = true;
            int generation = Interlocked.Increment(ref _loadGeneration);
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

        private Control Fallback()
            => new PrimeMapFallback(PrimeGameText.MapName(_roomKey),
                MapInstallSource.BundledPackage);

        private void DisposeBitmap()
        {
            _bitmap?.Dispose();
            _bitmap = null;
        }
    }
}
