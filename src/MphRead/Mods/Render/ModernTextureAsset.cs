using System;
using System.IO;
using System.Runtime.InteropServices;
using ReFuel.Stb;

namespace MphRead.Mods.Render
{
    internal enum TextureAssetClass
    {
        World,
        Hunter,
        Weapon,
        AlternateForm,
        Turret,
        Effect,
        Ui
    }

    internal enum TextureAssetChannel
    {
        Albedo,
        Normal,
        Material,
        Emissive,
        Effect,
        Mask
    }

    /// <summary>
    /// Backend-independent decoded texture. Authoring formats stop here; GPU upload,
    /// quality policy and residency live in <see cref="TextureAssetManager"/>.
    /// </summary>
    internal sealed class ModernTextureAsset : PreparedTextureAsset
    {
        public const int MaximumDimension = 8192;
        public const long MaximumPixels = 64L * 1024 * 1024;
        public const long MaximumDecodedBytes = MaximumPixels * 4;

        public byte[] Pixels { get; }

        private ModernTextureAsset(string key, TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, byte[] pixels)
            : base(key, assetClass, channel, width, height)
        {
            Pixels = pixels;
        }

        public static ModernTextureAsset Decode(Stream source, string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, int maximumDimension = MaximumDimension)
        {
            if (source == null || !source.CanRead) throw new InvalidDataException("Texture source is unreadable.");
            maximumDimension = Math.Clamp(maximumDimension, 1, MaximumDimension);
            MemoryStream? owned = null;
            if (!source.CanSeek)
            {
                owned = new MemoryStream();
                source.CopyTo(owned);
                owned.Position = 0;
                source = owned;
            }
            try
            {
                long start = source.Position;
                Span<byte> header = stackalloc byte[18];
                int read = source.Read(header);
                source.Position = start;
                if (read == header.Length && IsSupportedTga(header))
                    return DecodeTga(source, key, assetClass, channel, maximumDimension);
#if ANDROID
                using var bounds = new Android.Graphics.BitmapFactory.Options
                {
                    InScaled = false, InPremultiplied = false, InJustDecodeBounds = true
                };
                _ = Android.Graphics.BitmapFactory.DecodeStream(source, null, bounds);
                source.Position = start;
                ValidateDimensions(bounds.OutWidth, bounds.OutHeight);
                int sample = 1;
                while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maximumDimension)
                    sample *= 2;
                using var options = new Android.Graphics.BitmapFactory.Options
                {
                    InScaled = false, InPremultiplied = false, InSampleSize = sample
                };
                using var image = Android.Graphics.BitmapFactory.DecodeStream(source, null, options)
                    ?? throw new InvalidDataException("Android could not decode the texture image.");
                ValidateDimensions(image.Width, image.Height);
                byte[] rgba = new byte[checked(image.Width * image.Height * 4)];
                int[] row = new int[image.Width];
                for (int y = 0; y < image.Height; y++)
                {
                    image.GetPixels(row, 0, image.Width, 0, y, image.Width, 1);
                    for (int x = 0; x < image.Width; x++)
                    {
                        int pixel = row[x], offset = (y * image.Width + x) * 4;
                        rgba[offset] = (byte)(pixel >> 16);
                        rgba[offset + 1] = (byte)(pixel >> 8);
                        rgba[offset + 2] = (byte)pixel;
                        rgba[offset + 3] = (byte)(pixel >> 24);
                    }
                }
                return new ModernTextureAsset(key, assetClass, channel, image.Width, image.Height, rgba)
                    .Fit(maximumDimension);
#else
                StbImage image;
                try { image = StbImage.Load(source, StbiImageFormat.Rgba); }
                catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
                { throw new InvalidDataException("Texture image could not be decoded.", ex); }
                using (image)
                {
                    ValidateDimensions(image.Width, image.Height);
                    if (image.ImagePointer == IntPtr.Zero) throw new InvalidDataException("Texture decoder returned no pixels.");
                    (int targetWidth, int targetHeight) = FitDimensions(
                        image.Width, image.Height, maximumDimension);
                    if (targetWidth != image.Width || targetHeight != image.Height)
                    {
                        // STB already owns a full decoded RGBA surface. When a
                        // high-resolution source is capped, sample directly from
                        // that native surface into the final managed asset instead
                        // of first allocating a second full-resolution byte[].
                        byte[] fitted = Resample(image.ImagePointer, image.Width, image.Height,
                            targetWidth, targetHeight, channel == TextureAssetChannel.Normal);
                        return new ModernTextureAsset(key, assetClass, channel,
                            targetWidth, targetHeight, fitted);
                    }

                    byte[] rgba = new byte[checked(image.Width * image.Height * 4)];
                    Marshal.Copy(image.ImagePointer, rgba, 0, rgba.Length);
                    return new ModernTextureAsset(key, assetClass, channel, image.Width, image.Height, rgba);
                }
#endif
            }
            finally { owned?.Dispose(); }
        }

        internal static (int Width, int Height) ProbeDimensions(ReadOnlySpan<byte> bytes)
        {
            int width = 0, height = 0;
            ReadOnlySpan<byte> ktx2 = new byte[]
                { 0xAB,0x4B,0x54,0x58,0x20,0x32,0x30,0xBB,0x0D,0x0A,0x1A,0x0A };
            if (bytes.Length >= 28 && bytes[..12].SequenceEqual(ktx2))
            {
                uint encodedWidth = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[20..24]);
                uint encodedHeight = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes[24..28]);
                width = encodedWidth <= Int32.MaxValue ? (int)encodedWidth : 0;
                height = encodedHeight <= Int32.MaxValue ? (int)encodedHeight : 0;
            }
            else if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }))
            {
                width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[16..20]);
                height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes[20..24]);
            }
            else if (bytes.Length >= 4 && bytes[0] == 255 && bytes[1] == 216)
            {
                int at = 2;
                while (at + 3 < bytes.Length)
                {
                    if (bytes[at++] != 255) break;
                    while (at < bytes.Length && bytes[at] == 255) at++;
                    if (at >= bytes.Length) break;
                    int marker = bytes[at++];
                    if (marker is 216 or 217 or 218) break;
                    if (at + 2 > bytes.Length) break;
                    int length = (bytes[at] << 8) | bytes[at + 1];
                    if (length < 2 || at + length > bytes.Length) break;
                    if (marker is 192 or 193 or 194 or 195 or 197 or 198 or 199 or 201 or 202 or 203 or 205 or 206 or 207
                        && length >= 8)
                    {
                        height = (bytes[at + 3] << 8) | bytes[at + 4];
                        width = (bytes[at + 5] << 8) | bytes[at + 6];
                        break;
                    }
                    at += length;
                }
            }
            else if (bytes.Length >= 26 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
            {
                width = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes[18..22]);
                int rawHeight = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes[22..26]);
                height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
            }
            else if (bytes.Length >= 18 && bytes[2] is 1 or 2 or 3 or 9 or 10 or 11)
            {
                width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[12..14]);
                height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes[14..16]);
            }
            ValidateDimensions(width, height);
            return (width, height);
        }

        internal static string? PortableEncodedExtension(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length >= 12 && bytes[..12].SequenceEqual(
                new byte[] { 0xAB,0x4B,0x54,0x58,0x20,0x32,0x30,0xBB,0x0D,0x0A,0x1A,0x0A })) return ".ktx2";
            if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) return ".png";
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 216) return ".jpg";
            if (bytes.Length >= 18 && IsSupportedTga(bytes[..18])) return ".tga";
            return null;
        }

        private static bool IsSupportedTga(ReadOnlySpan<byte> header)
            => header.Length >= 18 && header[1] == 0 && header[2] is 2 or 10
                && header[16] is 24 or 32
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]) > 0
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[14..16]) > 0;

        private static ModernTextureAsset DecodeTga(Stream source, string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, int maximumDimension)
        {
            Span<byte> header = stackalloc byte[18];
            source.ReadExactly(header);
            if (!IsSupportedTga(header)) throw new InvalidDataException("Unsupported TGA image.");
            int idLength = header[0];
            int type = header[2];
            int width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]);
            int height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(header[14..16]);
            int bytesPerPixel = header[16] / 8;
            bool rightOrigin = (header[17] & 0x10) != 0;
            bool topOrigin = (header[17] & 0x20) != 0;
            ValidateDimensions(width, height);
            if (idLength > 0)
            {
                byte[] id = new byte[idLength];
                source.ReadExactly(id);
            }
            (int targetWidth, int targetHeight) = FitDimensions(width, height, maximumDimension);
            byte[] rgba = new byte[checked(targetWidth * targetHeight * 4)];
            byte[] pixel = new byte[4];
            int written = 0;
            void WritePixel(byte[] bgra)
            {
                if (written >= width * height) throw new InvalidDataException("TGA contains too many pixels.");
                int rawX = written % width, rawY = written / width;
                int x = rightOrigin ? width - 1 - rawX : rawX;
                int y = topOrigin ? rawY : height - 1 - rawY;
                int targetX = Math.Min(targetWidth - 1, x * targetWidth / width);
                int targetY = Math.Min(targetHeight - 1, y * targetHeight / height);
                int target = (targetY * targetWidth + targetX) * 4;
                rgba[target] = bgra[2];
                rgba[target + 1] = bgra[1];
                rgba[target + 2] = bgra[0];
                rgba[target + 3] = bytesPerPixel == 4 ? bgra[3] : (byte)255;
                written++;
            }
            void ReadPixel()
            {
                source.ReadExactly(pixel.AsSpan(0, bytesPerPixel));
                WritePixel(pixel);
            }
            if (type == 2)
            {
                while (written < width * height) ReadPixel();
            }
            else
            {
                while (written < width * height)
                {
                    int packet = source.ReadByte();
                    if (packet < 0) throw new InvalidDataException("Truncated TGA RLE stream.");
                    int count = (packet & 0x7f) + 1;
                    if ((packet & 0x80) != 0)
                    {
                        source.ReadExactly(pixel.AsSpan(0, bytesPerPixel));
                        for (int i = 0; i < count; i++) WritePixel(pixel);
                    }
                    else
                    {
                        for (int i = 0; i < count; i++) ReadPixel();
                    }
                }
            }
            if (written != width * height) throw new InvalidDataException("Truncated TGA image.");
            if (channel == TextureAssetChannel.Normal) RenormalizeNormals(rgba);
            return new ModernTextureAsset(key, assetClass, channel, targetWidth, targetHeight, rgba);
        }

        public static ModernTextureAsset FromRgba(string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, int width, int height, byte[] pixels)
        {
            ValidateDimensions(width, height);
            if (pixels == null || pixels.LongLength != (long)width * height * 4)
                throw new InvalidDataException("Texture RGBA byte count does not match its dimensions.");
            return new ModernTextureAsset(key, assetClass, channel, width, height, pixels);
        }

        internal static ModernTextureAsset FromRgbaPointer(string key,
            TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, IntPtr pixels, int maximumDimension)
        {
            ValidateDimensions(width, height);
            if (pixels == IntPtr.Zero)
                throw new InvalidDataException("Texture decoder returned no pixels.");
            maximumDimension = Math.Clamp(maximumDimension, 1, MaximumDimension);
            (int targetWidth, int targetHeight) = FitDimensions(width, height, maximumDimension);
            if (targetWidth != width || targetHeight != height)
            {
                byte[] fitted = Resample(pixels, width, height, targetWidth, targetHeight,
                    channel == TextureAssetChannel.Normal);
                return new ModernTextureAsset(key, assetClass, channel,
                    targetWidth, targetHeight, fitted);
            }

            byte[] rgba = new byte[checked(width * height * 4)];
            Marshal.Copy(pixels, rgba, 0, rgba.Length);
            return new ModernTextureAsset(key, assetClass, channel, width, height, rgba);
        }

        public ModernTextureAsset Fit(int maximumDimension)
        {
            maximumDimension = Math.Clamp(maximumDimension, 1, MaximumDimension);
            if (Width <= maximumDimension && Height <= maximumDimension) return this;
            (int width, int height) = FitDimensions(Width, Height, maximumDimension);
            byte[] pixels = Resample(Pixels, Width, Height, width, height, Channel == TextureAssetChannel.Normal);
            return new ModernTextureAsset(Key, AssetClass, Channel, width, height, pixels);
        }

        private static (int Width, int Height) FitDimensions(int width, int height, int maximumDimension)
        {
            maximumDimension = Math.Clamp(maximumDimension, 1, MaximumDimension);
            if (width <= maximumDimension && height <= maximumDimension) return (width, height);
            double scale = Math.Min((double)maximumDimension / width, (double)maximumDimension / height);
            return (Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
        }

        private static void RenormalizeNormals(byte[] pixels)
        {
            for (int target = 0; target < pixels.Length; target += 4)
            {
                float nx = pixels[target] / 127.5f - 1f;
                float ny = pixels[target + 1] / 127.5f - 1f;
                float nz = pixels[target + 2] / 127.5f - 1f;
                float length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (length > 0.0001f) { nx /= length; ny /= length; nz /= length; }
                pixels[target] = (byte)Math.Clamp((int)MathF.Round((nx * 0.5f + 0.5f) * 255), 0, 255);
                pixels[target + 1] = (byte)Math.Clamp((int)MathF.Round((ny * 0.5f + 0.5f) * 255), 0, 255);
                pixels[target + 2] = (byte)Math.Clamp((int)MathF.Round((nz * 0.5f + 0.5f) * 255), 0, 255);
            }
        }

        public override long EstimateGpuBytes(bool mipmaps)
        {
            long baseBytes = checked((long)Width * Height * 4);
            return mipmaps ? baseBytes + baseBytes / 3 : baseBytes;
        }

        private static void ValidateDimensions(int width, int height)
        {
            long pixels = (long)width * height;
            if (width <= 0 || height <= 0 || width > MaximumDimension || height > MaximumDimension
                || pixels <= 0 || pixels > MaximumPixels || pixels * 4 > MaximumDecodedBytes)
                throw new InvalidDataException("Texture dimensions exceed the 8192x8192 modern-asset limit.");
        }

        private static unsafe byte[] Resample(byte[] source, int sourceWidth, int sourceHeight,
            int targetWidth, int targetHeight, bool normalMap)
        {
            fixed (byte* pixels = source)
                return Resample(pixels, sourceWidth, sourceHeight, targetWidth, targetHeight, normalMap);
        }

        private static unsafe byte[] Resample(IntPtr source, int sourceWidth, int sourceHeight,
            int targetWidth, int targetHeight, bool normalMap)
        {
            if (source == IntPtr.Zero) throw new InvalidDataException("Texture decoder returned no pixels.");
            return Resample((byte*)source, sourceWidth, sourceHeight, targetWidth, targetHeight, normalMap);
        }

        private static unsafe byte[] Resample(byte* source, int sourceWidth, int sourceHeight,
            int targetWidth, int targetHeight, bool normalMap)
        {
            var result = new byte[checked(targetWidth * targetHeight * 4)];
            for (int y = 0; y < targetHeight; y++)
            {
                float sy = ((y + 0.5f) * sourceHeight / targetHeight) - 0.5f;
                int y0 = Math.Clamp((int)MathF.Floor(sy), 0, sourceHeight - 1);
                int y1 = Math.Min(sourceHeight - 1, y0 + 1);
                float fy = Math.Clamp(sy - y0, 0, 1);
                for (int x = 0; x < targetWidth; x++)
                {
                    float sx = ((x + 0.5f) * sourceWidth / targetWidth) - 0.5f;
                    int x0 = Math.Clamp((int)MathF.Floor(sx), 0, sourceWidth - 1);
                    int x1 = Math.Min(sourceWidth - 1, x0 + 1);
                    float fx = Math.Clamp(sx - x0, 0, 1);
                    int sourceA = (y0 * sourceWidth + x0) * 4;
                    int sourceB = (y0 * sourceWidth + x1) * 4;
                    int sourceC = (y1 * sourceWidth + x0) * 4;
                    int sourceD = (y1 * sourceWidth + x1) * 4;
                    int target = (y * targetWidth + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        float top = source[sourceA + channel]
                            + (source[sourceB + channel] - source[sourceA + channel]) * fx;
                        float bottom = source[sourceC + channel]
                            + (source[sourceD + channel] - source[sourceC + channel]) * fx;
                        result[target + channel] = (byte)Math.Clamp(
                            (int)MathF.Round(top + (bottom - top) * fy), 0, 255);
                    }
                    if (normalMap)
                    {
                        float nx = result[target] / 127.5f - 1f;
                        float ny = result[target + 1] / 127.5f - 1f;
                        float nz = result[target + 2] / 127.5f - 1f;
                        float length = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (length > 0.0001f) { nx /= length; ny /= length; nz /= length; }
                        result[target] = (byte)Math.Clamp((int)MathF.Round((nx * 0.5f + 0.5f) * 255), 0, 255);
                        result[target + 1] = (byte)Math.Clamp((int)MathF.Round((ny * 0.5f + 0.5f) * 255), 0, 255);
                        result[target + 2] = (byte)Math.Clamp((int)MathF.Round((nz * 0.5f + 0.5f) * 255), 0, 255);
                    }
                }
            }
            return result;
        }
    }
}
