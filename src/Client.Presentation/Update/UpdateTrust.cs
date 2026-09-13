using System;
using System.Security.Cryptography;

namespace MphRead.Mods.Update;

/// <summary>
/// Verification for the signed first-party update manifest.
///
/// The client carries only this public SubjectPublicKeyInfo. The corresponding
/// private key is a release-CI secret and is intentionally not represented in
/// source or in a player package.
/// </summary>
public sealed class UpdateTrust
{
    // ECDSA P-256 SPKI, base64 encoded. This is public material, not a secret.
    // Rotate it only as an explicit release-key migration with an overlapping
    // client release; P0 deliberately has one pinned key.
    public const string PinnedPublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEgW9SnMfmlcXJvYxszMiguqy3gkKjy9ge7srMXe1oCpr/Tq8Ii03k4AxnMMM0FedFjMkhcKeG5p1omkng2NbX9g==";

    private readonly byte[] _subjectPublicKeyInfo;

    public UpdateTrust()
        : this(Convert.FromBase64String(PinnedPublicKeySpkiBase64)) { }

    /// <summary>
    /// Test seam for a separately generated public key. Production callers use
    /// the parameterless constructor so a settings file cannot replace trust.
    /// </summary>
    public UpdateTrust(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        if (subjectPublicKeyInfo.Length is < 32 or > 512)
            throw new ArgumentException("invalid ECDSA public key length", nameof(subjectPublicKeyInfo));
        _subjectPublicKeyInfo = subjectPublicKeyInfo.ToArray();
        using ECDsa ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out int consumed);
            if (consumed != _subjectPublicKeyInfo.Length || ecdsa.KeySize != 256)
                throw new ArgumentException("public key is not an ECDSA P-256 SPKI", nameof(subjectPublicKeyInfo));
        }
        catch (CryptographicException ex)
        {
            throw new ArgumentException("public key is not an ECDSA key", nameof(subjectPublicKeyInfo), ex);
        }
    }

    public bool VerifyManifest(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> signature)
    {
        if (manifest.Length == 0 || manifest.Length > UpdateManifestValidator.MaxManifestBytes
            || signature.Length == 0 || signature.Length > UpdateManifestValidator.MaxSignatureBytes)
            return false;
        if (!TryValidateRfc3279Signature(signature))
            return false;
        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out int consumed);
            if (consumed != _subjectPublicKeyInfo.Length || ecdsa.KeySize != 256)
                return false;
            byte[] hash = SHA256.HashData(manifest);
            // Explicit DER is important: the CI signature is the RFC 3279
            // SEQUENCE(INTEGER r, INTEGER s), not IEEE P1363 r||s bytes.
            // The explicit format overload keeps the CI/client contract
            // stable across crypto providers.
            return ecdsa.VerifyHash(hash, signature.ToArray(),
                DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static bool VerifyPinnedManifest(ReadOnlySpan<byte> manifest,
        ReadOnlySpan<byte> signature) => new UpdateTrust().VerifyManifest(manifest, signature);

    /// <summary>
    /// Require a complete, minimally encoded DER sequence before handing it to
    /// a platform crypto provider. This rejects truncated, empty, trailing, or
    /// non-RFC3279 encodings consistently across Windows, Linux, macOS and
    /// Android providers.
    /// </summary>
    internal static bool TryValidateRfc3279Signature(ReadOnlySpan<byte> value)
    {
        if (value.Length < 8 || value[0] != 0x30) return false;
        if (!TryReadDerLength(value[1..], out int sequenceLength, out int lengthBytes))
            return false;
        int sequenceStart = 1 + lengthBytes;
        if (sequenceLength != value.Length - sequenceStart) return false;
        int offset = sequenceStart;
        if (!TryReadInteger(value, ref offset, out _)
            || !TryReadInteger(value, ref offset, out _)
            || offset != value.Length)
            return false;
        return true;
    }

    private static bool TryReadInteger(ReadOnlySpan<byte> value, ref int offset,
        out ReadOnlySpan<byte> integer)
    {
        integer = default;
        if (offset >= value.Length || value[offset++] != 0x02) return false;
        if (!TryReadDerLength(value[offset..], out int length, out int lengthBytes))
            return false;
        offset += lengthBytes;
        if (length is <= 0 or > 33 || offset + length > value.Length) return false;
        integer = value.Slice(offset, length);
        // Positive INTEGERs may have one sign-protection zero, but must not
        // have redundant leading zeros. P-256 values are at most 32 bytes.
        if (integer[0] >= 0x80) return false;
        if (integer.Length > 1 && integer[0] == 0 && integer[1] < 0x80) return false;
        if (integer.Length == 33 && integer[0] != 0) return false;
        offset += length;
        return true;
    }

    private static bool TryReadDerLength(ReadOnlySpan<byte> value, out int length,
        out int bytes)
    {
        length = 0;
        bytes = 0;
        if (value.Length == 0) return false;
        byte first = value[0];
        if ((first & 0x80) == 0)
        {
            length = first;
            bytes = 1;
            return true;
        }
        int count = first & 0x7f;
        // RFC 3279 signatures are tiny. Reject indefinite and non-minimal
        // long-form lengths, which also avoids integer overflow.
        if (count is <= 0 or > 2 || value.Length < count + 1
            || value[1] == 0)
            return false;
        int parsed = 0;
        for (int i = 0; i < count; i++)
        {
            parsed = (parsed << 8) | value[i + 1];
        }
        if (parsed < 128) return false;
        length = parsed;
        bytes = count + 1;
        return true;
    }
}
