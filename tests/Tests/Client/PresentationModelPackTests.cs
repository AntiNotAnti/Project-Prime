using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class PresentationModelPackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"project-prime-model-pack-{Guid.NewGuid():N}");

    public PresentationModelPackTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AssetKeysCanonicalizeOnlySupportedStableShapes()
    {
        PresentationModelAssetKey hunter = PresentationModelAssetKey.Parse(
            "MODEL/Hunter/Samus/Recolor/003");

        Assert.Equal("model/hunter/samus/recolor/3", hunter.Value);
        Assert.Equal(hunter, PresentationModelAssetKey.ForHunter("samus", 3));
        Assert.Equal("model/weapon/power-beam/first-person",
            PresentationModelAssetKey.ForFirstPersonWeapon("Power-Beam").Value);
        Assert.Equal("model/pickup/missile", PresentationModelAssetKey.ForPickup("Missile").Value);
        Assert.Equal("model/door/blue", PresentationModelAssetKey.ForDoor("Blue").Value);
        Assert.Equal("model/prop/teleporter", PresentationModelAssetKey.ForProp("Teleporter").Value);
        Assert.Equal("model/environment/archives/console-01",
            PresentationModelAssetKey.ForEnvironment("Archives", "Console-01").Value);
        Assert.Equal("custom/example/weapon.v2",
            PresentationModelAssetKey.ForCustom("Example", "Weapon.V2").Value);

        Assert.False(PresentationModelAssetKey.TryParse(
            "model/hunter/samus/recolor/-1", out _));
        Assert.False(PresentationModelAssetKey.TryParse(
            "model/weapon/power-beam/world", out _));
        Assert.False(PresentationModelAssetKey.TryParse("model/../samus", out _));
        Assert.False(PresentationModelAssetKey.TryParse(" custom/example/model", out _));
        Assert.False(default(PresentationModelAssetKey).IsValid);
    }

    [Fact]
    public void ValidPackResolvesReplacementWithoutChangingAuthoritativeIdentities()
    {
        WriteGlb("models/power_beam.glb");
        WriteManifest(new
        {
            format = 1,
            models = new[]
            {
                new
                {
                    key = "model/weapon/power-beam/first-person",
                    asset = "models/power_beam.glb",
                    metadata = new
                    {
                        displayName = "Power Beam Remaster",
                        author = "Project Prime",
                        license = "CC-BY-4.0"
                    }
                }
            }
        });

        PresentationModelPackLoadResult loaded = PresentationModelPackLoader.Load(_root);
        var original = new PresentationModelRequest(
            PresentationModelAssetKey.ForFirstPersonWeapon("power-beam"),
            originalPresentationIdentity: "models/weapons/power_beam",
            simulationIdentity: "weapon:power-beam",
            collisionIdentity: "collision:power-beam-projectile",
            animationIdentity: "animation:power-beam-first-person");

        ResolvedPresentationModel resolved = loaded.Resolver.Resolve(original);

        Assert.Empty(loaded.Issues);
        Assert.Equal(1, loaded.Resolver.Count);
        Assert.True(resolved.UsesReplacement);
        Assert.Equal("models/power_beam.glb", resolved.Replacement!.RelativePath);
        Assert.Equal(Path.Combine(_root, "models", "power_beam.glb"),
            resolved.Replacement.FullPath);
        Assert.Equal(12, resolved.Replacement.EncodedBytes);
        Assert.Equal("Power Beam Remaster", resolved.Replacement.Metadata!.DisplayName);
        Assert.Equal("Project Prime", resolved.Replacement.Metadata.Author);
        Assert.Equal("CC-BY-4.0", resolved.Replacement.Metadata.License);
        Assert.Equal(original.OriginalPresentationIdentity, resolved.OriginalPresentationIdentity);
        Assert.Equal(original.SimulationIdentity, resolved.SimulationIdentity);
        Assert.Equal(original.CollisionIdentity, resolved.CollisionIdentity);
        Assert.Equal(original.AnimationIdentity, resolved.AnimationIdentity);
    }

    [Fact]
    public void MissingPackAndUnlistedModelRetainOriginalPresentationFallback()
    {
        PresentationModelPackLoadResult missing = PresentationModelPackLoader.Load(_root);
        Assert.Equal(0, missing.Resolver.Count);
        Assert.Contains(missing.Issues,
            issue => issue.Kind == PresentationModelPackIssueKind.MissingManifest);

        WriteManifest(new { format = 1, models = Array.Empty<object>() });
        PresentationModelPackLoadResult empty = PresentationModelPackLoader.Load(_root);
        var original = new PresentationModelRequest(PresentationModelAssetKey.ForDoor("blue"),
            "models/door_blue", "entity:door", "collision:door_blue", "animation:door_blue");

        ResolvedPresentationModel resolved = empty.Resolver.Resolve(original);

        Assert.False(resolved.UsesReplacement);
        Assert.Null(resolved.Replacement);
        Assert.Equal("models/door_blue", resolved.OriginalPresentationIdentity);
        Assert.Equal("entity:door", resolved.SimulationIdentity);
        Assert.Equal("collision:door_blue", resolved.CollisionIdentity);
        Assert.Equal("animation:door_blue", resolved.AnimationIdentity);
    }

    [Fact]
    public void OriginalIdentityContractRejectsInvalidProgrammerInput()
    {
        PresentationModelAssetKey key = PresentationModelAssetKey.ForProp("console");

        Assert.Throws<ArgumentException>(() => new PresentationModelRequest(default,
            "model", "simulation", "collision", "animation"));
        Assert.Throws<ArgumentException>(() => new PresentationModelRequest(key,
            " model ", "simulation", "collision", "animation"));
        Assert.Throws<ArgumentException>(() => new PresentationModelRequest(key,
            "model", "simulation\n", "collision", "animation"));
        Assert.Throws<ArgumentException>(() => new PresentationModelRequest(key,
            "model", "simulation", "collision", new string('a',
                PresentationModelPackLimits.MaximumIdentityLength + 1)));
    }

    [Fact]
    public void UnsafeUnsupportedMissingAndOversizedModelsFailSoftPerEntry()
    {
        WriteGlb("invalid/not_glb.glb", validHeader: false);
        WriteGlb("wrong/model.gltf");
        string huge = Path.Combine(_root, "huge.glb");
        using (FileStream stream = File.Create(huge))
            stream.SetLength(PresentationModelPackLimits.MaximumModelFileBytes + 1);
        WriteManifest(new
        {
            format = 1,
            models = new object[]
            {
                Entry("model/prop/unsafe", "../outside.glb"),
                Entry("model/prop/wrong-extension", "wrong/model.gltf"),
                Entry("model/prop/missing", "missing.glb"),
                Entry("model/prop/invalid", "invalid/not_glb.glb"),
                Entry("model/prop/huge", "huge.glb")
            }
        });

        PresentationModelPackLoadResult loaded = PresentationModelPackLoader.Load(_root);

        Assert.Equal(0, loaded.Resolver.Count);
        Assert.Contains(loaded.Issues, issue => issue.Kind == PresentationModelPackIssueKind.UnsafeModelPath
            && issue.Key == PresentationModelAssetKey.ForProp("unsafe"));
        Assert.Contains(loaded.Issues, issue => issue.Kind == PresentationModelPackIssueKind.UnsupportedModel
            && issue.Key == PresentationModelAssetKey.ForProp("wrong-extension"));
        Assert.Contains(loaded.Issues, issue => issue.Kind == PresentationModelPackIssueKind.MissingModel);
        Assert.Contains(loaded.Issues, issue => issue.Kind == PresentationModelPackIssueKind.UnsupportedModel);
        Assert.Contains(loaded.Issues, issue => issue.Kind == PresentationModelPackIssueKind.ModelTooLarge);
    }

    [Fact]
    public void MalformedVersionedDuplicateAndEntryBoundsAreDeterministic()
    {
        File.WriteAllText(Path.Combine(_root, PresentationModelPackLoader.ManifestFileName), "{ invalid");
        PresentationModelPackLoadResult malformed = PresentationModelPackLoader.Load(_root);
        Assert.Contains(malformed.Issues,
            issue => issue.Kind == PresentationModelPackIssueKind.MalformedManifest);

        WriteManifest(new { format = 2, models = Array.Empty<object>() });
        PresentationModelPackLoadResult version = PresentationModelPackLoader.Load(_root);
        Assert.Contains(version.Issues,
            issue => issue.Kind == PresentationModelPackIssueKind.UnsupportedManifestVersion);

        WriteGlb("models/shared.glb");
        WriteManifest(new
        {
            format = 1,
            models = new[]
            {
                Entry("model/prop/console", "models/shared.glb"),
                Entry("MODEL/PROP/CONSOLE", "models/shared.glb")
            }
        });
        PresentationModelPackLoadResult duplicate = PresentationModelPackLoader.Load(_root);
        Assert.Equal(0, duplicate.Resolver.Count);
        PresentationModelPackIssue issue = Assert.Single(duplicate.Issues,
            candidate => candidate.Kind == PresentationModelPackIssueKind.DuplicateModelKey);
        Assert.Equal("model/prop/console", issue.Key!.Value.Value);

        object[] entries = Enumerable.Range(0, PresentationModelPackLimits.MaximumModels + 1)
            .Select(index => Entry($"custom/test/model-{index}", "models/shared.glb"))
            .ToArray();
        WriteManifest(new { format = 1, models = entries });
        PresentationModelPackLoadResult tooMany = PresentationModelPackLoader.Load(_root);
        Assert.Equal(0, tooMany.Resolver.Count);
        Assert.Contains(tooMany.Issues,
            candidate => candidate.Kind == PresentationModelPackIssueKind.TooManyModels);
    }

    [Fact]
    public void ManifestSizeUnknownPropertiesAndInvalidMetadataRemainBounded()
    {
        string manifestPath = Path.Combine(_root, PresentationModelPackLoader.ManifestFileName);
        using (FileStream stream = File.Create(manifestPath))
            stream.SetLength(PresentationModelPackLimits.MaximumManifestBytes + 1);
        PresentationModelPackLoadResult huge = PresentationModelPackLoader.Load(_root);
        Assert.Contains(huge.Issues,
            issue => issue.Kind == PresentationModelPackIssueKind.ManifestTooLarge);

        File.WriteAllText(manifestPath,
            "{\"format\":1,\"format\":1,\"models\":[]}");
        PresentationModelPackLoadResult duplicateProperty = PresentationModelPackLoader.Load(_root);
        Assert.Contains(duplicateProperty.Issues,
            issue => issue.Kind == PresentationModelPackIssueKind.MalformedManifest);

        WriteGlb("models/door.glb");
        WriteManifest(new
        {
            format = 1,
            models = new[]
            {
                new
                {
                    key = "model/door/blue",
                    asset = "models/door.glb",
                    metadata = new { displayName = " bad ", author = "Artist", license = "MIT" }
                }
            }
        });
        PresentationModelPackLoadResult invalidMetadata = PresentationModelPackLoader.Load(_root);
        Assert.Equal(1, invalidMetadata.Resolver.Count);
        Assert.Contains(invalidMetadata.Issues,
            issue => issue.Kind == PresentationModelPackIssueKind.InvalidMetadata);
        Assert.True(invalidMetadata.Resolver.TryGetReplacement(PresentationModelAssetKey.ForDoor("blue"),
            out PresentationModelReplacement? replacement));
        Assert.Null(replacement!.Metadata);
    }

    private static object Entry(string key, string asset) => new { key, asset };

    private void WriteManifest<T>(T value)
        => File.WriteAllText(Path.Combine(_root, PresentationModelPackLoader.ManifestFileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void WriteGlb(string relativePath, bool validHeader = true)
    {
        string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = new byte[12];
        if (validHeader)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546C67);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        }
        File.WriteAllBytes(path, bytes);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
