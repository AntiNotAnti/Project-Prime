using System;
using System.Linq;
using Avalonia;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class PrimeRoutePresentationTests
{
    [Fact]
    public void PrimaryNavigationContainsExactlyFourPlayerDestinations()
    {
        Assert.Equal(
            [PrimeRoute.Play, PrimeRoute.Hunter, PrimeRoute.Rankings, PrimeRoute.Theater],
            PrimeRouteInfo.Navigation);
        Assert.DoesNotContain(PrimeRoute.Settings, PrimeRouteInfo.Navigation);
        Assert.DoesNotContain(PrimeRoute.Armory, PrimeRouteInfo.Navigation);
        Assert.DoesNotContain(PrimeRoute.HunterLicense, PrimeRouteInfo.Navigation);
    }

    [Theory]
    [InlineData(0, PrimeShellBreakpoint.Mobile)]
    [InlineData(719.99, PrimeShellBreakpoint.Mobile)]
    [InlineData(720, PrimeShellBreakpoint.Compact)]
    [InlineData(999.99, PrimeShellBreakpoint.Compact)]
    [InlineData(1000, PrimeShellBreakpoint.Wide)]
    [InlineData(2560, PrimeShellBreakpoint.Wide)]
    public void ResponsiveBreakpointsMatchTheShellContract(double width,
        PrimeShellBreakpoint expected)
        => Assert.Equal(expected, PrimeRoutePresentation.Breakpoint(width));

    [Fact]
    public void CompactAndMobileLabelsRemainReadable()
    {
        string[] compact = PrimeRouteInfo.Navigation.Select(route =>
            PrimeRoutePresentation.NavigationLabel(route,
                PrimeShellBreakpoint.Compact)).ToArray();
        string[] mobile = PrimeRoutePresentation.MobilePrimary.Select(route =>
            PrimeRoutePresentation.NavigationLabel(route,
                PrimeShellBreakpoint.Mobile)).Append("More").ToArray();

        Assert.Equal(["Play", "Hunter", "Ranks", "Replays"], compact);
        Assert.Equal(["Play", "Hunter", "Ranks", "More"], mobile);
        Assert.All(compact.Concat(mobile), label => Assert.True(label.Length >= 4));
    }

    [Fact]
    public void MobileMoreMenuContainsEveryNonPrimaryDestination()
        => Assert.Equal(["Theater", "Settings", "Account", "Connection", "About"],
            PrimeRoutePresentation.MoreItems);

    [Fact]
    public void LegacyHunterRoutesNormalizeWithoutRemainingPrimary()
    {
        Assert.True(PrimeRouteInfo.TryParse("HunterLicense", out PrimeRoute license));
        Assert.True(PrimeRouteInfo.TryParse("Armory", out PrimeRoute armory));
        Assert.Equal(PrimeRoute.Hunter, license);
        Assert.Equal(PrimeRoute.Hunter, armory);
    }

    [Fact]
    public void HunterSectionsContainOverviewRosterArsenalCareerAndMatches()
        => Assert.Equal(["Overview", "Hunters", "Arsenal", "Career", "Matches"],
            Enum.GetNames<HunterSection>());

    [Fact]
    public void RouteViewStatePreservesScrollAndHunterSectionAcrossRebuilds()
    {
        var state = new PrimeRouteViewState();
        state.SelectHunterSection(HunterSection.Arsenal);
        state.CaptureScroll(PrimeRoute.Hunter, new Vector(0, 284));

        Assert.Equal(HunterSection.Arsenal, state.HunterSection);
        Assert.Equal(new Vector(0, 284), state.ScrollFor(PrimeRoute.HunterLicense));
        Assert.Equal(default, state.ScrollFor(PrimeRoute.Theater));
    }

    [Fact]
    public void GatewayFailureUsesConcisePrimaryCopy()
    {
        const string technical = "HttpRequestException: connection reset by peer";
        string summary = PrimeRoutePresentation.GatewaySummary(
            GatewayPhase.Failed, technical);

        Assert.DoesNotContain("HttpRequestException", summary, StringComparison.Ordinal);
        Assert.Contains("Could not", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplayCardUsesAuthoritativeDemoMetadataOnly()
    {
        var demo = new PrimeDemoEntry("id", "/tmp/a.fpdemo", "a.fpdemo",
            "Alinos Perch", new DateTime(2026, 9, 9, 18, 42, 0), 2048);

        PrimeReplayPresentation card = PrimeReplayPresentation.From(demo);

        Assert.Equal("Alinos Perch", card.Title);
        Assert.Equal("a.fpdemo", card.FileName);
        Assert.Equal("2 KB", card.Size);
        Assert.Contains("Sep 9, 2026", card.RecordedLine, StringComparison.Ordinal);
    }
}
