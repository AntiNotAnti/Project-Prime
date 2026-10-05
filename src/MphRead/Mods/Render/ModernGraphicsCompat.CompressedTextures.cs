#if !MPHREAD_SERVER
using System;
using System.Diagnostics;
using System.IO;
using OpenTK.Graphics.OpenGL;
using Silk.NET.WebGPU;
using WgpuTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace MphRead.Mods.Render
{
    internal sealed unsafe partial class ModernGraphicsCompat
    {
        internal static GpuTextureCompressionFormat? TextureCompressionForCheck { get; set; }
        internal static GpuTextureCompressionFormat PreferredCharacterTextureCompression
        {
            get
            {
                GpuTextureCompressionFormat preferred = PreferredTextureCompression;
                // Samus's high-contrast armor failed the ETC2 image-quality budget.
                // Keep the bounded RGBA fallback until a better ETC2 tier is authored.
                // Explicit diagnostics can still inspect the rejected format.
                return ResolveCharacterTextureCompression(preferred, TextureCompressionForCheck != null);
            }
        }
        internal static GpuTextureCompressionFormat ResolveCharacterTextureCompression(
            GpuTextureCompressionFormat preferred, bool explicitDiagnostic = false)
            => !explicitDiagnostic && preferred == GpuTextureCompressionFormat.Etc2Rgba8
                ? GpuTextureCompressionFormat.None : preferred;
        internal static GpuTextureCompressionFormat PreferredTextureCompression
        {
            get
            {
                ModernGraphicsCompat? current = _current;
                if (current == null) return GpuTextureCompressionFormat.None;
                if (TextureCompressionForCheck is GpuTextureCompressionFormat forced)
                {
                    if (forced != GpuTextureCompressionFormat.None && !TextureCompressionSupported(forced))
                        throw new InvalidOperationException("Requested diagnostic texture compression is unsupported.");
                    return forced;
                }
#if ANDROID
                if (current._device.SupportsTextureCompressionAstc)
                    return GpuTextureCompressionFormat.Astc4x4Rgba;
                if (current._device.SupportsTextureCompressionEtc2)
                    return GpuTextureCompressionFormat.Etc2Rgba8;
                if (current._device.SupportsTextureCompressionBc)
                    return GpuTextureCompressionFormat.Bc7Rgba;
#else
                // The full character shader showed BC7 corruption on Metal even
                // when isolated texel probes passed. Use the verified ASTC path
                // on adapters exposing it; keep BC7 for other desktop backends.
                if (current._device.Backend == GraphicsBackend.Metal && current._device.SupportsTextureCompressionAstc)
                    return GpuTextureCompressionFormat.Astc4x4Rgba;
                if (current._device.SupportsTextureCompressionBc)
                    return GpuTextureCompressionFormat.Bc7Rgba;
                if (current._device.SupportsTextureCompressionAstc)
                    return GpuTextureCompressionFormat.Astc4x4Rgba;
                if (current._device.SupportsTextureCompressionEtc2)
                    return GpuTextureCompressionFormat.Etc2Rgba8;
#endif
                return GpuTextureCompressionFormat.None;
            }
        }

        internal static bool TextureCompressionSupported(GpuTextureCompressionFormat format)
        {
            ModernGraphicsCompat? current = _current;
            if (current == null) return false;
            return format switch
            {
                GpuTextureCompressionFormat.Bc7Rgba => current._device.SupportsTextureCompressionBc,
                GpuTextureCompressionFormat.Etc2Rgba8 => current._device.SupportsTextureCompressionEtc2,
                GpuTextureCompressionFormat.Astc4x4Rgba => current._device.SupportsTextureCompressionAstc,
                _ => false
            };
        }

        internal static void UploadCompressedTexture(Ktx2TextureAsset asset, bool mipmaps)
        {
            ModernGraphicsCompat self = Current;
            if (!TextureCompressionSupported(asset.CompressionFormat))
                throw new InvalidOperationException(
                    $"WebGPU compression {asset.CompressionFormat} is not enabled on this adapter.");
            int id = self._resources.BoundTexture(self._resources.ActiveTextureUnit);
            if (id == 0) throw new InvalidOperationException("No texture is bound.");
            CompressedTextureMip[] levels = mipmaps && asset.Mips.Length > 1
                ? asset.Mips : new[] { asset.Mips[0] };
            self._resources.CompressedTexImage2D(TextureTarget.Texture2D,
                asset.Width, asset.Height, asset.CompressionFormat, levels);
            self.EnsureTexture(id);
        }

        private void UploadCompressedTexture(NativeTexture native,
            CompressedTextureMip[] levels, int mipCount)
        {
            if (levels.Length < mipCount)
                throw new InvalidOperationException("Compressed texture mip chain is incomplete.");

            long start = PerformanceStart();
            FlushCommands();
            long uploaded = 0;
            for (int level = 0; level < mipCount; level++)
            {
                CompressedTextureMip mip = levels[level];
                uint blocksX = (uint)Math.Max(1, (mip.Width + 3) / 4);
                uint blocksY = (uint)Math.Max(1, (mip.Height + 3) / 4);
                uint bytesPerRow = checked(blocksX * 16u);
                int expected = checked((int)(bytesPerRow * blocksY));
                if (mip.Data.Length != expected)
                    throw new InvalidDataException(
                        $"Compressed mip {level} is {mip.Data.Length} bytes; expected {expected}.");

                var destination = new ImageCopyTexture
                {
                    Texture = native.Texture,
                    Origin = new Origin3D(0, 0, 0),
                    Aspect = TextureAspect.All,
                    MipLevel = (uint)level
                };
                var layout = new TextureDataLayout
                {
                    BytesPerRow = bytesPerRow,
                    RowsPerImage = blocksY
                };
                // WebGPU compressed copies operate on complete texel blocks.
                // The physical mip extent is rounded up to the 4x4 block edge.
                var extent = new Extent3D(blocksX * 4u, blocksY * 4u, 1);
                fixed (byte* data = mip.Data)
                {
                    _api.QueueWriteTexture(_queue, destination, data,
                        (nuint)mip.Data.Length, layout, extent);
                }
                uploaded += mip.Data.LongLength;
            }

            if (start != 0)
            {
                _textureUploadBytes += uploaded;
                _textureUploadMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            }
        }

        private static WgpuTextureFormat CompressedNativeTextureFormat(
            GpuTextureCompressionFormat format) =>
            format switch
            {
                GpuTextureCompressionFormat.Bc7Rgba => WgpuTextureFormat.BC7RgbaUnorm,
                GpuTextureCompressionFormat.Etc2Rgba8 => WgpuTextureFormat.Etc2Rgba8Unorm,
                GpuTextureCompressionFormat.Astc4x4Rgba => WgpuTextureFormat.Astc4x4Unorm,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
    }
}
#endif
