using MphRead.Mods.Launcher;
using Xunit;

namespace MphRead.Tests;

public sealed class LauncherCompatibilityTests
{
    [Fact]
    public void PortableNetworkChoicesExplainTheNodeMigration()
    {
        Assert.Contains("GUI Node browser", TextLauncher.NodeMigrationMessage);
        Assert.Contains("public lobby", TextLauncher.NodeMigrationMessage);
        Assert.DoesNotContain("server_address", TextLauncher.NodeMigrationMessage);
        Assert.DoesNotContain("master", TextLauncher.NodeMigrationMessage, System.StringComparison.OrdinalIgnoreCase);
    }
}
