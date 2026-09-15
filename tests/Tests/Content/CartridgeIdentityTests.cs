using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using MphRead;
using Xunit;

namespace MphRead.Tests;

public sealed class CartridgeIdentityTests
{
    [Fact]
    public void ExactSyntheticImageIsRecognizedAndStreamPositionIsRestored()
    {
        (byte[] image, CartridgeIdentity identity) = Synthetic("TEST", 4);
        using var stream = new MemoryStream(image, writable: false);
        stream.Position = 3;
        var catalog = new CartridgeCatalog([identity]);

        CartridgeValidationResult result = catalog.Validate(stream);

        Assert.True(result.IsValid);
        Assert.Equal(CartridgeValidationStatus.Valid, result.Status);
        Assert.Equal(identity, result.Identity);
        Assert.Equal(identity.Sha256, result.ActualSha256);
        Assert.Equal(3, stream.Position);
    }

    [Fact]
    public void CorrectHeaderWithWrongWholeImageHashIsRejected()
    {
        (byte[] image, CartridgeIdentity actual) = Synthetic("TEST", 4);
        // A different digest with the same header and length is a modified
        // image, not an unsupported release.
        var wrong = new CartridgeIdentity(actual.GameCode, actual.Revision, actual.Region,
            actual.DisplayName, actual.Size, new string('0', 64), actual.Family);
        var catalog = new CartridgeCatalog([wrong]);

        CartridgeValidationResult result = catalog.Validate(new MemoryStream(image, writable: false));

        Assert.Equal(CartridgeValidationStatus.HeaderValidWrongHash, result.Status);
        Assert.Equal(wrong, result.Identity);
        Assert.Equal(actual.Sha256, result.ActualSha256);
    }

    [Fact]
    public void RevisionMismatchIsUnsupportedEvenWhenTheImageIsOtherwiseReadable()
    {
        (byte[] image, _) = Synthetic("TEST", 4);
        CartridgeIdentity revisionFive = Synthetic("TEST", 5).Identity;
        var catalog = new CartridgeCatalog([revisionFive]);

        CartridgeValidationResult result = catalog.Validate(new MemoryStream(image, writable: false));

        Assert.Equal(CartridgeValidationStatus.Unsupported, result.Status);
        Assert.True(result.HasValidHeader);
        Assert.Equal("TEST", result.Header.GameCode);
        Assert.Equal(4, result.Header.Revision);
    }

    [Fact]
    public void TruncatedImageIsRejectedBeforeHashAcceptance()
    {
        (byte[] image, CartridgeIdentity identity) = Synthetic("TEST", 4);
        byte[] truncated = image[..^1];

        CartridgeValidationResult result = new CartridgeCatalog([identity])
            .Validate(new MemoryStream(truncated, writable: false));

        Assert.Equal(CartridgeValidationStatus.InvalidLength, result.Status);
        Assert.Equal(identity, result.Identity);
        Assert.Equal(truncated.Length, result.ActualLength);
    }

    [Fact]
    public void MissingImageIsReportedAsReadError()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "project-prime-cartridge-missing-" + Guid.NewGuid().ToString("N") + ".nds");

        CartridgeValidationResult result = CartridgeCatalog.Supported.ValidateFile(path);

        Assert.Equal(CartridgeValidationStatus.ReadError, result.Status);
    }

    [Fact]
    public void Sha256CasingIsNormalizedForCatalogComparison()
    {
        (byte[] image, CartridgeIdentity lower) = Synthetic("TEST", 4);
        var upper = new CartridgeIdentity(lower.GameCode, lower.Revision, lower.Region,
            lower.DisplayName, lower.Size, lower.Sha256.ToUpperInvariant(), lower.Family);

        CartridgeValidationResult result = new CartridgeCatalog([upper])
            .Validate(new MemoryStream(image, writable: false));

        Assert.True(result.IsValid);
        Assert.Equal(lower.Sha256, upper.Sha256);
    }

    [Fact]
    public void ForwardOnlyStreamCanBeValidatedWithoutASecondFileOpen()
    {
        (byte[] image, CartridgeIdentity identity) = Synthetic("TEST", 4);

        CartridgeValidationResult result = new CartridgeCatalog([identity])
            .Validate(new ForwardOnlyStream(image));

        Assert.True(result.IsValid);
        Assert.Equal(identity.Sha256, result.ActualSha256);
    }

    [Fact]
    public void DuplicateHeaderDefinitionsAreRejected()
    {
        CartridgeIdentity first = Synthetic("TEST", 4).Identity;
        CartridgeIdentity second = new(first.GameCode, first.Revision, first.Region,
            "different", first.Size, Sha(new byte[checked((int)first.Size)]), first.Family);

        Assert.Throws<ArgumentException>(() => new CartridgeCatalog([first, second]));
    }

    [Fact]
    public void DuplicateManifestHashesAreRejected()
    {
        CartridgeIdentity first = Synthetic("TEST", 4).Identity;
        CartridgeIdentity second = new("NEXT", 5, "USA", "different", first.Size,
            first.Sha256, first.Family);

        Assert.Throws<ArgumentException>(() => new CartridgeCatalog([first, second]));
    }

    [Fact]
    public void HeaderKnownOnlyFromHistoricalListsFailsClosed()
    {
        (byte[] image, _) = Synthetic("AMHP", 0);

        CartridgeValidationResult result = CartridgeCatalog.Supported.Validate(
            new MemoryStream(image, writable: false));

        Assert.Equal(CartridgeValidationStatus.Unsupported, result.Status);
        Assert.Equal("AMHP", result.Header.GameCode);
    }

    [Fact]
    public void DefaultCatalogContainsOnlyVerifiedWholeRomIdentities()
    {
        Assert.Equal(2, CartridgeCatalog.Supported.Identities.Length);
        Assert.Contains(CartridgeCatalog.Supported.Identities,
            identity => identity.VariantCode == "AMHE0"
                && identity.Size == 67108864L
                && identity.Sha256 == "7d0a98ff98e1b7c985d1f3d89b01730af1b2115061a4dfea847612d217a8b855");
        Assert.Contains(CartridgeCatalog.Supported.Identities,
            identity => identity.VariantCode == "AMHE1"
                && identity.Size == 67108864L
                && identity.Sha256 == "bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f");
    }

    private static (byte[] Image, CartridgeIdentity Identity) Synthetic(string gameCode, byte revision)
    {
        byte[] image = new byte[96];
        Encoding.ASCII.GetBytes(gameCode).CopyTo(image, CartridgeHeader.GameCodeOffset);
        image[CartridgeHeader.RevisionOffset] = revision;
        for (int i = CartridgeHeader.MinimumSize; i < image.Length; i++)
        {
            image[i] = (byte)(i * 13 + revision);
        }
        string hash = Sha(image);
        return (image, new CartridgeIdentity(gameCode, revision, "USA",
            $"Synthetic {gameCode}{revision}", image.Length, hash, CartridgeFamily.Hunters));
    }

    private static string Sha(byte[] bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class ForwardOnlyStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin loc)
            => throw new NotSupportedException();
    }
}
