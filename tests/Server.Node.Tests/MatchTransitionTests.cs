using System.Collections.Concurrent;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Workers;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class MatchTransitionTests
{
    [Theory]
    [InlineData(3, 3)]
    [InlineData(8, 6)]
    public void ProposerIsAutomaticallyYesAndUsesSevenTenthsThreshold(int playerCount, int needed)
    {
        var h = new LobbyHarness(playerCount);
        MatchSpec match = h.Start();

        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.Equal("Owner", proposal.ProposerName);
        Assert.Equal(playerCount, proposal.Eligible);
        Assert.Equal(1, proposal.Yes);
        Assert.Equal(0, proposal.No);
        Assert.Equal(needed, proposal.Needed);
        Assert.Equal(MatchTransitionVoteState.Pending, proposal.State);
        Assert.True(proposal.OwnVote);

        int additionalYes = needed - 1;
        for (int index = 1; index <= additionalYes; index++)
        {
            proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
                h.Players[index], new LobbyMatchTransitionVote(h.Revision,
                    match.MatchId.Value, proposal.BallotRevision, true)));
        }

        Assert.Equal(MatchTransitionVoteState.Approved, proposal.State);
        Assert.Equal(needed, proposal.Yes);
        Assert.Equal(LobbyPhase.InMatch, h.Current.Phase);
        Assert.Equal(match.MatchId.Value, h.Current.CurrentMatchId);
    }

    [Fact]
    public void VotesAreIdempotentButCannotChangeAndObserversAreExcluded()
    {
        var h = new LobbyHarness(8, includeObserver: true);
        MatchSpec match = h.Start();
        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        LobbyMatchTransitionVote no = new(h.Revision, match.MatchId.Value,
            proposal.BallotRevision, false);
        var first = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(h.Players[1], no));
        var repeat = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(h.Players[1],
            no with { ExpectedRevision = h.Revision }));

        Assert.Equal(first, repeat);
        Assert.False(repeat.OwnVote);
        Assert.Equal("already_voted", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[1], new LobbyMatchTransitionVote(h.Revision, match.MatchId.Value,
                proposal.BallotRevision, true))).Code);
        Assert.Equal("role", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Observer!, new LobbyMatchTransitionVote(h.Revision, match.MatchId.Value,
                proposal.BallotRevision, true))).Code);
        Assert.Equal(8, h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.Eligible);
    }

    [Fact]
    public void ChangeMapRequiresDifferentNodeHostedMapAndPreservesMode()
    {
        var h = new LobbyHarness(1);
        MatchSpec match = h.Start();
        var changed = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.ChangeMap, "unit2")));
        Assert.Equal("unit2", changed.TargetMapKey);
        Assert.Equal(MatchMode.Battle, changed.Mode);

        var same = new LobbyHarness(1);
        MatchSpec sameMatch = same.Start();
        Assert.Equal("map_same", Assert.Throws<LobbyCommandException>(() => same.Manager.Execute(
            same.Players[0], new LobbyMatchTransitionPropose(same.Revision,
                sameMatch.MatchId.Value, MatchTransitionChoice.ChangeMap, "unit"))).Code);
    }

    [Fact]
    public void ActiveHunterChangeAppliesOnlyToTheReplacementMatch()
    {
        var h = new LobbyHarness(1);
        MatchSpec first = h.Start();
        Assert.Equal(Hunter.Samus, Assert.Single(first.Roster
            .Where(seat => seat.Role == SeatRole.Player)).Hunter);

        LobbySnapshot selected = Assert.IsType<LobbySnapshot>(h.Manager.Execute(
            h.Players[0], new LobbySelectHunter(Hunter.Trace, h.Revision)));
        Assert.Equal(Hunter.Trace, selected.Members.Single().Hunter);

        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                first.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.True(h.Manager.TryBeginMatchTransition(first.MatchId.Value,
            proposal.TransitionId, out LobbyMatchTransitionSelection? transition));
        Assert.Equal(Hunter.Trace, transition!.Members.Single().Hunter);
        Assert.True(h.Manager.CompleteMatchTransition(first.MatchId.Value,
            proposal.TransitionId));

        MatchSpec replacement = Assert.Single(h.Manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.Equal(Hunter.Trace, Assert.Single(replacement.Roster
            .Where(seat => seat.Role == SeatRole.Player)).Hunter);
    }

    [Fact]
    public void ElectoratePruningCanApproveBallotForCoordinatorDiscovery()
    {
        var h = new LobbyHarness(3);
        MatchSpec match = h.Start();
        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[1], new LobbyMatchTransitionVote(h.Revision,
                match.MatchId.Value, proposal.BallotRevision, true)));
        Assert.Equal(MatchTransitionVoteState.Pending, proposal.State);

        h.Manager.Disconnect(h.Players[2].SessionId);

        Assert.True(h.Manager.TryGetApprovedMatchTransition(match.MatchId.Value, out var selection));
        Assert.NotNull(selection);
        Assert.Equal(proposal.TransitionId, selection!.TransitionId);
        Assert.Equal(2, h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.Eligible);
    }

    [Fact]
    public void StaleRevisionMatchAndBallotIdentityAreRejected()
    {
        var h = new LobbyHarness(3);
        MatchSpec match = h.Start();
        Assert.Equal("stale_revision", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision - 1,
                match.MatchId.Value, MatchTransitionChoice.Restart))).Code);
        Assert.Equal("phase", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                Guid.NewGuid(), MatchTransitionChoice.Restart))).Code);

        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.Equal("stale_ballot", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[1], new LobbyMatchTransitionVote(h.Revision,
                match.MatchId.Value, proposal.BallotRevision + 1, true))).Code);
        Assert.Equal("stale_ballot", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[1], new LobbyMatchTransitionVote(h.Revision,
                Guid.NewGuid(), proposal.BallotRevision, true))).Code);
    }

    [Fact]
    public void OneHumanProposalPassesImmediatelyAndCooldownsAreIdentityAndRoomScoped()
    {
        var clock = new MutableClock();
        var h = new LobbyHarness(1, clock: clock);
        MatchSpec match = h.Start();
        var first = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.Equal(MatchTransitionVoteState.Approved, first.State);
        Assert.Equal(1, first.Yes);
        Assert.Equal(1, first.Needed);

        Assert.Equal("cooldown", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart))).Code);
        clock.Advance(TimeSpan.FromSeconds(91));
        h.Manager.PruneWaitlists();
        Assert.Equal("cooldown", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart))).Code);
        clock.Advance(TimeSpan.FromSeconds(90));
        h.Manager.PruneWaitlists();
        var second = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.Equal(MatchTransitionVoteState.Approved, second.State);
    }

    [Fact]
    public void DeadlineEqualityExpiresAndImpossibleBallotRejectsImmediately()
    {
        var clock = new MutableClock();
        var h = new LobbyHarness(3, clock: clock);
        MatchSpec match = h.Start();
        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        clock.Advance(LobbyManager.MatchTransitionVoteWindow);
        h.Manager.PruneWaitlists();
        Assert.Equal(MatchTransitionVoteState.Expired,
            h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.State);
        Assert.Equal("stale_ballot", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[1], new LobbyMatchTransitionVote(h.Revision,
                match.MatchId.Value, proposal.BallotRevision, true))).Code);

        var impossible = new LobbyHarness(8);
        MatchSpec impossibleMatch = impossible.Start();
        var impossibleProposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(impossible.Manager.Execute(
            impossible.Players[0], new LobbyMatchTransitionPropose(impossible.Revision,
                impossibleMatch.MatchId.Value, MatchTransitionChoice.Restart)));
        for (int index = 1; index <= 3; index++)
            impossibleProposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(impossible.Manager.Execute(
                impossible.Players[index], new LobbyMatchTransitionVote(impossible.Revision,
                    impossibleMatch.MatchId.Value, impossibleProposal.BallotRevision, false)));
        Assert.Equal(MatchTransitionVoteState.Rejected, impossibleProposal.State);
    }

    [Fact]
    public void ReconnectGraceKeepsMemberInElectorateUntilExpiry()
    {
        var clock = new MutableClock();
        var h = new LobbyHarness(3, clock: clock);
        MatchSpec match = h.Start();
        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart)));
        h.Manager.SetSessionResumeDeadline(h.Players[2].SessionId,
            clock.GetUtcNow().AddSeconds(10));
        Assert.Equal(3, h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.Eligible);

        clock.Advance(TimeSpan.FromSeconds(11));
        _ = h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(2, h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.Eligible);
        Assert.Equal(MatchTransitionVoteState.Pending,
            h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.State);
        Assert.Equal(proposal.BallotRevision,
            h.Manager.MatchTransitionForSession(h.Players[0].SessionId)!.BallotRevision);
    }

    [Fact]
    public void TournamentIdentityRejectsActiveMatchTransition()
    {
        var h = new LobbyHarness(1);
        LobbySnapshot lobby = h.Current;
        h.Manager.Execute(h.Players[0], new LobbyTournamentIdentity(lobby.Revision,
            Guid.NewGuid(), Guid.NewGuid()));
        h.Manager.Execute(h.Players[0], new LobbyTournamentControl(
            h.Revision, TournamentControl.Resume));
        MatchSpec match = h.Start();
        Assert.Equal("unsupported", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                match.MatchId.Value, MatchTransitionChoice.Restart))).Code);
    }

    [Fact]
    public void RestartCompletionPreservesLobbyRulesRosterAndRetiresOnlyOldMatch()
    {
        var h = new LobbyHarness(3);
        LobbySnapshot configured = h.Current;
        MatchSpec first = h.Start();
        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                first.MatchId.Value, MatchTransitionChoice.Restart)));
        h.Manager.Execute(h.Players[1], new LobbyMatchTransitionVote(h.Revision,
            first.MatchId.Value, proposal.BallotRevision, true));
        var approved = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[2], new LobbyMatchTransitionVote(h.Revision,
                first.MatchId.Value, proposal.BallotRevision, true)));
        Assert.True(h.Manager.TryBeginMatchTransition(first.MatchId.Value,
            approved.TransitionId, out var selection));
        Assert.NotNull(selection);
        Assert.True(h.Manager.CompleteMatchTransition(first.MatchId.Value,
            approved.TransitionId));
        LobbySnapshot open = h.Current;
        Assert.Equal(LobbyPhase.Open, open.Phase);
        Assert.Equal(first.Content.MapKey, open.MapKey);
        Assert.Equal(first.Rules.Mode, open.Mode);
        Assert.Equal(configured.Members.Select(m => m.SessionId), open.Members.Select(m => m.SessionId));
        Assert.All(open.Members, member => Assert.False(member.Ready));

        MatchSpec second = Assert.Single(h.Manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.NotEqual(first.Rng1Seed, second.Rng1Seed);
        Assert.NotEqual(first.Rng2Seed, second.Rng2Seed);
        Assert.Equal(first.Rules, second.Rules);
        Assert.Equal(first.Content.MapKey, second.Content.MapKey);
        Assert.False(h.Manager.MatchEnded(first.MatchId, true));
        Assert.Equal(second.MatchId.Value, h.Current.CurrentMatchId);
    }

    [Fact]
    public void CustomChangeMapUsesExistingReadinessPolicyBeforeFreshPlacement()
    {
        string baseHash = new('a', 64);
        string contentHash = new('b', 64);
        MapRequirement requirement = new("custom.transition", "1.0.0", contentHash,
            new string('c', 64), 128, MapRequirement.ComputeMatchContentHash(
                baseHash, "custom.transition", "1.0.0", contentHash, "test", 8));
        NodeContentCatalog catalog = new([
            new ContentIdentity("unit", baseHash, "1", "test", 8),
            new ContentIdentity("custom", baseHash, "1", "test", 8, requirement)]);
        var manager = new LobbyManager { ContentCatalog = catalog };
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Custom", LobbyVisibility.Public, 1));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        MatchSpec first = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            catalog.Get("unit", MatchMode.Battle), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.True(manager.MatchReady(new(first.MatchId, new(1), new(Guid.NewGuid()),
            Guid.NewGuid(), "localhost", 10000)));

        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(manager.Execute(owner,
            new LobbyMatchTransitionPropose(manager.ForSession(owner.SessionId)!.Revision,
                first.MatchId.Value,
                MatchTransitionChoice.ChangeMap, "custom")));
        Assert.Equal(MatchTransitionVoteState.Approved, proposal.State);
        Assert.True(manager.TryBeginMatchTransition(first.MatchId.Value,
            proposal.TransitionId, out _));
        Assert.True(manager.CompleteMatchTransition(first.MatchId.Value,
            proposal.TransitionId));
        Assert.Equal(LobbyPhase.Open, manager.ForSession(owner.SessionId)!.Phase);
        Assert.False(manager.ForSession(owner.SessionId)!.Members.Single().Ready);
        Assert.Empty(manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));

        LobbySnapshot ready = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, manager.ForSession(owner.SessionId)!.Revision));
        MatchSpec second = Assert.Single(manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.Equal("custom", second.Content.MapKey);
        Assert.Equal(requirement, second.Content.RequiredMap);
        Assert.Equal(LobbyPhase.StartingMatch, manager.ForSession(owner.SessionId)!.Phase);
        Assert.True(ready.Members.Single().Ready);
    }

    [Fact]
    public void ContinuationFreezesRosterAndSettingsUntilRequiredMapIsReady()
    {
        var h = new LobbyHarness(2);
        LobbySnapshot configured = h.Current;
        LobbyRulesOptions rules = new(TimeLimitSeconds: 300, ScoreGoal: 10,
            DamageLevel: 2, FriendlyFire: true);
        configured = (LobbySnapshot)h.Manager.Execute(h.Players[0],
            new LobbyConfigure(configured.Revision, "unit", MatchMode.Battle, Rules: rules));
        configured = (LobbySnapshot)h.Manager.Execute(h.Players[1],
            new LobbyRequestTeam(1, configured.Revision));
        _ = h.Manager.Execute(h.Players[1],
            new LobbySelectHunter(Hunter.Trace, h.Manager.ForSession(h.Players[0].SessionId)!.Revision));
        MatchSpec first = h.Start();

        string customHash = new('b', 64);
        MapRequirement requirement = new("custom.transition", "1.0.0", customHash,
            new string('c', 64), 128, MapRequirement.ComputeMatchContentHash(
                "custom-base", "custom.transition", "1.0.0", customHash, "test", 8));
        h.Manager.ContentCatalog = new NodeContentCatalog([
            new ContentIdentity("unit", "hash", "1", "test", 8),
            new ContentIdentity("custom", "custom-base", "1", "test", 8, requirement)]);

        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                first.MatchId.Value, MatchTransitionChoice.ChangeMap, "custom")));
        proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[1], new LobbyMatchTransitionVote(h.Revision,
                first.MatchId.Value, proposal.BallotRevision, true)));
        Assert.True(h.Manager.TryBeginMatchTransition(first.MatchId.Value,
            proposal.TransitionId, out _));
        Assert.True(h.Manager.CompleteMatchTransition(first.MatchId.Value,
            proposal.TransitionId));

        LobbySnapshot frozen = h.Current;
        LobbyIdentity newcomer = new(Guid.NewGuid(), Guid.NewGuid(), "Newcomer");
        Assert.Equal("transitioning", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            newcomer, new LobbyJoin(frozen.LobbyId, frozen.Revision))).Code);
        Assert.Equal("transitioning", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            newcomer, new LobbyQueueJoin(frozen.LobbyId, frozen.Revision))).Code);
        Assert.Equal("transitioning", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyConfigure(frozen.Revision, "unit2", MatchMode.Survival))).Code);
        Assert.Equal("transitioning", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[1], new LobbyRequestTeam(0, frozen.Revision))).Code);
        Assert.Equal("transitioning", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[1], new LobbySelectHunter(Hunter.Samus, frozen.Revision))).Code);
        Assert.Equal("transitioning", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(
            h.Players[0], new LobbyTournamentIdentity(frozen.Revision,
                Guid.NewGuid(), Guid.NewGuid()))).Code);
        Assert.Equal(2, frozen.Members.Count(member => !member.Observer));
        Assert.Equal(first.Rules.Mode, frozen.Mode);
        Assert.Equal("custom", frozen.MapKey);
        Assert.Equal(rules.Normalize(MatchMode.Battle), frozen.Rules);
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));

        _ = h.Manager.Execute(h.Players[0], new LobbySetReady(true, h.Revision));
        _ = h.Manager.Execute(h.Players[1], new LobbySetReady(true, h.Revision));
        MatchSpec replacement = Assert.Single(h.Manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.Equal("custom", replacement.Content.MapKey);
        Assert.Equal(first.Rules.With(roomKey: "custom"), replacement.Rules);
        Assert.Equal(first.Roster.Select(seat => (seat.PlayerId, seat.GuestSessionId,
            seat.Hunter, seat.Team, seat.Role)), replacement.Roster.Select(seat =>
            (seat.PlayerId, seat.GuestSessionId, seat.Hunter, seat.Team, seat.Role)));

        var quick = new LobbyHarness(1, lobbyPlayerLimit: 2);
        MatchSpec quickMatch = quick.Start();
        var quickProposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(quick.Manager.Execute(
            quick.Players[0], new LobbyMatchTransitionPropose(quick.Revision,
                quickMatch.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.True(quick.Manager.TryBeginMatchTransition(quickMatch.MatchId.Value,
            quickProposal.TransitionId, out _));
        Assert.True(quick.Manager.CompleteMatchTransition(quickMatch.MatchId.Value,
            quickProposal.TransitionId));
        Assert.Equal("no_match", Assert.Throws<LobbyCommandException>(() => quick.Manager.Execute(
            new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Quick"), new QuickPlayJoin())).Code);
        Assert.Single(quick.Current.Members);
    }

    [Fact]
    public void FailedTransitionPreparationReopensLobbyAndRetainsFailureProjection()
    {
        var h = new LobbyHarness(1);
        MatchSpec first = h.Start();
        var approved = Assert.IsType<NodeMatchTransitionVoteSnapshot>(h.Manager.Execute(
            h.Players[0], new LobbyMatchTransitionPropose(h.Revision,
                first.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.True(h.Manager.TryBeginMatchTransition(first.MatchId.Value,
            approved.TransitionId, out _));
        Assert.True(h.Manager.CompleteMatchTransition(first.MatchId.Value,
            approved.TransitionId));

        // Simulate the Node losing the hosted map catalog between the old
        // Worker acknowledgement and replacement preparation.
        h.Manager.ContentCatalog = null;
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));

        LobbySnapshot lobby = h.Current;
        Assert.Equal(LobbyPhase.Open, lobby.Phase);
        Assert.Null(lobby.CurrentMatchId);
        NodeMatchTransitionVoteSnapshot? failure = h.Manager.MatchTransitionForSession(h.Players[0].SessionId);
        Assert.NotNull(failure);
        Assert.Equal(approved.TransitionId, failure!.TransitionId);
        Assert.Equal(first.MatchId.Value, failure.MatchId);
        Assert.Equal(MatchTransitionVoteState.Failed, failure.State);
        Assert.Equal("map_unavailable", failure.FailureCode);
    }

    [Fact]
    public async Task ReplacementPlacementFailureReopensLobbyWithTransitionFailure()
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer,
            new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task loop = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyCreate("Transition", LobbyVisibility.Public));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
            NodeMatchHandoff first = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));

            // Keep the old match cancellable, but make the shared Worker
            // unavailable for the fresh replacement placement.
            scheduler.Drain("replacement placement failure");
            var approved = Assert.IsType<NodeMatchTransitionVoteSnapshot>(await coordinator.ExecuteAsync(owner,
                new LobbyMatchTransitionPropose(lobbies.ForSession(owner.SessionId)!.Revision,
                    first.MatchId,
                    MatchTransitionChoice.Restart)));
            Assert.Equal(MatchTransitionVoteState.Approved, approved.State);

            await WaitUntilAsync(() => lobbies.MatchTransitionForSession(owner.SessionId)
                is { State: MatchTransitionVoteState.Failed }, stop.Token);
            LobbySnapshot reopened = lobbies.ForSession(owner.SessionId)!;
            NodeMatchTransitionVoteSnapshot failure = lobbies.MatchTransitionForSession(owner.SessionId)!;
            Assert.Equal(LobbyPhase.Open, reopened.Phase);
            Assert.Null(reopened.CurrentMatchId);
            Assert.Equal(approved.TransitionId, failure.TransitionId);
            Assert.Equal(MatchTransitionVoteState.Failed, failure.State);
            Assert.Equal("replacement_placement_failed", failure.FailureCode);
            IReadOnlyList<object> replay = await coordinator.ForSessionEventsAsync(owner.SessionId);
            Assert.Contains(replay,
                value => value is NodeMatchTransitionVoteSnapshot snapshot
                    && snapshot.TransitionId == approved.TransitionId
                    && snapshot.State == MatchTransitionVoteState.Failed);
            Assert.DoesNotContain(replay,
                value => value is NodeMatchTransitionStarted started
                    && started.TransitionId == approved.TransitionId);
        }
        finally
        {
            stop.Cancel();
            await loop;
        }
    }

    [Fact]
    public async Task PostAckTransitionBoundaryFailureRetiresOldMatchAndPublishesFailure()
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var catalog = new NodeContentCatalog([new ContentIdentity("unit", "hash", "1", "test", 8)]);
        // Coordinator subscribes after this handler, giving the test a
        // deterministic terminal interleaving in which content disappears
        // immediately after Worker cancellation is acknowledged.
        scheduler.TransitionEnded += (_, _) => lobbies.ContentCatalog = null;
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer, catalog);
        try
        {
            LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyCreate("Transition", LobbyVisibility.Public));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbySetReady(true, lobby.Revision));
            await coordinator.ExecuteAsync(owner, new LobbyStart(lobby.Revision));
            NodeMatchHandoff first = Assert.IsType<NodeMatchHandoff>(coordinator.ForSession(owner.SessionId));
            var approved = Assert.IsType<NodeMatchTransitionVoteSnapshot>(await coordinator.ExecuteAsync(owner,
                new LobbyMatchTransitionPropose(lobbies.ForSession(owner.SessionId)!.Revision,
                    first.MatchId,
                    MatchTransitionChoice.Restart)));

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await WaitUntilAsync(() => lobbies.MatchTransitionForSession(owner.SessionId)
                is { State: MatchTransitionVoteState.Failed }, timeout.Token);
            LobbySnapshot reopened = lobbies.ForSession(owner.SessionId)!;
            NodeMatchTransitionVoteSnapshot failure = lobbies.MatchTransitionForSession(owner.SessionId)!;
            Assert.Equal(LobbyPhase.Open, reopened.Phase);
            Assert.Null(reopened.CurrentMatchId);
            Assert.Equal(first.MatchId, failure.MatchId);
            Assert.Equal(approved.TransitionId, failure.TransitionId);
            Assert.Equal(MatchTransitionVoteState.Failed, failure.State);
            Assert.Equal("transition_ack_rejected", failure.FailureCode);
            Assert.Contains(coordinator.ForSessionEvents(owner.SessionId),
                value => value is NodeMatchEnded ended
                    && ended.MatchId == first.MatchId && ended.Interrupted);
            Assert.DoesNotContain(coordinator.ForSessionEvents(owner.SessionId),
                value => value is NodeMatchTransitionStarted started
                    && started.TransitionId == approved.TransitionId);
        }
        finally
        {
            lobbies.ContentCatalog = catalog;
        }
    }

    [Fact]
    public async Task CoordinatorDiscoversPrunedApprovalAndOrdersStartedTerminalAndFreshHandoff()
    {
        var workers = new WorkerManager(new(Guid.NewGuid()), Guid.NewGuid());
        await using var scheduler = new WorkerScheduler(workers);
        using var issuer = new WorkerAdmissionIssuer("test");
        await scheduler.StartWorkerAsync(WorkerManagerTests.Launch("controlled-completion") with
        { Content = new("1", "hash", "test", 8) });
        var lobbies = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var second = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Player2");
        var third = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Player3");
        using var coordinator = new NodeMatchCoordinator(lobbies, scheduler, workers, issuer,
            new NodeContentCatalog([
                new ContentIdentity("unit", "hash", "1", "test", 8),
                new ContentIdentity("unit2", "hash2", "1", "test", 8)]));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notifications = new ConcurrentQueue<NodeMatchNotification>();
        Task reader = Task.Run(async () =>
        {
            try
            {
                await foreach (NodeMatchNotification notification in coordinator.ReadNotifications(stop.Token))
                    notifications.Enqueue(notification);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                // Cancellation is the deterministic end of the notification
                // collector after all ordering assertions have completed.
            }
        });
        Task loop = coordinator.RunContinuationsAsync(stop.Token);
        try
        {
            LobbySnapshot lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyCreate("Transition", LobbyVisibility.Public, 3));
            lobby = (LobbySnapshot)lobbies.Execute(second,
                new LobbyJoin(lobby.LobbyId, lobby.Revision));
            lobby = (LobbySnapshot)lobbies.Execute(third,
                new LobbyJoin(lobby.LobbyId, lobby.Revision));
            lobby = (LobbySnapshot)lobbies.Execute(owner,
                new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
            foreach (LobbyIdentity player in new[] { owner, second, third })
                lobbies.Execute(player, new LobbySetReady(true,
                    lobbies.ForSession(owner.SessionId)!.Revision));
            await coordinator.ExecuteAsync(owner,
                new LobbyStart(lobbies.ForSession(owner.SessionId)!.Revision));
            await WaitUntilAsync(() => notifications.Any(notification =>
                notification.Payload is NodeMatchHandoff), stop.Token);
            NodeMatchHandoff first = notifications
                .Where(notification => notification.SessionId == owner.SessionId)
                .Select(notification => notification.Payload)
                .OfType<NodeMatchHandoff>().First();

            var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(await coordinator.ExecuteAsync(owner,
                new LobbyMatchTransitionPropose(lobbies.ForSession(owner.SessionId)!.Revision, first.MatchId,
                    MatchTransitionChoice.Restart)));
            proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(await coordinator.ExecuteAsync(second,
                new LobbyMatchTransitionVote(lobbies.ForSession(second.SessionId)!.Revision, first.MatchId,
                    proposal.BallotRevision, true)));
            Assert.Equal(MatchTransitionVoteState.Pending, proposal.State);

            // The third member leaves while in reconnect grace/roster pruning;
            // the manager approves the now two-player electorate. No direct
            // vote response carries this approval to the coordinator.
            lobbies.Disconnect(third.SessionId);

            await WaitUntilAsync(() => notifications.Any(notification =>
                notification.Payload is NodeMatchHandoff handoff && handoff.MatchId != first.MatchId), stop.Token);
            NodeMatchNotification[] ownerEvents = notifications
                .Where(notification => notification.SessionId == owner.SessionId).ToArray();
            NodeMatchHandoff fresh = Assert.Single(ownerEvents.Select(notification => notification.Payload)
                .OfType<NodeMatchHandoff>(), handoff => handoff.MatchId != first.MatchId);
            Assert.NotEqual(first.Ticket, fresh.Ticket);
            Assert.NotEqual(first.Nonce, fresh.Nonce);
            Assert.NotEqual(first.WireMatchId, fresh.WireMatchId);
            int started = Array.FindIndex(ownerEvents, notification =>
                notification.Payload is NodeMatchTransitionStarted value
                    && value.PreviousMatchId == first.MatchId);
            int terminal = Array.FindIndex(ownerEvents, notification =>
                notification.Payload is NodeMatchEnded value
                    && value.MatchId == first.MatchId && value.Interrupted);
            int handoff = Array.FindIndex(ownerEvents, notification =>
                notification.Payload is NodeMatchHandoff value
                    && value.MatchId != first.MatchId);
            Assert.True(started >= 0 && terminal > started && handoff > terminal,
                string.Join(" | ", ownerEvents.Select(value => value.Payload.GetType().Name)));
            Assert.Equal(LobbyPhase.InMatch,
                lobbies.ForSession(owner.SessionId)!.Phase);
        }
        finally
        {
            stop.Cancel();
            await Task.WhenAll(loop, reader);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
            await Task.Delay(10, cancellationToken);
    }

    private sealed class LobbyHarness
    {
        private readonly NodeContentCatalog _catalog = new([
            new ContentIdentity("unit", "hash", "1", "test", 8),
            new ContentIdentity("unit2", "hash2", "1", "test", 8)]);
        public LobbyManager Manager { get; }
        public LobbyIdentity[] Players { get; }
        public LobbyIdentity? Observer { get; }
        public LobbySnapshot Current => Manager.ForSession(Players[0].SessionId)!;
        public long Revision => Current.Revision;

        public LobbyHarness(int playerCount, bool includeObserver = false,
            TimeProvider? clock = null, int? lobbyPlayerLimit = null)
        {
            Players = Enumerable.Range(0, playerCount)
                .Select(index => new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(),
                    index == 0 ? "Owner" : "Player" + index)).ToArray();
            Manager = new LobbyManager(clock: clock) { ContentCatalog = _catalog };
            LobbySnapshot lobby = (LobbySnapshot)Manager.Execute(Players[0],
                new LobbyCreate("Transition", LobbyVisibility.Public,
                    lobbyPlayerLimit ?? playerCount, includeObserver ? 1 : 0));
            for (int index = 1; index < Players.Length; index++)
                lobby = (LobbySnapshot)Manager.Execute(Players[index],
                    new LobbyJoin(lobby.LobbyId, lobby.Revision));
            if (includeObserver)
            {
                Observer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Observer");
                lobby = (LobbySnapshot)Manager.Execute(Observer,
                    new LobbyJoin(lobby.LobbyId, lobby.Revision, true));
            }
            lobby = (LobbySnapshot)Manager.Execute(Players[0],
                new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        }

        public MatchSpec Start()
        {
            foreach (LobbyIdentity player in Players)
                Manager.Execute(player, new LobbySetReady(true, Revision));
            MatchSpec match = Manager.PrepareMatch(Players[0].SessionId, Revision,
                _catalog.Get("unit", MatchMode.Battle), new(Guid.NewGuid()), Guid.NewGuid());
            Assert.True(Manager.MatchReady(new(match.MatchId, new(1), new(Guid.NewGuid()),
                Guid.NewGuid(), "localhost", 10000)));
            return match;
        }
    }

    private sealed class MutableClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
