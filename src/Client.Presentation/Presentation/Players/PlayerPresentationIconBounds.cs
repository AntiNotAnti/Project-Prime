using System.Collections.Generic;

namespace MphRead.Entities
{
    /// <summary>Where the drawing sits inside one frame of a HUD sheet, in that frame's own pixels.</summary>
    public readonly struct IconBounds
    {
        public readonly int MinX;
        public readonly int MinY;
        public readonly int MaxX;
        public readonly int MaxY;
        public IconBounds(int minX, int minY, int maxX, int maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;
        public float CentreX => (MinX + MaxX + 1) / 2f;
        public float CentreY => (MinY + MaxY + 1) / 2f;
    }
}
