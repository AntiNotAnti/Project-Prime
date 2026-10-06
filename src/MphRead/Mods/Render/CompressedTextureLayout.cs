namespace MphRead.Mods.Render;

internal static class CompressedTextureLayout
{
    // BC7, ETC2 RGBA8 and ASTC 4x4 all use 4x4 blocks. WebGPU requires
    // the base texture extent to align to these blocks, unlike Vulkan/GL.
    internal static bool HasAlignedBaseExtent(int width, int height)
        => width > 0 && height > 0 && width % 4 == 0 && height % 4 == 0;
}
