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
        private static volatile bool _loaded;
        private static readonly object ResolverGate = new();
        public static string Root => Path.Combine(OperatingSystem.IsAndroid()
            ? Launcher.LauncherPrefs.Directory : AppContext.BaseDirectory, "texture-packs", "default");
        public static int Revision { get; private set; }
        public static void Reload() { lock(ResolverGate) { _loaded = false; _resolver = null; Revision++; } }

        public static ResolvedMaterial? Resolve(string model, int texture, int palette, int recolor,
            MaterialAssetKey? key = null, MaterialAssetKey? fallbackKey = null,
            SourceBankMaterialSnapshot? sourceBanks = null)
        {
            EnsureLoaded();
            MaterialAssetKey portableKey = key ?? MaterialAssetKey.Model(model, texture, palette, recolor);
            ResolvedMaterial? capturedSource = sourceBanks?.Resolve(portableKey);
            bool nativeFlipbook = capturedSource == null && MapMaterialAssetRegistry.IsNativeFlipbook(portableKey);
            ResolvedMaterial? local = _resolver?.Resolve(model, texture, palette, recolor, key, fallbackKey);
            if (nativeFlipbook && local != null)
                local = local with { Albedo = null };
            if (local != null && HasChannels(local))
            {
                if (local.Albedo == null && capturedSource?.Albedo is { } inherited)
                    return local with { Albedo = inherited.WithTexturePolicy(inherited.TransferOverride,
                        local.AlphaPolicy, local.WrapS is { } s && local.WrapT is { } t
                            ? new TextureWrapPolicy(s, t) : inherited.WrapPolicy, local.AlphaFactor) };
                return local;
            }
            if (capturedSource != null) return capturedSource;
            return MapMaterialAssetRegistry.Resolve(portableKey,
                includeCompanions: RenderOptions.AdvancedMaterials) ?? local;
        }

        internal static ResolvedMaterial? ResolveLocalExplicit(MaterialAssetKey key)
        {
            EnsureLoaded();
            return _resolver?.ResolveExplicit(key);
        }

        internal static ResolvedMaterial? ResolveExplicit(MaterialAssetKey key,
            SourceBankMaterialSnapshot? sourceBanks = null)
            => ResolveLocalExplicit(key) ?? sourceBanks?.Resolve(key) ?? MapMaterialAssetRegistry.Resolve(key);

        private static bool HasChannels(ResolvedMaterial material)
            => material.Albedo != null || material.Normal != null || material.SpecularRoughness != null || material.Emissive != null
                || material.AlphaPolicy.Mode != TextureAlphaMode.Legacy || material.AlphaFactor != 1
                || material.WrapS.HasValue || material.WrapT.HasValue;

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock(ResolverGate)
            {
                if (_loaded) return;
                MaterialResolver resolver;
                try { resolver = new MaterialResolver(Root); }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
                    or ArgumentException or System.Text.Json.JsonException or InvalidOperationException)
                {
                    DebugLog.Line("render", "Material manifest ignored; preserving legacy lookup: " + ex.Message);
                    resolver = new MaterialResolver(Root, useManifest: false);
                }
                _resolver = resolver;
                _loaded = true; // Publish only the fully initialized, immutable lookup.
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
                MaterialAssetKey.ForModel(model, textureId, paletteId, recolorId), model.SourceBankMaterials);
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
        /// File-backed channels and explicitly immutable package-backed channels
        /// are safe to decode away from the graphics thread. GPU object
        /// creation/upload still happens on the Scene draw thread.
        /// </summary>
        internal static bool CanDecodeOffThread(ResolvedMaterial material, bool includeCompanions = true)
            => material.Albedo?.CanDecodeOffThread == true
                && (!includeCompanions
                    || ((material.Normal?.CanDecodeOffThread ?? true)
                        && (material.SpecularRoughness?.CanDecodeOffThread ?? true)
                        && (material.Emissive?.CanDecodeOffThread ?? true)));

        internal static PreparedTextureAsset? DecodePrepared(MaterialImage image,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension,
            bool linearFiltering = false)
        {
            try
            {
                return image.Prepare(assetClass, channel, maximumDimension, linearFiltering);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException or UnauthorizedAccessException)
            {
                DebugLog.Line("render", "background texture decode ignored " + image.Path + ": " + ex.Message);
                return null;
            }
        }

        internal static bool TryUploadPreparedBound(PreparedTextureAsset asset, bool repeat,
            out int width, out int height)
        {
            width = height = 0;
            try
            {
                TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(
                    asset.AssetClass, asset.Channel);
                sampling = asset.EffectiveSampling(sampling);
                TextureAssetManager.UploadPreparedBound(asset, repeat, sampling);
                OpenGlFailureInjection.Hit("world-material-upload");
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
            ModernTextureAsset asset = image.ReadRgba(TextureAssetClass.World, TextureAssetChannel.Albedo);
            width = asset.Width; height = asset.Height;
            return asset.Pixels;
        }

        private static bool TryUploadBound(MaterialImage image, TextureAssetClass assetClass, TextureAssetChannel channel,
            out int width, out int height)
        {
            width = height = 0;
            try
            {
                PreparedTextureAsset asset = image.Prepare(assetClass, channel,
                    TextureAssetManager.DimensionLimit(assetClass, channel));
                return TryUploadPreparedBound(asset, repeat: true, out width, out height);
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
