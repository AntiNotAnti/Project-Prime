using System.Collections.Generic;
using MphRead.Hud;

namespace MphRead.Entities
{
    public partial class PlayerPresentation
    {
        public static IconBounds ModIconBounds(IReadOnlyList<byte> data, int frame, int width, int height)
        {
            int tilesX = width / 8;
            int image = frame * width * height;
            int minX = width;
            int minY = height;
            int maxX = -1;
            int maxY = -1;
            for (int y = 0; y < height; y++)
            {
                int ty = y / 8;
                int py = y % 8;
                for (int x = 0; x < width; x++)
                {
                    int index = image + ty * tilesX * 64 + x / 8 * 64 + py * 8 + x % 8;
                    if (index < 0 || index >= data.Count || data[index] == 0)
                    {
                        continue;
                    }

                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }

            if (maxX < minX || maxY < minY)
            {
                // An empty frame. The whole of it, so the caller's arithmetic
                // has something with a size in it rather than a division by
                // zero.
                return new IconBounds(0, 0, width - 1, height - 1);
            }

            return new IconBounds(minX, minY, maxX, maxY);
        }
    }
}
