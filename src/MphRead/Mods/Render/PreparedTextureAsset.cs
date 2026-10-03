using System;

namespace MphRead.Mods.Render
{
    internal enum GpuTextureCompressionFormat
    {
        None,
        Bc7Rgba,
        Etc2Rgba8,
        Astc4x4Rgba
    }

    internal readonly record struct CompressedTextureMip(int Width, int Height, byte[] Data);

    /// <summary>
    /// Result of an off-thread authored-texture decode. RGBA images use
    /// ModernTextureAsset; Basis/KTX2 may retain GPU-native block-compressed
    /// mip data until the render thread promotes it.
    /// </summary>
    internal abstract class PreparedTextureAsset
    {
        protected PreparedTextureAsset(string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, int width, int height)
        {
            Key = key;
            AssetClass = assetClass;
            Channel = channel;
            Width = width;
            Height = height;
        }

        public string Key { get; }
        public TextureAssetClass AssetClass { get; }
        public TextureAssetChannel Channel { get; }
        public int Width { get; }
        public int Height { get; }

        // RGBA assets can generate a chain on the GPU. Compressed assets
        // override this and require an authored/transcoded chain.
        public virtual bool MipmapsAvailable => Width > 1 || Height > 1;

        public abstract long EstimateGpuBytes(bool mipmaps);
    }
}
