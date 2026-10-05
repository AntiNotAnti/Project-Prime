using System;
using System.IO;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;

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

        internal MaterialMapBindings GetCharacterModelMaterialMaps(string key,
            CharacterEmbeddedMaterialMaps maps, TextureAssetClass assetClass)
        {
            // Companions remain scene-owned residents even while the lighting
            // toggle is off. Draw submission gates their use, so toggling never
            // destroys a binding cached by the character model.
            int normal = UploadMap(maps.Normal, TextureAssetChannel.Normal);
            int material = UploadMap(maps.MetallicRoughness, TextureAssetChannel.Material);
            int emissive = UploadMap(maps.Emissive, TextureAssetChannel.Emissive);
            return new(normal, material, emissive);

            int UploadMap(CharacterEmbeddedAlbedo? image, TextureAssetChannel channel)
            {
                if (image == null) return 0;
                using var stream = new MemoryStream(image.Image, writable: false);
                // Fit before conversion to bound the temporary RGBA allocation.
                ModernTextureAsset decoded = ModernTextureAsset.Decode(stream, key,
                    assetClass, channel, TextureAssetManager.DimensionLimit(assetClass, channel));
                byte[] pixels = decoded.Pixels;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (channel == TextureAssetChannel.Material)
                    {
                        // glTF: roughness=G, metallic=B. Project Prime:
                        // specular strength=R, roughness=G.
                        pixels[i] = Quantize(pixels[i+2] / 255f * maps.MetallicFactor);
                        pixels[i+1] = Quantize(pixels[i+1] / 255f * maps.RoughnessFactor);
                        pixels[i+2] = 0;
                    }
                    else if (channel == TextureAssetChannel.Normal && maps.NormalScale != 1)
                    {
                        var n = new System.Numerics.Vector3((pixels[i]/255f*2-1)*maps.NormalScale,
                            (pixels[i+1]/255f*2-1)*maps.NormalScale, pixels[i+2]/255f*2-1);
                        n = n.LengthSquared() < 1e-8f ? System.Numerics.Vector3.UnitZ : System.Numerics.Vector3.Normalize(n);
                        pixels[i] = Quantize(n.X*.5f+.5f); pixels[i+1] = Quantize(n.Y*.5f+.5f); pixels[i+2] = Quantize(n.Z*.5f+.5f);
                    }
                    else if (channel == TextureAssetChannel.Emissive)
                    {
                        pixels[i] = ScaleSrgb(pixels[i], maps.EmissiveFactor.X);
                        pixels[i+1] = ScaleSrgb(pixels[i+1], maps.EmissiveFactor.Y);
                        pixels[i+2] = ScaleSrgb(pixels[i+2], maps.EmissiveFactor.Z);
                    }
                    pixels[i+3] = 255;
                }
                int binding = CharacterTextureAssets.UploadRgba("character-model/"+key+"/"+channel,
                    assetClass, channel, decoded.Width, decoded.Height, pixels, repeat:false, out _, out _);
                // Optional companion residency can fail under a mobile budget.
                // Albedo/geometry must remain usable in that case.
                if (binding != 0) RegisterModernTexture(binding, assetClass, channel);
                return binding;
            }
        }

        private static byte Quantize(float value) => (byte)Math.Clamp((int)MathF.Round(value*255),0,255);
        private static byte ScaleSrgb(byte value, float factor)
        {
            float srgb=value/255f;
            float linear=srgb<=.04045f?srgb/12.92f:MathF.Pow((srgb+.055f)/1.055f,2.4f);
            linear*=factor;
            return Quantize(linear<=.0031308f?linear*12.92f:1.055f*MathF.Pow(linear,1/2.4f)-.055f);
        }
    }
}
