using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaButton = Avalonia.Controls.Button;
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
    Action CancelDelete);

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
        else
            actions.Children.Add(Text("Import replay is unavailable on this platform.",
                "prime-muted"));
        actions.Children.Add(Button("Refresh", () => context.Run(
            "Refresh replays", context.Refresh), quiet: true));
        root.Children.Add(actions);

        var importPath = Input("Import path");
        var exportPath = Input("Export destination");
        var advanced = Stack(Text("Raw paths are provided for advanced workflows only.",
            "prime-muted"));
        if (context.SupportsImport)
            advanced.Children.Add(Stack(Text("Import path", "prime-label"), importPath,
                Button("Import from path", () => context.Run("Import replay",
                    () => context.ImportFromPath(importPath.Text ?? "")))));
        if (context.SupportsExport)
            advanced.Children.Add(Stack(Text("Export destination", "prime-label"),
                exportPath));
        root.Children.Add(new Expander { Header = "Advanced", Content = advanced });

        PrimeReplayEntry? selected = state.Selected ?? state.Replays.FirstOrDefault();
        root.Children.Add(ResponsiveSplit(
            PrimeControlFactory.SectionPanel(BuildList(context, selected)),
            PrimeControlFactory.SectionPanel(BuildDetails(context, selected, exportPath))));
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
            var content = Stack(Text(card.Title, "prime-heading"),
                Text(card.RecordedLine, "prime-body"),
                Text($"{card.FileName} · {card.Size}", "prime-muted"));
            AvaloniaButton select = Button("", () => context.Select(captured), quiet: true);
            select.Content = content;
            select.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            list.Children.Add(new PrimeCard(PrimeControlFactory.SelectedRow(select,
                selected?.Id == replay.Id)));
        }
        return list;
    }

    private static Control BuildDetails(TheatrePresentationContext context,
        PrimeReplayEntry? selected, TextBox exportPath)
    {
        var detail = Stack(Text("Replay details", "prime-heading"));
        if (selected == null)
        {
            detail.Children.Add(Text("Select a replay to see its details.", "prime-muted"));
            return detail;
        }

        PrimeReplayEntry captured = selected;
        PrimeReplayPresentation card = PrimeReplayPresentation.From(selected);
        detail.Children.Add(Text(card.Title, "prime-title"));
        detail.Children.Add(Text(card.FileName, "prime-body"));
        detail.Children.Add(Text($"Recorded {card.RecordedLine} · {card.Size}", "prime-muted"));
        detail.Children.Add(Text("FULL REPLAY", "prime-label"));
        detail.Children.Add(Button("Watch Replay", () => context.Run("Play replay",
            () => context.Play(captured)), primary: true));
        detail.Children.Add(Text("HIGHLIGHTS", "prime-label"));
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
        if (context.SupportsExport)
        {
            detail.Children.Add(Button("Export Replay", () => context.Run("Export replay",
                () => context.ExportWithPicker(captured))));
            detail.Children.Add(Button("Export to advanced path", () => context.Run(
                "Export replay", () => context.ExportToPath(captured,
                    exportPath.Text ?? "")), quiet: true));
        }
        else
            detail.Children.Add(Text("Export replay is unavailable on this platform.",
                "prime-muted"));
        detail.Children.Add(Text("Show File is unavailable on this platform.", "prime-muted"));

        if (context.SupportsRename)
        {
            TextBox rename = Input("Rename file (must end in .fpreplay)");
            detail.Children.Add(rename);
            detail.Children.Add(Button("Rename", () => context.Run("Rename replay",
                () => context.Rename(captured, rename.Text ?? ""))));
        }
        else detail.Children.Add(Text("Rename is unavailable on this platform.",
            "prime-muted"));

        if (context.PendingDeleteId == selected.Id)
        {
            detail.Children.Add(Text("Delete this local replay?", "prime-muted"));
            detail.Children.Add(Button("Confirm Delete", () => context.Run("Delete replay",
                () => context.Delete(captured)), primary: true));
            detail.Children.Add(Button("Cancel", context.CancelDelete, quiet: true));
        }
        else detail.Children.Add(Button("Delete", () => context.RequestDelete(captured)));
        return detail;
    }

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
}
