using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using MphRead;
using Xunit;

namespace MphRead.Tests.Fidelity;

public sealed class FidelityManifestTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("prime-fidelity-manifest-").FullName;

    [Fact, Trait("Category", "FidelitySmoke")]
    public void ManifestIsOrdinalDeterministicAndContentAddressed()
    {
        FidelityReferenceIdentity identity = CreateReference();
        Directory.CreateDirectory(Path.Combine(_root, "z")); Directory.CreateDirectory(Path.Combine(_root, "a"));
        File.WriteAllBytes(Path.Combine(_root, "z", "last.dat"), [3, 2, 1]);
        File.WriteAllBytes(Path.Combine(_root, "a", "first.bin"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(_root, ".DS_Store"), "host metadata");
        FidelityReferenceManifest first = FidelityManifestBuilder.Build(_root, identity);
        FidelityReferenceManifest second = FidelityManifestBuilder.Build(_root, identity);
        Assert.Equal(first.AggregateSha256, second.AggregateSha256);
        Assert.Equal(first.TotalBytes, second.TotalBytes);
        Assert.Equal(FidelityManifestBuilder.Serialize(first), FidelityManifestBuilder.Serialize(second));
        Assert.Equal(first.Files.Select(file => file.Path).Order(StringComparer.Ordinal), first.Files.Select(file => file.Path));
        Assert.Equal(3, first.Files.Count);
        Assert.Contains(first.Files, file => file.Path == "_bin/arm9.bin" && file.ContentType == "nds-arm9");
        File.WriteAllBytes(Path.Combine(_root, "z", "last.dat"), [3, 2, 0]);
        Assert.NotEqual(first.AggregateSha256, FidelityManifestBuilder.Build(_root, identity).AggregateSha256);
    }

    [Fact]
    public void UnknownIdentitySymlinksAndBoundsFailClosed()
    {
        FidelityReferenceIdentity identity = CreateReference();
        Assert.Throws<InvalidDataException>(() => FidelityManifestBuilder.Build(_root,
            new FidelityReferenceIdentity("AMHE1", "_bin/arm9.bin", new string('0', 64))));
        File.WriteAllBytes(Path.Combine(_root, "extra.bin"), [1]);
        Assert.Throws<InvalidDataException>(() => FidelityManifestBuilder.Build(_root, identity, new FidelityManifestLimits(MaxFiles: 1)));
        Assert.Throws<InvalidDataException>(() => FidelityManifestBuilder.Build(_root, identity, new FidelityManifestLimits(MaxTotalBytes: 3, MaxFileBytes: 3)));
        Assert.Throws<InvalidDataException>(() => FidelityManifestBuilder.Build(_root, identity, new FidelityManifestLimits(MaxRelativePathLength: 8)));
        string outside = Path.Combine(Path.GetTempPath(), $"prime-fidelity-outside-{Guid.NewGuid():N}");
        File.WriteAllText(outside, "outside");
        try
        {
            File.CreateSymbolicLink(Path.Combine(_root, "escape.bin"), outside);
            Assert.Throws<InvalidDataException>(() => FidelityManifestBuilder.Build(_root, identity));
        }
        finally { File.Delete(outside); }
    }

    [Theory, InlineData("../arm9.bin"), InlineData("/arm9.bin"), InlineData("_bin/../arm9.bin")]
    public void AnchorPathsMustBeCanonical(string anchor)
    {
        string hash = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3, 4]));
        Assert.Throws<InvalidDataException>(() => FidelityManifestBuilder.Build(_root, new FidelityReferenceIdentity("AMHE1", anchor, hash)));
    }

    private FidelityReferenceIdentity CreateReference()
    {
        string directory = Path.Combine(_root, "_bin"); Directory.CreateDirectory(directory);
        byte[] bytes = [1, 2, 3, 4]; File.WriteAllBytes(Path.Combine(directory, "arm9.bin"), bytes);
        return new("AMHE1", "_bin/arm9.bin", Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }
    public void Dispose() { try { Directory.Delete(_root, true); } catch (DirectoryNotFoundException) { } }
}
