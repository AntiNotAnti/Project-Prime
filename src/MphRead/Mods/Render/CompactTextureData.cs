using System;
using System.Collections;
using System.Collections.Generic;

namespace MphRead.Mods.Render;

/// <summary>
/// Eagerly decoded, immutable texture texels with storage sized to the encoded
/// information: one byte for palette indices and two bytes for direct or
/// alpha-indexed texels. The arrays are owned and never exposed.
/// </summary>
internal sealed class CompactTextureData : IReadOnlyList<TextureData>
{
    private enum StorageKind : byte
    {
        PaletteIndex,
        DirectRgb,
        AlphaIndex
    }

    private readonly byte[]? _indices;
    private readonly ushort[]? _values;
    private readonly StorageKind _kind;
    private readonly bool _transparentIndexZero;

    private CompactTextureData(byte[] indices, bool transparentIndexZero)
    {
        _indices = indices;
        _kind = StorageKind.PaletteIndex;
        _transparentIndexZero = transparentIndexZero;
    }

    private CompactTextureData(ushort[] values, StorageKind kind)
    {
        _values = values;
        _kind = kind;
    }

    public int Count => _indices?.Length ?? _values!.Length;

    internal long StorageByteCount => _indices?.LongLength ?? _values!.LongLength * sizeof(ushort);

    public TextureData this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (_kind == StorageKind.PaletteIndex)
            {
                byte value = _indices![index];
                byte alpha = _transparentIndexZero && value == 0 ? (byte)0 : (byte)255;
                return new TextureData(value, alpha);
            }
            ushort packed = _values![index];
            if (_kind == StorageKind.DirectRgb)
            {
                byte alpha = (packed & 0x8000) == 0 ? (byte)0 : (byte)255;
                return new TextureData(packed, alpha);
            }
            return new TextureData((uint)(packed & 0xFF), (byte)(packed >> 8));
        }
    }

    internal static CompactTextureData Decode(Texture texture, ReadOnlySpan<byte> textureBytes)
    {
        int pixelCount = texture.Width * texture.Height;
        int entriesPerByte = texture.Format == TextureFormat.Palette2Bit ? 4
            : texture.Format == TextureFormat.Palette4Bit ? 2 : 1;
        if (pixelCount % entriesPerByte != 0)
        {
            throw new ProgramException($"Pixel count {pixelCount} is not divisible by {entriesPerByte}.");
        }
        pixelCount /= entriesPerByte;

        if (pixelCount <= 0)
        {
            return texture.Format == TextureFormat.DirectRgb
                ? new CompactTextureData(Array.Empty<ushort>(), StorageKind.DirectRgb)
                : texture.Format == TextureFormat.PaletteA5I3 || texture.Format == TextureFormat.PaletteA3I5
                    ? new CompactTextureData(Array.Empty<ushort>(), StorageKind.AlphaIndex)
                    : new CompactTextureData(Array.Empty<byte>(), IsTransparentIndexZero(texture));
        }

        if (texture.Format == TextureFormat.DirectRgb)
        {
            ValidateReadableRange(texture, textureBytes, pixelCount, sizeof(ushort));
            var values = new ushort[pixelCount];
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                values[pixelIndex] = Read.SpanReadUshort(textureBytes,
                    (int)(texture.ImageOffset + pixelIndex * 2));
            }
            return new CompactTextureData(values, StorageKind.DirectRgb);
        }

        if (texture.Format == TextureFormat.PaletteA5I3 || texture.Format == TextureFormat.PaletteA3I5)
        {
            ValidateReadableRange(texture, textureBytes, pixelCount, sizeof(byte));
            var values = new ushort[pixelCount];
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                byte entry = textureBytes[(int)(texture.ImageOffset + pixelIndex)];
                uint index = texture.Format == TextureFormat.PaletteA5I3
                    ? (uint)(entry & 0x7)
                    : (uint)(entry & 0x1F);
                byte alpha = texture.Format == TextureFormat.PaletteA5I3
                    ? AlphaFromA5I3(entry)
                    : AlphaFromA3I5(entry);
                values[pixelIndex] = (ushort)(index | ((uint)alpha << 8));
            }
            return new CompactTextureData(values, StorageKind.AlphaIndex);
        }

        ValidateReadableRange(texture, textureBytes, pixelCount, sizeof(byte));
        var indices = new byte[pixelCount * entriesPerByte];
        if (entriesPerByte == 1)
        {
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                indices[pixelIndex] = textureBytes[(int)(texture.ImageOffset + pixelIndex)];
            }
        }
        else
        {
            int mask = entriesPerByte == 4 ? 0x3 : 0xF;
            int bitsPerEntry = sizeof(byte) * 8 / entriesPerByte;
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                byte entry = textureBytes[(int)(texture.ImageOffset + pixelIndex)];
                int outputOffset = pixelIndex * entriesPerByte;
                for (int entryIndex = 0; entryIndex < entriesPerByte; entryIndex++)
                {
                    int shift = entryIndex * bitsPerEntry;
                    indices[outputOffset + entryIndex] = (byte)((entry >> shift) & mask);
                }
            }
        }
        return new CompactTextureData(indices, IsTransparentIndexZero(texture));
    }

    private static bool IsTransparentIndexZero(Texture texture)
        => texture.Opaque == 0 && (texture.Format == TextureFormat.Palette2Bit
            || texture.Format == TextureFormat.Palette4Bit || texture.Format == TextureFormat.Palette8Bit);

    private static void ValidateReadableRange(Texture texture, ReadOnlySpan<byte> textureBytes,
        int elementCount, int elementSize)
    {
        int lastElement = elementCount - 1;
        long lastOffset = (long)texture.ImageOffset + (long)lastElement * elementSize;
        if (lastOffset >= 0 && lastOffset <= textureBytes.Length - elementSize)
            return;

        // Preserve the old eager parser's span/indexer exception on truncated
        // input, before allocating an output proportional to the claimed size.
        for (int elementIndex = 0; elementIndex < elementCount; elementIndex++)
        {
            if (elementSize == sizeof(ushort))
                _ = Read.SpanReadUshort(textureBytes, (int)(texture.ImageOffset + elementIndex * 2));
            else
                _ = textureBytes[(int)(texture.ImageOffset + elementIndex)];
        }
    }

    private static byte AlphaFromA5I3(byte value)
        => (byte)MathF.Round((value >> 3) / 31.0f * 255.0f);

    private static byte AlphaFromA3I5(byte value)
        => (byte)MathF.Round((value >> 5) / 7.0f * 255.0f);

    public IEnumerator<TextureData> GetEnumerator()
    {
        for (int index = 0; index < Count; index++)
            yield return this[index];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
