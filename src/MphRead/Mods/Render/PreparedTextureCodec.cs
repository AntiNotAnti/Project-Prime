using System;
using System.IO;

namespace MphRead.Mods.Render
{
    internal static class PreparedTextureCodec
    {
        internal static bool IsKtx2Payload(ReadOnlySpan<byte> bytes)
        {
#if !MPHREAD_SERVER
            return Ktx2TextureAsset.IsKtx2(bytes);
#else
            return false;
#endif
        }
        internal static PreparedTextureAsset Decode(Stream source, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel, int maximumDimension,
            TextureAssetSemantics? semantics = null, bool linearFiltering = false)
        {
            Stream readable = Seekable(source, out MemoryStream? owned);
            try
            {
#if !MPHREAD_SERVER
                if (IsKtx2(readable))
                {
                    try
                    {
                        PreparedTextureAsset ktx = Ktx2TextureAsset.Decode(readable, key, assetClass, channel,
                            maximumDimension, semantics, linearFiltering);
                        return linearFiltering
                            ? ColorTexturePolicy.PrepareForLinearFiltering(ktx, ktx.Semantics.Alpha, ktx.Semantics.Wrap)
                            : ktx;
                    }
                    catch (Exception ex) when (ex is DllNotFoundException
                        or EntryPointNotFoundException or BadImageFormatException)
                    {
                        throw new InvalidDataException(
                            "KTX2/Basis runtime decoder is unavailable on this platform.", ex);
                    }
                }
#endif
                ModernTextureAsset decoded = ModernTextureAsset.Decode(readable, key, assetClass, channel,
                    maximumDimension, semantics, linearFiltering);
                return linearFiltering
                    ? ColorTexturePolicy.PrepareForLinearFiltering(decoded, decoded.Semantics.Alpha,
                        decoded.Semantics.Wrap)
                    : decoded;
            }
            finally
            {
                owned?.Dispose();
            }
        }

        internal static ModernTextureAsset DecodeRgba(Stream source, string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int maximumDimension = ModernTextureAsset.MaximumDimension,
            TextureAssetSemantics? semantics = null)
        {
            Stream readable = Seekable(source, out MemoryStream? owned);
            try
            {
#if !MPHREAD_SERVER
                if (IsKtx2(readable))
                {
                    try
                    {
                        return Ktx2TextureAsset.DecodeRgba(readable, key, assetClass,
                            channel, maximumDimension, requestedSemantics: semantics);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException
                        or EntryPointNotFoundException or BadImageFormatException)
                    {
                        throw new InvalidDataException(
                            "KTX2/Basis runtime decoder is unavailable on this platform.", ex);
                    }
                }
#endif
                return ModernTextureAsset.Decode(readable, key, assetClass, channel,
                    maximumDimension, semantics);
            }
            finally
            {
                owned?.Dispose();
            }
        }

        internal static void ValidateKtx2(Stream source)
        {
#if !MPHREAD_SERVER
            Stream readable = Seekable(source, out MemoryStream? owned);
            try
            {
                try
                {
                    Ktx2TextureAsset.Validate(readable);
                }
                catch (Exception ex) when (ex is DllNotFoundException
                    or EntryPointNotFoundException or BadImageFormatException)
                {
                    throw new InvalidDataException(
                        "KTX2/Basis runtime decoder is unavailable on this platform.", ex);
                }
            }
            finally
            {
                owned?.Dispose();
            }
#else
            _ = source;
#endif
        }

        private static bool IsKtx2(Stream source)
        {
            long start = source.Position;
            Span<byte> magic = stackalloc byte[12];
            int read = source.Read(magic);
            source.Position = start;
#if !MPHREAD_SERVER
            return read == magic.Length && Ktx2TextureAsset.IsKtx2(magic);
#else
            return false;
#endif
        }

        private static Stream Seekable(Stream source, out MemoryStream? owned)
        {
            if (source == null || !source.CanRead)
                throw new InvalidDataException("Texture source is unreadable.");
            owned = null;
            if (source.CanSeek) return source;
            owned = new MemoryStream();
            source.CopyTo(owned);
            owned.Position = 0;
            return owned;
        }
    }
}
