using System;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;
using Xunit;

namespace MphRead.Tests;

[Collection("custom room loader")]
public sealed class MapBuildDirectoryScopeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-map-build-scope-" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousDataDirectory =
        Environment.GetEnvironmentVariable("PRIME_DATA_DIRECTORY");
    private readonly string _previousMapDirectory = CustomRooms.MapDirectory;

    [Fact]
    public void ExplicitBuildDirectoryExcludesUserEditorDrafts()
    {
        string dataDirectory = Path.Combine(_root, "data");
        string buildDirectory = Path.Combine(_root, "repository-maps");
        string editorProject = Path.Combine(dataDirectory, "maps", "projects", "draft");
        Directory.CreateDirectory(buildDirectory);
        Directory.CreateDirectory(editorProject);
        Environment.SetEnvironmentVariable("PRIME_DATA_DIRECTORY", dataDirectory);

        File.WriteAllText(Path.Combine(buildDirectory, "release.json"),
            "{\"name\":\"release map\","
            + "\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":0}],"
            + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
            + "\"spawns\":[{\"position\":[-2,2,0]},{\"position\":[2,2,0]}]}");
        File.WriteAllText(Path.Combine(editorProject, "map.project.json"),
            "{\"stableId\":\"community.draft\",\"version\":\"1.0.0\","
            + "\"metadata\":{\"name\":\"New Map\",\"author\":\"test\","
            + "\"description\":\"\",\"redistribution\":false},"
            + "\"map\":{\"name\":\"NEW MAP\"},"
            + "\"supportedModes\":[\"Battle\"]}");

        CustomRooms.SetBuildMapDirectory(buildDirectory);

        MapDefinition definition = Assert.Single(CustomRooms.Definitions);
        Assert.Equal("RELEASE MAP", definition.Name);
        Assert.DoesNotContain(CustomRooms.Definitions,
            candidate => candidate.Name.Equals("NEW MAP", StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PRIME_DATA_DIRECTORY", _previousDataDirectory);
        CustomRooms.MapDirectory = _previousMapDirectory;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
