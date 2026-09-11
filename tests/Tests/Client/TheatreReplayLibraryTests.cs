using System;
using System.Diagnostics;
using System.IO;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

public sealed class TheatreReplayLibraryTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        $"project-prime-theatre-{Guid.NewGuid():N}");
    private readonly ReplayLibraryAdapter _library;
    private readonly PrimeReplayEntry _replay;

    public TheatreReplayLibraryTests()
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "original.fpreplay");
        File.WriteAllBytes(path, [1, 2, 3]);
        _library = new ReplayLibraryAdapter(_directory);
        _replay = new PrimeReplayEntry("original", path, "original.fpreplay",
            "MP1 SANCTORUS", new DateTime(2026, 1, 1), 3);
    }

    [Theory]
    [InlineData("Tournament Final", "Tournament Final.fpreplay")]
    [InlineData("Tournament Final.fpreplay", "Tournament Final.fpreplay")]
    [InlineData("Tournament Final.FPREPLAY", "Tournament Final.fpreplay")]
    [InlineData("  Tournament Final  ", "Tournament Final.fpreplay")]
    public void RenameNormalizesExactlyOneExtension(string requested, string expected)
    {
        Assert.True(_library.Rename(_replay, requested));
        Assert.True(File.Exists(Path.Combine(_directory, expected)));
        Assert.False(File.Exists(_replay.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("/tmp/escape")]
    [InlineData("C:\\escape")]
    [InlineData("bad:name")]
    [InlineData("bad|name")]
    [InlineData("bad*name")]
    [InlineData("name.fpreplay.fpreplay")]
    public void RenameRejectsPathsAndInvalidBasenames(string requested)
    {
        Assert.False(_library.Rename(_replay, requested));
        Assert.True(File.Exists(_replay.Path));
    }

    [Fact]
    public void RenameRejectsCollisionWithoutOverwritingEitherFile()
    {
        string destination = Path.Combine(_directory, "occupied.fpreplay");
        File.WriteAllBytes(destination, [9, 8, 7]);

        Assert.False(_library.Rename(_replay, "occupied"));
        Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(_replay.Path));
        Assert.Equal(new byte[] { 9, 8, 7 }, File.ReadAllBytes(destination));
    }

    [Fact]
    public void RenameFinalDestinationMustRemainInsideReplayDirectory()
    {
        Assert.True(ReplayFileNamePolicy.IsWithinDirectory(_directory,
            Path.Combine(_directory, "safe.fpreplay")));
        Assert.False(ReplayFileNamePolicy.IsWithinDirectory(_directory,
            Path.Combine(Directory.GetParent(_directory)!.FullName,
                "outside.fpreplay")));
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("MacOS")]
    [InlineData("Linux")]
    public void RevealUsesArgumentListWithoutShellInterpolation(string platformName)
    {
        ReplayRevealPlatform platform = Enum.Parse<ReplayRevealPlatform>(platformName);
        string path = Path.Combine(_directory, "name with spaces;$(touch evil).fpreplay");
        ProcessStartInfo start = ReplayReveal.CreateStartInfo(path, platform)!;

        Assert.False(start.UseShellExecute);
        Assert.Empty(start.Arguments);
        Assert.NotEmpty(start.ArgumentList);
        Assert.DoesNotContain("touch evil", start.FileName, StringComparison.Ordinal);
        if (platform == ReplayRevealPlatform.Windows)
        {
            Assert.Single(start.ArgumentList);
            Assert.StartsWith("/select,", start.ArgumentList[0], StringComparison.Ordinal);
            Assert.Contains(path, start.ArgumentList[0], StringComparison.Ordinal);
        }
        else if (platform == ReplayRevealPlatform.MacOS)
        {
            Assert.Equal(["-R", path], start.ArgumentList);
        }
        else
        {
            Assert.Equal([_directory], start.ArgumentList);
        }
    }

    [Fact]
    public void RevealIsUnavailableForUnsupportedPlatform()
        => Assert.Null(ReplayReveal.CreateStartInfo(_replay.Path,
            ReplayRevealPlatform.Unsupported));

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
