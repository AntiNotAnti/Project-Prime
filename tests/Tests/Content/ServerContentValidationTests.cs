using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using MphRead;
using MphRead.Mods.MapGen;
using MphRead.Mods.Network;
using Xunit;

namespace MphRead.Tests;

[Collection("Transport impairment")]
public sealed class ServerContentValidationTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "project-prime-staged-content-test-" + Guid.NewGuid().ToString("N"));

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

    [Fact]
    public void MapDescriptorsAreOrdinalAndUseMatchModeValues()
    {
        ContentMapDescriptor[] maps = ServerContentValidation.BuildMapDescriptors(new[]
        {
            new ServerContentScenario("z-map", GameMode.PrimeHunter),
            new ServerContentScenario("a-map", GameMode.Capture),
            new ServerContentScenario("a-map", GameMode.Battle),
            new ServerContentScenario("z-map", GameMode.Battle)
        });

        Assert.Equal(new[] { "a-map", "z-map" }, maps.Select(map => map.MapKey));
        Assert.Equal(new[] { MatchMode.Battle, MatchMode.Capture }, maps[0].Modes);
        Assert.Equal(new[] { MatchMode.Battle, MatchMode.PrimeHunter }, maps[1].Modes);
        string json = JsonSerializer.Serialize(maps);
        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Number, document.RootElement[0].GetProperty("Modes")[0].ValueKind);
        Assert.Equal((int)MatchMode.Battle, document.RootElement[0].GetProperty("Modes")[0].GetInt32());
        Assert.Equal((int)MatchMode.Capture, document.RootElement[0].GetProperty("Modes")[1].GetInt32());
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("unknown-mode")]
    [InlineData("non-ascii")]
    [InlineData("too-long")]
    public void MapDescriptorValidationRejectsInvalidPairs(string invalid)
    {
        ServerContentScenario[] scenarios = invalid switch
        {
            "duplicate" => new[]
            {
                new ServerContentScenario("map", GameMode.Battle),
                new ServerContentScenario("map", GameMode.Battle)
            },
            "unknown-mode" => new[] { new ServerContentScenario("map", (GameMode)15) },
            "non-ascii" => new[] { new ServerContentScenario("mäp", GameMode.Battle) },
            _ => new[] { new ServerContentScenario(new string('m', 129), GameMode.Battle) }
        };

        Assert.Throws<ProgramException>(() => ServerContentValidation.BuildMapDescriptors(scenarios));
    }

    [Fact]
    public void MapDescriptorValidationRejectsMapAndPairBounds()
    {
        var tooManyMaps = Enumerable.Range(0, 257)
            .Select(index => new ServerContentScenario($"map-{index}", GameMode.Battle));
        Assert.Throws<ProgramException>(() => ServerContentValidation.BuildMapDescriptors(tooManyMaps));

        var tooManyPairs = Enumerable.Range(0, 65).SelectMany(index =>
            Enumerable.Range((int)GameMode.Battle, 12)
                .Select(mode => new ServerContentScenario($"map-{index}", (GameMode)mode)));
        Assert.Throws<ProgramException>(() => ServerContentValidation.BuildMapDescriptors(tooManyPairs));
    }

    [Fact]
    public void WorkerPreparationOptionIsParsed()
    {
        var flags = ProjectPrime.Server.Worker.Program.ParseArguments(
            new[] { "--prepare-content", "true", "--content-dir", _directory, "--content-version", "AMHE1" });

        Assert.Equal("true", flags["--prepare-content"]);
    }

    [Fact]
    public async Task PreparationAndDescriptionCannotRunTogether()
    {
        TextWriter previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(1, await ProjectPrime.Server.Worker.Program.Main(new[]
            {
                "--prepare-content", "true", "--describe-content", "true",
                "--content-dir", _directory, "--content-version", "AMHE1"
            }));
        }
        finally { Console.SetError(previousError); }

        Assert.Contains("cannot be enabled together", error.ToString());
    }

    [Fact]
    public async Task PreparationRejectsDeveloperValidationFixtures()
    {
        TextWriter previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(1, await ProjectPrime.Server.Worker.Program.Main(new[]
            {
                "--prepare-content", "true", "--validation-fixture", "none",
                "--content-dir", _directory, "--content-version", "AMHE1"
            }));
        }
        finally { Console.SetError(previousError); }

        Assert.Contains("developer validation fixture", error.ToString());
    }

    [Fact]
    public async Task PreparationRejectsBakedContentWithoutWriting()
    {
        CreateManifest();
        string manifestPath = Path.Combine(_directory, ServerContentPackage.ManifestName);
        byte[] before = File.ReadAllBytes(manifestPath);
        TextWriter previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            Assert.Equal(1, await ProjectPrime.Server.Worker.Program.Main(new[]
            {
                "--prepare-content", "true", "--content-dir", _directory,
                "--content-version", "AMHE1", "--map-dir", Path.Combine(_directory, "maps")
            }));
        }
        finally { Console.SetError(previousError); }

        Assert.Equal(before, File.ReadAllBytes(manifestPath));
        Assert.Contains("baked server content packages are read-only", error.ToString());
    }

    [Fact]
    public async Task DescriptionDoesNotPrepareMissingCustomRoomFiles()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "_bin"));
        Directory.CreateDirectory(Path.Combine(_directory, "models"));
        Directory.CreateDirectory(Path.Combine(_directory, "levels"));
        File.WriteAllBytes(Path.Combine(_directory, "_bin", "arm9.bin"), new byte[] { 1 });
        string mapDirectory = Path.Combine(_directory, "maps");
        Directory.CreateDirectory(mapDirectory);
        File.WriteAllText(Path.Combine(mapDirectory, "fixture.json"),
            "{\"name\":\"fixture\",\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}]}\n");

        TextWriter previousError = Console.Error;
        using var error = new StringWriter();
        try
        {
            Console.SetError(error);
            // The intentionally incomplete extracted fixture cannot pass the
            // retail room probe, but description must still remain read-only.
            Assert.Equal(1, await ProjectPrime.Server.Worker.Program.Main(new[]
            {
                "--describe-content", "true", "--content-dir", _directory,
                "--content-version", "AMHE1", "--map-dir", mapDirectory
            }));
        }
        finally { Console.SetError(previousError); }

        Assert.False(Directory.Exists(Path.Combine(_directory, "_archives")));
        Assert.False(File.Exists(Path.Combine(_directory, "levels", "entities", "fixture_Ent.bin")));
        Assert.False(File.Exists(Path.Combine(_directory, "levels", "nodeData", "fixture_Node.bin")));
    }

    [Fact]
    public void NeedsGeneratingRequiresAllRoomOutputs()
    {
        string previousDirectory = CustomRooms.MapDirectory;
        string previousContentRoot = CustomRooms.ContentRoot;
        using var saved = ServerContent.PreserveContext("AMHE1");
        try
        {
            Directory.CreateDirectory(Path.Combine(_directory, "_bin"));
            Directory.CreateDirectory(Path.Combine(_directory, "models"));
            Directory.CreateDirectory(Path.Combine(_directory, "levels"));
            File.WriteAllBytes(Path.Combine(_directory, "_bin", "arm9.bin"), new byte[] { 1 });
            ContentEnvironment.Open(_directory, "AMHE1");
            string mapDirectory = Path.Combine(_directory, "maps");
            Directory.CreateDirectory(mapDirectory);
            File.WriteAllText(Path.Combine(mapDirectory, "fixture.json"),
                "{\"name\":\"fixture\",\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}]}\n");
            CustomRooms.MapDirectory = mapDirectory;
            CustomRooms.ContentRoot = _directory;
            MethodInfo loader = typeof(CustomRooms).GetMethod("LoadDefinitions",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            MapDefinition definition = Assert.Single(
                (IReadOnlyList<MapDefinition>)loader.Invoke(null, null)!);

            string archive = CustomRooms.ArchiveDirectory(definition);
            string entities = CustomRooms.EntityDirectory();
            string nodes = CustomRooms.NodeDirectory();
            Directory.CreateDirectory(archive);
            Directory.CreateDirectory(entities);
            Directory.CreateDirectory(nodes);
            string prefix = definition.Name.ToLowerInvariant();
            File.WriteAllBytes(Path.Combine(archive, prefix + "_Model.bin"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(archive, prefix + "_Collision.bin"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(entities, prefix + "_Ent.bin"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(nodes, prefix + "_Node.bin"), new byte[] { 1 });
            Assert.True(CustomRooms.NeedsGenerating(definition));

            File.WriteAllBytes(Path.Combine(archive, prefix + "_Anim.bin"), new byte[] { 1 });
            Assert.False(CustomRooms.NeedsGenerating(definition));
        }
        finally
        {
            CustomRooms.MapDirectory = previousDirectory;
            CustomRooms.ContentRoot = previousContentRoot;
        }
    }

    [Fact]
    public void AppleDoubleMapFilesAreIgnored()
    {
        string previousDirectory = CustomRooms.MapDirectory;
        string mapDirectory = Path.Combine(_directory, "maps");
        Directory.CreateDirectory(mapDirectory);
        const string definition = "{\"name\":\"fixture\",\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}]}\n";
        File.WriteAllText(Path.Combine(mapDirectory, "fixture.json"), definition);
        File.WriteAllText(Path.Combine(mapDirectory, "._fixture.json"),
            "{\"name\":\"apple double\",\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}]}\n");
        try
        {
            CustomRooms.MapDirectory = mapDirectory;
            MethodInfo loader = typeof(CustomRooms).GetMethod("LoadDefinitions",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var definitions = (IReadOnlyList<MapDefinition>)loader.Invoke(null, null)!;
            Assert.Single(definitions);
            Assert.Equal("FIXTURE", definitions[0].Name);
        }
        finally { CustomRooms.MapDirectory = previousDirectory; }
    }

    [Fact]
    public void WorkerAcceptsMapDirectoryAndNormalizesItBeforeUse()
    {
        string previous = CustomRooms.MapDirectory;
        try
        {
            var flags = ProjectPrime.Server.Worker.Program.ParseArguments(new[] { "--map-dir", "worker-maps" });
            ProjectPrime.Server.Worker.Program.ApplyMapDirectory(flags);
            Assert.Equal(Path.GetFullPath("worker-maps"), CustomRooms.MapDirectory);
        }
        finally
        {
            CustomRooms.MapDirectory = previous;
        }
    }

    [Fact]
    public async Task DescribeContentEmitsIdentityAndMapCatalog()
    {
        using var saved = ServerContent.PreserveContext("AMHE1");
        CreateManifest();
        string mapDirectory = Path.Combine(_directory, "maps");
        Directory.CreateDirectory(mapDirectory);
        TextWriter previousOut = Console.Out;
        TextWriter previousError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            Assert.Equal(0, await ProjectPrime.Server.Worker.Program.Main(new[]
            {
                "--describe-content", "true", "--content-dir", _directory,
                "--content-version", "AMHE1", "--map-dir", mapDirectory
            }));
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }

        using JsonDocument document = JsonDocument.Parse(output.ToString());
        Assert.False(String.IsNullOrWhiteSpace(document.RootElement.GetProperty("ContentVersion").GetString()));
        Assert.False(String.IsNullOrWhiteSpace(document.RootElement.GetProperty("ContentHash").GetString()));
        Assert.True(document.RootElement.TryGetProperty("BuildVersion", out _));
        Assert.True(document.RootElement.TryGetProperty("ProtocolVersion", out _));
        JsonElement map = Assert.Single(document.RootElement.GetProperty("Maps").EnumerateArray());
        Assert.Equal("MP1 SANCTORUS", map.GetProperty("MapKey").GetString());
        Assert.Equal((int)MatchMode.Battle, Assert.Single(map.GetProperty("Modes").EnumerateArray()).GetInt32());
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
