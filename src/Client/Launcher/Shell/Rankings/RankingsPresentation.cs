using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Launcher.Theme;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

internal sealed record RankingsPresentationContext(
    bool SignedIn,
    RankingsState State,
    Action OpenGateway,
    Action<string, Func<Task>> Run,
    Func<string, Task> ChangeMetric,
    Func<Hunter?, Task> ChangeHunter,
    Func<bool, Task> Load);

/// <summary>Pure Avalonia composition over authoritative ranking state.</summary>
internal static class RankingsPresentation
{
    public static Control Build(RankingsPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = Stack(PrimeControlFactory.PageHeading("Rankings",
            "OFFICIAL CAREER LEADERBOARD",
            "Compare verified career results without leaving the current route."));
        if (!context.SignedIn)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Sign in required", "prime-heading"),
                Text("Official rankings and your highlighted row require an account.",
                    "prime-muted"),
                Button("Open Gateway", context.OpenGateway, primary: true))));
            return root;
        }

        RankingsState state = context.State;
        root.Children.Add(PrimeControlFactory.SectionPanel(BuildFilters(context)));
        if (state.Error != null)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Could not refresh rankings.", "prime-body"),
                new Expander
                {
                    Header = "Details",
                    Content = Text(state.Error, "prime-muted")
                },
                Button("Retry", () => context.Run("Load rankings",
                    () => context.Load(false)), primary: true))));
        }

        if (state.Rows.IsDefaultOrEmpty)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text(state.Loading ? "Loading rankings…"
                    : "No rankings are available for these filters.", "prime-muted"),
                state.Loading
                    ? Text("—     Loading player rows\n—     Loading player rows\n—     Loading player rows",
                        "prime-muted")
                    : Button("Refresh", () => context.Run("Load rankings",
                        () => context.Load(false)), primary: true))));
            return root;
        }

        PrimeLeaderboardRow? current = state.Rows.FirstOrDefault(row => row.IsCurrentPlayer);
        if (current is { } currentPlayer)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Your position", "prime-heading"),
                Text(currentPlayer.Entry.DisplayName, "prime-title"),
                Text($"{PrimeLeaderboardMetrics.Label(state.Metric)} · "
                    + currentPlayer.Entry.Score.ToString(CultureInfo.InvariantCulture),
                    "prime-body"),
                Text($"{currentPlayer.Entry.Matches} matches · "
                    + $"{currentPlayer.Entry.Kills} kills", "prime-muted"))));
        }

        var table = Stack();
        // This row remains outside any future vertical paging host, making it
        // the stable table header without introducing a custom grid control.
        table.Children.Add(BuildRow("#", "Player",
            PrimeLeaderboardMetrics.Label(state.Metric), "Matches", "Kills", header: true));
        int rank = 0;
        foreach (PrimeLeaderboardRow row in state.Rows)
        {
            rank++;
            table.Children.Add(PrimeControlFactory.SelectedRow(BuildRow(
                rank.ToString(CultureInfo.InvariantCulture), row.Entry.DisplayName,
                row.Entry.Score.ToString(CultureInfo.InvariantCulture),
                row.Entry.Matches.ToString(CultureInfo.InvariantCulture),
                row.Entry.Kills.ToString(CultureInfo.InvariantCulture)),
                row.IsCurrentPlayer));
        }
        if (state.CanLoadMore)
            table.Children.Add(Button("Next page", () => context.Run(
                "Load next ranking page", () => context.Load(true)), primary: true));
        root.Children.Add(new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = PrimeControlFactory.SectionPanel(table)
        });
        return root;
    }

    private static Control BuildFilters(RankingsPresentationContext context)
    {
        RankingsState state = context.State;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 12
        };
        var metricChoice = new ComboBox
        {
            ItemsSource = PrimeLeaderboardMetrics.All,
            SelectedItem = state.Metric,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinHeight = PrimeLayoutMetrics.MinimumTouchTargetDip,
            ItemTemplate = new FuncDataTemplate<string>((metric, _) =>
                Text(PrimeLeaderboardMetrics.Label(metric), "prime-body"))
        };
        metricChoice.SelectionChanged += (_, _) =>
        {
            if (metricChoice.SelectedItem is string metric && metric != context.State.Metric)
                context.Run("Change ranking metric", () => context.ChangeMetric(metric));
        };
        grid.Children.Add(Stack(Text("Metric", "prime-label"), metricChoice));

        if (state.Metric != "rp")
        {
            var values = new List<object> { "All Hunters" };
            values.AddRange(Enum.GetValues<Hunter>().Where(hunter => hunter <= Hunter.Weavel)
                .Cast<object>());
            var choice = new ComboBox
            {
                ItemsSource = values,
                SelectedItem = state.HunterFilter is { } hunter ? hunter : "All Hunters",
                HorizontalAlignment = HorizontalAlignment.Stretch,
                MinHeight = PrimeLayoutMetrics.MinimumTouchTargetDip
            };
            choice.SelectionChanged += (_, _) =>
            {
                Hunter? hunter = choice.SelectedItem is Hunter selected ? selected : null;
                if (hunter != context.State.HunterFilter)
                    context.Run("Change Hunter filter", () => context.ChangeHunter(hunter));
            };
            Control field = Stack(Text("Hunter", "prime-label"), choice);
            grid.Children.Add(field);
            Grid.SetColumn(field, 1);
        }
        return grid;
    }

    private static Grid BuildRow(string rank, string player, string score,
        string matches, string kills, bool header = false)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("54,*,140,100,100"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, header ? 8 : 0),
            MinHeight = PrimeLayoutMetrics.MinimumTouchTargetDip
        };
        TextBlock[] cells =
        {
            Text(rank, "prime-label"), Text(player, header ? "prime-label" : "prime-body"),
            Text(score, header ? "prime-label" : "prime-body"),
            Text(matches, header ? "prime-label" : "prime-muted"),
            Text(kills, header ? "prime-label" : "prime-muted")
        };
        for (int index = 0; index < cells.Length; index++)
        {
            row.Children.Add(cells[index]);
            Grid.SetColumn(cells[index], index);
        }
        return row;
    }

    private static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 10 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { style } };

    private static AvaloniaButton Button(string label, Action action, bool primary = false)
        => PrimeControlFactory.Button(label, action, primary);
}
