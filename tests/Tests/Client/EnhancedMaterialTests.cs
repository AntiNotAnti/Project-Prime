using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class EnhancedMaterialTests : IDisposable
{
    private static readonly byte[] _onePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-enhancement-test-" + Guid.NewGuid().ToString("N"));

    public EnhancedMaterialTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void OriginalMaterialProducesSafeEnhancedFallback()
    {
        TextureIdentity texture = new(new object(), textureId: 2, paletteId: 3, recolorId: 1);
        RenderMaterial original = new()
        {
            Textured = true,
            Texture = texture,
            Specular = new Vector3(.1f, .7f, .2f),
            Emission = new Vector3(2, 1, .5f)
        };

        EnhancedMaterial material = EnhancedMaterial.FromOriginal(original);

        Assert.Equal(texture, material.Albedo);
        Assert.Null(material.Normal);
        Assert.Null(material.Emissive);
        Assert.Equal(.7f, material.SpecularStrength);
        Assert.Equal(EnhancedMaterial.DefaultSmoothness, material.Smoothness);
        Assert.Equal(0, material.ReflectionStrength);
        Assert.Equal(new Vector3(1, .5f, .25f), material.EmissionTint);
        Assert.Equal(2, material.EmissionStrength);

        original.Specular = new Vector3(float.NaN, float.PositiveInfinity, -1);
        original.Emission = new Vector3(float.NaN, -1, float.NegativeInfinity);
        material = EnhancedMaterial.FromOriginal(original);
        Assert.Equal(0, material.SpecularStrength);
        Assert.Equal(Vector3.One, material.EmissionTint);
        Assert.Equal(0, material.EmissionStrength);
    }

    [Fact]
    public void TextureAssetKeysCanonicalizeSupportedPersistentShapes()
    {
        TextureAssetKey model = TextureAssetKey.Parse(
            "MODEL/Samus/Texture/003/Palette/00/Recolor/2");

        Assert.Equal("model/samus/texture/3/palette/0/recolor/2", model.Value);
        Assert.Equal(model, TextureAssetKey.ForModel("samus", 3, 0, 2));
        Assert.Equal("room/archives/texture/1/palette/4",
            TextureAssetKey.ForRoom("Archives", 1, 4).Value);
        Assert.Equal("effect/missile/texture/7",
            TextureAssetKey.ForEffect("MISSILE", 7).Value);
        Assert.Equal("custom/mod-name/wall.01",
            TextureAssetKey.ForCustom("Mod-Name", "Wall.01").Value);
        Assert.False(TextureAssetKey.TryParse("model/samus/texture/-1/palette/0/recolor/0", out _));
        Assert.False(TextureAssetKey.TryParse("model/../texture/1/palette/0/recolor/0", out _));
        Assert.False(TextureAssetKey.TryParse("model/samus/texture/1/palette/0", out _));
        Assert.False(TextureAssetKey.TryParse(" custom/mod/asset", out _));
        Assert.False(default(TextureAssetKey).IsValid);
    }

    [Fact]
    public void ValidManifestResolvesEnhancedChannelsAndRetainsOriginalFallback()
    {
        Directory.CreateDirectory(Path.Combine(_root, "textures"));
        File.WriteAllBytes(Path.Combine(_root, "textures", "armor.png"), _onePixelPng);
        File.WriteAllBytes(Path.Combine(_root, "textures", "armor_n.png"), _onePixelPng);
        File.WriteAllBytes(Path.Combine(_root, "textures", "armor_e.png"), _onePixelPng);
        WriteManifest(new
        {
            format = 1,
            materials = new[]
            {
                new
                {
                    key = "model/samus/texture/3/palette/0/recolor/0",
                    albedo = "textures/armor.png",
                    normal = "textures/armor_n.png",
                    emissive = "textures/armor_e.png",
                    specularStrength = .55f,
                    smoothness = .7f,
                    reflectionStrength = .25f,
                    emissionTint = new[] { 1f, .5f, .25f },
                    emissionStrength = 2f
                }
            }
        });

        EnhancementPackLoadResult loaded = EnhancementPackLoader.Load(_root);

        Assert.Empty(loaded.Issues);
        Assert.Equal(1, loaded.Resolver.Count);
        TextureAssetKey key = TextureAssetKey.ForModel("samus", 3, 0, 0);
        Assert.True(loaded.Resolver.TryGetReplacement(key, out EnhancedMaterialReplacement? replacement));
        Assert.Equal(1, replacement!.Albedo!.Width);
        Assert.Equal(1, replacement.Albedo.Height);
        Assert.Equal("textures/armor.png", replacement.Albedo.RelativePath);
        Assert.Equal(_onePixelPng, replacement.Albedo.EncodedPng.ToArray());
        Assert.Equal(4, replacement.Albedo.Rgba8.Length);
        Assert.Equal(255, replacement.Albedo.Rgba8.Span[3]);
        Assert.NotNull(replacement.Normal);
        Assert.NotNull(replacement.Emissive);
        Assert.Equal(4, replacement.Emissive!.Rgba8.Length);

        TextureIdentity originalTexture = new(new object());
        RenderMaterial original = new() { Textured = true, Texture = originalTexture };
        EnhancedMaterial resolved = loaded.Resolver.Resolve(key, original);
        Assert.NotEqual(originalTexture, resolved.Albedo);
        Assert.NotNull(resolved.Normal);
        Assert.NotNull(resolved.Emissive);
        Assert.Equal(.55f, resolved.SpecularStrength);
        Assert.Equal(.7f, resolved.Smoothness);
        Assert.Equal(.25f, resolved.ReflectionStrength);
        Assert.Equal(new Vector3(1, .5f, .25f), resolved.EmissionTint);
        Assert.Equal(2, resolved.EmissionStrength);

        EnhancedMaterial missing = loaded.Resolver.Resolve(
            TextureAssetKey.ForEffect("unlisted", 0), original);
        Assert.Equal(originalTexture, missing.Albedo);
        Assert.Null(missing.Normal);
        Assert.Null(missing.Emissive);
    }

    [Fact]
    public void UnsafeMissingAndUndecodableAssetsFallBackPerChannel()
    {
        File.WriteAllBytes(Path.Combine(_root, "broken.png"), _onePixelPng[..20]);
        WriteManifest(new[]
        {
            new
            {
                key = "effect/unsafe/texture/0",
                albedo = "../outside.png",
                normal = (string?)null,
                emissive = (string?)null
            },
            new
            {
                key = "effect/missing/texture/0",
                albedo = "missing.png",
                normal = (string?)null,
                emissive = (string?)null
            },
            new
            {
                key = "effect/broken/texture/0",
                albedo = "broken.png",
                normal = (string?)null,
                emissive = (string?)null
            }
        });

        EnhancementPackLoadResult loaded = EnhancementPackLoader.Load(_root);

        Assert.Equal(3, loaded.Resolver.Count);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancementPackIssueKind.UnsafeTexturePath);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancementPackIssueKind.MissingTexture);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancementPackIssueKind.UnsupportedTexture);
        TextureIdentity originalTexture = new(new object());
        RenderMaterial original = new() { Textured = true, Texture = originalTexture };
        foreach (string effect in new[] { "unsafe", "missing", "broken" })
        {
            Assert.Equal(originalTexture,
                loaded.Resolver.Resolve(TextureAssetKey.ForEffect(effect, 0), original).Albedo);
        }
    }

    [Fact]
    public void MalformedAndDuplicateManifestsFailSoft()
    {
        File.WriteAllText(Path.Combine(_root, EnhancementPackLoader.ManifestFileName), "{ invalid");
        EnhancementPackLoadResult malformed = EnhancementPackLoader.Load(_root);
        Assert.Equal(0, malformed.Resolver.Count);
        Assert.Contains(malformed.Issues, issue => issue.Kind == EnhancementPackIssueKind.MalformedManifest);

        WriteManifest(new[]
        {
            new { key = "effect/missile/texture/0", smoothness = .2f },
            new { key = "EFFECT/MISSILE/TEXTURE/00", smoothness = .8f }
        });
        EnhancementPackLoadResult duplicate = EnhancementPackLoader.Load(_root);
        Assert.Equal(0, duplicate.Resolver.Count);
        Assert.Contains(duplicate.Issues, issue => issue.Kind == EnhancementPackIssueKind.DuplicateMaterialKey);

        File.WriteAllText(Path.Combine(_root, EnhancementPackLoader.ManifestFileName),
            "{\"key\":\"effect/a/texture/0\",\"key\":\"effect/b/texture/0\"}");
        EnhancementPackLoadResult duplicateProperty = EnhancementPackLoader.Load(_root);
        Assert.Equal(0, duplicateProperty.Resolver.Count);
        Assert.Contains(duplicateProperty.Issues,
            issue => issue.Kind == EnhancementPackIssueKind.MalformedManifest);
    }

    [Fact]
    public void FileDimensionAndAggregatePoliciesAreBoundedBeforeDecode()
    {
        string hugeFile = Path.Combine(_root, "huge.png");
        using (FileStream stream = File.Create(hugeFile))
            stream.SetLength(EnhancementPackLimits.MaximumTextureFileBytes + 1);
        WriteManifest(new { key = "effect/huge/texture/0", albedo = "huge.png" });
        EnhancementPackLoadResult huge = EnhancementPackLoader.Load(_root,
            _ => throw new InvalidOperationException("oversized files must not reach the decoder"));
        Assert.Contains(huge.Issues, issue => issue.Kind == EnhancementPackIssueKind.TextureTooLarge);

        byte[] dimensions = (byte[])_onePixelPng.Clone();
        dimensions[16] = 0;
        dimensions[17] = 0;
        dimensions[18] = 0x20;
        dimensions[19] = 0x01; // 8193, rejected before CRC/decode.
        File.WriteAllBytes(Path.Combine(_root, "dimensions.png"), dimensions);
        WriteManifest(new { key = "effect/dimensions/texture/0", albedo = "dimensions.png" });
        EnhancementPackLoadResult tooWide = EnhancementPackLoader.Load(_root,
            _ => throw new InvalidOperationException("oversized dimensions must not reach the decoder"));
        Assert.Contains(tooWide.Issues, issue => issue.Kind == EnhancementPackIssueKind.TextureTooLarge);

        Assert.True(EnhancementResourceBudget.TryCreate(8, 16, out EnhancementResourceBudget? budget));
        Assert.True(budget!.TryReserve(4, 8));
        Assert.True(budget.TryReserve(4, 8));
        Assert.False(budget.TryReserve(1, 0));
        Assert.False(budget.TryReserve(0, 1));
        Assert.False(EnhancementResourceBudget.TryCreate(0, 1, out _));
    }

    private void WriteManifest<T>(T value)
        => File.WriteAllText(Path.Combine(_root, EnhancementPackLoader.ManifestFileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
