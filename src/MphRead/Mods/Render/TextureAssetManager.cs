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
                ModernTextureAsset asset = ModernTextureAsset.Decode(stream, key, assetClass, channel, cap);
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

        private int Upload(string cacheKey, ModernTextureAsset asset, bool repeat,
            TextureSamplerDescriptor sampling, out int width, out int height)
        {
            width = asset.Width; height = asset.Height;
            bool mipmaps = sampling.Mipmaps && (asset.Width > 1 || asset.Height > 1);
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
                ModernTextureAsset asset = ModernTextureAsset.Decode(source, key, assetClass, channel, cap);
                TextureSamplerDescriptor sampling = TextureSamplingPolicy.ResolveModern(assetClass, channel);
                sampling = sampling with
                {
                    Mipmaps = sampling.Mipmaps && (asset.Width > 1 || asset.Height > 1)
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

        internal static void UploadPreparedBound(ModernTextureAsset asset, bool repeat,
            TextureSamplerDescriptor sampling)
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                asset.Width, asset.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, asset.Pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)(sampling.LinearMagnification ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
            if (sampling.Mipmaps)
            {
                // Modern assets build their complete mip chain while the
                // texture is prepared, not on the first frame that happens to
                // look at the material.
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
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
        {
            if (RenderOptions.TextureQuality != TextureAssetQuality.Automatic) return RenderOptions.TextureQuality;
#if ANDROID
            return TextureAssetQuality.Medium;
#else
            return TextureAssetQuality.High;
#endif
        }

        private static int HardwareMaxDimension()
        {
            try
            {
                int value = GL.GetInteger(GetPName.MaxTextureSize);
                return Math.Clamp(value <= 0 ? ModernTextureAsset.MaximumDimension : value, 256, ModernTextureAsset.MaximumDimension);
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
