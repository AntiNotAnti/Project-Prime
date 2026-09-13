using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPrime.Server.Shared;
using MphRead.Mods.Accounts;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Theme;
using Xunit;
using AvaloniaButton = Avalonia.Controls.Button;

namespace MphRead.Tests.Client;

/// <summary>
/// Integration contracts for the production shell caller. The pure movement
/// rules live in PrimeInputNavigatorTests; these checks keep the live shell
/// adapter, focus lifecycle, and destructive identity path wired together.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class PrimeShellNavigationIntegrationTests
{
    [AvaloniaFact]
    public void MatchReturnRebuildsAnAlreadySelectedPlayRoot()
    {
        var shell = PrimeShellView.CreateCapture(new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }, PrimeRoute.Play);
        var stale = new Border();
        try
        {
            shell.SetRouteContentForCapture(PrimeRoute.Play, stale);

            shell.Reset();

            Assert.NotSame(stale, shell.CaptureRouteContent);
        }
        finally
        {
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [Fact]
    public void ProductionShellUsesLiveRootNavigationInsteadOfLinearTraversal()
    {
        string source = Read("src/Client.Presentation/Launcher/Shell/PrimeShellView.axaml.cs");
        int moveStart = source.IndexOf("private void MovePadFocus(",
            StringComparison.Ordinal);
        int commandStart = source.IndexOf("private void RunCommand(", moveStart,
            StringComparison.Ordinal);
        Assert.True(moveStart >= 0);
        Assert.True(commandStart > moveStart);

        string movement = source[moveStart..commandStart];
        Assert.Contains("PrimeInputNavigator", source, StringComparison.Ordinal);
        Assert.Contains("CaptureNavigationTargets", movement,
            StringComparison.Ordinal);
        Assert.Contains("_inputNavigator.Move(", movement,
            StringComparison.Ordinal);
        Assert.Contains("PrimeShellNavigationAdapter.Apply", movement,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Array.FindIndex(controls", source,
            StringComparison.Ordinal);
        Assert.Contains("NavigationDirection(direction)", movement,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionShellOwnsLayeredModalRestoreMotionAndSectionInput()
    {
        string source = Read("src/Client.Presentation/Launcher/Shell/PrimeShellView.axaml.cs");
        Assert.Contains("_inputNavigator.OpenModal", source,
            StringComparison.Ordinal);
        Assert.Contains("_inputNavigator.CloseModal", source,
            StringComparison.Ordinal);
        Assert.Contains("_inputNavigator.RestoreScope", source,
            StringComparison.Ordinal);
        Assert.Contains("PrimeMotion.AnimateEntry", source,
            StringComparison.Ordinal);
        Assert.Contains("_routeMotion?.Dispose()", source,
            StringComparison.Ordinal);
        Assert.Contains("_overlayMotion?.Dispose()", source,
            StringComparison.Ordinal);
        Assert.Contains("MoveMajorSection", source, StringComparison.Ordinal);
        Assert.Contains("GamepadButtons.LeftBumper", source,
            StringComparison.Ordinal);
        Assert.Contains("GamepadButtons.Start", source,
            StringComparison.Ordinal);
        Assert.Contains("settingsView.ShowSection", source,
            StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigationMode.Cycle", source,
            StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigation.SetTabNavigation", source,
            StringComparison.Ordinal);
        int start = source.IndexOf("GamepadButtons.Start", StringComparison.Ordinal);
        int startEnd = source.IndexOf("Navigate(PrimeRoute.Settings)", start,
            StringComparison.Ordinal);
        Assert.True(start >= 0 && startEnd > start);
        Assert.Contains("!OverlayRoot.IsVisible", source[start..startEnd],
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionShellOwnsSeatOfferModalAndNetworkSettingsRouting()
    {
        string source = Read("src/Client.Presentation/Launcher/Shell/PrimeShellView.axaml.cs");
        Assert.Contains("SeatOffersHandledExternally: true", source,
            StringComparison.Ordinal);
        Assert.Contains("new SeatOfferCard", source, StringComparison.Ordinal);
        Assert.Contains("new PrimeSeatOfferKey(lobby.LobbyId, offer.OfferId)", source,
            StringComparison.Ordinal);
        Assert.Contains("offer.ExpiresAt <= DateTimeOffset.UtcNow", source,
            StringComparison.Ordinal);
        Assert.Contains("KeyboardNavigationMode.Cycle", source,
            StringComparison.Ordinal);
        Assert.Contains("_playPresentation.TryExitChatEditing()", source,
            StringComparison.Ordinal);
        Assert.Contains("_activeLobbyChatPanel?.TryExitEditing()", source,
            StringComparison.Ordinal);
        Assert.Contains("TrackChatPanel: panel => _activeLobbyChatPanel = panel", source,
            StringComparison.Ordinal);
        Assert.Contains("OpenNetworkSettings: OpenNetworkSettings", source,
            StringComparison.Ordinal);
        Assert.Contains("_settingsFocusCategory = \"Network\"", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void IdentityExitConfirmationCoversRecoverableLobbyAndHandoff()
    {
        Guid owner = Guid.NewGuid();
        LobbySnapshot lobby = new(owner, "Room", LobbyVisibility.Public, owner,
            LobbyPhase.Open, 1, 8, 16, ImmutableArray<LobbyMember>.Empty,
            ImmutableArray<LobbyChatEntry>.Empty);
        NodeMatchHandoff handoff = new(Guid.NewGuid(), 1, "127.0.0.1", 5000,
            "ticket", 1, false, Hunter.Samus);

        Assert.False(PrimeShellNavigationAdapter.RequiresIdentityExitConfirmation(
            null, null));
        Assert.True(PrimeShellNavigationAdapter.RequiresIdentityExitConfirmation(
            lobby, null));
        Assert.True(PrimeShellNavigationAdapter.RequiresIdentityExitConfirmation(
            null, handoff));

        string source = Read("src/Client.Presentation/Launcher/Shell/PrimeShellView.axaml.cs");
        Assert.Contains("ShowIdentityExitConfirmation", source,
            StringComparison.Ordinal);
        Assert.Contains("RequiresIdentityExitConfirmation", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ControllerPromptUsesFamilyGlyphsAndProductionState()
    {
        Assert.Equal("Select (A)", PrimeControllerGlyphs.Prompt("Select",
            GamepadButtons.A, ControllerFamily.Xbox));
        Assert.Equal("Select (Cross)", PrimeControllerGlyphs.Prompt("Select",
            GamepadButtons.A, ControllerFamily.PlayStation));
        Assert.Equal("Select (B)", PrimeControllerGlyphs.Prompt("Select",
            GamepadButtons.A, ControllerFamily.Nintendo));

        string source = Read("src/Client.Presentation/Launcher/Shell/PrimeShellView.axaml.cs");
        Assert.Contains("GamepadInput.State.Family", source,
            StringComparison.Ordinal);
        Assert.Contains("PrimeControllerGlyphs.Prompt", source,
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ControllerAUsesToggleStateTransitionForPlayFiltersWithoutDoubleFiringButtons()
    {
        var filters = new[]
        {
            new CheckBox { Content = "Open player seats", IsChecked = false },
            new CheckBox { Content = "Spectatable", IsChecked = false },
            new CheckBox { Content = "Hide full matches", IsChecked = false }
        };

        foreach (CheckBox filter in filters)
        {
            int changes = 0;
            int filterClicks = 0;
            filter.IsCheckedChanged += (_, _) => changes++;
            filter.AddHandler(AvaloniaButton.ClickEvent, (_, _) => filterClicks++);

            Assert.True(PrimeShellView.TryActivateControllerTarget(filter));
            Assert.True(filter.IsChecked);
            Assert.Equal(1, changes);
            Assert.Equal(1, filterClicks);
        }

        var button = new AvaloniaButton { Content = "Normal action" };
        int clicks = 0;
        button.Click += (_, _) => clicks++;

        Assert.True(PrimeShellView.TryActivateControllerTarget(button));
        Assert.Equal(1, clicks);

        string source = Read("src/Client.Presentation/Launcher/Shell/PrimeShellView.axaml.cs");
        int toggle = source.IndexOf("case ToggleButton", StringComparison.Ordinal);
        int normal = source.IndexOf("case AvaloniaButton", toggle,
            StringComparison.Ordinal);
        Assert.True(toggle >= 0 && normal > toggle);
        Assert.Contains("toggle.IsChecked = toggle.IsChecked != true", source[toggle..normal],
            StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ProductionShellTreeProducesStableRootCandidates()
    {
        var shell = PrimeShellView.CreateCapture(new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }, PrimeRoute.Hunter);
        var window = new Window
        {
            Width = 1024,
            Height = 720,
            Content = shell
        };
        try
        {
            window.Show();
            Control page = Find(shell, "PageHost");
            Control header = Find(shell, "HeaderBorder");
            Control footer = Find(shell, "FooterBorder");
            Control mobileFooter = Find(shell, "MobileNavigationBorder");
            Control overlay = Find(shell, "OverlayRoot");
            var adapter = new PrimeShellNavigationAdapter(shell);
            PrimeShellNavigationTarget[] targets = adapter.Capture(page,
                new[] { header, footer, mobileFooter }, overlay, false,
                PrimeRoute.Hunter).ToArray();

            Assert.NotEmpty(targets);
            Assert.All(targets, target =>
                Assert.Equal(PrimeShellNavigationAdapter.CoordinateRoot,
                    target.Candidate.Bounds.CoordinateRoot));
            Assert.DoesNotContain(targets, target =>
                target.Candidate.Layer == PrimeNavigationLayer.Modal);

            var navigator = new PrimeInputNavigator();
            PrimeNavigationResult entered = navigator.EnterScope(
                new PrimeFocusScope(PrimeRoute.Hunter, "Overview"),
                targets.Select(target => target.Candidate));
            Assert.True(entered.Moved);
            Assert.True(PrimeShellNavigationAdapter.Apply(targets, entered));

            PrimeNavigationCandidate[] pageCandidates = targets
                .Where(target => target.Candidate.Layer == PrimeNavigationLayer.ActivePage)
                .OrderBy(target => target.Candidate.RootBounds.Y)
                .ThenBy(target => target.Candidate.RootBounds.X)
                .Select(target => target.Candidate)
                .ToArray();
            PrimeNavigationCandidate? pageTarget = null;
            PrimeNavigationCandidate? headerTarget = null;
            foreach (PrimeNavigationCandidate pageCandidate in pageCandidates)
            {
                PrimeNavigationCandidate? candidate = PrimeSpatialNavigation.Select(
                    pageCandidate.FocusId, targets.Select(target => target.Candidate),
                    PrimeNavigationDirection.Up);
                if (candidate?.Layer != PrimeNavigationLayer.ShellChrome)
                    continue;
                pageTarget = pageCandidate;
                headerTarget = candidate;
                break;
            }
            Assert.NotNull(pageTarget);
            Assert.NotNull(headerTarget);
            Assert.Equal(PrimeNavigationLayer.ShellChrome, headerTarget!.Layer);
            PrimeNavigationCandidate? pageReturn = PrimeSpatialNavigation.Select(
                headerTarget.FocusId, targets.Select(target => target.Candidate),
                PrimeNavigationDirection.Down);
            Assert.NotNull(pageReturn);
            Assert.Equal(PrimeNavigationLayer.ActivePage, pageReturn!.Layer);
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void RealConfirmationOverlayCyclesTabWithoutLeakingToCoveredShell()
    {
        var shell = PrimeShellView.CreateCapture(new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }, PrimeRoute.Hunter);
        var window = new Window
        {
            Width = 1024,
            Height = 720,
            Content = shell
        };
        try
        {
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Control overlay = Find(shell, "OverlayRoot");
            KeyboardNavigationMode before = KeyboardNavigation.GetTabNavigation(
                (InputElement)overlay);
            MethodInfo confirmation = typeof(PrimeShellView).GetMethod(
                "ShowIdentityExitConfirmation",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "The production identity confirmation was not found.");
            confirmation.Invoke(shell, new object[] { "Sign Out" });
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            for (int i = 0; i < 4; i++)
                Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(1024, 720));
            window.Arrange(new Rect(0, 0, 1024, 720));
            Dispatcher.UIThread.RunJobs();

            KeyboardNavigationMode trapped = KeyboardNavigation.GetTabNavigation(
                (InputElement)overlay);
            Assert.Equal(KeyboardNavigationMode.Cycle, trapped);
            Control[] modalStops = overlay.GetVisualDescendants().OfType<Control>()
                .Where(control => control.Focusable && control.IsEffectivelyVisible
                    && control.IsEffectivelyEnabled)
                .ToArray();
            Assert.True(modalStops.Length >= 2);
            Assert.True(modalStops[0].Focus());

            IInputRoot inputRoot = (IInputRoot)window;
            IKeyboardNavigationHandler keyboardNavigation =
                inputRoot.KeyboardNavigationHandler
                ?? throw new InvalidOperationException(
                    "The headless window has no keyboard navigation handler.");
            IInputElement current = modalStops[0];
            for (int i = 0; i < modalStops.Length * 2; i++)
            {
                keyboardNavigation.Move(current,
                    NavigationDirection.Next, KeyModifiers.None);
                IInputElement? next = inputRoot.FocusManager?.GetFocusedElement();
                Assert.NotNull(next);
                Assert.True(IsWithin(overlay, next!),
                    "Tab focus escaped the visible confirmation overlay.");
                current = next!;
            }

            Assert.True(shell.GoBack());
            Assert.Equal(before, KeyboardNavigation.GetTabNavigation(
                (InputElement)overlay));
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void AuthoritativeSeatOfferRendersOnceInTheFocusTrappedShellOverlay()
    {
        Guid nodeId = Guid.NewGuid();
        Guid lobbyId = Guid.NewGuid();
        Guid offerId = Guid.NewGuid();
        var session = new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(),
            "Queue Pilot", nodeId, new string('a', 43));
        var offer = new LobbyQueueOffer(offerId, DateTimeOffset.UtcNow.AddMinutes(1),
            LobbySeatPolicy.ImmediateSeat);
        var waitlist = new LobbyWaitlistSnapshot(1,
            [new LobbyQueueEntrySummary(1, "Queue Pilot",
                LobbyQueueEntryState.SeatOffered, 10)],
            IsSelfQueued: true, SelfState: LobbyQueueEntryState.SeatOffered,
            SelfQueueSequence: 10, SelfOffer: offer);
        var lobby = new LobbySnapshot(lobbyId, "Offer Room",
            LobbyVisibility.Public, Guid.NewGuid(), LobbyPhase.Open, 7, 1, 4,
            ImmutableArray<LobbyMember>.Empty,
            ImmutableArray<LobbyChatEntry>.Empty, Waitlist: waitlist);
        var play = new PlayState(PlayPhase.Lobby, Array.Empty<NodeListing>(),
            new MphRead.Mods.Network.NodeControlClient.ViewState(
                Session: session, Lobby: lobby),
            Hunter.Samus, "", Loading: false, Revision: lobby.Revision);
        var capture = new PrimeShellCaptureState(Play: play,
            Identity: PrimeShellCaptureIdentity.SignedIn);
        var shell = PrimeShellView.CreateLobbyCapture(new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }, capture);
        var window = new Window
        {
            Width = 1024,
            Height = 720,
            Content = shell
        };

        try
        {
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            for (int i = 0; i < 4; i++)
                Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(1024, 720));
            window.Arrange(new Rect(0, 0, 1024, 720));

            Control overlay = Find(shell, "OverlayRoot");
            Control page = Find(shell, "PageHost");
            Assert.True(overlay.IsVisible);
            Assert.Equal(KeyboardNavigationMode.Cycle,
                KeyboardNavigation.GetTabNavigation((InputElement)overlay));
            Assert.Single(overlay.GetVisualDescendants().OfType<SeatOfferCard>());
            Assert.Empty(page.GetVisualDescendants().OfType<SeatOfferCard>());
            Assert.Single(shell.GetVisualDescendants().OfType<SeatOfferCard>());
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void BackExitsTheLiveChatEditorWithoutDiscardingItsDraftOrNavigating()
    {
        Guid nodeId = Guid.NewGuid();
        var session = new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(),
            "Chat Pilot", nodeId, new string('a', 43));
        var lobby = new LobbySnapshot(Guid.NewGuid(), "Chat Room",
            LobbyVisibility.Public, session.SessionId, LobbyPhase.Open, 3, 4, 4,
            ImmutableArray<LobbyMember>.Empty,
            [new LobbyChatEntry(1, Guid.NewGuid(), "Other Pilot", "Ready?")]);
        var play = new PlayState(PlayPhase.Lobby, Array.Empty<NodeListing>(),
            new MphRead.Mods.Network.NodeControlClient.ViewState(
                Session: session, Lobby: lobby),
            Hunter.Samus, "", Loading: false, Revision: lobby.Revision);
        var capture = new PrimeShellCaptureState(Play: play,
            Identity: PrimeShellCaptureIdentity.SignedIn);
        var shell = PrimeShellView.CreateLobbyCapture(new MenuSettings(),
            new[] { "MP3 PROVING GROUND" }, capture);
        var window = new Window
        {
            Width = 1024,
            Height = 720,
            Content = shell
        };

        try
        {
            window.Show();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            for (int i = 0; i < 4; i++)
                Dispatcher.UIThread.RunJobs();
            window.Measure(new Size(1024, 720));
            window.Arrange(new Rect(0, 0, 1024, 720));

            LobbyChatPanel panel = shell.GetVisualDescendants()
                .OfType<LobbyChatPanel>().Single();
            TextBox editor = panel.DraftEditor;
            Assert.True(editor.Focus());
            editor.Text = "Keep this unsent draft";
            Dispatcher.UIThread.RunJobs();
            Assert.True(editor.IsFocused);

            Assert.True(shell.GoBack());
            Dispatcher.UIThread.RunJobs();

            Assert.False(editor.IsFocused);
            Assert.Equal("Keep this unsent draft", editor.Text);
            Assert.IsNotType<TextBox>(window.FocusManager?.GetFocusedElement());
            Assert.Single(shell.GetVisualDescendants().OfType<LobbyChatPanel>());
            FieldInfo stateField = typeof(PrimeShellView).GetField(
                "_playPresentation", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("Play presentation state was not found.");
            var state = Assert.IsType<PlayPresentationState>(stateField.GetValue(shell));
            Assert.Equal("Keep this unsent draft", state.ChatDraft);
            Assert.False(state.ChatEditing);
        }
        finally
        {
            window.Close();
            shell.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private static Control Find(Control root, string name)
        => root.GetVisualDescendants().OfType<Control>()
            .Single(control => String.Equals(control.Name, name,
                StringComparison.Ordinal));

    private static bool IsWithin(Control root, IInputElement element)
        => element is Control control
            && (ReferenceEquals(control, root)
                || root.GetVisualDescendants().Contains(control));

    private static string Read(string path)
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "Game.sln")))
            root = Directory.GetParent(root)?.FullName;
        return File.ReadAllText(Path.Combine(root ?? throw new InvalidOperationException(
            "Repository root was not found."), path));
    }
}
