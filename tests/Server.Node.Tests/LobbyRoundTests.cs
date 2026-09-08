using System.Collections.Immutable;
using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Shared;
using MphRead;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

public sealed class LobbyRoundTests
{
    [Fact]
    public void IntermissionVotesUseFrozenOptionsIdentityAndExistingNextMatchLifecycle()
    {
        var h = new Harness();
        MatchSpec first = h.Start(); h.Manager.MatchEnded(first.MatchId, false);
        var ballot = h.Round(new LobbyVoteOpen(h.Revision, ImmutableArray.Create(new LobbyMapChoice("MP4 HIGHGROUND", MatchMode.Battle))));
        uint frozen = ballot.BallotRevision;
        var voted = h.Round(new LobbyVoteCast(h.Revision, frozen, 2));
        Assert.Equal(frozen, voted.BallotRevision);
        Assert.Equal("already_voted", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteCast(h.Revision, frozen, 1))).Code);
        Assert.Equal("stale_ballot", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteCast(h.Revision, frozen + 1, 2))).Code);
        var resolved = h.Round(new LobbyVoteResolve(h.Revision, frozen));
        Assert.Equal(LobbyPhase.Open, resolved.Lobby.Phase);
        Assert.Equal("MP4 HIGHGROUND", resolved.Lobby.MapKey);
        Assert.False(resolved.Lobby.Members[0].Ready);
        MatchSpec second = h.Start();
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.Equal("MP4 HIGHGROUND", second.Rules.RoomKey);
        Assert.Equal(LobbyPhase.StartingMatch, h.Snapshot.Phase);
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
        var ballot = h.Round(new LobbyVoteOpen(h.Revision, ImmutableArray.Create(new LobbyMapChoice("MP4 HIGHGROUND", MatchMode.Battle))));
        Assert.Equal("role", Assert.Throws<LobbyCommandException>(() => h.Manager.Execute(observer, new LobbyVoteCast(h.Revision, ballot.BallotRevision, 1))).Code);
        Assert.Equal("deadline", Assert.Throws<LobbyCommandException>(() => h.Round(new LobbyVoteResolve(h.Revision, ballot.BallotRevision))).Code);
        clock.Advance();
        Assert.Equal("MP4 HIGHGROUND", h.Round(new LobbyVoteResolve(h.Revision, ballot.BallotRevision)).Lobby.MapKey);
    }

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance() => _now = _now.AddSeconds(6);
    }
    private sealed class Harness
    {
        public LobbyManager Manager { get; }
        public LobbyIdentity Owner { get; } = new(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        public LobbySnapshot Snapshot => Manager.ForSession(Owner.SessionId)!;
        public long Revision => Snapshot.Revision;
        public Harness(TimeProvider? clock = null)
        {
            Manager = new LobbyManager { RoundClock = clock ?? TimeProvider.System };
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
