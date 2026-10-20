#if !MPHREAD_SERVER
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ktx;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// KTX2/Basis decoder for OpenGL/GLES. ETC1S/UASTC is transcoded to
    /// ordinary RGBA before the existing texture manager uploads it.
    /// </summary>
    internal static unsafe class Ktx2TextureAsset
    {
        private static ReadOnlySpan<byte> Identifier =>
            new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        private const int MaximumEncodedBytes = 256 * 1024 * 1024;

        internal static bool IsKtx2(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= Identifier.Length && bytes[..Identifier.Length].SequenceEqual(Identifier);

        internal static PreparedTextureAsset Decode(Stream source, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension)
        {
            byte[] encoded = ReadEncoded(source);
            fixed (byte* bytes = encoded)
            {
                Ktx2.Texture* texture = null;
                Ktx2.ErrorCode error = Ktx2.CreateFromMemory(in *bytes, (nuint)encoded.Length,
                    Ktx2.TextureCreateFlagBits.LoadImageData, out texture);
                if (error != Ktx2.ErrorCode.Success || texture == null)
                    throw new InvalidDataException($"KTX2 image could not be opened: {error}.");
                try
                {
                    ValidateShape(texture);
                    int width = checked((int)texture->BaseWidth);
                    int height = checked((int)texture->BaseHeight);
                    maximumDimension = Math.Clamp(maximumDimension, 1,
                        ModernTextureAsset.MaximumDimension);
                    if (Ktx2.NeedsTranscoding(texture))
                    {
#if ANDROID
                        // GLES3 guarantees ETC2 RGBA8. Prefer ASTC 4x4 only when
                        // the current EGL owner previously advertised it.
                        if (AndroidTextureCompressionPolicy.Enabled
                            && width <= maximumDimension && height <= maximumDimension)
                        {
                            GpuTextureCompressionFormat format = AndroidTextureCompressionPolicy.Preferred;
                            Ktx2.TranscodeFormat target = format == GpuTextureCompressionFormat.Astc4x4Rgba
                                ? Ktx2.TranscodeFormat.Astc4X4Rgba
                                : Ktx2.TranscodeFormat.Etc2Rgba;
                            Ktx2.ErrorCode result = Ktx2.TranscodeBasis(texture, target,
                                Ktx2.TranscodeFlagBits.HighQuality);
                            if (result != Ktx2.ErrorCode.Success)
                                throw new InvalidDataException($"GPU block transcode failed: {result}.");
                            return CopyCompressed(texture, key, assetClass, channel,
                                width, height, format);
                        }
#endif
                        TranscodeRgba(texture);
                        return CopyRgba(texture, key, assetClass, channel,
                            width, height, maximumDimension);
                    }

#if ANDROID
                    if (AndroidTextureCompressionPolicy.Enabled
                        && width <= maximumDimension && height <= maximumDimension)
                    {
                        GpuTextureCompressionFormat fixedFormat = texture->VkFormat switch
                        {
                            Ktx2.VkFormat.Etc2R8G8B8A8UnormBlock => GpuTextureCompressionFormat.Etc2Rgba8,
                            Ktx2.VkFormat.Astc4X4UnormBlock when AndroidTextureCompressionPolicy.SupportsAstc
                                => GpuTextureCompressionFormat.Astc4x4Rgba,
                            _ => GpuTextureCompressionFormat.None
                        };
                        if (fixedFormat != GpuTextureCompressionFormat.None)
                            return CopyCompressed(texture, key, assetClass, channel,
                                width, height, fixedFormat);
                    }
#endif
                    if (texture->VkFormat is Ktx2.VkFormat.R8G8B8A8Unorm
                        or Ktx2.VkFormat.R8G8B8A8Srgb)
                    {
                        return CopyRgba(texture, key, assetClass, channel,
                            width, height, maximumDimension);
                    }

                    throw new InvalidDataException(
                        "KTX2 uses a fixed GPU format unsupported by this adapter. "
                        + "Use Basis Universal ETC1S/UASTC KTX2 for portable transcoding.");
                }
                finally
                {
                    Ktx2.Destroy(texture);
                }
            }
        }

        internal static ModernTextureAsset DecodeRgba(Stream source, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension,
            int mipLevel = 0)
        {
            byte[] encoded = ReadEncoded(source);
            fixed (byte* bytes = encoded)
            {
                Ktx2.Texture* texture = null;
                Ktx2.ErrorCode error = Ktx2.CreateFromMemory(in *bytes, (nuint)encoded.Length,
                    Ktx2.TextureCreateFlagBits.LoadImageData, out texture);
                if (error != Ktx2.ErrorCode.Success || texture == null)
                    throw new InvalidDataException($"KTX2 image could not be opened: {error}.");
                try
                {
                    ValidateShape(texture);
                    if (Ktx2.NeedsTranscoding(texture)) TranscodeRgba(texture);
                    if (texture->VkFormat is not (Ktx2.VkFormat.R8G8B8A8Unorm
                        or Ktx2.VkFormat.R8G8B8A8Srgb))
                    {
                        throw new InvalidDataException(
                            "Fixed compressed KTX2 cannot be converted to RGBA by the Basis transcoder.");
                    }
                    if (mipLevel < 0 || mipLevel >= texture->NumLevels)
                        throw new InvalidDataException("KTX2 diagnostic mip level is invalid.");
                    return CopyRgba(texture, key, assetClass, channel,
                        Math.Max(1, checked((int)texture->BaseWidth) >> mipLevel),
                        Math.Max(1, checked((int)texture->BaseHeight) >> mipLevel),
                        maximumDimension, mipLevel);
                }
                finally
                {
                    Ktx2.Destroy(texture);
                }
            }
        }

        internal static void Validate(Stream source)
        {
            byte[] encoded = ReadEncoded(source);
            fixed (byte* bytes = encoded)
            {
                Ktx2.Texture* texture = null;
                Ktx2.ErrorCode error = Ktx2.CreateFromMemory(in *bytes, (nuint)encoded.Length,
                    Ktx2.TextureCreateFlagBits.LoadImageData, out texture);
                if (error != Ktx2.ErrorCode.Success || texture == null)
                    throw new InvalidDataException($"Malformed KTX2 image: {error}.");
                try { ValidateShape(texture); }
                finally { Ktx2.Destroy(texture); }
            }
        }

        private static void ValidateShape(Ktx2.Texture* texture)
        {
            if (texture->NumDimensions != 2 || texture->BaseWidth == 0 || texture->BaseHeight == 0
                || texture->BaseWidth > ModernTextureAsset.MaximumDimension
                || texture->BaseHeight > ModernTextureAsset.MaximumDimension
                || texture->BaseDepth > 1 || texture->IsArray || texture->IsCubemap
                || texture->NumFaces != 1)
            {
                throw new InvalidDataException(
                    "Project Prime KTX2 textures must be one non-array 2D image up to 8192x8192.");
            }
        }

#if ANDROID
        private static CompressedTextureAsset CopyCompressed(Ktx2.Texture* texture, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, GpuTextureCompressionFormat format)
        {
            int maxLevels = 1;
            for (int dim = Math.Max(width, height); dim > 1; dim >>= 1) maxLevels++;
            if (texture->NumLevels == 0 || texture->NumLevels > maxLevels)
                throw new InvalidDataException("KTX2 has an invalid compressed mip count.");
            var mips = new List<CompressedTextureMip>((int)texture->NumLevels);
            for (uint level = 0; level < texture->NumLevels; level++)
            {
                int w = Math.Max(1, width >> (int)level);
                int h = Math.Max(1, height >> (int)level);
                int size = checked(((w + 3) / 4) * ((h + 3) / 4) * 16);
                Ktx2.ErrorCode error = Ktx2.GetImageOffset(texture, level, 0, 0, out nuint offset);
                nuint available = Ktx2.GetImageSize(texture, level);
                if (error != Ktx2.ErrorCode.Success || available < (nuint)size
                    || offset > texture->DataSize || (nuint)size > texture->DataSize - offset)
                    throw new InvalidDataException("Compressed KTX2 mip data is missing or truncated.");
                byte[] blocks = new byte[size];
                Marshal.Copy((IntPtr)(texture->PData + offset), blocks, 0, size);
                mips.Add(new CompressedTextureMip(w, h, blocks));
            }
            return new CompressedTextureAsset(key, assetClass, channel, format, mips.ToArray());
        }
#endif

        private static void TranscodeRgba(Ktx2.Texture* texture)
        {
            Ktx2.ErrorCode error = Ktx2.TranscodeBasis(texture,
                Ktx2.TranscodeFormat.Rgba32, Ktx2.TranscodeFlagBits.HighQuality);
            if (error != Ktx2.ErrorCode.Success)
                throw new InvalidDataException($"Basis RGBA transcode failed: {error}.");
        }

        private static ModernTextureAsset CopyRgba(Ktx2.Texture* texture, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, int maximumDimension, int mipLevel = 0)
        {
            Ktx2.ErrorCode error = Ktx2.GetImageOffset(texture, (uint)mipLevel, 0, 0, out nuint offset);
            if (error != Ktx2.ErrorCode.Success)
                throw new InvalidDataException($"KTX2 base image offset is invalid: {error}.");
            int expected = checked(width * height * 4);
            nuint nativeSize = Ktx2.GetImageSize(texture, (uint)mipLevel);
            if (nativeSize < (nuint)expected || offset > texture->DataSize
                || (nuint)expected > texture->DataSize - offset)
                throw new InvalidDataException("KTX2 RGBA base image is truncated.");
            return ModernTextureAsset.FromRgbaPointer(
                key, assetClass, channel, width, height,
                (IntPtr)(texture->PData + offset), maximumDimension);
        }

        private static byte[] ReadEncoded(Stream source)
        {
            if (source.CanSeek && source.Length - source.Position > MaximumEncodedBytes)
                throw new InvalidDataException("KTX2 image exceeds the 256 MiB encoded limit.");
            using var output = new MemoryStream();
            byte[] buffer = new byte[81920];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaximumEncodedBytes)
                    throw new InvalidDataException("KTX2 image exceeds the 256 MiB encoded limit.");
                output.Write(buffer, 0, read);
            }
            byte[] bytes = output.ToArray();
            if (!IsKtx2(bytes)) throw new InvalidDataException("Malformed KTX2 identifier.");
            return bytes;
        }
    }
}
#endif
