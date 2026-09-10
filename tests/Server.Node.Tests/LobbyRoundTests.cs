using System.Collections.Immutable;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Shared;
using MphRead;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class LobbyRoundTests
{
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
        Assert.Equal(4, ballot.Options.Length);
        Assert.Equal("unsupported", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteOpen(h.Revision, []))).Code);
        Assert.Equal("unsupported", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteResolve(h.Revision, ballot.BallotRevision))).Code);
        var resolved = h.Round(new LobbyVoteCast(h.Revision, ballot.BallotRevision, option));
        Assert.Equal(option, resolved.ResolvedOption!.Id);
        var second = Assert.Single(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid())).Spec;
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.Equal(map, second.Content.MapKey);
        Assert.Equal(LobbyPhase.StartingMatch, h.Snapshot.Phase);
        Assert.False(h.Snapshot.Members[0].Ready);
        Assert.Empty(h.Manager.PrepareContinuations(new(Guid.NewGuid()), Guid.NewGuid()));
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
