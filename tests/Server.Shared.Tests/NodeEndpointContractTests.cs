using ProjectPrime.Server.Shared;
using Xunit;

namespace ProjectPrime.Server.Shared.Tests;

public sealed class NodeEndpointContractTests
{
    [Fact]
    public void CanonicalControlEndpointIsAcceptedWithoutRelaxingItsPath()
    {
        Assert.True(NodeEndpointContract.TryValidatePublicControlUri(
            "wss://127.0.0.1:44321/v1/control", out Uri endpoint));
        Assert.Equal("/v1/control", endpoint.AbsolutePath);

        Assert.True(NodeEndpointContract.TryValidatePublicControlUri(
            "wss://node.example/v1/control", out _));

        Assert.False(NodeEndpointContract.TryValidatePublicControlUri(
            "wss://127.0.0.1:44321/v1/control/", out _));
        Assert.False(NodeEndpointContract.TryValidatePublicControlUri(
            "wss://127.0.0.1:44321/v1%2fcontrol", out _));
        Assert.False(NodeEndpointContract.TryValidatePublicControlUri(
            "wss://127.0.0.1:0/v1/control", out _));
    }
}
