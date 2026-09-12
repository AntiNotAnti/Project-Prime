using System;
using System.IO;
using System.Text.Json;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

public sealed class ReflectionProbeTests : IDisposable
{
    private static readonly byte[] _png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "project-prime-probe-test-" + Guid.NewGuid().ToString("N"));

    public ReflectionProbeTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ProbeKeysAreStrictStableAndCanonical()
    {
        ReflectionProbeKey key = ReflectionProbeKey.Parse("ROOM/Archives/Reflection/Main");
        Assert.Equal("room/archives/reflection/main", key.Value);
        Assert.Equal(key, ReflectionProbeKey.ForRoom("archives", "main"));
        Assert.Equal("generic/reflection/default", ReflectionProbeKey.ForGeneric("DEFAULT").Value);
        Assert.False(ReflectionProbeKey.TryParse("room/../reflection/main", out _));
        Assert.False(ReflectionProbeKey.TryParse("room/a/reflection/main/extra", out _));
        Assert.False(default(ReflectionProbeKey).IsValid);
    }

    [Fact]
    public void ResolutionUsesAuthoredThenGeneratedThenGenericThenNone()
    {
        WriteFaces("cube");
        WriteManifest(new
        {
            format = 1,
            genericFallback = Authored("generic/reflection/default", "cube"),
            rooms = new object[]
            {
                new { room = "archives", authored = Authored("room/archives/reflection/main", "cube") },
                new
                {
                    room = "vesper",
                    generatedStatic = new
                    {
                        key = "room/vesper/reflection/static",
                        resolution = 512,
                        capturePosition = new[] { 1f, 2f, 3f }
                    }
                }
            }
        });

        ReflectionProbePackLoadResult loaded = ReflectionProbePackLoader.Load(_root);
        Assert.Empty(loaded.Issues);

        ReflectionProbeSelection authored = loaded.Resolver.Resolve("ARCHIVES");
        Assert.Equal(ReflectionProbeSourceKind.AuthoredCubemap, authored.Kind);
        Assert.Equal(1, authored.Cubemap!.Dimension);
        Assert.Equal(6, authored.Cubemap.Faces.Count);
        foreach (ReflectionProbeFaceAsset face in authored.Cubemap.Faces.Values)
        {
            Assert.Equal(4, face.Rgba8.Length);
            Assert.Equal(255, face.Rgba8.Span[3]);
        }
        Assert.Equal(ReflectionProbeSourceKind.GenericFallback,
            loaded.Resolver.Resolve("archives",
                EnhancedEnvironmentAssetKey.Parse("generic/reflection/default")).Kind);
        Assert.Equal(ReflectionProbeSourceKind.AuthoredCubemap,
            loaded.Resolver.Resolve("archives",
                EnhancedEnvironmentAssetKey.Parse("probes/unavailable.ktx")).Kind);

        ReflectionProbeSelection generated = loaded.Resolver.Resolve("vesper");
        Assert.Equal(ReflectionProbeSourceKind.GeneratedStatic, generated.Kind);
        Assert.Equal(512, generated.Generated!.Resolution);
        Assert.Equal(new Vector3(1, 2, 3), generated.Generated.CapturePosition);
        Assert.Equal(ReflectionProbeSourceKind.GenericFallback,
            loaded.Resolver.ResolveUploadable("vesper").Kind);

        Assert.Equal(ReflectionProbeSourceKind.GenericFallback,
            loaded.Resolver.Resolve("unknown").Kind);

        WriteManifest(new { format = 1, rooms = Array.Empty<object>() });
        ReflectionProbePackLoadResult empty = ReflectionProbePackLoader.Load(_root);
        Assert.Equal(ReflectionProbeSourceKind.None, empty.Resolver.Resolve("archives").Kind);
    }

    [Fact]
    public void FrameCapturesOneImmutableCubeAndClearsItOnReset()
    {
        WriteFaces("cube");
        WriteManifest(new
        {
            format = 1,
            rooms = new[]
            {
                new { room = "archives", authored = Authored(
                    "room/archives/reflection/main", "cube") }
            }
        });
        ReflectionCubemapAsset cube = ReflectionProbePackLoader.Load(_root)
            .Resolver.ResolveUploadable("archives").Cubemap!;
        var probe = new RenderReflectionProbe(cube);
        var frame = new RenderFrame();

        frame.CaptureReflectionProbe(probe);
        Assert.Same(probe, frame.ReflectionProbe);
        Assert.Equal(6, probe.Faces.Count);
        Assert.Equal(1, probe.MipLevelCount);
        frame.Seal();
        Assert.Throws<InvalidOperationException>(() =>
            frame.CaptureReflectionProbe(probe));

        frame.Reset();
        Assert.Null(frame.ReflectionProbe);
    }

    [Fact]
    public void InvalidAuthoredCubeFallsThroughToGeneratedDescriptor()
    {
        WriteFaces("cube");
        WriteManifest(new
        {
            format = 1,
            rooms = new[]
            {
                new
                {
                    room = "archives",
                    authored = Authored("room/archives/reflection/main", "../outside"),
                    generatedStatic = new { key = "room/archives/reflection/static", resolution = 256 }
                }
            }
        });

        ReflectionProbePackLoadResult loaded = ReflectionProbePackLoader.Load(_root);
        Assert.Contains(loaded.Issues, issue => issue.Kind == ReflectionProbePackIssueKind.UnsafePath);
        Assert.Equal(ReflectionProbeSourceKind.GeneratedStatic,
            loaded.Resolver.Resolve("archives").Kind);
    }

    [Fact]
    public void CubemapRequiresSixEqualSquareBoundedPngFaces()
    {
        WriteFaces("cube");
        byte[] two = (byte[])_png.Clone();
        two[19] = 2;
        two[23] = 2;
        File.WriteAllBytes(Path.Combine(_root, "cube", "nz.png"), two);
        WriteManifest(new
        {
            format = 1,
            rooms = new[] { new { room = "archives", authored = Authored("room/archives/reflection/main", "cube") } }
        });

        ReflectionProbePackLoadResult loaded = ReflectionProbePackLoader.Load(_root, DecodeHeader);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == ReflectionProbePackIssueKind.FaceDimensionMismatch);
        Assert.Equal(ReflectionProbeSourceKind.None, loaded.Resolver.Resolve("archives").Kind);
        Assert.True(ReflectionProbePackLimits.MaximumFaceDimension <= 2048);
        Assert.True(ReflectionProbePackLimits.MaximumAggregateDecodedBytes < 1024L * 1024 * 1024);
    }

    [Fact]
    public void SamplingPolicyCapsStrengthAndChoosesRoughnessMipDeterministically()
    {
        EnhancedMaterial material = new(null, null, null, 0, .25f, 1,
            Vector3.One, 0);
        ReflectionSamplingPolicy policy = ReflectionSamplingPolicy.FromMaterial(material, 5);

        Assert.True(policy.Enabled);
        Assert.Equal(ReflectionSamplingPolicy.MaximumSubtleStrength, policy.Strength);
        Assert.Equal(.25f, policy.Smoothness);
        Assert.Equal(.75f, policy.Roughness);
        Assert.Equal(3, policy.MipLevel);
        Assert.False(ReflectionSamplingPolicy.FromMaterial(material, 0).Enabled);
    }

    [Fact]
    public void SdlFailureCacheSuppressesRepeatAndRetriesChangedOrReloadedCube()
    {
        var first = new SdlGpuReflectionResourceConfiguration(
            ReflectionProbeKey.ForRoom("archives", "main"), 256, 9, 11);
        var changedContent = first with { ContentFingerprint = 12 };
        var policy = new SdlGpuReflectionFailurePolicy();

        Assert.True(policy.ShouldAttempt(first));
        Assert.True(policy.RecordFailure(first));
        Assert.False(policy.ShouldAttempt(first));
        Assert.False(policy.RecordFailure(first));
        Assert.True(policy.ShouldAttempt(changedContent));

        policy.RecordSuccess(changedContent);
        Assert.Null(policy.Failed);
        Assert.True(policy.ShouldAttempt(first));
    }

    private void WriteFaces(string directory)
    {
        string path = Path.Combine(_root, directory);
        Directory.CreateDirectory(path);
        foreach (string face in new[] { "px", "nx", "py", "ny", "pz", "nz" })
            File.WriteAllBytes(Path.Combine(path, face + ".png"), _png);
    }

    private static object Authored(string key, string directory) => new
    {
        key,
        faces = new
        {
            positiveX = $"{directory}/px.png",
            negativeX = $"{directory}/nx.png",
            positiveY = $"{directory}/py.png",
            negativeY = $"{directory}/ny.png",
            positiveZ = $"{directory}/pz.png",
            negativeZ = $"{directory}/nz.png"
        }
    };

    private static DecodedRgbImage DecodeHeader(ReadOnlyMemory<byte> png)
    {
        ReadOnlySpan<byte> bytes = png.Span;
        int width = bytes[19];
        int height = bytes[23];
        return new DecodedRgbImage(width, height, new byte[width * height * 3]);
    }

    private void WriteManifest<T>(T value) => File.WriteAllText(
        Path.Combine(_root, ReflectionProbePackLoader.ManifestFileName),
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
