using MphRead.Mods.Network;
using MphRead.Mods.Launcher;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MphRead.Tests;

[Collection("Match baseline globals")]
public sealed class ClientOnlineRuntimeTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    public void OnlineRuntimeRollbackFlagIsExplicit(string? value, bool expected)
        => Assert.Equal(expected, ClientOnlineRuntime.ParseEnabled(value));

    [Fact]
    public async Task RuntimeOwnsTheExistingFlowStateMachineAndLifecycleCancellation()
    {
        await using var runtime = new ClientOnlineRuntime(enabled: true);
        Assert.Equal(ClientSessionPhase.Gateway, runtime.Flow.Phase);
        runtime.Flow.ShowHome(hasIdentity: true, hasLobby: false);
        Assert.Equal(ClientSessionPhase.OnlineHome, runtime.Flow.Phase);
        CancellationToken lifetime = runtime.Lifetime;
        Assert.False(lifetime.IsCancellationRequested);
        await runtime.DisposeAsync();
        Assert.True(lifetime.IsCancellationRequested);
    }
}
