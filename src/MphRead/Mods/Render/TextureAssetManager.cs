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
            string cacheKey = key + "|" + assetClass + "|" + channel + "|" + cap + "|mip=" + RenderOptions.TextureMipmaps;
            if (_resident.TryGetValue(cacheKey, out Resident resident))
            {
                width = resident.Width; height = resident.Height; return resident.Binding;
            }
            width = height = 0;
            try
            {
                using Stream? stream = open();
                if (stream == null) return 0;
                ModernTextureAsset asset = ModernTextureAsset.Decode(stream, key, assetClass, channel).Fit(cap);
                return Upload(cacheKey, asset, repeat, out width, out height);
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
            string cacheKey = key + "|" + assetClass + "|" + channel + "|" + cap + "|mip=" + RenderOptions.TextureMipmaps;
            if (_resident.TryGetValue(cacheKey, out Resident resident))
            {
                uploadedWidth = resident.Width; uploadedHeight = resident.Height; return resident.Binding;
            }
            try
            {
                ModernTextureAsset asset = ModernTextureAsset.FromRgba(key, assetClass, channel, width, height, rgba).Fit(cap);
                return Upload(cacheKey, asset, repeat, out uploadedWidth, out uploadedHeight);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException
                or OverflowException or InvalidOperationException)
            {
                uploadedWidth = uploadedHeight = 0;
                DebugLog.Line("render", "modern RGBA texture ignored " + key + ": " + ex.Message);
                return 0;
            }
        }

        private int Upload(string cacheKey, ModernTextureAsset asset, bool repeat, out int width, out int height)
        {
            width = asset.Width; height = asset.Height;
            bool mipmaps = RenderOptions.TextureFiltering && RenderOptions.TextureMipmaps && (asset.Width > 1 || asset.Height > 1);
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
                UploadPreparedBound(asset, repeat, mipmaps);
                if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("GPU texture upload failed.");
                _resident.Add(cacheKey, new(texture, bytes, width, height));
                _residentBytes += bytes;
                DebugLog.Line("render", "modern texture " + asset.Key + " " + width + "x" + height
                    + " resident=" + (_residentBytes / (1024.0 * 1024.0)).ToString("0.0") + " MiB");
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
                ModernTextureAsset asset = ModernTextureAsset.Decode(source, key, assetClass, channel)
                    .Fit(DimensionLimit(assetClass, channel));
                bool mipmaps = RenderOptions.TextureFiltering && RenderOptions.TextureMipmaps && (asset.Width > 1 || asset.Height > 1);
                UploadPreparedBound(asset, repeat, mipmaps);
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

        private static void UploadPreparedBound(ModernTextureAsset asset, bool repeat, bool mipmaps)
        {
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                asset.Width, asset.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, asset.Pixels);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS,
                (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT,
                (int)(repeat ? TextureWrapMode.Repeat : TextureWrapMode.ClampToEdge));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            if (mipmaps)
            {
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                    (int)TextureMinFilter.LinearMipmapLinear);
            }
            else
            {
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
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
