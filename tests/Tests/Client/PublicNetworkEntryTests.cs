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

    [Fact]
    public void NodeBrowserHasNoLegacyDirectServerFallback()
    {
        Type? browser = typeof(GuiLauncher).Assembly.GetType("MphRead.Mods.Launcher.Gui.NodeBrowserView");
        Assert.NotNull(browser);
        string retiredFallbackEvent = "Legacy" + "Requested";
        Assert.Null(browser!.GetEvent(retiredFallbackEvent,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.DoesNotContain(browser.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            method => method.Name.Contains("Legacy", StringComparison.OrdinalIgnoreCase));
    }
}
