using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Graphics.OpenGL;
using ReFuel.Stb;
using MphRead.Mods.Cosmetics;
using MphRead.Mods.Cosmetics.Skins;
namespace MphRead
{
    public partial class Scene
    {
        private readonly Dictionary<(string, string, int, SkinContext), RenderMaterialOverride> _cosmeticTextures = new();
        internal RenderMaterialOverride CosmeticMaterialSubmission { get; set; }
        internal RenderMaterialOverride GetCosmeticMaterial(SkinDefinition skin, Model model, int material, SkinContext context)
        {
            var key = (skin.Key, model.Name, material, context);
            if (_cosmeticTextures.TryGetValue(key, out var binding)) return binding;
            string? root = context switch { SkinContext.ViewModel => skin.GunAssets,
                SkinContext.AltForm => skin.AltFormAssets, SkinContext.Halfturret => skin.TurretAssets, _ => skin.AlbedoSet };
            if (root == null) return default;
            // Paths originate only in the compiled catalog, never the network.
            string stem = root + "/" + model.Name + "/" + material;
            string Channel(string? channelRoot, string suffix) =>
                (context == SkinContext.Biped && channelRoot != null
                    ? channelRoot + "/" + model.Name + "/" + material : stem) + suffix;
            binding = new(UploadCosmetic(stem + "_albedo.png"), UploadCosmetic(Channel(skin.NormalSet, "_normal.png")),
                UploadCosmetic(Channel(skin.SpecularSet, "_specular.png")), UploadCosmetic(Channel(skin.EmissiveSet, "_emissive.png")));
            _cosmeticTextures[key] = binding;
            return binding;
        }
        private int UploadCosmetic(string path)
        {
            using Stream? stream = CosmeticAsset.Open(path);
            if (stream == null) return 0;
            int texture = 0;
            try
            {
                using StbImage image = StbImage.Load(stream, StbiImageFormat.Rgba);
                if (image.Width <= 0 || image.Height <= 0 || image.Width > 2048 || image.Height > 2048 || image.ImagePointer == IntPtr.Zero) return 0;
                texture = AllocateTexture();
                GL.ActiveTexture(TextureUnit.Texture0); GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, image.Width, image.Height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, image.ImagePointer);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                return texture;
            }
            catch (Exception ex)
            {
                if (texture != 0) ReleaseTexture(texture);
                Mods.DebugLog.Line("cosmetics", $"Optional material unavailable: {path}: {ex.Message}");
                return 0;
            }
            finally { GL.BindTexture(TextureTarget.Texture2D, 0); }
        }
        internal void ClearCosmeticTextures()
        {
            foreach (var material in _cosmeticTextures.Values)
            {
                if (material.AlbedoBinding != 0) ReleaseTexture(material.AlbedoBinding);
                if (material.NormalBinding != 0) ReleaseTexture(material.NormalBinding);
                if (material.SpecularBinding != 0) ReleaseTexture(material.SpecularBinding);
                if (material.EmissiveBinding != 0) ReleaseTexture(material.EmissiveBinding);
            }
            _cosmeticTextures.Clear();
        }
    }
}
