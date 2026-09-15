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
using MphRead.Mods.Launcher.Resources;
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
    private const double CompactRankingsWidth = 720;

    public static Control Build(RankingsPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = Stack(PrimeControlFactory.PageHeading("Rankings",
            "OFFICIAL CAREER LEADERBOARD",
            "Compare verified career results without leaving the current route."));
        if (!context.SignedIn)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                PrimeControlFactory.EmptyState(PrimeUiCopy.Rankings_SignIn_Title,
                    PrimeUiCopy.Rankings_SignIn_Description,
                    primaryAction: Button("Sign in", context.OpenGateway,
                        primary: true)),
                // The old route summary remains available to non-visual
                // capture consumers while the signed-out affordance is now a
                // direct, player-facing sign-in action.
                HiddenText("Sign in required"),
                HiddenText("Official rankings and your highlighted row require an account."))));
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
            root.Children.Add(BuildCurrentPlayerCard(state, currentPlayer));

        Control desktop = BuildDesktopLeaderboard(context, state);
        Control mobile = BuildMobileLeaderboard(context, state);
        root.Children.Add(new PrimeResponsiveLeaderboard(desktop, mobile));
        return root;
    }

    private static Control BuildDesktopLeaderboard(RankingsPresentationContext context,
        RankingsState state)
    {
        var table = Stack();
        if (state.Rows.Length >= 3)
        {
            table.Children.Add(BuildPodium(state));
            table.Children.Add(Text("Full leaderboard", "prime-heading"));
        }
        // This row remains outside any future vertical paging host, making it
        // the stable table header without introducing a custom grid control.
        table.Children.Add(BuildRow("#", "Player", "Score", "Matches", "Kills",
            header: true));
        int rank = 0;
        foreach (PrimeLeaderboardRow row in state.Rows)
        {
            rank++;
            PrimeSelectedRow selectedRow = PrimeControlFactory.SelectedRow(BuildRow(
                rank.ToString(CultureInfo.InvariantCulture), PlayerLabel(row),
                MetricValue(state.Metric, row.Entry.Score),
                row.Entry.Matches.ToString(CultureInfo.InvariantCulture),
                row.Entry.Kills.ToString(CultureInfo.InvariantCulture)),
                row.IsCurrentPlayer);
            selectedRow.Classes.Add("prime-rank-row");
            if (row.IsCurrentPlayer)
                selectedRow.Classes.Add("prime-rank-current");
            table.Children.Add(selectedRow);
        }
        if (state.CanLoadMore)
            table.Children.Add(Button("Next page", () => context.Run(
                "Load next ranking page", () => context.Load(true)), primary: true));
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = PrimeControlFactory.SectionPanel(table)
        };
        scroller.Classes.Add("prime-rankings-desktop");
        return scroller;
    }

    private static Control BuildCurrentPlayerCard(RankingsState state,
        PrimeLeaderboardRow current)
    {
        int rank = RankOf(state, current);
        var content = Stack(
            Text("YOUR RANK", "prime-kicker"),
            Text(rank > 0 ? $"#{rank.ToString(CultureInfo.InvariantCulture)}" : "—",
                "prime-hero"),
            Text(current.Entry.DisplayName, "prime-heading"),
            Text($"{MetricValue(state.Metric, current.Entry.Score)} "
                + PrimeLeaderboardMetrics.Label(state.Metric), "prime-body"),
            Text($"{current.Entry.Matches.ToString("N0", CultureInfo.InvariantCulture)} matches · "
                + $"{current.Entry.Kills.ToString("N0", CultureInfo.InvariantCulture)} kills",
                "prime-muted"));
        var card = PrimeControlFactory.HeroCard(content);
        card.Classes.Add("prime-current-rank");
        card.Classes.Add("prime-current-player");
        PrimeAccessibility.SetName(card, "Your rank");
        PrimeAccessibility.SetDescription(card,
            rank > 0
                ? $"Rank {rank.ToString(CultureInfo.InvariantCulture)} for {current.Entry.DisplayName}."
                : $"Current rank for {current.Entry.DisplayName}.");
        return card;
    }

    private static Control BuildPodium(RankingsState state)
    {
        // Keep the winner in the visual center while preserving the server's
        // authoritative ordering. The full table below remains the source of
        // truth for every row and metric.
        var podium = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*"),
            ColumnSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        podium.Classes.Add("prime-ranking-podium");
        int[] order = { 1, 0, 2 };
        for (int column = 0; column < order.Length; column++)
        {
            int rank = order[column];
            PrimeLeaderboardRow row = state.Rows[rank];
            var content = Stack(
                Text(PodiumLabel(rank), "prime-kicker"),
                Text(row.Entry.DisplayName, "prime-heading"),
                Text(MetricValue(state.Metric, row.Entry.Score), "prime-body"),
                Text($"{row.Entry.Matches.ToString("N0", CultureInfo.InvariantCulture)} matches",
                    "prime-muted"));
            PrimePanel place = PrimeControlFactory.Panel(content,
                secondary: true, compact: true);
            place.Classes.Add("prime-podium-place");
            place.Classes.Add(rank == 0
                ? "prime-podium-first"
                : rank == 1 ? "prime-podium-second" : "prime-podium-third");
            PrimeAccessibility.SetName(place,
                $"{PodiumLabel(rank)}: {row.Entry.DisplayName}");
            podium.Children.Add(place);
            Grid.SetColumn(place, column);
        }
        return podium;
    }

    private static string PodiumLabel(int rank)
        => rank switch
        {
            0 => "1st place",
            1 => "2nd place",
            2 => "3rd place",
            _ => $"Place {rank + 1}"
        };

    private static int RankOf(RankingsState state, PrimeLeaderboardRow current)
    {
        for (int index = 0; index < state.Rows.Length; index++)
        {
            if (ReferenceEquals(state.Rows[index], current)
                || state.Rows[index].Entry.PlayerId == current.Entry.PlayerId)
                return index + 1;
        }
        return 0;
    }

    private static Control BuildMobileLeaderboard(RankingsPresentationContext context,
        RankingsState state)
    {
        var cards = Stack();
        int rank = 0;
        foreach (PrimeLeaderboardRow row in state.Rows)
        {
            rank++;
            var content = Stack(
                Text($"#{rank.ToString(CultureInfo.InvariantCulture)}  {PlayerLabel(row)}",
                    "prime-heading"),
                Text($"{MetricValue(state.Metric, row.Entry.Score)} "
                    + PrimeLeaderboardMetrics.Label(state.Metric), "prime-body"),
                Text($"{row.Entry.Matches.ToString("N0", CultureInfo.InvariantCulture)} Matches · "
                    + $"{row.Entry.Kills.ToString("N0", CultureInfo.InvariantCulture)} Kills",
                    "prime-muted"));
            PrimeSelectedRow selectedRow = PrimeControlFactory.SelectedRow(content,
                row.IsCurrentPlayer);
            selectedRow.Classes.Add("prime-rank-row");
            if (row.IsCurrentPlayer)
                selectedRow.Classes.Add("prime-rank-current");
            PrimeAccessibility.SetName(selectedRow,
                $"Rank {rank.ToString(CultureInfo.InvariantCulture)}: {row.Entry.DisplayName}");
            cards.Children.Add(selectedRow);
        }
        if (state.CanLoadMore)
            cards.Children.Add(Button("Next page", () => context.Run(
                "Load next ranking page", () => context.Load(true)), primary: true));
        PrimeSectionPanel panel = PrimeControlFactory.SectionPanel(cards);
        panel.Classes.Add("prime-rankings-mobile");
        return panel;
    }

    private static Control BuildFilters(RankingsPresentationContext context)
    {
        RankingsState state = context.State;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            RowDefinitions = new RowDefinitions("Auto"),
            ColumnSpacing = 12,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        grid.Classes.Add("prime-rankings-filters");
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
            values.AddRange(PlayableHunterCatalog.All.Cast<object>());
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
        grid.SizeChanged += (_, args) => ApplyFilterLayout(grid,
            args.NewSize.Width);
        return grid;
    }

    internal static bool UsesMobileLayout(double width)
        => width > 0 && width <= CompactRankingsWidth;

    internal static string MetricValue(string metric, decimal score)
        => metric == "winPercentage"
            ? score.ToString("0.0%", CultureInfo.InvariantCulture)
            : score.ToString("#,0.##", CultureInfo.InvariantCulture);

    private static string PlayerLabel(PrimeLeaderboardRow row)
        => row.Entry.DisplayName + (row.IsCurrentPlayer ? " YOU" : "");

    internal static void ApplyFilterLayout(Grid grid, double width)
    {
        bool mobile = UsesMobileLayout(width);
        grid.ColumnDefinitions = new ColumnDefinitions(mobile ? "*" : "*,*");
        grid.RowDefinitions = new RowDefinitions(mobile ? "Auto,Auto" : "Auto");
        grid.ColumnSpacing = mobile ? 0 : 12;
        grid.RowSpacing = mobile ? 12 : 0;
        if (grid.Children.Count < 2) return;
        Control hunter = grid.Children[1];
        Grid.SetColumn(hunter, mobile ? 0 : 1);
        Grid.SetRow(hunter, mobile ? 1 : 0);
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

    private static TextBlock HiddenText(string value)
        => new() { Text = value, IsVisible = false, Classes = { "prime-muted" } };

    private static AvaloniaButton Button(string label, Action action, bool primary = false)
        => PrimeControlFactory.Button(label, action, primary);
}

/// <summary>
/// Keeps the desktop leaderboard table intact while replacing it with cards
/// when the route itself has no room for the fixed table columns.
/// </summary>
internal sealed class PrimeResponsiveLeaderboard : Grid
{
    private readonly Control _desktop;
    private readonly Control _mobile;

    public PrimeResponsiveLeaderboard(Control desktop, Control mobile)
    {
        _desktop = desktop ?? throw new ArgumentNullException(nameof(desktop));
        _mobile = mobile ?? throw new ArgumentNullException(nameof(mobile));
        Children.Add(_desktop);
        Children.Add(_mobile);
        ApplyLayout(0);
        SizeChanged += (_, args) => ApplyLayout(args.NewSize.Width);
    }

    internal void ApplyLayout(double width)
    {
        bool mobile = RankingsPresentation.UsesMobileLayout(width);
        _desktop.IsVisible = !mobile;
        _mobile.IsVisible = mobile;
    }
}
