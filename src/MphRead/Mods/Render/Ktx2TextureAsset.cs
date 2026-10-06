#if !MPHREAD_SERVER
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Ktx;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// Prepared KTX2/Basis payload. Basis ETC1S/UASTC is transcoded on the
    /// existing background texture worker directly to the best WebGPU block
    /// format enabled on the active adapter. No decompress/recompress step is
    /// performed on the render thread.
    /// </summary>
    internal sealed unsafe class Ktx2TextureAsset : PreparedTextureAsset
    {
        private static ReadOnlySpan<byte> Identifier =>
            new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
        private const int MaximumEncodedBytes = 256 * 1024 * 1024;

        private Ktx2TextureAsset(string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, int width, int height,
            GpuTextureCompressionFormat compressionFormat, CompressedTextureMip[] mips)
            : base(key, assetClass, channel, width, height)
        {
            CompressionFormat = compressionFormat;
            Mips = mips;
        }

        internal GpuTextureCompressionFormat CompressionFormat { get; }
        internal CompressedTextureMip[] Mips { get; }
        public override bool MipmapsAvailable => Mips.Length > 1;

        public override long EstimateGpuBytes(bool mipmaps)
        {
            long bytes = 0;
            int count = mipmaps ? Mips.Length : Math.Min(1, Mips.Length);
            for (int i = 0; i < count; i++) bytes = checked(bytes + Mips[i].Data.LongLength);
            return bytes;
        }

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
                    bool needsFit = width > maximumDimension || height > maximumDimension;
                    // WebGPU requires base compressed extents to be block-aligned.
                    // Retain the authored NPOT UV/aspect/texels through RGBA rather
                    // than padding/rescaling the image or allocating an invalid texture.
                    bool unalignedBlocks = !CompressedTextureLayout.HasAlignedBaseExtent(width, height);
                    bool basis = Ktx2.NeedsTranscoding(texture);

                    if (basis)
                    {
                        GpuTextureCompressionFormat preferred = needsFit || unalignedBlocks
                            ? GpuTextureCompressionFormat.None
                            : PreserveRgbaMips(assetClass)
                                ? ModernGraphicsCompat.PreferredCharacterTextureCompression
                                : ModernGraphicsCompat.PreferredTextureCompression;
                        if (preferred != GpuTextureCompressionFormat.None)
                        {
                            Transcode(texture, preferred);
                            return CopyCompressed(texture, key, assetClass, channel,
                                preferred, width, height);
                        }

                        TranscodeRgba(texture);
                        if (PreserveRgbaMips(assetClass))
                            return CopyRgbaMips(texture,key,assetClass,channel,maximumDimension,
                                needsFit ? "dimension-cap" : unalignedBlocks ? "unaligned-block-extent" : "adapter-format-fallback");
                        return CopyRgba(texture, key, assetClass, channel,
                            width, height, maximumDimension);
                    }

                    GpuTextureCompressionFormat direct = DirectCompression(texture->VkFormat);
                    if (!needsFit && !unalignedBlocks && direct != GpuTextureCompressionFormat.None
                        && ModernGraphicsCompat.TextureCompressionSupported(direct))
                    {
                        return CopyCompressed(texture, key, assetClass, channel,
                            direct, width, height);
                    }

                    if (direct != GpuTextureCompressionFormat.None && unalignedBlocks)
                        throw new InvalidDataException(
                            $"KTX2 fixed compressed base extent {width}x{height} is not aligned to 4x4 blocks. "
                            + "Use Basis Universal KTX2 or RGBA8 so the authored dimensions can be preserved.");

                    if (texture->VkFormat is Ktx2.VkFormat.R8G8B8A8Unorm
                        or Ktx2.VkFormat.R8G8B8A8Srgb)
                    {
                        if (PreserveRgbaMips(assetClass))
                            return CopyRgbaMips(texture,key,assetClass,channel,maximumDimension,
                                needsFit ? "dimension-cap" : "authored-rgba8");
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

        private static void Transcode(Ktx2.Texture* texture,
            GpuTextureCompressionFormat format)
        {
            Ktx2.TranscodeFormat target = format switch
            {
                GpuTextureCompressionFormat.Bc7Rgba => Ktx2.TranscodeFormat.BC7Rgba,
                GpuTextureCompressionFormat.Astc4x4Rgba => Ktx2.TranscodeFormat.Astc4X4Rgba,
                GpuTextureCompressionFormat.Etc2Rgba8 => Ktx2.TranscodeFormat.Etc2Rgba,
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };
            Ktx2.ErrorCode error = Ktx2.TranscodeBasis(texture, target,
                Ktx2.TranscodeFlagBits.HighQuality);
            if (error != Ktx2.ErrorCode.Success)
                throw new InvalidDataException($"Basis texture transcode failed: {error}.");
        }

        private static void TranscodeRgba(Ktx2.Texture* texture)
        {
            Ktx2.ErrorCode error = Ktx2.TranscodeBasis(texture,
                Ktx2.TranscodeFormat.Rgba32, Ktx2.TranscodeFlagBits.HighQuality);
            if (error != Ktx2.ErrorCode.Success)
                throw new InvalidDataException($"Basis RGBA transcode failed: {error}.");
        }

        private static Ktx2TextureAsset CopyCompressed(Ktx2.Texture* texture, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            GpuTextureCompressionFormat format, int width, int height)
        {
            int count = checked((int)Math.Max(1u, texture->NumLevels));
            var mips = new List<CompressedTextureMip>(count);
            for (int level = 0; level < count; level++)
            {
                Ktx2.ErrorCode error = Ktx2.GetImageOffset(texture, (uint)level, 0, 0, out nuint offset);
                if (error != Ktx2.ErrorCode.Success)
                    throw new InvalidDataException($"KTX2 mip {level} offset is invalid: {error}.");
                nuint nativeSize = Ktx2.GetImageSize(texture, (uint)level);
                if (nativeSize == 0 || nativeSize > Int32.MaxValue
                    || offset > texture->DataSize || nativeSize > texture->DataSize - offset)
                    throw new InvalidDataException($"KTX2 mip {level} has invalid bounds.");
                byte[] data = new byte[(int)nativeSize];
                Marshal.Copy((IntPtr)(texture->PData + offset), data, 0, data.Length);
                int mipWidth = Math.Max(1, width >> (int)level);
                int mipHeight = Math.Max(1, height >> (int)level);
                int expected = checked(Math.Max(1, (mipWidth + 3) / 4)
                    * Math.Max(1, (mipHeight + 3) / 4) * 16);
                if (data.Length != expected)
                    throw new InvalidDataException(
                        $"KTX2 mip {level} block size {data.Length} does not match expected {expected}.");
                mips.Add(new CompressedTextureMip(mipWidth, mipHeight, data));
            }
            return new Ktx2TextureAsset(key, assetClass, channel, width, height,
                format, mips.ToArray());
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

        private static bool PreserveRgbaMips(TextureAssetClass assetClass)
            => assetClass is TextureAssetClass.Hunter or TextureAssetClass.Weapon
                or TextureAssetClass.AlternateForm or TextureAssetClass.Turret;

        private static RgbaMipTextureAsset CopyRgbaMips(Ktx2.Texture* texture,string key,
            TextureAssetClass assetClass,TextureAssetChannel channel,int maximumDimension,string preparationReason)
        {
            int width=checked((int)texture->BaseWidth),height=checked((int)texture->BaseHeight);
            int count=checked((int)texture->NumLevels);
            int maximumLevels=1+(int)Math.Floor(Math.Log2(Math.Max(width,height)));
            if (count < 1 || count > maximumLevels)
                throw new InvalidDataException("KTX2 authored RGBA mip count is invalid.");
            int first=0;
            while (first < count && (Math.Max(1,width >> first) > maximumDimension
                || Math.Max(1,height >> first) > maximumDimension)) first++;
            if (first == count)
                throw new InvalidDataException("KTX2 authored RGBA chain has no level within the character texture cap.");
            var mips=new RgbaTextureMip[count-first];
            long total=0;
            for (int level=first;level<count;level++)
            {
                int mipWidth=Math.Max(1,width >> level),mipHeight=Math.Max(1,height >> level);
                int expected=checked(mipWidth*mipHeight*4);
                total=checked(total+expected);
                if (total > ModernTextureAsset.MaximumDecodedBytes)
                    throw new InvalidDataException("KTX2 authored RGBA chain exceeds the decoded-byte limit.");
                Ktx2.ErrorCode error=Ktx2.GetImageOffset(texture,(uint)level,0,0,out nuint offset);
                nuint size=Ktx2.GetImageSize(texture,(uint)level);
                if (error != Ktx2.ErrorCode.Success || texture->PData == null
                    || size != (nuint)expected || offset > texture->DataSize || size > texture->DataSize-offset)
                    throw new InvalidDataException($"KTX2 authored RGBA mip {level} has invalid bounds or layout.");
                byte[] pixels=new byte[expected];
                Marshal.Copy((IntPtr)(texture->PData+offset),pixels,0,expected);
                mips[level-first]=new(mipWidth,mipHeight,pixels);
            }
            return new RgbaMipTextureAsset(key,assetClass,channel,mips,first,preparationReason);
        }

        private static GpuTextureCompressionFormat DirectCompression(Ktx2.VkFormat format) =>
            format switch
            {
                Ktx2.VkFormat.BC7UnormBlock or Ktx2.VkFormat.BC7SrgbBlock
                    => GpuTextureCompressionFormat.Bc7Rgba,
                Ktx2.VkFormat.Etc2R8G8B8A8UnormBlock or Ktx2.VkFormat.Etc2R8G8B8A8SrgbBlock
                    => GpuTextureCompressionFormat.Etc2Rgba8,
                Ktx2.VkFormat.Astc4X4UnormBlock or Ktx2.VkFormat.Astc4X4SrgbBlock
                    => GpuTextureCompressionFormat.Astc4x4Rgba,
                _ => GpuTextureCompressionFormat.None
            };

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
