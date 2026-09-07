using System;
using System.Collections.Generic;
using MphRead.Mods.UI.AppShell;
using MphRead.Mods.UI.Navigation;
using MphRead.Mods.UI.State;
using MphRead.Mods.UI.Theme;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class UiFoundationTests
{
    [Fact]
    public void RouterRestoresRouteAndFocusWhenGoingBack()
    {
        var router = new UiRouter();
        router.Navigate(UiRoute.Play, sourceFocusKey: "home-play");
        router.Navigate(UiRoute.PrivateMatch, sourceFocusKey: "play-private");

        UiNavigationChangedEventArgs? change = null;
        router.Changed += (_, e) => change = e;

        Assert.True(router.GoBack());
        Assert.Equal(UiRoute.Play, router.CurrentRoute);
        Assert.Equal("play-private", change?.FocusKey);

        Assert.True(router.GoBack());
        Assert.Equal(UiRoute.Home, router.CurrentRoute);
        Assert.Equal("home-play", change?.FocusKey);
        Assert.False(router.CanGoBack);
    }

    [Fact]
    public void BackClosesTopModalBeforeChangingRoute()
    {
        var router = new UiRouter();
        router.Navigate(UiRoute.Play, sourceFocusKey: "home-play");
        router.OpenModal("filters", returnFocusKey: "play-filters");
        router.OpenModal("region", returnFocusKey: "region-select");

        UiNavigationChangedEventArgs? change = null;
        router.Changed += (_, e) => change = e;

        Assert.True(router.GoBack());
        Assert.Equal(UiRoute.Play, router.CurrentRoute);
        Assert.Equal(1, router.ModalCount);
        Assert.Equal("region-select", change?.FocusKey);

        Assert.True(router.GoBack());
        Assert.Equal(UiRoute.Play, router.CurrentRoute);
        Assert.Equal(0, router.ModalCount);
        Assert.Equal("play-filters", change?.FocusKey);

        Assert.True(router.GoBack());
        Assert.Equal(UiRoute.Home, router.CurrentRoute);
    }

    [Fact]
    public void ReplaceDoesNotGrowBackStackAndResetClearsTransientNavigation()
    {
        var router = new UiRouter();
        router.Navigate(UiRoute.Play);
        router.Replace(UiRoute.Lobby, new { Session = 7 });
        Assert.Equal(1, router.BackCount);
        Assert.Equal(UiRoute.Lobby, router.CurrentRoute);

        router.OpenModal("notice");
        router.Reset(UiRoute.Home);
        Assert.Equal(0, router.BackCount);
        Assert.Equal(0, router.ModalCount);
        Assert.False(router.CanGoBack);
    }

    [Theory]
    [InlineData(double.NaN, UiLayoutMode.Compact)]
    [InlineData(0, UiLayoutMode.Compact)]
    [InlineData(719, UiLayoutMode.Compact)]
    [InlineData(720, UiLayoutMode.Medium)]
    [InlineData(1099, UiLayoutMode.Medium)]
    [InlineData(1100, UiLayoutMode.Wide)]
    [InlineData(3440, UiLayoutMode.Wide)]
    public void ResponsiveBreakpointsAreStable(double width, UiLayoutMode expected)
        => Assert.Equal(expected, UiBreakpoints.FromWidth(width));

    [Fact]
    public void AccessibilityPreferencesClampScaleAndSafeArea()
    {
        var preferences = new AccessibilityPreferences { UiScale = 4, SafeArea = -4 };
        Assert.Equal(1.5, preferences.UiScale);
        Assert.Equal(0, preferences.SafeArea);

        preferences.UiScale = 0.1;
        preferences.SafeArea = 100;
        preferences.LargeText = true;
        Assert.Equal(0.8, preferences.UiScale);
        Assert.Equal(64, preferences.SafeArea);
        Assert.True(preferences.TextSize(UiTypography.TextBody) >= UiTypography.TextMinimum);
    }

    [Fact]
    public void FocusPolicySupportsPredictableWrappedNavigation()
    {
        var policy = new UiFocusNavigationPolicy();
        policy.ConnectVertical(new[] { "home", "play", "settings" }, wrap: true);

        AssertMove(policy, "home", UiFocusDirection.Down, "play");
        AssertMove(policy, "play", UiFocusDirection.Down, "settings");
        AssertMove(policy, "settings", UiFocusDirection.Down, "home");
        AssertMove(policy, "home", UiFocusDirection.Up, "settings");
        Assert.False(policy.TryMove("home", UiFocusDirection.Left, out _));
    }

    [Fact]
    public void AppShellStatePublishesOnlyRealLayoutChanges()
    {
        using var state = new AppShellState();
        var changes = new List<string?>();
        state.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        state.SetViewport(800);
        state.SetViewport(800);
        state.SetViewport(1200);

        Assert.Equal(UiLayoutMode.Wide, state.LayoutMode);
        Assert.Equal(2, changes.FindAll(name => name == nameof(AppShellState.ViewportWidth)).Count);
        Assert.Equal(2, changes.FindAll(name => name == nameof(AppShellState.LayoutMode)).Count);
    }

    private static void AssertMove(UiFocusNavigationPolicy policy, string from,
        UiFocusDirection direction, string expected)
    {
        Assert.True(policy.TryMove(from, direction, out string target));
        Assert.Equal(expected, target);
    }
}
