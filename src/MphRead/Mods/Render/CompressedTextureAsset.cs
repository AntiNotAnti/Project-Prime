#if ANDROID
using System;

namespace MphRead.Mods.Render;

/// <summary>GPU-native 4x4, 16-byte block textures retaining their authored mip chain.</summary>
internal sealed class CompressedTextureAsset : PreparedTextureAsset
{
    internal GpuTextureCompressionFormat Format { get; }
    internal CompressedTextureMip[] Mips { get; }

    internal CompressedTextureAsset(string key, TextureAssetClass assetClass,
        TextureAssetChannel channel, GpuTextureCompressionFormat format,
        CompressedTextureMip[] mips)
        : base(key, assetClass, channel, mips.Length == 0 ? 0 : mips[0].Width,
            mips.Length == 0 ? 0 : mips[0].Height)
    {
        if (format is not (GpuTextureCompressionFormat.Etc2Rgba8 or GpuTextureCompressionFormat.Astc4x4Rgba)
            || mips.Length == 0 || Width <= 0 || Height <= 0)
            throw new ArgumentException("Invalid compressed texture asset.", nameof(mips));
        for (int i = 0; i < mips.Length; i++)
        {
            int expectedW = Math.Max(1, Width >> i), expectedH = Math.Max(1, Height >> i);
            int expectedSize = checked(((expectedW + 3) / 4) * ((expectedH + 3) / 4) * 16);
            if (mips[i].Width != expectedW || mips[i].Height != expectedH
                || mips[i].Data == null || mips[i].Data.Length != expectedSize)
                throw new ArgumentException("Compressed texture mip chain is malformed.", nameof(mips));
        }
        Format = format;
        Mips = mips;
    }

    public override bool MipmapsAvailable => Mips.Length > 1
        && Mips[^1].Width == 1 && Mips[^1].Height == 1;

    // Every mip is uploaded, even if the sampler is set to a non-mipmap filter.
    public override long EstimateGpuBytes(bool mipmaps)
    {
        _ = mipmaps;
        long result = 0;
        foreach (CompressedTextureMip mip in Mips) result += mip.Data.Length;
        return result;
    }
}
#endif
