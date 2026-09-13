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
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        heading.Children.Add(Text(PrimeUiCopy.Play_OnlinePlayers_Title.ToUpperInvariant(),
            "prime-kicker"));
        if (state.State is PresenceLoadState.Ready or PresenceLoadState.Failed
            && state.TotalOnline > 0)
        {
            TextBlock count = Text(PopulationLabel(state), "prime-online-badge");
            PrimeAccessibility.SetStatus(count, PopulationLabel(state),
                state.State == PresenceLoadState.Failed
                    ? PrimeStatusKind.Warning : PrimeStatusKind.Success);
            heading.Children.Add(count);
            Grid.SetColumn(count, 1);
        }
        content.Children.Add(heading);

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
            if (state.State == PresenceLoadState.Loading && state.TotalOnline > 0)
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
            {
                if (state.State == PresenceLoadState.Loading)
                    content.Children.Add(Text("Refreshing player status…", "prime-muted"));
                else if (state.State == PresenceLoadState.Failed)
                {
                    content.Children.Add(Text("Player list unavailable. Showing the last known list.",
                        "prime-muted"));
                    content.Children.Add(PrimeControlFactory.Button(PrimeUiCopy.Common_Retry,
                        refresh, quiet: true));
                }
                AddPlayers(content, state, expanded, requestedPage, selectPage);
            }

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
        PrimeAccessibility.SetDescription(panel,
            "Public players grouped by current activity. Only display names and activity are shown.");
        Content = panel;
    }

    private static void AddPlayers(StackPanel content, PresencePresentationState state,
        bool expanded, int requestedPage, Action<int> selectPage)
    {
        int pageSize = expanded ? PresenceContract.PageSize : PreviewCount;
        int pageCount = expanded
            ? Math.Max(1, (state.Players.Length + pageSize - 1) / pageSize) : 1;
        int page = Math.Clamp(requestedPage, 0, pageCount - 1);
        PublicPresenceEntry[] entries = state.Players
            .Skip(page * pageSize).Take(pageSize).ToArray();
        bool firstGroup = true;
        foreach ((PlayerPresenceActivity activity, string title, string glyph) in ActivityGroups)
        {
            PublicPresenceEntry[] group = entries
                .Where(entry => entry.Activity == activity).ToArray();
            if (group.Length == 0) continue;
            if (!firstGroup) content.Children.Add(new PrimeDivider());
            firstGroup = false;
            content.Children.Add(Text(title, "prime-section-heading"));
            for (int index = 0; index < group.Length; index++)
            {
                PublicPresenceEntry entry = group[index];
                var row = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
                    ColumnSpacing = 10,
                    MinHeight = 44
                };
                row.Classes.Add("prime-online-player-row");
                TextBlock marker = Text(glyph, "prime-presence-glyph");
                marker.MinWidth = 18;
                marker.HorizontalAlignment = HorizontalAlignment.Center;
                row.Children.Add(marker);
                TextBlock playerName = Text(entry.DisplayName, "prime-body");
                Grid.SetColumn(playerName, 1);
                row.Children.Add(playerName);
                TextBlock activityText = Text(ActivityLabel(entry.Activity), "prime-muted");
                Grid.SetColumn(activityText, 2);
                row.Children.Add(activityText);
                PrimeAccessibility.SetName(row,
                    $"Online player {entry.DisplayName} {index + 1}");
                string region = String.IsNullOrWhiteSpace(entry.Region)
                    ? "" : $" Region {entry.Region}.";
                PrimeAccessibility.SetDescription(row,
                    $"{ActivityLabel(entry.Activity)}.{region}");
                content.Children.Add(row);
                if (index + 1 < group.Length)
                    content.Children.Add(new PrimeDivider());
            }
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

    internal static string ActivityGlyph(PlayerPresenceActivity activity) => activity switch
    {
        PlayerPresenceActivity.Online => "●",
        PlayerPresenceActivity.InLobby => "◉",
        PlayerPresenceActivity.InMatch => "◆",
        _ => throw new ArgumentOutOfRangeException(nameof(activity))
    };

    private static readonly (PlayerPresenceActivity Activity, string Title, string Glyph)[]
        ActivityGroups =
        [
            (PlayerPresenceActivity.Online, "AVAILABLE", "●"),
            (PlayerPresenceActivity.InLobby, "IN LOBBY", "◉"),
            (PlayerPresenceActivity.InMatch, "PLAYING", "◆")
        ];

    private static string PopulationLabel(PresencePresentationState state)
        => state.TotalOnline == 1 ? "● 1 online" : $"● {state.TotalOnline} online";

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap,
            Classes = { style } };
}
