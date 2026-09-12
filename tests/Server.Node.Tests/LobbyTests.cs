using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Node.Sessions;
using ProjectPrime.Server.Shared;
using MphRead;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class LobbyTests
{
    private static LobbyIdentity Person(string name) => new(Guid.NewGuid(), Guid.NewGuid(), name);
    [Fact]
    public void ConfiguredBotsReserveCapacityAndFreezeWithoutInventedAccounts()
    {
        var manager = new LobbyManager(); var owner = Person("Owner"); var other = Person("Other");
        var lobby = (LobbySnapshot)manager.Execute(owner, new LobbyCreate("Bots", LobbyVisibility.Public, 3, 0));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, 2, 3, 11));
        Assert.Throws<LobbyCommandException>(() => manager.Execute(other, new LobbyJoin(lobby.LobbyId, lobby.Revision)));
        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        var spec = manager.PrepareMatch(owner.SessionId, lobby.Revision, new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(TimeSpan.FromSeconds(3), spec.Rules.TimeLimit);
        Assert.Equal(11, spec.Rules.ScoreGoal);
        RosterSeat[] bots = spec.Roster.Where(s => s.Role == SeatRole.Bot).ToArray();
        Assert.Equal(2, bots.Length);
        Assert.Equal(2, bots.Select(bot => bot.Hunter).Distinct().Count());
        Assert.All(bots, bot =>
        {
            Assert.InRange(bot.Hunter, Hunter.Samus, Hunter.Weavel);
            Assert.Null(bot.PlayerId);
            Assert.Null(bot.GuestSessionId);
        });
        Assert.Equal(BotFillPolicy.FillVacancies, spec.BotFillPolicy);
        Assert.Equal(3, spec.Roster.Select(s => s.SeatId).Distinct().Count());
    }

    [Fact]
    public void ApplyingMatchSettingsPersistsEverySupportedFieldAndResetsReadiness()
    {
        var manager = new LobbyManager(); var owner = Person("Owner");
        var lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Configured", LobbyVisibility.Public, 4, 0));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, lobby.Revision));

        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "MP1 SANCTORUS", MatchMode.Survival, 2, 600, 4));

        Assert.Equal("MP1 SANCTORUS", lobby.MapKey);
        Assert.Equal(MatchMode.Survival, lobby.Mode);
        Assert.Equal(2, lobby.BotCount);
        Assert.Equal(600, lobby.TimeLimitSeconds);
        Assert.Equal(4, lobby.PointGoal);
        Assert.All(lobby.Members, member => Assert.False(member.Ready));

        lobby = (LobbySnapshot)manager.Execute(owner, new LobbySetReady(true, lobby.Revision));
        var spec = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            new("MP1 SANCTORUS", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(4, spec.Rules.StartingLives);
        Assert.Equal(0, spec.Rules.ScoreGoal);
        Assert.Equal(TimeSpan.FromMinutes(10), spec.Rules.TimeLimit);
    }

    [Fact]
    public void DisconnectedPlayerCannotBeFrozenIntoHostOnlyMatch()
    {
        var manager = new LobbyManager();
        var owner = Person("Owner");
        var guest = Person("Guest");
        var lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Connected players", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(guest,
            new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(guest,
            new LobbySetReady(true, lobby.Revision));

        manager.SetSessionResumeDeadline(guest.SessionId,
            DateTimeOffset.UtcNow + NodeSessionManager.DisconnectGrace);

        LobbySnapshot disconnected = manager.ForSession(owner.SessionId)!;
        Assert.False(disconnected.Members.Single(member => member.SessionId == guest.SessionId).Ready);
        LobbyCommandException error = Assert.Throws<LobbyCommandException>(() =>
            manager.PrepareMatch(owner.SessionId, disconnected.Revision,
                new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid()));
        Assert.Equal("player_disconnected", error.Code);
        Assert.Equal(LobbyPhase.Open, manager.ForSession(owner.SessionId)!.Phase);
        Assert.Null(manager.ForSession(owner.SessionId)!.CurrentMatchId);

        manager.SetSessionResumeDeadline(guest.SessionId, null);
        lobby = (LobbySnapshot)manager.Execute(guest,
            new LobbySetReady(true, disconnected.Revision));
        MatchSpec spec = manager.PrepareMatch(owner.SessionId, lobby.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(2, spec.Roster.Count(seat => seat.Role == SeatRole.Player));
    }

    [Fact]
    public void InvalidPointLimitsFailWithoutMutatingLobby()
    {
        var manager = new LobbyManager(); var owner = Person("Owner");
        var lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Configured", LobbyVisibility.Public, 4, 0));

        Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, PointGoal: 0)));
        Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle, PointGoal: 65536)));

        LobbySnapshot unchanged = manager.ForSession(owner.SessionId)!;
        Assert.Equal(lobby.Revision, unchanged.Revision);
        Assert.Equal("", unchanged.MapKey);
        Assert.Null(unchanged.PointGoal);
    }
    [Fact]
    public void MembershipRevisionsAndOwnerTransferRemainAuthoritative()
    {
        var manager = new LobbyManager(); var a = Person("A"); var b = Person("B");
        var original = (LobbySnapshot)manager.Execute(a, new LobbyCreate("Arena", LobbyVisibility.Public));
        var joined = (LobbySnapshot)manager.Execute(b, new LobbyJoin(original.LobbyId, original.Revision));
        Assert.Equal(original.Revision + 1, joined.Revision);
        Assert.Single(original.Members); // Published snapshots cannot change behind consumers.
        var ready = (LobbySnapshot)manager.Execute(b, new LobbySetReady(true, joined.Revision));
        var selected = (LobbySnapshot)manager.Execute(b, new LobbySelectHunter(Hunter.Kanden, ready.Revision));
        Assert.False(selected.Members.Single(m => m.SessionId == b.SessionId).Ready);
        Assert.Equal(ready.Revision + 1, selected.Revision);
        manager.Disconnect(a.SessionId);
        var after = (LobbySnapshot)manager.Execute(b, new LobbySetReady(false, selected.Revision + 1));
        Assert.Equal(b.SessionId, after.OwnerSessionId);
        Assert.Equal(selected.Revision + 1, after.Revision);
        manager.Disconnect(b.SessionId); Assert.Equal(0, manager.Count);
    }
    [Fact]
    public void CapacityRoleAndOneLobbyChecksDoNotPartiallyMutate()
    {
        var manager = new LobbyManager(); var a = Person("A"); var b = Person("B");
        var lobby = (LobbySnapshot)manager.Execute(a, new LobbyCreate("Arena", LobbyVisibility.Public, 1, 1));
        Assert.Throws<LobbyCommandException>(() => manager.Execute(b, new LobbyJoin(lobby.LobbyId, lobby.Revision)));
        var observer = (LobbySnapshot)manager.Execute(b, new LobbyJoin(lobby.LobbyId, lobby.Revision, true));
        Assert.Equal(2, observer.Revision);
        Assert.Throws<LobbyCommandException>(() => manager.Execute(b, new LobbySetReady(true, observer.Revision)));
        Assert.Throws<LobbyCommandException>(() => manager.Execute(b, new LobbyCreate("Second", LobbyVisibility.Public)));
        Assert.Equal(1, manager.Count);
    }
    [Fact]
    public void ChatAndPublicBrowserAreBoundedAndUnlistedIsHidden()
    {
        var manager = new LobbyManager(); var a = Person("A"); var b = Person("B");
        manager.Execute(a, new LobbyCreate("Private", LobbyVisibility.Unlisted));
        manager.Execute(b, new LobbyCreate("Public", LobbyVisibility.Public));
        LobbySnapshot latest = null!;
        for (int i = 0; i < 40; i++) latest = (LobbySnapshot)manager.Execute(a, new LobbyChat("Message " + i, i + 1));
        Assert.Equal(16, latest.Chat.Length); Assert.Equal("Message 24", latest.Chat[0].Text);
        Assert.Throws<LobbyCommandException>(() => manager.Execute(a, new LobbyChat(new string('é', 129), latest.Revision)));
        Assert.Single(((LobbyListSnapshot)manager.Execute(a, new LobbyList())).Lobbies);
        Assert.Throws<LobbyCommandException>(() => manager.Execute(a, new LobbyList(0, 17)));
        Assert.True(NodeControlCodec.Write("lobby.snapshot", 1, null, latest).Length < NodeControlCodec.MaximumFrameBytes);
    }

    [Fact]
    public void PublicBrowserHidesReconnectOnlyLobbyWithoutDestroyingIt()
    {
        var manager = new LobbyManager();
        LobbyIdentity owner = Person("Owner");
        LobbyIdentity browser = Person("Browser");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Reconnect", LobbyVisibility.Public));
        Assert.Single(((LobbyListSnapshot)manager.Execute(browser, new LobbyList())).Lobbies);

        manager.SetSessionResumeDeadline(owner.SessionId,
            DateTimeOffset.UtcNow + NodeSessionManager.DisconnectGrace);

        Assert.Empty(((LobbyListSnapshot)manager.Execute(browser, new LobbyList())).Lobbies);
        Assert.Equal(lobby.LobbyId, manager.ForSession(owner.SessionId)!.LobbyId);

        manager.SetSessionResumeDeadline(owner.SessionId, null);
        Assert.Single(((LobbyListSnapshot)manager.Execute(browser, new LobbyList())).Lobbies);
    }

    [Fact]
    public void PublicBrowserKeepsLobbyVisibleWhileAnotherMemberIsConnected()
    {
        var manager = new LobbyManager();
        LobbyIdentity owner = Person("Owner");
        LobbyIdentity member = Person("Member");
        LobbyIdentity browser = Person("Browser");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Occupied", LobbyVisibility.Public));
        manager.Execute(member, new LobbyJoin(lobby.LobbyId, lobby.Revision));

        manager.SetSessionResumeDeadline(owner.SessionId,
            DateTimeOffset.UtcNow + NodeSessionManager.DisconnectGrace);

        LobbyListEntry visible = Assert.Single(
            ((LobbyListSnapshot)manager.Execute(browser, new LobbyList())).Lobbies);
        Assert.Equal(lobby.LobbyId, visible.LobbyId);
    }

    [Fact]
    public void QuickPlayScoresAtomicallyAndHonorsPreferences()
    {
        var manager = new LobbyManager();
        LobbyIdentity sparseOwner = Person("Sparse");
        LobbySnapshot sparse = (LobbySnapshot)manager.Execute(sparseOwner,
            new LobbyCreate("Sparse", LobbyVisibility.Public, 4, 0));
        LobbyIdentity populatedOwner = Person("Populated");
        LobbySnapshot populated = (LobbySnapshot)manager.Execute(populatedOwner,
            new LobbyCreate("Populated", LobbyVisibility.Public, 4, 0));
        LobbyIdentity second = Person("Second");
        populated = (LobbySnapshot)manager.Execute(second, new LobbyJoin(populated.LobbyId, populated.Revision));
        populated = (LobbySnapshot)manager.Execute(populatedOwner,
            new LobbyConfigure(populated.Revision, "unit", MatchMode.Survival, 0));

        LobbyIdentity joining = Person("Joining");
        LobbySnapshot selected = (LobbySnapshot)manager.Execute(joining,
            new QuickPlayJoin(MatchMode.Survival, AllowBots: false));

        Assert.Equal(populated.LobbyId, selected.LobbyId);
        Assert.Contains(selected.Members, member => member.SessionId == joining.SessionId);
        Assert.DoesNotContain(sparse.Members, member => member.SessionId == joining.SessionId);
    }

    [Fact]
    public void QuickPlayNeverBypassesWaitlistOrOverfillsDuringConcurrentRequests()
    {
        var manager = new LobbyManager();
        LobbyIdentity owner = Person("Owner");
        LobbySnapshot lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("OneSeat", LobbyVisibility.Public, 2, 0));
        LobbyIdentity queued = Person("Queued");
        _ = manager.Execute(queued, new LobbyQueueJoin(lobby.LobbyId, lobby.Revision));

        LobbyCommandException fairness = Assert.Throws<LobbyCommandException>(() =>
            manager.Execute(Person("NoBypass"), new QuickPlayJoin()));
        Assert.Equal("no_match", fairness.Code);

        manager.Execute(queued, new LobbyQueueLeave(lobby.LobbyId, manager.ForSession(queued.SessionId)!.Revision));
        LobbyIdentity a = Person("A");
        LobbyIdentity b = Person("B");
        var outcomes = new object?[2];
        Parallel.Invoke(
            () => outcomes[0] = Record.Exception(() => manager.Execute(a, new QuickPlayJoin())),
            () => outcomes[1] = Record.Exception(() => manager.Execute(b, new QuickPlayJoin())));

        Assert.Single(outcomes, outcome => outcome is null);
        LobbyCommandException rejection = Assert.IsType<LobbyCommandException>(outcomes.Single(outcome => outcome is not null));
        Assert.Equal("no_match", rejection.Code);
        Assert.Equal(2, manager.ForSession(owner.SessionId)!.Members.Count(member => !member.Observer));
    }

    [Fact]
    public void QuickPlayCanBeDisabledForOperationalRollback()
    {
        var manager = new LobbyManager(quickPlayV2Enabled: false);
        LobbyCommandException error = Assert.Throws<LobbyCommandException>(() =>
            manager.Execute(Person("Player"), new QuickPlayJoin()));
        Assert.Equal("unsupported", error.Code);
    }
}
