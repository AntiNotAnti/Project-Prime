#if !MPHREAD_SERVER
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Ktx;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// KTX2/Basis decoder for OpenGL/GLES. Portable RGBA fallback retains
    /// authored levels; explicitly enabled, observed block formats stay compressed.
    /// </summary>
    internal static unsafe class Ktx2TextureAsset
    {
        private static ReadOnlySpan<byte> Identifier =>
            new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        private const int MaximumEncodedBytes = 256 * 1024 * 1024;

        internal static bool IsKtx2(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= Identifier.Length && bytes[..Identifier.Length].SequenceEqual(Identifier);

        internal static PreparedTextureAsset Decode(Stream source, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension,
            TextureAssetSemantics? requestedSemantics = null, bool linearFiltering = false)
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
                    TextureAssetSemantics semantics = ResolveSemantics(texture, channel, requestedSemantics);
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
                                width, height, format, semantics: semantics);
                        }
#endif
#if !ANDROID
                        if (DesktopTextureCompressionPolicy.SupportsBc7
                            && HasFittingAuthoredLevel(texture, width, height, maximumDimension))
                        {
                            Ktx2.ErrorCode result = Ktx2.TranscodeBasis(texture,
                                Ktx2.TranscodeFormat.BC7Rgba, Ktx2.TranscodeFlagBits.HighQuality);
                            if (result == Ktx2.ErrorCode.Success)
                                return CopyCompressed(texture, key, assetClass, channel,
                                    width, height, GpuTextureCompressionFormat.Bc7Rgba, maximumDimension, semantics);
                            // A failed native transcode need not leave a reusable
                            // texture. Reopen the original payload before RGBA fallback.
                            Ktx2.Destroy(texture);
                            texture = null;
                            error = Ktx2.CreateFromMemory(in *bytes, (nuint)encoded.Length,
                                Ktx2.TextureCreateFlagBits.LoadImageData, out texture);
                            if (error != Ktx2.ErrorCode.Success || texture == null)
                                throw new InvalidDataException($"KTX2 fallback could not be reopened: {error}.");
                            ValidateShape(texture);
                        }
#endif
                        TranscodeRgba(texture);
                        return CopyRgbaChain(texture, key, assetClass, channel,
                            width, height, maximumDimension, semantics, linearFiltering);
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
                                width, height, fixedFormat, semantics: semantics);
                    }
#endif
#if !ANDROID
                    if (DesktopTextureCompressionPolicy.SupportsBc7
                        && texture->VkFormat is Ktx2.VkFormat.BC7UnormBlock or Ktx2.VkFormat.BC7SrgbBlock
                        && HasFittingAuthoredLevel(texture, width, height, maximumDimension))
                        return CopyCompressed(texture, key, assetClass, channel,
                            width, height, GpuTextureCompressionFormat.Bc7Rgba, maximumDimension, semantics);
#endif
                    if (texture->VkFormat is Ktx2.VkFormat.R8G8B8A8Unorm
                        or Ktx2.VkFormat.R8G8B8A8Srgb)
                    {
                        return CopyRgbaChain(texture, key, assetClass, channel,
                            width, height, maximumDimension, semantics, linearFiltering);
                    }

                    throw new InvalidDataException(
                        "KTX2 uses a fixed GPU format unsupported by this adapter. "
                        + "Use Basis Universal ETC1S/UASTC KTX2 for portable transcoding.");
                }
                finally
                {
                    if (texture != null) Ktx2.Destroy(texture);
                }
            }
        }

        internal static ModernTextureAsset DecodeRgba(Stream source, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension,
            int mipLevel = 0, TextureAssetSemantics? requestedSemantics = null)
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
                    TextureAssetSemantics semantics = ResolveSemantics(texture, channel, requestedSemantics);
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
                        maximumDimension, semantics, mipLevel);
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

        private static TextureAssetSemantics ResolveSemantics(Ktx2.Texture* texture,
            TextureAssetChannel channel, TextureAssetSemantics? requested)
        {
            TextureAssetSemantics fallback = requested ?? TextureAssetSemantics.ForChannel(channel);
            if (fallback.Channel != channel)
                throw new InvalidDataException("KTX2 texture semantics channel does not match the requested channel.");
            TextureTransferFunction? declared = DeclaredTransfer(texture);
            if (declared == TextureTransferFunction.Srgb
                && channel is TextureAssetChannel.Normal or TextureAssetChannel.Material or TextureAssetChannel.Mask)
                throw new InvalidDataException("Numeric KTX2 texture channels must use linear transfer.");
            return new TextureAssetSemantics(channel, declared ?? fallback.Transfer,
                fallback.Alpha, fallback.Wrap, fallback.AlphaFactor);
        }

        private static TextureTransferFunction? DeclaredTransfer(Ktx2.Texture* texture)
        {
            switch (texture->VkFormat)
            {
                case Ktx2.VkFormat.R8G8B8A8Srgb:
                case Ktx2.VkFormat.BC7SrgbBlock:
                case Ktx2.VkFormat.Etc2R8G8B8A8SrgbBlock:
                case Ktx2.VkFormat.Etc2R8G8B8SrgbBlock:
                case Ktx2.VkFormat.Etc2R8G8B8A1SrgbBlock:
                case Ktx2.VkFormat.Astc4X4SrgbBlock:
                    return TextureTransferFunction.Srgb;
                case Ktx2.VkFormat.R8G8B8A8Unorm:
                case Ktx2.VkFormat.BC7UnormBlock:
                case Ktx2.VkFormat.Etc2R8G8B8A8UnormBlock:
                case Ktx2.VkFormat.Etc2R8G8B8UnormBlock:
                case Ktx2.VkFormat.Etc2R8G8B8A1UnormBlock:
                case Ktx2.VkFormat.Astc4X4UnormBlock:
                    return TextureTransferFunction.Linear;
            }
            if (texture->PDfd == null) return null;
            byte transfer = ((byte*)texture->PDfd)[14];
            return transfer switch
            {
                1 => TextureTransferFunction.Linear,
                2 => TextureTransferFunction.Srgb,
                _ => null
            };
        }

        private static CompressedTextureAsset CopyCompressed(Ktx2.Texture* texture, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, GpuTextureCompressionFormat format, int maximumDimension = int.MaxValue,
            TextureAssetSemantics? semantics = null)
        {
            int maxLevels = GpuCompressedTextureMath.MipLevelCount(width, height);
            if (texture->NumLevels == 0 || texture->NumLevels > maxLevels)
                throw new InvalidDataException("KTX2 has an invalid compressed mip count.");
            uint first = 0;
            while (first + 1 < texture->NumLevels
                && (Math.Max(1, width >> (int)first) > maximumDimension
                    || Math.Max(1, height >> (int)first) > maximumDimension)) first++;
            if (Math.Max(1, width >> (int)first) > maximumDimension
                || Math.Max(1, height >> (int)first) > maximumDimension)
                throw new InvalidDataException("Compressed authored levels cannot satisfy the dimension cap.");
            var mips = new List<CompressedTextureMip>(checked((int)(texture->NumLevels - first)));
            for (uint level = first; level < texture->NumLevels; level++)
            {
                int w = Math.Max(1, width >> (int)level);
                int h = Math.Max(1, height >> (int)level);
                int size = GpuCompressedTextureMath.LevelBytes(w, h);
                Ktx2.ErrorCode error = Ktx2.GetImageOffset(texture, level, 0, 0, out nuint offset);
                nuint available = Ktx2.GetImageSize(texture, level);
                if (error != Ktx2.ErrorCode.Success || available < (nuint)size
                    || offset > texture->DataSize || (nuint)size > texture->DataSize - offset)
                    throw new InvalidDataException("Compressed KTX2 mip data is missing or truncated.");
                byte[] blocks = new byte[size];
                Marshal.Copy((IntPtr)(texture->PData + offset), blocks, 0, size);
                mips.Add(new CompressedTextureMip(w, h, blocks));
            }
            return new CompressedTextureAsset(key, assetClass, channel, format, mips.ToArray(), semantics);
        }

        private static bool HasFittingAuthoredLevel(Ktx2.Texture* texture, int width, int height, int cap)
        {
            int levels = checked((int)texture->NumLevels);
            if (levels < 1 || levels > GpuCompressedTextureMath.MipLevelCount(width, height)) return false;
            return Math.Max(1, width >> (levels - 1)) <= cap
                && Math.Max(1, height >> (levels - 1)) <= cap;
        }

        private static void TranscodeRgba(Ktx2.Texture* texture)
        {
            Ktx2.ErrorCode error = Ktx2.TranscodeBasis(texture,
                Ktx2.TranscodeFormat.Rgba32, Ktx2.TranscodeFlagBits.HighQuality);
            if (error != Ktx2.ErrorCode.Success)
                throw new InvalidDataException($"Basis RGBA transcode failed: {error}.");
        }

        private static ModernTextureAsset CopyRgba(Ktx2.Texture* texture, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, int maximumDimension, TextureAssetSemantics semantics,
            int mipLevel = 0, bool linearFiltering = false)
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
                (IntPtr)(texture->PData + offset), maximumDimension, semantics, linearFiltering);
        }

        private static PreparedTextureAsset CopyRgbaChain(Ktx2.Texture* texture, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, int maximumDimension, TextureAssetSemantics semantics,
            bool linearFiltering)
        {
            if (texture->NumLevels <= 1)
                return CopyRgba(texture, key, assetClass, channel, width, height, maximumDimension,
                    semantics, linearFiltering: linearFiltering);
            if (texture->NumLevels > GpuCompressedTextureMath.MipLevelCount(width, height))
                throw new InvalidDataException("KTX2 RGBA mip count exceeds its dimensions.");
            uint first = 0;
            while (first + 1 < texture->NumLevels
                && (Math.Max(1, width >> (int)first) > maximumDimension
                    || Math.Max(1, height >> (int)first) > maximumDimension)) first++;
            int firstWidth = Math.Max(1, width >> (int)first), firstHeight = Math.Max(1, height >> (int)first);
            if (firstWidth > maximumDimension || firstHeight > maximumDimension)
                return CopyRgba(texture, key, assetClass, channel, firstWidth, firstHeight, maximumDimension,
                    semantics, (int)first, linearFiltering);
            var levels = new CompressedTextureMip[checked((int)(texture->NumLevels - first))];
            for (uint level = first; level < texture->NumLevels; level++)
            {
                int w = Math.Max(1, width >> (int)level), h = Math.Max(1, height >> (int)level);
                int bytes = checked(w * h * 4);
                Ktx2.ErrorCode error = Ktx2.GetImageOffset(texture, level, 0, 0, out nuint offset);
                if (error != Ktx2.ErrorCode.Success || Ktx2.GetImageSize(texture, level) < (nuint)bytes
                    || offset > texture->DataSize || (nuint)bytes > texture->DataSize - offset)
                    throw new InvalidDataException("KTX2 authored RGBA mip is truncated.");
                byte[] pixels = new byte[bytes];
                Marshal.Copy((IntPtr)(texture->PData + offset), pixels, 0, bytes);
                levels[level - first] = new(w, h, pixels);
            }
            return new RgbaMipTextureAsset(key, assetClass, channel, levels, semantics);
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
