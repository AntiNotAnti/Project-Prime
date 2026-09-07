using System;
using System.Linq;
using MphRead.Identity;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class LobbyRuntimeTests
{
    [Fact]
    public void DomainKeepsLobbyAndMatchStateSeparateAndBounded()
    {
        LobbyRuntime lobby = Create();
        uint revision = lobby.Revision;
        for (byte i = 0; i < LobbyRuntime.MaximumPlayers; i++)
        {
            Assert.True(lobby.TryAdd(Player((ulong)i + 1, "P" + i, team: i, host: i == 0)));
        }
        Assert.Equal(revision + LobbyRuntime.MaximumPlayers, lobby.Revision);
        Assert.False(lobby.TryAdd(Player(99, "FULL", team: 0)));
        for (byte i = 0; i < LobbyRuntime.MaximumObservers; i++)
        {
            Assert.True(lobby.TryAdd(Player((ulong)i + 100, "O" + i, observer: true)));
        }
        Assert.False(lobby.TryAdd(Player(999, "FULL", observer: true)));
        Assert.Equal(8, lobby.Players.Count);
        Assert.Equal(16, lobby.Observers.Count);
        Assert.Equal(1ul, lobby.HostConnectionId);
        Assert.False(lobby.TryAdd(Player(1000, "OTHER HOST", team: 0, host: true)));
    }

    [Fact]
    public void InvalidMembersAndNoOpMutationsDoNotAdvanceRevision()
    {
        LobbyRuntime lobby = Create();
        uint revision = lobby.Revision;
        Assert.False(lobby.TryAdd(Player(0, "ZERO", team: 0)));
        Assert.False(lobby.TryAdd(Player(1, "", team: 0)));
        Assert.False(lobby.TryAdd(Player(1, new string('X', 17), team: 0)));
        Assert.False(lobby.TryAdd(Player(1, "BAD\nNAME", team: 0)));
        Assert.False(lobby.TryAdd(Player(1, "BAD TEAM", team: 8)));
        Assert.False(lobby.TryAdd(Player(1, "BAD HUNTER", team: 0) with { Hunter = (Hunter)255 }));
        Assert.False(lobby.TryAdd(Player(1, "EMPTY ID", team: 0) with { PlayerId = default(PlayerId) }));
        Assert.False(lobby.TryAdd(Player(1, "BOT ID", team: 0) with { Bot = true, PlayerId = new PlayerId(Guid.NewGuid()) }));
        Assert.False(lobby.TryAdd(Player(1, "BAD OBSERVER", observer: true) with { Ready = true }));
        Assert.False(lobby.TryAdd(Player(1, "BOT LOADING", team: 0) with { Bot = true, Loading = true }));
        Assert.False(lobby.TryAdd(Player(1, "BOT ADMIN", team: 0) with { Bot = true, Admin = true }));
        Assert.False(lobby.TryAdd(Player(1, "WATCH GRACE", observer: true) with { DisconnectedGrace = true }));
        Assert.False(lobby.TryAdd(Player(1, "READY GRACE", team: 0) with { Ready = true, DisconnectedGrace = true }));
        Assert.False(lobby.TryAdd(Player(1, "LOADING GRACE", team: 0) with { Loading = true, DisconnectedGrace = true }));
        Assert.Equal(revision, lobby.Revision);

        LobbyPlayer guest = Player(1, "GUEST", team: 0);
        Assert.True(lobby.TryAdd(guest));
        Assert.Null(lobby.Players[0].PlayerId);
        Assert.False(lobby.TryUpdate(guest));
        Assert.Equal(revision + 1, lobby.Revision);

        LobbyPlayer account = guest with { PlayerId = new PlayerId(Guid.NewGuid()), DisplayName = "ACCOUNT" };
        Assert.True(lobby.TryUpdate(account));
        Assert.True(lobby.Players[0].Authenticated);

        Assert.True(lobby.TryUpdate(account with { DisconnectedGrace = true }));
        Assert.Equal(LobbyPermissions.None, lobby.PermissionsFor(account.ConnectionId));

        Assert.True(lobby.TryAdd(Player(2, "WATCH LOAD", observer: true) with { Loading = true }));
        Assert.True(lobby.Observers.Single().Loading);
    }

    [Fact]
    public void BotsAreReadyAndObserversNeverCountTowardStart()
    {
        var policy = new LobbyPolicy(LobbyPolicyKind.PersistentLobby, true, 2, true);
        LobbyRuntime lobby = Create(policy);
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true)));
        Assert.True(lobby.TryAdd(Player(2, "BOT", team: 1) with { Bot = true }));
        Assert.True(lobby.TryAdd(Player(3, "WATCH", observer: true)));
        Assert.True(lobby.Players[1].Ready);
        Assert.False(lobby.AllPlayersReady);
        Assert.False(lobby.CanStart(1));
        Assert.True(lobby.CanStart(1, force: true));
        Assert.False(lobby.CanStart(3, force: true));
        Assert.True(lobby.TryUpdate(lobby.Players[0] with { Ready = true }));
        Assert.True(lobby.AllPlayersReady);
        Assert.True(lobby.TryBeginStarting(1));
        Assert.Equal(LobbyPhase.Starting, lobby.Phase);
        Assert.True(lobby.TryLock());
        Assert.Equal(LobbyPhase.Locked, lobby.Phase);
    }

    [Fact]
    public void DraftFreezesValidatedImmutableMatchRulesAndLocksEdits()
    {
        LobbyRuntime lobby = Create();
        MatchRules original = lobby.Draft.Current;
        var selection = new LobbySelection("UNIT2 ALINOS GATE", MatchMode.TeamBattle);
        Assert.True(lobby.TrySetSelection(selection));
        MatchRules frozen = lobby.Draft.Freeze();
        Assert.Equal(selection.MapKey, frozen.RoomKey);
        Assert.Equal(selection.Mode, frozen.Mode);
        Assert.Equal(original.ScoreGoal, frozen.ScoreGoal);
        Assert.False(lobby.TrySetSelection(selection));

        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true, ready: true)));
        Assert.True(lobby.TryBeginStarting(1));
        Assert.False(lobby.TrySetSelection(new("MP1 SANCTORUS", MatchMode.Battle)));
        Assert.True(lobby.TryReopen());
        Assert.True(lobby.TrySetSelection(new("MP1 SANCTORUS", MatchMode.Battle)));
    }

    [Fact]
    public void PermissionsFollowRoleAndPhase()
    {
        LobbyRuntime lobby = Create();
        Assert.True(lobby.TryAdd(Player(1, "PLAYER", team: 0)));
        Assert.True(lobby.TryAdd(Player(2, "HOST", team: 1, host: true, ready: true)));
        Assert.True(lobby.TryAdd(Player(3, "ADMIN", team: 2) with { Admin = true }));
        Assert.True(lobby.TryAdd(Player(4, "WATCH", observer: true)));
        Assert.Equal(LobbyPermissions.Player, lobby.PermissionsFor(1));
        Assert.True(lobby.PermissionsFor(2).HasFlag(LobbyPermissions.StartMatch));
        Assert.True(lobby.PermissionsFor(3).HasFlag(LobbyPermissions.Moderate));
        Assert.Equal(LobbyPermissions.None, lobby.PermissionsFor(4));
        Assert.Equal(LobbyPermissions.None, lobby.PermissionsFor(999));

        Assert.True(lobby.TryBeginStarting(2, force: true));
        Assert.False(lobby.PermissionsFor(2).HasFlag(LobbyPermissions.SetMap));
        Assert.True(lobby.PermissionsFor(2).HasFlag(LobbyPermissions.ReturnToLobby));
    }

    [Fact]
    public void SummaryVoteAndRemovalAdvanceOnlyOnChangeAndMigratesHost()
    {
        LobbyRuntime lobby = Create();
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true)));
        Assert.True(lobby.TryAdd(Player(2, "NEXT", team: 1)));
        uint revision = lobby.Revision;
        Assert.False(lobby.TrySetCompletedMatch(0));
        Assert.True(lobby.TrySetCompletedMatch(77));
        Assert.False(lobby.TrySetCompletedMatch(77));
        Assert.True(lobby.TrySetActiveVote(9));
        Assert.False(lobby.TrySetActiveVote(9));
        Assert.True(lobby.TrySetActiveVote(0));
        Assert.True(lobby.TryRemove(1));
        Assert.False(lobby.TryRemove(1));
        Assert.Equal(revision + 4, lobby.Revision);
        Assert.Equal(2ul, lobby.HostConnectionId);
        Assert.True(lobby.Players[0].Host);
    }

    [Fact]
    public void HostMigrationPrefersOldestAuthenticatedHumanThenOldestGuest()
    {
        LobbyRuntime lobby = Create();
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true)));
        Assert.True(lobby.TryAdd(Player(2, "FIRST GUEST", team: 1)));
        Assert.True(lobby.TryAdd(Player(3, "BOT", team: 2) with { Bot = true }));
        Assert.True(lobby.TryAdd(Player(4, "FIRST ACCOUNT", team: 3)
            with { PlayerId = new PlayerId(Guid.Parse("11111111-1111-1111-1111-111111111111")) }));
        Assert.True(lobby.TryAdd(Player(5, "SECOND ACCOUNT", team: 4)
            with { PlayerId = new PlayerId(Guid.Parse("22222222-2222-2222-2222-222222222222")) }));
        Assert.True(lobby.TryAdd(Player(6, "WATCH", observer: true)
            with { PlayerId = new PlayerId(Guid.Parse("33333333-3333-3333-3333-333333333333")) }));

        Assert.True(lobby.TryRemove(1));
        Assert.Equal(4ul, lobby.HostConnectionId);
        Assert.True(lobby.TryFind(4, out LobbyPlayer firstAccount));
        Assert.True(firstAccount.Host);

        Assert.True(lobby.TryRemove(4));
        Assert.Equal(5ul, lobby.HostConnectionId);
        Assert.True(lobby.TryRemove(5));
        Assert.Equal(2ul, lobby.HostConnectionId);
        Assert.True(lobby.TryFind(2, out LobbyPlayer firstGuest));
        Assert.True(firstGuest.Host);
        Assert.True(lobby.TryFind(3, out LobbyPlayer bot));
        Assert.False(bot.Host);
        Assert.True(lobby.TryFind(6, out LobbyPlayer observer));
        Assert.False(observer.Host);
    }

    [Fact]
    public void ConnectionStatusCanChangeWhileLockedAndGraceMigratesHost()
    {
        LobbyRuntime lobby = Create();
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true, ready: true)));
        Assert.True(lobby.TryAdd(Player(2, "NEXT", team: 1, ready: true)));
        Assert.True(lobby.TryAdd(Player(3, "WATCH", observer: true)));
        Assert.True(lobby.TryBeginStarting(1));
        Assert.True(lobby.TryLock());

        uint revision = lobby.Revision;
        Assert.True(lobby.TrySetConnectionStatus(1, loading: true, disconnectedGrace: false));
        Assert.True(lobby.TryFind(1, out LobbyPlayer loadingHost));
        Assert.True(loadingHost.Loading);
        Assert.True(loadingHost.Ready);
        Assert.True(loadingHost.Host);

        Assert.True(lobby.TrySetConnectionStatus(1, loading: false, disconnectedGrace: true));
        Assert.True(lobby.TryFind(1, out LobbyPlayer graceHost));
        Assert.True(graceHost.DisconnectedGrace);
        Assert.False(graceHost.Loading);
        Assert.False(graceHost.Ready);
        Assert.False(graceHost.Host);
        Assert.Equal(2ul, lobby.HostConnectionId);
        Assert.True(lobby.TryFind(2, out LobbyPlayer successor));
        Assert.True(successor.Host);
        Assert.Equal(revision + 2, lobby.Revision);

        Assert.True(lobby.TrySetConnectionStatus(3, loading: true, disconnectedGrace: false));
        Assert.False(lobby.TrySetConnectionStatus(3, loading: false, disconnectedGrace: true));
        Assert.False(lobby.TrySetConnectionStatus(1, loading: true, disconnectedGrace: true));
        Assert.False(lobby.TrySetConnectionStatus(99, loading: false, disconnectedGrace: false));
    }

    [Fact]
    public void DisconnectedGraceRowsDoNotSatisfyStartQuorumOrTeamPresence()
    {
        var policy = new LobbyPolicy(LobbyPolicyKind.PersistentLobby, readyRequired: false,
            minimumPlayers: 2, hostMayForceStart: true);
        LobbyRuntime lobby = Create(policy);
        Assert.True(lobby.TrySetSelection(new LobbySelection("MP1 SANCTORUS", MatchMode.TeamBattle)));
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true)));
        Assert.True(lobby.TryAdd(Player(2, "GRACE", team: 1)));
        Assert.True(lobby.TrySetConnectionStatus(2, loading: false, disconnectedGrace: true));

        Assert.Equal(1, lobby.ConnectedPlayerCount);
        Assert.False(lobby.CanStart(1, force: true));
        Assert.Equal(LobbyStartBlock.TooFewPlayers,
            LobbyStartPolicy.Evaluate(lobby, 1, force: true, tournamentStartAllowed: true).Block);
    }

    [Fact]
    public void HostMigrationSkipsDisconnectedGraceRows()
    {
        LobbyRuntime lobby = Create();
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true)));
        Assert.True(lobby.TryAdd(Player(2, "GRACE", team: 1)));
        Assert.True(lobby.TryAdd(Player(3, "CONNECTED", team: 2)));
        Assert.True(lobby.TrySetConnectionStatus(2, loading: false, disconnectedGrace: true));

        Assert.True(lobby.TryRemove(1));
        Assert.Equal(3ul, lobby.HostConnectionId);
        Assert.True(lobby.TryFind(2, out LobbyPlayer grace));
        Assert.False(grace.Host);
    }

    [Theory]
    [InlineData(LobbyPolicyKind.NoLobby)]
    [InlineData(LobbyPolicyKind.IntermissionLobby)]
    [InlineData(LobbyPolicyKind.PersistentLobby)]
    public void PolicyKindsAreExplicitAndBounded(LobbyPolicyKind kind)
    {
        var policy = new LobbyPolicy(kind, true, 8, false);
        Assert.Equal(kind, policy.Kind);
        Assert.Throws<ArgumentOutOfRangeException>(() => new LobbyPolicy(kind, true, 0, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LobbyPolicy(kind, true, 9, false));
    }

    [Fact]
    public void RevisionWrapSkipsTheReservedZeroValue()
    {
        var lobby = new LobbyRuntime(42, LobbyPolicy.PrivateHosted,
            MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS"), uint.MaxValue);
        Assert.True(lobby.TryAdd(Player(1, "HOST", team: 0, host: true)));
        Assert.Equal(1u, lobby.Revision);
    }

    private static LobbyRuntime Create(LobbyPolicy? policy = null)
        => new(42, policy ?? LobbyPolicy.PrivateHosted,
            MatchRules.CreateDefault(MatchMode.Battle, "MP1 SANCTORUS"));

    private static LobbyPlayer Player(ulong connectionId, string name, byte team = byte.MaxValue,
        bool observer = false, bool host = false, bool ready = false)
        => new(null, connectionId, name, Hunter.Samus, observer ? byte.MaxValue : team,
            ready, observer, false, host, false, 0, 0);
}
