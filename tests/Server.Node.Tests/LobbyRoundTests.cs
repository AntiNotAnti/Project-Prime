using System.Collections.Immutable;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

[Trait("LifecycleFast", "true")]
public sealed class LobbyRoundTests
{
    [Fact]
    public void MatchOwnershipIndexTracksPrepareTerminalReplacementAndDeletionBoundaries()
    {
        var h = new Harness();
        MatchSpec first = h.Start();
        AssertOwnership(h.Manager, first.MatchId.Value, h.Snapshot.LobbyId);

        Assert.True(h.Manager.MatchReady(new(first.MatchId, new(1),
            new(Guid.NewGuid()), Guid.NewGuid(), "localhost", 10000)));
        AssertOwnership(h.Manager, first.MatchId.Value, h.Snapshot.LobbyId);
        Assert.True(h.Manager.MatchEnded(first.MatchId, interrupted: false));
        // A completed match remains owned/indexed throughout PostMatch.
        AssertOwnership(h.Manager, first.MatchId.Value, h.Snapshot.LobbyId);

        NodeRoundSnapshot ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        NodeRoundSnapshot reopened = h.Round(new LobbyVoteCast(h.Revision,
            ballot.BallotRevision, 3));
        Assert.Equal(LobbyPhase.Open, reopened.Lobby.Phase);
        Assert.Null(reopened.Lobby.CurrentMatchId);
        AssertOwnership(h.Manager);

        MatchSpec second = h.Start();
        AssertOwnership(h.Manager, second.MatchId.Value, h.Snapshot.LobbyId);
        Assert.DoesNotContain(first.MatchId.Value,
            h.Manager.MatchOwnershipSnapshot().Index.Keys);
        Assert.True(h.Manager.MatchEnded(second.MatchId, interrupted: true));
        AssertOwnership(h.Manager);

        // A late terminal for the old lifecycle cannot recreate ownership.
        Assert.False(h.Manager.MatchEnded(first.MatchId, interrupted: true));
        h.Manager.Disconnect(h.Owner.SessionId);
        AssertOwnership(h.Manager);
    }

    [Fact]
    public void FailedContinuationPreparationAndTransitionPreserveIndexInvariant()
    {
        var h = new Harness();
        MatchSpec first = h.Start();
        Assert.True(h.Manager.MatchEnded(first.MatchId, interrupted: false));
        NodeRoundSnapshot ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, 1));

        // The approved continuation cannot acquire its configured content;
        // the manager reopens without leaving a stale replacement identity.
        h.Manager.ContentCatalog = null;
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
        Assert.Equal(LobbyPhase.Open, h.Snapshot.Phase);
        AssertOwnership(h.Manager);

        var transitionHarness = new Harness();
        MatchSpec active = transitionHarness.Start();
        Assert.True(transitionHarness.Manager.MatchReady(new(active.MatchId,
            new(1), new(Guid.NewGuid()), Guid.NewGuid(), "localhost", 10000)));
        var proposal = Assert.IsType<NodeMatchTransitionVoteSnapshot>(
            transitionHarness.Manager.Execute(transitionHarness.Owner,
                new LobbyMatchTransitionPropose(transitionHarness.Revision,
                    active.MatchId.Value, MatchTransitionChoice.Restart)));
        Assert.True(transitionHarness.Manager.TryBeginMatchTransition(
            active.MatchId.Value, proposal.TransitionId, out _));
        AssertOwnership(transitionHarness.Manager, active.MatchId.Value,
            transitionHarness.Snapshot.LobbyId);

        // A transition failure leaves the old active lifecycle authoritative.
        Assert.True(transitionHarness.Manager.FailMatchTransition(
            active.MatchId.Value, proposal.TransitionId));
        AssertOwnership(transitionHarness.Manager, active.MatchId.Value,
            transitionHarness.Snapshot.LobbyId);
        Assert.True(transitionHarness.Manager.MatchEnded(active.MatchId, true));
        AssertOwnership(transitionHarness.Manager);
    }

    [Theory]
    [InlineData(1, "MP1 SANCTORUS")]
    [InlineData(2, "MP4 HIGHGROUND")]
    [InlineData(4, "MP2 HARVESTER")]
    public void NodeBallotStartsFreshMatchWithoutReadyingPlayers(byte option, string map)
    {
        var h = new Harness();
        var first = h.Start(); h.Manager.MatchEnded(first.MatchId, false);
        var ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        Assert.Equal(15, (ballot.VoteDeadline!.Value - h.Manager.RoundClock.GetUtcNow()).TotalSeconds, 1);
        Assert.Equal(5, ballot.Options.Length);
        Assert.Equal("unsupported", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteOpen(h.Revision, []))).Code);
        Assert.Equal("unsupported", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteResolve(h.Revision, ballot.BallotRevision))).Code);
        var resolved = h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, option));
        Assert.Equal(option, resolved.ResolvedOption!.Id);
        var second = Assert.Single(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.True(second.LifecycleEpoch.Value > first.LifecycleEpoch.Value);
        Assert.Equal(map, second.Content.MapKey);
        Assert.Equal(LobbyPhase.StartingMatch, h.Snapshot.Phase);
        Assert.False(h.Snapshot.Members[0].Ready);
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
    }

    [Fact]
    public void PostMatchContinuationClearsBallotAtWorkerReady()
    {
        var h = new Harness();
        MatchSpec first = h.Start();
        Assert.True(h.Manager.MatchEnded(first.MatchId, false));
        NodeRoundSnapshot ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, 1));
        MatchSpec replacement = Assert.Single(h.Manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;

        Assert.True(h.Manager.MatchReady(new(replacement.MatchId, new(1),
            new(Guid.NewGuid()), Guid.NewGuid(), "localhost", 10000)));
        NodeRoundSnapshot active = h.Manager.RoundForSession(h.Owner.SessionId)!;
        Assert.Equal(LobbyPhase.InMatch, active.Lobby.Phase);
        Assert.Empty(active.Options);
        Assert.Null(active.ResolvedOption);
    }

    [Fact]
    public void SpawnPolicyVoteFreezesIntoTheNextAuthoritativeMatch()
    {
        var h = new Harness();
        MatchSpec first = h.Start();
        h.Manager.MatchEnded(first.MatchId, false);
        NodeRoundSnapshot ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        LobbyVoteEntry enhanced = Assert.Single(ballot.Options, option =>
            option.Choice == LobbyVoteChoice.SpawnPolicy
                && option.SpawnPolicy == SpawnPolicy.Enhanced);

        h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, enhanced.Id));
        MatchSpec next = Assert.Single(h.Manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;

        Assert.Equal(SpawnPolicy.Enhanced, next.Rules.SpawnPolicy);
        Assert.Equal(SpawnPolicy.Enhanced, h.Snapshot.Rules!.SpawnPolicy);
        Assert.Equal(first.Content.MapKey, next.Content.MapKey);
    }

    [Fact]
    public void HunterCanChangeDuringRecapAndIsFrozenIntoNextMatch()
    {
        var h = new Harness();
        MatchSpec first = h.Start();
        h.Manager.MatchEnded(first.MatchId, false);
        NodeRoundSnapshot ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;

        var selected = (LobbySnapshot)h.Manager.Execute(h.Owner,
            new LobbySelectHunter(Hunter.Trace, h.Revision));

        Assert.Equal(LobbyPhase.PostMatch, selected.Phase);
        Assert.Equal(Hunter.Trace, selected.Members.Single().Hunter);
        NodeRoundSnapshot resolved = h.Round(new LobbyVoteCast(h.Revision,
            ballot.BallotRevision, 1));
        Assert.NotNull(resolved.ResolvedOption);
        Assert.Equal("phase", Assert.Throws<LobbyCommandException>(() =>
            h.Manager.Execute(h.Owner, new LobbySelectHunter(Hunter.Spire,
                h.Revision))).Code);

        MatchSpec next = Assert.Single(h.Manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.Equal(Hunter.Trace, Assert.Single(next.Roster
            .Where(seat => seat.Role == SeatRole.Player)).Hunter);
        Assert.Equal(Hunter.Samus, Assert.Single(first.Roster
            .Where(seat => seat.Role == SeatRole.Player)).Hunter);
    }

    [Fact]
    public void ReturnVoteResetsLobbyAndInterruptedMatchHasNoBallot()
    {
        var h = new Harness(); var first = h.Start(); h.Manager.MatchEnded(first.MatchId, false);
        var ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        var resolved = h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, 3));
        Assert.Equal(LobbyPhase.Open, resolved.Lobby.Phase);
        Assert.Null(resolved.Lobby.CurrentMatchId);
        Assert.False(resolved.Lobby.Members[0].Ready);
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
        var second = h.Start(); h.Manager.MatchEnded(second.MatchId, true);
        Assert.Equal(LobbyPhase.Open, h.Snapshot.Phase);
        Assert.Empty(h.Manager.RoundForSession(h.Owner.SessionId)!.Options);
    }

    [Fact]
    public void TournamentPauseAndIdentityStayAtNodeWhileActiveSpecRemainsFrozen()
    {
        var h = new Harness(); Guid tournament = Guid.NewGuid(), round = Guid.NewGuid();
        h.Round(new LobbyTournamentIdentity(h.Revision, tournament, round));
        h.Ready();
        Assert.Equal("paused", Assert.Throws<LobbyCommandException>(() => h.Prepare()).Code);
        h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.Resume));
        MatchSpec first = h.Prepare();
        Assert.Equal(tournament, first.TournamentId); Assert.Equal(round, first.RoundId);
        h.Manager.MatchReady(new(first.MatchId, new(1), new(Guid.NewGuid()), Guid.NewGuid(), "localhost", 10000));
        h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.PauseBetweenRounds));
        Assert.Equal(LobbyPhase.InMatch, h.Snapshot.Phase);
        Assert.Equal("phase", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyTournamentSelectNext(h.Revision, Guid.NewGuid(), "MP4 HIGHGROUND", MatchMode.Battle))).Code);
        h.Manager.MatchEnded(first.MatchId, false);
        Guid next = Guid.NewGuid();
        h.Round(new LobbyTournamentSelectNext(h.Revision, next, "MP4 HIGHGROUND", MatchMode.Battle));
        Assert.Equal("MP1 SANCTORUS", first.Rules.RoomKey); Assert.Equal(round, first.RoundId);
        h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.Resume));
        MatchSpec second = h.Start(); Assert.Equal(next, second.RoundId);
        h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.EndTournament));
        Assert.Equal(LobbyPhase.StartingMatch, h.Snapshot.Phase); // End holds future rounds, not the active Worker.
        Assert.Equal("ended", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.Resume))).Code);
    }

    [Fact]
    public void PermissionsStaleRevisionsAndUnsupportedOperationsFailExplicitly()
    {
        var h = new Harness(); var observer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Observer");
        h.Manager.Execute(observer, new LobbyJoin(h.Snapshot.LobbyId, h.Revision, true));
        Assert.Equal("owner", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(observer, new LobbyTournamentIdentity(h.Revision, Guid.NewGuid(), Guid.NewGuid()))).Code);
        h.Round(new LobbyTournamentIdentity(h.Revision, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Equal("unsupported", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.ForceActiveResult))).Code);
        Assert.Equal("stale_revision", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyRoundStatus(h.Revision - 1))).Code);
        byte[] frame = NodeControlCodec.Write("lobby.round", 1, null, h.Round(new LobbyRoundStatus(h.Revision)));
        Assert.True(frame.Length < NodeControlCodec.MaximumFrameBytes);
    }

    [Fact]
    public void TournamentOwnerControlsTeamsAndSpectatorPermissionBeforeFreeze()
    {
        var manager = new LobbyManager(); var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var player = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Player");
        var lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Teams", LobbyVisibility.Public, 2, 1));
        lobby = (LobbySnapshot)manager.Execute(player, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        long Revision() => manager.ForSession(owner.SessionId)!.Revision;
        manager.Execute(owner, new LobbyTournamentIdentity(Revision(), Guid.NewGuid(), Guid.NewGuid()));
        var assigned = (NodeRoundSnapshot)manager.Execute(owner, new LobbyTournamentAssignTeam(Revision(), player.SessionId, 1));
        Assert.Equal(1, assigned.Lobby.Members.Single(m => m.SessionId == player.SessionId).Team);
        Assert.Equal("owner", Assert.Throws<LobbyCommandException>(() => manager.Execute(player, new LobbyTournamentSetObserver(Revision(), owner.SessionId, true))).Code);
        var observed = (NodeRoundSnapshot)manager.Execute(owner, new LobbyTournamentSetObserver(Revision(), player.SessionId, true));
        Assert.True(observed.Lobby.Members.Single(m => m.SessionId == player.SessionId).Observer);
        Assert.Equal("role", Assert.Throws<LobbyCommandException>(() => manager.Execute(player, new LobbySetReady(true, Revision()))).Code);
    }

    [Fact]
    public void NoVotesKeepNextMapAndObserversCannotVote()
    {
        var clock = new TestClock(); var h = new Harness(clock);
        var observer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Observer");
        h.Manager.Execute(observer, new LobbyJoin(h.Snapshot.LobbyId, h.Revision, true));
        var spec = h.Start(); h.Manager.MatchEnded(spec.MatchId, false);
        var ballot = h.Manager.RoundForSession(h.Owner.SessionId)!;
        Assert.Equal("role", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(observer, new LobbyVoteCast(h.Revision, ballot.BallotRevision, 1))).Code);
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
        clock.Advance();
        Assert.Equal("deadline", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, 1))).Code);
        Assert.Equal("MP4 HIGHGROUND", Assert.Single(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid())).Spec.Content.MapKey);
    }

    [Fact]
    public void VotesArePerSessionImmutableAndOwnerDepartureCannotStall()
    {
        var h = new Harness();
        // Use a separate two-player lobby for a frozen two-person electorate.
        var manager = new LobbyManager { ContentCatalog = h.Manager.ContentCatalog };
        var a = h.Owner; var b = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Player");
        var lobby = (LobbySnapshot)manager.Execute(a, new LobbyCreate("Pair", LobbyVisibility.Public, 2));
        manager.Execute(b, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        long Revision() => manager.ForSession(a.SessionId)!.Revision;
        manager.Execute(a, new LobbyConfigure(Revision(), "MP1 SANCTORUS", MatchMode.Battle));
        manager.Execute(a, new LobbySetReady(true, Revision()));
        manager.Execute(b, new LobbySetReady(true, Revision()));
        var first = manager.PrepareMatch(a.SessionId, Revision(), h.Manager.ContentCatalog!.Get("MP1 SANCTORUS"), new(Guid.NewGuid()), Guid.NewGuid());
        manager.MatchEnded(first.MatchId, false);
        var ballot = manager.RoundForSession(a.SessionId)!;
        manager.Execute(b, new LobbyVoteCast(Revision(), ballot.BallotRevision, 2));
        manager.Execute(b, new LobbyVoteCast(Revision(), ballot.BallotRevision, 2));
        Assert.Equal(0, manager.RoundForSession(a.SessionId)!.OwnVote);
        Assert.Equal(2, manager.RoundForSession(b.SessionId)!.OwnVote);
        Assert.Equal("already_voted", Assert.Throws<LobbyCommandException>(() => manager.Execute(b, new LobbyVoteCast(Revision(), ballot.BallotRevision, 1))).Code);
        Assert.Equal("invalid", Assert.Throws<LobbyCommandException>(() => manager.Execute(a, new LobbyVoteCast(Revision(), ballot.BallotRevision, 8))).Code);
        Assert.Equal("stale_ballot", Assert.Throws<LobbyCommandException>(() => manager.Execute(a, new LobbyVoteCast(Revision(), ballot.BallotRevision + 1, 2))).Code);
        manager.Disconnect(a.SessionId);
        Assert.Equal(2, manager.RoundForSession(b.SessionId)!.ResolvedOption!.Id);
        var next = Assert.Single(manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
        Assert.Single(next.Members); Assert.Equal(b.SessionId, next.Members[0].SessionId);
    }

    [Fact]
    public void TiesUseStableOptionOrderRegardlessOfArrivalOrder()
    {
        foreach (bool reverse in new[] { false, true })
        {
            var h = new Harness();
            var manager = new LobbyManager { ContentCatalog = h.Manager.ContentCatalog };
            var a = h.Owner; var b = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Player");
            var lobby = (LobbySnapshot)manager.Execute(a, new LobbyCreate("Pair", LobbyVisibility.Public, 2));
            manager.Execute(b, new LobbyJoin(lobby.LobbyId, lobby.Revision));
            long Revision() => manager.ForSession(a.SessionId)!.Revision;
            manager.Execute(a, new LobbyConfigure(Revision(), "MP1 SANCTORUS", MatchMode.Battle));
            manager.Execute(a, new LobbySetReady(true, Revision())); manager.Execute(b, new LobbySetReady(true, Revision()));
            var first = manager.PrepareMatch(a.SessionId, Revision(), h.Manager.ContentCatalog!.Get("MP1 SANCTORUS"), new(Guid.NewGuid()), Guid.NewGuid());
            manager.MatchEnded(first.MatchId, false);
            uint ballot = manager.RoundForSession(a.SessionId)!.BallotRevision;
            manager.Execute(reverse ? b : a, new LobbyVoteCast(Revision(), ballot, reverse ? (byte)2 : (byte)1));
            manager.Execute(reverse ? a : b, new LobbyVoteCast(Revision(), ballot, reverse ? (byte)1 : (byte)2));
            Assert.Equal(LobbyVoteChoice.Rematch, manager.RoundForSession(a.SessionId)!.ResolvedOption!.Choice);
        }
    }

    [Fact]
    public void CatalogRotationPreservesOrderFiltersModesAndCapsOptions()
    {
        var catalog = NodeContentCatalog.FromConfiguration(Enumerable.Range(0, 12).Select(i =>
            new NodeMapConfiguration("map" + i, "hash", "1", "build", 1,
                [i == 1 ? (int)MatchMode.TeamBattle : (int)MatchMode.Battle])));
        Assert.Equal(catalog.Maps.OrderBy(key => key, StringComparer.Ordinal), catalog.Maps);
        Assert.Equal("map0", catalog.Maps.First());
        Assert.Equal("map2", catalog.RoundMaps("map0", MatchMode.Battle)[0].MapKey);
        var manager = new LobbyManager { ContentCatalog = catalog };
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Maps", LobbyVisibility.Public));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbyConfigure(lobby.Revision, "map0", MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        var spec = manager.PrepareMatch(owner.SessionId, lobby.Revision, catalog.Get("map0"), new(Guid.NewGuid()), Guid.NewGuid());
        manager.MatchEnded(spec.MatchId, false);
        var options = manager.RoundForSession(owner.SessionId)!.Options;
        Assert.Equal(8, options.Length);
        Assert.DoesNotContain(options, o => o.MapKey == "map1");
        Assert.DoesNotContain(options, o => o.Choice == LobbyVoteChoice.Map && (o.MapKey == "map0" || o.MapKey == "map2"));
    }

    [Fact]
    public void CustomMapVoteCarriesExactIdentityAndWaitsForPlayerReadiness()
    {
        string baseHash = new('a', 64);
        string mapHash = new('b', 64);
        string matchHash = MapRequirement.ComputeMatchContentHash(baseHash,
            "community.rotation", "1.4.0", mapHash, "test-build", 1);
        var requirement = new MapRequirement("community.rotation", "1.4.0", mapHash,
            new string('c', 64), 319_488, matchHash);
        var catalog = new NodeContentCatalog(
        [
            new ContentIdentity("MP1 SANCTORUS", baseHash, "AMHE1", "test-build", 1),
            new ContentIdentity("CUSTOM ROTATION", baseHash, "AMHE1", "test-build", 1,
                requirement)
        ]);
        var manager = new LobbyManager { ContentCatalog = catalog };
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Custom rotation", LobbyVisibility.Public, 1));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "MP1 SANCTORUS", MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        MatchSpec first = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            catalog.Get("MP1 SANCTORUS"), new(Guid.NewGuid()), Guid.NewGuid());
        manager.MatchEnded(first.MatchId, false);

        NodeRoundSnapshot ballot = manager.RoundForSession(owner.SessionId)!;
        LobbyVoteEntry next = ballot.Options.Single(option => option.Choice == LobbyVoteChoice.NextMap);
        Assert.Equal(requirement, next.RequiredMap);
        NodeRoundSnapshot resolved = (NodeRoundSnapshot)manager.Execute(owner,
            new LobbyVoteCast(ballot.Lobby.Revision, ballot.BallotRevision, next.Id));

        Assert.Equal(LobbyPhase.Open, resolved.Lobby.Phase);
        Assert.Equal(requirement, resolved.Lobby.RequiredMap);
        Assert.False(resolved.Lobby.Members.Single().Ready);
        Assert.Empty(manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));

        manager.Execute(owner, new LobbySetReady(true, manager.ForSession(owner.SessionId)!.Revision));
        MatchSpec continuation = Assert.Single(manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.Equal("CUSTOM ROTATION", continuation.Content.MapKey);
        Assert.Equal(requirement, continuation.Content.RequiredMap);
    }

    [Fact]
    public void CustomMapContinuationRetainsIntentAcrossDisconnectedRetry()
    {
        var clock = new TestClock();
        string baseHash = new('a', 64);
        string mapHash = new('b', 64);
        string matchHash = MapRequirement.ComputeMatchContentHash(baseHash,
            "community.rotation", "1.4.0", mapHash, "test-build", 1);
        var requirement = new MapRequirement("community.rotation", "1.4.0", mapHash,
            new string('c', 64), 319_488, matchHash);
        var catalog = new NodeContentCatalog(
        [
            new ContentIdentity("MP1 SANCTORUS", baseHash, "AMHE1", "test-build", 1),
            new ContentIdentity("CUSTOM ROTATION", baseHash, "AMHE1", "test-build", 1,
                requirement)
        ]);
        var manager = new LobbyManager { RoundClock = clock, ContentCatalog = catalog };
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Custom retry", LobbyVisibility.Public, 1));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "MP1 SANCTORUS", MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        MatchSpec first = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            catalog.Get("MP1 SANCTORUS"), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.True(manager.MatchEnded(first.MatchId, false));

        NodeRoundSnapshot ballot = manager.RoundForSession(owner.SessionId)!;
        LobbyVoteEntry next = ballot.Options.Single(option =>
            option.Choice == LobbyVoteChoice.NextMap);
        NodeRoundSnapshot resolved = (NodeRoundSnapshot)manager.Execute(owner,
            new LobbyVoteCast(ballot.Lobby.Revision, ballot.BallotRevision, next.Id));
        Assert.Equal(LobbyPhase.Open, resolved.Lobby.Phase);
        Assert.Equal(requirement, resolved.Lobby.RequiredMap);

        // Make the approved player ready, then model a reconnect-grace
        // disconnect. PrepareMatchCore must report the operational boundary
        // without discarding the approved intent or reopening the lobby.
        manager.SetSessionResumeDeadline(owner.SessionId,
            clock.GetUtcNow().AddMinutes(1));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, manager.ForSession(owner.SessionId)!.Revision));
        Assert.Empty(manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
        NodeRoundSnapshot pending = manager.RoundForSession(owner.SessionId)!;
        Assert.Equal(LobbyPhase.Open, pending.Lobby.Phase);
        Assert.Equal(requirement, pending.Lobby.RequiredMap);
        Assert.True(pending.Lobby.Members.Single().Ready);

        manager.SetSessionResumeDeadline(owner.SessionId, null);
        MatchSpec continuation = Assert.Single(manager.PrepareContinuations(
            new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.Equal("CUSTOM ROTATION", continuation.Content.MapKey);
        Assert.True(continuation.LifecycleEpoch.Value > first.LifecycleEpoch.Value);
    }

    [Theory]
    [InlineData(4)] [InlineData(31)]
    public void InvalidVoteWindowFailsConstruction(int seconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new LobbyManager(postMatchVoteSeconds: seconds));

    [Fact]
    public void RoundCodecRejectsInvalidResolvedChoiceAndMissingDeadline()
    {
        var h = new Harness(); var first = h.Start(); h.Manager.MatchEnded(first.MatchId, false);
        var round = h.Manager.RoundForSession(h.Owner.SessionId)!;
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("lobby.round", 1, null, round with { VoteDeadline = null }));
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("lobby.round", 1, null,
            round with { ResolvedOption = new(9, LobbyVoteChoice.Map, "bad", MatchMode.Battle, 0) }));
    }

    [Fact]
    public void InterruptedTournamentStillRequiresNewRoundAndOperatorResume()
    {
        var h = new Harness(); var tournament = Guid.NewGuid(); var round = Guid.NewGuid();
        h.Round(new LobbyTournamentIdentity(h.Revision, tournament, round));
        h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.Resume));
        var match = h.Start(); h.Manager.MatchEnded(match.MatchId, true);
        Assert.Equal(LobbyPhase.Open, h.Snapshot.Phase);
        Assert.True(h.Manager.RoundForSession(h.Owner.SessionId)!.Paused);
        h.Ready();
        Assert.Equal("paused", Assert.Throws<LobbyCommandException>(() => h.Prepare()).Code);
        h.Round(new LobbyTournamentControl(h.Revision, TournamentControl.Resume));
        Assert.Equal("round_identity", Assert.Throws<LobbyCommandException>(() => h.Prepare()).Code);
        Assert.Empty(h.Manager.RoundForSession(h.Owner.SessionId)!.Options);
    }

    [Fact]
    public void ExpiredResumeGraceIsPrunedAtContinuationFreezeWithoutReaper()
    {
        var clock = new TestClock(); var h = new Harness(clock);
        var manager = new LobbyManager(clock: clock) { ContentCatalog = h.Manager.ContentCatalog };
        var a = h.Owner; var b = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Player");
        var lobby = (LobbySnapshot)manager.Execute(a, new LobbyCreate("Pair", LobbyVisibility.Public, 2));
        manager.Execute(b, new LobbyJoin(lobby.LobbyId, lobby.Revision));
        long Revision() => manager.ForSession(a.SessionId)!.Revision;
        manager.Execute(a, new LobbyConfigure(Revision(), "MP1 SANCTORUS", MatchMode.Battle));
        manager.Execute(a, new LobbySetReady(true, Revision())); manager.Execute(b, new LobbySetReady(true, Revision()));
        var first = manager.PrepareMatch(a.SessionId, Revision(), h.Manager.ContentCatalog!.Get("MP1 SANCTORUS"), new(Guid.NewGuid()), Guid.NewGuid());
        manager.MatchEnded(first.MatchId, false);
        var ballot = manager.RoundForSession(a.SessionId)!;
        manager.Execute(b, new LobbyVoteCast(Revision(), ballot.BallotRevision, 1));
        manager.SetSessionResumeDeadline(a.SessionId, clock.GetUtcNow().AddSeconds(16));
        clock.Advance(); // Exact expiry; no NodeSessionReaper call.
        var next = Assert.Single(manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
        Assert.Single(next.Members); Assert.Equal(b.SessionId, next.Members[0].SessionId);
        Assert.Single(next.Spec.Roster); Assert.Null(manager.ForSession(a.SessionId));
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(16);
    }

    private static void AssertOwnership(LobbyManager manager,
        Guid? expectedMatch = null, Guid? expectedLobby = null)
    {
        var ownership = manager.MatchOwnershipSnapshot();
        Assert.Equal(ownership.LobbyScan.Count, ownership.Index.Count);
        foreach (var pair in ownership.LobbyScan)
            Assert.True(ownership.Index.TryGetValue(pair.Key, out Guid lobbyId)
                && lobbyId == pair.Value);
        foreach (var pair in ownership.Index)
            Assert.True(ownership.LobbyScan.TryGetValue(pair.Key, out Guid lobbyId)
                && lobbyId == pair.Value);
        if (expectedMatch is null) return;
        Assert.NotNull(expectedLobby);
        Assert.True(ownership.Index.TryGetValue(expectedMatch.Value, out Guid actual));
        Assert.Equal(expectedLobby.Value, actual);
    }
    private sealed class Harness
    {
        public LobbyManager Manager { get; }
        public LobbyIdentity Owner { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        public LobbySnapshot Snapshot => Manager.ForSession(Owner.SessionId)!;
        public long Revision => Snapshot.Revision;
        public Harness(TimeProvider? clock = null)
        {
            Manager = new LobbyManager { RoundClock = clock ?? TimeProvider.System,
                ContentCatalog = new NodeContentCatalog(new[] { "MP1 SANCTORUS", "MP4 HIGHGROUND", "MP2 HARVESTER" }
                    .Select(key => new ContentIdentity(key, "test-hash", "AMHE1", "test-build", 1))) };
            Manager.Execute(Owner, new LobbyCreate("Arena", LobbyVisibility.Public, 1, 1));
            Manager.Execute(Owner, new LobbyConfigure(Revision, "MP1 SANCTORUS", MatchMode.Battle));
        }
        public NodeRoundSnapshot Round(NodeCommand command) => (NodeRoundSnapshot)Manager.Execute(Owner, command);
        public void Ready() => Manager.Execute(Owner, new LobbySetReady(true, Revision));
        public MatchSpec Prepare() => Manager.PrepareMatch(Owner.SessionId, Revision,
            new(Snapshot.MapKey, "test-hash", "AMHE1", "test-build", 1), new(Guid.NewGuid()), Guid.NewGuid());
        public MatchSpec Start() { Ready(); return Prepare(); }
    }
}
