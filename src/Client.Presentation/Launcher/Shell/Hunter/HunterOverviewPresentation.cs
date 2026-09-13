using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Presentation;

namespace MphRead.Mods.Launcher.Gui;

/// <summary>
/// Concise Hunter landing summary. Detailed combat and preference analytics
/// intentionally remain in the Career section.
/// </summary>
internal static class HunterOverviewPresentation
{
    public static HunterDossier SelectProfile(IReadOnlyList<HunterDossier> hunters,
        Hunter selectedHunter)
    {
        ArgumentNullException.ThrowIfNull(hunters);
        if (hunters.Count == 0)
            throw new ArgumentException("At least one Hunter is required.", nameof(hunters));
        return hunters.FirstOrDefault(dossier => dossier.IsFavorite)
            ?? hunters.FirstOrDefault(dossier => dossier.Hunter == selectedHunter)
            ?? hunters[0];
    }

    public static Control BuildSummary(HunterLicensePageState state,
        IReadOnlyList<HunterDossier> hunters, Hunter selectedHunter,
        Func<Hunter, Control> buildPreview, Action<string>? updateDisplayName = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(hunters);
        ArgumentNullException.ThrowIfNull(buildPreview);
        if (state.License == null)
            throw new ArgumentException("A loaded Hunter license is required.", nameof(state));

        HunterDossier profile = SelectProfile(hunters, selectedHunter);
        var hero = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("210,*"),
            ColumnSpacing = 20
        };
        Control preview = buildPreview(profile.Hunter);
        hero.Children.Add(preview);

        var identity = Stack(
            Text(state.License.DisplayName.ToUpperInvariant(), "prime-title"),
            Text(state.License.Title, "prime-heading"),
            Text($"{state.License.Points.ToString("N0", CultureInfo.InvariantCulture)} RP",
                "prime-body"),
            PrimeControlFactory.Divider(),
            Text("FAVORITE HUNTER", "prime-label"),
            Text(PrimeGameText.HunterLabel(profile.Hunter), "prime-heading"));
        hero.Children.Add(identity);
        Grid.SetColumn(identity, 1);
        MakeResponsive(hero, preview, identity);

        CareerTotals? totals = state.Career?.Totals;
        var stats = new WrapPanel { Orientation = Orientation.Horizontal };
        stats.Children.Add(PrimeControlFactory.StatTile("MATCHES",
            totals?.Matches.ToString("N0", CultureInfo.InvariantCulture) ?? "—"));
        stats.Children.Add(PrimeControlFactory.StatTile("WIN RATE",
            FormatPercentage(totals?.WinRatio)));
        stats.Children.Add(PrimeControlFactory.StatTile("K / D",
            FormatDecimal(totals?.KillDeathRatio)));
        stats.Children.Add(PrimeControlFactory.StatTile("RP",
            state.License.Points.ToString("N0", CultureInfo.InvariantCulture)));

        var summary = Stack(hero, stats);
        if (updateDisplayName != null)
            summary.Children.Add(BuildProfileEditor(state.License.DisplayName,
                updateDisplayName));
        return PrimeControlFactory.SectionPanel(summary);
    }

    private static Control BuildProfileEditor(string currentName,
        Action<string> updateDisplayName)
    {
        var displayName = new TextBox
        {
            Text = currentName,
            Watermark = "Display name",
            Classes = { "prime-input" }
        };
        var save = PrimeControlFactory.Button("Save Display Name",
            () => updateDisplayName(displayName.Text ?? ""));
        return new Expander
        {
            Header = "Edit Profile",
            IsExpanded = false,
            Content = Stack(displayName, save)
        };
    }

    private static void MakeResponsive(Grid hero, Control preview, Control identity)
    {
        hero.SizeChanged += (_, e) =>
        {
            bool compact = e.NewSize.Width > 0 && e.NewSize.Width < 720;
            hero.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "210,*");
            hero.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
            hero.ColumnSpacing = compact ? 0 : 20;
            hero.RowSpacing = compact ? 12 : 0;
            Grid.SetColumn(preview, 0);
            Grid.SetColumn(identity, compact ? 0 : 1);
            Grid.SetRow(preview, 0);
            Grid.SetRow(identity, compact ? 1 : 0);
        };
    }

    private static string FormatDecimal(decimal? value)
        => value?.ToString("0.00", CultureInfo.InvariantCulture) ?? "—";

    private static string FormatPercentage(decimal? value)
        => value?.ToString("0.0%", CultureInfo.InvariantCulture) ?? "—";

    private static StackPanel Stack(params Control[] controls)
    {
        var stack = new StackPanel { Spacing = 12 };
        foreach (Control control in controls) stack.Children.Add(control);
        return stack;
    }

    private static TextBlock Text(string value, string style)
        => new() { Text = value, TextWrapping = TextWrapping.Wrap, Classes = { style } };
}
