using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Shared modern texture policy and residency. Maps/material packs, hunter skins,
    /// viewmodels, alternate forms, turrets and effect materials all pass through the
    /// same resolution, memory, mip and upload rules.
    /// </summary>
    internal sealed class TextureAssetManager : IDisposable
    {
        private readonly record struct Resident(int Binding, long Bytes, int Width, int Height,
            bool MipmapsAvailable, bool CanGenerateMipmaps, int ResidentMipLevels);
        private const int TextureMaxAnisotropyExt = 0x84FE;
        private const int MaxTextureMaxAnisotropyExt = 0x84FF;
        private readonly Dictionary<string, Resident> _resident = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _requestAliases = new(StringComparer.Ordinal);
        private readonly Func<int> _allocateTexture;
        private readonly Action<int> _releaseTexture;
        private readonly Action<int, PreparedTextureAsset>? _preparedUploaded;
        private readonly bool _linearFiltering;
        private long _residentBytes;

        public TextureAssetManager(Func<int> allocateTexture, Action<int> releaseTexture,
            Action<int, PreparedTextureAsset>? preparedUploaded = null, bool linearFiltering = false)
        {
            _allocateTexture = allocateTexture;
            _releaseTexture = releaseTexture;
            _preparedUploaded = preparedUploaded;
            _linearFiltering = linearFiltering;
        }

        internal bool LinearFiltering => _linearFiltering;

        public long ResidentBytes => _residentBytes;
        public int ResidentCount => _resident.Count;

        internal bool TryGetMipResidency(int binding, out bool available, out bool canGenerate, out int levels)
        {
            foreach (Resident resident in _resident.Values)
                if (resident.Binding == binding)
                {
                    available = resident.MipmapsAvailable;
                    canGenerate = resident.CanGenerateMipmaps;
                    levels = resident.ResidentMipLevels;
                    return true;
                }
            available = canGenerate = false; levels = 0;
            return false;
        }

        // Only scene-owned optional variant leases use this. Stable base/maps remain pinned.
        internal bool ReleaseBinding(int binding)
        {
            string? key = null;
            foreach (var pair in _resident)
                if (pair.Value.Binding == binding) { key = pair.Key; break; }
            if (key == null) return false;
            Resident resident = _resident[key];
            _resident.Remove(key); _residentBytes -= resident.Bytes;
            foreach (string alias in _requestAliases.Where(pair => pair.Value == key).Select(pair => pair.Key).ToArray())
                _requestAliases.Remove(alias);
            _releaseTexture(binding); return true;
        }

        public int Upload(string key, Func<Stream?> open, TextureAssetClass assetClass,
            TextureAssetChannel channel, bool repeat, out int width, out int height,
            TextureAssetSemantics? semantics = null)
        {
            int cap = DimensionLimit(assetClass, channel);
            TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(assetClass, channel);
            TextureWrapPolicy requestedWrap = semantics?.Wrap ?? (repeat ? TextureWrapPolicy.Repeat : TextureWrapPolicy.Clamp);
            TextureAssetSemantics requested = (semantics ?? TextureAssetSemantics.ForChannel(channel))
                .WithWrap(requestedWrap);
            string requestKey = key + "|" + assetClass + "|" + channel + "|" + cap + "|sample=" + sampling.CacheKey
                + "|linear=" + _linearFiltering + "|repeat=" + requested.CacheKey;
            if (_requestAliases.TryGetValue(requestKey, out string? residentKey)
                && _resident.TryGetValue(residentKey, out Resident resident))
            {
                width = resident.Width; height = resident.Height; return resident.Binding;
            }
            width = height = 0;
            try
            {
                using Stream? stream = open();
                if (stream == null) return 0;
                PreparedTextureAsset asset = PreparedTextureCodec.Decode(
                    stream, key, assetClass, channel, cap, requested, _linearFiltering);
                string cacheKey = requestKey + "|resolved=" + asset.Semantics.CacheKey;
                _requestAliases[requestKey] = cacheKey;
                return Upload(cacheKey, asset, repeat, sampling, out width, out height);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException or UnauthorizedAccessException)
            {
                DebugLog.Line("render", "modern texture ignored " + key + ": " + ex.Message);
                return 0;
            }
        }

        public int UploadRgba(string key, TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, byte[] rgba, bool repeat, out int uploadedWidth, out int uploadedHeight,
            TextureAssetSemantics? semantics = null)
        {
            int cap = DimensionLimit(assetClass, channel);
            TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(assetClass, channel);
            TextureWrapPolicy requestedWrap = semantics?.Wrap ?? (repeat ? TextureWrapPolicy.Repeat : TextureWrapPolicy.Clamp);
            TextureAssetSemantics requested = (semantics ?? TextureAssetSemantics.ForChannel(channel))
                .WithWrap(requestedWrap);
            string requestKey = key + "|" + assetClass + "|" + channel + "|" + cap + "|sample=" + sampling.CacheKey
                + "|linear=" + _linearFiltering + "|repeat=" + requested.CacheKey;
            if (_requestAliases.TryGetValue(requestKey, out string? residentKey)
                && _resident.TryGetValue(residentKey, out Resident resident))
            {
                uploadedWidth = resident.Width; uploadedHeight = resident.Height; return resident.Binding;
            }
            try
            {
                ModernTextureAsset asset = ModernTextureAsset.FromRgba(key, assetClass, channel, width, height, rgba, requested);
                PreparedTextureAsset prepared = _linearFiltering
                    ? ColorTexturePolicy.PrepareForLinearFiltering(
                        ColorTexturePolicy.FitForLinearFiltering(asset, cap, requested), requested.Alpha, requested.Wrap)
                    : asset.Fit(cap);
                string cacheKey = requestKey + "|resolved=" + prepared.Semantics.CacheKey;
                _requestAliases[requestKey] = cacheKey;
                return Upload(cacheKey, prepared, repeat, sampling, out uploadedWidth, out uploadedHeight);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException)
            {
                uploadedWidth = uploadedHeight = 0;
                DebugLog.Line("render", "modern RGBA texture ignored " + key + ": " + ex.Message);
                return 0;
            }
        }

        // Explicit top-level source preview sampling. Uses this manager's
        // allocator, budget and Scene lifetime; no alpha/channel conversion.
        internal int UploadRgba(string key, int width, int height, byte[] rgba,
            TextureSamplerDescriptor sampling)
        {
            string cacheKey = key + "|source-rgba|repeat-st|" + sampling.CacheKey;
            if (_resident.TryGetValue(cacheKey, out Resident resident)) return resident.Binding;
            var image = ModernTextureAsset.FromRgba(key, TextureAssetClass.Hunter,
                TextureAssetChannel.Albedo, width, height, rgba);
            if (width > DimensionLimit(TextureAssetClass.Hunter, TextureAssetChannel.Albedo)
                || height > DimensionLimit(TextureAssetClass.Hunter, TextureAssetChannel.Albedo)) return 0;
            return Upload(cacheKey, image, true, sampling, out _, out _);
        }

        private int Upload(string cacheKey, PreparedTextureAsset asset, bool repeat,
            TextureSamplerDescriptor sampling, out int width, out int height)
        {
            width = asset.Width; height = asset.Height;
            bool mipmaps = sampling.Mipmaps && asset.MipmapsAvailable;
            sampling = sampling with { Mipmaps = mipmaps };
            long bytes = asset.EstimateGpuBytes(mipmaps);
            if (_resident.ContainsKey(cacheKey))
            {
                Resident existing = _resident[cacheKey];
                width = existing.Width; height = existing.Height; return existing.Binding;
            }
            if (_residentBytes + bytes > MemoryBudgetBytes())
            {
                DebugLog.Line("render", "modern texture budget reached; native fallback for " + asset.Key);
                width = height = 0;
                return 0;
            }
            TextureWrapPolicy wrap = asset.Semantics.Wrap ?? (repeat ? TextureWrapPolicy.Repeat : TextureWrapPolicy.Clamp);
            if (asset.Semantics.Wrap == null) asset = asset.WithSemantics(asset.Semantics.WithWrap(wrap));
            int texture = _allocateTexture();
            bool uploaded = false;
            try
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texture);
                UploadPreparedBound(asset, repeat, sampling);
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("GPU texture upload failed.");
                _resident.Add(cacheKey, new(texture, bytes, width, height,
                    asset.MipmapsAvailable, asset.CanGenerateMipmaps, asset.ResidentMipLevels));
                _residentBytes += bytes;
                uploaded = true;
            }
            catch
            {
                _releaseTexture(texture);
                width = height = 0;
            }
            finally { GL.BindTexture(TextureTarget.Texture2D, 0); }
            if (!uploaded) return 0;
            DebugLog.Line("render", "modern texture " + asset.Key + " " + width + "x" + height
                + " resident=" + (_residentBytes / (1024.0 * 1024.0)).ToString("0.0") + " MiB"
                + " " + TextureSamplingPolicy.Describe(sampling));
            try { _preparedUploaded?.Invoke(texture, asset); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            { DebugLog.Line("render", "prepared texture callback ignored " + asset.Key + ": " + ex.Message); }
            return texture;
        }

        public static bool TryUploadBound(Stream source, string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, bool repeat, out int width, out int height)
        {
            width = height = 0;
            try
            {
                int cap = DimensionLimit(assetClass, channel);
                PreparedTextureAsset asset = PreparedTextureCodec.Decode(
                    source, key, assetClass, channel, cap);
                TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(assetClass, channel);
                sampling = sampling with
                {
                    Mipmaps = sampling.Mipmaps && asset.MipmapsAvailable
                };
                UploadPreparedBound(asset, repeat, sampling);
                if (GL.GetError() != ErrorCode.NoError) return false;
                width = asset.Width; height = asset.Height;
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException or UnauthorizedAccessException)
            {
                DebugLog.Line("render", "modern texture upload ignored " + key + ": " + ex.Message);
                return false;
            }
        }

        internal static void UploadPreparedBound(PreparedTextureAsset asset, bool repeat,
            TextureSamplerDescriptor sampling, bool linearColor = false)
        {
            ClearPriorUploadErrors();
            sampling = asset.EffectiveSampling(sampling);
            if (asset is CompressedTextureAsset gpu)
            {
                int internalFormat = gpu.Format switch
                {
#if ANDROID
                    GpuTextureCompressionFormat.Etc2Rgba8 => linearColor && gpu.Semantics.Transfer == TextureTransferFunction.Srgb
                        ? 0x9279 : 0x9278, // GL_COMPRESSED_SRGB8_ALPHA8_ETC2_EAC / RGBA8_ETC2_EAC
                    GpuTextureCompressionFormat.Astc4x4Rgba => linearColor && gpu.Semantics.Transfer == TextureTransferFunction.Srgb
                        ? 0x93D0 : 0x93B0, // GL_COMPRESSED_SRGB8_ALPHA8_ASTC_4x4_KHR / RGBA_ASTC_4x4_KHR
#elif !MPHREAD_SERVER
                    GpuTextureCompressionFormat.Bc7Rgba when DesktopTextureCompressionPolicy.SupportsBc7
                        => linearColor && gpu.Semantics.Transfer == TextureTransferFunction.Srgb
                            ? 0x8E8D : DesktopTextureCompressionPolicy.Bc7InternalFormat,
#endif
                    _ => throw new NotSupportedException("GPU texture compression format is not enabled/supported on this context.")
                };
                for (int level = 0; level < gpu.Mips.Length; level++)
                {
                    CompressedTextureMip mip = gpu.Mips[level];
#if ANDROID
                    GlEs.CompressedTexImage2D(TextureTarget.Texture2D, level, internalFormat,
                        mip.Width, mip.Height, mip.Data);
#else
                    OpenTK.Graphics.OpenGL.GL.CompressedTexImage2D<byte>(TextureTarget.Texture2D, level, (InternalFormat)internalFormat,
                        mip.Width, mip.Height, 0, mip.Data.Length, mip.Data);
#endif
                }
                ApplyBoundSampling(asset.Semantics.Wrap ?? (repeat ? TextureWrapPolicy.Repeat : TextureWrapPolicy.Clamp), sampling, generateMipmaps: false);
                return;
            }
            if (asset is RgbaMipTextureAsset authored)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                for (int level = 0; level < authored.Mips.Length; level++)
                {
                    CompressedTextureMip mip = authored.Mips[level];
                    GL.TexImage2D(TextureTarget.Texture2D, level, linearColor && authored.Semantics.Transfer == TextureTransferFunction.Srgb
                        ? (PixelInternalFormat)0x8C43 : PixelInternalFormat.Rgba8,
                        mip.Width, mip.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, mip.Data);
                }
                ApplyBoundSampling(asset.Semantics.Wrap ?? (repeat ? TextureWrapPolicy.Repeat : TextureWrapPolicy.Clamp), sampling, generateMipmaps: false);
                return;
            }
            if (asset is ModernTextureAsset rgba)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, linearColor && rgba.Semantics.Transfer == TextureTransferFunction.Srgb
                    ? (PixelInternalFormat)0x8C43 : PixelInternalFormat.Rgba8,
                    rgba.Width, rgba.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba.Pixels);
                ApplyBoundSampling(asset.Semantics.Wrap ?? (repeat ? TextureWrapPolicy.Repeat : TextureWrapPolicy.Clamp), sampling, generateMipmaps: sampling.Mipmaps);
                return;
            }
            throw new NotSupportedException($"Prepared texture type {asset.GetType().Name} is unsupported.");
        }

        private static void ApplyBoundSampling(TextureWrapPolicy wrap, TextureSamplerDescriptor sampling,
            bool generateMipmaps)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)ToGlWrap(wrap.S));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)ToGlWrap(wrap.T));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)(sampling.LinearMagnification ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
            if (sampling.Mipmaps)
            {
                if (generateMipmaps)
                {
                    // RGBA sources generate once during promotion. KTX2 sources
                    // arrive with their transcoded authored mip chain intact.
                    GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                }
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)(sampling.LinearMinification
                        ? TextureMinFilter.LinearMipmapLinear
                        : TextureMinFilter.NearestMipmapNearest));
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)(sampling.LinearMinification ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
            }
            ApplyBoundAnisotropy(sampling.Anisotropy);
        }

        private static TextureWrapMode ToGlWrap(RepeatMode mode) => mode switch
        {
            RepeatMode.Repeat => TextureWrapMode.Repeat,
            RepeatMode.Mirror => TextureWrapMode.MirroredRepeat,
            _ => TextureWrapMode.ClampToEdge
        };

        private static void ClearPriorUploadErrors()
        {
            // glGetError reports process/context state, not the operation that
            // happened to query it. Establish a clean scope so a stale error
            // from an earlier render command cannot make a valid HD upload look
            // like the failing operation and force an unnecessary native fallback.
            for (int i = 0; i < 16; i++)
            {
                ErrorCode error = GL.GetError();
                if (error == ErrorCode.NoError) return;
                DebugLog.Line("render", "pre-existing GL error before texture upload: " + error);
            }
        }

        private static void ApplyBoundAnisotropy(int requested)
        {
            if (requested <= 1) return;
            try
            {
                string extensions = GL.GetString(StringName.Extensions) ?? "";
                if (!extensions.Contains("GL_EXT_texture_filter_anisotropic", StringComparison.Ordinal)
                    && !extensions.Contains("GL_ARB_texture_filter_anisotropic", StringComparison.Ordinal))
                    return;
                int max = GL.GetInteger((GetPName)MaxTextureMaxAnisotropyExt);
                if (max <= 1) return;
                GL.TexParameter(TextureTarget.Texture2D,
                    (TextureParameterName)TextureMaxAnisotropyExt, Math.Clamp(requested, 1, Math.Min(max, 16)));
            }
            catch
            {
                // Unsupported anisotropy is an allowed fallback; trilinear mips
                // remain the stability requirement.
            }
        }

        public static int DimensionLimit(TextureAssetClass assetClass, TextureAssetChannel channel)
        {
            _ = channel;
            return Math.Min(RequestedDimensionLimit(EffectiveQuality(), assetClass), HardwareMaxDimension());
        }

        internal static int RequestedDimensionLimit(TextureAssetQuality quality, TextureAssetClass assetClass)
        {
            int limit = quality switch
            {
                TextureAssetQuality.Low => assetClass == TextureAssetClass.Effect ? 512 : 1024,
                TextureAssetQuality.Medium => assetClass == TextureAssetClass.Effect ? 1024 : 2048,
                TextureAssetQuality.High => assetClass == TextureAssetClass.Effect ? 2048 : 4096,
                TextureAssetQuality.Ultra => assetClass == TextureAssetClass.Effect ? 4096 : 8192,
                _ => OperatingSystem.IsAndroid() ? 2048 : 4096
            };
            return assetClass == TextureAssetClass.Ui ? Math.Min(limit, 2048) : limit;
        }

        private static TextureAssetQuality EffectiveQuality()
            => EffectiveQualityForPlatform(RenderOptions.TextureQuality, OperatingSystem.IsAndroid());

        internal static TextureAssetQuality EffectiveQualityForPlatform(
            TextureAssetQuality requested, bool android)
        {
            if (requested != TextureAssetQuality.Automatic) return requested;
            return android ? TextureAssetQuality.Medium : TextureAssetQuality.High;
        }

        /// <summary>
        /// Scene-owned world/material replacements keep stable texture IDs and
        /// therefore use admission control rather than mid-match eviction.
        /// This budget is separate from cosmetic residency so one large map
        /// cannot evict a hunter/viewmodel texture that another subsystem owns.
        /// </summary>
        internal static long WorldMaterialMemoryBudgetBytes()
            => WorldMaterialMemoryBudgetBytesForQuality(
                EffectiveQuality(), OperatingSystem.IsAndroid());

        internal static long WorldMaterialMemoryBudgetBytesForQuality(
            TextureAssetQuality quality, bool android)
        {
            quality = EffectiveQualityForPlatform(quality, android);
            long mib = android
                ? quality switch
                {
                    TextureAssetQuality.Low => 128,
                    TextureAssetQuality.Medium => 256,
                    TextureAssetQuality.High => 384,
                    TextureAssetQuality.Ultra => 768,
                    _ => 256
                }
                : quality switch
                {
                    TextureAssetQuality.Low => 256,
                    TextureAssetQuality.Medium => 512,
                    TextureAssetQuality.High => 1024,
                    TextureAssetQuality.Ultra => 3072,
                    _ => 1024
                };
            return mib * 1024 * 1024;
        }

        private static int HardwareMaxDimension()
        {
            try
            {
                int value = GL.GetInteger(GetPName.MaxTextureSize);
                return Math.Clamp(value <= 0 ? ModernTextureAsset.MaximumDimension : value,
                    256, ModernTextureAsset.MaximumDimension);
            }
            catch { return ModernTextureAsset.MaximumDimension; }
        }

        private static long MemoryBudgetBytes()
        {
            TextureAssetQuality quality = EffectiveQuality();
#if ANDROID
            long mib = quality switch
            {
                TextureAssetQuality.Low => 96,
                TextureAssetQuality.Medium => 192,
                TextureAssetQuality.High => 384,
                TextureAssetQuality.Ultra => 512,
                _ => 192
            };
#else
            long mib = quality switch
            {
                TextureAssetQuality.Low => 128,
                TextureAssetQuality.Medium => 256,
                TextureAssetQuality.High => 512,
                TextureAssetQuality.Ultra => 2048,
                _ => 512
            };
#endif
            return mib * 1024 * 1024;
        }

        public void Clear()
        {
            foreach (Resident resident in _resident.Values) _releaseTexture(resident.Binding);
            _resident.Clear();
            _residentBytes = 0;
        }

        public void Dispose() => Clear();
    }
}
