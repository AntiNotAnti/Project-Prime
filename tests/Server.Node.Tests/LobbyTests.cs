using FruityPrime.Server.Node.Lobbies;
using FruityPrime.Server.Shared;
using MphRead;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

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
        Assert.Equal(2, spec.Roster.Count(s => s.Role == SeatRole.Bot));
        Assert.All(spec.Roster.Where(s => s.Role == SeatRole.Bot), bot => { Assert.Null(bot.PlayerId); Assert.Null(bot.GuestSessionId); });
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
}
