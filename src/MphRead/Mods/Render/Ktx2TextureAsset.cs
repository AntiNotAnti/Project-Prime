#if !MPHREAD_SERVER
using System;
using System.IO;
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
                        TranscodeRgba(texture);
                        return CopyRgba(texture, key, assetClass, channel,
                            width, height, maximumDimension);
                    }

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
