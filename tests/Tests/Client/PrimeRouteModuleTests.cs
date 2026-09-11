using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class PrimeRouteModuleTests
{
    [Fact]
    public void HunterModuleKeepsGuestCareerHonestAndArsenalDiscoverable()
    {
        Control view = HunterPresentation.Build(new HunterPresentationContext(
            SignedIn: false,
            HunterSection.Overview,
            HunterLicensePageState.Initial,
            Array.Empty<HunterDossier>(),
            CanLoadMore: false,
            _ => { },
            () => { },
            (_, _) => { },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(),
            (_, _) => throw new InvalidOperationException()));

        string text = TextOf(view);
        Assert.Contains("Sign in required", text, StringComparison.Ordinal);
        Assert.Contains("Arsenal", text, StringComparison.Ordinal);
        Assert.Contains("available to guests", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RankingsModuleProvidesLocalizedSignedOutState()
    {
        Control view = RankingsPresentation.Build(new RankingsPresentationContext(
            SignedIn: false,
            RankingsState.Initial,
            () => { },
            (_, _) => { },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask));

        string text = TextOf(view);
        Assert.Contains("Sign in required", text, StringComparison.Ordinal);
        Assert.Contains("highlighted row", text, StringComparison.Ordinal);
    }

    [Fact]
    public void RankingsModuleSwitchesFromDesktopTableToMobileCardsAt720()
    {
        PlayerId current = new(Guid.NewGuid());
        RankingsState state = RankingsState.Initial with
        {
            Metric = "winPercentage",
            Rows = ImmutableArray.Create(
                new PrimeLeaderboardRow(new LeaderboardEntry(new PlayerId(Guid.NewGuid()),
                    "Rival", 14, 3, 8, 12, 12, 0.666m), false),
                new PrimeLeaderboardRow(new LeaderboardEntry(current,
                    "JARRETT", 10, 4, 7, 11, 11, 0.636m), true))
        };
        Control view = RankingsPresentation.Build(new RankingsPresentationContext(
            SignedIn: true,
            state,
            () => { },
            (_, _) => { },
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask));
        PrimeResponsiveLeaderboard responsive = Assert.Single(
            Walk(view).OfType<PrimeResponsiveLeaderboard>());
        ScrollViewer desktop = Assert.Single(Walk(view).OfType<ScrollViewer>(),
            control => control.Classes.Contains("prime-rankings-desktop"));
        PrimeSectionPanel mobile = Assert.Single(Walk(view).OfType<PrimeSectionPanel>(),
            control => control.Classes.Contains("prime-rankings-mobile"));
        Grid filters = Assert.Single(Walk(view).OfType<Grid>(),
            control => control.Classes.Contains("prime-rankings-filters"));

        responsive.ApplyLayout(721);
        RankingsPresentation.ApplyFilterLayout(filters, 721);
        Assert.True(desktop.IsVisible);
        Assert.False(mobile.IsVisible);
        Assert.Equal(1, Grid.GetColumn(filters.Children[1]));
        Assert.Equal(0, Grid.GetRow(filters.Children[1]));
        Assert.Contains("#\nPlayer\nScore\nMatches\nKills", TextOf(desktop),
            StringComparison.Ordinal);

        responsive.ApplyLayout(720);
        RankingsPresentation.ApplyFilterLayout(filters, 720);
        Assert.False(desktop.IsVisible);
        Assert.True(mobile.IsVisible);
        Assert.Equal(0, Grid.GetColumn(filters.Children[1]));
        Assert.Equal(1, Grid.GetRow(filters.Children[1]));
        Assert.Contains("#2  JARRETT YOU", TextOf(mobile), StringComparison.Ordinal);
        Assert.Contains("63.6% Win Percentage", TextOf(mobile),
            StringComparison.Ordinal);
        Assert.Contains("11 Matches · 10 Kills", TextOf(mobile),
            StringComparison.Ordinal);
        Assert.Empty(Walk(mobile).OfType<ScrollViewer>());
    }

    [Theory]
    [InlineData("rp", "1499.5", "1,499.5")]
    [InlineData("kd", "2.345", "2.35")]
    [InlineData("winPercentage", "0.625", "62.5%")]
    public void RankingsMetricValuesUsePlayerFacingFormatting(string metric,
        string score, string expected)
        => Assert.Equal(expected, RankingsPresentation.MetricValue(metric,
            decimal.Parse(score, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void TheatreModuleShowsEmptyAndCapabilityStatesWithoutPaths()
    {
        Control view = TheatrePresentation.Build(new TheatrePresentationContext(
            TheatreState.Initial,
            SupportsImport: false,
            SupportsExport: false,
            SupportsRename: false,
            PendingDeleteId: null,
            (_, _) => { },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            _ => Task.CompletedTask,
            _ => { },
            _ => Task.CompletedTask,
            () => Task.CompletedTask,
            _ => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => { },
            () => { }));

        string text = TextOf(view);
        Assert.Contains("No replays yet", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Import replay is unavailable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Show File is unavailable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("/tmp/", text, StringComparison.Ordinal);
    }

    private static string TextOf(Control root)
        => String.Join('\n', Walk(root).OfType<TextBlock>().Select(text => text.Text));

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
                foreach (Control descendant in Walk(child))
                    yield return descendant;
        }
        else if (control is ContentControl content && content.Content is Control contentChild)
        {
            foreach (Control descendant in Walk(contentChild))
                yield return descendant;
        }
        else if (control is Decorator decorator && decorator.Child is Control decoratorChild)
        {
            foreach (Control descendant in Walk(decoratorChild))
                yield return descendant;
        }
    }
}
