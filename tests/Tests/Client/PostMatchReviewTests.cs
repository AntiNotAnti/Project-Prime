using System;
using System.Collections.Immutable;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ProjectPrime.Server.Shared;
using MphRead.Identity;
using MphRead.Mods.Input;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using MphRead.Tests.Client;
using Xunit;

namespace MphRead.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class PostMatchReviewTests
{
    [Fact]
    public void ClosingGameWindowOverridesEvenReadyContinuation()
    {
        var state = new NodeControlClient.ViewState(Handoff:
            new NodeMatchHandoff(Guid.NewGuid(), 1, "127.0.0.1", 5000, "ticket", 1, false, Hunter.Samus));
        Assert.Equal(PostMatchTransition.Quit, PostMatchFlow.Evaluate(state, Guid.NewGuid(), gameWindowOpen: false));
    }

    [Fact]
    public void LeavePreemptsVoteWithoutCancelingItsOwnRequest()
    {
        using var scope = new PostMatchCommandScope();
        Assert.True(scope.TryBeginVote(out var vote));
        Assert.True(scope.TryBeginLeave(out var leave));
        Assert.True(vote.IsCancellationRequested);
        Assert.False(leave.IsCancellationRequested);
        Assert.False(scope.TryBeginVote(out _));
        Assert.False(scope.TryBeginLeave(out _));
        scope.CompleteVote();
        scope.CompleteLeave();
        Assert.True(scope.TryBeginLeave(out var retry));
        scope.Dispose();
        Assert.True(retry.IsCancellationRequested);
    }

    [Fact]
    public void RecapCommandsSerializeHunterSelectionWithVoteAndLeave()
    {
        using var scope = new PostMatchCommandScope();
        Assert.True(scope.TryBeginHunter(out var hunter));
        Assert.False(scope.TryBeginVote(out _));
        Assert.True(scope.TryBeginLeave(out var leave));
        Assert.True(hunter.IsCancellationRequested);
        Assert.False(leave.IsCancellationRequested);
        scope.CompleteHunter();
        scope.CompleteLeave();
        Assert.True(scope.TryBeginVote(out var vote));
        scope.Dispose();
        Assert.True(vote.IsCancellationRequested);
    }

    [AvaloniaFact]
    public void RecapHunterSelectionTracksLobbyAuthorityAndLocksAtResolution()
    {
        Guid sessionId = Guid.NewGuid();
        Guid playerId = Guid.NewGuid();
        var member = new LobbyMember(sessionId, playerId, "Pilot",
            Hunter.Kanden, 0, false, false);
        var lobby = new LobbySnapshot(Guid.NewGuid(), "Arena",
            LobbyVisibility.Public, sessionId, LobbyPhase.PostMatch, 4, 1, 0,
            ImmutableArray.Create(member), ImmutableArray<LobbyChatEntry>.Empty,
            "MP1 SANCTORUS", MatchMode.Battle);
        var option = new LobbyVoteEntry(1, LobbyVoteChoice.Rematch,
            lobby.MapKey, lobby.Mode, 0);
        var round = new NodeRoundSnapshot(lobby, null, null, false, false,
            1, 2, DateTimeOffset.UtcNow.AddMinutes(1),
            ImmutableArray.Create(option), 0);
        using var view = new PostMatchView(null);
        view.Update(round, lobby: lobby, localSessionId: sessionId);

        Assert.True(view.HunterSelector.IsEnabled);
        Assert.Equal(Hunter.Kanden, view.HunterSelector.SelectedItem);
        Hunter? requested = null;
        view.HunterRequested += hunter => requested = hunter;
        view.HunterSelector.SelectedItem = Hunter.Trace;

        Assert.Equal(Hunter.Trace, requested);
        Assert.True(view.HunterChangePending);
        Assert.False(view.HunterSelector.IsEnabled);

        LobbySnapshot acceptedLobby = lobby with
        {
            Revision = lobby.Revision + 1,
            Members = ImmutableArray.Create(member with { Hunter = Hunter.Trace })
        };
        view.Update(round, lobby: acceptedLobby, localSessionId: sessionId);
        Assert.False(view.HunterChangePending);
        Assert.True(view.HunterSelector.IsEnabled);
        Assert.Equal(Hunter.Trace, view.HunterSelector.SelectedItem);

        view.Update(round with { ResolvedOption = option }, lobby: acceptedLobby,
            localSessionId: sessionId);
        Assert.False(view.HunterSelector.IsEnabled);
    }

    [AvaloniaFact]
    public void BallotAndScoreboardHaveIndependentConstrainedScrollRegions()
    {
        using var view = new PostMatchView(null);
        var zones = Assert.IsType<Grid>(view.Content);
        Assert.Equal(2, zones.ColumnDefinitions.Count);
        var score = Assert.IsType<Grid>(zones.Children[0]);
        var vote = Assert.IsType<Grid>(zones.Children[1]);
        Assert.Equal(1, Grid.GetColumn(vote));
        Assert.True(score.RowDefinitions[1].Height.IsStar);
        Assert.True(vote.RowDefinitions[2].Height.IsStar);
        ScrollViewer scoreScroll = Assert.Single(score.Children.OfType<ScrollViewer>());
        ScrollViewer ballotScroll = Assert.Single(vote.Children.OfType<ScrollViewer>());
        Assert.Equal(440, scoreScroll.MaxHeight);
        Assert.Equal(440, ballotScroll.MaxHeight);
        Assert.Equal(ScrollBarVisibility.Auto, scoreScroll.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Auto, ballotScroll.VerticalScrollBarVisibility);
        Assert.DoesNotContain(vote.Children.OfType<ScrollViewer>(), scroll => scroll.Content is Grid);
        Assert.Equal(6, vote.RowDefinitions.Count); // Heading, countdown, ballot scroll, hints, Confirm, Cancel.
        var cancel = Assert.IsType<Avalonia.Controls.Button>(vote.Children[5]);
        Assert.Equal("ResultsCancelLeave", cancel.Name);
        Assert.False(cancel.IsVisible);
    }

    [Fact]
    public void WinningCardIsMarkedIndependentlyOfLocalVote()
    {
        var winner = new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "map", MatchMode.Battle, 4);
        Assert.Contains("NEXT MATCH", PostMatchView.CardHeading(winner, 0, 2, winner));
        var returned = winner with { Choice = LobbyVoteChoice.ReturnToLobby };
        Assert.Contains("RETURNING TO LOBBY", PostMatchView.CardHeading(returned, 0, 0, returned));
        Assert.DoesNotContain("NEXT MATCH", PostMatchView.CardHeading(winner, 0, 1, returned with { Id = 2 }));
    }

    [AvaloniaFact]
    public void TouchLeaveButtonRequiresConfirmationBeforeDirectLeave()
    {
        using var view = new PostMatchView(null);
        bool raised = false;
        view.LeaveRequested += () => raised = true;
        var zones = Assert.IsType<Grid>(view.Content);
        var vote = Assert.IsType<Grid>(zones.Children[1]);
        var leave = Assert.IsType<Avalonia.Controls.Button>(vote.Children[4]);
        var cancel = Assert.IsType<Avalonia.Controls.Button>(vote.Children[5]);
        Assert.Equal("Leave Lobby", leave.Content);
        Assert.Equal("ResultsLeaveLobby", leave.Name);
        string? leaveTip = ToolTip.GetTip(leave)?.ToString();
        Assert.Contains("leave the lobby directly", leaveTip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("shared vote", leaveTip, StringComparison.OrdinalIgnoreCase);

        leave.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));
        Assert.False(raised);
        Assert.True(view.LeaveConfirmationPending);
        Assert.Equal("Confirm Leave Lobby", leave.Content);
        Assert.True(cancel.IsVisible);
        Assert.Equal("Cancel", cancel.Content);

        cancel.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));

        Assert.False(raised);
        Assert.False(view.LeaveConfirmationPending);

        leave.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));
        leave.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(
            Avalonia.Controls.Button.ClickEvent));
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void ControllerConfirmAndBackActionsStayWithinLeaveConfirmation()
    {
        using var view = new PostMatchView(null);
        int raised = 0;
        view.LeaveRequested += () => raised++;

        view.RequestLeave(); // Controller B opens the confirmation.
        Assert.True(view.LeaveConfirmationPending);
        view.CancelLeaveConfirmation(); // Controller B cancels while it is open.
        Assert.False(view.LeaveConfirmationPending);
        Assert.Equal(0, raised);

        view.RequestLeave();
        view.SubmitSelection(); // Controller A confirms.
        Assert.False(view.LeaveConfirmationPending);
        Assert.Equal(1, raised);
    }

    [AvaloniaTheory]
    [InlineData(899, true)]
    [InlineData(900, false)]
    public void ResultsUseMeasuredWidthBreakpoint(double width, bool compact)
    {
        using var view = new PostMatchView(null);
        Arrange(view, width, compact ? 800 : 560);

        var zones = Assert.IsType<Grid>(view.Content);
        Assert.Equal(compact, view.IsCompactLayout);
        Assert.Equal(compact, view.ScoreboardUsesMobileCards);
        Assert.Equal(compact ? 1 : 2, zones.ColumnDefinitions.Count);
        Assert.Equal(compact ? 2 : 1, zones.RowDefinitions.Count);
        var score = Assert.IsType<Grid>(zones.Children[0]);
        var vote = Assert.IsType<Grid>(zones.Children[1]);
        Assert.Equal(compact ? 0 : 1, Grid.GetColumn(vote));
        Assert.Equal(compact ? 1 : 0, Grid.GetRow(vote));
        Assert.Equal(ScrollBarVisibility.Disabled,
            Assert.Single(score.Children.OfType<ScrollViewer>()).HorizontalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled,
            Assert.Single(vote.Children.OfType<ScrollViewer>()).HorizontalScrollBarVisibility);
    }

    [AvaloniaFact]
    public void ResultsUnavailableCopyIsPlayerFacingAndSpecific()
    {
        using var view = new PostMatchView(null);
        var zones = Assert.IsType<Grid>(view.Content);
        var score = Assert.IsType<Grid>(zones.Children[0]);
        var header = Assert.IsType<StackPanel>(score.Children[0]);
        Assert.Contains(header.Children.OfType<TextBlock>(), text =>
            text.Text == "Match results are unavailable.");
        Assert.DoesNotContain(header.Children.OfType<TextBlock>(), text =>
            text.Text == "Authoritative match results are unavailable.");

        ScrollViewer scoreScroll = Assert.Single(score.Children.OfType<ScrollViewer>());
        var scoreboard = Assert.IsType<StackPanel>(scoreScroll.Content);
        var empty = Assert.IsType<Border>(Assert.Single(scoreboard.Children));
        var message = Assert.IsType<TextBlock>(empty.Child);
        Assert.Equal("Match results are unavailable.", message.Text);
    }

    [AvaloniaFact]
    public void EmptyAuthoritativeScoreboardUsesPlayerResultsCopy()
    {
        using var view = new PostMatchView(CompletionSnapshot(withPlayer: false));
        var zones = Assert.IsType<Grid>(view.Content);
        var score = Assert.IsType<Grid>(zones.Children[0]);
        ScrollViewer scoreScroll = Assert.Single(score.Children.OfType<ScrollViewer>());
        var scoreboard = Assert.IsType<StackPanel>(scoreScroll.Content);
        var empty = Assert.IsType<Border>(Assert.Single(scoreboard.Children));
        var message = Assert.IsType<TextBlock>(empty.Child);

        Assert.Equal("No player results were received.", message.Text);
    }

    [AvaloniaFact]
    public void CompactScoreboardUsesSingleLabelInsteadOfDesktopColumnHeader()
    {
        using var view = new PostMatchView(CompletionSnapshot(withPlayer: true));
        Arrange(view, 899, 800);

        var zones = Assert.IsType<Grid>(view.Content);
        var score = Assert.IsType<Grid>(zones.Children[0]);
        ScrollViewer scoreScroll = Assert.Single(score.Children.OfType<ScrollViewer>());
        var scoreboard = Assert.IsType<StackPanel>(scoreScroll.Content);
        var header = Assert.IsType<Border>(scoreboard.Children[0]);
        var label = Assert.IsType<TextBlock>(header.Child);

        Assert.Equal("SCOREBOARD", label.Text);
        Assert.DoesNotContain("PLAYER", label.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("K / D / A", label.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void SelectedBallotCardHasSemanticStateAndExplicitLabels()
    {
        var options = ImmutableArray.Create(
            new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "same-map", MatchMode.Battle, 2),
            new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "next-map", MatchMode.Battle, 1));
        var round = new NodeRoundSnapshot(null!, null, null, false, false, 4, 9,
            DateTimeOffset.UtcNow.AddMinutes(1), options, OwnVote: 1,
            ResolvedOption: options[1]);
        using var view = new PostMatchView(null);
        view.Update(round);

        var zones = Assert.IsType<Grid>(view.Content);
        var vote = Assert.IsType<Grid>(zones.Children[1]);
        ScrollViewer ballotScroll = Assert.Single(vote.Children.OfType<ScrollViewer>());
        var ballot = Assert.IsType<StackPanel>(ballotScroll.Content);
        Avalonia.Controls.Button[] cards = ballot.Children
            .OfType<Avalonia.Controls.Button>().ToArray();
        Assert.Equal(2, cards.Length);

        var ownHeading = Assert.IsType<TextBlock>(
            Assert.IsType<StackPanel>(cards[0].Content).Children[0]);
        var resolvedHeading = Assert.IsType<TextBlock>(
            Assert.IsType<StackPanel>(cards[1].Content).Children[0]);
        Assert.Contains("YOUR VOTE", ownHeading.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("SELECTED", ownHeading.Text, StringComparison.Ordinal);
        Assert.Contains("SELECTED", resolvedHeading.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("YOUR VOTE", resolvedHeading.Text, StringComparison.Ordinal);

        view.MoveSelection(1);

        Assert.Equal(new Thickness(2), cards[1].BorderThickness);

        var selectedSurface = Assert.IsType<SolidColorBrush>(cards[1].Background);
        var normalSurface = Assert.IsType<SolidColorBrush>(cards[0].Background);
        Assert.Equal(GuiTheme.BrandSurface, selectedSurface.Color);
        Assert.NotEqual(normalSurface.Color, selectedSurface.Color);
        var selectedBorder = Assert.IsType<SolidColorBrush>(cards[1].BorderBrush);
        Assert.Equal(GuiTheme.Brand, selectedBorder.Color);
    }

    [AvaloniaTheory]
    [InlineData(940, 560)]
    [InlineData(899, 560)]
    [InlineData(720, 560)]
    [InlineData(560, 500)]
    [InlineData(560, 800)]
    public void ResultsActionTargetsRemainFullWidthAndInsideViewport(double width, double height)
    {
        using var view = new PostMatchView(null);
        view.RequestLeave();
        var window = new Window { Width = width, Height = height, Content = view };
        try
        {
            window.Show();
            Arrange(window, width, height);

            var zones = Assert.IsType<Grid>(view.Content);
            var vote = Assert.IsType<Grid>(zones.Children[1]);
            Avalonia.Controls.Button[] actions =
            [
                Assert.IsType<Avalonia.Controls.Button>(vote.Children[4]),
                Assert.IsType<Avalonia.Controls.Button>(vote.Children[5])
            ];
            foreach (Avalonia.Controls.Button action in actions)
            {
                Assert.True(action.IsEffectivelyVisible);
                Assert.True(action.MinHeight >= 48);
                Point? origin = action.TranslatePoint(new Point(), view);
                Assert.True(origin.HasValue);
                Rect bounds = new(origin!.Value, action.Bounds.Size);
                Assert.True(bounds.Left >= -1 && bounds.Right <= width + 1,
                    $"{action.Name} overflows the horizontal viewport: {bounds}");
                Assert.True(bounds.Top >= -1 && bounds.Bottom <= height + 1,
                    $"{action.Name} is clipped by the vertical viewport: {bounds}");
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void MobileBallotCardsUseTheMeasuredColumnAndTouchTarget()
    {
        var options = ImmutableArray.Create(
            new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "same-map", MatchMode.Battle, 2),
            new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "next-map", MatchMode.Battle, 1));
        var round = new NodeRoundSnapshot(null!, null, null, false, false, 4, 9,
            DateTimeOffset.UtcNow.AddMinutes(1), options, OwnVote: 0);
        using var view = new PostMatchView(null);
        view.Update(round);
        var window = new Window { Width = 560, Height = 800, Content = view };
        try
        {
            window.Show();
            Arrange(window, 560, 800);
            var zones = Assert.IsType<Grid>(view.Content);
            var vote = Assert.IsType<Grid>(zones.Children[1]);
            ScrollViewer ballotScroll = Assert.Single(vote.Children.OfType<ScrollViewer>());
            Avalonia.Controls.Button[] cards = view.GetVisualDescendants()
                .OfType<Avalonia.Controls.Button>()
                .Where(button => button.Name?.StartsWith("ResultsOption",
                    StringComparison.Ordinal) == true)
                .ToArray();
            Assert.Equal(2, cards.Length);
            Assert.True(view.IsCompactLayout);
            Assert.True(view.ScoreboardUsesMobileCards);
            Assert.All(cards, card =>
            {
                Assert.Equal(HorizontalAlignment.Stretch, card.HorizontalAlignment);
                Assert.True(card.MinHeight >= 48);
                Assert.True(card.Bounds.Width > 0);
                Assert.True(card.Bounds.Width <= ballotScroll.Bounds.Width + 1,
                    $"{card.Name} exceeds its ballot column: {card.Bounds.Width}"
                    + $" > {ballotScroll.Bounds.Width}");
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ResizingAfterVotePreservesAuthoritativeBallotAndSelection()
    {
        var options = ImmutableArray.Create(
            new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "same-map", MatchMode.Battle, 2),
            new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "next-map", MatchMode.Battle, 1));
        var round = new NodeRoundSnapshot(null!, null, null, false, false, 4, 9,
            DateTimeOffset.UtcNow.AddMinutes(1), options, OwnVote: 2);
        using var view = new PostMatchView(null);
        view.Update(round);
        uint revision = view.Selection.BallotRevision;
        int selectedIndex = view.Selection.SelectedIndex;
        byte ownVote = view.Ballot.OwnVote;
        byte[] optionIds = view.Ballot.Options.Select(option => option.Id).ToArray();

        Arrange(view, 560, 800);
        Arrange(view, 940, 560);
        view.Update(round);

        Assert.Equal(revision, view.Selection.BallotRevision);
        Assert.Equal(selectedIndex, view.Selection.SelectedIndex);
        Assert.Equal(ownVote, view.Ballot.OwnVote);
        Assert.Equal(optionIds, view.Ballot.Options.Select(option => option.Id));
        Assert.Equal(1, view.Ballot.Options[1].Votes);
        Assert.False(view.Ballot.CanVote);
    }

    [AvaloniaFact]
    public void HintsFollowInputFamilyAndTouchHidesNoisyPrompts()
    {
        using var view = new PostMatchView(null);
        GamepadState before = GamepadInput.State;
        try
        {
            GamepadInput.State = new GamepadState
            {
                Connected = true,
                Name = "DualSense",
                Family = ControllerFamily.PlayStation
            };
            view.SetInputDevice(PrimeInputDevice.Gamepad);
            view.RequestLeave();
            Assert.Contains("Cross", view.InputHint, StringComparison.Ordinal);
            Assert.Contains("Circle", view.InputHint, StringComparison.Ordinal);

            view.SetInputDevice(PrimeInputDevice.Touch);
            Assert.False(view.InputHintVisible);
            Assert.Equal("", view.InputHint);
        }
        finally
        {
            GamepadInput.State = before;
        }
    }

    [AvaloniaFact]
    public async Task ContinuationLoadingStopsResultsWorkAndClosesIdempotently()
    {
        using var shell = new PrimeShellState();
        await using var play = new PlayController(shell);
        var window = new PostMatchWindow(play, Guid.NewGuid(), null);
        try
        {
            Func<bool> pump = () => true;
            typeof(PostMatchWindow).GetField("_pump", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, pump);
            DispatcherTimer timer = (DispatcherTimer)typeof(PostMatchWindow)
                .GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(window)!;
            timer.Start();
            Assert.True(window.IsScenePumpActive);
            Assert.True(window.IsGameplayPollingActive);

            var transition = new MatchTransitionState(
                MatchTransitionStage.LoadingNextRound, "next-map", "Battle",
                "Samus", "Preparing the selected arena.");
            Assert.True(window.EnterContinuationLoading(transition));
            Assert.Equal(PostMatchPresentationMode.ContinuationLoading, window.Mode);
            Assert.False(window.EnterContinuationLoading(transition));
            Assert.False(window.IsScenePumpActive);
            Assert.False(window.IsGameplayPollingActive);
            Assert.True(window.View.IsContinuationLoading);
            Assert.True(window.Topmost);
            Assert.Equal(GuiTheme.InkBrush, window.View.Background);
            Assert.Equal(1, window.View.Opacity);

            var stage = Assert.IsType<MatchTransitionView>(window.View.Content);
            Assert.Equal(MatchTransitionStage.LoadingNextRound,
                stage.State.Stage);
            Assert.Equal("next-map", stage.State.Map);

            Assert.True(window.CompleteContinuation());
            Assert.False(window.CompleteContinuation());
            Assert.True(window.IsClosed);
        }
        finally
        {
            if (!window.IsClosed) window.Close();
        }
    }

    [AvaloniaFact]
    public async Task UserCloseDuringContinuationPropagatesAfterResultsWaitHasEnded()
    {
        using var shell = new PrimeShellState();
        await using var play = new PlayController(shell);
        var window = new PostMatchWindow(play, Guid.NewGuid(), null);
        bool closeRequested = false;
        window.UserCloseRequested += (_, _) => closeRequested = true;
        try
        {
            Assert.True(window.EnterContinuationLoading(new MatchTransitionState(
                MatchTransitionStage.LoadingNextRound,
                Detail: "Preparing the selected arena.")));

            window.Close();

            Assert.True(closeRequested);
            Assert.True(window.IsClosed);
            Assert.Equal(PostMatchTransition.Quit, window.Transition);
        }
        finally
        {
            if (!window.IsClosed) window.CloseForTransition();
        }
    }

    [AvaloniaFact]
    public async Task CoordinatorCloseOfContinuationDoesNotMasqueradeAsUserQuit()
    {
        using var shell = new PrimeShellState();
        await using var play = new PlayController(shell);
        var window = new PostMatchWindow(play, Guid.NewGuid(), null);
        bool closeRequested = false;
        window.UserCloseRequested += (_, _) => closeRequested = true;
        try
        {
            Assert.True(window.EnterContinuationLoading(new MatchTransitionState(
                MatchTransitionStage.LoadingNextRound)));

            Assert.True(window.CompleteContinuation());

            Assert.False(closeRequested);
            Assert.True(window.IsClosed);
            Assert.Equal(PostMatchTransition.Continue, window.Transition);
        }
        finally
        {
            if (!window.IsClosed) window.CloseForTransition();
        }
    }

    [AvaloniaFact]
    public void ContinuationStageRejectsCommandsAndStaleResultUpdates()
    {
        var options = ImmutableArray.Create(
            new LobbyVoteEntry(1, LobbyVoteChoice.Rematch, "same-map", MatchMode.Battle, 1),
            new LobbyVoteEntry(2, LobbyVoteChoice.NextMap, "next-map", MatchMode.Battle, 0));
        var round = new NodeRoundSnapshot(null!, null, null, false, false, 1, 2,
            DateTimeOffset.UtcNow.AddMinutes(1), options, OwnVote: 0);
        using var view = new PostMatchView(null);
        int votes = 0;
        int leaves = 0;
        view.VoteRequested += _ => votes++;
        view.LeaveRequested += () => leaves++;
        view.Update(round);
        view.MoveSelection(1);
        int selected = view.Selection.SelectedIndex;
        uint revision = view.Selection.BallotRevision;
        PostMatchBallotModel ballot = view.Ballot;
        Assert.True(view.EnterContinuationLoading(new MatchTransitionState(
            MatchTransitionStage.LoadingNextRound, "next-map", "Battle",
            "Samus", "Preparing the selected arena.")));
        object stageContent = view.Content!;
        view.Choose();
        view.RequestLeave();
        view.ConfirmLeave();
        view.Update(round with { OwnVote = 1, BallotRevision = 3 });

        Assert.Equal(0, votes);
        Assert.Equal(0, leaves);
        Assert.False(view.LeaveConfirmationPending);
        Assert.Equal(selected, view.Selection.SelectedIndex);
        Assert.Equal(revision, view.Selection.BallotRevision);
        Assert.Same(ballot, view.Ballot);
        Assert.Same(stageContent, view.Content);
    }

    private static void Arrange(Control view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static MatchResultsSnapshot CompletionSnapshot(bool withPlayer)
    {
        PlayerId? local = withPlayer ? new PlayerId(Guid.NewGuid()) : null;
        ImmutableArray<PlayerOutcomeSummary> players = withPlayer
            ? ImmutableArray.Create(new PlayerOutcomeSummary(Guid.NewGuid(), local,
                ParticipantKind.RegisteredHuman, "Local", ParticipantOutcome.Finished,
                Standing: 0, TeamStanding: 0, Points: 3, Kills: 2, Deaths: 1,
                Slot: 0, Hunter: Hunter.Samus, TeamIndex: 0))
            : ImmutableArray<PlayerOutcomeSummary>.Empty;
        var completion = new MatchCompletionSummary(new(Guid.NewGuid()), new(Guid.NewGuid()),
            MatchEndReason.ScoreGoal, players, Guid.NewGuid(), null, Guid.NewGuid());
        return new MatchResultsSnapshot("fixture-map", GameMode.Battle, null, completion, local);
    }
}
