using System;

namespace MphRead.Mods.Render;

/// <summary>GPU-native 4x4, 16-byte block textures retaining their authored mip chain.</summary>
internal sealed class CompressedTextureAsset : PreparedTextureAsset
{
    internal GpuTextureCompressionFormat Format { get; }
    internal CompressedTextureMip[] Mips { get; }

    internal CompressedTextureAsset(string key, TextureAssetClass assetClass,
        TextureAssetChannel channel, GpuTextureCompressionFormat format,
        CompressedTextureMip[] mips, TextureAssetSemantics? semantics = null)
        : base(key, assetClass, channel, mips.Length == 0 ? 0 : mips[0].Width,
            mips.Length == 0 ? 0 : mips[0].Height, semantics)
    {
        if (format is not (GpuTextureCompressionFormat.Bc7Rgba or GpuTextureCompressionFormat.Etc2Rgba8 or GpuTextureCompressionFormat.Astc4x4Rgba)
            || mips.Length == 0 || Width <= 0 || Height <= 0
            || mips.Length > GpuCompressedTextureMath.MipLevelCount(Width, Height))
            throw new ArgumentException("Invalid compressed texture asset.", nameof(mips));
        for (int i = 0; i < mips.Length; i++)
        {
            int expectedW = Math.Max(1, Width >> i), expectedH = Math.Max(1, Height >> i);
            int expectedSize = GpuCompressedTextureMath.LevelBytes(expectedW, expectedH);
            if (mips[i].Width != expectedW || mips[i].Height != expectedH
                || mips[i].Data == null || mips[i].Data.Length != expectedSize)
                throw new ArgumentException("Compressed texture mip chain is malformed.", nameof(mips));
        }
        Format = format;
        Mips = mips;
    }

    internal override PreparedTextureAsset WithSemantics(TextureAssetSemantics semantics)
        => new CompressedTextureAsset(Key, AssetClass, Channel, Format, Mips, semantics);

    public override bool MipmapsAvailable => Mips.Length == GpuCompressedTextureMath.MipLevelCount(Width, Height);
    public override bool CanGenerateMipmaps => false;
    public override int ResidentMipLevels => Mips.Length;

    // Every mip is uploaded, even if the sampler is set to a non-mipmap filter.
    public override long EstimateGpuBytes(bool mipmaps)
    {
        _ = mipmaps;
        long result = 0;
        foreach (CompressedTextureMip mip in Mips) result += mip.Data.Length;
        return result;
    }
}
