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
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests.Client;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PlayPresentationTests
{
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
                Assert.Single(defaultView.GetVisualDescendants().OfType<SeatOfferCard>());
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
}
