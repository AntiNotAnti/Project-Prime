using System;
using System.IO;
using MphRead.Mods.Render;

namespace MphRead
{
    public partial class Scene
    {
        private TextureAssetManager? _characterTextureAssets;
        private TextureAssetManager CharacterTextureAssets => _characterTextureAssets
            ??= new TextureAssetManager(AllocateTexture, ReleaseTexture);

        internal int GetCharacterModelTexture(string key, byte[] image, TextureAssetClass assetClass,
            bool opaque)
        {
            // Share the scene's bounded texture residency and cleanup. The GLB
            // loader has already resolved the image from its embedded buffer.
            int binding;
            if (opaque)
            {
                // Source materials often store a gloss mask in albedo alpha.
                // glTF OPAQUE explicitly ignores that channel. Clear it before
                // resizing so transparent texels cannot darken the RGB filter.
                using var stream = new MemoryStream(image, writable: false);
                ModernTextureAsset decoded = ModernTextureAsset.Decode(stream, key,
                    assetClass, TextureAssetChannel.Albedo);
                for (int i = 3; i < decoded.Pixels.Length; i += 4) decoded.Pixels[i] = 255;
                binding = CharacterTextureAssets.UploadRgba("character-model/" + key,
                    assetClass, TextureAssetChannel.Albedo, decoded.Width, decoded.Height,
                    decoded.Pixels, repeat: false, out _, out _);
            }
            else
                binding = CharacterTextureAssets.Upload("character-model/" + key,
                    () => new MemoryStream(image, writable: false), assetClass,
                    TextureAssetChannel.Albedo, repeat: false, out _, out _);
            RegisterModernTexture(binding, assetClass, TextureAssetChannel.Albedo);
            return binding;
        }

        internal void ClearCharacterModelTextures()
        {
            _characterTextureAssets?.Dispose();
            _characterTextureAssets = null;
        }
    }
}
