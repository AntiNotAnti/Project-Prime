using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace MphRead.Mods.Import.Retro;

/// <summary>
/// Bounded, detached Retro TXTR source conversion. Tile layout follows the
/// MIT Second Hunt reference 41452f2, Import/Retro/Txtr.cs (Copyright 2020
/// NoneGiven). Pixels use an explicit expanded-channel, 3:5 CMPR contract;
/// this is not that reference's floor-scaled DXT1 preview conversion.
/// No IO, publication, GL, row flip, material or sampler policy lives here.
/// </summary>
public static class RetroTextureSource
{
    public const int MaxDimension = 4096;
    public const int MaxMipCount = 13;
    public const int MaxSourceBytes = 64 * 1024 * 1024;
    public const int MaxOutputBytes = 64 * 1024 * 1024;
    public const string PixelContract = "gx-expanded-cmpr-3-5-rgb5a3-v1";

    public sealed record Mip(int Width, int Height, int Offset, int Length);
    public sealed record Layout(uint Format, int Width, int Height, int MipCount,
        int PayloadEnd, int PaddingBytes, int TopRgbaBytes, IReadOnlyList<Mip> Mips);
    public sealed class Decoded
    {
        private readonly byte[] _pixels;
        public Layout Layout { get; }
        public ReadOnlySpan<byte> Pixels => _pixels;
        internal Decoded(Layout layout, byte[] pixels) => (Layout, _pixels) = (layout, pixels);
    }

    /// <summary>Validates every declared tiled mip, even for top-only callers.</summary>
    public static Layout Inspect(ReadOnlySpan<byte> source, int outputByteBudget = MaxOutputBytes)
    {
        if (outputByteBudget is < 1 or > MaxOutputBytes)
            throw new ArgumentOutOfRangeException(nameof(outputByteBudget));
        Require(source.Length is >= 12 and <= MaxSourceBytes, "TXTR source size exceeds its bounds.");
        uint format = BinaryPrimitives.ReadUInt32BigEndian(source);
        if (format is not (8 or 10))
            throw new NotSupportedException("Unsupported Retro TXTR format: " + format);
        int width = BinaryPrimitives.ReadUInt16BigEndian(source[4..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(source[6..]);
        Require(width is >= 1 and <= MaxDimension && height is >= 1 and <= MaxDimension,
            "TXTR dimensions exceed their bounds.");
        long rgbaBytes = checked((long)width * height * 4);
        Require(rgbaBytes <= outputByteBudget, "TXTR decoded output exceeds its budget.");
        uint count = BinaryPrimitives.ReadUInt32BigEndian(source[8..]);
        int maximumMips = 1;
        for (int size = Math.Max(width, height); size > 1; size >>= 1) maximumMips++;
        Require(count >= 1 && count <= MaxMipCount && count <= maximumMips,
            "TXTR mip count exceeds its dimensions or limit.");
        var mips = new Mip[(int)count];
        int tile = format == 10 ? 8 : 4;
        long offset = 12;
        int mipWidth = width, mipHeight = height;
        for (int i = 0; i < mips.Length; i++)
        {
            long length = checked(((mipWidth + tile - 1L) / tile)
                * ((mipHeight + tile - 1L) / tile) * 32);
            long end = checked(offset + length);
            Require(end <= source.Length && end <= MaxSourceBytes, "TXTR tiled mip is truncated.");
            mips[i] = new(mipWidth, mipHeight, checked((int)offset), checked((int)length));
            offset = end;
            mipWidth = Math.Max(1, mipWidth >> 1);
            mipHeight = Math.Max(1, mipHeight >> 1);
        }
        int payloadEnd = checked((int)offset);
        int padding = source.Length - payloadEnd;
        if (padding != 0)
        {
            // Verified uncompressed PAK entries retain their 32-byte-aligned
            // uniform FF suffix. Compressed resources end exactly at the chain.
            Require(source.Length == checked((payloadEnd + 31) & ~31), "Unexpected TXTR trailing extent.");
            foreach (byte value in source[payloadEnd..])
                Require(value == 0xff, "Unexpected TXTR alignment padding.");
        }
        return new(format, width, height, mips.Length, payloadEnd, padding,
            checked((int)rgbaBytes), Array.AsReadOnly(mips));
    }

    /// <summary>Returns top-first straight RGBA8 without retaining source memory.</summary>
    public static Decoded DecodeTop(ReadOnlySpan<byte> source, int outputByteBudget = MaxOutputBytes)
    {
        Layout layout = Inspect(source, outputByteBudget);
        var pixels = new byte[layout.TopRgbaBytes];
        ReadOnlySpan<byte> top = source.Slice(layout.Mips[0].Offset, layout.Mips[0].Length);
        if (layout.Format == 10) DecodeCmpr(top, layout.Width, layout.Height, pixels);
        else DecodeRgb5A3(top, layout.Width, layout.Height, pixels);
        return new(layout, pixels);
    }

    private static int Expand5(int value) => (value << 3) | (value >> 2);
    private static int Expand6(int value) => (value << 2) | (value >> 4);
    private static void Rgb565(ushort value, Span<byte> rgba)
    {
        rgba[0] = (byte)Expand5(value >> 11);
        rgba[1] = (byte)Expand6((value >> 5) & 63);
        rgba[2] = (byte)Expand5(value & 31);
        rgba[3] = 255;
    }

    private static void DecodeCmpr(ReadOnlySpan<byte> source, int width, int height, Span<byte> pixels)
    {
        int offset = 0;
        Span<byte> palette = stackalloc byte[16];
        for (int tileY = 0; tileY < height; tileY += 8)
        for (int tileX = 0; tileX < width; tileX += 8)
        for (int block = 0; block < 4; block++)
        {
            ReadOnlySpan<byte> encoded = source.Slice(offset, 8);
            offset += 8;
            ushort a = BinaryPrimitives.ReadUInt16BigEndian(encoded);
            ushort b = BinaryPrimitives.ReadUInt16BigEndian(encoded[2..]);
            Rgb565(a, palette);
            Rgb565(b, palette[4..]);
            for (int channel = 0; channel < 3; channel++)
            {
                int first = palette[channel], second = palette[4 + channel];
                palette[8 + channel] = (byte)(a > b ? (5 * first + 3 * second) >> 3 : (first + second) >> 1);
                palette[12 + channel] = (byte)(a > b ? (3 * first + 5 * second) >> 3 : (first + second) >> 1);
            }
            palette[11] = 255;
            palette[15] = (byte)(a > b ? 255 : 0);
            uint selectors = BinaryPrimitives.ReadUInt32BigEndian(encoded[4..]);
            int blockX = tileX + (block & 1) * 4, blockY = tileY + (block >> 1) * 4;
            for (int i = 0; i < 16; i++)
            {
                int x = blockX + (i & 3), y = blockY + (i >> 2);
                if (x >= width || y >= height) continue;
                int color = (int)((selectors >> (30 - 2 * i)) & 3);
                palette.Slice(color * 4, 4).CopyTo(pixels.Slice((y * width + x) * 4, 4));
            }
        }
    }

    private static void DecodeRgb5A3(ReadOnlySpan<byte> source, int width, int height, Span<byte> pixels)
    {
        int offset = 0;
        for (int tileY = 0; tileY < height; tileY += 4)
        for (int tileX = 0; tileX < width; tileX += 4)
        for (int i = 0; i < 16; i++)
        {
            ushort value = BinaryPrimitives.ReadUInt16BigEndian(source[offset..]);
            offset += 2;
            int x = tileX + (i & 3), y = tileY + (i >> 2);
            if (x >= width || y >= height) continue;
            Span<byte> color = pixels.Slice((y * width + x) * 4, 4);
            if ((value & 0x8000) != 0)
            {
                color[0] = (byte)Expand5((value >> 10) & 31);
                color[1] = (byte)Expand5((value >> 5) & 31);
                color[2] = (byte)Expand5(value & 31);
                color[3] = 255;
            }
            else
            {
                int alpha = value >> 12;
                color[0] = (byte)(((value >> 8) & 15) * 17);
                color[1] = (byte)(((value >> 4) & 15) * 17);
                color[2] = (byte)((value & 15) * 17);
                color[3] = (byte)((alpha << 5) | (alpha << 2) | (alpha >> 1));
            }
        }
    }

    private static void Require(bool condition, string detail)
    { if (!condition) throw new InvalidDataException(detail); }
}
