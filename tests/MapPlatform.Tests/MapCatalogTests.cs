using System.Text;
using MphRead.Mods.MapGen;

namespace ProjectPrime.MapPlatform.Tests;

public sealed class MapCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-map-catalog-tests-" + Guid.NewGuid().ToString("N"));

    public MapCatalogTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task InstallRefreshAndRemoveUseStableIdentityWithoutTouchingSource()
    {
        string incoming = Path.Combine(_root, "incoming", "untrusted-name.fpmap");
        Directory.CreateDirectory(Path.GetDirectoryName(incoming)!);
        MapBundleWriteResult written = await new MapBundleWriter().WriteAsync(incoming, new MapManifest
        {
            StableId = "community.catalog-fixture",
            Version = new MapVersion(2, 0, 0),
            Name = "Catalog Fixture",
            Author = "Tests",
            SupportedModes = [MapMode.Battle],
            Redistribution = true,
            Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes(
            "{\"name\":\"CATALOG FIXTURE\",\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":1}],"
            + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
            + "\"spawns\":[{\"position\":[0,2,0]},{\"position\":[2,2,0]}]}"))]);
        string installedDirectory = Path.Combine(_root, "installed");
        using var catalog = new MapCatalog(new MapCatalogOptions { InstalledDirectory = installedDirectory });

        InstalledMap installed = await catalog.InstallAsync(incoming);

        Assert.True(File.Exists(incoming));
        Assert.Equal(written.ContentIdentity, installed.ContentIdentity);
        Assert.StartsWith(installedDirectory, installed.SourcePath, StringComparison.Ordinal);
        Assert.DoesNotContain("untrusted-name", Path.GetFileName(installed.SourcePath));
        Assert.Single(catalog.Snapshot.Maps);

        await catalog.RefreshAsync();
        Assert.Single(catalog.Snapshot.Maps);
        await catalog.RemoveAsync(written.ContentIdentity);
        Assert.Empty(catalog.Snapshot.Maps);
        Assert.True(File.Exists(incoming));
    }

    [Fact]
    public async Task RefreshPublishesANewImmutableSnapshot()
    {
        string projects = Path.Combine(_root, "projects");
        Directory.CreateDirectory(projects);
        File.WriteAllText(Path.Combine(projects, "fixture.json"),
            "{\"name\":\"fixture\",\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}]}");
        using var catalog = new MapCatalog(new MapCatalogOptions
        {
            InstalledDirectory = Path.Combine(_root, "installed"),
            ProjectDirectories = [projects]
        });
        MapCatalogSnapshot before = catalog.Snapshot;

        await catalog.RefreshAsync();

        MapCatalogSnapshot after = catalog.Snapshot;
        Assert.NotSame(before, after);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Single(after.Maps);
        Assert.Equal("legacy.fixture", after.Maps[0].ContentIdentity.Identity.StableId);
    }

    [Fact]
    public async Task ValidCacheRestoresReadyStateAndRemovalPrunesOnlyItsCache()
    {
        string incoming = Path.Combine(_root, "incoming.fpmap");
        MapBundleWriteResult written = await new MapBundleWriter().WriteAsync(incoming,
            new MapManifest
            {
                StableId = "community.cached-fixture", Version = new MapVersion(1, 0, 0),
                Name = "Cached Fixture", Author = "Tests", SupportedModes = [MapMode.Battle],
                Recipe = "map.json"
            }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes(
                "{\"name\":\"CACHED FIXTURE\",\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":1}],"
                + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
                + "\"spawns\":[{\"position\":[0,2,0]},{\"position\":[2,2,0]}]}"))]);
        string installed = Path.Combine(_root, "installed");
        string cache = Path.Combine(_root, "cache");
        using var catalog = new MapCatalog(new MapCatalogOptions
        {
            InstalledDirectory = installed, CacheDirectory = cache
        });
        await catalog.InstallAsync(incoming);
        string build = Path.Combine(cache, new string('a', 64));
        Directory.CreateDirectory(build);
        string[] runtime = ["Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin"];
        var generated = new List<MapGeneratedFile>();
        foreach (string name in runtime)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            File.WriteAllBytes(Path.Combine(build, name), bytes);
            generated.Add(new(name, bytes.Length, MapJson.Sha256(bytes)));
        }
        var metadata = new MapBuildMetadata
        {
            CompilerSchemaVersion = MapCompilerSchema.Current, CompilerVersion = "test",
            BuildFingerprint = Path.GetFileName(build), SourceIdentity = written.ContentIdentity,
            GeneratedFiles = generated, Statistics = new MapBuildStatistics { RenderTriangles = 12 }
        };
        File.WriteAllBytes(Path.Combine(build, "build.json"),
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(metadata,
                MapJsonContext.Default.MapBuildMetadata));

        await catalog.RefreshAsync();
        Assert.Equal(MapBuildState.Ready, Assert.Single(catalog.Snapshot.Maps).BuildState);
        Assert.Equal(12, catalog.Snapshot.Maps[0].Statistics!.RenderTriangles);

        await catalog.RemoveAsync(written.ContentIdentity);
        Assert.False(Directory.Exists(build));
        Assert.Empty(catalog.Snapshot.Maps);
    }

    [Fact]
    public async Task EditableProjectAndInstalledPackageWithSameIdentityCoexist()
    {
        string projects = Path.Combine(_root, "projects");
        Directory.CreateDirectory(projects);
        var project = new MapProject
        {
            StableId = "community.parallel-source",
            Version = new MapVersion(1, 2, 0),
            Metadata = new MapProjectMetadata { Name = "Parallel Source", Author = "Tests" },
            Map = new MapDefinition
            {
                Name = "PARALLEL SOURCE",
                Materials = [new MapMaterial { Name = "fixture", SourceMaterial = 1 }],
                Brushes = [new MapBrush { Min = [0, 0, 0], Max = [1, 1, 1] }],
                Spawns = [new MapSpawn { Position = [0, 2, 0] }, new MapSpawn { Position = [2, 2, 0] }]
            }
        };
        MapProjectIO.Save(project, Path.Combine(projects, "map.json"));

        string incoming = Path.Combine(_root, "parallel.fpmap");
        await new MapBundleWriter().WriteAsync(incoming, new MapManifest
        {
            StableId = project.StableId,
            Version = project.Version,
            Name = project.Metadata.Name,
            Author = project.Metadata.Author,
            SupportedModes = [MapMode.Battle],
            Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes(
            "{\"name\":\"PARALLEL SOURCE\",\"materials\":[{\"name\":\"fixture\",\"sourceMaterial\":1}],"
            + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
            + "\"spawns\":[{\"position\":[0,2,0]},{\"position\":[2,2,0]}]}"))]);

        using var catalog = new MapCatalog(new MapCatalogOptions
        {
            InstalledDirectory = Path.Combine(_root, "installed"),
            ProjectDirectories = [projects]
        });
        await catalog.InstallAsync(incoming);

        Assert.Equal(2, catalog.Snapshot.Maps.Length);
        Assert.Contains(catalog.Snapshot.Maps, map => map.Source == MapInstallSource.LocalProject);
        Assert.Contains(catalog.Snapshot.Maps, map => map.Source == MapInstallSource.InstalledPackage);
        Assert.DoesNotContain(catalog.Snapshot.Maps, map => map.BuildState == MapBuildState.Invalid);
    }

    [Fact]
    public async Task MissingImportedGeometryRemainsVisibleAsRepairableCatalogState()
    {
        string projects = Path.Combine(_root, "missing-project");
        Directory.CreateDirectory(projects);
        var project = new MapProject
        {
            StableId = "community.missing-source",
            Metadata = new MapProjectMetadata { Name = "Missing Source", Author = "Tests" },
            Map = new MapDefinition
            {
                Name = "MISSING SOURCE",
                Import = new MapImport { Source = "missing.bsp", KeepSpawns = true }
            }
        };
        MapProjectIO.Save(project, Path.Combine(projects, "map.project.json"));
        using var catalog = new MapCatalog(new MapCatalogOptions
        {
            InstalledDirectory = Path.Combine(_root, "installed"),
            ProjectDirectories = [projects]
        });

        await catalog.RefreshAsync();

        InstalledMap map = Assert.Single(catalog.Snapshot.Maps);
        Assert.Equal(MapBuildState.MissingDependency, map.BuildState);
        Assert.Contains(map.Diagnostics, diagnostic => diagnostic.Code == "MAP-DEP-002");
    }

    [Fact]
    public async Task InstallRejectsSemanticallyInvalidPackageWithoutPublishingOrCopyingIt()
    {
        string incoming = Path.Combine(_root, "invalid.fpmap");
        await new MapBundleWriter().WriteAsync(incoming, new MapManifest
        {
            StableId = "community.invalid-install",
            Version = new MapVersion(1, 0, 0),
            Name = "Invalid Install",
            Author = "Tests",
            SupportedModes = [MapMode.Battle],
            Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes("{}"))]);
        string installed = Path.Combine(_root, "installed-invalid");
        using var catalog = new MapCatalog(new MapCatalogOptions { InstalledDirectory = installed },
            new MapBundleReadOptions { AllowLegacyV1 = false });

        MapValidationException exception = await Assert.ThrowsAsync<MapValidationException>(async () =>
            await catalog.InstallAsync(incoming));

        Assert.Contains(exception.Diagnostics, value => value.Code == "MAP-GEO-002");
        Assert.Empty(catalog.Snapshot.Maps);
        Assert.False(Directory.Exists(installed));
        Assert.True(File.Exists(incoming));
    }

    [Fact]
    public async Task InstallAndRefreshShareOneMutationAuthority()
    {
        (string incoming, MapContentIdentity identity) = await CreatePackageAsync("install-refresh");
        using var catalog = new MapCatalog(new MapCatalogOptions
            { InstalledDirectory = Path.Combine(_root, "installed-race") });

        await Task.WhenAll(catalog.InstallAsync(incoming).AsTask(),
            catalog.RefreshAsync().AsTask());

        Assert.NotNull(catalog.Snapshot.Find(identity));
        Assert.Single(catalog.Snapshot.Maps);
    }

    [Fact]
    public async Task RemoveAndRefreshShareOneMutationAuthority()
    {
        (string incoming, MapContentIdentity identity) = await CreatePackageAsync("remove-refresh");
        using var catalog = new MapCatalog(new MapCatalogOptions
            { InstalledDirectory = Path.Combine(_root, "remove-race") });
        await catalog.InstallAsync(incoming);

        await Task.WhenAll(catalog.RemoveAsync(identity).AsTask(),
            catalog.RefreshAsync().AsTask());

        Assert.Null(catalog.Snapshot.Find(identity));
        Assert.Empty(catalog.Snapshot.Maps);
    }

    [Fact]
    public void SnapshotBuildsDeterministicLookupIndexes()
    {
        MapProject project = new()
        {
            StableId = "community.indexed",
            Version = new MapVersion(1, 2, 3),
            Metadata = new MapProjectMetadata { Name = "Indexed", Author = "Tests" },
            Map = new MapDefinition { Name = "INDEXED ROOM" }
        };
        var identity = new MapContentIdentity(project.Identity, new string('a', 64));
        var installed = new InstalledMap(identity, "Indexed", "Tests", "",
            MapInstallSource.InstalledPackage, [MapMode.Battle], MapBuildState.Ready,
            null, "/installed.fpmap", new string('b', 64), 12, null, null, [], project);
        var editable = installed with
        {
            Source = MapInstallSource.LocalProject,
            SourcePath = "/project/map.project.json"
        };

        var snapshot = new MapCatalogSnapshot(4, [installed, editable], []);

        Assert.Same(installed, snapshot.Find(identity));
        Assert.Same(installed, snapshot.Find("community.indexed", new MapVersion(1, 2, 3),
            new string('A', 64)));
        Assert.Same(installed, snapshot.FindRoom("indexed room"));
        Assert.Equal(2, snapshot.ByStableIdVersion[project.Identity].Length);
    }

    [Fact]
    public async Task RefreshDoesNotEraseAnActiveBuildState()
    {
        (string incoming, MapContentIdentity identity) = await CreatePackageAsync("building");
        using var catalog = new MapCatalog(new MapCatalogOptions
            { InstalledDirectory = Path.Combine(_root, "building-installed") });
        await catalog.InstallAsync(incoming);
        catalog.PublishBuildState(identity, MapBuildState.Building, null, []);

        await catalog.RefreshAsync();

        Assert.Equal(MapBuildState.Building, catalog.Snapshot.Find(identity)!.BuildState);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private async Task<(string Path, MapContentIdentity Identity)> CreatePackageAsync(string suffix)
    {
        string path = Path.Combine(_root, suffix + ".fpmap");
        MapBundleWriteResult result = await new MapBundleWriter().WriteAsync(path, new MapManifest
        {
            StableId = "community." + suffix,
            Version = new MapVersion(1, 0, 0),
            Name = suffix,
            Author = "Tests",
            SupportedModes = [MapMode.Battle],
            Recipe = "map.json"
        }, [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes(
            $"{{\"name\":\"{suffix.ToUpperInvariant()}\",\"materials\":[{{\"name\":\"fixture\",\"sourceMaterial\":1}}],"
            + "\"brushes\":[{\"min\":[0,0,0],\"max\":[1,1,1]}],"
            + "\"spawns\":[{\"position\":[0,2,0]},{\"position\":[2,2,0]}]}"))]);
        return (path, result.ContentIdentity);
    }
}
