using System;

namespace MphRead.Mods.Render;

/// <summary>Authored contiguous RGBA levels; never regenerate artist-authored lower levels.</summary>
internal sealed class RgbaMipTextureAsset : PreparedTextureAsset
{
    internal CompressedTextureMip[] Mips { get; }

    internal RgbaMipTextureAsset(string key, TextureAssetClass assetClass,
        TextureAssetChannel channel, CompressedTextureMip[] mips)
        : base(key, assetClass, channel, mips.Length == 0 ? 0 : mips[0].Width,
            mips.Length == 0 ? 0 : mips[0].Height)
    {
        if (mips.Length == 0 || mips.Length > GpuCompressedTextureMath.MipLevelCount(Width, Height))
            throw new ArgumentException("Invalid authored RGBA mip count.", nameof(mips));
        for (int i = 0; i < mips.Length; i++)
        {
            int width = Math.Max(1, Width >> i), height = Math.Max(1, Height >> i);
            if (mips[i].Width != width || mips[i].Height != height
                || mips[i].Data.Length != checked(width * height * 4))
                throw new ArgumentException("Malformed authored RGBA mip chain.", nameof(mips));
        }
        Mips = mips;
    }

    public override bool MipmapsAvailable => Mips.Length == GpuCompressedTextureMath.MipLevelCount(Width, Height);
    public override bool CanGenerateMipmaps => false;
    public override int ResidentMipLevels => Mips.Length;
    public override long EstimateGpuBytes(bool mipmaps)
    {
        long bytes = 0;
        foreach (var mip in Mips) bytes += mip.Data.Length;
        return bytes;
    }
}
