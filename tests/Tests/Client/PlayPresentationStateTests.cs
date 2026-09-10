using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ProjectPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher;
using Xunit;

namespace MphRead.Tests;

public sealed class PlayPresentationStateTests
{
    [Fact]
    public void ChatDraftIsBoundedInUtf8WithoutSplittingACharacter()
    {
        string draft = new string('é', 200);
        string bounded = Utf8TextLimit.Truncate(draft, 256);

        Assert.Equal(256, Encoding.UTF8.GetByteCount(bounded));
        Assert.Equal(128, bounded.Length);
        Assert.Equal(new string('é', 128), bounded);
    }

    [Fact]
    public void FiltersUseOnlyAuthoritativeCapacityAndModeFields()
    {
        LobbyListEntry open = Entry("Open", MatchMode.Battle, players: 1, playerLimit: 4,
            observers: 0, observerLimit: 2);
        LobbyListEntry full = Entry("Full", MatchMode.Battle, players: 4, playerLimit: 4,
            observers: 2, observerLimit: 2);
        LobbyListEntry spectatable = Entry("Spectatable", MatchMode.TeamBattle, players: 4,
            playerLimit: 4, observers: 1, observerLimit: 2);

        var filtered = MatchBrowserFiltering.Apply(
            new[] { full, spectatable, open },
            new MatchBrowserFilters(Mode: MatchMode.Battle, OpenPlayerSlotsOnly: true),
            MatchBrowserSort.Name);
        Assert.Equal(new[] { open.LobbyId }, filtered.Select(item => item.LobbyId));

        var spectators = MatchBrowserFiltering.Apply(
            new[] { full, spectatable, open },
            new MatchBrowserFilters(SpectatableOnly: true),
            MatchBrowserSort.Name);
        Assert.Equal(new[] { open.LobbyId, spectatable.LobbyId }, spectators.Select(item => item.LobbyId));
        Assert.True(MatchBrowserFiltering.IsWaitlistJoinable(full));
        Assert.False(MatchBrowserFiltering.IsPlayerJoinable(full));
    }

    [Fact]
    public void RuleControlsFollowModeApplicabilityAndBuildStructuredRules()
    {
        LobbyRuleApplicability survival = LobbyRuleApplicability.For(MatchMode.Survival);
        Assert.True(survival.StartingLives);
        Assert.False(survival.ScoreGoal);
        Assert.False(survival.ObjectiveTimeGoal);

        var draft = new HostMatchDraft
        {
            Mode = MatchMode.Survival,
            TimeLimitText = "10:00",
            StartingLivesText = "4",
            ScoreGoalText = "not applicable",
            KillcamPolicy = KillcamPolicy.Disabled
        };
        Assert.True(draft.TryBuildRules(out LobbyRulesOptions rules, out string error), error);
        Assert.Equal(600, rules.TimeLimitSeconds);
        Assert.Equal(4, rules.StartingLives);
        Assert.Null(rules.ScoreGoal);
        Assert.Equal(KillcamPolicy.Disabled, rules.KillcamPolicy);

        draft.Mode = MatchMode.Defender;
        draft.ObjectiveTimeGoalText = "120";
        Assert.True(draft.TryBuildRules(out rules, out error), error);
        Assert.Equal(120, rules.ObjectiveTimeGoalSeconds);
        Assert.Null(rules.ScoreGoal);
    }

    [Fact]
    public void PresentationStatePreservesDraftsAndAllowsOnlyOneOfferAction()
    {
        var ui = new PlayPresentationState();
        ui.SetChatDraft(new string('x', 300));
        ui.SetFilters(new MatchBrowserFilters(MapKey: "MP1 SANCTORUS", HideFull: true));
        Assert.Equal(256, Encoding.UTF8.GetByteCount(ui.ChatDraft));
        Assert.Equal("MP1 SANCTORUS", ui.Filters.MapKey);
        Assert.True(ui.Filters.HideFull);

        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Assert.True(ui.TryBeginOfferAction(first));
        Assert.False(ui.TryBeginOfferAction(first));
        ui.ObserveOffer(first);
        Assert.False(ui.TryBeginOfferAction(first));
        ui.ObserveOffer(second);
        Assert.True(ui.TryBeginOfferAction(second));
        Assert.False(ui.TryBeginOfferAction(Guid.Empty));
    }

    [Fact]
    public void ClearFiltersRemovesOnlyTheBoundedBrowserFilters()
    {
        var ui = new PlayPresentationState();
        ui.SetFilters(new MatchBrowserFilters(Mode: MatchMode.TeamBattle,
            MapKey: "MP1 SANCTORUS", OpenPlayerSlotsOnly: true,
            SpectatableOnly: true, HideFull: true));
        ui.SetSort(MatchBrowserSort.Name);

        ui.ClearFilters();

        Assert.Equal(new MatchBrowserFilters(), ui.Filters);
        Assert.Equal(MatchBrowserSort.Name, ui.Sort);
    }

    [Fact]
    public void ChatPresentationPreservesDraftAndBoundsUnreadAndScrollState()
    {
        var ui = new PlayPresentationState();
        Guid lobbyId = Guid.NewGuid();
        LobbyChatEntry[] initial = { Chat(1) };
        LobbyChatEntry[] history = Enumerable.Range(1, 50).Select(sequence => Chat(sequence)).ToArray();

        ui.SetChatDraft("typed draft");
        ui.ObserveChat(lobbyId, initial);
        ui.ObserveChat(lobbyId, history);
        Assert.Equal(PlayPresentationState.ChatHistoryLimit, ui.ChatUnreadCount);
        ui.ObserveChat(lobbyId, history);
        Assert.Equal(PlayPresentationState.ChatHistoryLimit, ui.ChatUnreadCount);

        ui.SetChatScrollOffset(double.NaN);
        Assert.Equal(0, ui.ChatScrollOffset);
        ui.SetChatScrollOffset(1_000_000);
        Assert.Equal(100_000, ui.ChatScrollOffset);
        Assert.True(ui.ChatScrollPositionKnown);

        ui.SetChatEditing(true);
        Assert.Equal(0, ui.ChatUnreadCount);
        Assert.True(ui.TryExitChatEditing());
        Assert.False(ui.TryExitChatEditing());
        Assert.Equal("typed draft", ui.ChatDraft);

        ui.ResetChatPresentation();
        Assert.Equal("typed draft", ui.ChatDraft);
        Assert.Equal(0, ui.ChatUnreadCount);
    }

    [Fact]
    public void PlayerFacingReadinessReasonOmitsInternalResetTerminology()
    {
        string message = PlayPresentation.PlayerFacingEligibilityMessage(
            new LobbyStartEligibility(false,
                "All players must be Ready. Changing settings resets readiness."));

        Assert.Equal("Waiting for every player to ready.", message);
        Assert.DoesNotContain("reset", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("revision", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NetworkAndSeatOfferLabelsStayPlayerFacing()
    {
        Assert.Equal("Automatic", PlayPresentation.PreferredRegionLabel("  "));
        Assert.Equal("Europe", PlayPresentation.PreferredRegionLabel(" eu-west "));
        Assert.Equal("Unknown (eu-central)", PlayPresentation.PreferredRegionLabel(" eu-central "));
        Assert.Equal("Offer expires in 0:12", SeatOfferCard.FormatCountdown(12));
        Assert.Equal("Offer expires in 1:00", SeatOfferCard.FormatCountdown(60));
        Assert.Equal("Offer expired", SeatOfferCard.FormatCountdown(0));
        Assert.Equal("Immediate seat", PrimeMatchCard.SeatPolicyLabel(LobbySeatPolicy.ImmediateSeat));
    }

    [Fact]
    public async Task DelayedChatSuccessAcknowledgesOnlyTheSubmittedDraftGeneration()
    {
        var ui = new PlayPresentationState();
        var posted = new List<Action>();
        int refreshes = 0;
        ui.SetChatDraft("first message");
        var send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task operation = PlayPresentation.ExecuteChatSendAsync(ui, "first message",
            () => send.Task, posted.Add, () => refreshes++);
        ui.SetChatDraft("newer typing");
        send.SetResult();
        await operation;

        Assert.Single(posted);
        Assert.Equal("newer typing", ui.ChatDraft);
        posted[0]();
        Assert.Equal("newer typing", ui.ChatDraft);
        Assert.Equal(1, refreshes);

        ui.SetChatDraft("stable message");
        posted.Clear();
        refreshes = 0;
        var secondSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task secondOperation = PlayPresentation.ExecuteChatSendAsync(ui, "stable message",
            () => secondSend.Task, posted.Add, () => refreshes++);
        // A normal authoritative rebuild preserves the bounded draft and its
        // generation while the request is still in flight.
        ui.ResetChatPresentation();
        secondSend.SetResult();
        await secondOperation;
        posted[0]();
        Assert.Equal("", ui.ChatDraft);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void FailedOfferActionCanRetryButSuccessfulOfferActionCannot()
    {
        var ui = new PlayPresentationState();
        Guid offer = Guid.NewGuid();

        Assert.True(ui.TryBeginOfferAction(offer));
        Assert.False(ui.TryBeginOfferAction(offer));

        ui.ReleaseOfferAction(offer);
        Assert.True(ui.TryBeginOfferAction(offer));
        Assert.False(ui.TryBeginOfferAction(offer));

        ui.CompleteOfferAction(offer);
        Assert.False(ui.TryBeginOfferAction(offer));
        ui.ReleaseOfferAction(offer);
        Assert.False(ui.TryBeginOfferAction(offer));
    }

    [Fact]
    public async Task OfferSuccessCompletesAndRefreshesThroughPostedUiAction()
    {
        var ui = new PlayPresentationState();
        Guid offer = Guid.NewGuid();
        var posted = new List<Action>();
        int refreshes = 0;

        Assert.True(ui.TryBeginOfferAction(offer));
        await PlayPresentation.ExecuteOfferActionAsync(ui, offer,
            () => Task.CompletedTask, posted.Add, () => refreshes++);

        Assert.False(ui.TryBeginOfferAction(offer));
        Assert.Equal(0, refreshes);
        Assert.Single(posted);

        posted[0]();
        Assert.Equal(1, refreshes);
        Assert.False(ui.TryBeginOfferAction(offer));
    }

    [Fact]
    public async Task OfferFailureReleasesAndRefreshesThroughPostedUiActionWithoutRetrying()
    {
        var ui = new PlayPresentationState();
        Guid offer = Guid.NewGuid();
        var posted = new List<Action>();
        int refreshes = 0;
        int attempts = 0;

        Assert.True(ui.TryBeginOfferAction(offer));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlayPresentation.ExecuteOfferActionAsync(ui, offer, () =>
            {
                attempts++;
                return Task.FromException(new InvalidOperationException("stale revision"));
            }, posted.Add, () => refreshes++));

        Assert.Equal(1, attempts);
        Assert.False(ui.TryBeginOfferAction(offer));
        Assert.Equal(0, refreshes);
        Assert.Single(posted);

        posted[0]();
        Assert.Equal(1, refreshes);
        Assert.True(ui.TryBeginOfferAction(offer));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void TimeParserRejectsOversizedMinuteValuesWithoutOverflow()
    {
        Assert.False(HostMatchDraft.TryParseTime("71582789:00", out _));
        Assert.False(HostMatchDraft.TryParseTime("2147483647:59", out _));
    }

    [Fact]
    public void LeaveConfirmationSurvivesRebuildAndIsOneShotAndCancelable()
    {
        var ui = new PlayPresentationState();
        Guid lobbyId = Guid.NewGuid();
        Guid otherLobbyId = Guid.NewGuid();

        ui.ObserveLobby(lobbyId);
        ui.RequestLeaveConfirmation(lobbyId);
        ui.ObserveLobby(lobbyId);
        Assert.True(ui.IsLeaveConfirmationOpen(lobbyId));

        ui.CancelLeaveConfirmation(lobbyId);
        Assert.False(ui.IsLeaveConfirmationOpen(lobbyId));

        ui.RequestLeaveConfirmation(lobbyId);
        Assert.True(ui.TryConfirmLeave(lobbyId));
        Assert.False(ui.TryConfirmLeave(lobbyId));

        ui.RequestLeaveConfirmation(lobbyId);
        ui.ObserveLobby(otherLobbyId);
        Assert.False(ui.IsLeaveConfirmationOpen(lobbyId));
        Assert.False(ui.TryConfirmLeave(lobbyId));

        ui.RequestLeaveConfirmation(lobbyId);
        ui.ObserveLobby(Guid.Empty);
        Assert.False(ui.IsLeaveConfirmationOpen(lobbyId));
    }

    [Theory]
    [InlineData(LobbyPhase.Open, false, false)]
    [InlineData(LobbyPhase.Open, true, true)]
    [InlineData(LobbyPhase.InMatch, false, true)]
    [InlineData(LobbyPhase.PostMatch, false, true)]
    public void LeaveConfirmationPolicyProtectsRecoverableStates(
        LobbyPhase phase, bool hasHandoff, bool expected)
    {
        Assert.Equal(expected, PlayPresentation.RequiresLeaveConfirmation(phase, hasHandoff));
    }

    private static LobbyListEntry Entry(string name, MatchMode mode, int players,
        int playerLimit, int observers, int observerLimit)
        => new(Guid.NewGuid(), name, LobbyPhase.Open, players, playerLimit, observers, 1,
            ObserverLimit: observerLimit, MapKey: "MP1 SANCTORUS", Mode: mode);

    private static LobbyChatEntry Chat(long sequence)
        => new(sequence, Guid.NewGuid(), "Pilot", $"Message {sequence}");
}
