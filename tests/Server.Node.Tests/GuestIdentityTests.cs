using ProjectPrime.Server.Node.Identity;
using ProjectPrime.Server.Node.Lobbies;
using ProjectPrime.Server.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using MphRead;
using MphRead.Identity;

namespace ProjectPrime.Server.Node.Tests;

public sealed class GuestIdentityTests
{
    [Fact]
    public async Task ExplicitGuestAdmissionIsTypedAndSingleUse()
    {
        await using var host = new NodeHostFixture();
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();
        Guid guest = Guid.NewGuid();
        string ticket = host.Ticket(guest, kind: "guest");
        NodeIdentity identity = Assert.IsType<NodeIdentity>(await validator.ValidateAsync(ticket));
        Assert.Null(await validator.ValidateAsync(ticket));
        Assert.Null(identity.PlayerId);
        Assert.Equal(guest, identity.GuestSessionId);
        Assert.Equal(HumanIdentityKind.Guest, identity.IdentityKey.Kind);
        Assert.Null(await validator.ValidateAsync(host.Ticket(guest, kind: "unknown")));
        Assert.Null(await validator.ValidateAsync(host.Ticket(Guid.Empty, kind: "guest")));
    }

    [Fact]
    public void GuestAndRegisteredRosterSeatsAreTaggedAndGuestsForcePractice()
    {
        var manager = new LobbyManager();
        var account = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Account");
        var guest = new LobbyIdentity(Guid.NewGuid(), null, Guid.NewGuid(), "Guest");
        var snapshot = (LobbySnapshot)manager.Execute(account, new LobbyCreate("Room", LobbyVisibility.Public, 2, 1));
        snapshot = (LobbySnapshot)manager.Execute(guest, new LobbyJoin(snapshot.LobbyId, snapshot.Revision));
        snapshot = (LobbySnapshot)manager.Execute(account, new LobbyConfigure(snapshot.Revision, "unit", MatchMode.Battle));
        snapshot = (LobbySnapshot)manager.Execute(account, new LobbySetReady(true, snapshot.Revision));
        snapshot = (LobbySnapshot)manager.Execute(guest, new LobbySetReady(true, snapshot.Revision));
        MatchSpec spec = manager.PrepareMatch(account.SessionId, snapshot.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());

        Assert.Equal(MatchTrustClass.Practice, spec.TrustClass);
        Assert.Contains(spec.Roster, seat => seat.PlayerId?.Value == account.PlayerId && seat.GuestSessionId == null);
        Assert.Contains(spec.Roster, seat => seat.PlayerId == null && seat.GuestSessionId == guest.GuestSessionId);
    }

    [Fact]
    public void RegisteredObserverDoesNotChangeTrustClassAndWireIdentityRejectsAmbiguity()
    {
        var manager = new LobbyManager();
        var account = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Account");
        var observer = new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "Observer");
        var snapshot = (LobbySnapshot)manager.Execute(account, new LobbyCreate("Room", LobbyVisibility.Public, 1, 1));
        snapshot = (LobbySnapshot)manager.Execute(observer, new LobbyJoin(snapshot.LobbyId, snapshot.Revision, true));
        snapshot = (LobbySnapshot)manager.Execute(account, new LobbyConfigure(snapshot.Revision, "unit", MatchMode.Battle));
        snapshot = (LobbySnapshot)manager.Execute(account, new LobbySetReady(true, snapshot.Revision));
        MatchSpec spec = manager.PrepareMatch(account.SessionId, snapshot.Revision,
            new("unit", "hash", "1", "test", 8), new(Guid.NewGuid()), Guid.NewGuid());
        Assert.Equal(MatchTrustClass.Community, spec.TrustClass);

        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(Guid.NewGuid(), null, "Guest", Guid.NewGuid(), new string('a', 43))));
        Assert.Throws<ArgumentException>(() => NodeControlCodec.Write("node.session", 1, null,
            new NodeSessionSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Guest", Guid.NewGuid(), new string('a', 43), Guid.NewGuid())));
        Assert.Throws<ArgumentException>(() => new LobbyManager().Execute(
            new LobbyIdentity(Guid.NewGuid(), Guid.NewGuid(), "é"), new LobbyList()));
    }
}
