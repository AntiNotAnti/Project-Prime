using System;
using MphRead.Imaging;

namespace MphRead
{
    /// <summary>
    /// Renderer-owned immutable RGB8 decoder result. Keeping this value in the
    /// Client contract prevents linked content-preparation types from leaking
    /// through public renderer APIs when multiple compositions reference them.
    /// </summary>
    public sealed class DecodedRgbImage
    {
        private readonly byte[] _pixels;

        public DecodedRgbImage(int width, int height, ReadOnlyMemory<byte> pixels)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException(
                    "A decoded renderer image must contain exactly three RGB bytes per pixel.",
                    nameof(pixels));
            }
            long pixelCount = (long)width * height;
            if (pixelCount > Int32.MaxValue / 3L || pixels.Length != pixelCount * 3)
            {
                throw new ArgumentException(
                    "A decoded renderer image must contain exactly three RGB bytes per pixel.",
                    nameof(pixels));
            }
            Width = width;
            Height = height;
            _pixels = pixels.ToArray();
        }

        public int Width { get; }
        public int Height { get; }
        public ReadOnlyMemory<byte> Pixels => _pixels;

        internal static DecodedRgbImage DecodePng(ReadOnlyMemory<byte> encoded)
        {
            var decoded = StbImageDecoder.DecodeRgba(encoded);
            byte[] rgb = new byte[checked(decoded.Width * decoded.Height * 3)];
            ReadOnlySpan<byte> rgba = decoded.Pixels.Span;
            for (int source = 0, destination = 0; source < rgba.Length;
                source += 4, destination += 3)
            {
                rgb[destination] = rgba[source];
                rgb[destination + 1] = rgba[source + 1];
                rgb[destination + 2] = rgba[source + 2];
            }
            return new DecodedRgbImage(decoded.Width, decoded.Height, rgb);
        }
    }
}
