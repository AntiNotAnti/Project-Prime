using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Transport impairment")]
public sealed class ServerContentValidationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "fruity-staged-content-test-" + Guid.NewGuid().ToString("N"));

    public ServerContentValidationTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void MissingExtractedDataFailsWithoutCreatingFiles()
    {
        var error = Assert.Throws<ProgramException>(() => ServerContentValidation.Validate(_directory, "AMHE1",
            new[] { new RotationEntry() }));
        Assert.Contains("_bin/arm9.bin", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public void HostingAllowsNoRotationButStillRequiresContent()
    {
        var error = Assert.Throws<ProgramException>(() => ServerContentValidation.Validate(_directory, "AMHE1",
            Array.Empty<RotationEntry>(), hosting: true));
        Assert.Contains("_bin/arm9.bin", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(769)]
    public void InvalidRotationCountFailsBeforeReadingContent(int count)
    {
        var entries = Enumerable.Repeat(new RotationEntry(), count).ToArray();
        var error = Assert.Throws<ProgramException>(() => ServerContentValidation.Validate(_directory, "AMHE1", entries));
        Assert.Contains("768", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Theory]
    [InlineData("room")]
    [InlineData("mode")]
    [InlineData("negative-time")]
    [InlineData("nan-time")]
    [InlineData("infinite-time")]
    [InlineData("points")]
    public void InvalidRotationSettingsFailBeforeReadingContent(string invalid)
    {
        RotationEntry entry = invalid switch
        {
            "room" => new() { RoomKey = " " },
            "mode" => new() { Mode = GameMode.None },
            "negative-time" => new() { TimeLimit = -1 },
            "nan-time" => new() { TimeLimit = Single.NaN },
            "infinite-time" => new() { TimeLimit = Single.PositiveInfinity },
            _ => new() { PointGoal = -1 }
        };
        var error = Assert.Throws<ProgramException>(() => ServerContentValidation.Validate(_directory, "AMHE1", new[] { entry }));
        Assert.Contains("Invalid configured", error.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Fact]
    public void ValidatedScenarioViewReusesTheOpenedManifestAndContextRestoresIt()
    {
        using var saved = ServerContent.PreserveContext("AMHE1");
        ServerContentScenario[] previous = ServerContent.ValidatedScenarios.ToArray();
        ServerContentManifest manifest = CreateManifest();
        using (ServerContent.PreserveContext("AMHE1"))
        {
            ServerContent.Open(_directory, "AMHE1");
            Assert.Equal(manifest.Scenarios, ServerContent.ValidatedScenarios.ToArray());
            // Reading declarations is an in-memory operation, not a second
            // integrity pass. Opening again still verifies changed dependencies.
            File.WriteAllBytes(Path.Combine(_directory, "levels/test.bin"), new byte[] { 9 });
            Assert.Equal(manifest.Scenarios, ServerContent.ValidatedScenarios.ToArray());
            Assert.Throws<ProgramException>(() => ServerContent.Open(_directory, "AMHE1"));
        }
        Assert.Equal(previous, ServerContent.ValidatedScenarios.ToArray());
    }

    [Fact]
    public void PackageRejectsUncoveredConfiguredScenarioBeforeSceneCreation()
    {
        using var saved = ServerContent.PreserveContext("AMHE1");
        CreateManifest();
        var error = Assert.Throws<ProgramException>(() => ServerContentValidation.Validate(_directory, "AMHE1",
            new[] { new RotationEntry { RoomKey = "MP1 SANCTORUS", Mode = GameMode.Survival } }));
        Assert.Contains("does not support", error.Message);
        Assert.Equal(2, Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories).Count());
    }

    private ServerContentManifest CreateManifest()
    {
        byte[] bytes = { 1, 2, 3, 4 };
        Directory.CreateDirectory(Path.Combine(_directory, "levels"));
        File.WriteAllBytes(Path.Combine(_directory, "levels/test.bin"), bytes);
        var manifest = new ServerContentManifest(1, ServerContentPack.Profile, "AMHE1",
            ServerContentPack.Amhe1Arm9Sha256, new[] { "MP1 SANCTORUS" }, 600,
            new[] { new ServerContentFile("levels/test.bin", bytes.Length, Convert.ToHexStringLower(SHA256.HashData(bytes))) },
            new[] { new ServerContentScenario("MP1 SANCTORUS", GameMode.Battle) }, NetLaunch.RoomPlayerCount);
        File.WriteAllText(Path.Combine(_directory, ServerContentPack.ManifestName), JsonSerializer.Serialize(manifest));
        return manifest;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
