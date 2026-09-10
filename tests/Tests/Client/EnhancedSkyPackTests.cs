using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class EnhancedSkyPackTests : IDisposable
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] RgbaPixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGMQVDJ2AQABWQCrEyolqwAAAABJRU5ErkJggg==");

    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"project-prime-sky-pack-{Guid.NewGuid():N}");

    public EnhancedSkyPackTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SkyKeysAreStableAndRejectUnsupportedShapes()
    {
        EnhancedSkyAssetKey room = EnhancedSkyAssetKey.Parse("ROOM/Archives/SKY/Exterior-01");

        Assert.Equal("room/archives/sky/exterior-01", room.Value);
        Assert.Equal(room, EnhancedSkyAssetKey.ForRoom("archives", "exterior-01"));
        Assert.Equal("custom/example/starfield.v2",
            EnhancedSkyAssetKey.ForCustom("Example", "Starfield.V2").Value);
        Assert.False(EnhancedSkyAssetKey.TryParse("room/../sky/default", out _));
        Assert.False(EnhancedSkyAssetKey.TryParse("room/archives/background/default", out _));
        Assert.False(EnhancedSkyAssetKey.TryParse(" room/archives/sky/default", out _));
        Assert.False(default(EnhancedSkyAssetKey).IsValid);
    }

    [Fact]
    public void ValidPackLoadsBackgroundCubemapAndDeterministicallyOrderedAtmosphere()
    {
        foreach (string file in new[]
        {
            "background.png", "px.png", "nx.png", "py.png", "ny.png", "pz.png", "nz.png",
            "near.png", "far.png", "stars.png", "nebula.png"
        }) WritePng($"skies/{file}");
        WriteManifest(new
        {
            format = 1,
            skies = new[]
            {
                new
                {
                    key = "room/archives/sky/default",
                    background = "skies/background.png",
                    cubemap = new
                    {
                        positiveX = "skies/px.png", negativeX = "skies/nx.png",
                        positiveY = "skies/py.png", negativeY = "skies/ny.png",
                        positiveZ = "skies/pz.png", negativeZ = "skies/nz.png"
                    },
                    layers = new[]
                    {
                        Layer("skies/near.png", 5, .2f),
                        Layer("skies/far.png", -2, .05f)
                    },
                    stars = new
                    {
                        texture = "skies/stars.png", intensity = 2f,
                        twinkleRate = 1.5f, rotationSpeed = .02f
                    },
                    nebula = new
                    {
                        texture = "skies/nebula.png", opacity = .6f, intensity = 1.25f,
                        scrollU = .01f, scrollV = -.02f, rotationSpeed = .03f
                    }
                }
            }
        });

        EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root);
        var request = new EnhancedSkyRequest(EnhancedSkyAssetKey.ForRoom("archives"),
            "original:archives-background");
        ResolvedEnhancedSky resolved = loaded.Resolver.Resolve(request);

        Assert.Empty(loaded.Issues);
        Assert.Equal(1, loaded.Resolver.Count);
        Assert.True(resolved.UsesEnhancement);
        Assert.Equal("original:archives-background", resolved.OriginalPresentationIdentity);
        EnhancedSkyReplacement replacement = resolved.Replacement!;
        Assert.Equal("skies/background.png", replacement.Background!.RelativePath);
        Assert.Equal(1, replacement.Background.Width);
        Assert.NotNull(replacement.Cubemap);
        Assert.Equal("skies/px.png", replacement.Cubemap!.PositiveX.RelativePath);
        Assert.Equal(EnhancedSkyBaseKind.Cubemap, replacement.BaseKind);
        Assert.Same(replacement.Cubemap, replacement.BaseCubemap);
        Assert.Null(replacement.BaseBackground);
        Assert.Equal(new[] { -2, 5 }, replacement.Layers.Select(layer => layer.Order));
        Assert.Equal(new[] { "skies/far.png", "skies/near.png" },
            replacement.Layers.Select(layer => layer.Texture.RelativePath));
        Assert.Equal(2, replacement.Stars!.Intensity);
        Assert.Equal(1.5f, replacement.Stars.TwinkleRate);
        Assert.Equal(.6f, replacement.Nebula!.Opacity);
        Assert.Equal(-.02f, replacement.Nebula.ScrollV);
        Assert.Equal(new[]
        {
            EnhancedSkyCompositionKind.Nebula,
            EnhancedSkyCompositionKind.Stars,
            EnhancedSkyCompositionKind.AuthoredLayer,
            EnhancedSkyCompositionKind.AuthoredLayer
        }, replacement.Composition.Select(entry => entry.Kind));
        Assert.All(replacement.Composition, entry =>
        {
            Assert.Equal(EnhancedSkyOverlayBlend.StraightAlpha, entry.Blend);
            Assert.False(entry.SelectiveBloom);
        });
        Assert.False(replacement.SelectiveBloom);
    }

    [Fact]
    public void TextureAssetRetainsDecodedRgbaAndStableUploadIdentity()
    {
        WritePng("rgba.png", RgbaPixelPng);
        WriteManifest(new
        {
            format = 1,
            skies = new[] { Sky("room/test/sky/default", "rgba.png") }
        });

        EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root);
        Assert.True(loaded.Resolver.TryGetReplacement(EnhancedSkyAssetKey.ForRoom("test"),
            out EnhancedSkyReplacement? replacement));
        EnhancedSkyTextureAsset texture = replacement!.Background!;

        Assert.Equal(new byte[] { 17, 34, 51, 68 }, texture.Rgba8.ToArray());
        Assert.Equal(texture.TextureIdentity, texture.Pixels.Identity);
        Assert.Equal(texture.Rgba8.ToArray(), texture.Pixels.Rgba8.ToArray());
        Assert.False(texture.Pixels.OnlyOpaque);
        Assert.Equal(4, texture.DecodedRgba8Bytes);
        Assert.Equal(EnhancedSkyBaseKind.Background2D, replacement.BaseKind);
        Assert.Same(texture, replacement.BaseBackground);
        Assert.Null(replacement.BaseCubemap);

        WritePng("rgba.png", OnePixelPng);
        Assert.Equal(new byte[] { 17, 34, 51, 68 }, texture.Rgba8.ToArray());
        ResolvedEnhancedSky secondResolution = loaded.Resolver.Resolve(new EnhancedSkyRequest(
            EnhancedSkyAssetKey.ForRoom("test"), "original:test-sky"));
        Assert.Equal(texture.TextureIdentity,
            secondResolution.Replacement!.BaseBackground!.TextureIdentity);
    }

    [Fact]
    public void CompatibilityRgbDecoderExpandsAnOpaqueAlphaChannel()
    {
        WritePng("rgb.png");
        WriteManifest(new
        {
            format = 1,
            skies = new[] { Sky("room/test/sky/default", "rgb.png") }
        });

        EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root,
            _ => new DecodedRgbImage(1, 1, new byte[] { 4, 5, 6 }));
        Assert.True(loaded.Resolver.TryGetReplacement(EnhancedSkyAssetKey.ForRoom("test"),
            out EnhancedSkyReplacement? replacement));

        Assert.Equal(new byte[] { 4, 5, 6, 255 }, replacement!.Background!.Rgba8.ToArray());
        Assert.True(replacement.Background.Pixels.OnlyOpaque);
    }

    [Fact]
    public void EqualOrderAuthoredLayersUseStableTextureKeyInsteadOfManifestOrder()
    {
        WritePng("layers/a.png");
        WritePng("layers/z.png");
        WriteManifestWithLayers(Layer("layers/z.png", 4, .2f),
            Layer("layers/a.png", 4, .1f));
        EnhancedSkyPackLoadResult first = EnhancedSkyPackLoader.Load(_root);
        Assert.True(first.Resolver.TryGetReplacement(EnhancedSkyAssetKey.ForRoom("test"),
            out EnhancedSkyReplacement? firstReplacement));

        WriteManifestWithLayers(Layer("layers/a.png", 4, .1f),
            Layer("layers/z.png", 4, .2f));
        EnhancedSkyPackLoadResult second = EnhancedSkyPackLoader.Load(_root);
        Assert.True(second.Resolver.TryGetReplacement(EnhancedSkyAssetKey.ForRoom("test"),
            out EnhancedSkyReplacement? secondReplacement));

        string[] expected = ["layers/a.png", "layers/z.png"];
        Assert.Equal(expected, firstReplacement!.Layers.Select(layer => layer.Texture.RelativePath));
        Assert.Equal(expected, secondReplacement!.Layers.Select(layer => layer.Texture.RelativePath));
        Assert.Equal(expected, firstReplacement.Composition.Select(entry => entry.StableKey));
        Assert.Equal(expected, secondReplacement.Composition.Select(entry => entry.StableKey));
    }

    [Fact]
    public void MissingAndInvalidEnhancementsResolveOriginalSky()
    {
        EnhancedSkyPackLoadResult missing = EnhancedSkyPackLoader.Load(_root);
        Assert.Contains(missing.Issues,
            issue => issue.Kind == EnhancedSkyPackIssueKind.MissingManifest);

        WriteManifest(new
        {
            format = 1,
            skies = new[]
            {
                new { key = "room/vesper/sky/default", background = "missing.png" }
            }
        });
        EnhancedSkyPackLoadResult invalid = EnhancedSkyPackLoader.Load(_root);
        ResolvedEnhancedSky resolved = invalid.Resolver.Resolve(new EnhancedSkyRequest(
            EnhancedSkyAssetKey.ForRoom("vesper"), "original:vesper-sky"));

        Assert.Equal(0, invalid.Resolver.Count);
        Assert.False(resolved.UsesEnhancement);
        Assert.Null(resolved.Replacement);
        Assert.Equal("original:vesper-sky", resolved.OriginalPresentationIdentity);
        Assert.Contains(invalid.Issues, issue => issue.Kind == EnhancedSkyPackIssueKind.MissingTexture);
        Assert.Contains(invalid.Issues, issue => issue.Kind == EnhancedSkyPackIssueKind.InvalidSky);
    }

    [Fact]
    public void UnsafeMalformedAndOversizedTexturesFailSoft()
    {
        File.WriteAllBytes(Path.Combine(_root, "broken.png"), OnePixelPng[..20]);
        string huge = Path.Combine(_root, "huge.png");
        using (FileStream stream = File.Create(huge))
            stream.SetLength(EnhancedSkyPackLimits.MaximumTextureFileBytes + 1);
        WriteManifest(new
        {
            format = 1,
            skies = new object[]
            {
                Sky("room/test/sky/unsafe", "../outside.png"),
                Sky("room/test/sky/unsupported", "asset.jpg"),
                Sky("room/test/sky/broken", "broken.png"),
                Sky("room/test/sky/huge", "huge.png")
            }
        });

        EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root);

        Assert.Equal(0, loaded.Resolver.Count);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedSkyPackIssueKind.UnsafeTexturePath);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedSkyPackIssueKind.UnsupportedTexture);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedSkyPackIssueKind.TextureTooLarge);
    }

    [Fact]
    public void CubemapIsAtomicAndAtmosphereMetadataIsBounded()
    {
        byte[] twoByOneHeader = (byte[])OnePixelPng.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(twoByOneHeader.AsSpan(16), 2);
        WritePng("faces/one.png");
        WritePng("faces/two.png", twoByOneHeader);
        WritePng("background.png");
        WritePng("layer.png");
        WriteManifest(new
        {
            format = 1,
            skies = new[]
            {
                new
                {
                    key = "room/test/sky/default",
                    background = "background.png",
                    cubemap = new
                    {
                        positiveX = "faces/one.png", negativeX = "faces/one.png",
                        positiveY = "faces/one.png", negativeY = "faces/one.png",
                        positiveZ = "faces/one.png", negativeZ = "faces/two.png"
                    },
                    layers = new[]
                    {
                        new
                        {
                            texture = "layer.png", order = 0, scrollU = 5f,
                            scrollV = 0f, rotationSpeed = 0f, opacity = 1f, intensity = 1f
                        }
                    },
                    stars = new { texture = "layer.png", intensity = 9f, twinkleRate = 0f, rotationSpeed = 0f },
                    nebula = new
                    {
                        texture = "layer.png", opacity = -1f, intensity = 1f,
                        scrollU = 0f, scrollV = 0f, rotationSpeed = 0f
                    }
                }
            }
        });

        EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root, FakeDecode);
        Assert.True(loaded.Resolver.TryGetReplacement(EnhancedSkyAssetKey.ForRoom("test"),
            out EnhancedSkyReplacement? replacement));
        Assert.NotNull(replacement!.Background);
        Assert.Null(replacement.Cubemap);
        Assert.Empty(replacement.Layers);
        Assert.Null(replacement.Stars);
        Assert.Null(replacement.Nebula);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancedSkyPackIssueKind.InvalidCubemap);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancedSkyPackIssueKind.InvalidLayerMetadata);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancedSkyPackIssueKind.InvalidStarsMetadata);
        Assert.Contains(loaded.Issues, issue => issue.Kind == EnhancedSkyPackIssueKind.InvalidNebulaMetadata);
    }

    [Fact]
    public void VersionDuplicatesAndManifestCountsAreStrictAndDeterministic()
    {
        WriteManifest(new { format = 2, skies = Array.Empty<object>() });
        EnhancedSkyPackLoadResult version = EnhancedSkyPackLoader.Load(_root);
        Assert.Contains(version.Issues,
            issue => issue.Kind == EnhancedSkyPackIssueKind.UnsupportedManifestVersion);

        WritePng("sky.png");
        WriteManifest(new
        {
            format = 1,
            skies = new[]
            {
                Sky("room/test/sky/default", "sky.png"),
                Sky("ROOM/TEST/SKY/DEFAULT", "sky.png")
            }
        });
        EnhancedSkyPackLoadResult duplicate = EnhancedSkyPackLoader.Load(_root);
        Assert.Equal(0, duplicate.Resolver.Count);
        EnhancedSkyPackIssue issue = Assert.Single(duplicate.Issues,
            candidate => candidate.Kind == EnhancedSkyPackIssueKind.DuplicateSkyKey);
        Assert.Equal("room/test/sky/default", issue.Key!.Value.Value);

        object[] skies = Enumerable.Range(0, EnhancedSkyPackLimits.MaximumSkies + 1)
            .Select(index => Sky($"custom/test/sky-{index}", "sky.png")).ToArray();
        WriteManifest(new { format = 1, skies });
        EnhancedSkyPackLoadResult tooMany = EnhancedSkyPackLoader.Load(_root);
        Assert.Equal(0, tooMany.Resolver.Count);
        Assert.Contains(tooMany.Issues, candidate => candidate.Kind == EnhancedSkyPackIssueKind.TooManySkies);

        string manifestPath = Path.Combine(_root, EnhancedSkyPackLoader.ManifestFileName);
        using (FileStream stream = File.Create(manifestPath))
            stream.SetLength(EnhancedSkyPackLimits.MaximumManifestBytes + 1);
        EnhancedSkyPackLoadResult huge = EnhancedSkyPackLoader.Load(_root);
        Assert.Contains(huge.Issues,
            candidate => candidate.Kind == EnhancedSkyPackIssueKind.ManifestTooLarge);
    }

    [Fact]
    public void AnimatedLayerCountIsCappedBeforeResolution()
    {
        WritePng("layer.png");
        object[] layers = Enumerable.Range(0, EnhancedSkyPackLimits.MaximumLayersPerSky + 3)
            .Select(index => Layer("layer.png", index, 0)).ToArray();
        WriteManifest(new
        {
            format = 1,
            skies = new[]
            {
                new { key = "room/test/sky/default", layers }
            }
        });

        EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root);

        Assert.True(loaded.Resolver.TryGetReplacement(EnhancedSkyAssetKey.ForRoom("test"),
            out EnhancedSkyReplacement? replacement));
        Assert.Equal(EnhancedSkyPackLimits.MaximumLayersPerSky, replacement!.Layers.Count);
        Assert.Equal(Enumerable.Range(0, EnhancedSkyPackLimits.MaximumLayersPerSky),
            replacement.Layers.Select(layer => layer.Order));
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedSkyPackIssueKind.TooManyLayers);
    }

    [Fact]
    public void SymlinkedTextureIsRejectedWhenPlatformSupportsLinks()
    {
        string outside = Path.Combine(Path.GetTempPath(), $"project-prime-outside-{Guid.NewGuid():N}.png");
        string link = Path.Combine(_root, "linked.png");
        File.WriteAllBytes(outside, OnePixelPng);
        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or PlatformNotSupportedException) { return; }
            WriteManifest(new
            {
                format = 1,
                skies = new[] { Sky("room/test/sky/default", "linked.png") }
            });

            EnhancedSkyPackLoadResult loaded = EnhancedSkyPackLoader.Load(_root);

            Assert.Equal(0, loaded.Resolver.Count);
            Assert.Contains(loaded.Issues,
                issue => issue.Kind == EnhancedSkyPackIssueKind.UnsafeTexturePath);
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }

    private static object Sky(string key, string background) => new { key, background };

    private static object Layer(string texture, int order, float scrollU) => new
    {
        texture,
        order,
        scrollU,
        scrollV = 0f,
        rotationSpeed = 0f,
        opacity = 1f,
        intensity = 1f
    };

    private void WriteManifestWithLayers(params object[] layers) => WriteManifest(new
    {
        format = 1,
        skies = new[] { new { key = "room/test/sky/default", layers } }
    });

    private void WriteManifest<T>(T value)
        => File.WriteAllText(Path.Combine(_root, EnhancedSkyPackLoader.ManifestFileName),
            JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void WritePng(string relativePath, byte[]? bytes = null)
    {
        string path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? OnePixelPng);
    }

    private static DecodedRgbImage FakeDecode(ReadOnlyMemory<byte> encoded)
    {
        ReadOnlySpan<byte> bytes = encoded.Span;
        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]));
        return new DecodedRgbImage(width, height, new byte[checked(width * height * 3)]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
