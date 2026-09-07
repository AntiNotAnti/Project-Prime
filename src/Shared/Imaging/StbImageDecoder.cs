using System;
using System.IO;
using MphRead.Mods.MapGen;
using ReFuel.Stb;

namespace MphRead.Imaging
{
    // Explicitly source-linked by the Client and Tools composition roots.
    public static class StbImageDecoder
    {
        public static RgbImage Decode(ReadOnlyMemory<byte> encoded)
        {
            using var source = new MemoryStream(encoded.ToArray());
            using StbImage image = StbImage.Load(source, StbiImageFormat.Rgb);
            return new RgbImage(image.Width, image.Height, image.AsSpan<byte>().ToArray());
        }
    }
}
