using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace MphRead;

/// <summary>
/// Versioned storage format for enhancement-pack RGBA8 data. Brotli reduces
/// distribution size; this is deliberately not described
/// as GPU block compression, and backend upload remains ordinary RGBA8.
/// </summary>
public static class EnhancedTextureCookedFormat
{
    private const uint Magic = 0x45545050; // PPTE, little-endian
    private const int Version = 1;
    private const int HeaderBytes = 52;

    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba8)
    {
        int decodedBytes = RenderTexturePixels.ValidateRgba8ByteCount(width,
            height);
        if (rgba8.Length != decodedBytes)
            throw new ArgumentException(
                "Cooked texture pixels must be tightly packed RGBA8.",
                nameof(rgba8));
        byte[] hash = SHA256.HashData(rgba8);
        using var output = new MemoryStream(HeaderBytes + decodedBytes / 2);
        Span<byte> header = stackalloc byte[HeaderBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(header, Magic);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], width);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], decodedBytes);
        hash.CopyTo(header[20..]);
        output.Write(header);
        using (var compressor = new BrotliStream(output,
            CompressionLevel.SmallestSize, leaveOpen: true))
        {
            compressor.Write(rgba8);
        }
        return output.ToArray();
    }

    public static bool TryDecode(ReadOnlySpan<byte> encoded, out int width,
        out int height, out byte[] rgba8)
    {
        rgba8 = Array.Empty<byte>();
        if (!TryReadHeader(encoded, out width, out height,
            out int declaredBytes)) return false;

        byte[] result = GC.AllocateUninitializedArray<byte>(declaredBytes);
        if (!BrotliDecoder.TryDecompress(encoded[HeaderBytes..], result,
                out int bytesWritten)
            || bytesWritten != declaredBytes
            || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(result), encoded.Slice(20, 32)))
        {
            width = height = 0;
            return false;
        }
        rgba8 = result;
        return true;
    }

    internal static bool TryReadHeader(ReadOnlySpan<byte> encoded,
        out int width, out int height, out int decodedBytes)
    {
        width = 0;
        height = 0;
        decodedBytes = 0;
        if (encoded.Length <= HeaderBytes
            || BinaryPrimitives.ReadUInt32LittleEndian(encoded) != Magic
            || BinaryPrimitives.ReadInt32LittleEndian(encoded[4..]) != Version)
            return false;
        width = BinaryPrimitives.ReadInt32LittleEndian(encoded[8..]);
        height = BinaryPrimitives.ReadInt32LittleEndian(encoded[12..]);
        decodedBytes = BinaryPrimitives.ReadInt32LittleEndian(encoded[16..]);
        int expectedBytes;
        try
        {
            expectedBytes = RenderTexturePixels.ValidateRgba8ByteCount(width,
                height);
        }
        catch (ArgumentException)
        {
            width = height = decodedBytes = 0;
            return false;
        }
        if (decodedBytes != expectedBytes
            || decodedBytes > EnhancementPackLimits.MaximumDecodedTextureBytes)
        {
            width = height = decodedBytes = 0;
            return false;
        }
        return true;
    }
}
