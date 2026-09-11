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

    private static PlayPresentationContext Context(PrimeShellState shell,
        PlayController controller, LobbySnapshot? lobby,
        Action<string, Func<Task>>? run = null)
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
            new PlayPresentationState(),
            CancellationToken.None,
            run ?? ((_, _) => { }),
            static action => action(),
            static () => { },
            static () => { },
            static (_, height, _) => new Border { Height = height },
            static _ => Task.CompletedTask,
            static _ => Task.CompletedTask,
            static (_, _) => { },
            static () => { },
            static (_, _) => { });
    }

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
