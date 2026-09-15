using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaButton = Avalonia.Controls.Button;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Settings;
using MphRead.Mods.MapGen;
using ProjectPrime.Server.Shared;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(MphRead.Tests.Client.UiCaptureTestAppBuilder))]

namespace MphRead.Tests.Client;

/// <summary>
/// Headless application builder used only by the launcher fixture tests.  The
/// Skia renderer is enabled so the same concrete controls can be captured as
/// an in-memory bitmap without requiring a desktop window or display server.
/// </summary>
internal static class UiCaptureTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<LauncherApp>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}

[Collection(AvaloniaUiCollection.Name)]
public sealed class UiCaptureFixtureTests
{
    private static readonly string[] RequiredFixtureNames =
    {
        "title-loading", "title-ready-keyboard", "title-ready-controller",
        "title-ready-touch", "title-reduced-motion",
        "gateway-default", "gateway-login", "gateway-register", "gateway-confirm",
        "gateway-confirm-clean",
        "gateway-error", "gateway-guest",
        "play-home", "play-finding", "play-empty", "play-browser", "play-browser-full",
        "play-browser-filters", "play-browser-filters-empty",
        "play-presence-populated", "play-presence-loading", "play-presence-unavailable",
        "play-network-error", "play-advanced-network", "play-directory-not-loaded",
        "play-directory-loading", "play-directory-empty", "play-directory-loaded",
        "play-directory-error",
        "maps-default", "maps-library", "maps-my-maps", "maps-community-empty",
        "maps-details", "maps-many-items",
        "theatre-list", "theatre-selected", "theatre-empty", "theatre-advanced",
        "host-wide", "host-compact", "host-mobile", "host-step-2",
        "pause-normal", "pause-leave-confirmation",
        "lobby-owner-team", "lobby-owner-ffa", "lobby-owner-start-disabled",
        "lobby-ffa", "lobby-team-selector",
        "lobby-member-team", "lobby-observer",
        "lobby-full", "lobby-waitlist", "lobby-seat-offer", "lobby-chat",
        "lobby-disconnected", "lobby-handoff-failure", "lobby-postmatch",
        "lobby-owner-ffa-wide", "lobby-owner-team-wide", "lobby-member-wide",
        "lobby-observer-wide", "lobby-waitlist-wide", "lobby-chat-wide", "lobby-mobile",
        "results-ffa", "results-team", "results-ballot", "results-voted", "results-resolved",
        "results-no-authoritative-result",
        "settings-shell-route", "settings-gameplay", "settings-dirty-footer", "settings-controls",
        "controls-gamepad", "controls-stylus", "controls-mobile",
        "settings-graphics", "settings-audio",
        "settings-system", "settings-network", "settings-accessibility", "settings-about",
        "settings-pro-hud-off", "settings-pro-hud-on", "settings-radar-custom",
        "settings-gyro-unsupported", "settings-gyro-supported",
        "settings-touch-buttons-off", "settings-touch-buttons-on",
        "settings-advanced-controller-collapsed", "settings-advanced-controller-expanded",
        "hunter-overview", "hunter-arsenal", "hunter-roster", "hunter-career",
        "hunter-matches", "hunter-history", "hunter-overview-simplified",
        "hunter-empty-history", "hunter-preview-failure", "rankings-desktop", "rankings-mobile"
    };

    [AvaloniaFact]
    public async Task GameFilesSetupPageIncludesProgressFeedback()
    {
        PrimeShellView view = PrimeShellView.CreateCapture(new MenuSettings(),
            Array.Empty<string>(), PrimeRoute.Gateway);
        try
        {
            MethodInfo build = typeof(PrimeShellView).GetMethod("BuildGameFilesPage",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            Control page = Assert.IsAssignableFrom<Control>(build.Invoke(view, null));

            ProgressRow progress = Assert.Single(Walk(page).OfType<ProgressRow>());
            Assert.False(progress.IsVisible);
        }
        finally
        {
            await view.DisposeAsync();
        }
    }

    private static readonly string[] UnavailableFixtureNames = Array.Empty<string>();

    private static readonly string[] SettingsFixtureNames =
    {
        "settings-gameplay", "settings-controls", "controls-gamepad", "controls-stylus",
        "controls-mobile",
        "settings-graphics", "settings-audio",
        "settings-system", "settings-network", "settings-accessibility", "settings-about",
        "settings-pro-hud-off", "settings-pro-hud-on", "settings-radar-custom",
        "settings-gyro-unsupported", "settings-gyro-supported",
        "settings-touch-buttons-off", "settings-touch-buttons-on",
        "settings-advanced-controller-collapsed", "settings-advanced-controller-expanded"
    };

    [Fact]
    public void CatalogContainsEveryP5FixtureAndRequiredViewport()
    {
        UiCaptureSize[] sizes = UiCapture.RequiredSizes.ToArray();
        Assert.Equal(new[] { "1920x1080", "2560x1440", "1280x720", "830x390", "960x540",
            "1280x800", "1440x900", "720x900", "940x560", "900x1100", "560x800" },
            sizes.Select(size => size.Name).ToArray());
        Assert.Equal(new[] { (1920, 1080), (2560, 1440), (1280, 720),
            (830, 390), (960, 540), (1280, 800), (1440, 900), (720, 900),
            (940, 560), (900, 1100), (560, 800) },
            sizes.Select(size => (size.Width, size.Height)).ToArray());
        Assert.Equal(RequiredFixtureNames,
            UiCapture.FixtureDefinitions.Select(fixture => fixture.Name).ToArray());
        Assert.Equal(UnavailableFixtureNames,
            UiCapture.PlannedButUnavailableFixtures.ToArray());
        Assert.Empty(RequiredFixtureNames.Intersect(UnavailableFixtureNames,
            StringComparer.OrdinalIgnoreCase));
        Assert.Equal(105, RequiredFixtureNames.Length + UnavailableFixtureNames.Length);
        Assert.Equal(RequiredFixtureNames.Length * sizes.Length,
            UiCapture.FixtureDefinitions.Count * sizes.Length);
        Assert.Equal(UiCapture.FixtureDefinitions.Count,
            UiCapture.FixtureDefinitions.Select(fixture => fixture.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [AvaloniaFact]
    public void ShortGatewayFormKeepsKeyboardFieldAndActionsScrollable()
    {
        PrimeShellView shell = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "gateway-login", new MenuSettings(), Array.Empty<string>()));
        var window = new Window { Width = 830, Height = 390, Content = shell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            ScrollViewer pageScroller = Assert.Single(shell.GetVisualDescendants()
                .OfType<ScrollViewer>(), scroller => scroller.Name == "PageScroller");
            TextBox[] fields = shell.GetVisualDescendants().OfType<TextBox>()
                .Where(field => field.IsEffectivelyVisible && field.IsEffectivelyEnabled)
                .ToArray();
            Assert.True(fields.Length >= 2,
                "The sign-in fixture should expose editable email and password fields.");

            TextBox activeField = fields[^1];
            Assert.True(activeField.Focus());
            activeField.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.True(activeField.IsFocused);
            AssertWithinScrollViewport(activeField, pageScroller,
                "Focused sign-in field");

            PrimeButton submit = Assert.Single(shell.GetVisualDescendants()
                .OfType<PrimeButton>(), button => Equals(button.Content, "Sign in"));
            Assert.True(submit.IsEffectivelyVisible);
            submit.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            AssertWithinScrollViewport(submit, pageScroller, "Sign-in action");
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(shell);
        }
    }

    [AvaloniaFact]
    public void PresenceFixturesExposeAccessibleRowsAndExplicitLoadingFailures()
    {
        Control populated = UiCapture.BuildFixture("play-presence-populated",
            new MenuSettings(), Array.Empty<string>());
        var populatedWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = populated
        };
        try
        {
            populatedWindow.Show();
            Dispatcher.UIThread.RunJobs();
            PrimeSectionPanel panel = Assert.Single(populated.GetVisualDescendants()
                .OfType<PrimeSectionPanel>()
                .Where(value => value.Classes.Contains("prime-online-players")));
            Assert.Equal("Online players", AutomationProperties.GetName(panel));
            Assert.False(String.IsNullOrWhiteSpace(
                AutomationProperties.GetHelpText(panel)));

            Control[] rows = panel.GetVisualDescendants().OfType<Control>()
                .Where(control => control.Classes.Contains("prime-online-player-row"))
                .ToArray();
            Assert.True(rows.Length > 0,
                $"Presence panel did not realize rows. Text: {TextOf(panel)}; classes: "
                + String.Join(",", panel.GetVisualDescendants().OfType<Control>()
                    .SelectMany(control => control.Classes).Distinct()));
            Assert.All(rows, row =>
            {
                Assert.False(String.IsNullOrWhiteSpace(
                    AutomationProperties.GetName(row)));
                Assert.False(String.IsNullOrWhiteSpace(
                    AutomationProperties.GetHelpText(row)));
            });
            Assert.Contains("Lastraven", TextOf(populated), StringComparison.Ordinal);
        }
        finally
        {
            populatedWindow.Content = null;
            populatedWindow.Close();
            DisposeView(populated);
        }

        Control loading = UiCapture.BuildFixture("play-presence-loading",
            new MenuSettings(), Array.Empty<string>());
        var loadingWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = loading
        };
        try
        {
            loadingWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Checking who’s online…", TextOf(loading),
                StringComparison.Ordinal);
        }
        finally
        {
            loadingWindow.Content = null;
            loadingWindow.Close();
            DisposeView(loading);
        }

        Control unavailable = UiCapture.BuildFixture("play-presence-unavailable",
            new MenuSettings(), Array.Empty<string>());
        var unavailableWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = unavailable
        };
        try
        {
            unavailableWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Status unavailable", TextOf(unavailable),
                StringComparison.Ordinal);
            Assert.Contains("Lobby browsing and hosting are still available.",
                TextOf(unavailable), StringComparison.Ordinal);
        }
        finally
        {
            unavailableWindow.Content = null;
            unavailableWindow.Close();
            DisposeView(unavailable);
        }
    }

    [AvaloniaFact]
    public void BrowseFilterFixturesUseTheProductionFilterAndEmptyState()
    {
        Control filtered = UiCapture.BuildFixture("play-browser-filters",
            new MenuSettings(), Array.Empty<string>());
        var filteredWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = filtered
        };
        try
        {
            filteredWindow.Show();
            Dispatcher.UIThread.RunJobs();
            CheckBox openSeats = Assert.Single(filtered.GetVisualDescendants()
                .OfType<CheckBox>(), check => Equals(check.Content, "Open player seats"));
            Assert.True(openSeats.IsChecked);
            Assert.NotEmpty(filtered.GetVisualDescendants().OfType<PrimeMatchCard>());
        }
        finally
        {
            filteredWindow.Content = null;
            filteredWindow.Close();
            DisposeView(filtered);
        }

        Control empty = UiCapture.BuildFixture("play-browser-filters-empty",
            new MenuSettings(), Array.Empty<string>());
        var emptyWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = empty
        };
        try
        {
            emptyWindow.Show();
            Dispatcher.UIThread.RunJobs();
            CheckBox hideFull = Assert.Single(empty.GetVisualDescendants()
                .OfType<CheckBox>(), check => Equals(check.Content, "Hide full lobbies"));
            Assert.True(hideFull.IsChecked);
            Assert.Empty(empty.GetVisualDescendants().OfType<PrimeMatchCard>());
            Assert.Contains("No lobbies match these filters.", TextOf(empty),
                StringComparison.Ordinal);
        }
        finally
        {
            emptyWindow.Content = null;
            emptyWindow.Close();
            DisposeView(empty);
        }
    }

    [AvaloniaFact]
    public void HostStepTwoFixtureUsesTheProductionResponsiveStage()
    {
        PrimeShellView view = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "host-step-2", new MenuSettings(), new[] { "MP3 PROVING GROUND" }));
        var window = new Window
        {
            Width = 940,
            Height = 560,
            Content = view
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            PrimePlayResponsivePanel dashboard = Assert.Single(view.GetVisualDescendants()
                .OfType<PrimePlayResponsivePanel>()
                .Where(panel => panel.Classes.Contains("prime-host-layout")));
            Assert.Equal(2, dashboard.CurrentStep);
            // The compact shell scales this desktop page to its authored
            // width before the responsive panel measures it. At this window
            // size that logical width is wide, so both authored stages remain
            // visible even though the step rail is advanced to step two.
            Assert.Equal(PrimeContentLayout.Wide, dashboard.Layout);
            Assert.Contains(dashboard.Children, child => child.IsVisible
                && TextOf(child).Contains("MISSION", StringComparison.Ordinal));
            Assert.Contains(dashboard.Children, child => child.IsVisible
                && TextOf(child).Contains("MATCH CONFIGURATION", StringComparison.Ordinal));
            Assert.Contains(dashboard.Children, child => child.IsVisible
                && TextOf(child).Contains("LOBBY & SEATS", StringComparison.Ordinal));
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void SettingsDirtyFixtureShowsOneResponsiveFooter()
    {
        SettingsView view = ExtractSettingsView(UiCapture.BuildFixture(
            "settings-dirty-footer", new MenuSettings(), Array.Empty<string>()));
        var window = new Window
        {
            Width = 940,
            Height = 560,
            Content = view
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.IsDirty);
            SettingsActionBar footer = Assert.Single(view.GetVisualDescendants()
                .OfType<SettingsActionBar>());
            Assert.True(footer.IsVisible);
            Assert.True(footer.IsDirty);
            Assert.Equal("1 unsaved change", footer.DirtyCountText);
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void PauseFixturesExposeNormalAndLeaveConfirmationStates()
    {
        PauseMenuView normal = Assert.IsType<PauseMenuView>(UiCapture.BuildFixture(
            "pause-normal", new MenuSettings(), Array.Empty<string>()));
        var normalWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = normal
        };
        try
        {
            normalWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.False(normal.LeaveConfirmationPending);
            Assert.Contains(normal.GetVisualDescendants().OfType<MenuEntry>(),
                entry => entry.Title == "Leave match" && entry.IsVisible);
        }
        finally
        {
            normalWindow.Content = null;
            normalWindow.Close();
            DisposeView(normal);
        }

        PauseMenuView confirmation = Assert.IsType<PauseMenuView>(UiCapture.BuildFixture(
            "pause-leave-confirmation", new MenuSettings(), Array.Empty<string>()));
        var confirmationWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = confirmation
        };
        try
        {
            confirmationWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(confirmation.LeaveConfirmationPending);
            Assert.Contains("Leave match?", TextOf(confirmation),
                StringComparison.Ordinal);
            Assert.Contains(confirmation.GetVisualDescendants().OfType<MenuEntry>(),
                entry => entry.Title == "Leave match" && entry.Primary && entry.IsVisible);
            Assert.Contains(confirmation.GetVisualDescendants().OfType<MenuEntry>(),
                entry => entry.Title == "Stay" && entry.IsVisible);
        }
        finally
        {
            confirmationWindow.Content = null;
            confirmationWindow.Close();
            DisposeView(confirmation);
        }
    }

    [AvaloniaFact]
    public void TheatreDetailFixtureIncludesAReadableTimelineAlternative()
    {
        Control view = UiCapture.BuildFixture("theatre-selected", new MenuSettings(),
            Array.Empty<string>());
        var window = new Window
        {
            Width = 940,
            Height = 560,
            Content = view
        };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("Replay events", TextOf(view), StringComparison.Ordinal);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("Kill", StringComparison.Ordinal) == true);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("Node capture", StringComparison.Ordinal) == true);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text?.Contains("Match point", StringComparison.Ordinal) == true);
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void RankingsDesktopFixtureCarriesTopThreeAndCurrentPlayer()
    {
        PrimeShellView view = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "rankings-desktop", new MenuSettings(), Array.Empty<string>()));
        var window = new Window { Width = 1440, Height = 900, Content = view };
        try
        {
            RankingsState state = Assert.IsType<RankingsState>(view.CaptureRankingsState);
            Assert.Equal(3, state.Rows.Length);
            Assert.Contains(state.Rows, row => row.IsCurrentPlayer);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(1440, 900));
            window.Arrange(new Rect(0, 0, 1440, 900));
            PrimeResponsiveLeaderboard responsive = Assert.Single(view
                .GetVisualDescendants().OfType<PrimeResponsiveLeaderboard>());
            Assert.Contains(responsive.GetVisualDescendants().OfType<Control>(),
                candidate => candidate.Classes.Contains("prime-rankings-desktop")
                    && candidate.IsEffectivelyVisible);
            Assert.Contains("Capture Preview YOU", TextOf(view), StringComparison.Ordinal);
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaTheory]
    [InlineData(1024, 720)]
    [InlineData(830, 390)]
    [InlineData(560, 800)]
    public void SettingsShellRouteCaptureOwnsOneResponsiveActionBar(int width, int height)
    {
        PrimeShellView shell = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "settings-shell-route", new MenuSettings(), Array.Empty<string>()));
        var window = new Window { Width = width, Height = height, Content = shell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));

            SettingsView settings = Assert.Single(shell.GetVisualDescendants()
                .OfType<SettingsView>());
            Assert.False(settings.HasEmbeddedActionBar);
            SettingsActionBar actionBar = Assert.Single(shell.GetVisualDescendants()
                .OfType<SettingsActionBar>());
            Panel shellActionBar = Assert.Single(shell.GetVisualDescendants()
                .OfType<Panel>(), panel => panel.Name == "ActionBar");
            Assert.Same(actionBar, Assert.Single(shellActionBar.Children));
            Assert.Equal(width <= 600, actionBar.IsNarrow);

            using WriteableBitmap frame = window.CaptureRenderedFrame()!;
            Assert.Equal(new PixelSize(width, height), frame.PixelSize);
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(shell);
        }
    }

    [AvaloniaFact]
    public void OwnerStartDisabledCaptureUsesTheRealLobbyEligibilityState()
    {
        PrimeShellView shell = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "lobby-owner-start-disabled", new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }));
        var window = new Window { Width = 940, Height = 560, Content = shell };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            PlayState play = Assert.IsType<PlayState>(shell.CapturePlayState);
            LobbySnapshot lobby = Assert.IsType<LobbySnapshot>(play.Lobby);
            NodeSessionSnapshot session = Assert.IsType<NodeSessionSnapshot>(
                play.Node?.Session);
            Assert.Equal(session.SessionId, lobby.OwnerSessionId);
            Assert.Contains(lobby.Members, member => !member.Ready && !member.Observer);

            PrimeButton start = Assert.Single(shell.GetVisualDescendants()
                .OfType<PrimeButton>(), button => Equals(button.Content, "Start Match"));
            Assert.False(start.IsEnabled);
            Assert.False(start.IsEffectivelyEnabled);
            Assert.Contains("prime-primary", start.Classes);
            Assert.Contains("ready", TextOf(shell), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(shell);
        }
    }

    [Fact]
    public void NamedUi2FixturesCarryTheirRequestedCaptureStates()
    {
        PrimeShellView confirmation = Assert.IsType<PrimeShellView>(
            UiCapture.BuildFixture("gateway-confirm-clean", new MenuSettings(),
                Array.Empty<string>()));
        try
        {
            Assert.Equal(GatewayPhase.Confirming,
                confirmation.CaptureGatewayState?.Phase);
            Assert.Null(confirmation.CaptureGatewayState?.PlayerId);
            Assert.NotNull(confirmation.Gateway.PendingRegistration);
        }
        finally
        {
            DisposeView(confirmation);
        }

        PrimeShellView ffa = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "lobby-ffa", new MenuSettings(), new[] { "MP3 PROVING GROUND" }));
        try
        {
            LobbySnapshot lobby = Assert.IsType<LobbySnapshot>(ffa.CapturePlayState?.Lobby);
            Assert.Equal(MatchMode.Battle, lobby.Mode);
            Assert.All(lobby.Members, member => Assert.Equal((byte)0, member.Team));
        }
        finally
        {
            DisposeView(ffa);
        }

        PrimeShellView team = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "lobby-team-selector", new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }));
        try
        {
            LobbySnapshot lobby = Assert.IsType<LobbySnapshot>(team.CapturePlayState?.Lobby);
            Assert.Equal(MatchMode.TeamBattle, lobby.Mode);
            Assert.Contains(lobby.Members, member => member.Team == 0);
            Assert.Contains(lobby.Members, member => member.Team == 1);
        }
        finally
        {
            DisposeView(team);
        }

        PrimeShellView history = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "hunter-history", new MenuSettings(), Array.Empty<string>()));
        try
        {
            HunterLicensePageState state = Assert.IsType<HunterLicensePageState>(
                history.CaptureLicenseState);
            Assert.Equal(HunterLicenseSection.Matches, state.Section);
            Assert.NotEmpty(state.Matches);
        }
        finally
        {
            DisposeView(history);
        }

        PrimeShellView overview = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "hunter-overview-simplified", new MenuSettings(),
            Array.Empty<string>()));
        try
        {
            HunterLicensePageState state = Assert.IsType<HunterLicensePageState>(
                overview.CaptureLicenseState);
            Assert.Equal(HunterLicenseSection.Overview, state.Section);
            Assert.NotNull(state.License);
            Assert.NotNull(state.Career);
        }
        finally
        {
            DisposeView(overview);
        }
    }

    [AvaloniaFact]
    public void PlayDirectoryFixturesExposeExactlyOneDirectoryStatus()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["play-directory-not-loaded"] = "prime-directory-not-loaded",
            ["play-directory-loading"] = "prime-directory-loading",
            ["play-directory-empty"] = "prime-directory-empty",
            ["play-directory-loaded"] = "prime-directory-loaded",
            ["play-directory-error"] = "prime-directory-failed"
        };

        foreach ((string fixtureName, string expectedClass) in expected)
        {
            Control control = UiCapture.BuildFixture(fixtureName, new MenuSettings(),
                new[] { "MP3 PROVING GROUND" });
            PrimeShellView view = Assert.IsType<PrimeShellView>(control);
            var window = new Window { Width = 940, Height = 560, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                PrimeSectionPanel status = Assert.Single(view.GetVisualDescendants()
                    .OfType<PrimeSectionPanel>(),
                    panel => panel.Classes.Contains("prime-directory-state"));
                Assert.Contains(expectedClass, status.Classes);

                string[] stateClasses =
                [
                    "prime-directory-not-loaded", "prime-directory-loading",
                    "prime-directory-empty", "prime-directory-loaded",
                    "prime-directory-failed"
                ];
                Assert.Single(stateClasses, status.Classes.Contains);
            }
            finally
            {
                window.Close();
                DisposeView(view);
            }
        }
    }

    [AvaloniaFact]
    public void PlayP1BAndP1CCaptureFixturesExposeTheCompactHierarchy()
    {
        Control home = UiCapture.BuildFixture("play-advanced-network",
            new MenuSettings(), Array.Empty<string>());
        var homeWindow = new Window { Width = 940, Height = 560, Content = home };
        try
        {
            homeWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Walk(home).OfType<Expander>());
            Assert.Single(Walk(home).OfType<PrimeSectionPanel>(), panel =>
                panel.Classes.Contains("prime-network-summary"));
            Assert.Contains("Automatic region", TextOf(home), StringComparison.Ordinal);
        }
        finally
        {
            homeWindow.Close();
            DisposeView(home);
        }

        Control empty = UiCapture.BuildFixture("play-directory-empty",
            new MenuSettings(), Array.Empty<string>());
        var emptyWindow = new Window { Width = 940, Height = 560, Content = empty };
        try
        {
            emptyWindow.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("NO OPEN LOBBIES", TextOf(empty), StringComparison.Ordinal);
            Assert.Contains("No public lobbies are open right now.", TextOf(empty),
                StringComparison.Ordinal);
            Assert.Equal(1, Walk(empty).OfType<PrimeButton>()
                .Count(button => Equals(button.Content, "Host lobby")));
            Assert.Single(Walk(empty).OfType<PrimeButton>(),
                button => Equals(button.Content, "Refresh"));
        }
        finally
        {
            emptyWindow.Close();
            DisposeView(empty);
        }

        Control lobby = UiCapture.BuildFixture("lobby-owner-ffa-wide",
            new MenuSettings(), new[] { "MP3 PROVING GROUND" });
        var lobbyWindow = new Window { Width = 1280, Height = 720, Content = lobby };
        try
        {
            lobbyWindow.Show();
            Dispatcher.UIThread.RunJobs();
            string text = TextOf(lobby);
            Assert.Contains("● OPEN · 3/8 PLAYERS", text,
                StringComparison.Ordinal);
            Assert.Contains("0/16 OBSERVERS", text, StringComparison.Ordinal);
            Assert.Contains("YOUR HUNTER", text, StringComparison.Ordinal);
            Assert.Contains("✓ Ready", text, StringComparison.Ordinal);
            Assert.Contains("Samus · YOU · HOST", text,
                StringComparison.Ordinal);
            Assert.Contains("No observers.", text, StringComparison.Ordinal);
        }
        finally
        {
            lobbyWindow.Close();
            DisposeView(lobby);
        }
    }

    [AvaloniaFact]
    public void MapsCaptureFixturesUseTheirRequestedPlayerSurfaces()
    {
        Control libraryFixture = UiCapture.BuildFixture("maps-library",
            new MenuSettings(), Array.Empty<string>());
        MapsPresentationView library = ExtractRouteContent<MapsPresentationView>(libraryFixture);
        try
        {
            Assert.Equal("Library", Assert.Single(Walk(library).OfType<PrimeTabStrip>())
                .SelectedTab.Label);
            Assert.Single(library.ItemsControl!.ItemsSource!.Cast<InstalledMap>());
        }
        finally
        {
            DisposeView(libraryFixture);
        }

        Control myMapsFixture = UiCapture.BuildFixture("maps-my-maps",
            new MenuSettings(), Array.Empty<string>());
        MapsPresentationView myMaps = ExtractRouteContent<MapsPresentationView>(myMapsFixture);
        try
        {
            Assert.Equal("My Maps", Assert.Single(Walk(myMaps).OfType<PrimeTabStrip>())
                .SelectedTab.Label);
            Assert.Single(myMaps.ItemsControl!.ItemsSource!.Cast<InstalledMap>());
        }
        finally
        {
            DisposeView(myMapsFixture);
        }

        Control communityFixture = UiCapture.BuildFixture("maps-community-empty",
            new MenuSettings(), Array.Empty<string>());
        MapsPresentationView community =
            ExtractRouteContent<MapsPresentationView>(communityFixture);
        try
        {
            Assert.Equal("Community", Assert.Single(Walk(community).OfType<PrimeTabStrip>())
                .SelectedTab.Label);
            Assert.Null(community.ItemsControl);
            Assert.Contains("Community maps are coming later.", TextOf(community),
                StringComparison.Ordinal);
        }
        finally
        {
            DisposeView(communityFixture);
        }

        Control manyFixture = UiCapture.BuildFixture("maps-many-items",
            new MenuSettings(), Array.Empty<string>());
        MapsPresentationView many = ExtractRouteContent<MapsPresentationView>(manyFixture);
        try
        {
            ListBox manyItems = many.ItemsControl
                ?? throw new Xunit.Sdk.XunitException("Many-items fixture has no map list.");
            Assert.Equal(120, manyItems.ItemsSource!.Cast<InstalledMap>().Count());
            Assert.NotNull(manyItems.ItemsPanel);
        }
        finally
        {
            DisposeView(manyFixture);
        }

        Control detailsFixture = UiCapture.BuildFixture("maps-details", new MenuSettings(),
            Array.Empty<string>());
        Control details = ExtractRouteContent<Control>(detailsFixture);
        try
        {
            string detailText = TextOf(details);
            Assert.Contains("MAP DETAILS", detailText, StringComparison.Ordinal);
            Assert.Contains("STABLE ID", detailText, StringComparison.Ordinal);
            Assert.Contains("CONTENT HASH", detailText, StringComparison.Ordinal);
            Assert.Contains("SOURCE PATH", detailText, StringComparison.Ordinal);
        }
        finally
        {
            DisposeView(detailsFixture);
        }
    }

    [AvaloniaTheory]
    [InlineData(940, 560)]
    [InlineData(830, 390)]
    [InlineData(560, 800)]
    public void MapsCaptureFixturesRenderAtDesktopAndMobileSizes(int width, int height)
    {
        foreach (string fixtureName in new[]
        {
            "maps-library", "maps-my-maps", "maps-community-empty", "maps-details",
            "maps-many-items"
        })
        {
            Control view = UiCapture.BuildFixture(fixtureName, new MenuSettings(),
                Array.Empty<string>());
            var window = new Window { Width = width, Height = height, Content = view };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using WriteableBitmap frame = window.CaptureRenderedFrame()!;
                Assert.Equal(new PixelSize(width, height), frame.PixelSize);
                AssertFiniteBounds(view, fixtureName);
                if (fixtureName == "maps-many-items")
                {
                    int realized = view.GetVisualDescendants().OfType<ListBoxItem>().Count();
                    Assert.InRange(realized, 1, 119);
                }
            }
            finally
            {
                window.Close();
                DisposeView(view);
            }
        }
    }

    [AvaloniaFact]
    public void TheatreCaptureFixturesKeepGeneratedFilenamesInAdvancedOnly()
    {
        foreach (string fixtureName in new[]
        {
            "theatre-list", "theatre-selected", "theatre-empty", "theatre-advanced"
        })
        {
            Control fixture = UiCapture.BuildFixture(fixtureName, new MenuSettings(),
                Array.Empty<string>());
            Control view = ExtractRouteContent<Control>(fixture);
            try
            {
                Expander advanced = Assert.Single(Walk(view).OfType<Expander>(),
                    item => Equals(item.Header, "Advanced"));
                PrimeSectionPanel[] normalPanels = Walk(view)
                    .OfType<PrimeSectionPanel>().ToArray();
                string normalText = String.Join('\n', normalPanels.Select(TextOf));
                Assert.DoesNotContain("capture-final-001.fpreplay", normalText,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("capture-final-002.fpreplay", normalText,
                    StringComparison.Ordinal);
                Assert.DoesNotContain("capture-final-003.fpreplay", normalText,
                    StringComparison.Ordinal);

                if (fixtureName == "theatre-advanced")
                {
                    Assert.True(advanced.IsExpanded);
                    Assert.Contains("capture-final-001.fpreplay", TextOf(advanced),
                        StringComparison.Ordinal);
                }
                else
                {
                    Assert.False(advanced.IsExpanded);
                }
            }
            finally
            {
                DisposeView(fixture);
            }
        }
    }

    [AvaloniaFact]
    public void WidePlayFixturesKeepInitialActionsInsideTheRealShellViewport()
    {
        foreach (string fixtureName in new[] { "host-wide", "lobby-owner-team-wide",
            "lobby-member-wide", "lobby-chat-wide" })
        {
            Control view = UiCapture.BuildFixture(fixtureName, new MenuSettings(),
                new[] { "MP3 PROVING GROUND" });
            var window = new Window { Width = 1280, Height = 720, Content = view };
            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                PrimePlayResponsivePanel layout = Assert.Single(view
                    .GetVisualDescendants().OfType<PrimePlayResponsivePanel>());
                Assert.Equal(PrimeContentLayout.Wide, layout.Layout);
                ScrollViewer pageScroller = Assert.Single(view
                    .GetVisualDescendants().OfType<ScrollViewer>(),
                    scroller => scroller.Name == "PageScroller");
                Assert.Equal(0, pageScroller.Offset.Y);
                Rect viewport = new(pageScroller.Viewport);

                IEnumerable<Control> required = fixtureName == "host-wide"
                    ? layout.GetVisualDescendants().OfType<Control>()
                        .Where(control => control is TextBox { Watermark: not null }
                            || control is ComboBox
                            || control is PrimeButton button
                                && Equals(button.Content, "Create lobby"))
                    : view.GetVisualDescendants().OfType<Control>()
                        .Where(control => control is ComboBox
                            || control is PrimePreviewStage
                            || control is PrimeButton button
                                && (Equals(button.Content, "Ready")
                                    || Equals(button.Content, "Start Match")
                                    || Equals(button.Content, "Edit Match"))
                            || control is ScrollViewer scroll
                                && scroll.Classes.Contains("prime-roster-scroll"));
                Control[] targets = required.ToArray();
                Assert.NotEmpty(targets);
                if (fixtureName == "host-wide")
                {
                    WrapPanel stepRail = Assert.Single(layout.Children
                        .OfType<WrapPanel>(), panel => panel.Classes.Contains(
                            "prime-host-step-rail"));
                    Assert.False(stepRail.IsVisible);
                    Assert.Equal(2, layout.Children.OfType<PrimeCompactPanel>().Count());
                }
                if (fixtureName.StartsWith("lobby-", StringComparison.Ordinal))
                {
                    Assert.Contains("LOBBY CHAT", TextOf(view),
                        StringComparison.Ordinal);
                    Assert.Single(view.GetVisualDescendants().OfType<PrimeButton>()
                        .Where(button => button.Content?.ToString()?
                            .StartsWith("Leave lobby", StringComparison.Ordinal) == true));
                }
                foreach (Control target in targets)
                {
                    Assert.True(target.IsEffectivelyVisible,
                        $"{fixtureName}: {Describe(target)} is not effectively visible.");
                    Rect? bounds = BoundsRelativeTo(target, pageScroller);
                    Assert.True(bounds.HasValue,
                        $"{fixtureName}: could not resolve {target.GetType().Name} bounds.");
                    Assert.True(bounds.Value.Left >= viewport.Left - 1
                        && bounds.Value.Top >= viewport.Top - 1
                        && bounds.Value.Right <= viewport.Right + 1
                        && bounds.Value.Bottom <= viewport.Bottom + 1,
                        $"{fixtureName}: {Describe(target)} is outside PageScroller "
                        + $"({bounds}, layout {layout.Bounds}, viewport {viewport}).");
                }
            }
            finally
            {
                window.Close();
                DisposeView(view);
            }
        }
    }

    [AvaloniaFact]
    public void CompactLobbyKeepsThePrimaryReadinessActionNearTheTop()
    {
        Control view = UiCapture.BuildFixture("lobby-owner-team",
            new MenuSettings(), new[] { "MP3 PROVING GROUND" });
        var window = new Window { Width = 720, Height = 900, Content = view };
        try
        {
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            ScrollViewer pageScroller = Assert.Single(view.GetVisualDescendants()
                .OfType<ScrollViewer>(), scroller => scroller.Name == "PageScroller");
            PrimeButton start = Assert.Single(view.GetVisualDescendants()
                .OfType<PrimeButton>(), button => Equals(button.Content, "Start Match"));
            Rect? bounds = BoundsRelativeTo(start, pageScroller);

            Assert.Equal(0, pageScroller.Offset.Y);
            Assert.True(bounds.HasValue);
            Assert.True(bounds.Value.Top >= 0
                && bounds.Value.Bottom <= pageScroller.Viewport.Height,
                $"Start Match is outside the initial compact viewport at {bounds.Value}.");
        }
        finally
        {
            window.Close();
            DisposeView(view);
        }
    }

    private static string Describe(Control control)
        => control switch
        {
            TextBox textBox => $"TextBox '{textBox.Watermark}'",
            PrimeButton button => $"PrimeButton '{button.Content}'",
            _ => control.GetType().Name
        };

    private static Rect? BoundsRelativeTo(Control control, Visual relativeTo)
    {
        Avalonia.Matrix? transform = control.TransformToVisual(relativeTo);
        if (!transform.HasValue) return null;
        Rect local = new(control.Bounds.Size);
        Point[] corners =
        {
            transform.Value.Transform(local.TopLeft),
            transform.Value.Transform(local.TopRight),
            transform.Value.Transform(local.BottomLeft),
            transform.Value.Transform(local.BottomRight)
        };
        double left = corners.Min(point => point.X);
        double top = corners.Min(point => point.Y);
        double right = corners.Max(point => point.X);
        double bottom = corners.Max(point => point.Y);
        return new Rect(left, top, right - left, bottom - top);
    }

    private static void AssertWithinScrollViewport(Control control,
        ScrollViewer scroller, string description)
    {
        Rect? bounds = BoundsRelativeTo(control, scroller);
        Assert.True(bounds.HasValue,
            $"{description} could not be translated to PageScroller.");
        Rect viewport = new(scroller.Viewport);
        Assert.True(bounds.Value.Left >= viewport.Left - 1
            && bounds.Value.Top >= viewport.Top - 1
            && bounds.Value.Right <= viewport.Right + 1
            && bounds.Value.Bottom <= viewport.Bottom + 1,
            $"{description} is outside PageScroller ({bounds} vs {viewport}).");
    }

    [AvaloniaFact]
    public void RankingsMobileFixtureUsesRankingDataAndResponsiveMobileLayout()
    {
        PrimeShellView view = Assert.IsType<PrimeShellView>(UiCapture.BuildFixture(
            "rankings-mobile", new MenuSettings(), Array.Empty<string>()));
        var window = new Window { Width = 560, Height = 800, Content = view };
        try
        {
            RankingsState state = Assert.IsType<RankingsState>(
                view.CaptureRankingsState);
            Assert.Equal("rp", state.Metric);
            Assert.NotEmpty(state.Rows);
            Assert.Contains(state.Rows, row => row.IsCurrentPlayer);

            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(560, 800));
            window.Arrange(new Rect(0, 0, 560, 800));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            PrimeResponsiveLeaderboard responsive = Assert.Single(view
                .GetVisualDescendants().OfType<PrimeResponsiveLeaderboard>());
            Control mobile = Assert.Single(responsive.GetVisualDescendants().OfType<Control>(),
                candidate => candidate.Classes.Contains("prime-rankings-mobile"));
            Control desktop = Assert.Single(responsive.GetVisualDescendants().OfType<Control>(),
                candidate => candidate.Classes.Contains("prime-rankings-desktop"));
            Assert.True(mobile.IsEffectivelyVisible);
            Assert.False(desktop.IsEffectivelyVisible);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Capture Preview YOU");
        }
        finally
        {
            window.Content = null;
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void EveryFixtureBuildsAConcreteControlInHeadlessAvalonia()
    {
        MenuSettings settings = new();
        IReadOnlyList<string> rooms = new[] { "MP3 PROVING GROUND" };

        foreach (UiCaptureFixtureDefinition fixture in UiCapture.FixtureDefinitions)
        {
            Control view = UiCapture.BuildFixture(fixture.Name, settings, rooms);
            Window window = new() { Width = 940, Height = 560, Content = view };
            try
            {
                window.Show();
                Control[] descendants = view.GetVisualDescendants().OfType<Control>().ToArray();
                Assert.True(descendants.Length > 0,
                    $"Fixture '{fixture.Name}' did not construct visual descendants.");
            }
            finally
            {
                window.Close();
                DisposeView(view);
            }
        }
    }

    [AvaloniaFact]
    public void ControlsCaptureFixturesExposeTheirRequestedPlatformAndActiveTab()
    {
        SettingsView desktop = ExtractSettingsView(UiCapture.BuildFixture(
            "controls-gamepad", new MenuSettings(), Array.Empty<string>()));
        var desktopWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = desktop
        };
        try
        {
            desktopWindow.Show();
            Assert.Equal(new[] { "Mouse & Keyboard", "Gamepad", "Stylus" },
                desktop.ControlsTabNames.ToArray());
            Assert.Equal(new[] { "Gamepad" },
                desktop.VisibleControlsTabNames.ToArray());
            Assert.Equal("Gamepad", desktop.ActiveControlsTab);
        }
        finally
        {
            desktopWindow.Close();
            DisposeView(desktop);
        }

        SettingsView mobile = ExtractSettingsView(UiCapture.BuildFixture(
            "controls-mobile", new MenuSettings(), Array.Empty<string>()));
        var mobileWindow = new Window
        {
            Width = 560,
            Height = 800,
            Content = mobile
        };
        try
        {
            mobileWindow.Show();
            Assert.Equal(new[] { "Touch", "Gamepad", "Stylus" },
                mobile.ControlsTabNames.ToArray());
            Assert.Equal(new[] { "Touch" },
                mobile.VisibleControlsTabNames.ToArray());
            Assert.Equal("Touch", mobile.ActiveControlsTab);
            Assert.DoesNotContain("Mouse & Keyboard", mobile.ControlsTabNames);
            Assert.True(mobile.HasTouchControlRows);
            Assert.Contains(SettingRowIds.TouchButtons, mobile.RenderedRowIds);
            Assert.Contains(SettingRowIds.StylusAiming, mobile.RenderedRowIds);
        }
        finally
        {
            mobileWindow.Close();
            DisposeView(mobile);
        }
    }

    [AvaloniaFact]
    public void CaptureRoutesAndSeatOfferOverlayAreOpaqueWithPlausibleCountdowns()
    {
        Control control = UiCapture.BuildFixture("lobby-seat-offer",
            new MenuSettings(), new[] { "MP3 PROVING GROUND" });
        PrimeShellView view = Assert.IsType<PrimeShellView>(control);
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(940, 560));
            window.Arrange(new Rect(0, 0, 940, 560));

            ContentControl pageHost = view.GetVisualDescendants()
                .OfType<ContentControl>().Single(candidate => candidate.Name == "PageHost");
            Control page = Assert.IsAssignableFrom<Control>(pageHost.Content);
            SeatOfferCard offer = Assert.Single(view.GetVisualDescendants()
                .OfType<SeatOfferCard>());
            Assert.Equal(1, page.Opacity);
            Assert.Equal(1, offer.Opacity);

            string countdown = Assert.Single(offer.GetVisualDescendants()
                .OfType<TextBlock>().Select(text => text.Text),
                text => text?.StartsWith("Accept within ",
                    StringComparison.Ordinal) == true)!;
            string[] time = countdown[(countdown.LastIndexOf(' ') + 1)..].Split(':');
            Assert.Equal(2, time.Length);
            int seconds = int.Parse(time[0], CultureInfo.InvariantCulture) * 60
                + int.Parse(time[1], CultureInfo.InvariantCulture);
            Assert.InRange(seconds, 1, 12);
        }
        finally
        {
            window.Close();
            DisposeView(view);
        }

        Control results = UiCapture.BuildFixture("results-ballot",
            new MenuSettings(), Array.Empty<string>());
        var resultsWindow = new Window
        {
            Width = 940,
            Height = 560,
            Content = results
        };
        try
        {
            resultsWindow.Show();
            Dispatcher.UIThread.RunJobs();
            string voteCountdown = Assert.Single(results.GetVisualDescendants()
                .OfType<TextBlock>().Select(text => text.Text),
                text => text?.Contains("Voting closes in ",
                    StringComparison.Ordinal) == true)!;
            string digits = new(voteCountdown.Where(Char.IsDigit).ToArray());
            Assert.InRange(int.Parse(digits, CultureInfo.InvariantCulture), 1, 25);
        }
        finally
        {
            resultsWindow.Close();
            DisposeView(results);
        }

        string source = ReadRepositoryFile("src/Client/Launcher/Gui/UiCapture.cs");
        Assert.DoesNotContain("DateTimeOffset.MaxValue", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("new DateTimeOffset(2040", source,
            StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(940, 560)]
    [InlineData(560, 800)]
    public void SeatOfferCaptureUsesCenteredBoundedAutoHeightGeometry(
        int width, int height)
    {
        Control control = UiCapture.BuildFixture("lobby-seat-offer",
            new MenuSettings(), new[] { "MP3 PROVING GROUND" });
        PrimeShellView view = Assert.IsType<PrimeShellView>(control);
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));

            ContentControl overlay = view.GetVisualDescendants()
                .OfType<ContentControl>()
                .Single(candidate => candidate.Name == "OverlayHost");
            SeatOfferCard offer = Assert.Single(view.GetVisualDescendants()
                .OfType<SeatOfferCard>());
            Point origin = offer.TranslatePoint(default, overlay)
                ?? throw new Xunit.Sdk.XunitException(
                    "Seat offer could not be translated to its overlay.");

            Assert.Equal(HorizontalAlignment.Center, offer.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, offer.VerticalAlignment);
            Assert.True(Double.IsNaN(offer.Height));
            Assert.InRange(offer.Bounds.Width, 1, Math.Min(520, width - 48));
            Assert.InRange(offer.Bounds.Height, 1, height - 49);
            Assert.InRange(Math.Abs(origin.X + offer.Bounds.Width / 2
                - overlay.Bounds.Width / 2), 0, 1);
            Assert.InRange(Math.Abs(origin.Y + offer.Bounds.Height / 2
                - overlay.Bounds.Height / 2), 0, 8);
        }
        finally
        {
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaTheory]
    [InlineData(940, 560)]
    [InlineData(830, 390)]
    [InlineData(560, 800)]
    public void SettingsSlidersKeepLabelsTrackAndValueGutterSeparated(
        int width, int height)
    {
        Control control = UiCapture.BuildFixture("settings-controls",
            new MenuSettings(), Array.Empty<string>());
        SettingsView view = Assert.IsType<SettingsView>(control);
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(width, height));
            window.Arrange(new Rect(0, 0, width, height));

            SliderRow[] sliders = view.GetVisualDescendants().OfType<SliderRow>()
                .Where(slider => slider.IsEffectivelyVisible)
                .ToArray();
            Assert.NotEmpty(sliders);
            Assert.All(sliders, slider =>
            {
                Rect track = slider.RenderedTrack;
                Assert.True(slider.PreferredLabelRight
                    + SliderRow.RenderedLabelTrackGap <= track.X + 0.5,
                    $"Slider label reaches {slider.PreferredLabelRight:0.##} but track starts at {track.X:0.##}.");
                Assert.True(track.Width >= 72,
                    $"Slider track is only {track.Width:0.##} DIPs wide.");
                Assert.True(track.Right <= slider.Bounds.Width - 112 + 0.5,
                    "Slider track entered the value gutter.");
            });
        }
        finally
        {
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void SettingsFixturesConstructAndRenderTheirRegisteredRows()
    {
        MenuSettings settings = new();
        IReadOnlyList<string> rooms = Array.Empty<string>();

        foreach (string name in SettingsFixtureNames)
        {
            Control control = UiCapture.BuildFixture(name, settings, rooms);
            SettingsView view = ExtractSettingsView(control);
            try
            {
                Assert.NotEmpty(view.RenderedRowIds);
                SettingRegistry.ValidateRenderedRows(view.RenderedRowIds,
                    SettingPlatform.Desktop);
                if (name.StartsWith("settings-touch-buttons-", StringComparison.Ordinal))
                {
                    Assert.True(view.HasTouchControlRows);
                    Assert.Equal(name.EndsWith("-on", StringComparison.Ordinal),
                        view.RenderedTouchButtonsVisible);
                }
                if (name.StartsWith("settings-gyro-", StringComparison.Ordinal))
                {
                    Assert.Equal(name.EndsWith("-supported", StringComparison.Ordinal),
                        view.RenderedGyroControlsEnabled);
                }
                if (name.StartsWith("settings-advanced-controller-", StringComparison.Ordinal))
                {
                    Assert.Equal(name.EndsWith("-expanded", StringComparison.Ordinal),
                        view.RenderedAdvancedControllerExpanded);
                }
            }
            finally
            {
                DisposeView(control);
            }
        }
    }

    [AvaloniaFact]
    public void ProductionSettingsUseACollapsedAdvancedControllerExpanderByDefault()
    {
        var view = new SettingsView(new MenuSettings());
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            window.Show();
            view.ShowSection("Controls");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(false, view.RenderedAdvancedControllerExpanded);
        }
        finally
        {
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void ControlsTabsAreDesktopAwareAndPreservePendingEdits()
    {
        ControllerTuningSnapshot previous = CaptureControllerTuning();
        ControllerPreset previousPreset = InputSettings.ControllerPreset;
        var view = new SettingsView(new MenuSettings());
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            SetDefaultControllerTuningWithEnabledGyro();
            InputSettings.ControllerPreset = ControllerPreset.Classic;
            window.Show();
            view.ShowSection("Controls");

            Assert.Equal(new[] { "Mouse & Keyboard", "Gamepad", "Stylus" },
                view.ControlsTabNames.ToArray());
            Assert.Equal(new[] { "Mouse & Keyboard" },
                view.VisibleControlsTabNames.ToArray());
            Assert.Equal("Mouse & Keyboard", view.ActiveControlsTab);

            view.ShowControlsTab("Gamepad");
            SliderRow horizontal = GetPrivateField<SliderRow>(view,
                "_gamepadHorizontalSensitivity");
            int editedValue = NextSliderValue(horizontal);
            horizontal.Value = editedValue;

            // The same SliderRow remains alive while the local page changes;
            // switching back cannot rebuild it or lose its dirty marker.
            view.ShowControlsTab("Mouse & Keyboard");
            Assert.Equal(new[] { "Mouse & Keyboard" },
                view.VisibleControlsTabNames.ToArray());
            view.ShowControlsTab("Gamepad");
            Assert.Equal(editedValue, horizontal.Value);

            view.ShowControlsTab("Stylus");
            Assert.Equal(new[] { "Stylus" }, view.VisibleControlsTabNames.ToArray());
            Assert.Contains(SettingRowIds.StylusAiming, view.RenderedRowIds);
        }
        finally
        {
            window.Close();
            view.Dispose();
            RestoreControllerTuning(previous);
            InputSettings.ControllerPreset = previousPreset;
        }
    }

    [AvaloniaFact]
    public void ControlsTabsUseTouchGamepadAndStylusOnAndroidWithoutEmptyMouseTab()
    {
        var view = new SettingsView(new MenuSettings(), inGame: false, scene: null,
            captureTouchControls: true, captureGyroSupported: false,
            captureAdvancedControllerExpanded: false);
        var window = new Window { Width = 560, Height = 800, Content = view };
        try
        {
            window.Show();
            view.ShowSection("Controls");

            Assert.Equal(new[] { "Touch", "Gamepad", "Stylus" },
                view.ControlsTabNames.ToArray());
            Assert.Equal(new[] { "Touch" }, view.VisibleControlsTabNames.ToArray());
            Assert.DoesNotContain("Mouse & Keyboard", view.ControlsTabNames);
            Assert.True(view.HasTouchControlRows);
            Assert.Contains(SettingRowIds.TouchButtons, view.RenderedRowIds);
            Assert.Contains(SettingRowIds.StylusAiming, view.RenderedRowIds);
            Assert.False(view.RenderedAdvancedControllerExpanded);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public void SettingsGyroCapabilityUpdatesAreMarshalledAndPreferencesArePreserved()
    {
        bool previousPreference = MphRead.Mods.InputSettings.GamepadGyroEnabled;
        ControllerCapabilityOwner owner = ControllerCapabilities.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: true)!;
        var view = new SettingsView(new MenuSettings());
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            MphRead.Mods.InputSettings.GamepadGyroEnabled = true;
            window.Show();
            view.ShowSection("Controls");

            Assert.True(view.IsObservingControllerCapabilities);
            Assert.True(owner.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl, "gamepad:unknown", "Unknown pad",
                hasGyroscope: null)));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.RenderedGyroControlsEnabled);
            Assert.Contains("saved preference is preserved", view.RenderedGyroCapabilityNote,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(MphRead.Mods.InputSettings.GamepadGyroEnabled);

            Assert.True(owner.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl, "gamepad:gyro", "Gyro pad",
                hasGyroscope: true)));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.RenderedGyroControlsEnabled);

            Assert.True(owner.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl, "gamepad:no-gyro", "Basic pad",
                hasGyroscope: false)));
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.RenderedGyroControlsEnabled);
            Assert.True(MphRead.Mods.InputSettings.GamepadGyroEnabled);
        }
        finally
        {
            window.Close();
            Assert.False(view.IsObservingControllerCapabilities);
            view.Dispose();
            Assert.False(view.IsObservingControllerCapabilities);
            Assert.True(owner.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Sdl, "gamepad:late", "Late pad", hasGyroscope: true)));
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.RenderedGyroControlsEnabled);
            owner.Dispose();
            MphRead.Mods.InputSettings.GamepadGyroEnabled = previousPreference;
        }
    }

    [AvaloniaFact]
    public void SettingsIgnoresDelayedCapabilityEventAfterOwnerReplacement()
    {
        bool previousGyroPreference = MphRead.Mods.InputSettings.GamepadGyroEnabled;
        ControllerCapabilityOwner firstOwner = ControllerCapabilities.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: true)!;
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int callbackCount = 0;
        EventHandler<ControllerCapabilityChangedEventArgs> blocker = (_, _) =>
        {
            if (Interlocked.Increment(ref callbackCount) == 1)
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Capability event blocker timed out.");
                }
            }
        };
        ControllerCapabilities.Changed += blocker;
        ControllerCapabilityOwner? replacement = null;
        Task<bool>? delayedPublish = null;
        var view = new SettingsView(new MenuSettings());
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            MphRead.Mods.InputSettings.GamepadGyroEnabled = true;
            window.Show();
            view.ShowSection("Controls");

            delayedPublish = Task.Run(() => firstOwner.Publish(
                ControllerCapabilitySnapshot.Connected(
                    ControllerBackend.Sdl, "gamepad:old", "Old gyro pad",
                    hasGyroscope: true)));
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

            replacement = ControllerCapabilities.TryAcquire(
                ControllerBackend.Glfw, replaceCurrent: true)!;
            Assert.True(replacement.Publish(ControllerCapabilitySnapshot.Connected(
                ControllerBackend.Glfw, "gamepad:new", "New basic pad",
                hasGyroscope: false)));

            // Release the old publisher only after the replacement has
            // committed. Its queued UI callback is now deliberately stale.
            release.Set();
            Assert.True(delayedPublish.Wait(TimeSpan.FromSeconds(5)));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(ControllerBackend.Glfw, ControllerCapabilities.Current.Backend);
            Assert.False(ControllerCapabilities.Current.HasGyroscope);
            Assert.False(view.RenderedGyroControlsEnabled);
        }
        finally
        {
            release.Set();
            if (delayedPublish != null)
            {
                delayedPublish.Wait(TimeSpan.FromSeconds(5));
            }
            window.Close();
            view.Dispose();
            ControllerCapabilities.Changed -= blocker;
            replacement?.Dispose();
            firstOwner.Dispose();
            MphRead.Mods.InputSettings.GamepadGyroEnabled = previousGyroPreference;
        }
    }

    [AvaloniaFact]
    public void SettingsNoOpSavePreservesRawControllerTuningAndDisabledGyroPreference()
    {
        ControllerTuningSnapshot previous = CaptureControllerTuning();
        ControllerCapabilityOwner owner = ControllerCapabilities.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: true)!;
        Window? window = null;
        SettingsView? view = null;
        try
        {
            SetDefaultControllerTuningWithEnabledGyro();
            ControllerTuningSnapshot expected = CaptureControllerTuning();
            view = new SettingsView(new MenuSettings());
            window = new Window { Width = 940, Height = 560, Content = view };
            window.Show();
            view.CommitForTests();

            Assert.Equal(expected, CaptureControllerTuning());
        }
        finally
        {
            window?.Close();
            view?.Dispose();
            owner.Dispose();
            RestoreControllerTuning(previous);
        }
    }

    [AvaloniaFact]
    public void SettingsUnrelatedSavePreservesRawControllerTuningAndDisabledGyroPreference()
    {
        ControllerTuningSnapshot previous = CaptureControllerTuning();
        ControllerCapabilityOwner owner = ControllerCapabilities.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: true)!;
        Window? window = null;
        SettingsView? view = null;
        try
        {
            SetNonGridControllerTuning();
            ControllerTuningSnapshot expected = CaptureControllerTuning();
            view = new SettingsView(new MenuSettings());
            window = new Window { Width = 940, Height = 560, Content = view };
            window.Show();
            view.ShowSection("Controls");

            SliderRow mouseSensitivity = typeof(SettingsView)
                .GetField("_sensitivity", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(view) as SliderRow
                ?? throw new Xunit.Sdk.XunitException("Mouse slider was not built.");
            float previousMouseSensitivity = MphRead.Mods.InputSettings.MouseSensitivity;
            mouseSensitivity.Value = Math.Min(mouseSensitivity.Value + 1,
                (int)Math.Round(MphRead.Mods.InputSettings.UiMaximumMouseSensitivity
                    / MphRead.Mods.InputSettings.MouseSensitivityStep));
            Assert.NotEqual(previousMouseSensitivity,
                mouseSensitivity.Value * MphRead.Mods.InputSettings.MouseSensitivityStep);

            view.CommitForTests();

            Assert.Equal(expected, CaptureControllerTuning());
            Assert.NotEqual(previousMouseSensitivity,
                MphRead.Mods.InputSettings.MouseSensitivity);
        }
        finally
        {
            window?.Close();
            view?.Dispose();
            owner.Dispose();
            RestoreControllerTuning(previous);
        }
    }

    [AvaloniaFact]
    public void SettingsControllerGeneralResetClearsOnlyRefreshedSliderMarkers()
    {
        ControllerTuningSnapshot previous = CaptureControllerTuning();
        ControllerPreset previousPreset = InputSettings.ControllerPreset;
        ControllerCapabilityOwner owner = ControllerCapabilities.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: true)!;
        Window? window = null;
        SettingsView? view = null;
        try
        {
            SetDefaultControllerTuningWithEnabledGyro();
            InputSettings.ControllerPreset = ControllerPreset.Classic;
            view = new SettingsView(new MenuSettings());
            window = new Window { Width = 940, Height = 560, Content = view };
            window.Show();
            view.ShowSection("Controls");

            SliderRow horizontal = GetPrivateField<SliderRow>(view,
                "_gamepadHorizontalSensitivity");
            SliderRow exponent = GetPrivateField<SliderRow>(view, "_gamepadLook");
            horizontal.Value = NextSliderValue(horizontal);
            exponent.Value = NextSliderValue(exponent);
            int editedExponent = exponent.Value;

            InputSettings.ResetControllerGeneral();
            InvokePrivate(view, "RefreshControllerGeneralRows");
            view.CommitForTests();

            // General reset repopulates horizontal sensitivity, so its marker
            // must be gone. The response exponent was not refreshed and must
            // retain its deliberate edit (quantized exactly once on commit).
            Assert.Equal(1f, InputSettings.GamepadHorizontalSensitivity);
            float expectedExponent = .05f + editedExponent / 100f * 7.95f;
            Assert.Equal(expectedExponent, InputSettings.GamepadLookExponent);
            Assert.NotEqual(1.60f, InputSettings.GamepadLookExponent);
        }
        finally
        {
            window?.Close();
            view?.Dispose();
            owner.Dispose();
            RestoreControllerTuning(previous);
            InputSettings.ControllerPreset = previousPreset;
        }
    }

    [AvaloniaFact]
    public void SettingsControllerPresetRefreshClearsControllerSliderMarkers()
    {
        ControllerTuningSnapshot previous = CaptureControllerTuning();
        ControllerPreset previousPreset = InputSettings.ControllerPreset;
        Dictionary<PadAction, GamepadButtons> previousBindings = PadBindings.Actions
            .ToDictionary(action => action, PadBindings.Get);
        ControllerCapabilityOwner owner = ControllerCapabilities.TryAcquire(
            ControllerBackend.Sdl, replaceCurrent: true)!;
        Window? window = null;
        SettingsView? view = null;
        try
        {
            SetDefaultControllerTuningWithEnabledGyro();
            InputSettings.ControllerPreset = ControllerPreset.Classic;
            view = new SettingsView(new MenuSettings());
            window = new Window { Width = 940, Height = 560, Content = view };
            window.Show();
            view.ShowSection("Controls");

            SliderRow horizontal = GetPrivateField<SliderRow>(view,
                "_gamepadHorizontalSensitivity");
            SliderRow exponent = GetPrivateField<SliderRow>(view, "_gamepadLook");
            horizontal.Value = NextSliderValue(horizontal);
            exponent.Value = NextSliderValue(exponent);

            ChoiceRow preset = GetPrivateField<ChoiceRow>(view, "_gamepadPresetRow");
            Assert.Equal((int)ControllerPreset.Custom, preset.Index);
            preset.Index = (int)ControllerPreset.Classic;
            view.CommitForTests();

            // Selecting a named preset is a full authoritative controller
            // refresh. Both edited rows therefore commit the restored defaults,
            // rather than quantizing their stale UI values a second time.
            Assert.Equal(1f, InputSettings.GamepadHorizontalSensitivity);
            Assert.Equal(1.60f, InputSettings.GamepadLookExponent);
        }
        finally
        {
            window?.Close();
            view?.Dispose();
            owner.Dispose();
            foreach ((PadAction action, GamepadButtons buttons) in previousBindings)
            {
                PadBindings.Set(action, buttons);
            }
            RestoreControllerTuning(previous);
            InputSettings.ControllerPreset = previousPreset;
        }
    }

    [AvaloniaFact]
    public void SettingsIdentityContextSeparatesAccountAndGuestPresentation()
    {
        var account = SettingsIdentityContext.Authenticated("Account Pilot");
        var accountView = new SettingsView(new MenuSettings(), identity: account);
        var accountWindow = new Window { Width = 940, Height = 560, Content = accountView };
        try
        {
            accountWindow.Show();
            Assert.True(accountView.IdentityContext.IsAuthenticated);
            Assert.Equal("Account Pilot", accountView.IdentityContext.DisplayName);
            Assert.True(accountView.HasHunterProfileAction);
            TextBox accountName = Assert.Single(accountView.GetVisualDescendants()
                .OfType<TextBox>());
            Assert.Equal("Account Pilot", accountName.Text);
            Assert.True(accountName.IsReadOnly);
            Assert.False(accountName.IsEnabled);
        }
        finally
        {
            accountWindow.Close();
            accountView.Dispose();
        }

        var guest = SettingsIdentityContext.Guest("Local Guest");
        var guestView = new SettingsView(new MenuSettings(), identity: guest);
        var guestWindow = new Window { Width = 940, Height = 560, Content = guestView };
        try
        {
            guestWindow.Show();
            Assert.False(guestView.IdentityContext.IsAuthenticated);
            Assert.True(guestView.IdentityContext.IsGuest);
            Assert.False(guestView.HasHunterProfileAction);
            TextBox guestName = Assert.Single(guestView.GetVisualDescendants()
                .OfType<TextBox>());
            Assert.Equal("Local Guest", guestName.Text);
            Assert.False(guestName.IsReadOnly);
        }
        finally
        {
            guestWindow.Close();
            guestView.Dispose();
        }
    }

    [AvaloniaFact]
    public void AimAssistHasNoPlayerFacingSettingsSurface()
    {
        var view = new SettingsView(new MenuSettings());
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            window.Show();
            view.ShowSection("Controls");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            Assert.DoesNotContain(view.RenderedRowIds, rowId =>
                rowId.Contains("aim-assist", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text?.Contains("aim assist", StringComparison.OrdinalIgnoreCase) == true);
        }
        finally
        {
            window.Close();
            DisposeView(view);
        }
    }

    [AvaloniaFact]
    public void CaptureSettingsVariantsRestoreGlobalInputState()
    {
        bool previousGyro = MphRead.Mods.InputSettings.GamepadGyroEnabled;
        bool previousTouch = MphRead.Mods.Input.TouchSettings.ButtonsVisible;
        try
        {
            Control gyro = UiCapture.BuildFixture("settings-gyro-supported",
                new MenuSettings(), Array.Empty<string>());
            Assert.True(MphRead.Mods.InputSettings.GamepadGyroEnabled);
            DisposeView(gyro);
            Assert.Equal(previousGyro, MphRead.Mods.InputSettings.GamepadGyroEnabled);

            Control touch = UiCapture.BuildFixture("settings-touch-buttons-off",
                new MenuSettings(), Array.Empty<string>());
            Assert.False(MphRead.Mods.Input.TouchSettings.ButtonsVisible);
            DisposeView(touch);
            Assert.Equal(previousTouch, MphRead.Mods.Input.TouchSettings.ButtonsVisible);
        }
        finally
        {
            MphRead.Mods.InputSettings.GamepadGyroEnabled = previousGyro;
            MphRead.Mods.Input.TouchSettings.ButtonsVisible = previousTouch;
        }
    }

    [AvaloniaFact]
    public void UnavailableStatesAreDocumentedInsteadOfAliasedToDefaultViews()
    {
        Assert.All(UiCapture.PlannedButUnavailableFixtures, name =>
            Assert.Throws<KeyNotFoundException>(() => UiCapture.GetFixture(name)));
    }

    [AvaloniaFact]
    public void ProHudFixtureKeepsItsStateUntilTheViewIsDisposed()
    {
        bool previous = Features.ProHud;
        Control off = UiCapture.BuildFixture("settings-pro-hud-off",
            new MenuSettings(), Array.Empty<string>());
        try
        {
            Assert.False(Features.ProHud);
            Assert.IsType<SettingsView>(ExtractSettingsView(off));
        }
        finally
        {
            DisposeView(off);
        }
        Assert.Equal(previous, Features.ProHud);

        Control on = UiCapture.BuildFixture("settings-pro-hud-on",
            new MenuSettings(), Array.Empty<string>());
        try
        {
            Assert.True(Features.ProHud);
            Assert.IsType<SettingsView>(ExtractSettingsView(on));
        }
        finally
        {
            DisposeView(on);
        }
        Assert.Equal(previous, Features.ProHud);
    }

    [AvaloniaFact]
    public void RadarCustomFixtureKeepsItsStateUntilTheViewIsDisposed()
    {
        global::MphRead.Hud.Radar.RadarStyle previousStyle
            = global::MphRead.Hud.Radar.RadarSettings.Style;
        global::MphRead.Hud.Radar.RadarOrientation previousOrientation
            = global::MphRead.Hud.Radar.RadarSettings.Orientation;
        global::MphRead.Hud.Radar.RadarAnchor previousAnchor
            = global::MphRead.Hud.Radar.RadarSettings.Anchor;
        float previousScale = global::MphRead.Hud.Radar.RadarSettings.Scale;
        float previousOffsetX = global::MphRead.Hud.Radar.RadarSettings.OffsetX;
        float previousOffsetY = global::MphRead.Hud.Radar.RadarSettings.OffsetY;

        Control custom = UiCapture.BuildFixture("settings-radar-custom",
            new MenuSettings(), Array.Empty<string>());
        try
        {
            Assert.Equal(global::MphRead.Hud.Radar.RadarStyle.Enhanced,
                global::MphRead.Hud.Radar.RadarSettings.Style);
            Assert.Equal(global::MphRead.Hud.Radar.RadarOrientation.North,
                global::MphRead.Hud.Radar.RadarSettings.Orientation);
            Assert.Equal(global::MphRead.Hud.Radar.RadarAnchor.Custom,
                global::MphRead.Hud.Radar.RadarSettings.Anchor);
            Assert.Equal(1.25f, global::MphRead.Hud.Radar.RadarSettings.Scale);
            Assert.Equal(24, global::MphRead.Hud.Radar.RadarSettings.OffsetX);
            Assert.Equal(-16, global::MphRead.Hud.Radar.RadarSettings.OffsetY);
            SettingsView settingsView = Assert.IsType<SettingsView>(
                ExtractSettingsView(custom));
            Expander advanced = Assert.Single(Walk(settingsView).OfType<Expander>(),
                item => Equals(item.Header, "Advanced radar"));
            Assert.False(advanced.IsExpanded);
            Assert.Contains(Walk(Assert.IsAssignableFrom<Control>(advanced.Content)),
                control => control.Name == SettingRowIds.RadarMarkerScale);
            Assert.DoesNotContain(Walk(Assert.IsAssignableFrom<Control>(advanced.Content)),
                control => control is RadarLayoutEditorPreview);
            Assert.Single(Walk(settingsView).OfType<RadarLayoutEditorPreview>());
        }
        finally
        {
            DisposeView(custom);
        }

        Assert.Equal(previousStyle, global::MphRead.Hud.Radar.RadarSettings.Style);
        Assert.Equal(previousOrientation, global::MphRead.Hud.Radar.RadarSettings.Orientation);
        Assert.Equal(previousAnchor, global::MphRead.Hud.Radar.RadarSettings.Anchor);
        Assert.Equal(previousScale, global::MphRead.Hud.Radar.RadarSettings.Scale);
        Assert.Equal(previousOffsetX, global::MphRead.Hud.Radar.RadarSettings.OffsetX);
        Assert.Equal(previousOffsetY, global::MphRead.Hud.Radar.RadarSettings.OffsetY);
    }

    [AvaloniaFact]
    public void CaptureHunterAndArsenalDoNotStartPreviewWorkers()
    {
        string[] fixtureNames =
        {
            "hunter-overview", "hunter-arsenal", "hunter-roster", "hunter-career",
            "hunter-matches", "hunter-empty-history", "hunter-preview-failure"
        };

        foreach (string fixtureName in fixtureNames)
        {
            Control control = UiCapture.BuildFixture(fixtureName, new MenuSettings(),
                Array.Empty<string>());
            PrimeShellView view = Assert.IsType<PrimeShellView>(control);
            var window = new Window { Width = 940, Height = 560, Content = view };
            try
            {
                window.Show();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                Assert.True(view.CaptureMode);
                Assert.Equal(0, view.HunterPreviewLoadStarts);
                Assert.Equal(0, view.WeaponPreviewLoadStarts);

                if (fixtureName == "hunter-preview-failure")
                {
                    Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == "Preview unavailable.");
                    Assert.Contains(view.GetVisualDescendants().OfType<AvaloniaButton>(),
                        button => button.Content?.ToString() == "Retry preview");
                }
                else if (fixtureName is "hunter-overview" or "hunter-arsenal"
                    or "hunter-roster")
                {
                    Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                        text => text.Text == "Preview omitted for offline capture.");
                }
            }
            finally
            {
                window.Close();
                DisposeView(view);
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("results-ffa", MatchMode.Battle, PostMatchBallotState.Loading, 3)]
    [InlineData("results-team", MatchMode.TeamBattle, PostMatchBallotState.Loading, 4)]
    [InlineData("results-ballot", MatchMode.Battle, PostMatchBallotState.Open, 3)]
    [InlineData("results-voted", MatchMode.Battle, PostMatchBallotState.Voted, 3)]
    [InlineData("results-resolved", MatchMode.Battle, PostMatchBallotState.Resolved, 3)]
    public void ResultsFixturesUseConcreteResultAndBallotStates(string fixtureName,
        MatchMode mode, PostMatchBallotState ballotState, int expectedRows)
    {
        Control control = UiCapture.BuildFixture(fixtureName, new MenuSettings(),
            Array.Empty<string>());
        PostMatchView view = Assert.IsType<PostMatchView>(control);
        try
        {
            Assert.True(view.Results.HasAuthoritativeResult);
            Assert.Equal(mode, view.Results.Mode);
            Assert.Equal(expectedRows, view.Results.Scoreboard.Length);
            Assert.All(view.Results.Scoreboard,
                row => Assert.False(String.IsNullOrWhiteSpace(row.Name)));
            Assert.Equal(ballotState, view.Ballot.State);
            if (ballotState is PostMatchBallotState.Open or PostMatchBallotState.Voted
                or PostMatchBallotState.Resolved)
            {
                Assert.NotEmpty(view.Ballot.Options);
                Assert.True(view.Ballot.HasAuthoritativeDeadline);
            }
        }
        finally
        {
            view.Dispose();
        }
    }

    [AvaloniaFact]
    public void ResultsNoAuthoritativeResultFixtureStaysExplicitlyUnavailable()
    {
        Control control = UiCapture.BuildFixture("results-no-authoritative-result",
            new MenuSettings(), Array.Empty<string>());
        PostMatchView view = Assert.IsType<PostMatchView>(control);
        try
        {
            Assert.False(view.Results.HasAuthoritativeResult);
            Assert.Empty(view.Results.Scoreboard);
            Assert.Equal(PostMatchBallotState.Loading, view.Ballot.State);
        }
        finally
        {
            view.Dispose();
        }
    }

    [Fact]
    public void UiShotDispatchesBeforeTheGameFileSetupGate()
    {
        string modEntry = ReadRepositoryFile("src/Client/Runtime/ModEntry.cs");
        int preferencesLoad = modEntry.IndexOf(
            "Launcher.LauncherPrefs.Load();", StringComparison.Ordinal);
        int uiShotDispatch = modEntry.IndexOf(
            "string? uiShot = ValueAfter(args, \"uishot\");",
            StringComparison.Ordinal);
        int normalDispatch = modEntry.IndexOf(
            "public static bool TryHandle(string[] args)",
            StringComparison.Ordinal);

        Assert.True(preferencesLoad >= 0 && uiShotDispatch > preferencesLoad
            && uiShotDispatch < normalDispatch,
            "uishot must dispatch from TryHandleHeadless after preferences load.");
        Assert.Equal(1, modEntry.Split(
            "string? uiShot = ValueAfter(args, \"uishot\");",
            StringSplitOptions.None).Length - 1);

        string program = ReadRepositoryFile("src/Client/Program.cs");
        int headlessDispatch = program.IndexOf(
            "if (Mods.ModEntry.TryHandleHeadless(args))",
            StringComparison.Ordinal);
        int setupGate = program.IndexOf("if (CheckSetup(args))",
            StringComparison.Ordinal);
        Assert.True(headlessDispatch >= 0 && setupGate > headlessDispatch,
            "Program must invoke headless mod dispatch before CheckSetup.");
    }

    [AvaloniaFact]
    public void EveryFixtureRendersAtEveryRequiredViewport()
    {
        MenuSettings settings = new();
        IReadOnlyList<string> rooms = new[] { "MP3 PROVING GROUND" };
        string? captureDirectory = Environment.GetEnvironmentVariable(
            "PRIME_UI_CAPTURE_DIRECTORY");
        if (!String.IsNullOrWhiteSpace(captureDirectory))
            Directory.CreateDirectory(captureDirectory);
        foreach (UiCaptureFixtureDefinition fixture in UiCapture.FixtureDefinitions)
        {
            string fixtureName = fixture.Name;
            foreach (UiCaptureSize captureSize in UiCapture.RequiredSizes)
            {
                Control view = fixture.Build(settings, rooms);
                Window window = new()
                {
                    Width = captureSize.Width,
                    Height = captureSize.Height,
                    Content = view
                };
                try
                {
                    window.Show();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using WriteableBitmap frame = window.CaptureRenderedFrame()!;

                    Assert.Equal(new PixelSize(captureSize.Width, captureSize.Height),
                        frame.PixelSize);
                    Assert.Equal(new Size(captureSize.Width, captureSize.Height),
                        view.Bounds.Size);
                    Assert.NotEmpty(view.GetVisualDescendants().OfType<Control>());
                    AssertFrameContainsPixels(frame, fixtureName, captureSize);
                    AssertFiniteBounds(view, fixtureName);
                    AssertVisibleFocusableTargetsStayInViewport(view, captureSize, fixtureName);
                    AssertLinearChildrenDoNotOverlap(view, fixtureName);
                    if (!String.IsNullOrWhiteSpace(captureDirectory))
                    {
                        string path = Path.Combine(captureDirectory,
                            $"{fixtureName}-{captureSize.Name}.png");
                        frame.Save(path);
                    }
                }
                finally
                {
                    window.Close();
                    DisposeView(view);
                }
            }
        }
    }

    [AvaloniaFact]
    public void ResultsLeaveActionIsReachableThroughAConcreteHeadlessControl()
    {
        Control control = UiCapture.BuildFixture("results-no-authoritative-result",
            new MenuSettings(), Array.Empty<string>());
        var view = Assert.IsType<PostMatchView>(control);
        Window window = new() { Width = 940, Height = 560, Content = view };
        try
        {
            window.Show();
            var leave = view.GetVisualDescendants().OfType<AvaloniaButton>()
                .Single(button => button.Name == "ResultsLeaveLobby");
            bool requested = false;
            view.LeaveRequested += () => requested = true;

            leave.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(AvaloniaButton.ClickEvent));

            Assert.True(view.LeaveConfirmationPending);
            Assert.False(requested);
            Assert.Equal("Confirm Leave Lobby", leave.Content);

            leave.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(AvaloniaButton.ClickEvent));

            Assert.False(view.LeaveConfirmationPending);
            Assert.True(requested);
            Assert.True(leave.Focusable);
        }
        finally
        {
            window.Close();
            view.Dispose();
        }
    }

    private static string TextOf(Control root)
        => String.Join('\n', Walk(root).OfType<TextBlock>()
            .Select(text => text.Text).OfType<string>());

    private static IEnumerable<Control> Walk(Control control)
    {
        yield return control;
        if (control is Panel panel)
        {
            foreach (Control child in panel.Children)
            {
                foreach (Control descendant in Walk(child))
                    yield return descendant;
            }
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

    private static void DisposeView(Control view)
    {
        if (view is PrimeShellView shell)
        {
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        else if (view is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private readonly record struct ControllerTuningSnapshot(
        float MoveDeadZone,
        float LookDeadZone,
        float OuterDeadZone,
        float MoveActivateThreshold,
        float MoveReleaseThreshold,
        float LookExponent,
        float YawRate,
        float PitchRate,
        float OuterBoostStart,
        float OuterYawBoost,
        float OuterPitchBoost,
        float BoostDelaySeconds,
        float BoostRampSeconds,
        float TriggerPressThreshold,
        float TriggerReleaseThreshold,
        float HorizontalSensitivity,
        float VerticalSensitivity,
        float ZoomHorizontalMultiplier,
        float ZoomVerticalMultiplier,
        bool AutoCalibration,
        GamepadStickAimMode StickAimMode,
        bool OuterBoostEnabled,
        bool GyroEnabled,
        float GyroSensitivity,
        bool GyroInvertX,
        bool GyroInvertY);

    private static ControllerTuningSnapshot CaptureControllerTuning()
        => new(
            MphRead.Mods.InputSettings.GamepadMoveDeadZone,
            MphRead.Mods.InputSettings.GamepadLookDeadZone,
            MphRead.Mods.InputSettings.GamepadOuterDeadZone,
            MphRead.Mods.InputSettings.GamepadMoveActivateThreshold,
            MphRead.Mods.InputSettings.GamepadMoveReleaseThreshold,
            MphRead.Mods.InputSettings.GamepadLookExponent,
            MphRead.Mods.InputSettings.GamepadYawRate,
            MphRead.Mods.InputSettings.GamepadPitchRate,
            MphRead.Mods.InputSettings.GamepadOuterBoostStart,
            MphRead.Mods.InputSettings.GamepadOuterYawBoost,
            MphRead.Mods.InputSettings.GamepadOuterPitchBoost,
            MphRead.Mods.InputSettings.GamepadBoostDelaySeconds,
            MphRead.Mods.InputSettings.GamepadBoostRampSeconds,
            MphRead.Mods.InputSettings.GamepadTriggerPressThreshold,
            MphRead.Mods.InputSettings.GamepadTriggerReleaseThreshold,
            MphRead.Mods.InputSettings.GamepadHorizontalSensitivity,
            MphRead.Mods.InputSettings.GamepadVerticalSensitivity,
            MphRead.Mods.InputSettings.GamepadZoomHorizontalMultiplier,
            MphRead.Mods.InputSettings.GamepadZoomVerticalMultiplier,
            MphRead.Mods.InputSettings.GamepadAutoCalibrationEnabled,
            MphRead.Mods.InputSettings.GamepadStickAimMode,
            MphRead.Mods.InputSettings.GamepadOuterBoostEnabled,
            MphRead.Mods.InputSettings.GamepadGyroEnabled,
            MphRead.Mods.InputSettings.GamepadGyroSensitivity,
            MphRead.Mods.InputSettings.GamepadGyroInvertX,
            MphRead.Mods.InputSettings.GamepadGyroInvertY);

    private static void SetNonGridControllerTuning()
    {
        MphRead.Mods.InputSettings.GamepadMoveDeadZone = .333f;
        MphRead.Mods.InputSettings.GamepadLookDeadZone = .444f;
        MphRead.Mods.InputSettings.GamepadOuterDeadZone = .277f;
        MphRead.Mods.InputSettings.GamepadMoveActivateThreshold = .777f;
        MphRead.Mods.InputSettings.GamepadMoveReleaseThreshold = .555f;
        MphRead.Mods.InputSettings.GamepadLookExponent = 2.345f;
        MphRead.Mods.InputSettings.GamepadYawRate = 333.5f;
        MphRead.Mods.InputSettings.GamepadPitchRate = 444.5f;
        MphRead.Mods.InputSettings.GamepadOuterBoostStart = .876f;
        MphRead.Mods.InputSettings.GamepadOuterYawBoost = 777.5f;
        MphRead.Mods.InputSettings.GamepadOuterPitchBoost = 666.5f;
        MphRead.Mods.InputSettings.GamepadBoostDelaySeconds = .1234f;
        MphRead.Mods.InputSettings.GamepadBoostRampSeconds = .2345f;
        MphRead.Mods.InputSettings.GamepadTriggerPressThreshold = .876f;
        MphRead.Mods.InputSettings.GamepadTriggerReleaseThreshold = .543f;
        MphRead.Mods.InputSettings.GamepadHorizontalSensitivity = 1.234f;
        MphRead.Mods.InputSettings.GamepadVerticalSensitivity = 2.345f;
        MphRead.Mods.InputSettings.GamepadZoomMultiplier = 3.456f;
        MphRead.Mods.InputSettings.GamepadOuterBoostEnabled = true;
        MphRead.Mods.InputSettings.GamepadGyroEnabled = true;
        MphRead.Mods.InputSettings.GamepadGyroSensitivity = 4.567f;
        MphRead.Mods.InputSettings.GamepadGyroInvertX = true;
        MphRead.Mods.InputSettings.GamepadGyroInvertY = false;
    }

    private static void SetDefaultControllerTuningWithEnabledGyro()
    {
        MphRead.Mods.InputSettings.GamepadMoveDeadZone = .15f;
        MphRead.Mods.InputSettings.GamepadLookDeadZone = .10f;
        MphRead.Mods.InputSettings.GamepadOuterDeadZone = .02f;
        MphRead.Mods.InputSettings.GamepadMoveActivateThreshold = .25f;
        MphRead.Mods.InputSettings.GamepadMoveReleaseThreshold = .18f;
        MphRead.Mods.InputSettings.GamepadLookExponent = 1.60f;
        MphRead.Mods.InputSettings.GamepadYawRate = 300;
        MphRead.Mods.InputSettings.GamepadPitchRate = 240;
        MphRead.Mods.InputSettings.GamepadOuterBoostStart = .95f;
        MphRead.Mods.InputSettings.GamepadOuterYawBoost = 150;
        MphRead.Mods.InputSettings.GamepadOuterPitchBoost = 80;
        MphRead.Mods.InputSettings.GamepadBoostDelaySeconds = .18f;
        MphRead.Mods.InputSettings.GamepadBoostRampSeconds = .12f;
        MphRead.Mods.InputSettings.GamepadTriggerPressThreshold = .20f;
        MphRead.Mods.InputSettings.GamepadTriggerReleaseThreshold = .12f;
        MphRead.Mods.InputSettings.GamepadHorizontalSensitivity = 1f;
        MphRead.Mods.InputSettings.GamepadVerticalSensitivity = 1f;
        MphRead.Mods.InputSettings.GamepadZoomMultiplier = 1f;
        MphRead.Mods.InputSettings.GamepadOuterBoostEnabled = true;
        MphRead.Mods.InputSettings.GamepadGyroEnabled = true;
        MphRead.Mods.InputSettings.GamepadGyroSensitivity = 1f;
        MphRead.Mods.InputSettings.GamepadGyroInvertX = false;
        MphRead.Mods.InputSettings.GamepadGyroInvertY = false;
    }

    private static void RestoreControllerTuning(ControllerTuningSnapshot snapshot)
    {
        MphRead.Mods.InputSettings.GamepadMoveDeadZone = snapshot.MoveDeadZone;
        MphRead.Mods.InputSettings.GamepadLookDeadZone = snapshot.LookDeadZone;
        MphRead.Mods.InputSettings.GamepadOuterDeadZone = snapshot.OuterDeadZone;
        MphRead.Mods.InputSettings.GamepadMoveActivateThreshold
            = snapshot.MoveActivateThreshold;
        MphRead.Mods.InputSettings.GamepadMoveReleaseThreshold
            = snapshot.MoveReleaseThreshold;
        MphRead.Mods.InputSettings.GamepadLookExponent = snapshot.LookExponent;
        MphRead.Mods.InputSettings.GamepadYawRate = snapshot.YawRate;
        MphRead.Mods.InputSettings.GamepadPitchRate = snapshot.PitchRate;
        MphRead.Mods.InputSettings.GamepadOuterBoostStart = snapshot.OuterBoostStart;
        MphRead.Mods.InputSettings.GamepadOuterYawBoost = snapshot.OuterYawBoost;
        MphRead.Mods.InputSettings.GamepadOuterPitchBoost = snapshot.OuterPitchBoost;
        MphRead.Mods.InputSettings.GamepadBoostDelaySeconds = snapshot.BoostDelaySeconds;
        MphRead.Mods.InputSettings.GamepadBoostRampSeconds = snapshot.BoostRampSeconds;
        MphRead.Mods.InputSettings.GamepadTriggerPressThreshold
            = snapshot.TriggerPressThreshold;
        MphRead.Mods.InputSettings.GamepadTriggerReleaseThreshold
            = snapshot.TriggerReleaseThreshold;
        MphRead.Mods.InputSettings.GamepadHorizontalSensitivity
            = snapshot.HorizontalSensitivity;
        MphRead.Mods.InputSettings.GamepadVerticalSensitivity
            = snapshot.VerticalSensitivity;
        MphRead.Mods.InputSettings.GamepadZoomHorizontalMultiplier
            = snapshot.ZoomHorizontalMultiplier;
        MphRead.Mods.InputSettings.GamepadZoomVerticalMultiplier
            = snapshot.ZoomVerticalMultiplier;
        MphRead.Mods.InputSettings.GamepadAutoCalibrationEnabled
            = snapshot.AutoCalibration;
        MphRead.Mods.InputSettings.GamepadStickAimMode = snapshot.StickAimMode;
        MphRead.Mods.InputSettings.GamepadOuterBoostEnabled = snapshot.OuterBoostEnabled;
        MphRead.Mods.InputSettings.GamepadGyroEnabled = snapshot.GyroEnabled;
        MphRead.Mods.InputSettings.GamepadGyroSensitivity = snapshot.GyroSensitivity;
        MphRead.Mods.InputSettings.GamepadGyroInvertX = snapshot.GyroInvertX;
        MphRead.Mods.InputSettings.GamepadGyroInvertY = snapshot.GyroInvertY;
    }

    private static T GetPrivateField<T>(SettingsView view, string name)
        where T : class
        => typeof(SettingsView).GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(view) as T
            ?? throw new Xunit.Sdk.XunitException(
                $"SettingsView field '{name}' was not available.");

    private static void InvokePrivate(SettingsView view, string name)
    {
        MethodInfo? method = typeof(SettingsView).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
        {
            throw new Xunit.Sdk.XunitException(
                $"SettingsView method '{name}' was not available.");
        }
        method.Invoke(view, null);
    }

    private static int NextSliderValue(SliderRow row)
        => row.Value == 100 ? row.Value - 1 : row.Value + 1;

    private static SettingsView ExtractSettingsView(Control control)
        => control as SettingsView
            ?? (control as ContentControl)?.Content as SettingsView
            ?? throw new Xunit.Sdk.XunitException(
                $"Fixture returned {control.GetType().Name}, not a SettingsView.");

    private static T ExtractRouteContent<T>(Control fixture) where T : Control
    {
        Control content = fixture is PrimeShellView shell
            ? shell.CaptureRouteContent ?? throw new Xunit.Sdk.XunitException(
                "Shell capture did not mount route content.")
            : fixture;
        return Assert.IsAssignableFrom<T>(content);
    }

    private static void AssertFiniteBounds(Control view, string fixtureName)
    {
        Assert.All(view.GetVisualDescendants().OfType<Control>(), control =>
        {
            Assert.True(double.IsFinite(control.Bounds.X),
                $"{fixtureName}: {control.GetType().Name} has a non-finite X bound.");
            Assert.True(double.IsFinite(control.Bounds.Y),
                $"{fixtureName}: {control.GetType().Name} has a non-finite Y bound.");
            Assert.True(double.IsFinite(control.Bounds.Width),
                $"{fixtureName}: {control.GetType().Name} has a non-finite width.");
            Assert.True(double.IsFinite(control.Bounds.Height),
                $"{fixtureName}: {control.GetType().Name} has a non-finite height.");
        });
    }

    private static void AssertVisibleFocusableTargetsStayInViewport(Control view,
        UiCaptureSize captureSize, string fixtureName)
    {
        Rect viewport = new(captureSize.AvaloniaSize);
        Control[] focusable = view.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Focusable && control.IsEffectivelyVisible
                && control.IsEffectivelyEnabled)
            .ToArray();
        Assert.True(focusable.Length > 0,
            $"{fixtureName}: fixture has no visible focusable target.");
        Control[] targets = focusable
            .Where(control => !control.GetVisualAncestors().OfType<ScrollViewer>().Any())
            .ToArray();

        foreach (Control target in targets)
        {
            Point? origin = target.TranslatePoint(new Point(), view);
            Assert.True(origin.HasValue,
                $"{fixtureName}: could not translate {target.GetType().Name} to its root.");
            Rect bounds = new(origin!.Value, target.Bounds.Size);
            const double tolerance = 1.0;
            Assert.True(bounds.Left >= viewport.Left - tolerance
                && bounds.Top >= viewport.Top - tolerance
                && bounds.Right <= viewport.Right + tolerance
                && bounds.Bottom <= viewport.Bottom + tolerance,
                $"{fixtureName}: focusable {target.GetType().Name} is outside the viewport "
                + $"({bounds} vs {viewport}).");
        }
    }

    private static void AssertLinearChildrenDoNotOverlap(Control view, string fixtureName)
    {
        // Grid cells may intentionally layer content. ScrollViewer contents
        // may intentionally extend beyond the viewport. Restrict the check
        // to visible direct children of linear panels outside scrolling so
        // font and platform differences do not turn this into a pixel hash.
        foreach (Panel panel in view.GetVisualDescendants().OfType<Panel>()
            .Where(panel => panel is StackPanel or WrapPanel
                && !panel.GetVisualAncestors().OfType<ScrollViewer>().Any()))
        {
            Control[] children = panel.Children.OfType<Control>()
                .Where(control => control.IsEffectivelyVisible
                    && control.Bounds.Width > 0 && control.Bounds.Height > 0)
                .ToArray();
            for (int i = 0; i < children.Length; i++)
            {
                for (int j = i + 1; j < children.Length; j++)
                {
                    Rect left = children[i].Bounds;
                    Rect right = children[j].Bounds;
                    double width = Math.Min(left.Right, right.Right)
                        - Math.Max(left.Left, right.Left);
                    double height = Math.Min(left.Bottom, right.Bottom)
                        - Math.Max(left.Top, right.Top);
                    Assert.True(width <= 1 || height <= 1,
                        $"{fixtureName}: {panel.GetType().Name} children "
                        + $"{children[i].GetType().Name} and {children[j].GetType().Name} overlap.");
                }
            }
        }
    }

    private static void AssertFrameContainsPixels(WriteableBitmap frame,
        string fixtureName, UiCaptureSize captureSize)
    {
        using var encodedStream = new MemoryStream();
        frame.Save(encodedStream);
        byte[] encoded = encodedStream.ToArray();
        Assert.True(encoded.Length > 32,
            $"{fixtureName}-{captureSize.Name}: rendered PNG is empty.");
        using var compressed = new MemoryStream();
        int offset = 8; // PNG signature.
        while (offset + 12 <= encoded.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(offset, 4));
            Assert.True(length >= 0 && offset <= encoded.Length - 12 - length,
                $"{fixtureName}-{captureSize.Name}: malformed rendered PNG.");
            if (encoded[offset + 4] == (byte)'I' && encoded[offset + 5] == (byte)'D'
                && encoded[offset + 6] == (byte)'A' && encoded[offset + 7] == (byte)'T')
            {
                compressed.Write(encoded, offset + 8, length);
            }
            offset += 12 + length;
        }
        Assert.True(compressed.Length > 0,
            $"{fixtureName}-{captureSize.Name}: rendered PNG has no pixel data.");
        compressed.Position = 0;
        using var pixels = new MemoryStream();
        using (var inflater = new ZLibStream(compressed, CompressionMode.Decompress,
            leaveOpen: true))
        {
            inflater.CopyTo(pixels);
        }
        Assert.True(pixels.ToArray().Any(pixel => pixel != 0),
            $"{fixtureName}-{captureSize.Name}: rendered pixels are all zero.");
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Game.sln")))
            root = Directory.GetParent(root)?.FullName;
        return File.ReadAllText(Path.Combine(root ?? throw new InvalidOperationException(
            "Repository root was not found."), relativePath));
    }
}
