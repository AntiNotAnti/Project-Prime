using ProjectPrime.Server.Node.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeAuthenticationTests
{
    [Fact]
    public async Task ValidBackendAdmissionIsSingleUseAndDomainSeparated()
    {
        await using var host = new NodeHostFixture();
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();
        Guid player = Guid.NewGuid(); string ticket = host.Ticket(player);
        Assert.Equal(player, (await validator.ValidateAsync(ticket))!.PlayerId);
        Assert.Null(await validator.ValidateAsync(ticket));
        Assert.Null(await validator.ValidateAsync(host.Ticket(player, Guid.NewGuid().ToString("D"))));
        Assert.Null(await validator.ValidateAsync(host.Ticket(player, type: "JWT")));
        Assert.Null(await validator.ValidateAsync(host.Ticket(player, expiresIn: 121)));
        Assert.Null(await validator.ValidateAsync("opaque-account-token"));
    }

    [Fact]
    public async Task PresenceClaimDefaultsVisibleAndIsBoundToTheSignedIdentity()
    {
        await using var host = new NodeHostFixture();
        var validator = host.App.Services.GetRequiredService<NodeAdmissionValidator>();

        NodeIdentity legacy = (await validator.ValidateAsync(host.Ticket(Guid.NewGuid())))!;
        Assert.True(legacy.PublicPresence);

        NodeIdentity hidden = (await validator.ValidateAsync(
            host.Ticket(Guid.NewGuid(), publicPresence: false)))!;
        Assert.False(hidden.PublicPresence);

        NodeIdentity guest = (await validator.ValidateAsync(
            host.Ticket(Guid.NewGuid(), kind: "guest", publicPresence: false)))!;
        Assert.True(guest.IsGuest);
        Assert.False(guest.PublicPresence);
    }
}
