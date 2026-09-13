using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;

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
        CareerTotals? totals = state.Career?.Totals;
        var hero = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("300,*"),
            ColumnSpacing = 24
        };
        Control preview = buildPreview(profile.Hunter);
        ConfigureLicensePortrait(preview);
        hero.Children.Add(preview);

        var identity = Stack(
            Text("HUNTER LICENSE", "prime-kicker"),
            Text(state.License.DisplayName.ToUpperInvariant(), "prime-hero"),
            Text($"{state.License.Title} · "
                + $"{state.License.Points.ToString("N0", CultureInfo.InvariantCulture)} RP",
                "prime-subtitle"),
            PrimeControlFactory.Divider(),
            Text("FAVORITE HUNTER", "prime-label"),
            Text(PrimeGameText.HunterLabel(profile.Hunter), "prime-section-heading"),
            Text($"Affinity weapon · {profile.AffinityWeapon}", "prime-muted"),
            PrimeControlFactory.Divider(),
            Text("OFFICIAL CAREER", "prime-label"),
            BuildCareerStats(totals));
        if (updateDisplayName != null)
            identity.Children.Add(BuildProfileEditor(state.License.DisplayName,
                updateDisplayName));
        hero.Children.Add(identity);
        Grid.SetColumn(identity, 1);
        MakeResponsive(hero, preview, identity);

        var license = PrimeControlFactory.SectionPanel(hero);
        license.Classes.Add("prime-hunter-license");
        return license;
    }

    /// <summary>
    /// Gives the selected Hunter a durable identity block before the overview
    /// metrics. The route must still read clearly when preview art is absent,
    /// so identity is never encoded only by the preview surface.
    /// </summary>
    internal static Control BuildIdentityHeader(HunterDossier profile,
        HunterLicense? license = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        string hunter = PrimeGameText.HunterLabel(profile.Hunter);
        var content = Stack(
            Text("HUNTER PROFILE", "prime-kicker"),
            Text(hunter, "prime-hero"),
            Text("Bounty Hunter", "prime-subtitle"));
        if (license is { } current)
        {
            content.Children.Add(Text(
                $"{current.DisplayName} · {current.Points.ToString("N0", CultureInfo.InvariantCulture)} RP",
                "prime-muted"));
        }
        var header = PrimeControlFactory.HeroCard(content);
        header.Classes.Add("prime-hunter-identity");
        PrimeAccessibility.SetName(header, $"Hunter profile: {hunter}");
        PrimeAccessibility.SetDescription(header,
            $"Selected Hunter {hunter}. Bounty Hunter profile.");
        return header;
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

    private static PrimeStatRail BuildCareerStats(CareerTotals? totals)
    {
        string Number(long? value)
            => value?.ToString("N0", CultureInfo.InvariantCulture) ?? "—";

        var stats = PrimeControlFactory.StatRail(new[]
        {
            ("MATCHES", Number(totals?.Matches), (string?)null),
            ("RECORD", totals == null ? "—" : $"{totals.Wins}–{totals.LossCount}–{totals.Ties}",
                totals == null ? null : "W · L · T"),
            ("WIN RATE", FormatPercentage(totals?.WinRatio), (string?)null),
            ("K / D", FormatDecimal(totals?.KillDeathRatio), (string?)null),
            ("KILLS", Number(totals?.Kills), (string?)null),
            ("ASSISTS", Number(totals?.Assists), (string?)null),
            ("DAMAGE", Number(totals?.Damage), (string?)null),
            ("HEADSHOTS", Number(totals?.HeadshotKills), (string?)null),
            ("KILL STREAK", Number(totals?.LongestKillStreak), (string?)null),
            ("WIN STREAK", Number(totals?.LongestWinStreak), (string?)null)
        });
        stats.Classes.Add("prime-hunter-license-stats");
        foreach (PrimeStatTile tile in stats.Tiles)
            tile.MinWidth = 94;
        return stats;
    }

    private static void ConfigureLicensePortrait(Control preview)
    {
        preview.Classes.Add("prime-hunter-license-portrait");
        Image? image = FindImage(preview);
        if (image == null) return;

        // Generated previews are square, full-body stills. The license uses a
        // tighter shoulder-up crop so the selected Hunter reads at a glance.
        image.Stretch = Stretch.UniformToFill;
        image.RenderTransformOrigin = new RelativePoint(.5, .38,
            RelativeUnit.Relative);
        image.RenderTransform = new ScaleTransform(1.7, 1.7);
    }

    private static Image? FindImage(Control control)
    {
        if (control is Image image) return image;
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
            {
                Image? found = FindImage(child);
                if (found != null) return found;
            }
        }
        else if (control is ContentControl content && content.Content is Control child)
        {
            return FindImage(child);
        }
        else if (control is Decorator decorator
            && decorator.Child is Control decoratorChild)
        {
            return FindImage(decoratorChild);
        }
        return null;
    }

    private static void MakeResponsive(Grid hero, Control preview, Control identity)
    {
        hero.SizeChanged += (_, e) =>
        {
            bool compact = e.NewSize.Width > 0 && e.NewSize.Width < 720;
            hero.ColumnDefinitions = new ColumnDefinitions(compact ? "*" : "300,*");
            hero.RowDefinitions = new RowDefinitions(compact ? "Auto,Auto" : "Auto");
            hero.ColumnSpacing = compact ? 0 : 20;
            hero.RowSpacing = compact ? 12 : 0;
            if (preview is PrimePreviewStage)
                preview.Height = compact ? 260 : 360;
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
