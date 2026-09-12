using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Launcher.Theme;
using MphRead.Mods.Launcher.Resources;
using ProjectPrime.Server.Shared;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Pure presentation for the privacy-safe public directory. Network fetching
/// remains owned by <see cref="PlayController"/>.
/// </summary>
internal sealed class OnlinePlayersPanel : UserControl
{
    private const int PreviewCount = 8;

    public OnlinePlayersPanel(PresencePresentationState state, bool expanded,
        int requestedPage, Action showAll, Action close, Action<int> selectPage,
        Action refresh)
    {
        ArgumentNullException.ThrowIfNull(state);
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(Text(PrimeUiCopy.Play_OnlinePlayers_Title.ToUpperInvariant(),
            "prime-kicker"));

        if (state.State == PresenceLoadState.Loading && state.Players.Length == 0)
        {
            content.Children.Add(Text("Checking who’s online…", "prime-heading"));
            content.Children.Add(Text("Play remains available while status loads.",
                "prime-muted"));
        }
        else if (state.State == PresenceLoadState.Failed && state.Players.Length == 0)
        {
            content.Children.Add(Text("Status unavailable", "prime-heading"));
            content.Children.Add(Text("Lobby browsing and hosting are still available.",
                "prime-muted"));
            content.Children.Add(PrimeControlFactory.Button(PrimeUiCopy.Common_Retry, refresh,
                quiet: true));
        }
        else
        {
            content.Children.Add(Text(PopulationLabel(state), "prime-heading"));
            if (state.TotalOnline != state.VisibleOnline)
                content.Children.Add(Text(
                    $"{state.VisibleOnline} of {state.TotalOnline} players are visible.",
                    "prime-muted"));

            if (state.TotalOnline == 0)
                content.Children.Add(PrimeControlFactory.EmptyState("NO PLAYERS ONLINE",
                    PrimeUiCopy.Play_OnlinePlayers_Empty, compact: true));
            else if (state.Players.Length == 0)
                content.Children.Add(PrimeControlFactory.EmptyState("NO VISIBLE PLAYERS",
                    "Online players have chosen not to appear in this list.", compact: true));
            else
                AddPlayers(content, state, expanded, requestedPage, selectPage);

            if (!expanded && state.Players.Length > PreviewCount)
                content.Children.Add(PrimeControlFactory.Button(PrimeUiCopy.Common_ViewAll, showAll,
                    quiet: true));
            else if (expanded)
                content.Children.Add(PrimeControlFactory.Button("Show preview", close,
                    quiet: true));
        }

        var panel = PrimeControlFactory.SectionPanel(content);
        panel.Classes.Add("prime-online-players");
        PrimeAccessibility.SetName(panel, "Online players");
        Content = panel;
    }

    private static void AddPlayers(StackPanel content, PresencePresentationState state,
        bool expanded, int requestedPage, Action<int> selectPage)
    {
        int pageSize = expanded ? PresenceContract.PageSize : PreviewCount;
        int pageCount = expanded
            ? Math.Max(1, (state.Players.Length + pageSize - 1) / pageSize) : 1;
        int page = Math.Clamp(requestedPage, 0, pageCount - 1);
        foreach (PublicPresenceEntry entry in state.Players
            .Skip(page * pageSize).Take(pageSize))
        {
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 12
            };
            row.Children.Add(Text(entry.DisplayName, "prime-body"));
            TextBlock activity = Text(ActivityLabel(entry.Activity), "prime-muted");
            Grid.SetColumn(activity, 1);
            row.Children.Add(activity);
            content.Children.Add(row);
        }

        if (!expanded || pageCount <= 1) return;
        var pager = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8
        };
        Avalonia.Controls.Button previous = PrimeControlFactory.Button("Previous",
            () => selectPage(page - 1), quiet: true);
        previous.IsEnabled = page > 0;
        pager.Children.Add(previous);
        pager.Children.Add(Text($"Page {page + 1} of {pageCount}", "prime-muted"));
        Avalonia.Controls.Button next = PrimeControlFactory.Button("Next",
            () => selectPage(page + 1), quiet: true);
        next.IsEnabled = page + 1 < pageCount;
        pager.Children.Add(next);
        content.Children.Add(pager);
    }

    internal static string ActivityLabel(PlayerPresenceActivity activity) => activity switch
    {
        PlayerPresenceActivity.Online => "Online",
        PlayerPresenceActivity.InLobby => "In lobby",
        PlayerPresenceActivity.InMatch => "In match",
        _ => throw new ArgumentOutOfRangeException(nameof(activity))
    };

    private static string PopulationLabel(PresencePresentationState state)
        => state.TotalOnline == 1 ? "● 1 online" : $"● {state.TotalOnline} online";

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap,
            Classes = { style } };
}
