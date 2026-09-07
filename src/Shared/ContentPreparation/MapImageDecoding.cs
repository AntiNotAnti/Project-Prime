using System;

namespace MphRead.Mods.MapGen
{
    public sealed class RgbImage
    {
        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> Pixels { get; }
        public RgbImage(int width, int height, ReadOnlyMemory<byte> pixels)
        {
            if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 3))
            {
                throw new ArgumentException("A decoded image must contain exactly three RGB bytes per pixel.");
            }
            Width = width;
            Height = height;
            Pixels = pixels;
        }
    }

    public static class MapImageDecoding
    {
        // Installed by the host that supplies an actual image decoder.
        public static Func<ReadOnlyMemory<byte>, RgbImage>? Decoder { get; set; }
        public static RgbImage Decode(ReadOnlyMemory<byte> encoded)
        {
            var decoder = Decoder ?? throw new InvalidOperationException("Map texture baking requires a configured RGB image decoder.");
            return decoder(encoded);
        }
    }
}
