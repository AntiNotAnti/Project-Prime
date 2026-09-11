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
/// A bounded, authoritative match-list card.  It intentionally accepts only
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
        string displayName = string.IsNullOrWhiteSpace(entry.Name) ? "Unnamed match" : entry.Name;
        PrimeAccessibility.SetName(this, $"Match: {displayName}");
        PrimeAccessibility.SetDescription(this,
            $"{PrimeGameText.ModeLabel(entry.Mode)} match on "
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
            Classes = { "prime-heading" },
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
        rules.Children.Add(Metric("TIME", entry.TimeLimitSeconds is { } seconds
            ? FormatDuration(seconds) : "Default"));
        LobbyRuleApplicability applicability = LobbyRuleApplicability.For(entry.Mode);
        if (applicability.ScoreGoal)
            rules.Children.Add(Metric("SCORE", entry.PointGoal?.ToString(CultureInfo.InvariantCulture) ?? "Default"));
        else if (applicability.StartingLives)
            rules.Children.Add(Metric("LIVES", entry.PointGoal?.ToString(CultureInfo.InvariantCulture) ?? "Default"));
        else if (applicability.ObjectiveTimeGoal)
            rules.Children.Add(Metric("OBJECTIVE", entry.ObjectiveTimeGoalSeconds is { } objective
                ? FormatDuration(objective) : "Default"));
        rules.Children.Add(Metric("OBSERVERS", $"{entry.Observers}/{entry.ObserverLimit}"));
        if (entry.BotCount > 0)
            rules.Children.Add(Metric("BOTS", entry.BotCount.ToString(CultureInfo.InvariantCulture)));
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
                    ? "This match is full."
                    : "This match is not accepting new players.",
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
            "Join" => "Join match",
            "Waitlist" => "Join match player queue",
            "Spectate" => "Spectate match",
            _ => label
        });
        panel.Children.Add(button);
    }

    private static PrimeStatTile Metric(string label, string value)
        => PrimeControlFactory.StatTile(label, value);

    private static string FormatDuration(int seconds)
        => TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
}
