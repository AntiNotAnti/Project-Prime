using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FruityPrime.Server.Shared;
using MphRead;
using MphRead.Mods.Launcher.Gui;
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
            ScoreGoalText = "not applicable"
        };
        Assert.True(draft.TryBuildRules(out LobbyRulesOptions rules, out string error), error);
        Assert.Equal(600, rules.TimeLimitSeconds);
        Assert.Equal(4, rules.StartingLives);
        Assert.Null(rules.ScoreGoal);

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
}
