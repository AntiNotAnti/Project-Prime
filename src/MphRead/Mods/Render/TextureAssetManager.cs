using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly record struct Resident(int Binding, long Bytes, int Width, int Height);
        private const int TextureMaxAnisotropyExt = 0x84FE;
        private const int MaxTextureMaxAnisotropyExt = 0x84FF;
        private readonly Dictionary<string, Resident> _resident = new(StringComparer.Ordinal);
        private readonly Func<int> _allocateTexture;
        private readonly Action<int> _releaseTexture;
        private long _residentBytes;

        public TextureAssetManager(Func<int> allocateTexture, Action<int> releaseTexture)
        {
            _allocateTexture = allocateTexture;
            _releaseTexture = releaseTexture;
        }

        public long ResidentBytes => _residentBytes;
        public int ResidentCount => _resident.Count;

        // Only scene-owned optional variant leases use this. Stable base/maps remain pinned.
        internal bool ReleaseBinding(int binding)
        {
            string? key = null;
            foreach (var pair in _resident)
                if (pair.Value.Binding == binding) { key = pair.Key; break; }
            if (key == null) return false;
            Resident resident = _resident[key];
            _resident.Remove(key); _residentBytes -= resident.Bytes;
            _releaseTexture(binding); return true;
        }

        public int Upload(string key, Func<Stream?> open, TextureAssetClass assetClass,
            TextureAssetChannel channel, bool repeat, out int width, out int height)
        {
            int cap = DimensionLimit(assetClass, channel);
            TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(assetClass, channel);
            string cacheKey = key + "|" + assetClass + "|" + channel + "|" + cap + "|sample=" + sampling.CacheKey;
            if (_resident.TryGetValue(cacheKey, out Resident resident))
            {
                width = resident.Width; height = resident.Height; return resident.Binding;
            }
            width = height = 0;
            try
            {
                using Stream? stream = open();
                if (stream == null) return 0;
                PreparedTextureAsset asset = PreparedTextureCodec.Decode(
                    stream, key, assetClass, channel, cap);
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
            int width, int height, byte[] rgba, bool repeat, out int uploadedWidth, out int uploadedHeight)
        {
            int cap = DimensionLimit(assetClass, channel);
            TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(assetClass, channel);
            string cacheKey = key + "|" + assetClass + "|" + channel + "|" + cap + "|sample=" + sampling.CacheKey;
            if (_resident.TryGetValue(cacheKey, out Resident resident))
            {
                uploadedWidth = resident.Width; uploadedHeight = resident.Height; return resident.Binding;
            }
            try
            {
                ModernTextureAsset asset = ModernTextureAsset.FromRgba(key, assetClass, channel, width, height, rgba).Fit(cap);
                return Upload(cacheKey, asset, repeat, sampling, out uploadedWidth, out uploadedHeight);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException)
            {
                uploadedWidth = uploadedHeight = 0;
                DebugLog.Line("render", "modern RGBA texture ignored " + key + ": " + ex.Message);
                return 0;
            }
        }

        private int Upload(string cacheKey, PreparedTextureAsset asset, bool repeat,
            TextureSamplerDescriptor sampling, out int width, out int height)
        {
            width = asset.Width; height = asset.Height;
            bool mipmaps = sampling.Mipmaps && asset.MipmapsAvailable;
            sampling = sampling with { Mipmaps = mipmaps };
            long bytes = asset.EstimateGpuBytes(mipmaps);
            if (_residentBytes + bytes > MemoryBudgetBytes())
            {
                DebugLog.Line("render", "modern texture budget reached; native fallback for " + asset.Key);
                width = height = 0;
                return 0;
            }
            int texture = _allocateTexture();
            try
            {
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, texture);
                UploadPreparedBound(asset, repeat, sampling);
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("GPU texture upload failed.");
                _resident.Add(cacheKey, new(texture, bytes, width, height));
                _residentBytes += bytes;
                DebugLog.Line("render", "modern texture " + asset.Key + " " + width + "x" + height
                    + " resident=" + (_residentBytes / (1024.0 * 1024.0)).ToString("0.0") + " MiB"
                    + " " + TextureSamplingPolicy.Describe(sampling));
                return texture;
            }
            catch
            {
                _releaseTexture(texture);
                width = height = 0;
                return 0;
            }
            finally { GL.BindTexture(TextureTarget.Texture2D, 0); }
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
            TextureSamplerDescriptor sampling)
        {
            ClearPriorUploadErrors();
            if (asset is ModernTextureAsset rgba)
            {
                GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                    rgba.Width, rgba.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, rgba.Pixels);
                ApplyBoundSampling(repeat, sampling, generateMipmaps: sampling.Mipmaps);
                return;
            }
#if !MPHREAD_SERVER
            if (asset is RgbaMipTextureAsset authored)
            {
                sampling=sampling with {Mipmaps=sampling.Mipmaps && authored.MipmapsAvailable};
                GL.PixelStore(PixelStoreParameter.UnpackAlignment,4);
                if (ModernGraphicsCompat.Active)
                    ModernGraphicsCompat.UploadRgbaMipTexture(authored,sampling.Mipmaps);
                else
                {
                    int count=sampling.Mipmaps ? authored.Mips.Length : 1;
                    for (int level=0;level<count;level++)
                    {
                        RgbaTextureMip mip=authored.Mips[level];
                        GL.TexImage2D(TextureTarget.Texture2D,level,PixelInternalFormat.Rgba8,
                            mip.Width,mip.Height,0,PixelFormat.Rgba,PixelType.UnsignedByte,mip.Data);
                    }
                }
                ApplyBoundSampling(repeat,sampling,generateMipmaps:false);
                return;
            }
            if (asset is Ktx2TextureAsset compressed)
            {
                if (!ModernGraphicsCompat.Active)
                    throw new InvalidOperationException(
                        "Compressed KTX2 promotion requires the modern renderer.");
                sampling = sampling with
                {
                    Mipmaps = sampling.Mipmaps && compressed.MipmapsAvailable
                };
                ModernGraphicsCompat.UploadCompressedTexture(compressed, sampling.Mipmaps);
                ApplyBoundSampling(repeat, sampling, generateMipmaps: false);
                return;
            }
#endif
            throw new NotSupportedException($"Prepared texture type {asset.GetType().Name} is unsupported.");
        }

        private static void ApplyBoundSampling(bool repeat, TextureSamplerDescriptor sampling,
            bool generateMipmaps)
        {
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
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
