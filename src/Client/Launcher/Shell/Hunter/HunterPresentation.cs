using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Accounts;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Mods.Launcher.Gui;

internal sealed record HunterPresentationContext(
    bool SignedIn,
    HunterSection Section,
    HunterLicensePageState State,
    IReadOnlyList<HunterDossier> Hunters,
    bool CanLoadMore,
    Action<HunterSection> SelectSection,
    Action OpenGateway,
    Action<string, Func<Task>> Run,
    Func<Task> LoadOlder,
    Func<Task> LoadAll,
    Func<Control> BuildArsenal,
    Func<IReadOnlyList<HunterDossier>, Control> BuildRoster,
    Func<HunterLicensePageState, IReadOnlyList<HunterDossier>, Control> BuildOverview);

/// <summary>
/// Hunter route composition. Career, preview, and account controllers remain
/// outside this view and are supplied only as immutable state and callbacks.
/// </summary>
internal static class HunterPresentation
{
    public static Control Build(HunterPresentationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var root = Stack(PrimeControlFactory.PageHeading("Hunter",
            "PROFILE · LOADOUT · CAREER",
            "Review your profile, Hunters, arsenal, career, and official matches."));
        root.Children.Add(BuildTabs(context));

        if (context.Section == HunterSection.Arsenal)
        {
            root.Children.Add(context.BuildArsenal());
            return root;
        }
        if (!context.SignedIn)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Sign in required", "prime-heading"),
                Text("Profile and official career records require an account. Arsenal data remains available to guests.",
                    "prime-muted"),
                Button("Open Gateway", context.OpenGateway, primary: true))));
            return root;
        }

        HunterLicensePageState state = context.State;
        if (state.Error != null)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text("Could not refresh your Hunter profile.", "prime-body"),
                new Expander { Header = "Details", Content = Text(state.Error, "prime-muted") },
                Button("Retry", () => context.SelectSection(context.Section), primary: true))));
        }
        if (state.License == null)
        {
            root.Children.Add(PrimeControlFactory.SectionPanel(Stack(
                Text(state.LoadingLicense ? "Loading Hunter profile…"
                    : "Your Hunter profile is ready to load.", "prime-muted"),
                Button("Retry", () => context.SelectSection(context.Section), primary: true))));
            return root;
        }

        switch (context.Section)
        {
            case HunterSection.Hunters:
                root.Children.Add(context.BuildRoster(context.Hunters));
                break;
            case HunterSection.Matches:
                root.Children.Add(BuildMatches(context));
                break;
            case HunterSection.Career when state.Career != null:
                root.Children.Add(PrimeControlFactory.SectionPanel(BuildCareer(state.Career)));
                break;
            default:
                BuildOverview(context, root);
                break;
        }
        return root;
    }

    public static Control BuildCareer(CareerSummary career)
    {
        CareerTotals totals = career.Totals;
        var card = Stack(Text("Career", "prime-heading"));
        if (totals.Matches == 0)
        {
            card.Children.Add(Text("No official matches recorded. Complete an eligible match to begin your career history.",
                "prime-muted"));
            return card;
        }
        var overview = new WrapPanel { Orientation = Orientation.Horizontal };
        overview.Children.Add(PrimeControlFactory.StatTile("Matches",
            totals.Matches.ToString(CultureInfo.InvariantCulture),
            $"{totals.Wins} wins · {totals.LossCount} losses · {totals.Ties} ties"));
        overview.Children.Add(PrimeControlFactory.StatTile("Rating",
            career.Rating.Points.ToString(CultureInfo.InvariantCulture),
            $"Tier {career.Rating.Tier} · {career.Rating.Title}"));
        overview.Children.Add(PrimeControlFactory.StatTile("K / D",
            FormatDecimal(totals.KillDeathRatio), $"{totals.Kills} / {totals.Deaths}"));
        overview.Children.Add(PrimeControlFactory.StatTile("Win ratio",
            FormatDecimal(totals.WinRatio), career.RatingStatus));
        card.Children.Add(overview);
        card.Children.Add(PrimeControlFactory.Divider());
        card.Children.Add(Text("Combat", "prime-heading"));
        card.Children.Add(Text($"{totals.Kills} kills · {totals.Deaths} deaths · "
            + $"{totals.Assists} assists · {totals.Damage} damage · "
            + $"{totals.HeadshotKills} headshots", "prime-body"));
        card.Children.Add(Text("Streaks", "prime-heading"));
        card.Children.Add(Text($"Longest kill streak {totals.LongestKillStreak} · "
            + $"longest win streak {totals.LongestWinStreak}", "prime-body"));
        card.Children.Add(Text("Favorites", "prime-heading"));
        card.Children.Add(Text($"Most played Hunter · {ChoiceText(career.MostPlayedHunter)}\n"
            + $"Best Hunter · {ChoiceText(career.BestHunter)}\n"
            + $"Map · {ChoiceText(career.FavoriteMap)}\n"
            + $"Mode · {ChoiceText(career.FavoriteMode)}\n"
            + $"Weapon · {ChoiceText(career.FavoriteWeapon)}", "prime-body"));
        return card;
    }

    private static Control BuildTabs(HunterPresentationContext context)
    {
        var tabs = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (HunterSection section in Enum.GetValues<HunterSection>())
        {
            HunterSection captured = section;
            tabs.Children.Add(Button(section.ToString(), () => context.SelectSection(captured),
                quiet: context.Section != section));
        }
        return tabs;
    }

    private static Control BuildMatches(HunterPresentationContext context)
    {
        HunterLicensePageState state = context.State;
        var matches = Stack(Text("Match history", "prime-heading"));
        if (state.LoadingMatches && !state.HasMatches)
            matches.Children.Add(Text("Loading official matches…", "prime-muted"));
        else if (!state.HasMatches)
            matches.Children.Add(Text("No official matches recorded. Complete an eligible match to begin your career history.",
                "prime-muted"));
        foreach (MatchHistoryEntry match in state.Matches)
            matches.Children.Add(BuildHistoryRow(match));
        if (context.CanLoadMore)
            matches.Children.Add(Button("Load older matches", () => context.Run(
                "Load older matches", context.LoadOlder), primary: true));
        return PrimeControlFactory.SectionPanel(matches);
    }

    private static void BuildOverview(HunterPresentationContext context, StackPanel root)
    {
        HunterLicensePageState state = context.State;
        if (context.Hunters.Count > 0)
            root.Children.Add(context.BuildOverview(state, context.Hunters));
        else
            root.Children.Add(PrimeControlFactory.SectionPanel(new PrimeEmptyState(
                "No Hunter roster is available for this profile.")));
        var recent = Stack(Text("Recent matches", "prime-heading"));
        if (state.LoadingMatches)
            recent.Children.Add(Text("Loading recent official matches…", "prime-muted"));
        else if (!state.HasMatches)
            recent.Children.Add(Text("No official matches recorded.", "prime-muted"));
        else
        {
            foreach (MatchHistoryEntry match in state.Matches.Take(5))
                recent.Children.Add(BuildHistoryRow(match));
            recent.Children.Add(Button("View all matches", () => context.Run(
                "Load matches", context.LoadAll)));
        }
        root.Children.Add(PrimeControlFactory.SectionPanel(recent));
    }

    private static Control BuildHistoryRow(MatchHistoryEntry match)
    {
        PrimeMatchHistoryPresentation row = PrimeMatchHistoryPresentation.From(match);
        return PrimeControlFactory.SectionPanel(Stack(
            Text(row.Outcome, "prime-heading"),
            Text(row.Mission, "prime-body"),
            Text(row.CombatLine, "prime-muted"),
            Text(row.RatingLine, "prime-muted"),
            Text(row.DateLine, "prime-label")));
    }

    private static string FormatDecimal(decimal? value)
        => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

    private static string ChoiceText(CareerChoice? value)
        => value?.Key is { Length: > 0 } key ? key : "—";

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
