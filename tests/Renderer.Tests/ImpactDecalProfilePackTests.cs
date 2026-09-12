using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class ImpactDecalProfilePackTests : IDisposable
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private static readonly byte[] AlphaPixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPgEpHTAAAAzQBlapmEQgAAAABJRU5ErkJggg==");

    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"project-prime-impact-decals-{Guid.NewGuid():N}");

    public ImpactDecalProfilePackTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ValidPackResolvesStyleAndRetainsImmutableRgbaTexture()
    {
        WritePng("decals/beam.png", AlphaPixelPng);
        WriteManifest(new
        {
            format = 1,
            profiles = new[]
            {
                Profile("EnergyFlash", "BeamMark", "decals/beam.png", .35f,
                    18f, .7f, new[] { 1f, .75f, .25f }),
                Profile("ElectricBurst", "EnergyBurn", "decals/beam.png", .5f,
                    9f, .55f, new[] { .2f, .7f, 1f })
            }
        });
        ImpactDecalProfilePackLoadResult loaded
            = ImpactDecalProfilePackLoader.Load(_root);

        Assert.Empty(loaded.Issues);
        Assert.Equal(2, loaded.Catalog.Count);
        ImpactDecalProfileSelection selection
            = loaded.Catalog.Resolve(BeamImpactStyle.EnergyFlash);
        Assert.True(selection.UsesDecal);
        ImpactDecalProfile profile = selection.Profile!;
        Assert.Equal(ImpactDecalKind.BeamMark, profile.Kind);
        Assert.Equal(.35f, profile.Radius);
        Assert.Equal(TimeSpan.FromSeconds(18), profile.Lifetime);
        Assert.Equal(.7f, profile.Opacity);
        Assert.Equal(new OpenTK.Mathematics.Vector4(1, .75f, .25f, .7f),
            profile.Color);
        Assert.Equal(new byte[] { 10, 20, 30, 40 },
            profile.Texture.Rgba8.ToArray());
        Assert.Equal(profile.Texture.TextureIdentity,
            profile.Texture.Pixels.Identity);
        Assert.Same(profile.Texture.TextureIdentity.Source, profile.Texture);
        Assert.False(profile.Texture.Pixels.OnlyOpaque);

        Assert.Equal(10, profile.Texture.Rgba8.Span[0]);
        Assert.True(loaded.Catalog.TryGetProfile(BeamImpactStyle.ElectricBurst,
            out ImpactDecalProfile? shared));
        Assert.Same(profile.Texture, shared!.Texture);
    }

    [Fact]
    public void MissingInvalidAndUnknownStylesResolveToNoDecal()
    {
        ImpactDecalProfilePackLoadResult missing
            = ImpactDecalProfilePackLoader.Load(_root);
        Assert.Null(missing.Catalog.Resolve(BeamImpactStyle.EnergyFlash).Profile);
        Assert.Contains(missing.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.MissingManifest);

        WritePng("beam.png");
        WriteManifest(new
        {
            format = 1,
            profiles = new object[]
            {
                Profile("NotAStyle", "BeamMark", "beam.png", .2f, 5f, 1,
                    new[] { 1f, 1f, 1f }),
                Profile("0", "BeamMark", "beam.png", .2f, 5f, 1,
                    new[] { 1f, 1f, 1f }),
                Profile("EnergyFlash", "NotAKind", "beam.png", .2f, 5f, 1,
                    new[] { 1f, 1f, 1f }),
                Profile("ElectricBurst", "BeamMark", "beam.png", 0, 5f, 1,
                    new[] { 1f, 1f, 1f }),
                Profile("HeatBloom", "EnergyBurn", "beam.png", .4f,
                    ImpactDecalProfilePackLimits.MaximumLifetimeSeconds + 1,
                    1, new[] { 1f, 1f, 1f })
            }
        });

        ImpactDecalProfilePackLoadResult invalid
            = ImpactDecalProfilePackLoader.Load(_root);

        Assert.Equal(0, invalid.Catalog.Count);
        Assert.All(new[]
        {
            BeamImpactStyle.EnergyFlash, BeamImpactStyle.ElectricBurst,
            BeamImpactStyle.HeatBloom
        }, style => Assert.False(invalid.Catalog.Resolve(style).UsesDecal));
        Assert.True(invalid.Issues.Count(issue
            => issue.Kind == ImpactDecalProfilePackIssueKind.InvalidProfile) >= 5);
        Assert.False(invalid.Catalog.TryGetProfile((BeamImpactStyle)Byte.MaxValue,
            out _));
        Assert.Throws<ArgumentOutOfRangeException>(()
            => invalid.Catalog.Resolve((BeamImpactStyle)Byte.MaxValue));
    }

    [Fact]
    public void UnsafeMissingMalformedAndOversizedTexturesFailSoft()
    {
        File.WriteAllBytes(Path.Combine(_root, "broken.png"), OnePixelPng[..20]);
        byte[] decodeFailure = (byte[])OnePixelPng.Clone();
        decodeFailure[45] ^= 0xFF;
        WritePng("decode-failure.png", decodeFailure);
        string huge = Path.Combine(_root, "huge.png");
        using (FileStream stream = File.Create(huge))
            stream.SetLength(ImpactDecalProfilePackLimits.MaximumTextureFileBytes
                + 1);
        byte[] oversizedHeader = (byte[])OnePixelPng.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(oversizedHeader.AsSpan(16),
            (uint)(ImpactDecalProfilePackLimits.MaximumTextureDimension + 1));
        WritePng("oversized.png", oversizedHeader);
        WriteManifest(new
        {
            format = 1,
            profiles = new[]
            {
                Profile("EnergyFlash", "BeamMark", "../outside.png", .2f,
                    5f, 1, new[] { 1f, 1f, 1f }),
                Profile("ElectricBurst", "BeamMark", "missing.png", .2f,
                    5f, 1, new[] { 1f, 1f, 1f }),
                Profile("ExplosiveBloom", "MissileScorch", "broken.png", .8f,
                    8f, .8f, new[] { 1f, .4f, .1f }),
                Profile("HeavyPulse", "ExplosionMark", "huge.png", .8f,
                    8f, .8f, new[] { 1f, .4f, .1f }),
                Profile("HeatBloom", "EnergyBurn", "oversized.png", .8f,
                    8f, .8f, new[] { 1f, .4f, .1f }),
                Profile("PrecisionSpark", "BeamMark", "decode-failure.png",
                    .15f, 4f, .65f, new[] { 1f, .2f, .2f })
            }
        });

        ImpactDecalProfilePackLoadResult loaded
            = ImpactDecalProfilePackLoader.Load(_root);

        Assert.Equal(0, loaded.Catalog.Count);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.UnsafeTexturePath);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.MissingTexture);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.UnsupportedTexture);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.TextureTooLarge);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.TextureDecodeFailed);
    }

    [Fact]
    public void DuplicateStylesStrictSchemaAndBoundsAreRejectedDeterministically()
    {
        WritePng("beam.png");
        WriteManifest(new
        {
            format = 1,
            profiles = new[]
            {
                Profile("EnergyFlash", "BeamMark", "beam.png", .2f, 5f, 1,
                    new[] { 1f, 1f, 1f }),
                Profile("EnergyFlash", "EnergyBurn", "beam.png", .4f, 6f, .5f,
                    new[] { .5f, .5f, .5f })
            }
        });
        ImpactDecalProfilePackLoadResult duplicate
            = ImpactDecalProfilePackLoader.Load(_root);
        ImpactDecalProfilePackIssue duplicateIssue = Assert.Single(
            duplicate.Issues,
            issue => issue.Kind == ImpactDecalProfilePackIssueKind.DuplicateStyle);
        Assert.Equal(BeamImpactStyle.EnergyFlash, duplicateIssue.Style);
        Assert.Equal(0, duplicate.Catalog.Count);

        WriteManifest(new { format = 2, profiles = Array.Empty<object>() });
        ImpactDecalProfilePackLoadResult version
            = ImpactDecalProfilePackLoader.Load(_root);
        Assert.Contains(version.Issues, issue => issue.Kind
            == ImpactDecalProfilePackIssueKind.UnsupportedManifestVersion);

        File.WriteAllText(Path.Combine(_root,
            ImpactDecalProfilePackLoader.ManifestFileName),
            "{\"format\":1,\"format\":1,\"profiles\":[]}");
        ImpactDecalProfilePackLoadResult duplicateProperty
            = ImpactDecalProfilePackLoader.Load(_root);
        Assert.Contains(duplicateProperty.Issues, issue => issue.Kind
            == ImpactDecalProfilePackIssueKind.MalformedManifest);

        object[] tooMany = Enumerable.Range(0,
                ImpactDecalProfilePackLimits.MaximumProfiles + 1)
            .Select(_ => Profile("EnergyFlash", "BeamMark", "beam.png", .2f,
                5f, 1, new[] { 1f, 1f, 1f })).ToArray();
        WriteManifest(new { format = 1, profiles = tooMany });
        ImpactDecalProfilePackLoadResult count
            = ImpactDecalProfilePackLoader.Load(_root);
        Assert.Contains(count.Issues, issue => issue.Kind
            == ImpactDecalProfilePackIssueKind.TooManyProfiles);

        string manifestPath = Path.Combine(_root,
            ImpactDecalProfilePackLoader.ManifestFileName);
        using (FileStream stream = File.Create(manifestPath))
            stream.SetLength(ImpactDecalProfilePackLimits.MaximumManifestBytes + 1);
        ImpactDecalProfilePackLoadResult huge
            = ImpactDecalProfilePackLoader.Load(_root);
        Assert.Contains(huge.Issues, issue => issue.Kind
            == ImpactDecalProfilePackIssueKind.ManifestTooLarge);
    }

    [Fact]
    public void UnknownPropertiesDoNotPoisonValidProfiles()
    {
        WritePng("good.png");
        WritePng("bad.png");
        string json = JsonSerializer.Serialize(new
        {
            format = 1,
            profiles = new object[]
            {
                Profile("EnergyFlash", "BeamMark", "good.png", .2f, 5f, 1,
                    new[] { 1f, 1f, 1f }),
                new
                {
                    style = "ElectricBurst", kind = "BeamMark",
                    texture = "bad.png", radius = .2f, lifetimeSeconds = 5f,
                    opacity = 1f, tint = new[] { 1f, 1f, 1f }, unknown = true
                }
            }
        });
        File.WriteAllText(Path.Combine(_root,
            ImpactDecalProfilePackLoader.ManifestFileName), json);

        ImpactDecalProfilePackLoadResult loaded
            = ImpactDecalProfilePackLoader.Load(_root);

        Assert.Equal(1, loaded.Catalog.Count);
        Assert.True(loaded.Catalog.Resolve(BeamImpactStyle.EnergyFlash).UsesDecal);
        Assert.False(loaded.Catalog.Resolve(BeamImpactStyle.ElectricBurst).UsesDecal);
        Assert.Contains(loaded.Issues, issue => issue.Kind
            == ImpactDecalProfilePackIssueKind.InvalidProfile);
    }

    private static object Profile(string style, string kind, string texture,
        float radius, float lifetimeSeconds, float opacity, float[] tint)
        => new
        {
            style,
            kind,
            texture,
            radius,
            lifetimeSeconds,
            opacity,
            tint
        };

    private void WriteManifest<T>(T value)
        => File.WriteAllText(Path.Combine(_root,
            ImpactDecalProfilePackLoader.ManifestFileName),
            JsonSerializer.Serialize(value,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void WritePng(string relativePath, byte[]? bytes = null)
    {
        string path = Path.Combine(_root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes ?? OnePixelPng);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
