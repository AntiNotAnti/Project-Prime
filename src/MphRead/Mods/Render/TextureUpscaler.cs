using System;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Allocation-bounded Scale2x texture enlargement for the original DS art.
    /// It preserves hard silhouette/pixel edges better than a blind bilinear
    /// resize and runs only when a texture is first uploaded.
    /// </summary>
    internal static class TextureUpscaler
    {
        private const int MaxDimension = 4096;
        private const long MaxPixels = 16_777_216;

        public static uint[] Scale(uint[] source, int width, int height, int factor,
            out int outWidth, out int outHeight)
        {
            outWidth = width;
            outHeight = height;
            if (factor <= 1 || width <= 0 || height <= 0
                || source.Length < width * height)
            {
                return source;
            }

            uint[] current = source;
            int currentWidth = width, currentHeight = height;
            int passes = factor >= 4 ? 2 : 1;
            for (int pass = 0; pass < passes; pass++)
            {
                int nextWidth = currentWidth * 2;
                int nextHeight = currentHeight * 2;
                if (nextWidth > MaxDimension || nextHeight > MaxDimension
                    || (long)nextWidth * nextHeight > MaxPixels)
                {
                    break;
                }
                uint[] next = Scale2x(current, currentWidth, currentHeight);
                current = next;
                currentWidth = nextWidth;
                currentHeight = nextHeight;
            }
            outWidth = currentWidth;
            outHeight = currentHeight;
            return current;
        }

        private static uint[] Scale2x(uint[] source, int width, int height)
        {
            int outWidth = width * 2;
            var output = new uint[outWidth * height * 2];
            for (int y = 0; y < height; y++)
            {
                int up = Math.Max(0, y - 1);
                int down = Math.Min(height - 1, y + 1);
                for (int x = 0; x < width; x++)
                {
                    int left = Math.Max(0, x - 1);
                    int right = Math.Min(width - 1, x + 1);
                    uint b = source[up * width + x];
                    uint d = source[y * width + left];
                    uint e = source[y * width + x];
                    uint f = source[y * width + right];
                    uint h = source[down * width + x];

                    uint e0 = d == b && d != h && b != f ? d : e;
                    uint e1 = b == f && b != d && f != h ? f : e;
                    uint e2 = d == h && d != b && h != f ? d : e;
                    uint e3 = h == f && d != h && b != f ? f : e;

                    int o = (y * 2) * outWidth + x * 2;
                    output[o] = e0;
                    output[o + 1] = e1;
                    output[o + outWidth] = e2;
                    output[o + outWidth + 1] = e3;
                }
            }
            return output;
        }
    }
}
