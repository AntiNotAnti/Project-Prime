using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace MphRead;

/// <summary>
/// Immutable catalog and validator for exact whole-ROM identities.
///
/// The default catalog intentionally contains only identities with
/// independently verified whole-image evidence. Header-compatible releases
/// without a verified complete-image digest remain unsupported rather than
/// being accepted from their header alone.
/// </summary>
public sealed class CartridgeCatalog
{
    private const int HashBufferSize = 128 * 1024;

    public static readonly CartridgeCatalog Supported = new(
    [
        // Whole-image SHA-256 and 64 MiB length verified for the local AMHE1
        // reference dump and recorded in the Project Prime evidence set.
        new CartridgeIdentity("AMHE", 1, "USA", "Metroid Prime Hunters USA (Rev 1)",
            67108864L,
            "bcd9c2d408825589c35c6754c0efb547cbae78fbda9ce7f69500a9cab8e70b8f",
            CartridgeFamily.Hunters),
        // Whole-image SHA-256 and 64 MiB length independently verified for
        // the public AMHE0 reference image. This is not an ARM9 digest.
        new CartridgeIdentity("AMHE", 0, "USA", "Metroid Prime Hunters USA (Rev 0)",
            67108864L,
            "7d0a98ff98e1b7c985d1f3d89b01730af1b2115061a4dfea847612d217a8b855",
            CartridgeFamily.Hunters)
    ]);

    public ImmutableArray<CartridgeIdentity> Identities { get; }

    public CartridgeCatalog(IEnumerable<CartridgeIdentity> identities)
    {
        ArgumentNullException.ThrowIfNull(identities);
        CartridgeIdentity[] entries = identities.ToArray();
        if (entries.Length == 0)
        {
            throw new ArgumentException("A cartridge catalog must contain at least one identity.",
                nameof(identities));
        }

        var headers = new HashSet<(string GameCode, byte Revision)>();
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CartridgeIdentity identity in entries)
        {
            ArgumentNullException.ThrowIfNull(identity);
            if (!headers.Add((identity.GameCode, identity.Revision)))
            {
                throw new ArgumentException(
                    $"Duplicate cartridge header identity: {identity.VariantCode}.",
                    nameof(identities));
            }
            if (!hashes.Add(identity.Sha256))
            {
                throw new ArgumentException(
                    $"Duplicate cartridge SHA-256 identity: {identity.Sha256}.",
                    nameof(identities));
            }
        }

        Identities = entries.ToImmutableArray();
    }

    public bool TryGetIdentity(string gameCode, byte revision,
        [NotNullWhen(true)] out CartridgeIdentity? identity)
    {
        string normalized = gameCode?.ToUpperInvariant() ?? String.Empty;
        identity = Identities.FirstOrDefault(candidate =>
            candidate.Revision == revision
            && String.Equals(candidate.GameCode, normalized, StringComparison.Ordinal))!;
        return identity != null;
    }

    /// <summary>Validate a seekable or forward-only stream without closing it.</summary>
    public CartridgeValidationResult Validate(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return stream.CanSeek
                ? ValidateSeekable(stream)
                : ValidateForwardOnly(stream);
        }
        catch (IOException exception)
        {
            return ReadFailure(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ReadFailure(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return ReadFailure(exception.Message);
        }
        catch (ObjectDisposedException exception)
        {
            return ReadFailure(exception.Message);
        }
    }

    /// <summary>Open and validate one file, preserving read failures as a result.</summary>
    public CartridgeValidationResult ValidateFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Validate(stream);
        }
        catch (IOException exception)
        {
            return ReadFailure(exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return ReadFailure(exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return ReadFailure(exception.Message);
        }
        catch (ObjectDisposedException exception)
        {
            return ReadFailure(exception.Message);
        }
    }

    private CartridgeValidationResult ValidateSeekable(Stream stream)
    {
        long originalPosition = stream.Position;
        try
        {
            stream.Position = 0;
            long length = stream.Length;
            if (length < CartridgeHeader.MinimumSize)
            {
                return new CartridgeValidationResult(CartridgeValidationStatus.InvalidLength,
                    default, null, length, null,
                    $"The cartridge image is too short to contain an NDS header ({length} bytes).");
            }

            Span<byte> headerBytes = stackalloc byte[CartridgeHeader.MinimumSize];
            stream.ReadExactly(headerBytes);
            if (!CartridgeHeader.TryRead(headerBytes, out CartridgeHeader header))
            {
                return Unsupported(default, length, "The cartridge header is not a supported NDS header.");
            }
            if (!TryGetIdentity(header, out CartridgeIdentity? identity))
            {
                return Unsupported(header, length,
                    "No independently verified whole-image identity exists for this cartridge header.");
            }
            if (length != identity.Size)
            {
                return new CartridgeValidationResult(CartridgeValidationStatus.InvalidLength,
                    header, identity, length, null,
                    $"The image length is {length} bytes; the supported {identity.VariantCode} image is "
                    + $"{identity.Size} bytes.");
            }

            stream.Position = 0;
            byte[] digest = SHA256.HashData(stream);
            string actualSha256 = Convert.ToHexStringLower(digest);
            if (!CryptographicOperations.FixedTimeEquals(digest,
                    Convert.FromHexString(identity.Sha256)))
            {
                return new CartridgeValidationResult(CartridgeValidationStatus.HeaderValidWrongHash,
                    header, identity, length, actualSha256,
                    $"The {identity.VariantCode} header is valid, but the whole-image SHA-256 does not "
                    + "match the supported retail image.");
            }

            return new CartridgeValidationResult(CartridgeValidationStatus.Valid,
                header, identity, length, actualSha256, null);
        }
        finally
        {
            try { stream.Position = originalPosition; }
            catch (IOException) { }
            catch (NotSupportedException) { }
        }
    }

    private CartridgeValidationResult ValidateForwardOnly(Stream stream)
    {
        Span<byte> headerBytes = stackalloc byte[CartridgeHeader.MinimumSize];
        stream.ReadExactly(headerBytes);
        if (!CartridgeHeader.TryRead(headerBytes, out CartridgeHeader header))
        {
            return Unsupported(default, CartridgeHeader.MinimumSize,
                "The cartridge header is not a supported NDS header.");
        }
        if (!TryGetIdentity(header, out CartridgeIdentity? identity))
        {
            return Unsupported(header, CartridgeHeader.MinimumSize,
                "No independently verified whole-image identity exists for this cartridge header.");
        }

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(headerBytes);
        long length = headerBytes.Length;
        byte[] buffer = new byte[HashBufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) != 0)
        {
            hash.AppendData(buffer.AsSpan(0, read));
            length += read;
        }
        byte[] digest = hash.GetHashAndReset();
        string actualSha256 = Convert.ToHexStringLower(digest);
        if (length != identity.Size)
        {
            return new CartridgeValidationResult(CartridgeValidationStatus.InvalidLength,
                header, identity, length, actualSha256,
                $"The image length is {length} bytes; the supported {identity.VariantCode} image is "
                + $"{identity.Size} bytes.");
        }
        if (!CryptographicOperations.FixedTimeEquals(digest,
                Convert.FromHexString(identity.Sha256)))
        {
            return new CartridgeValidationResult(CartridgeValidationStatus.HeaderValidWrongHash,
                header, identity, length, actualSha256,
                $"The {identity.VariantCode} header is valid, but the whole-image SHA-256 does not "
                + "match the supported retail image.");
        }
        return new CartridgeValidationResult(CartridgeValidationStatus.Valid,
            header, identity, length, actualSha256, null);
    }

    private bool TryGetIdentity(CartridgeHeader header,
        [NotNullWhen(true)] out CartridgeIdentity? identity)
        => TryGetIdentity(header.GameCode, header.Revision, out identity);

    private CartridgeValidationResult Unsupported(CartridgeHeader header, long length, string error)
        => new(CartridgeValidationStatus.Unsupported, header, null, length, null, error);

    private static CartridgeValidationResult ReadFailure(string error)
        => new(CartridgeValidationStatus.ReadError, default, null, 0, null, error);
}
