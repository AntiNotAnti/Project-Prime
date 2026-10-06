using System;
using System.IO;

namespace MphRead.Mods.Render
{
    internal readonly record struct RgbaTextureMip(int Width, int Height, byte[] Data);

    /// <summary>
    /// Authored RGBA8 KTX2 levels, including linear-color and normalized-normal
    /// mipmaps. Promotion uploads these bytes directly instead of generating a
    /// different mip chain. PNG and GPU block-compressed assets keep their routes.
    /// </summary>
    internal sealed class RgbaMipTextureAsset : PreparedTextureAsset
    {
        internal RgbaMipTextureAsset(string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, RgbaTextureMip[] mips, int sourceBaseLevel,string preparationReason="authored-rgba8")
            : base(key, assetClass, channel, First(mips).Width, First(mips).Height)
        {
            if (sourceBaseLevel < 0) throw new InvalidDataException("Invalid authored RGBA source level.");
            if (Width <= 0 || Height <= 0 || Width > ModernTextureAsset.MaximumDimension
                || Height > ModernTextureAsset.MaximumDimension
                || (long)Width*Height > ModernTextureAsset.MaximumPixels)
                throw new InvalidDataException("Invalid authored RGBA dimensions.");
            int maximumLevels=1+(int)Math.Floor(Math.Log2(Math.Max(Width,Height)));
            if (mips.Length > maximumLevels) throw new InvalidDataException("Authored RGBA mip chain has excess levels.");
            long bytes=0;
            for (int level=0;level<mips.Length;level++)
            {
                RgbaTextureMip mip=mips[level];
                int width=Math.Max(1,Width >> level), height=Math.Max(1,Height >> level);
                if (mip.Width != width || mip.Height != height || mip.Data == null
                    || mip.Data.LongLength != checked((long)width*height*4))
                    throw new InvalidDataException($"Authored RGBA mip {level} has invalid dimensions or bytes.");
                bytes=checked(bytes+mip.Data.LongLength);
            }
            if (bytes > ModernTextureAsset.MaximumDecodedBytes)
                throw new InvalidDataException("Authored RGBA mip chain exceeds the decoded-byte limit.");
            Mips=(RgbaTextureMip[])mips.Clone();
            SourceBaseLevel=sourceBaseLevel;
            PreparationReason=preparationReason;
            CompleteMipChain=mips.Length == maximumLevels;
        }

        internal RgbaTextureMip[] Mips { get; }
        internal int SourceBaseLevel { get; }
        internal string PreparationReason { get; }
        internal bool CompleteMipChain { get; }
        public override bool MipmapsAvailable => CompleteMipChain && Mips.Length > 1;

        public override long EstimateGpuBytes(bool mipmaps)
        {
            long bytes=0;
            int count=mipmaps && MipmapsAvailable ? Mips.Length : 1;
            for (int level=0;level<count;level++) bytes=checked(bytes+Mips[level].Data.LongLength);
            return bytes;
        }

        private static RgbaTextureMip First(RgbaTextureMip[] mips)
            => mips != null && mips.Length > 0 ? mips[0]
                : throw new InvalidDataException("Authored RGBA mip chain is empty.");
    }
}
