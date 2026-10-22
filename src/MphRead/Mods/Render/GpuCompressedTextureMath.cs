using System;

namespace MphRead.Mods.Render;

/// <summary>Pure 4x4 block and mip bounds shared by decode, upload and desktop tests.</summary>
internal static class GpuCompressedTextureMath
{
    internal static int LevelBytes(int width, int height)
    {
        ValidateDimensions(width, height);
        return checked(((width + 3) / 4) * ((height + 3) / 4) * 16);
    }

    internal static int MipLevelCount(int width, int height)
    {
        ValidateDimensions(width, height);
        int levels = 1;
        for (int dimension = Math.Max(width, height); dimension > 1; dimension >>= 1)
            levels++;
        return levels;
    }

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
            throw new ArgumentOutOfRangeException(nameof(width),
                "GPU-compressed image dimensions must be 1..8192.");
    }
}
