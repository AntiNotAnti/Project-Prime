using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using MphRead;
using MphRead.Cosmetics;
using MphRead.Identity;
using MphRead.Mods.Network;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class CosmeticLobbyTests
{
    private static readonly CosmeticLoadoutIds SamusLoadout = new(1, 1, 1);

    [Fact]
    public void RegisteredAndGuestSelectionsClearReadyAndFreezeIntoMatch()
    {
        var manager = new LobbyManager();
        var account = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Account");
        var guest = new LobbyIdentity(Guid.NewGuid(), null, "Guest", Guid.NewGuid());
        var lobby = (LobbySnapshot)manager.Execute(account,
            new LobbyCreate("Cosmetics", LobbyVisibility.Public, 2, 0));
        lobby = (LobbySnapshot)manager.Execute(guest,
            new LobbyJoin(lobby.LobbyId, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(account,
            new LobbyConfigure(lobby.Revision, "unit", MatchMode.Battle));
        lobby = (LobbySnapshot)manager.Execute(account,
            new LobbySetReady(true, lobby.Revision));

        lobby = (LobbySnapshot)manager.Execute(account,
            new LobbySelectCosmetics(SamusLoadout, lobby.Revision));
        LobbyMember accountMember = lobby.Members.Single(value => value.SessionId == account.SessionId);
        Assert.False(accountMember.Ready);
        Assert.Equal(SamusLoadout, accountMember.Cosmetics);

        lobby = (LobbySnapshot)manager.Execute(guest,
            new LobbySelectCosmetics(SamusLoadout, lobby.Revision));
        Assert.Equal(SamusLoadout,
            lobby.Members.Single(value => value.SessionId == guest.SessionId).Cosmetics);
        lobby = (LobbySnapshot)manager.Execute(account,
            new LobbySetReady(true, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(guest,
            new LobbySetReady(true, lobby.Revision));

        MatchSpec spec = manager.PrepareMatch(account.SessionId, lobby.Revision,
            new("unit", "hash", "1", "test", NetHeader.Version),
            new NodeId(Guid.NewGuid()), Guid.NewGuid());

        Assert.Equal(SamusLoadout,
            spec.Roster.Single(value => value.PlayerId == new PlayerId(account.PlayerId!.Value)).Cosmetics);
        Assert.Equal(SamusLoadout,
            spec.Roster.Single(value => value.GuestSessionId == guest.GuestSessionId).Cosmetics);
    }

    [Fact]
    public void InvalidSelectionDoesNotMutateAndHunterChangeClearsPriorLoadout()
    {
        var manager = new LobbyManager();
        var owner = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Owner");
        var lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbyCreate("Cosmetics", LobbyVisibility.Public));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySelectCosmetics(SamusLoadout, lobby.Revision));
        lobby = (LobbySnapshot)manager.Execute(owner,
            new LobbySetReady(true, lobby.Revision));

        LobbyCommandException error = Assert.Throws<LobbyCommandException>(() => manager.Execute(owner,
            new LobbySelectCosmetics(new CosmeticLoadoutIds(ushort.MaxValue, 0, 0), lobby.Revision)));
        Assert.Equal("invalid", error.Code);
        LobbySnapshot unchanged = manager.ForSession(owner.SessionId)!;
        Assert.Equal(lobby.Revision, unchanged.Revision);
        Assert.True(unchanged.Members.Single().Ready);
        Assert.Equal(SamusLoadout, unchanged.Members.Single().Cosmetics);

        LobbySnapshot changed = (LobbySnapshot)manager.Execute(owner,
            new LobbySelectHunter(Hunter.Kanden, unchanged.Revision));
        Assert.False(changed.Members.Single().Ready);
        Assert.Equal(CosmeticLoadoutIds.Default, changed.Members.Single().Cosmetics);

        LobbyCommandException restricted = Assert.Throws<LobbyCommandException>(() =>
            manager.Execute(owner, new LobbySelectCosmetics(
                new CosmeticLoadoutIds(0, 0,
                    BuiltInCosmeticIds.DeathSamusBackwardCollapse),
                changed.Revision)));
        Assert.Equal("invalid", restricted.Code);
    }

    [Fact]
    public void BotSeatsRejectNonDefaultCosmetics()
    {
        MatchSpec spec = new(new MatchId(Guid.NewGuid()), new LobbyId(Guid.NewGuid()),
            new NodeId(Guid.NewGuid()), Guid.NewGuid(),
            new MatchRules(MatchMode.Battle, "unit", maxPlayers: 1),
            new ContentIdentity("unit", "hash", "1", "test", NetHeader.Version),
            MatchTrustClass.Community, null, null,
            ImmutableArray.Create(new RosterSeat(0, null, null, "Bot", Hunter.Samus,
                0, SeatRole.Bot, false, SamusLoadout)),
            ProjectPrime.Server.Shared.BotFillPolicy.Disabled, ObserverPolicy.Disabled, ReplayPolicy.Disabled,
            TelemetryPolicy.Disabled, 1, 2);

        Assert.Throws<ArgumentException>(spec.Validate);
    }

    [Fact]
    public void CosmeticControlCommandUsesDedicatedStrictRoute()
    {
        byte[] frame = Encoding.UTF8.GetBytes($$$"""
            {"version":{{{NodeControlCodec.Version}}},"type":"lobby.cosmetics.select","requestId":"{{{Guid.NewGuid():D}}}","payload":{"cosmetics":{"skinId":1,"armorEffectId":1,"deathEffectId":1},"expectedRevision":7}}
            """);

        Assert.Equal(new LobbySelectCosmetics(SamusLoadout, 7),
            NodeControlCodec.Read(frame).Command);
        Assert.Throws<JsonException>(() => NodeControlCodec.Read(Encoding.UTF8.GetBytes($$$"""
            {"version":{{{NodeControlCodec.Version}}},"type":"lobby.cosmetics.select","requestId":"{{{Guid.NewGuid():D}}}","payload":{"cosmetics":{"skinId":1,"armorEffectId":1,"deathEffectId":1},"expectedRevision":7,"guest":true}}
            """)));
    }
}
