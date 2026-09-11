using System;
using System.Security.Cryptography;

namespace MphRead.Mods.Network;

/// <summary>Direction-specific domain for an authenticated UDP datagram.</summary>
public enum NetAuthDirection : byte
{
    ClientToServer = 1,
    ServerToClient = 2
}

/// <summary>
/// Allocation-free integrity envelope for established UDP traffic. The tag is
/// carried after the application payload and covers the protocol version,
/// direction domain, complete header and complete payload.
/// </summary>
public static class NetAuthentication
{
    public const int KeySize = 32;
    public const int TagSize = 16;
    public const int CounterSize = sizeof(ulong);
    public static int MaximumPayloadSize => NetConfig.MaxPacketSize - NetHeader.Size - TagSize;

    // Keep the context explicit and stable. The direction byte below prevents
    // a client-to-server tag from being reflected into the reverse direction.
    private static ReadOnlySpan<byte> DomainPrefix => "ProjectPrime.UDP.Auth.v1"u8;

    public static int AuthenticatedSize(int payloadLength)
    {
        if (payloadLength < 0 || payloadLength > MaximumPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(payloadLength));
        return NetHeader.Size + payloadLength + TagSize;
    }

    /// <summary>
    /// Writes an exact authenticated datagram into caller-owned storage. No
    /// packet-sized managed buffer is created on this path.
    /// </summary>
    public static bool TrySign(ReadOnlySpan<byte> key, NetAuthDirection direction,
        in NetHeader header, ReadOnlySpan<byte> payload, Span<byte> destination, out int length)
    {
        length = 0;
        if (!ValidKey(key) || !ValidDirection(direction)
            || payload.Length > MaximumPayloadSize)
            return false;
        int total = NetHeader.Size + payload.Length + TagSize;
        if (destination.Length < total) return false;
        header.Write(destination);
        payload.CopyTo(destination[NetHeader.Size..]);
        ComputeTag(key, direction, destination[..NetHeader.Size], payload,
            destination.Slice(NetHeader.Size + payload.Length, TagSize));
        length = total;
        return true;
    }

    /// <summary>Throws on programmer/configuration errors and writes one packet.</summary>
    public static int Sign(ReadOnlySpan<byte> key, NetAuthDirection direction,
        in NetHeader header, ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        if (!ValidKey(key)) throw new ArgumentException("UDP authentication keys must be 32 bytes.", nameof(key));
        if (!ValidDirection(direction)) throw new ArgumentOutOfRangeException(nameof(direction));
        if (payload.Length > MaximumPayloadSize)
            throw new ArgumentOutOfRangeException(nameof(payload));
        if (!TrySign(key, direction, header, payload, destination, out int length))
            throw new ArgumentException("Destination is too small for the authenticated datagram.", nameof(destination));
        return length;
    }

    /// <summary>
    /// Verifies a complete datagram without changing any connection state. The
    /// returned payload aliases <paramref name="datagram"/> and excludes the tag.
    /// </summary>
    public static bool TryVerify(ReadOnlySpan<byte> key, NetAuthDirection direction,
        ReadOnlySpan<byte> datagram, out NetHeader header, out ReadOnlySpan<byte> payload)
    {
        header = default;
        payload = default;
        if (!ValidKey(key) || !ValidDirection(direction)
            || datagram.Length < NetHeader.Size + TagSize
            || datagram.Length > NetConfig.MaxPacketSize
            || !NetHeader.TryRead(datagram, out header))
            return false;
        int payloadLength = datagram.Length - NetHeader.Size - TagSize;
        payload = datagram.Slice(NetHeader.Size, payloadLength);
        ReadOnlySpan<byte> receivedTag = datagram[^TagSize..];
        Span<byte> expected = stackalloc byte[TagSize];
        ComputeTag(key, direction, datagram[..NetHeader.Size], payload, expected);
        if (!CryptographicOperations.FixedTimeEquals(expected, receivedTag))
        {
            header = default;
            payload = default;
            return false;
        }
        return true;
    }

    private static bool ValidKey(ReadOnlySpan<byte> key) => key.Length == KeySize;

    private static bool ValidDirection(NetAuthDirection direction)
        => direction is NetAuthDirection.ClientToServer or NetAuthDirection.ServerToClient;

    private static void ComputeTag(ReadOnlySpan<byte> key, NetAuthDirection direction,
        ReadOnlySpan<byte> header, ReadOnlySpan<byte> payload, Span<byte> tag)
    {
        int contextLength = DomainPrefix.Length + 2 + header.Length + payload.Length;
        Span<byte> context = stackalloc byte[contextLength];
        int offset = 0;
        DomainPrefix.CopyTo(context);
        offset += DomainPrefix.Length;
        context[offset++] = NetHeader.Version;
        context[offset++] = (byte)direction;
        header.CopyTo(context[offset..]);
        offset += header.Length;
        payload.CopyTo(context[offset..]);
        Span<byte> digest = stackalloc byte[32];
        HMACSHA256.HashData(key, context, digest);
        digest[..TagSize].CopyTo(tag);
    }
}
