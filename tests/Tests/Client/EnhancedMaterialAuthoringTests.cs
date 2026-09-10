using System;
using System.IO;
using System.Text.Json;
using MphRead;
using Xunit;

public sealed class EnhancedMaterialAuthoringTests : IDisposable
{
    private static readonly byte[] _onePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-material-authoring-test-" + Guid.NewGuid().ToString("N"));

    public EnhancedMaterialAuthoringTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void InspectProducesSortedChannelDimensionsValuesAndUsage()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pack", "textures"));
        File.WriteAllBytes(Path.Combine(_root, "pack", "textures", "armor.png"), _onePixelPng);
        File.WriteAllBytes(Path.Combine(_root, "pack", "textures", "armor_n.png"), _onePixelPng);
        WriteJson(Path.Combine(_root, "pack", "materials.json"), new
        {
            format = 1,
            materials = new object[]
            {
                new { key = "room/archives/texture/2/palette/0", smoothness = .2f },
                new
                {
                    key = "model/samus/texture/3/palette/0/recolor/0",
                    albedo = "textures/armor.png",
                    normal = "textures/armor_n.png",
                    specularStrength = .55f,
                    smoothness = .7f,
                    reflectionStrength = .25f,
                    emissionTint = new[] { 1f, .5f, .25f },
                    emissionStrength = 2f
                }
            }
        });
        string inventory = Path.Combine(_root, "inventory.json");
        WriteJson(inventory, new
        {
            format = 1,
            textures = new[]
            {
                new
                {
                    key = "MODEL/SAMUS/TEXTURE/03/PALETTE/0/RECOLOR/0",
                    originalAlbedo = new { path = "models/samus/3", width = 64, height = 32 },
                    models = new[] { "Samus", "Samus" },
                    rooms = new[] { "Celestial Archives", "Alinos" }
                }
            }
        });

        EnhancedMaterialInspectionReport report = EnhancedMaterialAuthoring.Inspect(
            Path.Combine(_root, "pack"), inventory);

        Assert.False(report.HasIssues);
        Assert.Equal(2, report.Materials.Count);
        EnhancedMaterialInspection samus = report.Materials[0];
        Assert.Equal("model/samus/texture/3/palette/0/recolor/0", samus.Key);
        Assert.Equal(new EnhancedMaterialAssetInspection(true, "models/samus/3", 64, 32),
            samus.OriginalAlbedo);
        Assert.Equal(new EnhancedMaterialAssetInspection(true, "textures/armor.png", 1, 1),
            samus.ReplacementAlbedo);
        Assert.Equal(new EnhancedMaterialAssetInspection(true, "textures/armor_n.png", 1, 1),
            samus.Normal);
        Assert.False(samus.Emissive.Present);
        Assert.Equal(.55f, samus.SpecularStrength);
        Assert.Equal(.7f, samus.Smoothness);
        Assert.Equal(.25f, samus.ReflectionStrength);
        Assert.Equal(new[] { 1f, .5f, .25f }, samus.EmissionTint);
        Assert.Equal(2f, samus.EmissionStrength);
        Assert.Equal(new[] { "Samus" }, samus.Models);
        Assert.Equal(new[] { "Alinos", "Celestial Archives" }, samus.Rooms);
        Assert.Equal("room/archives/texture/2/palette/0", report.Materials[1].Key);

        string first = EnhancedMaterialAuthoring.Serialize(report);
        string second = EnhancedMaterialAuthoring.Serialize(
            EnhancedMaterialAuthoring.Inspect(Path.Combine(_root, "pack"), inventory));
        Assert.Equal(first, second);
    }

    [Fact]
    public void StarterManifestCanonicalizesSortsAndDeduplicatesKeys()
    {
        string json = EnhancedMaterialAuthoring.GenerateStarterManifest(new[]
        {
            "room/archives/texture/2/palette/0",
            "MODEL/SAMUS/TEXTURE/03/PALETTE/0/RECOLOR/0",
            "room/archives/texture/02/palette/00"
        });

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement materials = document.RootElement.GetProperty("materials");
        Assert.Equal(2, materials.GetArrayLength());
        Assert.Equal("model/samus/texture/3/palette/0/recolor/0",
            materials[0].GetProperty("key").GetString());
        Assert.Equal("room/archives/texture/2/palette/0",
            materials[1].GetProperty("key").GetString());
        Assert.DoesNotContain("albedo", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedInventoryAndInvalidStarterKeyFailClearly()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pack"));
        WriteJson(Path.Combine(_root, "pack", "materials.json"),
            new { key = "effect/missile/texture/0" });
        string inventory = Path.Combine(_root, "inventory.json");
        File.WriteAllText(inventory, "{ invalid");

        InvalidDataException malformed = Assert.Throws<InvalidDataException>(() =>
            EnhancedMaterialAuthoring.Inspect(Path.Combine(_root, "pack"), inventory));
        Assert.Contains("malformed JSON", malformed.Message, StringComparison.Ordinal);
        InvalidDataException invalidKey = Assert.Throws<InvalidDataException>(() =>
            EnhancedMaterialAuthoring.GenerateStarterManifest(new[] { "../escape" }));
        Assert.Contains("invalid texture key", invalidKey.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedPackIsReportedWithoutThrowing()
    {
        Directory.CreateDirectory(Path.Combine(_root, "pack"));
        File.WriteAllText(Path.Combine(_root, "pack", "materials.json"), "{ invalid");

        EnhancedMaterialInspectionReport report = EnhancedMaterialAuthoring.Inspect(
            Path.Combine(_root, "pack"));

        Assert.Empty(report.Materials);
        Assert.Contains(report.Issues, issue => issue.Kind == "MalformedManifest");
    }

    private static void WriteJson<T>(string path, T value)
        => File.WriteAllText(path, JsonSerializer.Serialize(value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
