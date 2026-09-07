using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using MphRead.Imaging;
using MphRead.Mods.MapGen;
using Xunit;

namespace MphRead.Imaging.Tests;

public sealed class TgaImageDecoderTests
{
    [Theory]
    [InlineData(24, false, false, false)]
    [InlineData(24, false, false, true)]
    [InlineData(24, false, true, false)]
    [InlineData(24, false, true, true)]
    [InlineData(24, true, false, false)]
    [InlineData(24, true, false, true)]
    [InlineData(24, true, true, false)]
    [InlineData(24, true, true, true)]
    [InlineData(32, false, false, false)]
    [InlineData(32, false, false, true)]
    [InlineData(32, false, true, false)]
    [InlineData(32, false, true, true)]
    [InlineData(32, true, false, false)]
    [InlineData(32, true, false, true)]
    [InlineData(32, true, true, false)]
    [InlineData(32, true, true, true)]
    public void DecodesTruecolorTgaAcrossOriginsAndPacketModes(int depth, bool rle, bool rightOrigin, bool topOrigin)
    {
        RgbImage image = TgaImageDecoder.Decode(BuildTga(depth, rle, rightOrigin, topOrigin));

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(ExpectedRgb, image.Pixels.ToArray());
    }

    [Fact]
    public void MapImageDecodingCanUseTheManagedTgaFallback()
    {
        Func<ReadOnlyMemory<byte>, RgbImage>? previous = MapImageDecoding.Decoder;
        try
        {
            MapImageDecoding.Decoder = TgaImageDecoder.Decode;
            RgbImage image = MapImageDecoding.Decode(BuildTga(32, rle: true, rightOrigin: true, topOrigin: false));

            Assert.Equal(ExpectedRgb, image.Pixels.ToArray());
        }
        finally
        {
            MapImageDecoding.Decoder = previous;
        }
    }

    [Fact]
    public void RejectsTruncatedAndOverlongTgaPackets()
    {
        byte[] valid = BuildTga(24, rle: false, rightOrigin: false, topOrigin: true);
        Assert.Throws<InvalidDataException>(() => TgaImageDecoder.Decode(valid.AsMemory(0, valid.Length - 1)));

        byte[] overlong = BuildTga(24, rle: true, rightOrigin: false, topOrigin: true);
        overlong[18] = 0x84; // Five-pixel packet for a four-pixel image.
        Assert.Throws<InvalidDataException>(() => TgaImageDecoder.Decode(overlong));
    }

    private static readonly byte[] ExpectedRgb =
    {
        0xFF, 0x00, 0x00, 0xFF, 0x00, 0x00,
        0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF
    };

    private static readonly Pixel[] LogicalPixels =
    {
        new(0xFF, 0x00, 0x00), new(0xFF, 0x00, 0x00),
        new(0x00, 0x00, 0xFF), new(0xFF, 0xFF, 0xFF)
    };

    private static byte[] BuildTga(int depth, bool rle, bool rightOrigin, bool topOrigin)
    {
        int descriptor = (rightOrigin ? 0x10 : 0) | (topOrigin ? 0x20 : 0);
        var pixels = new List<Pixel>(LogicalPixels.Length);
        for (int fileY = 0; fileY < 2; fileY++)
        {
            int logicalY = topOrigin ? fileY : 1 - fileY;
            for (int fileX = 0; fileX < 2; fileX++)
            {
                int logicalX = rightOrigin ? 1 - fileX : fileX;
                pixels.Add(LogicalPixels[logicalY * 2 + logicalX]);
            }
        }

        var bytes = new List<byte>(64)
        {
            0, 0, (byte)(rle ? 10 : 2), 0, 0, 0, 0, 0, 0, 0, 0, 0
        };
        Span<byte> dimensions = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(dimensions, 2);
        bytes.AddRange(dimensions.ToArray());
        bytes.AddRange(dimensions.ToArray());
        bytes.Add((byte)depth);
        bytes.Add((byte)descriptor);

        if (!rle)
        {
            foreach (Pixel pixel in pixels) AddPixel(bytes, pixel, depth);
            return bytes.ToArray();
        }

        for (int index = 0; index < pixels.Count;)
        {
            int run = 1;
            while (index + run < pixels.Count && run < 128 && pixels[index + run].Equals(pixels[index])) run++;
            if (run > 1)
            {
                bytes.Add((byte)(0x80 | (run - 1)));
                AddPixel(bytes, pixels[index], depth);
                index += run;
                continue;
            }

            int rawStart = index++;
            while (index < pixels.Count && index - rawStart < 128)
            {
                if (index + 1 < pixels.Count && pixels[index + 1].Equals(pixels[index])) break;
                index++;
            }
            bytes.Add((byte)(index - rawStart - 1));
            for (int raw = rawStart; raw < index; raw++) AddPixel(bytes, pixels[raw], depth);
        }
        return bytes.ToArray();
    }

    private static void AddPixel(List<byte> bytes, Pixel pixel, int depth)
    {
        bytes.Add(pixel.B);
        bytes.Add(pixel.G);
        bytes.Add(pixel.R);
        if (depth == 32) bytes.Add(0xA5);
    }

    private readonly record struct Pixel(byte R, byte G, byte B);
}
