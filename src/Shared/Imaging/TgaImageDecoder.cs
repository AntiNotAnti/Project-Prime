using System;
using System.Buffers.Binary;
using System.IO;
using MphRead.Mods.MapGen;

namespace MphRead.Imaging
{
    /// <summary>Native-free truecolor TGA decoding for Quake map textures.</summary>
    public static class TgaImageDecoder
    {
        public static bool IsTruecolor(ReadOnlySpan<byte> bytes)
            => bytes.Length >= 18 && bytes[1] == 0 && (bytes[2] == 2 || bytes[2] == 10)
                && (bytes[16] == 24 || bytes[16] == 32);

        public static RgbImage Decode(ReadOnlyMemory<byte> encoded)
        {
            ReadOnlySpan<byte> bytes = encoded.Span;
            if (!IsTruecolor(bytes))
                throw new InvalidDataException("Expected a 24-bit or 32-bit truecolor TGA texture.");
            int width = BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..]);
            int height = BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..]);
            if (width == 0 || height == 0 || (bytes[17] & 0xC0) != 0)
                throw new InvalidDataException("The TGA texture dimensions or interleave mode are invalid.");
            int pixelCount = checked(width * height);
            int stride = bytes[16] / 8;
            int offset = 18 + bytes[0];
            int destination = 0;
            bool rle = bytes[2] == 10;
            // Validate the minimum possible payload before allocating from header dimensions.
            int minimumPayload = rle ? checked(((pixelCount + 127) / 128) * (stride + 1))
                : checked(pixelCount * stride);
            Require(bytes, offset, minimumPayload);
            byte[] rgb = new byte[checked(pixelCount * 3)];
            while (destination < pixelCount)
            {
                int count = 1;
                bool repeat = false;
                if (rle)
                {
                    Require(bytes, offset, 1);
                    byte header = bytes[offset++];
                    count = (header & 0x7F) + 1;
                    repeat = (header & 0x80) != 0;
                }
                if (count > pixelCount - destination)
                    throw new InvalidDataException("A TGA packet exceeds the texture dimensions.");
                Require(bytes, offset, checked((repeat ? 1 : count) * stride));
                for (int i = 0; i < count; i++)
                {
                    int source = offset + (repeat ? 0 : i * stride);
                    int x = destination % width;
                    int y = destination / width;
                    if ((bytes[17] & 0x10) != 0) x = width - 1 - x;
                    if ((bytes[17] & 0x20) == 0) y = height - 1 - y;
                    int target = (y * width + x) * 3;
                    rgb[target] = bytes[source + 2];
                    rgb[target + 1] = bytes[source + 1];
                    rgb[target + 2] = bytes[source];
                    destination++;
                }
                offset += (repeat ? 1 : count) * stride;
            }
            return new RgbImage(width, height, rgb);
        }

        private static void Require(ReadOnlySpan<byte> bytes, int offset, int count)
        {
            if (offset < 0 || count > bytes.Length - offset)
                throw new InvalidDataException("The TGA texture data is truncated.");
        }
    }
}
