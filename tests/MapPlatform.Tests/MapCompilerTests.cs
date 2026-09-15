using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using MphRead;
using MphRead.Editor;
using MphRead.Mods.MapGen;
using MphRead.Formats.Collision;
using MphRead.Utility;
using OpenTK.Mathematics;

namespace ProjectPrime.MapPlatform.Tests;

[CollectionDefinition("map compiler", DisableParallelization = true)]
public sealed class MapCompilerCollection { }

public sealed record FirstPartyMapCharacterization(
    string ProjectPath,
    string BuildFingerprint,
    string ContentHash,
    string ModelHash,
    string AnimationHash,
    string CollisionHash,
    string EntityHash,
    string NodeHash,
    int RenderTriangles,
    int RenderVertices,
    int Materials,
    int CollisionFaces,
    int CollisionPoints,
    int CollisionPlanes,
    int GridReferences,
    int Entities,
    int Spawns);

[Collection("map compiler")]
public sealed class MapCompilerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "project-prime-map-compiler-tests-" + Guid.NewGuid().ToString("N"));

    public MapCompilerTests() => Directory.CreateDirectory(_directory);

    public static IEnumerable<object[]> FirstPartyMaps =>
    [
        [new FirstPartyMapCharacterization("maps/arena/arena.json",
            "65d51354b2196129d58f05748195837a039d836f9c3d038936784665720b5708",
            "d25a70e56104be84d0d81e7551f591878fee303790874d127a3305e0471f7415",
            "8bee339d4dbd803551cdbf4861b498002c0fc9803bb50a2846743219c039ad31",
            "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0",
            "b8e978dff78d9deb6f7ee4cc5afdeef4965c8c0c6cce326760d2c38322a55d41",
            "ee808f8516ea02c27cca6f8ab7f9f2b7a774262876692c93ee19d37db7dec440",
            "435d1238de340fd9475a1d5bc5bd74981f24012c8ef352783309d30e5803a5ba",
            84, 168, 2, 42, 40, 23, 1_074, 12, 8)],
        [new FirstPartyMapCharacterization("maps/parallax/parallax.json",
            "b43bb546b026bc5f5eb28e75590cee1b64879fad8ab97b047bbb9b7ac459331f",
            "45b137a7d0282cb11648a4810c6a997451e2f7c8a47d1e5dea51085f5061fcea",
            "f2b0ede3ced7fd428752c858b646d1006b8e518e6560fc1d2d4b6dff824f0d20",
            "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0",
            "69360d454501c2cb4cd9dfc993be23147adf3174b80d1bc8184f42d5716f2eac",
            "cbd76c664a7e49303760fa7addcf04ff1ac12ad0d92df160f7b86785726f191e",
            "e29121476fb568c5c8287e6af5cdb550c99b80efcb6fcec3198236cf5d0b8525",
            1_076, 3_228, 10, 538, 1_449, 259, 6_713, 15, 4)],
        [new FirstPartyMapCharacterization("maps/dust2/dust2.json",
            "7f32371341f5bceaf3db47132b43c789b98c6a73e57abd0bbbcccac0d3cc80ff",
            "7227d369b6ba389182e511ebec0a5cfab90c8af968655b4ad4fba4212fcfda0c",
            "a3ea7f08f28524cac8d218485af17bafb4347108b94906822ca8d9298ce37c24",
            "9d908ecfb6b256def8b49a7c504e6c889c4b0e41fe6ce3e01863dd7b61a20aa0",
            "2726aa0d24c14631c5b2948b60650978a9e5fba1dbaa954f123880a29a53885d",
            "9dcd0497326aae6523d86357a0db3a54415588270b0e85a960e58041272910eb",
            "eb65aef86b2e13212c7970ee1320ebc0f30f66781ec0fc73696508f9fd3815ab",
            11_056, 33_168, 21, 5_643, 10_664, 2_687, 24_038, 40, 10)]
    ];

    [Fact]
    public void StableIdentityAndCanonicalSourceHashingAreStrict()
    {
        Assert.Equal("community.parallax",
            new MapIdentity("community.parallax", new MapVersion(1, 2, 0)).StableId);
        Assert.Throws<ArgumentException>(() => new MapIdentity("Community Parallax", new MapVersion(1, 0, 0)));
        Assert.Equal("legacy.parallax", MapIdentity.FromLegacyName("PARALLAX"));
        Assert.Equal(MapJson.Sha256(MapJson.Canonicalize("{\"a\":1,\"b\":2}"u8)),
            MapJson.Sha256(MapJson.Canonicalize("{\"b\":2,\"a\":1}"u8)));
    }

    [Theory]
    [MemberData(nameof(FirstPartyMaps))]
    [Trait("RequiresGameContent", "true")]
    public async Task FirstPartyMapOutputRemainsCharacterized(
        FirstPartyMapCharacterization expected)
    {
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
        ContentEnvironment.Open(GameDataDirectory(), "AMHE1");
        MapProject project = MapProjectIO.Load(Path.Combine(repository, expected.ProjectPath));
        MapBuildResult result = new MapCompiler().Compile(project, new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "characterization"),
            BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash,
            Force = true
        }, CancellationToken.None);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(expected.BuildFingerprint, result.BuildFingerprint);
        Assert.Equal(expected.ContentHash, result.ContentIdentity!.ContentHash);
        Assert.Equal(expected.ModelHash, Hash("Model.bin"));
        Assert.Equal(expected.AnimationHash, Hash("Anim.bin"));
        Assert.Equal(expected.CollisionHash, Hash("Collision.bin"));
        Assert.Equal(expected.EntityHash, Hash("Ent.bin"));
        Assert.Equal(expected.NodeHash, Hash("Node.bin"));
        MapBuildStatistics statistics = result.Statistics!;
        Assert.Equal(expected.RenderTriangles, statistics.RenderTriangles);
        Assert.Equal(expected.RenderVertices, statistics.RenderVertices);
        Assert.Equal(expected.Materials, statistics.Materials);
        Assert.Equal(expected.CollisionFaces, statistics.CollisionFaces);
        Assert.Equal(expected.CollisionPoints, statistics.CollisionPoints);
        Assert.Equal(expected.CollisionPlanes, statistics.CollisionPlanes);
        Assert.Equal(expected.GridReferences, statistics.CollisionGridReferences);
        Assert.Equal(expected.Entities, statistics.Entities);
        Assert.Equal(expected.Spawns, statistics.Spawns);

        string Hash(string name) => MapJson.Sha256(
            File.ReadAllBytes(Path.Combine(result.CachePath!, name)));
    }

    [Fact]
    public void ModeSpawnMaterialAndFixedPointValidationProducesStructuredCodes()
    {
        MapProject project = Project();
        project.Map.Spawns.Clear();
        project.Map.Brushes[0].Material = 4;
        project.Map.Brushes[0].Max[0] = 10000;

        var diagnostics = new MapValidator().ValidateProject(project);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "MAP-MODE-002");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "MAP-MAT-001");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "MAP-GEO-005");
        Assert.All(diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Message)));
    }

    [Fact]
    public void NativeSpawnValidationDetectsSolidIntersectionsAndOverlap()
    {
        MapProject project = NativeProject();
        project.Authoring!.Entities[0].Transform.Position = [0, -0.5f, 0];
        project.Authoring.Entities[1].Transform.Position = [0, -0.5f, 0];

        var diagnostics = new MapValidator().ValidateProject(project);

        Assert.Contains(diagnostics, value => value.Code == "MAP-SPAWN-003");
        Assert.Contains(diagnostics, value => value.Code == "MAP-SPAWN-004");
    }

    [Fact]
    public void NativeMaterialValidationRejectsAmbiguousSourceAndUnknownTerrain()
    {
        MapProject project = NativeProject();
        MapAuthoringMaterial material = project.Authoring!.Materials[0];
        material.CustomImage = "also-custom.png";
        material.Terrain = "MysteryFloor";

        var diagnostics = new MapValidator().ValidateProject(project);

        Assert.Contains(diagnostics, value => value.Code == "MAP-MAT-004");
        Assert.Contains(diagnostics, value => value.Code == "MAP-MAT-006");
    }

    [Fact]
    public void UnsupportedFutureEntityKindsFailValidation()
    {
        MapProject project = NativeProject();
        project.Authoring!.Entities.Add(new MapEntityDefinition
        {
            Id = "door.future",
            Kind = MapEntityKind.Door
        });

        var diagnostics = new MapValidator().ValidateProject(project);

        Assert.Contains(diagnostics, value => value.Code == "MAP-ENT-005"
            && value.ObjectId == "door.future");
    }

    [Fact]
    public void CollisionPackingIsByteDeterministicAndReportsGridMetrics()
    {
        var face = new CollisionDataEditor
        {
            Plane = new Vector4(Vector3.UnitY, 0),
            LayerMask = 5,
            Terrain = Terrain.Metal
        };
        face.Points.AddRange([new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 0, 4)]);
        byte[] first = MapCollisionPacker.Pack([face], out MapCollisionStatistics firstStatistics);
        byte[] second = MapCollisionPacker.Pack([face], out MapCollisionStatistics secondStatistics);

        Assert.Equal(first, second);
        Assert.Equal(firstStatistics, secondStatistics);
        Assert.Equal(1, firstStatistics.Faces);
        Assert.Equal(3, firstStatistics.DistinctPoints);
        Assert.Equal(1, firstStatistics.Planes);
        Assert.True(firstStatistics.GridReferences > 0);
    }

    [Fact]
    public void CaptureBountyAndNodesCompileToTypedRuntimeEntities()
    {
        MapProject project = NativeProject();
        project.SupportedModes = [MapMode.Capture, MapMode.Bounty, MapMode.Nodes];
        project.Authoring!.Entities.Clear();
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "spawn.team0", Kind = MapEntityKind.TeamSpawn, Team = 0,
            Transform = new MapTransform { Position = [-2, 1, 0] }
        });
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "spawn.team1", Kind = MapEntityKind.TeamSpawn, Team = 1,
            Transform = new MapTransform { Position = [2, 1, 0] }
        });
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "base.team0", Kind = MapEntityKind.CaptureBase, Team = 0,
            Transform = new MapTransform { Position = [-3, 1, 0] }, Size = [2, 2, 2]
        });
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "base.team1", Kind = MapEntityKind.CaptureBase, Team = 1,
            Transform = new MapTransform { Position = [3, 1, 0] }, Size = [2, 2, 2]
        });
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "base.bounty", Kind = MapEntityKind.BountyBase,
            Transform = new MapTransform { Position = [0, 1, -2] }, Size = [2, 2, 2]
        });
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "node.center", Kind = MapEntityKind.NodeObjective,
            Transform = new MapTransform { Position = [0, 1, 0] }, Size = [4, 2, 4]
        });

        Assert.DoesNotContain(new MapValidator().ValidateProject(project),
            diagnostic => diagnostic.Severity == MapDiagnosticSeverity.Error);
        MapBuildScene scene = new NativeMapProjectImporter().Import(project, verbose: false);
        Assert.Equal(9, scene.Entities.Count);
        Assert.Equal(2, scene.Entities.OfType<PlayerSpawnEntityEditor>().Count());
        Assert.Equal(3, scene.Entities.OfType<OctolithFlagEntityEditor>().Count());
        Assert.Equal(3, scene.Entities.OfType<FlagBaseEntityEditor>().Count());
        Assert.Single(scene.Entities.OfType<NodeDefenseEntityEditor>());
        Assert.NotEmpty(Repack.PackEntities(scene.Entities));
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public async Task CompileSameMapTwiceUsesValidatedContentAddressedCache()
    {
        string gameData = GameDataDirectory();
        Assert.True(File.Exists(Path.Combine(gameData, "_bin", "arm9.bin")),
            "The repository AMHE1 fixture is required for map characterization.");
        ContentEnvironment.Open(gameData, "AMHE1");
        MapProject project = Project();
        var options = new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash
        };
        var compiler = new MapCompiler();

        MapBuildResult first = compiler.Compile(project, options, CancellationToken.None);
        MapBuildResult second = compiler.Compile(project, options, CancellationToken.None);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.False(first.CacheHit);
        Assert.True(second.Success, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.True(second.CacheHit);
        Assert.Equal(first.BuildFingerprint, second.BuildFingerprint);
        Assert.Equal(first.ContentIdentity, second.ContentIdentity);
        Assert.Equal(first.Statistics!.RenderTriangles, second.Statistics!.RenderTriangles);
        Assert.Equal(first.Statistics.CollisionGridReferences, second.Statistics.CollisionGridReferences);
        Assert.Equal(first.Statistics.WorldMin, second.Statistics.WorldMin);
        Assert.Equal(first.Statistics.WorldMax, second.Statistics.WorldMax);
        Assert.Contains(first.Timings, timing => timing.Stage == "Build Render Geometry");
        Assert.Contains(first.Timings, timing => timing.Stage == "Build Collision");
        Assert.Contains(first.Timings, timing => timing.Stage == "Build Entities");
        Assert.Contains(first.Timings, timing => timing.Stage == "Build Node Data");
        Assert.NotNull(first.CachePath);
        foreach (string file in new[] { "Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin", "build.json" })
            Assert.True(File.Exists(Path.Combine(first.CachePath!, file)), file);

        byte[] expectedModel = File.ReadAllBytes(Path.Combine(first.CachePath!, "Model.bin"));
        MatchContentSnapshot mounted = ContentEnvironment.MountMap(first.ContentIdentity!,
            first.BuildFingerprint, first.CachePath!, project.Map, "gameplay-fixture");
        try
        {
            string modelPath = Path.Combine(CustomRooms.ArchiveDirectory(project.Map),
                "platform fixture_Model.bin");
            Assert.Equal(expectedModel, ContentEnvironment.ReadBytes(modelPath));
            File.WriteAllBytes(Path.Combine(first.CachePath!, "Model.bin"), new byte[] { 0 });
            Assert.Equal(expectedModel, ContentEnvironment.ReadBytes(modelPath));
            Assert.Equal(first.ContentIdentity, mounted.MapIdentity);
            Assert.Equal(64, mounted.MatchContentIdentity.Length);
        }
        finally
        {
            ContentEnvironment.UnmountMap();
        }
    }

    [Fact]
    public void ImportedSourceIdentityIsCapturedBeforeQ3MutationAndPackagedIdentityIsPreserved()
    {
        (string source, string textures) = CreateImportedFixture();
        MapProject imported = ImportedProject(source, textures);
        string sourceHash = MapProjectContentHasher.Compute(imported);
        var expected = new MapContentIdentity(imported.Identity, sourceHash);
        string cache = Path.Combine(_directory, "imported-cache");
        var compiler = new MapCompiler();

        MapBuildResult first = compiler.Compile(imported, new MapBuildOptions
        {
            CacheDirectory = cache,
            BaseContentIdentity = "unconfigured",
            Force = true
        }, CancellationToken.None);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.False(first.CacheHit);
        Assert.Equal(expected, first.ContentIdentity);
        Assert.Equal(2, imported.Map.Spawns.Count);
        Assert.NotEqual(sourceHash, MapProjectContentHasher.Compute(imported));

        // The importer mutates its detached legacy definition while deriving
        // Quake spawn entities. A fresh project must still hit the same cache
        // and return the original source identity, not that generated state.
        MapProject fresh = ImportedProject(source, textures);
        MapBuildResult cacheHit = compiler.Compile(fresh, new MapBuildOptions
        {
            CacheDirectory = cache,
            BaseContentIdentity = "unconfigured"
        }, CancellationToken.None);

        Assert.True(cacheHit.Success, string.Join(Environment.NewLine, cacheHit.Diagnostics));
        Assert.True(cacheHit.CacheHit);
        Assert.Equal(expected, cacheHit.ContentIdentity);

        // A packaged manifest is authoritative even though the package recipe
        // may be imported and mutated during compilation.
        MapProject packaged = ImportedProject(source, textures);
        var declared = new MapContentIdentity(
            new MapIdentity("community.q3-packaged", new MapVersion(9, 0, 0)),
            new string('c', 64));
        packaged.DeclaredContentIdentity = declared;
        MapBuildResult packagedResult = compiler.Compile(packaged, new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "packaged-cache"),
            BaseContentIdentity = "unconfigured",
            Force = true
        }, CancellationToken.None);

        Assert.True(packagedResult.Success,
            string.Join(Environment.NewLine, packagedResult.Diagnostics));
        Assert.Equal(declared, packagedResult.ContentIdentity);
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public async Task ConcurrentCompilersAtomicallyPublishOneValidCache()
    {
        string gameData = GameDataDirectory();
        ContentEnvironment.Open(gameData, "AMHE1");
        MapProject project = NativeProject();
        string cache = Path.Combine(_directory, "concurrent-cache");
        var options = new MapBuildOptions
        {
            CacheDirectory = cache,
            BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash,
            Force = true
        };

        MapBuildResult[] results = await Task.WhenAll(
            Task.Run(() => new MapCompiler().Compile(project, options, CancellationToken.None)),
            Task.Run(() => new MapCompiler().Compile(project, options, CancellationToken.None)));

        Assert.All(results, result => Assert.True(result.Success,
            string.Join(Environment.NewLine, result.Diagnostics)));
        Assert.Equal(results[0].BuildFingerprint, results[1].BuildFingerprint);
        Assert.True(MapCompiler.TryReadValidCache(results[0].CachePath!,
            results[0].BuildFingerprint, out _));
        string[] directories = Directory.EnumerateDirectories(cache).ToArray();
        Assert.Single(directories,
            path => !Path.GetFileName(path).StartsWith('.'));
        Assert.DoesNotContain(directories,
            path => Path.GetFileName(path).StartsWith(".build-", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeConvexBrushesProduceUnifiedRenderCollisionAndTypedEntities()
    {
        MapProject project = NativeProject();

        MapBuildScene scene = new NativeMapProjectImporter().Import(project, verbose: false);

        Assert.Equal(6, scene.Faces.Count);
        Assert.Equal(scene.Faces, scene.Solid);
        Assert.Equal(4, scene.Definition.Spawns.Count);
        Assert.All(scene.Faces, face => Assert.True(face.Points.Length >= 3));
        Assert.Contains(scene.Entities, entity => entity is PlayerSpawnEntityEditor);
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public async Task NativeProjectCompilesByteDeterministicallyAndRoundTripsThroughV2Package()
    {
        string gameData = GameDataDirectory();
        ContentEnvironment.Open(gameData, "AMHE1");
        MapProject project = NativeProject();
        var compiler = new MapCompiler();
        string firstCache = Path.Combine(_directory, "native-a");
        string secondCache = Path.Combine(_directory, "native-b");
        string baseIdentity = ContentEnvironment.GetContentIdentity().ContentHash;

        MapBuildResult first = compiler.Compile(project, new MapBuildOptions
        {
            CacheDirectory = firstCache, BaseContentIdentity = baseIdentity, Force = true
        }, CancellationToken.None);
        MapBuildResult second = compiler.Compile(project, new MapBuildOptions
        {
            CacheDirectory = secondCache, BaseContentIdentity = baseIdentity, Force = true
        }, CancellationToken.None);

        Assert.True(first.Success, string.Join(Environment.NewLine, first.Diagnostics));
        Assert.True(second.Success, string.Join(Environment.NewLine, second.Diagnostics));
        Assert.Equal(first.BuildFingerprint, second.BuildFingerprint);
        foreach (string file in new[] { "Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin" })
            Assert.Equal(File.ReadAllBytes(Path.Combine(first.CachePath!, file)),
                File.ReadAllBytes(Path.Combine(second.CachePath!, file)));

        string source = Path.Combine(_directory, "native.project.json");
        MapProjectIO.Save(project, source);
        string bundleA = Path.Combine(_directory, "native-a.fpmap");
        string bundleB = Path.Combine(_directory, "native-b.fpmap");
        MapBundleWriteResult writeA = MapPackageBuilder.Cook(project, source, bundleA);
        MapBundleWriteResult writeB = MapPackageBuilder.Cook(project, source, bundleB);
        Assert.Equal(File.ReadAllBytes(bundleA), File.ReadAllBytes(bundleB));
        Assert.Equal(writeA.ContentIdentity, writeB.ContentIdentity);
        MapBundleValidationResult validation = new MapBundleValidator().Validate(bundleA);
        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Diagnostics));
        MapProject loaded = MapProjectIO.Load(bundleA);
        Assert.NotNull(loaded.Authoring);
        Assert.Equal(project.Authoring!.Brushes.Count, loaded.Authoring!.Brushes.Count);
        Assert.Equal(writeA.ContentIdentity, loaded.DeclaredContentIdentity);
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public async Task NativeCustomImagesAndDamageVolumesCompileAndTravelInsidePackage()
    {
        string gameData = GameDataDirectory();
        ContentEnvironment.Open(gameData, "AMHE1");
        MapImageDecoding.Decoder = global::MphRead.Imaging.StbImageDecoder.Decode;
        MapProject project = NativeProject();
        string sourceImage = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../Logo.png"));
        string localImage = Path.Combine(_directory, "custom.png");
        File.Copy(sourceImage, localImage);
        MapAuthoringMaterial material = project.Authoring!.Materials[0];
        material.SourceMaterial = null;
        material.CustomImage = "custom.png";
        project.Authoring.Entities.Add(new MapEntityDefinition
        {
            Id = "volume.damage", Kind = MapEntityKind.DamageVolume,
            Transform = new MapTransform { Position = [0, 1, 0] },
            Size = [2, 2, 2], DamagePerTick = 4
        });
        string source = Path.Combine(_directory, "custom.project.json");
        MapProjectIO.Save(project, source);
        string bundle = Path.Combine(_directory, "custom.fpmap");

        MapBundleWriteResult written = MapPackageBuilder.Cook(project, source, bundle);
        MapBundleReadResult package = new MapBundleReader().Read(bundle);
        MapProject loaded = MapProjectIO.Load(bundle);
        MapBuildResult result = new MapCompiler().Compile(loaded, new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "custom-cache"),
            BaseContentIdentity = ContentEnvironment.GetContentIdentity().ContentHash
        }, CancellationToken.None);

        Assert.Contains(package.Manifest.Files, file => file.Role == MapFileRole.Textures);
        Assert.Equal(written.ContentIdentity, loaded.DeclaredContentIdentity);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(5, result.Statistics!.Entities);
        Assert.Equal(1, result.Statistics.Textures);
    }

    [Fact]
    public void ProjectContentIdentityIncludesExternalDependencyBytesButNotTimestamps()
    {
        MapProject project = NativeProject();
        string source = Path.Combine(_directory, "identity.project.json");
        string image = Path.Combine(_directory, "identity.png");
        project.Authoring!.Materials[0].SourceMaterial = null;
        project.Authoring.Materials[0].CustomImage = "identity.png";
        File.WriteAllBytes(image, [1, 2, 3, 4]);
        MapProjectIO.Save(project, source);

        string first = MapProjectContentHasher.Compute(project);
        File.SetLastWriteTimeUtc(image, DateTime.UtcNow.AddDays(-1));
        string timestampOnly = MapProjectContentHasher.Compute(project);
        File.WriteAllBytes(image, [1, 2, 3, 5]);
        string changed = MapProjectContentHasher.Compute(project);

        Assert.Equal(first, timestampOnly);
        Assert.NotEqual(first, changed);
    }

    [Fact]
    public void BuildFingerprintIncludesOnlyRelevantBaseContentIdentity()
    {
        MapProject project = NativeProject();
        string source = Path.Combine(_directory, "custom-only.project.json");
        string image = Path.Combine(_directory, "custom-only.png");
        File.WriteAllBytes(image, [1, 2, 3, 4]);
        project.Authoring!.Materials[0].SourceMaterial = null;
        project.Authoring.Materials[0].CustomImage = Path.GetFileName(image);
        MapProjectIO.Save(project, source);

        string first = MapBuildFingerprint.Compute(project, new string('a', 64));
        string unrelatedBase = MapBuildFingerprint.Compute(project, new string('b', 64));
        project.Authoring.Materials[0].CustomImage = null;
        project.Authoring.Materials[0].SourceMaterial = 1;
        string borrowedFirst = MapBuildFingerprint.Compute(project, new string('a', 64));
        string borrowedSecond = MapBuildFingerprint.Compute(project, new string('b', 64));

        Assert.Equal(first, unrelatedBase);
        Assert.NotEqual(borrowedFirst, borrowedSecond);
    }

    [Fact]
    public void DependencyAnalysisIsTheBaseContentAuthority()
    {
        MapProject project = NativeProject();
        MapDependencyAnalysis borrowed = MapDependencyAnalyzer.Analyze(project);
        project.Authoring!.Materials[0].SourceMaterial = null;
        project.Authoring.Materials[0].CustomImage = "custom.png";
        string image = Path.Combine(_directory, "custom.png");
        File.WriteAllBytes(image, [1, 2, 3]);
        MapProjectIO.Save(project, Path.Combine(_directory, "dependency.project.json"));

        MapDependencyAnalysis custom = MapDependencyAnalyzer.Analyze(project);

        Assert.True(borrowed.RequiresBaseContent);
        Assert.Contains(borrowed.Dependencies, value => value.Kind == MapDependencyKind.BaseContent);
        Assert.False(custom.RequiresBaseContent);
        Assert.True(custom.RequiresExternalTextures);
        Assert.Contains(custom.Dependencies, value => value.Kind == MapDependencyKind.CustomTexture
            && value.Hash == MapJson.Sha256(File.ReadAllBytes(image)));
    }

    [Fact]
    public void FullyCustomNativeMapCompilesWithoutOpeningBaseContent()
    {
        MapImageDecoding.Decoder = global::MphRead.Imaging.StbImageDecoder.Decode;
        MapProject project = NativeProject();
        string image = Path.Combine(_directory, "self-contained.png");
        File.Copy(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../Logo.png")), image);
        project.Authoring!.Materials[0].SourceMaterial = null;
        project.Authoring.Materials[0].CustomImage = Path.GetFileName(image);
        MapProjectIO.Save(project, Path.Combine(_directory, "self-contained.project.json"));

        MapBuildResult result = new MapCompiler().Compile(project, new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "self-contained-cache"),
            BaseContentIdentity = "unconfigured"
        }, CancellationToken.None);

        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        Assert.Equal(1, result.Statistics!.Textures);
    }

    [Fact]
    public async Task BuildSchedulerSingleFlightsAndCallerCancellationDoesNotCancelSharedBuild()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        MapBuildResult expected = new(true, false, new string('a', 64), _directory,
            NativeProject().Identity is { } identity
                ? new MapContentIdentity(identity, new string('b', 64)) : null,
            [], new MapBuildStatistics(), []);
        using var scheduler = new MapBuildScheduler(2, (_, _, token) =>
        {
            Interlocked.Increment(ref calls);
            entered.Set();
            release.Wait(token);
            return expected;
        });
        MapProject project = NativeProject();
        var options = new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "scheduled"),
            BaseContentIdentity = "base"
        };

        Task<MapBuildResult> first = scheduler.BuildAsync(project, options);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        using var cancel = new CancellationTokenSource();
        Task<MapBuildResult> second = scheduler.BuildAsync(project, options, cancel.Token);
        Assert.True(SpinWait.SpinUntil(() => scheduler.ActiveWaiters == 2,
            TimeSpan.FromSeconds(5)));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        release.Set();

        Assert.Same(expected, await first);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public async Task BuildSchedulerFreezesCompilerInputsBeforeConcurrentEditorChanges()
    {
        ContentEnvironment.Open(GameDataDirectory(), "AMHE1");
        MapProject project = NativeProject();
        string baseIdentity = ContentEnvironment.GetContentIdentity().ContentHash;
        MapBuildResult baseline = new MapCompiler().Compile(project, new MapBuildOptions
        {
            CacheDirectory = Path.Combine(_directory, "snapshot-baseline"),
            BaseContentIdentity = baseIdentity,
            Force = true
        }, CancellationToken.None);
        Assert.True(baseline.Success, string.Join(Environment.NewLine, baseline.Diagnostics));

        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var scheduler = new MapBuildScheduler(1, (snapshot, options, token) =>
        {
            entered.Set();
            release.Wait(token);
            return new MapCompiler().Compile(snapshot, options, token);
        });
        string scheduledCache = Path.Combine(_directory, "snapshot-scheduled");
        Task<MapBuildResult> scheduledTask = scheduler.BuildAsync(project, new MapBuildOptions
        {
            CacheDirectory = scheduledCache,
            BaseContentIdentity = baseIdentity,
            Force = true
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));

        // These edits happen while the compiler delegate is paused. They must
        // affect only the next editor build, never the already admitted one.
        project.Authoring!.Brushes[0].Transform.Position[0] = 99;
        project.Authoring.Entities[0].Id = "spawn.edited-after-submit";
        project.Map.Name = "EDITED AFTER SUBMIT";
        release.Set();

        MapBuildResult scheduled = await scheduledTask;
        Assert.True(scheduled.Success, string.Join(Environment.NewLine, scheduled.Diagnostics));
        Assert.Equal(baseline.BuildFingerprint, scheduled.BuildFingerprint);
        Assert.NotNull(baseline.Statistics);
        Assert.NotNull(scheduled.Statistics);
        Assert.Equal(baseline.Statistics!.RenderTriangles, scheduled.Statistics!.RenderTriangles);
        Assert.Equal(baseline.Statistics.RenderVertices, scheduled.Statistics.RenderVertices);
        Assert.Equal(baseline.Statistics.Materials, scheduled.Statistics.Materials);
        Assert.Equal(baseline.Statistics.CollisionFaces, scheduled.Statistics.CollisionFaces);
        Assert.Equal(baseline.Statistics.CollisionPoints, scheduled.Statistics.CollisionPoints);
        Assert.Equal(baseline.Statistics.CollisionPlanes, scheduled.Statistics.CollisionPlanes);
        Assert.Equal(baseline.Statistics.CollisionGridReferences, scheduled.Statistics.CollisionGridReferences);
        Assert.Equal(baseline.Statistics.Entities, scheduled.Statistics.Entities);
        Assert.Equal(baseline.Statistics.Spawns, scheduled.Statistics.Spawns);
        Assert.Equal(baseline.Statistics.WorldMin, scheduled.Statistics.WorldMin);
        Assert.Equal(baseline.Statistics.WorldMax, scheduled.Statistics.WorldMax);
        foreach (string name in new[] { "Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin" })
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(baseline.CachePath!, name)),
                File.ReadAllBytes(Path.Combine(scheduled.CachePath!, name)));
        }
    }

    [Fact]
    public void CancelledCompilerPublishLeavesNoPartialCache()
    {
        MapImageDecoding.Decoder = global::MphRead.Imaging.StbImageDecoder.Decode;
        MapProject project = NativeProject();
        string image = Path.Combine(_directory, "cancel.png");
        File.Copy(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../../../Logo.png")), image);
        project.Authoring!.Materials[0].SourceMaterial = null;
        project.Authoring.Materials[0].CustomImage = Path.GetFileName(image);
        MapProjectIO.Save(project, Path.Combine(_directory, "cancel.project.json"));
        string cache = Path.Combine(_directory, "cancel-cache");
        using var cancellation = new CancellationTokenSource();
        var progress = new InlineProgress<MapBuildProgress>(value =>
        {
            if (value.Stage == "Publishing cache") cancellation.Cancel();
        });

        Assert.Throws<OperationCanceledException>(() => new MapCompiler().Compile(project,
            new MapBuildOptions
            {
                CacheDirectory = cache,
                BaseContentIdentity = "unconfigured",
                Progress = progress
            }, cancellation.Token));

        Assert.False(Directory.Exists(cache) && Directory.EnumerateDirectories(cache).Any());
    }

    [Fact]
    public void CompilerFailuresCarryStableBuildStateClassification()
    {
        MapProject unsupported = NativeProject();
        unsupported.Authoring!.Entities.Add(new MapEntityDefinition
        {
            Id = "door.future",
            Kind = MapEntityKind.Door
        });
        MapBuildResult unsupportedResult = new MapCompiler().Compile(unsupported,
            new MapBuildOptions { CacheDirectory = Path.Combine(_directory, "unsupported") },
            CancellationToken.None);

        var missing = new MapProject
        {
            StableId = "community.missing-build-source",
            Metadata = new MapProjectMetadata { Name = "Missing", Author = "Tests" },
            SupportedModes = [MapMode.Battle],
            Map = new MapDefinition
            {
                Name = "MISSING",
                Import = new MapImport { Source = "missing.bsp" }
            }
        };
        MapProjectIO.Save(missing, Path.Combine(_directory, "missing.project.json"));
        MapBuildResult missingResult = new MapCompiler().Compile(missing,
            new MapBuildOptions { CacheDirectory = Path.Combine(_directory, "missing") },
            CancellationToken.None);

        Assert.Equal(CompilationFailureKind.UnsupportedFeature, unsupportedResult.FailureKind);
        Assert.Equal(MapBuildState.Unsupported, unsupportedResult.FailureKind.ToBuildState());
        Assert.Equal(CompilationFailureKind.MissingDependency, missingResult.FailureKind);
        Assert.Equal(MapBuildState.MissingDependency, missingResult.FailureKind.ToBuildState());
        Assert.Equal(MapBuildState.NeedsBuild,
            CompilationFailureKind.IOFailure.ToBuildState());
    }

    [Fact]
    [Trait("RequiresGameContent", "true")]
    public async Task AmbientMatchMountsAreIsolatedAcrossConcurrentExecutionContexts()
    {
        string gameData = GameDataDirectory();
        ContentEnvironment.Open(gameData, "AMHE1");
        var definition = new MapDefinition { Name = "SCOPED MAP" };
        string logical = Path.Combine(CustomRooms.ArchiveDirectory(definition),
            "scoped map_Model.bin");
        MatchContentSnapshot Make(string suffix, byte marker)
        {
            string directory = Path.Combine(_directory, suffix);
            Directory.CreateDirectory(directory);
            foreach (string name in new[] { "Model.bin", "Anim.bin", "Collision.bin", "Ent.bin", "Node.bin" })
                File.WriteAllBytes(Path.Combine(directory, name), [marker]);
            var identity = new MapContentIdentity(
                new MapIdentity("tests.scoped-" + suffix, new MapVersion(1, 0, 0)),
                new string(marker == 1 ? '1' : '2', 64));
            return ContentEnvironment.CreateMapSnapshot(identity, new string(marker == 1 ? 'a' : 'b', 64),
                directory, definition, "gameplay");
        }
        MatchContentSnapshot first = Make("one", 1);
        MatchContentSnapshot second = Make("two", 2);
        using var gate = new Barrier(2);

        Task Verify(MatchContentSnapshot snapshot, byte expected) => Task.Run(() =>
        {
            using IDisposable scope = ContentEnvironment.UseMatchContent(snapshot);
            gate.SignalAndWait();
            for (int index = 0; index < 100; index++)
                Assert.Equal(new byte[] { expected }, ContentEnvironment.ReadBytes(logical));
        });

        await Task.WhenAll(Verify(first, 1), Verify(second, 2));
        Assert.Null(ContentEnvironment.CurrentMatchContent);
    }

    private static MapProject Project()
    {
        var definition = new MapDefinition
        {
            Name = "PLATFORM FIXTURE",
            InGameName = "Platform Fixture",
            TextureSource = "MP3 PROVING GROUND",
            ScaleFactor = 4,
            KillHeight = -10
        };
        definition.Materials.Add(new MapMaterial { Name = "fixture", SourceMaterial = 1 });
        definition.Brushes.Add(new MapBrush { Min = [-4, -1, -4], Max = [4, 0, 4], Material = 0 });
        definition.Spawns.Add(new MapSpawn { Position = [-2, 0.1f, 0], Yaw = 90 });
        definition.Spawns.Add(new MapSpawn { Position = [2, 0.1f, 0], Yaw = -90 });
        return new MapProject
        {
            StableId = "tests.platform-fixture",
            Version = new MapVersion(1, 0, 0),
            Metadata = new MapProjectMetadata { Name = "Platform Fixture", Author = "Tests" },
            SupportedModes = [MapMode.Battle, MapMode.Survival],
            Map = definition
        };
    }

    private static MapProject ImportedProject(string source, string textures)
    {
        string baseDirectory = Path.GetDirectoryName(source)!;
        var definition = new MapDefinition
        {
            Name = "Q3 IDENTITY",
            InGameName = "Q3 Identity",
            ScaleFactor = 4,
            KillHeight = -10,
            Import = new MapImport
            {
                Source = source,
                MapName = "identity",
                Textures = textures,
                BaseDirectory = baseDirectory,
                UnitsPerUnit = 1,
                KeepSpawns = true,
                KeepSky = false,
                KeepClip = true,
                PatchLevel = 1
            },
            BaseDirectory = baseDirectory
        };
        return MapProject.FromLegacy(definition, "community.q3-identity");
    }

    private (string Source, string Textures) CreateImportedFixture()
    {
        string source = Path.Combine(_directory, "identity.pk3");
        string textures = Path.Combine(_directory, "identity.fptx");
        using (FileStream stream = File.Create(source))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("maps/identity.bsp");
            using Stream target = entry.Open();
            byte[] bsp = CreateIdentityBsp();
            target.Write(bsp);
        }
        File.WriteAllBytes(textures, CreateIdentityTexturePack());
        return (source, textures);
    }

    private static byte[] CreateIdentityTexturePack()
    {
        byte[] name = Encoding.UTF8.GetBytes("textures/identity");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("FPTX"));
        writer.Write((ushort)1);
        writer.Write((ushort)1);
        writer.Write((ushort)0); // BSP source texture index
        writer.Write((ushort)1); // width
        writer.Write((ushort)1); // height
        writer.Write((ushort)1); // palette entries
        writer.Write((ushort)name.Length);
        writer.Write(name);
        writer.Write((ushort)0x7FFF);
        writer.Write((byte)0);
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] CreateIdentityBsp()
    {
        byte[][] lumps = new byte[17][];
        lumps[0] = Encoding.ASCII.GetBytes(
            "{\n\"classname\" \"worldspawn\"\n}\n"
            + "{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"0 0 32\"\n}\n"
            + "{\n\"classname\" \"info_player_deathmatch\"\n\"origin\" \"8 0 32\"\n}\n");
        lumps[1] = IdentityTextureLump();
        lumps[10] = IdentityVertexLump();
        lumps[13] = IdentityFaceLump();

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("IBSP"));
        writer.Write(46);
        for (int i = 0; i < 17; i++)
        {
            writer.Write(0);
            writer.Write(0);
        }

        var locations = new (int Offset, int Length)[17];
        for (int i = 0; i < lumps.Length; i++)
        {
            if (lumps[i] is not { Length: > 0 }) continue;
            locations[i] = ((int)stream.Position, lumps[i].Length);
            writer.Write(lumps[i]);
        }
        for (int i = 0; i < locations.Length; i++)
        {
            stream.Position = 8 + i * 8;
            writer.Write(locations[i].Offset);
            writer.Write(locations[i].Length);
        }
        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] IdentityTextureLump()
    {
        byte[] lump = new byte[72];
        Encoding.ASCII.GetBytes("textures/identity").CopyTo(lump, 0);
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(68, 4), Q3Bsp.ContentsSolid);
        return lump;
    }

    private static byte[] IdentityVertexLump()
    {
        byte[] lump = new byte[9 * 44];
        for (int row = 0; row < 3; row++)
        for (int column = 0; column < 3; column++)
        {
            int offset = (row * 3 + column) * 44;
            WriteSingle(lump, offset, column - 1);
            WriteSingle(lump, offset + 4, row - 1);
            WriteSingle(lump, offset + 8, 0);
            WriteSingle(lump, offset + 12, column / 2f);
            WriteSingle(lump, offset + 16, row / 2f);
            WriteSingle(lump, offset + 28, 0);
            WriteSingle(lump, offset + 32, 0);
            WriteSingle(lump, offset + 36, 1);
            lump[offset + 40] = 255;
            lump[offset + 41] = 255;
            lump[offset + 42] = 255;
            lump[offset + 43] = 255;
        }
        return lump;
    }

    private static byte[] IdentityFaceLump()
    {
        byte[] lump = new byte[104];
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(0, 4), 0); // texture
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(8, 4), 2); // patch
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(12, 4), 0); // vertex
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(16, 4), 9); // vertex count
        WriteSingle(lump, 84, 0);
        WriteSingle(lump, 88, 0);
        WriteSingle(lump, 92, 1);
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(96, 4), 3);
        BinaryPrimitives.WriteInt32LittleEndian(lump.AsSpan(100, 4), 3);
        return lump;
    }

    private static void WriteSingle(byte[] destination, int offset, float value)
        => BinaryPrimitives.WriteInt32LittleEndian(destination.AsSpan(offset, 4),
            BitConverter.SingleToInt32Bits(value));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static MapProject NativeProject()
    {
        var project = new MapProject
        {
            StableId = "tests.native-fixture",
            Metadata = new MapProjectMetadata { Name = "Native Fixture", Author = "Tests" },
            Map = new MapDefinition
            {
                Name = "NATIVE FIXTURE", InGameName = "Native Fixture",
                TextureSource = "MP3 PROVING GROUND", ScaleFactor = 4, KillHeight = -10
            },
            Authoring = new MapAuthoringScene()
        };
        project.Authoring.Materials.Add(new MapAuthoringMaterial
        {
            Id = "material.default", Name = "Default",
            SourceRoom = "MP3 PROVING GROUND", SourceMaterial = 1
        });
        project.Authoring.Brushes.Add(ConvexBrushFactory.Box("brush.floor",
            "material.default", new Vector3(8, 1, 8)));
        for (int index = 0; index < 4; index++)
        {
            project.Authoring.Entities.Add(new MapEntityDefinition
            {
                Id = $"spawn.{index + 1}", Kind = MapEntityKind.PlayerSpawn,
                Transform = new MapTransform { Position = [index - 1.5f, 1, 0] }
            });
        }
        return project;
    }

    private static string GameDataDirectory()
        => Path.GetFullPath(Environment.GetEnvironmentVariable("GAME_DATA_DIRECTORY")
            ?? Path.Combine(AppContext.BaseDirectory, "../../../../../AMHE1"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
