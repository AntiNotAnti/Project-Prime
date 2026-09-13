using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ProjectPrime.Server.Node;
using Xunit;

namespace ProjectPrime.Server.Node.Tests;

public sealed class NodeNetworkOptionsTests
{
    [Fact]
    public async Task ForwardedClientAddressIsAcceptedOnlyFromConfiguredProxy()
    {
        var settings = new NodeNetworkOptions { TrustedProxies = ["127.0.0.1"] };
        var forwarding = new ForwardedHeadersOptions();
        NodeNetworkOptions.ConfigureForwarding(forwarding, settings);
        var middleware = new ForwardedHeadersMiddleware(_ => Task.CompletedTask,
            NullLoggerFactory.Instance, Options.Create(forwarding));

        var trusted = new DefaultHttpContext();
        trusted.Connection.RemoteIpAddress = IPAddress.Loopback;
        trusted.Request.Headers["X-Forwarded-For"] = "198.51.100.9";
        trusted.Request.Headers["X-Forwarded-Proto"] = "https";
        await middleware.Invoke(trusted);
        Assert.Equal("198.51.100.9", NodeNetworkOptions.EffectiveRemoteAddress(trusted));

        var untrusted = new DefaultHttpContext();
        untrusted.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.10");
        untrusted.Request.Headers["X-Forwarded-For"] = "198.51.100.9";
        untrusted.Request.Headers["X-Forwarded-Proto"] = "https";
        await middleware.Invoke(untrusted);
        Assert.Equal("203.0.113.10", NodeNetworkOptions.EffectiveRemoteAddress(untrusted));
    }

    [Fact]
    public void ForwardingConfigurationIsSingleHopAndSymmetric()
    {
        var forwarding = new ForwardedHeadersOptions();
        NodeNetworkOptions.ConfigureForwarding(forwarding, new NodeNetworkOptions
        {
            TrustedProxies = ["127.0.0.1"],
            TrustedNetworks = ["10.0.0.0/24"]
        });

        Assert.Equal(1, forwarding.ForwardLimit);
        Assert.True(forwarding.RequireHeaderSymmetry);
        Assert.Contains(IPAddress.Loopback, forwarding.KnownProxies);
        Assert.Single(forwarding.KnownIPNetworks);
    }
}
