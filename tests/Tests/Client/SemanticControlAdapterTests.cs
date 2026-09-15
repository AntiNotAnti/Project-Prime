using System.Collections.Generic;
using MphRead.Mods.Launcher.Gui;
using Xunit;

namespace MphRead.Tests;

public sealed class SemanticControlAdapterTests
{
    [Fact]
    public void ConfigurationIsDisabledWithoutExplicitDevelopmentSwitch()
    {
        bool resolved = HomeWindow.SemanticControlAdapter.TryResolveConfiguration(
            new Dictionary<string, string?>
            {
                ["PRIME_E2E_CONTROL_ENDPOINT"] = "/tmp/prime-control.sock",
                ["PRIME_E2E_CONTROL_TOKEN_FILE"] = "/tmp/prime-control.token"
            }, out _, out _);

        Assert.False(resolved);
    }

    [Fact]
    public void ConfigurationSelectsTheRequestedPerClientEndpointAndToken()
    {
        bool resolved = HomeWindow.SemanticControlAdapter.TryResolveConfiguration(
            new Dictionary<string, string?>
            {
                ["PRIME_E2E_ENABLE"] = "1",
                ["PRIME_E2E_CLIENT_SLOT"] = "b",
                ["PRIME_E2E_CONTROL_ENDPOINT_A"] = "/tmp/client-a.sock",
                ["PRIME_E2E_CONTROL_ENDPOINT_B"] = "/tmp/client-b.sock",
                ["PRIME_E2E_CLIENT_A_TOKEN_FILE"] = "/tmp/client-a.token",
                ["PRIME_E2E_CLIENT_B_TOKEN_FILE"] = "/tmp/client-b.token"
            }, out string endpoint, out string tokenFile);

        Assert.True(resolved);
        Assert.Equal("/tmp/client-b.sock", endpoint);
        Assert.Equal("/tmp/client-b.token", tokenFile);
    }
}
