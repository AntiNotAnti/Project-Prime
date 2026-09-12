using System;
using System.Reflection;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class PublicNetworkEntryTests
{
    [Fact]
    public void NetLaunchExposesOnlyTheWorkerHandoffEntryPoint()
    {
        MethodInfo[] methods = typeof(NetLaunch).GetMethods(BindingFlags.Public | BindingFlags.Static);
        Assert.Contains(methods, method => method.Name == nameof(NetLaunch.JoinWorkerAsync));
        Assert.DoesNotContain(methods, method => method.Name is "Join" or "JoinAsync");
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2001:DB8::1")]
    public void WorkerHandoffAcceptsCanonicalIpv4AndIpv6Literals(string host)
    {
        Assert.True(NetLaunch.IsValidWorkerHost(host));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("2001:0db8::1")]
    [InlineData("not-an-address")]
    public void WorkerHandoffRejectsUnsafeOrNoncanonicalAddress(string host)
    {
        Assert.False(NetLaunch.IsValidWorkerHost(host));
    }

    [Fact]
    public void NodeBrowserHasNoLegacyDirectServerFallback()
    {
        Type browser = typeof(NodeBrowserView);
        string retiredFallbackEvent = "Legacy" + "Requested";
        Assert.Null(browser.GetEvent(retiredFallbackEvent,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.DoesNotContain(browser.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            method => method.Name.Contains("Legacy", StringComparison.OrdinalIgnoreCase));
    }
}
