using System;

namespace MphRead.Mods.Render;

/// <summary>Logical texture capacity, excluding driver allocation padding and private memory.</summary>
internal static class TextureStorageMath
{
    internal static long Bytes(int width, int height, int mips, int bytesPerUnit, bool blocks4x4)
    {
        if (width <= 0 || height <= 0 || mips <= 0 || bytesPerUnit <= 0) return 0;
        long bytes = 0;
        for (int mip = 0; mip < mips; mip++)
        {
            int x = blocks4x4 ? (int)(((long)width + 3) / 4) : width;
            int y = blocks4x4 ? (int)(((long)height + 3) / 4) : height;
            bytes = checked(bytes + (long)x * y * bytesPerUnit);
            width = Math.Max(1, width / 2); height = Math.Max(1, height / 2);
        }
        return bytes;
    }
}
