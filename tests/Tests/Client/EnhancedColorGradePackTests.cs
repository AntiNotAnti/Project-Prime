using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using MphRead;
using OpenTK.Mathematics;
using Xunit;

namespace MphRead.Tests.Client;

public sealed class EnhancedColorGradePackTests : IDisposable
{
    private static readonly byte[] _onePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private readonly string _root = Path.Combine(Path.GetTempPath(),
        $"project-prime-color-grade-{Guid.NewGuid():N}");

    public EnhancedColorGradePackTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void RendererOwnedDefaultDecoderAcceptsSupportedPng()
    {
        DecodedRgbImage decoded = DecodedRgbImage.DecodePng(_onePixelPng);

        Assert.Equal(1, decoded.Width);
        Assert.Equal(1, decoded.Height);
        Assert.Equal(3, decoded.Pixels.Length);
    }

    [Fact]
    public void ValidPackUsesEnvironmentKeysAndProducesImmutableRgba8Pixels()
    {
        WriteHeaderPng("luts/cryochasm.png");
        WriteManifest(new
        {
            format = 1,
            luts = new[]
            {
                new { key = "LUTS/Cryochasm.PNG", texture = "luts/cryochasm.png" }
            }
        });

        EnhancedColorGradePackLoadResult loaded = EnhancedColorGradePackLoader.Load(
            _root, IdentityDecode);
        EnhancedEnvironmentAssetKey key
            = EnhancedEnvironmentAssetKey.Parse("luts/cryochasm.png");
        EnhancedColorGradeSelection selection = loaded.Resolver.Resolve(key);

        Assert.Empty(loaded.Issues);
        Assert.Equal(1, loaded.Resolver.Count);
        Assert.Equal(key, selection.Key);
        Assert.False(selection.UsesIdentityFallback);
        Assert.Equal("luts/cryochasm.png", selection.Lut.RelativePath);
        Assert.Equal(EnhancedLutAddressing.TextureWidth, selection.Lut.Width);
        Assert.Equal(EnhancedLutAddressing.TextureHeight, selection.Lut.Height);
        Assert.Equal(EnhancedLutAddressing.TextureWidth
            * EnhancedLutAddressing.TextureHeight * 4, selection.Lut.Rgba8.Length);
        Assert.Equal(selection.Lut.TextureIdentity, selection.Lut.Pixels.Identity);
        Assert.True(selection.Lut.Pixels.OnlyOpaque);
        Assert.All(Enumerable.Range(0, selection.Lut.Rgba8.Length / 4), pixel =>
            Assert.Equal(Byte.MaxValue, selection.Lut.Rgba8.Span[pixel * 4 + 3]));

        EnhancedColorGradeSelection missing = loaded.Resolver.Resolve(
            EnhancedEnvironmentAssetKey.Parse("luts/missing.png"));
        Assert.True(missing.UsesIdentityFallback);
        Assert.Same(loaded.Resolver.IdentityFallback, missing.Lut);
        Assert.Same(missing.Lut, loaded.Resolver.Resolve((EnhancedEnvironmentAssetKey?)null).Lut);

        RenderColorGradeState active
            = RenderColorGradeState.FromSelection(selection, strength: 0.5f);
        Assert.True(active.Enabled);
        Assert.Equal(selection.Lut.TextureIdentity, active.LutTexture);
        Assert.Equal(0.5f, active.Strength);
        Assert.False(RenderColorGradeState.FromSelection(missing).Enabled);
        Assert.False(RenderColorGradeState.FromSelection(selection, strength: 0).Enabled);
    }

    [Fact]
    public void MalformedUnsafeMissingAndInvalidTexturesFailSoftToIdentity()
    {
        File.WriteAllBytes(Path.Combine(_root, "broken.png"), new byte[20]);
        WriteHeaderPng("wrong.png", width: 16, height: 16);
        WriteHeaderPng("decode.png");
        WriteManifest(new
        {
            format = 1,
            luts = new object[]
            {
                Lut("luts/unsafe", "../outside.png"),
                Lut("luts/missing", "missing.png"),
                Lut("luts/broken", "broken.png"),
                Lut("luts/wrong", "wrong.png"),
                Lut("luts/decode", "decode.png"),
                Lut("reflection/not-a-lut", "decode.png")
            }
        });

        EnhancedColorGradePackLoadResult loaded = EnhancedColorGradePackLoader.Load(
            _root, _ => throw new InvalidDataException("rejected"));

        Assert.Equal(0, loaded.Resolver.Count);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.UnsafeTexturePath);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.MissingTexture);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.UnsupportedTexture);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.InvalidDimensions);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.TextureDecodeFailed);
        Assert.Contains(loaded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.InvalidLut);
        Assert.True(loaded.Resolver.Resolve(
            EnhancedEnvironmentAssetKey.Parse("luts/decode")).UsesIdentityFallback);

        File.WriteAllText(Path.Combine(_root, EnhancedColorGradePackLoader.ManifestFileName),
            "{ invalid");
        EnhancedColorGradePackLoadResult malformed
            = EnhancedColorGradePackLoader.Load(_root, IdentityDecode);
        Assert.Contains(malformed.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.MalformedManifest);
        Assert.True(malformed.Resolver.Resolve((EnhancedEnvironmentAssetKey?)null)
            .UsesIdentityFallback);
    }

    [Fact]
    public void VersionDuplicatesAndEntryCountAreStrictAndDeterministic()
    {
        WriteHeaderPng("lut.png");
        WriteManifest(new
        {
            format = 1,
            luts = new[]
            {
                Lut("luts/archives", "lut.png"),
                Lut("LUTS/ARCHIVES", "lut.png")
            }
        });
        EnhancedColorGradePackLoadResult duplicate
            = EnhancedColorGradePackLoader.Load(_root, IdentityDecode);
        Assert.Equal(0, duplicate.Resolver.Count);
        EnhancedColorGradePackIssue issue = Assert.Single(duplicate.Issues,
            candidate => candidate.Kind == EnhancedColorGradePackIssueKind.DuplicateLutKey);
        Assert.Equal("luts/archives", issue.Key!.Value.Value);

        WriteManifest(new { format = 2, luts = Array.Empty<object>() });
        EnhancedColorGradePackLoadResult version
            = EnhancedColorGradePackLoader.Load(_root, IdentityDecode);
        Assert.Contains(version.Issues,
            candidate => candidate.Kind == EnhancedColorGradePackIssueKind.UnsupportedManifestVersion);

        object[] entries = Enumerable.Range(0, EnhancedColorGradePackLimits.MaximumLuts + 1)
            .Select(index => Lut($"luts/test-{index}", "lut.png")).ToArray();
        WriteManifest(new { format = 1, luts = entries });
        EnhancedColorGradePackLoadResult tooMany
            = EnhancedColorGradePackLoader.Load(_root, IdentityDecode);
        Assert.Equal(0, tooMany.Resolver.Count);
        Assert.Contains(tooMany.Issues,
            candidate => candidate.Kind == EnhancedColorGradePackIssueKind.TooManyLuts);

        File.WriteAllText(Path.Combine(_root, EnhancedColorGradePackLoader.ManifestFileName),
            "{\"format\":1,\"format\":1,\"luts\":[]}");
        EnhancedColorGradePackLoadResult duplicateProperty
            = EnhancedColorGradePackLoader.Load(_root, IdentityDecode);
        Assert.Contains(duplicateProperty.Issues,
            candidate => candidate.Kind == EnhancedColorGradePackIssueKind.MalformedManifest);
    }

    [Fact]
    public void EncodedAndAggregateBudgetsAreEnforced()
    {
        string huge = Path.Combine(_root, "huge.png");
        using (FileStream stream = File.Create(huge))
            stream.SetLength(EnhancedColorGradePackLimits.MaximumTextureFileBytes + 1);
        WriteManifest(new { format = 1, luts = new[] { Lut("luts/huge", "huge.png") } });
        EnhancedColorGradePackLoadResult oversized = EnhancedColorGradePackLoader.Load(
            _root, _ => throw new InvalidOperationException("must not decode"));
        Assert.Contains(oversized.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.TextureTooLarge);

        WriteHeaderPng("one.png");
        WriteHeaderPng("two.png");
        WriteManifest(new
        {
            format = 1,
            luts = new[] { Lut("luts/one", "one.png"), Lut("luts/two", "two.png") }
        });
        long oneDecoded = EnhancedLutAddressing.TextureWidth
            * EnhancedLutAddressing.TextureHeight * 4L;
        EnhancedColorGradePackLoadResult bounded = EnhancedColorGradePackLoader.Load(
            _root, IdentityDecode, maximumAggregateEncodedBytes: 1024,
            maximumAggregateDecodedBytes: oneDecoded);
        Assert.Equal(1, bounded.Resolver.Count);
        Assert.False(bounded.Resolver.Resolve(
            EnhancedEnvironmentAssetKey.Parse("luts/one")).UsesIdentityFallback);
        Assert.True(bounded.Resolver.Resolve(
            EnhancedEnvironmentAssetKey.Parse("luts/two")).UsesIdentityFallback);
        Assert.Contains(bounded.Issues,
            issue => issue.Kind == EnhancedColorGradePackIssueKind.AggregateLimitExceeded);
        Assert.True(EnhancedColorGradePackLimits.MaximumAggregateDecodedBytes
            <= 4L * 1024 * 1024);
    }

    [Fact]
    public void SymlinkedTextureIsRejectedWhenPlatformSupportsLinks()
    {
        string outside = Path.Combine(Path.GetTempPath(),
            $"project-prime-color-grade-outside-{Guid.NewGuid():N}.png");
        string link = Path.Combine(_root, "linked.png");
        File.WriteAllBytes(outside, HeaderPng());
        try
        {
            try { File.CreateSymbolicLink(link, outside); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or PlatformNotSupportedException) { return; }
            WriteManifest(new
            {
                format = 1,
                luts = new[] { Lut("luts/linked", "linked.png") }
            });

            EnhancedColorGradePackLoadResult loaded
                = EnhancedColorGradePackLoader.Load(_root, IdentityDecode);

            Assert.Equal(0, loaded.Resolver.Count);
            Assert.Contains(loaded.Issues,
                issue => issue.Kind == EnhancedColorGradePackIssueKind.UnsafeTexturePath);
        }
        finally
        {
            try { File.Delete(outside); } catch { }
        }
    }

    [Fact]
    public void CpuSamplingMatchesAddressingIdentityAndStrengthContract()
    {
        EnhancedColorGradeLut identity = EnhancedColorGradeResolver.Empty.IdentityFallback;
        Vector3 input = new(.13f, .57f, .91f);
        Vector3 sampled = EnhancedColorGradeSampling.Sample(identity, input, 1);
        Assert.Equal(input.X, sampled.X, precision: 5);
        Assert.Equal(input.Y, sampled.Y, precision: 5);
        Assert.Equal(input.Z, sampled.Z, precision: 5);

        byte[] constant = new byte[EnhancedLutAddressing.TextureWidth
            * EnhancedLutAddressing.TextureHeight * 4];
        for (int offset = 0; offset < constant.Length; offset += 4)
        {
            constant[offset] = 51;
            constant[offset + 1] = 102;
            constant[offset + 2] = 204;
            constant[offset + 3] = Byte.MaxValue;
        }
        var lut = new EnhancedColorGradeLut("constant.png", constant,
            isIdentityFallback: false);
        Assert.Equal(input, EnhancedColorGradeSampling.Sample(lut, input, 0));
        Vector3 full = EnhancedColorGradeSampling.Sample(lut, input, 1);
        Assert.Equal(.2f, full.X, precision: 5);
        Assert.Equal(.4f, full.Y, precision: 5);
        Assert.Equal(.8f, full.Z, precision: 5);
        Vector3 half = EnhancedColorGradeSampling.Sample(lut, input, .5f);
        Assert.Equal(Vector3.Lerp(input, full, .5f), half);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorGradeSampling.Sample(lut, new Vector3(0, 0, 2), 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorGradeSampling.Sample(lut, Vector3.Zero, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EnhancedColorGradeSampling.Sample(lut, Vector3.Zero, -0.01f));
    }

    private static object Lut(string key, string texture) => new { key, texture };

    private void WriteManifest<T>(T value) => File.WriteAllText(
        Path.Combine(_root, EnhancedColorGradePackLoader.ManifestFileName),
        JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private void WriteHeaderPng(string relativePath, int width = EnhancedLutAddressing.TextureWidth,
        int height = EnhancedLutAddressing.TextureHeight)
    {
        string path = Path.Combine(_root,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, HeaderPng(width, height));
    }

    private static byte[] HeaderPng(int width = EnhancedLutAddressing.TextureWidth,
        int height = EnhancedLutAddressing.TextureHeight)
    {
        byte[] bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), checked((uint)height));
        return bytes;
    }

    private static DecodedRgbImage IdentityDecode(ReadOnlyMemory<byte> encoded)
    {
        ReadOnlySpan<byte> bytes = encoded.Span;
        int width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]));
        int height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]));
        byte[] rgb = new byte[checked(width * height * 3)];
        int offset = 0;
        for (int green = 0; green < height; green++)
        {
            for (int blue = 0; blue < EnhancedLutAddressing.Dimension; blue++)
            {
                for (int red = 0; red < EnhancedLutAddressing.Dimension; red++)
                {
                    rgb[offset++] = checked((byte)(red * 17));
                    rgb[offset++] = checked((byte)(green * 17));
                    rgb[offset++] = checked((byte)(blue * 17));
                }
            }
        }
        return new DecodedRgbImage(width, height, rgb);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }
}
