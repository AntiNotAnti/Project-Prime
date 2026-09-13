using System;
using System.IO;
using Xunit;

namespace MphRead.Tests;

public sealed class HomeViewRuntimeOwnershipTests
{
    [Fact]
    public void ClassicHomeDisposesItsCanonicalOwnersOnlyThroughExplicitLifetime()
    {
        string root = FindRepositoryRoot();
        string home = File.ReadAllText(Path.Combine(root,
            "src", "Client.Presentation", "Launcher", "Gui", "HomeView.cs"));
        string launcher = File.ReadAllText(Path.Combine(root,
            "src", "Client", "Launcher", "Gui", "GuiLauncher.cs"));

        Assert.Contains("IAsyncDisposable, IDisposable", home,
            StringComparison.Ordinal);
        Assert.Contains("await _classicPlay.DisposeAsync()", home,
            StringComparison.Ordinal);
        Assert.Contains("await _classicOnline.DisposeAsync()", home,
            StringComparison.Ordinal);
        Assert.Contains("window.Closed += (_, _) => _ = view.DisposeAsync().AsTask();",
            launcher, StringComparison.Ordinal);

        int detached = home.IndexOf("OnDetachedFromVisualTree", StringComparison.Ordinal);
        int detachedEnd = home.IndexOf("public async ValueTask DisposeAsync", detached,
            StringComparison.Ordinal);
        Assert.True(detached >= 0 && detachedEnd > detached);
        string detachedBody = home[detached..detachedEnd];
        Assert.DoesNotContain("DisposeAsync", detachedBody, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory != null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Game.sln")))
                return directory.FullName;
        }
        throw new DirectoryNotFoundException("Project Prime repository root was not found.");
    }
}
