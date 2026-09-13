using System;
using System.Reflection;
using System.Threading.Tasks;
using MphRead;
using MphRead.Identity;
using MphRead.Entities;
using MphRead.Mods.Launcher;
using MphRead.Mods.Network;
using MphRead.Mods.Launcher.Gui;
using ProjectPrime.Server.Shared;
using Xunit;

namespace MphRead.Tests.Client;

[Collection("Match baseline globals")]
public sealed class MatchTransitionLifecycleTests
{
    [Fact]
    public async Task StartedIsOnlyAnExpectationUntilTheOldMatchEnds()
    {
        Guid nodeId = Guid.NewGuid();
        Guid matchId = Guid.NewGuid();
        Guid transitionId = Guid.NewGuid();
        await using var node = NewNode(nodeId, out _);
        node.MarkGameplayJoined(matchId);
        object? previous = SwapNode(node);
        try
        {
            using var play = new AuthoritativePlay("127.0.0.1", 5000,
                "Hunter", Hunter.Samus);
            play.BindNodeMatch(matchId);
            node.ApplyEvent(Event("match.transition.started", 2,
                Started(nodeId, matchId, transitionId)));

            Assert.Equal(transitionId, node.ExpectedTransition!.TransitionId);
            Assert.False(node.ExpectedTransitionEndedFor(matchId));
            Assert.False(play.ObserveCompletion());
            Assert.Equal(AuthoritativePlay.TerminalState.Active, play.State);

            node.ApplyEvent(Event("match.ended", 3,
                new NodeMatchEnded(matchId, false)));

            Assert.True(node.ExpectedTransitionEndedFor(matchId));
            Assert.True(play.ObserveCompletion());
            Assert.Equal(AuthoritativePlay.TerminalState.Transitioning, play.State);
            Assert.Equal(transitionId, play.ExpectedTransition!.TransitionId);
        }
        finally
        {
            SwapNode(previous);
        }
    }

    [Fact]
    public async Task FailedTransitionClearsExpectationAndLeavesScenePlayable()
    {
        Guid nodeId = Guid.NewGuid();
        Guid matchId = Guid.NewGuid();
        Guid transitionId = Guid.NewGuid();
        await using var node = NewNode(nodeId, out _);
        node.MarkGameplayJoined(matchId);
        object? previous = SwapNode(node);
        try
        {
            using var play = new AuthoritativePlay("127.0.0.1", 5000,
                "Hunter", Hunter.Samus);
            play.BindNodeMatch(matchId);
            node.ApplyEvent(Event("match.transition.started", 2,
                Started(nodeId, matchId, transitionId)));
            node.ApplyEvent(Event("match.transition.state", 3,
                Vote(nodeId, matchId, transitionId,
                    MatchTransitionVoteState.Failed, "cancel rejected")));

            Assert.Null(node.ExpectedTransition);
            Assert.Equal("cancel rejected", node.TransitionVote!.FailureCode);
            Assert.False(node.ExpectedTransitionEnded);
            Assert.False(play.ObserveCompletion());
            Assert.Equal(AuthoritativePlay.TerminalState.Active, play.State);
        }
        finally
        {
            SwapNode(previous);
        }
    }

    [Fact]
    public async Task ActiveVoteAndExpectedTransitionSurviveReplacementHandoffAndOldEnd()
    {
        Guid nodeId = Guid.NewGuid();
        Guid lobbyId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid playerId = Guid.NewGuid();
        Guid firstMatch = Guid.NewGuid();
        Guid nextMatch = Guid.NewGuid();
        Guid transitionId = Guid.NewGuid();
        await using var node = NewNode(nodeId, out _,
            new NodeSessionSnapshot(sessionId, playerId, "Hunter", nodeId,
                new string('a', 43)));
        LobbySnapshot lobby = new(lobbyId, "Room", LobbyVisibility.Public,
            sessionId, LobbyPhase.StartingMatch, 1, 8, 16,
            [new LobbyMember(sessionId, playerId, "Hunter", Hunter.Samus, 0,
                false, false)], [], "map", MatchMode.Battle,
            CurrentMatchId: firstMatch);
        node.ApplyEvent(Event("lobby.snapshot", 2, lobby));
        NodeMatchHandoff first = new(firstMatch, 1, "127.0.0.1", 5000,
            "ticket", 1, false, Hunter.Samus);
        node.ApplyEvent(Event("match.handoff", 3, first));
        node.MarkGameplayJoined(firstMatch);
        node.ApplyEvent(Event("match.transition.state", 4,
            Vote(lobbyId, firstMatch, transitionId,
                MatchTransitionVoteState.Pending)));
        node.ApplyEvent(Event("match.transition.started", 5,
            Started(lobbyId, firstMatch, transitionId)));
        NodeMatchHandoff replacement = first with
        {
            MatchId = nextMatch,
            WireMatchId = 2,
            Nonce = 2
        };
        // The coalesced StartingMatch snapshot advances LastLobbyMatchId
        // before the old terminal event arrives; admission must still use the
        // transition's PreviousMatchId.
        node.ApplyEvent(Event("lobby.snapshot", 6,
            lobby with { Revision = 2, CurrentMatchId = nextMatch }));
        node.ApplyEvent(Event("match.handoff", 7, replacement));
        node.ApplyEvent(Event("match.ended", 8,
            new NodeMatchEnded(firstMatch, false)));
        node.ApplyEvent(Event("match.completion", 9,
            new NodeMatchCompletion(Completion(firstMatch))));

        Assert.Equal(nextMatch, node.Handoff!.MatchId);
        Assert.False(node.MatchEnded);
        Assert.Equal(transitionId, node.ExpectedTransition!.TransitionId);
        Assert.True(node.ExpectedTransitionEndedFor(firstMatch));
        Assert.Equal(firstMatch, node.TransitionVote!.MatchId);
        Assert.Null(node.CompletionSummaryFor(firstMatch));
        Assert.Null(node.CompletionFor(nextMatch));
    }

    [Fact]
    public async Task LifecycleEpochAndHandoffGenerationFenceLateControlEvents()
    {
        Guid nodeId = Guid.NewGuid();
        Guid currentMatch = Guid.NewGuid();
        Guid replacementMatch = Guid.NewGuid();
        await using var node = NewNode(nodeId, out _, new NodeSessionSnapshot(
            Guid.NewGuid(), Guid.NewGuid(), "Hunter", nodeId, new string('a', 43)));

        NodeMatchHandoff current = new(currentMatch, 11, "127.0.0.1", 5000,
            "ticket", 1, false, Hunter.Samus, Guid.Empty, "", false,
            new HandoffGeneration(3), new MatchLifecycleEpoch(7));
        node.ApplyEvent(Event("match.handoff", 2, current));
        node.ApplyEvent(Event("match.handoff", 3, current with
        {
            Nonce = 2,
            HandoffGeneration = new HandoffGeneration(2)
        }));
        node.ApplyEvent(Event("match.ended", 4,
            new NodeMatchEnded(currentMatch, true, new MatchLifecycleEpoch(6))));

        Assert.Equal(current, node.Handoff);
        Assert.False(node.MatchEnded);
        Assert.Equal((ulong)7, node.State.LifecycleEpoch.Value);
        Assert.Equal((ulong)3, node.State.HandoffGeneration.Value);

        NodeMatchHandoff replacement = current with
        {
            MatchId = replacementMatch,
            WireMatchId = 12,
            Nonce = 3,
            HandoffGeneration = HandoffGeneration.Initial,
            LifecycleEpoch = new MatchLifecycleEpoch(8)
        };
        node.ApplyEvent(Event("match.handoff", 5, replacement));
        node.ApplyEvent(Event("match.completion", 6,
            new NodeMatchCompletion(Completion(currentMatch),
                new MatchLifecycleEpoch(7))));

        Assert.Equal(replacementMatch, node.Handoff!.MatchId);
        Assert.False(node.MatchEnded);
        Assert.Equal((ulong)8, node.State.LifecycleEpoch.Value);
    }

    [Fact]
    public async Task DeliveryOverflowRequiresAuthoritativeResume()
    {
        Guid nodeId = Guid.NewGuid();
        await using var node = NewNode(nodeId, out _);

        node.ApplyEvent(Event("control.delivery_overflow", 2,
            new NodeControlDeliveryOverflow()));

        Assert.True(node.State.ResumeRequired);
        Assert.Equal("delivery_overflow", node.State.ErrorCode);
        Assert.Contains("resume", node.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TransitionExitAndSessionPhasesAreExplicit()
    {
        Assert.Equal(MatchExitReason.Transitioning,
            MatchRunResult.Classify(started: true, quit: false, left: false,
                completed: false, interrupted: false, connectionFailed: false,
                clientError: false, transitioning: true));

        var flow = new ClientSessionCoordinator();
        flow.ShowHome(hasIdentity: true, hasLobby: true);
        flow.BeginLaunch();
        flow.NotifyMatchStarted();
        flow.NotifyMatchEnded(new MatchRunResult(MatchExitReason.Transitioning));
        Assert.Equal(ClientSessionPhase.PreparingContinuation, flow.Phase);
        flow.BeginLaunch();
        Assert.Equal(ClientSessionPhase.Launching, flow.Phase);
    }

    [Fact]
    public void TransitionMapIntersectionAndHudNoticeAreDeterministic()
    {
        Assert.Equal(new[] { "map-a", "map-b" },
            MapPickerView.IntersectHostedAndLocal(
                ["map-b", "current", "map-a", "map-b"],
                ["map-a", "map-b", "map-c"], "current"));

        DateTimeOffset now = new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        string notice = PlayerPresentation.FormatTransitionVoteNotice(
            Vote(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                MatchTransitionVoteState.Pending) with
            {
                ProposerName = "Host",
                TargetMapKey = "map-a",
                Deadline = now.AddSeconds(12)
            }, now);
        Assert.Contains("Host", notice);
        Assert.Contains("Restart map-a", notice);
        Assert.Contains("Yes 0/2", notice);
        Assert.Contains("12s", notice);
        Assert.Contains("Press Start to vote", notice);
    }

    [Fact]
    public void PreparedRequiredMapAutoReadiesOnlyTheApprovedContinuation()
    {
        Guid nodeId = Guid.NewGuid();
        Guid lobbyId = Guid.NewGuid();
        Guid sessionId = Guid.NewGuid();
        Guid playerId = Guid.NewGuid();
        Guid matchId = Guid.NewGuid();
        Guid transitionId = Guid.NewGuid();
        MapRequirement requirement = new("custom.transition", "1.0.0",
            new string('b', 64), new string('c', 64), 128,
            MapRequirement.ComputeMatchContentHash(new string('a', 64),
                "custom.transition", "1.0.0", new string('b', 64), "test", 8));
        NodeSessionSnapshot session = new(sessionId, playerId, "Hunter", nodeId,
            new string('d', 43));
        LobbySnapshot lobby = new(lobbyId, "Room", LobbyVisibility.Public,
            sessionId, LobbyPhase.Open, 2, 8, 16,
            [new LobbyMember(sessionId, playerId, "Hunter", Hunter.Samus, 0,
                false, false)], [], "custom", MatchMode.Battle,
            RequiredMap: requirement);
        NodeMatchTransitionStarted started = new(lobbyId, matchId, transitionId,
            MatchTransitionChoice.ChangeMap, "custom", MatchMode.Battle);
        NodeMatchTransitionVoteSnapshot vote = new(lobbyId, matchId, transitionId,
            1, sessionId, "Hunter", MatchTransitionChoice.ChangeMap, "custom",
            MatchMode.Battle, 1, 1, 0, 1, DateTimeOffset.UtcNow.AddSeconds(30),
            MatchTransitionVoteState.Approved, true);
        NodeControlClient.ViewState state = new(Session: session, Lobby: lobby,
            TransitionVote: vote, ExpectedTransition: started,
            ExpectedTransitionEnded: true);

        Assert.True(PlayController.ShouldAutoReadyTransitionMap(state, requirement));
        Assert.False(PlayController.ShouldAutoReadyTransitionMap(
            state with { ExpectedTransitionEnded = false }, requirement));
        Assert.False(PlayController.ShouldAutoReadyTransitionMap(
            state with { Lobby = lobby with
                {
                    Members = [lobby.Members[0] with { Ready = true }]
                } }, requirement));
    }

    private static byte[] Event<T>(string type, long eventId, T payload)
        => NodeControlCodec.Write(type, eventId, null, payload);

    private static NodeMatchTransitionStarted Started(Guid lobbyId, Guid matchId,
        Guid transitionId)
        => new(lobbyId, matchId, transitionId, MatchTransitionChoice.Restart,
            "map", MatchMode.Battle);

    private static NodeMatchTransitionVoteSnapshot Vote(Guid lobbyId, Guid matchId,
        Guid transitionId, MatchTransitionVoteState state,
        string? failureCode = null)
        => new(lobbyId, matchId, transitionId, 1, Guid.NewGuid(), "Host",
            MatchTransitionChoice.Restart, "map", MatchMode.Battle,
            Eligible: 3, Yes: 0, No: 0, Needed: 2,
            DateTimeOffset.UtcNow.AddSeconds(30), state, null, failureCode);

    private static MatchCompletionSummary Completion(Guid matchId)
        => new(new(matchId), new(Guid.NewGuid()), MatchEndReason.ScoreGoal,
            [new(Guid.NewGuid(), new PlayerId(Guid.NewGuid()), ParticipantKind.RegisteredHuman,
                "Hunter", ParticipantOutcome.Finished, 0, 0, 7, 3, 1)],
            Guid.NewGuid(), null, Guid.NewGuid());

    private static NodeControlClient NewNode(Guid nodeId, out Guid sessionId,
        NodeSessionSnapshot? session = null)
    {
        session ??= new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(),
            "Hunter", nodeId, new string('a', 43));
        sessionId = session.SessionId;
        var node = new NodeControlClient(nodeId);
        node.ApplyEvent(Event("node.session", 1, session));
        return node;
    }

    private static object? SwapNode(object? value)
    {
        FieldInfo field = typeof(NodeSessions).GetField("_current",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        object? previous = field.GetValue(null);
        field.SetValue(null, value);
        return previous;
    }
}
