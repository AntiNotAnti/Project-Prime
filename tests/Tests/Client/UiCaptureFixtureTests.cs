using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using AvaloniaButton = Avalonia.Controls.Button;
using MphRead;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Settings;
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
        "gateway-default", "gateway-login", "gateway-register", "gateway-confirm",
        "gateway-error", "gateway-guest",
        "play-home", "play-finding", "play-empty", "play-browser", "play-browser-full",
        "play-network-error", "play-advanced-network",
        "lobby-owner-team", "lobby-owner-ffa", "lobby-member-team", "lobby-observer",
        "lobby-full", "lobby-waitlist", "lobby-seat-offer", "lobby-chat",
        "lobby-disconnected", "lobby-handoff-failure", "lobby-postmatch",
        "results-ffa", "results-team", "results-ballot", "results-voted", "results-resolved",
        "results-no-authoritative-result",
        "settings-gameplay", "settings-controls", "settings-graphics", "settings-audio",
        "settings-system", "settings-network", "settings-accessibility", "settings-about",
        "settings-pro-hud-off", "settings-pro-hud-on", "settings-radar-custom",
        "settings-gyro-unsupported", "settings-gyro-supported",
        "settings-touch-buttons-off", "settings-touch-buttons-on",
        "settings-advanced-controller-collapsed", "settings-advanced-controller-expanded",
        "hunter-overview", "hunter-arsenal", "hunter-roster", "hunter-career",
        "hunter-matches", "hunter-empty-history", "hunter-preview-failure"
    };

    private static readonly string[] UnavailableFixtureNames = Array.Empty<string>();

    private static readonly string[] SettingsFixtureNames =
    {
        "settings-gameplay", "settings-controls", "settings-graphics", "settings-audio",
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
        Assert.Equal(new[] { "1920x1080", "2560x1440", "1280x720", "940x560", "900x1100", "560x800" },
            sizes.Select(size => size.Name).ToArray());
        Assert.Equal(new[] { (1920, 1080), (2560, 1440), (1280, 720),
            (940, 560), (900, 1100), (560, 800) },
            sizes.Select(size => (size.Width, size.Height)).ToArray());
        Assert.Equal(RequiredFixtureNames,
            UiCapture.FixtureDefinitions.Select(fixture => fixture.Name).ToArray());
        Assert.Equal(UnavailableFixtureNames,
            UiCapture.PlannedButUnavailableFixtures.ToArray());
        Assert.Empty(RequiredFixtureNames.Intersect(UnavailableFixtureNames,
            StringComparer.OrdinalIgnoreCase));
        Assert.Equal(54, RequiredFixtureNames.Length + UnavailableFixtureNames.Length);
        Assert.Equal(RequiredFixtureNames.Length * sizes.Length,
            UiCapture.FixtureDefinitions.Count * sizes.Length);
        Assert.Equal(UiCapture.FixtureDefinitions.Count,
            UiCapture.FixtureDefinitions.Select(fixture => fixture.Name)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count());
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
    public void ProductionSettingsUseAnExpandedAdvancedControllerExpanderByDefault()
    {
        var view = new SettingsView(new MenuSettings());
        var window = new Window { Width = 940, Height = 560, Content = view };
        try
        {
            window.Show();
            view.ShowSection("Controls");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(true, view.RenderedAdvancedControllerExpanded);
        }
        finally
        {
            window.Close();
            DisposeView(view);
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
            Assert.IsType<SettingsView>(ExtractSettingsView(custom));
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

    private static SettingsView ExtractSettingsView(Control control)
        => control as SettingsView
            ?? (control as ContentControl)?.Content as SettingsView
            ?? throw new Xunit.Sdk.XunitException(
                $"Fixture returned {control.GetType().Name}, not a SettingsView.");

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
        Control[] targets = view.GetVisualDescendants().OfType<Control>()
            .Where(control => control.Focusable && control.IsEffectivelyVisible
                && control.IsEffectivelyEnabled
                && !control.GetVisualAncestors().OfType<ScrollViewer>().Any())
            .ToArray();
        Assert.NotEmpty(targets);

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
        Assert.Contains(pixels.ToArray(), pixel => pixel != 0);
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
