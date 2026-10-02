using System;
using System.Collections.Generic;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Skins;
using MphRead.Mods.Render;

namespace MphRead
{
    public partial class Scene
    {
        private readonly Dictionary<(string, string, int, SkinContext, TextureAssetQuality), RenderMaterialOverride> _cosmeticTextures = new();
        private TextureAssetManager? _cosmeticTextureAssets;
        private TextureAssetQuality _cosmeticTextureQuality = RenderOptions.TextureQuality;
        private TextureAssetManager CosmeticTextureAssets => _cosmeticTextureAssets ??= new TextureAssetManager(AllocateTexture, ReleaseTexture);
        internal RenderMaterialOverride CosmeticMaterialSubmission { get; set; }

        internal RenderMaterialOverride GetCosmeticMaterial(SkinDefinition skin, Model model, int material, SkinContext context)
        {
            if (_cosmeticTextureQuality != RenderOptions.TextureQuality)
            {
                ClearCosmeticTextures();
                _cosmeticTextureQuality = RenderOptions.TextureQuality;
            }
            var key = (skin.Key, model.Name, material, context, RenderOptions.TextureQuality);
            if (_cosmeticTextures.TryGetValue(key, out var binding)) return binding;
            string? root = context switch { SkinContext.ViewModel => skin.GunAssets,
                SkinContext.AltForm => skin.AltFormAssets, SkinContext.Halfturret => skin.TurretAssets, _ => skin.AlbedoSet };
            if (root == null) return default;
            string stem = root + "/" + model.Name + "/" + material;
            string Channel(string? channelRoot, string suffix) =>
                (context == SkinContext.Biped && channelRoot != null
                    ? channelRoot + "/" + model.Name + "/" + material : stem) + suffix;
            TextureAssetClass assetClass = context switch
            {
                SkinContext.ViewModel => TextureAssetClass.Weapon,
                SkinContext.AltForm => TextureAssetClass.AlternateForm,
                SkinContext.Halfturret => TextureAssetClass.Turret,
                _ => TextureAssetClass.Hunter
            };
            int albedo = UploadCosmetic(stem + "_albedo.png", assetClass, TextureAssetChannel.Albedo);
            if (albedo == 0 && skin.DecalAsset != null) albedo = UploadDecal(skin.DecalAsset, model, material, assetClass);
            binding = new(albedo,
                UploadCosmetic(Channel(skin.NormalSet, "_normal.png"), assetClass, TextureAssetChannel.Normal),
                UploadCosmetic(Channel(skin.SpecularSet, "_specular.png"), assetClass, TextureAssetChannel.Material),
                UploadCosmetic(Channel(skin.EmissiveSet, "_emissive.png"), assetClass, TextureAssetChannel.Emissive));
            _cosmeticTextures[key] = binding;
            return binding;
        }

        private int UploadDecal(string path, Model model, int materialIndex, TextureAssetClass assetClass)
        {
            try
            {
                var material = model.Materials[materialIndex];
                if (material.TextureId < 0 || material.Alpha < 31) return 0;
                using Stream? stream = CosmeticAsset.Open(path);
                if (stream == null) return 0;
                int cap = TextureAssetManager.DimensionLimit(assetClass, TextureAssetChannel.Albedo);
                ModernTextureAsset decal = ModernTextureAsset.Decode(stream, path, assetClass, TextureAssetChannel.Albedo, cap);
                var native = model.Recolors[0].Textures[material.TextureId];
                var pixels = model.GetPixels(material.TextureId, material.PaletteId, 0);
                int width = Math.Max(native.Width, decal.Width);
                int height = Math.Max(native.Height, decal.Height);
                byte[] output = new byte[checked(width * height * 4)];
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var basis = pixels[(y * native.Height / height) * native.Width + x * native.Width / width];
                    int source = ((y * decal.Height / height) * decal.Width + x * decal.Width / width) * 4;
                    int target = (y * width + x) * 4;
                    float opacity = decal.Pixels[source + 3] / 255f;
                    output[target] = (byte)(basis.Red * (1 - opacity) + decal.Pixels[source] * opacity);
                    output[target + 1] = (byte)(basis.Green * (1 - opacity) + decal.Pixels[source + 1] * opacity);
                    output[target + 2] = (byte)(basis.Blue * (1 - opacity) + decal.Pixels[source + 2] * opacity);
                    output[target + 3] = basis.Alpha;
                }
                string cacheKey = "cosmetic-decal/" + path + "/" + model.Name + "/" + materialIndex;
                return CosmeticTextureAssets.UploadRgba(cacheKey, assetClass, TextureAssetChannel.Albedo,
                    width, height, output, repeat: false, out _, out _);
            }
            catch (Exception ex)
            {
                Mods.DebugLog.Line("cosmetics", "Authored decal unavailable: " + path + ": " + ex.Message);
                return 0;
            }
        }

        private int UploadCosmetic(string path, TextureAssetClass assetClass, TextureAssetChannel channel)
        {
            return CosmeticTextureAssets.Upload("cosmetic/" + path, () => CosmeticAsset.Open(path),
                assetClass, channel, repeat: false, out _, out _);
        }

        internal void ClearCosmeticTextures()
        {
            _cosmeticTextures.Clear();
            _cosmeticTextureAssets?.Dispose();
            _cosmeticTextureAssets = null;
        }
    }
}
