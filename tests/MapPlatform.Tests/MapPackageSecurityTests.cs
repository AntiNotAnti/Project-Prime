using System.IO.Compression;
using System.Text;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace ProjectPrime.MapPlatform.Tests;

public sealed class MapPackageSecurityTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(),
        "project-prime-map-package-tests-" + Guid.NewGuid().ToString("N"));

    public MapPackageSecurityTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task V2BundleIsByteDeterministicAndVerifiesBothIdentities()
    {
        string first = Path.Combine(_directory, "first.fpmap");
        string second = Path.Combine(_directory, "second.fpmap");
        MapManifest manifest1 = Manifest();
        MapManifest manifest2 = Manifest();
        MapPackageFile[] files = Files();

        MapBundleWriteResult firstResult = await new MapBundleWriter().WriteAsync(first, manifest1, files);
        MapBundleWriteResult secondResult = await new MapBundleWriter().WriteAsync(second, manifest2, files);
        MapBundleReadResult read = new MapBundleReader(new() { AllowLegacyV1 = false }).Read(first);

        Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
        Assert.Equal(firstResult.ArtifactHash, secondResult.ArtifactHash);
        Assert.Equal(firstResult.ContentIdentity, secondResult.ContentIdentity);
        Assert.Equal(firstResult.ArtifactHash, read.ArtifactHash);
        Assert.Equal(firstResult.ContentIdentity.ContentHash, read.Manifest.ContentHash);
        Assert.Equal("community.fixture", read.Manifest.StableId);
        Assert.Equal("{}", Encoding.UTF8.GetString(read.ReadDeclaredFile("map.json")));
    }

    [Theory]
    [InlineData("../evil.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("C:/drive.bin")]
    [InlineData("nested\\ambiguous.bin")]
    [InlineData("nested//empty.bin")]
    [InlineData("./relative.bin")]
    [InlineData("native.dll")]
    [InlineData("script.sh")]
    public void UnsafeOrExecutablePathsAreRejected(string path)
    {
        MapPackageException exception = Assert.Throws<MapPackageException>(() => MapBundlePath.Canonicalize(path));
        Assert.StartsWith("MAP-PKG-", exception.Code);
    }

    [Fact]
    public void MissingManifestIsRejectedWhenLegacyMigrationIsDisabled()
    {
        string path = RawBundle(("map.json", "{}", null));
        MapPackageException exception = Assert.Throws<MapPackageException>(() =>
            new MapBundleReader(new() { AllowLegacyV1 = false }).Read(path));
        Assert.Equal("MAP-PKG-004", exception.Code);
    }

    [Fact]
    public void LegacyMigrationRequiresExactlyOneRecipe()
    {
        string path = RawBundle(("a.json", "{}", null), ("b.json", "{}", null));
        MapPackageException exception = Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(path));
        Assert.Equal("MAP-PKG-004", exception.Code);
    }

    [Fact]
    public void DuplicateAndCaseCollidingPathsAreRejected()
    {
        foreach ((string left, string right) in new[] { ("same.bin", "same.bin"), ("Case.bin", "case.bin") })
        {
            string path = RawBundle((left, "a", null), (right, "b", null));
            MapPackageException exception = Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(path));
            Assert.Equal("MAP-PKG-003", exception.Code);
        }
    }

    [Fact]
    public void SymlinkEntryIsRejected()
    {
        string path = RawBundle(("link", "target", unchecked((int)0xA1FF0000)));
        MapPackageException exception = Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(path));
        Assert.Equal("MAP-PKG-003", exception.Code);
    }

    [Fact]
    public void FileCountEntrySizeTotalSizeAndCompressionRatioAreBounded()
    {
        string artifact = RawBundle(("artifact.bin", "too large", null));
        Assert.Equal("MAP-PKG-007", Assert.Throws<MapPackageException>(() =>
            new MapBundleReader(new() { Limits = new() { MaximumArtifactBytes = 4 } }).Read(artifact)).Code);

        string count = RawBundle(("a.bin", "a", null), ("b.bin", "b", null));
        Assert.Equal("MAP-PKG-007", Assert.Throws<MapPackageException>(() =>
            new MapBundleReader(new() { Limits = new() { MaximumFileCount = 1 } }).Read(count)).Code);

        string size = RawBundle(("large.bin", new string('x', 32), null));
        Assert.Equal("MAP-PKG-007", Assert.Throws<MapPackageException>(() =>
            new MapBundleReader(new() { Limits = new() { MaximumEntryBytes = 16 } }).Read(size)).Code);
        Assert.Equal("MAP-PKG-007", Assert.Throws<MapPackageException>(() =>
            new MapBundleReader(new() { Limits = new() { MaximumDecompressedBytes = 16 } }).Read(size)).Code);

        string bombPath = Path.Combine(_directory, "bomb.fpmap");
        using (var output = File.Create(bombPath))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        {
            ZipArchiveEntry entry = archive.CreateEntry("zeros.bin", CompressionLevel.SmallestSize);
            using Stream stream = entry.Open();
            stream.Write(new byte[1024 * 1024]);
        }
        Assert.Equal("MAP-PKG-008", Assert.Throws<MapPackageException>(() =>
            new MapBundleReader(new() { Limits = new() { MaximumCompressionRatio = 10 } }).Read(bombPath)).Code);
    }

    [Fact]
    public async Task WriterRejectsOversizedInputBeforePublishingDestination()
    {
        string path = Path.Combine(_directory, "bounded.fpmap");
        File.WriteAllText(path, "preserve me");
        var limits = new MapBundleLimits { MaximumEntryBytes = 2 };

        MapPackageException exception = await Assert.ThrowsAsync<MapPackageException>(() =>
            new MapBundleWriter(limits).WriteAsync(path, Manifest(), Files()));

        Assert.Equal("MAP-PKG-007", exception.Code);
        Assert.Equal("preserve me", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(_directory, "bounded.fpmap.tmp-*"));
    }

    [Fact]
    public async Task WrongHashAndUndeclaredFileAreRejected()
    {
        string path = Path.Combine(_directory, "tampered.fpmap");
        await new MapBundleWriter().WriteAsync(path, Manifest(), Files());
        ReplaceManifest(path, manifest => manifest.ContentHash = new string('a', 64));
        Assert.Equal("MAP-PKG-009", Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(path)).Code);

        path = Path.Combine(_directory, "extra.fpmap");
        await new MapBundleWriter().WriteAsync(path, Manifest(), Files());
        using (FileStream output = new(path, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update))
        {
            using Stream stream = archive.CreateEntry("extra.bin").Open();
            stream.WriteByte(1);
        }
        Assert.Equal("MAP-PKG-009", Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(path)).Code);
    }

    [Fact]
    public void CorruptZipMalformedJsonAndDuplicateJsonMembersFailSafely()
    {
        string corrupt = Path.Combine(_directory, "corrupt.fpmap");
        File.WriteAllText(corrupt, "not a zip");
        Assert.Equal("MAP-PKG-011", Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(corrupt)).Code);

        string malformed = RawBundle((MapBundle.ManifestPath, "{", null));
        Assert.Equal("MAP-PKG-012", Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(malformed)).Code);

        string duplicate = RawBundle((MapBundle.ManifestPath,
            "{\"format\":2,\"format\":2}", null));
        Assert.Equal("MAP-PKG-012", Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(duplicate)).Code);
    }

    [Fact]
    public async Task UnknownManifestMemberAndUnexpectedJsonRoleAreRejected()
    {
        string path = Path.Combine(_directory, "unknown.fpmap");
        await new MapBundleWriter().WriteAsync(path, Manifest(), Files());
        using (FileStream output = new(path, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update))
        {
            ZipArchiveEntry old = archive.GetEntry(MapBundle.ManifestPath)!;
            string json;
            using (var reader = new StreamReader(old.Open())) json = reader.ReadToEnd();
            old.Delete();
            json = json.TrimEnd().TrimEnd('}') + ",\"surprise\":true}";
            using var writer = new StreamWriter(archive.CreateEntry(MapBundle.ManifestPath).Open());
            writer.Write(json);
        }
        Assert.Equal("MAP-PKG-012", Assert.Throws<MapPackageException>(() => new MapBundleReader().Read(path)).Code);
    }

    [Fact]
    public async Task StructurallyValidPackageWithInvalidMapRecipeFailsSemanticVerification()
    {
        string path = Path.Combine(_directory, "invalid-map.fpmap");
        await new MapBundleWriter().WriteAsync(path, Manifest(), Files());

        MapBundleValidationResult result = new MapBundleValidator().Validate(path);

        Assert.False(result.IsValid);
        Assert.Null(result.Bundle);
        Assert.Contains(result.Diagnostics, value => value.Code == "MAP-GEO-002"
            && value.Severity == MapDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task DuplicateMembersInV2RecipeFailSemanticVerification()
    {
        string path = Path.Combine(_directory, "duplicate-recipe.fpmap");
        byte[] recipe = Encoding.UTF8.GetBytes(
            "{\"stableId\":\"community.fixture\",\"stableId\":\"community.other\",\"map\":{}}");
        await new MapBundleWriter().WriteAsync(path, Manifest(),
            [new("map.json", MapFileRole.Recipe, recipe)]);

        MapBundleValidationResult result = new MapBundleValidator().Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, value => value.Code == "MAP-PKG-012");
    }

    [Fact]
    public async Task CaseVariantMembersInV2RecipeFailSemanticVerification()
    {
        string path = Path.Combine(_directory, "case-variant-recipe.fpmap");
        byte[] recipe = Encoding.UTF8.GetBytes(
            "{\"stableId\":\"community.fixture\",\"StableId\":\"community.other\",\"map\":{}}");
        await new MapBundleWriter().WriteAsync(path, Manifest(),
            [new("map.json", MapFileRole.Recipe, recipe)]);

        MapBundleValidationResult result = new MapBundleValidator().Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, value => value.Code == "MAP-PKG-012");
    }

    [Fact]
    public async Task RecipeIdentityCannotDisagreeWithManifestIdentity()
    {
        string path = Path.Combine(_directory, "identity-mismatch.fpmap");
        var project = new MapProject
        {
            StableId = "community.other",
            Version = new MapVersion(1, 2, 3),
            Metadata = new MapProjectMetadata
            {
                Name = "Fixture", Author = "Tests", Description = "Package fixture",
                Redistribution = true
            },
            SupportedModes = [MapMode.Battle],
            Map = new MapDefinition
            {
                Name = "FIXTURE",
                Materials = [new MapMaterial { Name = "fixture", SourceMaterial = 1 }],
                Brushes = [new MapBrush { Min = [0, 0, 0], Max = [1, 1, 1] }],
                Spawns = [new MapSpawn { Position = [0, 2, 0] },
                    new MapSpawn { Position = [2, 2, 0] }]
            }
        };
        byte[] recipe = JsonSerializer.SerializeToUtf8Bytes(project,
            MapJsonContext.Default.MapProject);
        await new MapBundleWriter().WriteAsync(path, Manifest(),
            [new("map.json", MapFileRole.Recipe, recipe)]);

        MapBundleValidationResult result = new MapBundleValidator().Validate(path);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, value => value.Code == "MAP-PKG-009");
    }

    private string RawBundle(params (string Path, string Contents, int? Attributes)[] entries)
    {
        string path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".fpmap");
        using var output = File.Create(path);
        using var archive = new ZipArchive(output, ZipArchiveMode.Create);
        foreach ((string entryPath, string contents, int? attributes) in entries)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryPath);
            if (attributes.HasValue) entry.ExternalAttributes = attributes.Value;
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, bufferSize: 1024, leaveOpen: false);
            writer.Write(contents);
        }
        return path;
    }

    private static MapManifest Manifest() => new()
    {
        StableId = "community.fixture",
        Version = new MapVersion(1, 2, 3),
        Name = "Fixture",
        Author = "Tests",
        Description = "Package fixture",
        SupportedModes = [MapMode.Battle],
        Redistribution = true,
        Recipe = "map.json"
    };

    private static MapPackageFile[] Files()
        => [new("map.json", MapFileRole.Recipe, Encoding.UTF8.GetBytes("{}"))];

    private static void ReplaceManifest(string path, Action<MapManifest> mutate)
    {
        using FileStream output = new(path, FileMode.Open, FileAccess.ReadWrite);
        using var archive = new ZipArchive(output, ZipArchiveMode.Update);
        ZipArchiveEntry old = archive.GetEntry(MapBundle.ManifestPath)!;
        MapManifest manifest;
        using (var stream = old.Open())
            manifest = JsonSerializer.Deserialize(stream, MapJsonContext.Default.MapManifest)!;
        old.Delete();
        mutate(manifest);
        using Stream replacement = archive.CreateEntry(MapBundle.ManifestPath).Open();
        JsonSerializer.Serialize(replacement, manifest, MapJsonContext.Default.MapManifest);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
