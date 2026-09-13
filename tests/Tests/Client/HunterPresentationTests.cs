using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using MphRead;
using MphRead.Identity;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class HunterPresentationTests
{
    private static readonly HunterLicense License = new(
        new MphRead.Identity.PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111")),
        "Jarrett", (int)Hunter.Samus,
        new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
        Points: 2435, Tier: 3, Title: "Gold III", NextThreshold: 2500,
        LastOfficialDelta: 22);

    [Fact]
    public void TabStripExposesOneExplicitSelectionAndInvokesTheRequestedTab()
    {
        string? selected = null;
        var strip = new PrimeTabStrip(new[]
        {
            new PrimeTabItem("Overview", false, () => selected = "Overview"),
            new PrimeTabItem("Career", true, () => selected = "Career"),
            new PrimeTabItem("Matches", false, () => selected = "Matches")
        });

        Assert.Equal("Career", strip.SelectedTab.Label);
        Assert.True(strip.SelectedTab.IsSelected);
        Assert.Contains("prime-selected", strip.SelectedTab.Classes);
        Assert.Single(strip.Tabs, tab => tab.IsSelected);

        strip.Tabs.Single(tab => tab.Label == "Matches").Invoke();
        Assert.Equal("Matches", selected);
    }

    [Fact]
    public void TabStripRejectsMissingOrAmbiguousSelection()
    {
        Assert.Throws<ArgumentException>(() => new PrimeTabStrip(new[]
        {
            new PrimeTabItem("Overview", false, () => { })
        }));
        Assert.Throws<ArgumentException>(() => new PrimeTabStrip(new[]
        {
            new PrimeTabItem("Overview", true, () => { }),
            new PrimeTabItem("Career", true, () => { })
        }));
    }

    [Theory]
    [InlineData(CareerOutcome.FinishedWin, "prime-status-success")]
    [InlineData(CareerOutcome.FinishedLoss, "prime-status-error")]
    [InlineData(CareerOutcome.Tie, "prime-status-warning")]
    [InlineData(CareerOutcome.Forfeit, "prime-status-warning")]
    [InlineData(CareerOutcome.DepartedGraceExpired, "prime-status-warning")]
    [InlineData(CareerOutcome.NoContest, "prime-status-muted")]
    public void HistoryRowUsesOutcomeEdgeSemanticsWithoutNestedCards(
        CareerOutcome outcome, string expectedClass)
    {
        var row = new PrimeHistoryRow(History(outcome));

        Assert.Contains("prime-history-row", row.Classes);
        Assert.Contains(expectedClass, row.Classes);
        Assert.DoesNotContain(Walk(row), control => control is PrimeSectionPanel);
        Assert.Contains(row.Presentation.Mission, TextOf(row), StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewLicenseUsesTheAvailableCareerStatsWithoutDuplicatingCareerNarrative()
    {
        HunterLicensePageState state = State();
        IReadOnlyList<HunterDossier> hunters = Hunters();
        Control summary = HunterOverviewPresentation.BuildSummary(state, hunters,
            Hunter.Kanden, _ => new Border());

        string text = TextOf(summary);
        Assert.Contains("JARRETT", text, StringComparison.Ordinal);
        Assert.Contains("Gold III", text, StringComparison.Ordinal);
        Assert.Contains("2,435 RP", text, StringComparison.Ordinal);
        Assert.Contains("MATCHES", text, StringComparison.Ordinal);
        Assert.Contains("WIN RATE", text, StringComparison.Ordinal);
        Assert.Contains("K / D", text, StringComparison.Ordinal);
        Assert.Contains("RECORD", text, StringComparison.Ordinal);
        Assert.Contains("72–52–4", text, StringComparison.Ordinal);
        Assert.Contains("KILLS", text, StringComparison.Ordinal);
        Assert.Contains("744", text, StringComparison.Ordinal);
        Assert.Contains("ASSISTS", text, StringComparison.Ordinal);
        Assert.Contains("210", text, StringComparison.Ordinal);
        Assert.Contains("DAMAGE", text, StringComparison.Ordinal);
        Assert.Contains("98,440", text, StringComparison.Ordinal);
        Assert.Contains("HEADSHOTS", text, StringComparison.Ordinal);
        Assert.Contains("KILL STREAK", text, StringComparison.Ordinal);
        Assert.Contains("WIN STREAK", text, StringComparison.Ordinal);
        Assert.Contains("FAVORITE HUNTER", text, StringComparison.Ordinal);
        Assert.Contains("Samus", text, StringComparison.Ordinal);
        Assert.Equal(10, Walk(summary).OfType<PrimeStatTile>().Count());
        Assert.Empty(Walk(summary).OfType<PrimeHeroCard>());
        Assert.DoesNotContain("Combat", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Streaks", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Longest", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Joined", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewLicenseTurnsTheGeneratedStillIntoATightHunterPortrait()
    {
        var image = new Image { Stretch = Stretch.Uniform };
        PrimePreviewStage preview = PrimeControlFactory.PreviewStage(image);

        Control summary = HunterOverviewPresentation.BuildSummary(State(), Hunters(),
            Hunter.Samus, _ => preview);

        Assert.Contains("prime-hunter-license-portrait", preview.Classes);
        Assert.Equal(Stretch.UniformToFill, image.Stretch);
        var zoom = Assert.IsType<ScaleTransform>(image.RenderTransform);
        Assert.Equal(1.7, zoom.ScaleX);
        Assert.Equal(1.7, zoom.ScaleY);
        Assert.Contains(preview, Walk(summary));
    }

    [Fact]
    public void OverviewIdentityNamesSelectedHunterAndBountyRoleWithoutPreviewArt()
    {
        Control identity = HunterOverviewPresentation.BuildIdentityHeader(
            Hunters().Single(hunter => hunter.Hunter == Hunter.Kanden));

        Assert.Contains("HUNTER PROFILE", TextOf(identity), StringComparison.Ordinal);
        Assert.Contains("Kanden", TextOf(identity), StringComparison.Ordinal);
        Assert.Contains("Bounty Hunter", TextOf(identity), StringComparison.Ordinal);
        Assert.Contains("prime-hunter-identity", identity.Classes);
        Assert.DoesNotContain("Preview unavailable", TextOf(identity),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileEditingRemainsAvailableWithoutExpandingTheOverview()
    {
        string? saved = null;
        Control summary = HunterOverviewPresentation.BuildSummary(State(), Hunters(),
            Hunter.Samus, _ => new Border(), value => saved = value);

        Expander editor = Assert.Single(Walk(summary).OfType<Expander>());
        Assert.False(editor.IsExpanded);
        TextBox input = Assert.Single(Walk(editor).OfType<TextBox>());
        input.Text = "New Hunter";
        Walk(editor).OfType<PrimeButton>().Single().Invoke();
        Assert.Equal("New Hunter", saved);
    }

    [Fact]
    public void OverviewOffersCareerAndMatchNavigationAroundLightweightRows()
    {
        HunterSection selected = HunterSection.Overview;
        Control view = HunterPresentation.Build(new HunterPresentationContext(
            SignedIn: true,
            HunterSection.Overview,
            State(),
            Hunters(),
            CanLoadMore: false,
            section => selected = section,
            () => { },
            (_, _) => { },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => new Border(),
            _ => new Border(),
            (state, hunters) => HunterOverviewPresentation.BuildSummary(state,
                hunters, Hunter.Samus, _ => new Border())));

        string text = TextOf(view);
        Assert.Contains("Recent matches", text, StringComparison.Ordinal);
        Assert.Contains("View Career", ButtonLabels(view));
        Assert.Contains("View All Matches", ButtonLabels(view));
        Assert.All(Walk(view).OfType<PrimeHistoryRow>(), row =>
            Assert.DoesNotContain(Walk(row), child => child is PrimeSectionPanel));

        Walk(view).OfType<PrimeButton>()
            .Single(button => String.Equals(button.Content as string, "View Career",
                StringComparison.Ordinal)).Invoke();
        Assert.Equal(HunterSection.Career, selected);
    }

    [Fact]
    public void DetailedCombatAndPreferenceAnalyticsRemainInCareer()
    {
        string text = TextOf(HunterPresentation.BuildCareer(State().Career!));

        Assert.Contains("Combat", text, StringComparison.Ordinal);
        Assert.Contains("Streaks", text, StringComparison.Ordinal);
        Assert.Contains("Most played Hunter", text, StringComparison.Ordinal);
        Assert.Contains("Best Hunter", text, StringComparison.Ordinal);
        Assert.Contains("Favorite map", text, StringComparison.Ordinal);
        Assert.Contains("Favorite mode", text, StringComparison.Ordinal);
        Assert.Contains("Favorite weapon", text, StringComparison.Ordinal);
        Assert.Contains("Team Battle", text, StringComparison.Ordinal);
        Assert.Contains("Power Beam", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TeamBattle", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PowerBeam", text, StringComparison.Ordinal);
        Assert.Contains("Next rank at 2,500 RP", text, StringComparison.Ordinal);
        Assert.Contains("Last match +22 RP", text, StringComparison.Ordinal);
    }

    private static HunterLicensePageState State()
    {
        var totals = new CareerTotals(Matches: 128, Wins: 72, Ties: 4,
            PlayedTicks: 0, Kills: 744, Deaths: 382, Assists: 210,
            Damage: 98_440, Losses: 52, HeadshotKills: 86, BipedKills: 0,
            AltFormKills: 0, LongestKillStreak: 12, LongestWinStreak: 6,
            KillDeathRatio: 1.95m, WinRatio: .5625m);
        var career = new CareerSummary("official", null, totals,
            new CareerChoice("Samus", 40, 40),
            new CareerChoice("Alinos Perch", 22, 22),
            new CareerChoice("TeamBattle", 30, 30),
            new CareerChoice("PowerBeam", 60, 60),
            new CareerChoice("Combat Hall", 10, 10),
            new CareerChoice("Samus", 20, 20),
            3, "Official rating active",
            new CareerRatingSummary(2435, 3, "Gold III", 2500, 22,
                "PairwiseNormalizedV1"));
        return new HunterLicensePageState(License, career,
            ImmutableArray.Create(History(CareerOutcome.FinishedWin)), null,
            HunterLicenseSection.Overview, false, false, false, null);
    }

    private static IReadOnlyList<HunterDossier> Hunters()
        => new[]
        {
            new HunterDossier(Hunter.Samus, "Samus", "Power Beam", null,
                IsFavorite: true, IsMostPlayed: true, IsBest: true,
                PreviewStatus: "Ready"),
            new HunterDossier(Hunter.Kanden, "Kanden", "Volt Driver", null,
                IsFavorite: false, IsMostPlayed: false, IsBest: false,
                PreviewStatus: "Ready")
        };

    private static MatchHistoryEntry History(CareerOutcome outcome)
        => new(Guid.Parse("22222222-2222-2222-2222-222222222222"), 1,
            new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
            "Alinos Perch", MatchMode.TeamBattle, MatchTrustClass.Ranked,
            Eligible: true, Won: outcome == CareerOutcome.FinishedWin,
            Tied: outcome == CareerOutcome.Tie, outcome, PlayedTicks: 3600,
            Kills: 18, Deaths: 7, Assists: 5, Damage: 2441,
            RatingStatus: "Official Match");

    private static IReadOnlyList<string> ButtonLabels(Control root)
        => Walk(root).OfType<Avalonia.Controls.Button>()
            .Select(button => button.Content?.ToString() ?? "").ToArray();

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
        else if (control is ContentControl content
            && content.Content is Control contentChild)
        {
            foreach (Control descendant in Walk(contentChild))
                yield return descendant;
        }
        else if (control is Decorator decorator
            && decorator.Child is Control decoratorChild)
        {
            foreach (Control descendant in Walk(decoratorChild))
                yield return descendant;
        }
    }
}
