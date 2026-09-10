using System;
using System.IO;
using MphRead.Mods.MapGen;
using ReFuel.Stb;

namespace MphRead.Imaging
{
    public readonly record struct RgbaImage(int Width, int Height,
        ReadOnlyMemory<byte> Pixels);

    // Explicitly source-linked by the Client and Tools composition roots.
    public static class StbImageDecoder
    {
        public static RgbImage Decode(ReadOnlyMemory<byte> encoded)
        {
            using var source = new MemoryStream(encoded.ToArray());
            using StbImage image = StbImage.Load(source);
            return new RgbImage(image.Width, image.Height,
                ConvertPixels(image, includeAlpha: false));
        }

        public static RgbaImage DecodeRgba(ReadOnlyMemory<byte> encoded)
        {
            using var source = new MemoryStream(encoded.ToArray());
            using StbImage image = StbImage.Load(source);
            return new RgbaImage(image.Width, image.Height,
                ConvertPixels(image, includeAlpha: true));
        }

        private static byte[] ConvertPixels(StbImage image, bool includeAlpha)
        {
            int sourceComponents = (int)image.Format;
            if (sourceComponents is < 1 or > 4)
                throw new InvalidDataException("Decoded image has an unsupported component format.");
            ReadOnlySpan<byte> source = image.AsSpan<byte>();
            int pixelCount = checked(image.Width * image.Height);
            if (source.Length != checked(pixelCount * sourceComponents))
                throw new InvalidDataException("Decoded image has an invalid pixel buffer length.");
            int destinationComponents = includeAlpha ? 4 : 3;
            byte[] destination = GC.AllocateUninitializedArray<byte>(
                checked(pixelCount * destinationComponents));
            for (int pixel = 0; pixel < pixelCount; pixel++)
            {
                int sourceOffset = pixel * sourceComponents;
                byte red = source[sourceOffset];
                byte green = sourceComponents >= 3 ? source[sourceOffset + 1] : red;
                byte blue = sourceComponents >= 3 ? source[sourceOffset + 2] : red;
                int destinationOffset = pixel * destinationComponents;
                destination[destinationOffset] = red;
                destination[destinationOffset + 1] = green;
                destination[destinationOffset + 2] = blue;
                if (includeAlpha)
                {
                    destination[destinationOffset + 3] = sourceComponents switch
                    {
                        2 => source[sourceOffset + 1],
                        4 => source[sourceOffset + 3],
                        _ => (byte)255
                    };
                }
            }
            return destination;
        }
    }
}
