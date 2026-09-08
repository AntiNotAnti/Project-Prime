using FruityPrime.Server.Node.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FruityPrime.Server.Node.Tests;

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
}
