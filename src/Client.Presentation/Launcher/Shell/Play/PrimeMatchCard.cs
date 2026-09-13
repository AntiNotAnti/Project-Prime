using System;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// A bounded, authoritative lobby-list card.  It intentionally accepts only
/// <see cref="LobbyListEntry"/> data: there is no fabricated ping, trust score,
/// or region value hiding in this presentation component.
/// </summary>
internal sealed class PrimeMatchCard : Border
{
    public PrimeMatchCard(LobbyListEntry entry, Action? join, Action? waitlist, Action? spectate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Classes.Add("prime-card");
        Padding = new Thickness(16);
        Margin = new Thickness(0, 0, 0, 10);
        string displayName = string.IsNullOrWhiteSpace(entry.Name) ? "Unnamed lobby" : entry.Name;
        PrimeAccessibility.SetName(this, $"Lobby: {displayName}");
        PrimeAccessibility.SetDescription(this,
            $"{PrimeGameText.ModeLabel(entry.Mode)} lobby on "
            + $"{PrimeGameText.MapName(entry.MapKey)}.");

        int openPlayers = MatchBrowserFiltering.OpenPlayerSlots(entry);
        var body = new StackPanel { Spacing = 8 };

        var heading = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*")
        };
        var title = new StackPanel { Spacing = 2 };
        title.Children.Add(new TextBlock
        {
            Text = displayName,
            Classes = { "prime-card-heading" },
            TextWrapping = TextWrapping.Wrap
        });
        title.Children.Add(new TextBlock
        {
            Text = PrimeGameText.MapName(entry.MapKey),
            Classes = { "prime-body" },
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(title);
        body.Children.Add(heading);

        var modeLine = new WrapPanel { Orientation = Orientation.Horizontal };
        modeLine.Children.Add(new PrimeStatusChip(PrimeGameText.ModeLabel(entry.Mode),
            GuiTheme.BrandBrush));
        modeLine.Children.Add(new PrimeStatusChip(
            $"{entry.Players + entry.BotCount}/{entry.PlayerLimit} players"
            + (openPlayers > 0 ? $" · {openPlayers} open" : " · Full"),
            GuiTheme.TextBrush));
        modeLine.Children.Add(new PrimeStatusChip(
            PrimeGameText.LobbyPhaseLabel(entry.Phase),
            entry.Phase == LobbyPhase.Open
                ? GuiTheme.SuccessBrush : GuiTheme.WarningBrush));
        body.Children.Add(modeLine);

        var rules = new WrapPanel { Orientation = Orientation.Horizontal };
        rules.Children.Add(Metric("TIME",
            LobbyRuleDefaults.Time(entry.Mode, entry.TimeLimitSeconds)));
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(entry.Mode);
        if (applicability.ScoreGoal)
            rules.Children.Add(Metric("SCORE",
                LobbyRuleDefaults.Score(entry.Mode, entry.PointGoal)));
        else if (applicability.StartingLives)
            rules.Children.Add(Metric("LIVES",
                LobbyRuleDefaults.Lives(entry.Mode, entry.PointGoal)));
        else if (applicability.ObjectiveTimeGoal)
            rules.Children.Add(Metric("OBJECTIVE",
                LobbyRuleDefaults.ObjectiveTime(entry.Mode, entry.ObjectiveTimeGoalSeconds)));
        rules.Children.Add(Metric("OBSERVERS", $"{entry.Observers}/{entry.ObserverLimit}"));
        if (entry.BotCount > 0)
        {
            rules.Children.Add(Metric("BOTS", entry.BotCount.ToString(CultureInfo.InvariantCulture)));
            rules.Children.Add(Metric("BOT DIFFICULTY",
                PrimeGameText.BotDifficultyLabel(entry.BotDifficulty)));
        }
        if (entry.WaitlistCount > 0)
            rules.Children.Add(Metric("WAITLIST", entry.WaitlistCount.ToString(CultureInfo.InvariantCulture)));
        body.Children.Add(rules);
        body.Children.Add(new TextBlock
        {
            Text = $"Seat policy · {PrimeGameText.SeatPolicyLabel(entry.SeatPolicy)}",
            Classes = { "prime-muted" },
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        AddAction(actions, "Join", join, primary: true);
        AddAction(actions, "Waitlist", waitlist);
        AddAction(actions, "Spectate", spectate);
        if (actions.Children.Count == 0)
            actions.Children.Add(new TextBlock
            {
                Text = entry.Phase == LobbyPhase.Open
                    ? "This lobby is full."
                    : "This lobby is not accepting new players.",
                Classes = { "prime-muted" }
            });
        body.Children.Add(actions);

        Child = body;
    }

    private static void AddAction(Panel panel, string label, Action? action, bool primary = false)
    {
        if (action == null) return;
        var button = PrimeControlFactory.Button(label, action, primary: primary);
        button.MinHeight = 44;
        button.MinWidth = label == "Waitlist" ? 110 : 92;
        PrimeAccessibility.SetName(button, label switch
        {
            "Join" => "Join lobby",
            "Waitlist" => "Join lobby player queue",
            "Spectate" => "Spectate lobby",
            _ => label
        });
        panel.Children.Add(button);
    }

    private static PrimeStatTile Metric(string label, string value)
        => PrimeControlFactory.StatTile(label, value);

}

/// <summary>
/// A compact landing-page lobby row. The browser keeps the detailed card above;
/// Play only needs the identity, mode, capacity, and one obvious next action.
/// </summary>
internal sealed class PrimeCompactLobbyRow : Border
{
    public PrimeCompactLobbyRow(LobbyListEntry entry, Action? join,
        Action? waitlist, Action? spectate)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Classes.Add("prime-compact-lobby-row");
        Padding = new Thickness(12, 10);
        Margin = new Thickness(0, 0, 0, 6);

        string displayName = string.IsNullOrWhiteSpace(entry.Name)
            ? "Unnamed lobby" : entry.Name.Trim();
        int openPlayers = MatchBrowserFiltering.OpenPlayerSlots(entry);
        int occupied = entry.Players + entry.BotCount;
        string capacity = $"{occupied}/{entry.PlayerLimit} players"
            + (openPlayers > 0 ? $" · {openPlayers} open" : " · Full");
        string detail = $"{PrimeGameText.MapName(entry.MapKey)} · "
            + PrimeGameText.ModeLabel(entry.Mode);
        PrimeAccessibility.SetName(this, $"Lobby: {displayName}");
        PrimeAccessibility.SetDescription(this, $"{detail}. {capacity}.");

        var text = new StackPanel { Spacing = 2 };
        text.Children.Add(new TextBlock
        {
            Text = displayName,
            Classes = { "prime-card-heading" },
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = detail,
            Classes = { "prime-muted" },
            TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock
        {
            Text = capacity,
            Classes = { "prime-telemetry" },
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        AddAction(actions, "Join", join, primary: true);
        AddAction(actions, "Waitlist", waitlist);
        AddAction(actions, "Spectate", spectate);
        if (actions.Children.Count == 0)
            actions.Children.Add(new TextBlock
            {
                Text = entry.Phase == LobbyPhase.Open
                    ? "Full" : "Not accepting players",
                Classes = { "prime-muted" }
            });

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            ColumnSpacing = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(text);
        row.Children.Add(actions);
        Grid.SetColumn(actions, 1);
        Child = row;
    }

    private static void AddAction(Panel panel, string label, Action? action,
        bool primary = false)
    {
        if (action == null) return;
        var button = PrimeControlFactory.Button(label, action, primary: primary,
            quiet: !primary);
        button.MinHeight = 44;
        button.MinWidth = label == "Waitlist" ? 100 : 86;
        PrimeAccessibility.SetName(button, label switch
        {
            "Join" => "Join lobby",
            "Waitlist" => "Join lobby player queue",
            "Spectate" => "Spectate lobby",
            _ => label
        });
        panel.Children.Add(button);
    }
}
