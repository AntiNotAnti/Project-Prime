using System;
using System.IO;
using OpenTK.Graphics.OpenGL;
using MphRead.Mods.Render.Materials;

namespace MphRead.Mods.Render
{
    internal readonly record struct MaterialMapBindings(int Normal, int Specular, int Emissive)
    {
        public bool Any => Normal != 0 || Specular != 0 || Emissive != 0;
    }

    /// <summary>
    /// Optional high-resolution albedo and material maps. Decoding, resolution caps,
    /// mip generation and upload policy are shared with cosmetics and effect assets
    /// through <see cref="TextureAssetManager"/>.
    /// </summary>
    internal static class TextureReplacementPack
    {
        private static MaterialResolver? _resolver;
        private static bool _loaded;
        public static string Root => Path.Combine(OperatingSystem.IsAndroid()
            ? Launcher.LauncherPrefs.Directory : AppContext.BaseDirectory, "texture-packs", "default");
        public static int Revision { get; private set; }
        public static void Reload() { _loaded = false; _resolver = null; Revision++; }

        public static ResolvedMaterial? Resolve(string model, int texture, int palette, int recolor,
            MaterialAssetKey? key = null, MaterialAssetKey? fallbackKey = null)
        {
            EnsureLoaded();
            ResolvedMaterial? local = _resolver?.Resolve(model, texture, palette, recolor, key, fallbackKey);
            if (local != null && HasChannels(local)) return local;
            MaterialAssetKey portableKey = key ?? MaterialAssetKey.Model(model, texture, palette, recolor);
            return MapMaterialAssetRegistry.Resolve(portableKey) ?? local;
        }

        internal static ResolvedMaterial? ResolveLocalExplicit(MaterialAssetKey key)
        {
            EnsureLoaded();
            return _resolver?.ResolveExplicit(key);
        }

        internal static ResolvedMaterial? ResolveExplicit(MaterialAssetKey key)
            => ResolveLocalExplicit(key) ?? MapMaterialAssetRegistry.Resolve(key);

        private static bool HasChannels(ResolvedMaterial material)
            => material.Albedo != null || material.Normal != null || material.SpecularRoughness != null || material.Emissive != null;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try { _resolver = new MaterialResolver(Root); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
            {
                DebugLog.Line("render", "Material manifest ignored; preserving legacy lookup: " + ex.Message);
                _resolver = new MaterialResolver(Root, useManifest: false);
            }
        }

        public static bool TryUpload(Model model, int textureId, int paletteId, int recolorId,
            out int width, out int height, out ResolvedMaterial? material, MaterialAssetKey? authoredKey = null)
        {
            width = height = 0;
            material = null;
            if (!RenderOptions.TextureReplacements) return false;
            material = Resolve(model.Name, textureId, paletteId, recolorId,
                authoredKey ?? MaterialAssetKey.ForModel(model, textureId, paletteId, recolorId),
                MaterialAssetKey.ForModel(model, textureId, paletteId, recolorId));
            TextureAssetClass assetClass = Classify(model);
            return TryUpload(material, assetClass, out width, out height);
        }

        internal static bool TryUpload(ResolvedMaterial? material, TextureAssetClass assetClass,
            out int width, out int height)
        {
            width = height = 0;
            if (material?.Albedo is not { } albedo) return false;
            return TryUploadBound(albedo, assetClass, TextureAssetChannel.Albedo, out width, out height);
        }

        /// <summary>
        /// Desktop file-backed replacement channels are safe to decode away from
        /// the graphics thread. GPU object creation/upload still happens on the
        /// Scene draw thread.
        /// </summary>
        internal static bool CanDecodeOffThread(ResolvedMaterial material, bool includeCompanions = true)
            => material.Albedo?.IsFileBacked == true
                && (!includeCompanions
                    || ((material.Normal?.IsFileBacked ?? true)
                        && (material.SpecularRoughness?.IsFileBacked ?? true)
                        && (material.Emissive?.IsFileBacked ?? true)));

        internal static ModernTextureAsset? DecodePrepared(MaterialImage image,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension)
        {
            try
            {
                using Stream stream = image.OpenRead();
                return ModernTextureAsset.Decode(stream, image.Path, assetClass, channel, maximumDimension);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException or UnauthorizedAccessException)
            {
                DebugLog.Line("render", "background texture decode ignored " + image.Path + ": " + ex.Message);
                return null;
            }
        }

        internal static bool TryUploadPreparedBound(ModernTextureAsset asset, bool repeat,
            out int width, out int height)
        {
            width = height = 0;
            try
            {
                TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(
                    asset.AssetClass, asset.Channel);
                sampling = sampling with
                {
                    Mipmaps = sampling.Mipmaps && (asset.Width > 1 || asset.Height > 1)
                };
                TextureAssetManager.UploadPreparedBound(asset, repeat, sampling);
                if (GL.GetError() != ErrorCode.NoError) return false;
                width = asset.Width;
                height = asset.Height;
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException or UnauthorizedAccessException)
            {
                DebugLog.Line("render", "prepared texture upload ignored " + asset.Key + ": " + ex.Message);
                return false;
            }
        }

        public static MaterialMapBindings UploadCompanions(ResolvedMaterial material,
            Func<int> allocateTexture, Action<int> releaseTexture, TextureAssetClass assetClass = TextureAssetClass.World)
        {
            if (material.Key.Value.StartsWith("effect/model/", StringComparison.Ordinal))
                assetClass = TextureAssetClass.Effect;
            int normal = 0, specular = 0, emissive = 0;
            try
            {
                normal = UploadCompanion(material.Normal, TextureAssetChannel.Normal, assetClass, allocateTexture, releaseTexture);
                specular = UploadCompanion(material.SpecularRoughness, TextureAssetChannel.Material, assetClass, allocateTexture, releaseTexture);
                emissive = UploadCompanion(material.Emissive, TextureAssetChannel.Emissive, assetClass, allocateTexture, releaseTexture);
                return new(normal, specular, emissive);
            }
            catch
            {
                if (normal != 0) releaseTexture(normal);
                if (specular != 0) releaseTexture(specular);
                if (emissive != 0) releaseTexture(emissive);
                throw;
            }
        }

        private static int UploadCompanion(MaterialImage? image, TextureAssetChannel channel,
            TextureAssetClass assetClass, Func<int> allocateTexture, Action<int> releaseTexture)
        {
            if (image == null) return 0;
            int texture = allocateTexture();
            try
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texture);
                if (!TryUploadBound(image, assetClass, channel, out int width, out int height))
                {
                    GL.BindTexture(TextureTarget.Texture2D, 0);
                    releaseTexture(texture);
                    return 0;
                }
                DebugLog.Line("render", "material map " + Path.GetFileName(image.Path) + " (" + width + "x" + height + ")");
                return texture;
            }
            catch (Exception ex)
            {
                GL.BindTexture(TextureTarget.Texture2D, 0);
                releaseTexture(texture);
                DebugLog.Line("render", "material map ignored " + image.Path + ": " + ex.Message);
                return 0;
            }
            finally { GL.BindTexture(TextureTarget.Texture2D, 0); }
        }

        internal static byte[] ReadRgba(string path, out int width, out int height)
            => ReadRgba(MaterialPack.ValidateImage(path), out width, out height);

        internal static byte[] ReadRgba(MaterialImage image, out int width, out int height)
        {
            using Stream stream = image.OpenRead();
            ModernTextureAsset asset = ModernTextureAsset.Decode(stream, image.Path, TextureAssetClass.World, TextureAssetChannel.Albedo);
            width = asset.Width; height = asset.Height;
            return asset.Pixels;
        }

        private static bool TryUploadBound(MaterialImage image, TextureAssetClass assetClass, TextureAssetChannel channel,
            out int width, out int height)
        {
            width = height = 0;
            try
            {
                using Stream stream = image.OpenRead();
                return TextureAssetManager.TryUploadBound(stream, image.Path, assetClass, channel, repeat: true, out width, out height);
            }
            catch (Exception ex)
            {
                DebugLog.Line("render", "texture image ignored " + image.Path + ": " + ex.Message);
                return false;
            }
        }

        internal static TextureAssetClass Classify(Model model)
        {
            string? scope = model.MaterialAssetScope;
            if (scope != null && scope.StartsWith("effect/model/", StringComparison.Ordinal)) return TextureAssetClass.Effect;
            return TextureAssetClass.World;
        }

        public static string CompanionPath(string albedoPath, char kind)
            => Path.Combine(Path.GetDirectoryName(albedoPath) ?? "", Path.GetFileNameWithoutExtension(albedoPath) + "_" + kind + ".png");
    }
}
