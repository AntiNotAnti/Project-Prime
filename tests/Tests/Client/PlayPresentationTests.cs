using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Accounts;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.Presentation;
using MphRead.Mods.Launcher.Theme;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PlayPresentationTests
{
    [Theory]
    [InlineData(MatchMode.Battle)]
    [InlineData(MatchMode.Survival)]
    [InlineData(MatchMode.PrimeHunter)]
    public void FreeForAllRosterDetailsNeverExposeTeam(MatchMode mode)
    {
        var member = new LobbyMember(Guid.NewGuid(), null, "Pilot", Hunter.Samus,
            Team: 1, Ready: false, Observer: false, GuestSessionId: Guid.NewGuid());

        string detail = PlayPresentation.LobbyMemberDetail(member,
            showTeam: mode.IsTeamMode(), isCurrent: true);

        Assert.Equal("Samus · YOU", detail);
        Assert.DoesNotContain("Team", detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MatchMode.TeamBattle)]
    [InlineData(MatchMode.TeamSurvival)]
    public void TeamRosterDetailsUseAuthoritativeTeamExceptForObservers(MatchMode mode)
    {
        var player = new LobbyMember(Guid.NewGuid(), null, "Pilot", Hunter.Kanden,
            Team: 1, Ready: true, Observer: false, GuestSessionId: Guid.NewGuid());
        var observer = player with { Observer = true };

        Assert.Equal("Kanden · Team 2 · YOU",
            PlayPresentation.LobbyMemberDetail(player,
                showTeam: mode.IsTeamMode(), isCurrent: true));
        Assert.Equal("Kanden · YOU",
            PlayPresentation.LobbyMemberDetail(observer,
                showTeam: mode.IsTeamMode(), isCurrent: true));
        Assert.Same(GuiTheme.SuccessBrush,
            PlayPresentation.LobbyMemberStatusBrush(player));
        Assert.Same(GuiTheme.TechBrush,
            PlayPresentation.LobbyMemberStatusBrush(observer));
        Assert.Same(GuiTheme.WarningBrush,
            PlayPresentation.LobbyMemberStatusBrush(player with { Ready = false }));
    }

    [AvaloniaFact]
    public async Task TeamSelectorAndLobbyCopyRemainAuthoritativeAndPlayerFacing()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            Guid sessionId = Guid.NewGuid();
            LobbySnapshot lobby = Lobby(sessionId, MatchMode.TeamBattle,
                team: 0, ready: false, owner: true);
            string? operation = null;
            Control view = PlayPresentation.Build(Context(shell, controller, lobby,
                run: (label, _) => operation = label));
            var window = new Window { Width = 940, Height = 900, Content = view };
            try
            {
                window.Show();
                PrimeButton team1 = Assert.Single(view.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Team 1"));
                PrimeButton team2 = Assert.Single(view.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Team 2"));

                Assert.Contains("prime-primary", team1.Classes);
                Assert.DoesNotContain("prime-primary", team2.Classes);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text?.Contains("Request Team", StringComparison.Ordinal) == true);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Not ready.");
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Choose your Hunter, then Ready.");
                PrimeSelectedRow localRow = Assert.Single(view.GetVisualDescendants()
                    .OfType<PrimeSelectedRow>());
                Assert.Same(GuiTheme.BrandBrush, localRow.BorderBrush);

                team2.Invoke();

                Assert.Equal("Select Team 2", operation);
                Assert.Contains("prime-primary", team1.Classes);
                Assert.DoesNotContain("prime-primary", team2.Classes);
                Assert.Equal((byte)0, lobby.Members[0].Team);
                LobbyStartEligibility eligibility = LobbyStartEligibility.Evaluate(lobby);
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == PlayPresentation.PlayerFacingEligibilityMessage(
                        eligibility));
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task PlayHomeAndMatchCardsUseTheRequestedVisualHierarchy()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            Control home = PlayPresentation.Build(Context(shell, controller, lobby: null));
            var entry = new LobbyListEntry(Guid.NewGuid(), "Ranked Room",
                LobbyPhase.Open, Players: 3, PlayerLimit: 4, Observers: 1,
                Revision: 7, WaitlistCount: 2, ObserverLimit: 4, BotCount: 0,
                MapKey: "MP1 SANCTORUS", Mode: MatchMode.TeamBattle,
                TimeLimitSeconds: 600, PointGoal: 7,
                SeatPolicy: LobbySeatPolicy.NextMatchSeat);
            var card = new PrimeMatchCard(entry, () => { }, () => { }, () => { });
            var host = new StackPanel { Children = { home, card } };
            var window = new Window { Width = 940, Height = 900, Content = host };
            try
            {
                window.Show();
                PrimeCard hero = Assert.Single(home.GetVisualDescendants()
                    .OfType<PrimeCard>(), candidate => candidate.BorderBrush == GuiTheme.BrandBrush);
                Assert.Contains(hero.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "QUICK PLAY");
                Assert.Contains(hero.GetVisualDescendants().OfType<PrimeButton>(),
                    button => Equals(button.Content, "Quick Play")
                        && button.Classes.Contains("prime-primary"));
                Assert.All(home.GetVisualDescendants().OfType<PrimeButton>()
                    .Where(button => Equals(button.Content, "Browse Matches")
                        || Equals(button.Content, "Host Match")),
                    button => Assert.DoesNotContain("prime-primary", button.Classes));
                Expander advanced = Assert.Single(home.GetVisualDescendants()
                    .OfType<Expander>());
                Assert.False(advanced.IsExpanded);
                Assert.Contains("prime-tech", Assert.IsType<TextBlock>(advanced.Header).Classes);
                Assert.Contains("prime-tech",
                    Assert.IsType<PrimeSectionPanel>(advanced.Content).Classes);

                string cardText = String.Join('\n', card.GetVisualDescendants()
                    .OfType<TextBlock>().Select(text => text.Text));
                Assert.Contains("Ranked Room", cardText, StringComparison.Ordinal);
                Assert.Contains(PrimeGameText.MapName(entry.MapKey), cardText,
                    StringComparison.Ordinal);
                Assert.Contains(PrimeGameText.ModeLabel(entry.Mode), cardText,
                    StringComparison.Ordinal);
                Assert.Contains("3/4 players · 1 open", cardText,
                    StringComparison.Ordinal);
                Assert.Contains("Seat policy · Next match seat", cardText,
                    StringComparison.Ordinal);
                PrimeButton join = Assert.Single(card.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Join"));
                Assert.Contains("prime-primary", join.Classes);
                Assert.DoesNotContain("prime-primary", Assert.Single(card.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Waitlist")).Classes);
                Assert.DoesNotContain("prime-primary", Assert.Single(card.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Spectate")).Classes);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public void LobbyChatUsesLocalPeerAndSystemNameSemantics()
    {
        Guid local = Guid.NewGuid();
        var panel = new LobbyChatPanel(
        [
            new LobbyChatEntry(1, local, "Local", "Ready."),
            new LobbyChatEntry(2, Guid.NewGuid(), "Rival", "Almost."),
            new LobbyChatEntry(3, Guid.Empty, "System", "Match updated.")
        ], "", _ => { }, _ => { }, localSessionId: local);
        var window = new Window { Width = 560, Height = 800, Content = panel };
        try
        {
            window.Show();
            TextBlock localName = Assert.Single(panel.GetVisualDescendants()
                .OfType<TextBlock>(), text => text.Classes.Contains("prime-chat-local-name"));
            TextBlock peerName = Assert.Single(panel.GetVisualDescendants()
                .OfType<TextBlock>(), text => text.Classes.Contains("prime-chat-peer-name"));
            Assert.Same(GuiTheme.BrandBrush, localName.Foreground);
            Assert.Same(GuiTheme.TechBrush, peerName.Foreground);
            Assert.Equal(2, panel.GetVisualDescendants().OfType<TextBlock>()
                .Count(text => text.Classes.Contains("prime-chat-system")));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExternalSeatOfferHandlingLeavesQueueContextWithoutInlineCard()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("QueueTester");
        var controller = new PlayController(shell);

        try
        {
            Guid sessionId = Guid.NewGuid();
            Guid lobbyId = Guid.NewGuid();
            var offer = new LobbyQueueOffer(Guid.NewGuid(),
                DateTimeOffset.UtcNow.AddMinutes(2), LobbySeatPolicy.ImmediateSeat);
            var waitlist = new LobbyWaitlistSnapshot(
                1,
                ImmutableArray.Create(new LobbyQueueEntrySummary(
                    1, "QueueTester", LobbyQueueEntryState.SeatOffered, 101)),
                IsSelfQueued: true,
                SelfState: LobbyQueueEntryState.SeatOffered,
                SelfQueueSequence: 101,
                SelfOffer: offer);
            var lobby = new LobbySnapshot(
                lobbyId, "Offer Room", LobbyVisibility.Public, Guid.NewGuid(),
                LobbyPhase.Open, 7, 1, 4, ImmutableArray<LobbyMember>.Empty,
                ImmutableArray<LobbyChatEntry>.Empty, Waitlist: waitlist);
            var session = new NodeSessionSnapshot(sessionId, Guid.NewGuid(),
                "QueueTester", Guid.NewGuid(), new string('a', 43));
            var state = new PlayState(
                PlayPhase.Lobby,
                Array.Empty<NodeListing>(),
                new NodeControlClient.ViewState(Session: session, Lobby: lobby),
                Hunter.Samus, "", Loading: false, Revision: lobby.Revision);
            var ui = new PlayPresentationState();
            LobbyChatPanel? trackedChatPanel = null;
            var context = new PlayPresentationContext(
                shell,
                controller,
                state,
                Array.Empty<string>(),
                ui,
                CancellationToken.None,
                static (_, _) => { },
                static _ => { },
                static () => { },
                static () => { },
                static (_, height, _) => new Border { Height = height },
                static _ => Task.CompletedTask,
                static _ => Task.CompletedTask,
                static (_, _) => { },
                static () => { },
                static (_, _) => { },
                ExpandAdvancedNetwork: false,
                OpenNetworkSettings: null,
                SeatOffersHandledExternally: true,
                TrackChatPanel: panel => trackedChatPanel = panel);

            Control view = PlayPresentation.Build(context);
            Assert.NotNull(trackedChatPanel);
            var window = new Window { Width = 940, Height = 560, Content = view };
            try
            {
                window.Show();
                Assert.Empty(view.GetVisualDescendants().OfType<SeatOfferCard>());
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "PLAYER SEAT QUEUE");
                Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "A player seat is ready for you. Respond in the seat offer prompt.");

                // Suppressing the inline card does not consume or bypass the
                // one-shot guard used by whichever surface owns the prompt.
                Assert.True(ui.TryBeginOfferAction(offer.OfferId));
                Assert.False(ui.TryBeginOfferAction(offer.OfferId));
            }
            finally
            {
                window.Close();
            }

            Control defaultView = PlayPresentation.Build(context with
            {
                SeatOffersHandledExternally = false
            });
            var defaultWindow = new Window
            {
                Width = 940,
                Height = 560,
                Content = defaultView
            };
            try
            {
                defaultWindow.Show();
                SeatOfferCard offerCard = Assert.Single(
                    defaultView.GetVisualDescendants().OfType<SeatOfferCard>());
                PrimeButton accept = Assert.Single(offerCard.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Accept"));
                PrimeButton decline = Assert.Single(offerCard.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Decline"));
                Assert.Contains("prime-primary", accept.Classes);
                Assert.DoesNotContain("prime-primary", decline.Classes);
            }
            finally
            {
                defaultWindow.Close();
            }

            // Every rebuild first clears the shell's reference, including a
            // transition away from a lobby where no chat panel is rendered.
            PlayPresentation.Build(context with { State = PlayState.Initial });
            Assert.Null(trackedChatPanel);
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public void ChatPanelExitClearsFocusWithoutDroppingDraft()
    {
        string? observedDraft = null;
        var panel = new LobbyChatPanel(Array.Empty<LobbyChatEntry>(), "",
            draft => observedDraft = draft,
            _ => { });
        var window = new Window
        {
            Width = 940,
            Height = 560,
            Content = panel
        };

        try
        {
            window.Show();
            Assert.True(panel.DraftEditor.Focus());
            panel.DraftEditor.Text = "Keep this unsent draft";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(panel.DraftEditor.IsFocused);

            Assert.True(panel.TryExitEditing());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.False(panel.DraftEditor.IsFocused);
            Assert.Equal("Keep this unsent draft", panel.DraftEditor.Text);
            Assert.Equal("Keep this unsent draft", observedDraft);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task HostResponsiveTreeRetainsEditorsDraftAndActionsAcrossLayouts()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            controller.SetCaptureMapCatalog(new[] { "MP1 SANCTORUS", "MP3 PROVING GROUND" });
            var ui = new PlayPresentationState { Subsection = PlaySubsection.HostMatch };
            ui.HostDraft.MapKey = "MP1 SANCTORUS";
            ui.HostDraft.BotCount = 7;
            ui.HostDraft.TimeLimitText = "10:00";
            ui.SetChatDraft("Unsent lobby note");
            int actions = 0;
            int previewBuilds = 0;
            Control view = PlayPresentation.Build(Context(shell, controller, lobby: null,
                run: (_, _) => actions++, ui: ui,
                buildPreview: (_, height, _) =>
                {
                    previewBuilds++;
                    return new Border { Height = height };
                }));
            PrimePlayResponsivePanel layout = Assert.IsType<PrimePlayResponsivePanel>(view);
            var window = new Window { Width = 1280, Height = 720, Content = view };
            try
            {
                window.Show();
                layout.ApplyLayout(1280);
                TextBox name = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(),
                    editor => Equals(editor.Watermark, "Lobby name"));
                ComboBox players = FieldEditor<ComboBox>(view, "Player seats");
                ComboBox bots = FieldEditor<ComboBox>(view, "Bots");
                name.Text = "Competitive Test";
                players.SelectedItem = 2;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                Assert.Equal(1, ui.HostDraft.BotCount);
                Assert.Equal(2, bots.ItemsSource!.Cast<object>().Count());
                Assert.Equal(1, bots.SelectedItem);
                int initialTransitions = layout.LayoutTransitionCount;

                layout.ApplyLayout(1300);
                Assert.Equal(initialTransitions, layout.LayoutTransitionCount);
                layout.ApplyLayout(1000);
                Assert.Equal(PrimeContentLayout.Compact, layout.Layout);
                layout.ApplyLayout(560);
                Assert.Equal(PrimeContentLayout.Mobile, layout.Layout);
                layout.ApplyLayout(1280);
                Assert.Equal(PrimeContentLayout.Wide, layout.Layout);

                Assert.Equal("Competitive Test", ui.HostDraft.Name);
                Assert.Equal("MP1 SANCTORUS", ui.HostDraft.MapKey);
                Assert.Equal("10:00", ui.HostDraft.TimeLimitText);
                Assert.Equal("Unsent lobby note", ui.ChatDraft);
                Assert.Same(name, Assert.Single(view.GetVisualDescendants().OfType<TextBox>(),
                    editor => Equals(editor.Watermark, "Lobby name")));
                Assert.Equal(0, actions);
                Assert.Equal(1, previewBuilds);
                Assert.Contains("prime-layout-wide", layout.Classes);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task HostResponsiveTransitionKeepsFocusedStageTwoEditorAndSelection()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            controller.SetCaptureMapCatalog(new[] { "MP1 SANCTORUS" });
            var ui = new PlayPresentationState { Subsection = PlaySubsection.HostMatch };
            ui.HostDraft.TimeLimitText = "10:00";
            Control view = PlayPresentation.Build(Context(shell, controller, lobby: null,
                ui: ui));
            PrimePlayResponsivePanel layout = Assert.IsType<PrimePlayResponsivePanel>(view);
            var window = new Window { Width = 1280, Height = 720, Content = view };
            try
            {
                window.Show();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                layout.ApplyLayout(1280);
                TextBox time = FieldEditor<TextBox>(view, "Time limit");
                Assert.True(time.Focus());
                time.SelectionStart = 1;
                time.SelectionEnd = 4;

                layout.ApplyLayout(1000);

                Assert.Equal(PrimeContentLayout.Compact, layout.Layout);
                Assert.Equal(2, layout.CurrentStep);
                Assert.Equal(2, ui.HostStep);
                Assert.Same(time, FieldEditor<TextBox>(view, "Time limit"));
                Assert.True(time.IsFocused);
                Assert.True(time.IsEffectivelyVisible);
                Assert.Equal(1, time.SelectionStart);
                Assert.Equal(4, time.SelectionEnd);
                WrapPanel rail = Assert.Single(layout.Children.OfType<WrapPanel>());
                PrimeStatusChip[] steps = rail.Children.OfType<PrimeStatusChip>().ToArray();
                Assert.Equal("✓ · Lobby seats", steps[0].Text);
                Assert.Equal("2 · Mission and rules", steps[1].Text);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task HostResponsiveTransitionKeepsOpenStageTwoComboBox()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            controller.SetCaptureMapCatalog(new[] { "MP1 SANCTORUS", "MP3 PROVING GROUND" });
            var ui = new PlayPresentationState { Subsection = PlaySubsection.HostMatch };
            Control view = PlayPresentation.Build(Context(shell, controller, lobby: null,
                ui: ui));
            PrimePlayResponsivePanel layout = Assert.IsType<PrimePlayResponsivePanel>(view);
            var window = new Window { Width = 1280, Height = 720, Content = view };
            try
            {
                window.Show();
                Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                layout.ApplyLayout(1280);
                ComboBox map = FieldEditor<ComboBox>(view, "Map");
                map.IsDropDownOpen = true;
                Assert.True(map.IsDropDownOpen);

                layout.ApplyLayout(560);

                Assert.Equal(PrimeContentLayout.Mobile, layout.Layout);
                Assert.Equal(2, ui.HostStep);
                Assert.Same(map, FieldEditor<ComboBox>(view, "Map"));
                Assert.True(map.IsEffectivelyVisible);
                Assert.True(map.IsDropDownOpen);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task HeightOnlyViewportChangesResizeStableHostAndLobbyControls()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            controller.SetCaptureMapCatalog(new[] { "MP1 SANCTORUS" });
            int actions = 0;
            int hostPreviewBuilds = 0;
            Control? hostPreview = null;
            Control hostView = PlayPresentation.Build(Context(shell, controller, lobby: null,
                run: (_, _) => actions++,
                ui: new PlayPresentationState { Subsection = PlaySubsection.HostMatch },
                buildPreview: (_, height, _) =>
                {
                    hostPreviewBuilds++;
                    return hostPreview = new Border { Height = height };
                }));
            PrimePlayResponsivePanel hostLayout = Assert.IsType<PrimePlayResponsivePanel>(hostView);
            var hostWindow = new Window { Width = 1280, Height = 720, Content = hostView };
            try
            {
                hostWindow.Show();
                hostLayout.ApplyLayout(1280);
                int transitions = hostLayout.LayoutTransitionCount;
                int viewportChanges = 0;
                hostLayout.ViewportChanged += _ => viewportChanges++;
                hostLayout.ApplyViewport(new Size(1280, 500));
                double shortHeight = Assert.IsType<Border>(hostPreview).Height;
                hostLayout.ApplyViewport(new Size(1280, 900));
                double tallHeight = Assert.IsType<Border>(hostPreview).Height;
                hostLayout.ApplyViewport(new Size(1280, 900));

                Assert.True(tallHeight > shortHeight);
                Assert.Equal(2, viewportChanges);
                Assert.Equal(transitions, hostLayout.LayoutTransitionCount);
                Assert.Equal(1, hostPreviewBuilds);
                Assert.Equal(0, actions);
            }
            finally
            {
                hostWindow.Close();
            }

            int lobbyPreviewBuilds = 0;
            Control? lobbyPreview = null;
            LobbySnapshot lobby = Lobby(Guid.NewGuid(), MatchMode.TeamBattle,
                team: 0, ready: false, owner: true);
            Control lobbyView = PlayPresentation.Build(Context(shell, controller, lobby,
                run: (_, _) => actions++, buildPreview: (_, height, _) =>
                {
                    lobbyPreviewBuilds++;
                    return lobbyPreview = new Border { Height = height };
                }));
            PrimePlayResponsivePanel lobbyLayout = Assert.IsType<PrimePlayResponsivePanel>(lobbyView);
            var lobbyWindow = new Window { Width = 1280, Height = 720, Content = lobbyView };
            try
            {
                lobbyWindow.Show();
                lobbyLayout.ApplyLayout(1280);
                LobbyChatPanel chat = Assert.Single(lobbyView.GetVisualDescendants()
                    .OfType<LobbyChatPanel>());
                ScrollViewer roster = Assert.Single(lobbyView.GetVisualDescendants()
                    .OfType<ScrollViewer>(), scroll =>
                        scroll.Classes.Contains("prime-roster-scroll"));
                int transitions = lobbyLayout.LayoutTransitionCount;
                lobbyLayout.ApplyViewport(new Size(1280, 500));
                double shortPreview = Assert.IsType<Border>(lobbyPreview).Height;
                double shortRoster = roster.MaxHeight;
                double shortChat = chat.HistoryMaxHeight;
                lobbyLayout.ApplyViewport(new Size(1280, 900));

                Assert.True(Assert.IsType<Border>(lobbyPreview).Height > shortPreview);
                Assert.True(roster.MaxHeight > shortRoster);
                Assert.True(chat.HistoryMaxHeight > shortChat);
                Assert.Same(chat, Assert.Single(lobbyView.GetVisualDescendants()
                    .OfType<LobbyChatPanel>()));
                Assert.Same(roster, Assert.Single(lobbyView.GetVisualDescendants()
                    .OfType<ScrollViewer>(), scroll =>
                        scroll.Classes.Contains("prime-roster-scroll")));
                Assert.Equal(transitions, lobbyLayout.LayoutTransitionCount);
                Assert.Equal(1, lobbyPreviewBuilds);
                Assert.Equal(0, actions);
            }
            finally
            {
                lobbyWindow.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task WideHostExposesTheCompleteBattleConfiguration()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            controller.SetCaptureMapCatalog(new[] { "MP1 SANCTORUS" });
            var ui = new PlayPresentationState { Subsection = PlaySubsection.HostMatch };
            Control view = PlayPresentation.Build(Context(shell, controller, lobby: null,
                ui: ui));
            PrimePlayResponsivePanel layout = Assert.IsType<PrimePlayResponsivePanel>(view);
            var window = new Window { Width = 1280, Height = 720, Content = view };
            try
            {
                window.Show();
                layout.ApplyLayout(1280);
                string text = VisibleText(view);
                foreach (string required in new[]
                {
                    "Match name", "Map", "Mode", "Player seats", "Observer seats",
                    "Bots", "Seat policy", "Time limit", "Score limit", "Damage level",
                    "Friendly fire", "Affinity weapons", "Player radar"
                })
                    Assert.Contains(required, text, StringComparison.Ordinal);
                PrimeButton create = Assert.Single(view.GetVisualDescendants()
                    .OfType<PrimeButton>(), button => Equals(button.Content, "Create Match"));
                Assert.True(create.IsVisible);
                Assert.DoesNotContain(VisibleTextBlocks(view),
                    item => item.Text?.Contains("Lobby seats", StringComparison.Ordinal) == true);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    [AvaloniaFact]
    public async Task LobbyResponsiveDashboardHidesIrrelevantQueueAndFfaTeamControls()
    {
        var shell = new PrimeShellState();
        shell.SelectGuest("Local Pilot");
        var controller = new PlayController(shell);
        try
        {
            Guid sessionId = Guid.NewGuid();
            LobbySnapshot lobby = Lobby(sessionId, MatchMode.Battle,
                team: 1, ready: false, owner: true);
            var ui = new PlayPresentationState();
            ui.SetChatDraft("Keep this draft through resize");
            int actions = 0;
            Control view = PlayPresentation.Build(Context(shell, controller, lobby,
                run: (_, _) => actions++, ui: ui));
            PrimePlayResponsivePanel layout = Assert.IsType<PrimePlayResponsivePanel>(view);
            var window = new Window { Width = 1280, Height = 720, Content = view };
            try
            {
                window.Show();
                layout.ApplyLayout(1280);
                string text = VisibleText(view);
                Assert.DoesNotContain("PLAYER SEAT QUEUE", text, StringComparison.Ordinal);
                Assert.DoesNotContain("TEAM 1", text, StringComparison.Ordinal);
                Assert.DoesNotContain("TEAM 2", text, StringComparison.Ordinal);
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<PrimeButton>(),
                    button => Equals(button.Content, "Team 1")
                        || Equals(button.Content, "Team 2"));
                Assert.Contains(view.GetVisualDescendants().OfType<PrimeButton>(),
                    button => Equals(button.Content, "Ready"));
                Assert.Contains(view.GetVisualDescendants().OfType<PrimeButton>(),
                    button => Equals(button.Content, "Start Match"));
                LobbyChatPanel chat = Assert.Single(view.GetVisualDescendants()
                    .OfType<LobbyChatPanel>());
                Assert.True(chat.DraftEditor.IsVisible);
                layout.ApplyLayout(1000);
                layout.ApplyLayout(560);
                layout.ApplyLayout(1280);
                Assert.Equal(PrimeContentLayout.Wide, layout.Layout);
                Assert.Same(chat, Assert.Single(view.GetVisualDescendants()
                    .OfType<LobbyChatPanel>()));
                Assert.Equal("Keep this draft through resize", chat.DraftEditor.Text);
                Assert.Equal("Keep this draft through resize", ui.ChatDraft);
                Assert.Equal(0, actions);
                Assert.Single(view.GetVisualDescendants().OfType<ScrollViewer>(),
                    scroll => scroll.Classes.Contains("prime-roster-scroll"));
            }
            finally
            {
                window.Close();
            }

            var queued = new LobbyWaitlistSnapshot(1,
                ImmutableArray.Create(new LobbyQueueEntrySummary(1, "Local Pilot",
                    LobbyQueueEntryState.Queued, 10)), IsSelfQueued: true,
                SelfState: LobbyQueueEntryState.Queued, SelfQueueSequence: 10);
            Control queuedView = PlayPresentation.Build(Context(shell, controller,
                lobby with { Waitlist = queued }));
            Assert.Contains(queuedView.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "PLAYER SEAT QUEUE");
        }
        finally
        {
            await controller.DisposeAsync();
            shell.Dispose();
        }
    }

    private static PlayPresentationContext Context(PrimeShellState shell,
        PlayController controller, LobbySnapshot? lobby,
        Action<string, Func<Task>>? run = null,
        PlayPresentationState? ui = null,
        Func<string?, double, string, Control>? buildPreview = null)
    {
        PlayState state = PlayState.Initial;
        if (lobby is not null)
        {
            LobbyMember current = lobby.Members[0];
            var session = new NodeSessionSnapshot(current.SessionId, current.PlayerId,
                current.DisplayName, Guid.NewGuid(), new string('a', 43),
                current.GuestSessionId);
            state = new PlayState(PlayPhase.Lobby, Array.Empty<NodeListing>(),
                new NodeControlClient.ViewState(Session: session, Lobby: lobby),
                current.Hunter, "", Loading: false, Revision: lobby.Revision);
        }
        return new PlayPresentationContext(
            shell,
            controller,
            state,
            Array.Empty<string>(),
            ui ?? new PlayPresentationState(),
            CancellationToken.None,
            run ?? ((_, _) => { }),
            static action => action(),
            static () => { },
            static () => { },
            buildPreview ?? ((_, height, _) => new Border { Height = height }),
            static _ => Task.CompletedTask,
            static _ => Task.CompletedTask,
            static (_, _) => { },
            static () => { },
            static (_, _) => { });
    }

    private static T FieldEditor<T>(Control root, string label) where T : Control
    {
        StackPanel field = Assert.Single(root.GetVisualDescendants()
            .OfType<StackPanel>(), candidate => candidate.Children
                .OfType<TextBlock>().Any(text => text.Text == label)
                && candidate.Children.OfType<T>().Any());
        return Assert.Single(field.Children.OfType<T>());
    }

    private static string VisibleText(Control root)
        => String.Join('\n', VisibleTextBlocks(root).Select(text => text.Text));

    private static TextBlock[] VisibleTextBlocks(Control root)
        => root.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible).ToArray();

    private static LobbySnapshot Lobby(Guid sessionId, MatchMode mode, byte team,
        bool ready, bool owner)
    {
        var current = new LobbyMember(sessionId, null, "Local Pilot", Hunter.Samus,
            team, ready, Observer: false, GuestSessionId: Guid.NewGuid());
        return new LobbySnapshot(Guid.NewGuid(), "Test Lobby", LobbyVisibility.Public,
            owner ? sessionId : Guid.NewGuid(), LobbyPhase.Open, Revision: 7,
            PlayerLimit: 4, ObserverLimit: 2,
            ImmutableArray.Create(current), ImmutableArray<LobbyChatEntry>.Empty,
            MapKey: "MP1 SANCTORUS", Mode: mode,
            TimeLimitSeconds: 600, PointGoal: 7);
    }
}
