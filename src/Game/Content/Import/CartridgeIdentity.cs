using System;

namespace MphRead;

/// <summary>The content family a cartridge belongs to.</summary>
public enum CartridgeFamily : byte
{
    Hunters,
    FirstHunt
}

/// <summary>The outcome of validating a complete cartridge image.</summary>
public enum CartridgeValidationStatus : byte
{
    Valid,
    Unsupported,
    HeaderValidWrongHash,
    InvalidLength,
    ReadError
}

/// <summary>
/// The small part of an NDS header used to select a cartridge identity. The
/// game code is the four-byte header value (for example, <c>AMHE</c>); the
/// revision is the separate ROM-version byte.
/// </summary>
public readonly record struct CartridgeHeader(string GameCode, byte Revision)
{
    public const int MinimumSize = 0x1F;
    public const int GameCodeOffset = 0x0C;
    public const int RevisionOffset = 0x1E;

    public string VariantCode => $"{GameCode}{Revision}";

    public static bool TryRead(ReadOnlySpan<byte> bytes, out CartridgeHeader header)
    {
        header = default;
        if (bytes.Length < MinimumSize)
        {
            return false;
        }

        ReadOnlySpan<byte> gameCode = bytes.Slice(GameCodeOffset, 4);
        for (int i = 0; i < gameCode.Length; i++)
        {
            byte value = gameCode[i];
            if (!((value >= (byte)'A' && value <= (byte)'Z')
                || (value >= (byte)'0' && value <= (byte)'9')))
            {
                return false;
            }
        }

        header = new CartridgeHeader(
            System.Text.Encoding.ASCII.GetString(gameCode),
            bytes[RevisionOffset]);
        return true;
    }
}

/// <summary>
/// Immutable identity of one exact, whole cartridge image. A header identity
/// alone is not sufficient: the size and SHA-256 digest identify the bytes
/// that may be extracted.
/// </summary>
public sealed record CartridgeIdentity
{
    public CartridgeIdentity(string gameCode, byte revision, string region,
        string displayName, long size, string sha256, CartridgeFamily family)
    {
        if (String.IsNullOrWhiteSpace(gameCode) || gameCode.Length != 4)
        {
            throw new ArgumentException("A cartridge game code must contain four characters.",
                nameof(gameCode));
        }
        if (String.IsNullOrWhiteSpace(region))
        {
            throw new ArgumentException("A cartridge region is required.", nameof(region));
        }
        if (String.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("A cartridge display name is required.", nameof(displayName));
        }
        if (size <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "A cartridge size must be positive.");
        }
        if (!Enum.IsDefined(family))
        {
            throw new ArgumentOutOfRangeException(nameof(family));
        }

        GameCode = gameCode.ToUpperInvariant();
        Revision = revision;
        Region = region;
        DisplayName = displayName;
        Size = size;
        Sha256 = NormalizeSha256(sha256);
        Family = family;
    }

    public string GameCode { get; }
    public byte Revision { get; }
    public string Region { get; }
    public string DisplayName { get; }
    public long Size { get; }
    public string Sha256 { get; }
    public CartridgeFamily Family { get; }

    public string VariantCode => $"{GameCode}{Revision}";

    public bool MatchesHeader(CartridgeHeader header)
        => String.Equals(GameCode, header.GameCode, StringComparison.OrdinalIgnoreCase)
            && Revision == header.Revision;

    internal static string NormalizeSha256(string sha256)
    {
        if (String.IsNullOrWhiteSpace(sha256) || sha256.Length != 64)
        {
            throw new ArgumentException("A cartridge SHA-256 digest must contain 64 hexadecimal characters.",
                nameof(sha256));
        }
        for (int i = 0; i < sha256.Length; i++)
        {
            if (!Uri.IsHexDigit(sha256[i]))
            {
                throw new ArgumentException("A cartridge SHA-256 digest must be hexadecimal.",
                    nameof(sha256));
            }
        }
        return sha256.ToLowerInvariant();
    }
}

/// <summary>The result of checking one stream against a cartridge catalog.</summary>
public sealed record CartridgeValidationResult(
    CartridgeValidationStatus Status,
    CartridgeHeader Header,
    CartridgeIdentity? Identity,
    long ActualLength,
    string? ActualSha256,
    string? Error)
{
    public bool IsValid => Status == CartridgeValidationStatus.Valid;
    public bool HasValidHeader => Header.GameCode is { Length: 4 };
}
