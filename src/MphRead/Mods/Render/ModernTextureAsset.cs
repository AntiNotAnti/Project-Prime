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
    internal sealed class ModernTextureAsset
    {
        public const int MaximumDimension = 8192;
        public const long MaximumPixels = 64L * 1024 * 1024;
        public const long MaximumDecodedBytes = MaximumPixels * 4;

        public string Key { get; }
        public TextureAssetClass AssetClass { get; }
        public TextureAssetChannel Channel { get; }
        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        private ModernTextureAsset(string key, TextureAssetClass assetClass, TextureAssetChannel channel,
            int width, int height, byte[] pixels)
        {
            Key = key;
            AssetClass = assetClass;
            Channel = channel;
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public static ModernTextureAsset Decode(Stream source, string key, TextureAssetClass assetClass,
            TextureAssetChannel channel)
        {
            if (source == null || !source.CanRead) throw new InvalidDataException("Texture source is unreadable.");
#if ANDROID
            using var options = new Android.Graphics.BitmapFactory.Options { InScaled = false, InPremultiplied = false };
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
            return new ModernTextureAsset(key, assetClass, channel, image.Width, image.Height, rgba);
#else
            StbImage image;
            try { image = StbImage.Load(source, StbiImageFormat.Rgba); }
            catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
            { throw new InvalidDataException("Texture image could not be decoded.", ex); }
            using (image)
            {
            ValidateDimensions(image.Width, image.Height);
            if (image.ImagePointer == IntPtr.Zero) throw new InvalidDataException("Texture decoder returned no pixels.");
            byte[] rgba = new byte[checked(image.Width * image.Height * 4)];
            Marshal.Copy(image.ImagePointer, rgba, 0, rgba.Length);
            return new ModernTextureAsset(key, assetClass, channel, image.Width, image.Height, rgba);
            }
#endif
        }

        internal static (int Width, int Height) ProbeDimensions(ReadOnlySpan<byte> bytes)
        {
            int width = 0, height = 0;
            if (bytes.Length >= 24 && bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }))
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
            if (bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 })) return ".png";
            if (bytes.Length >= 2 && bytes[0] == 255 && bytes[1] == 216) return ".jpg";
            return null;
        }

        public static ModernTextureAsset FromRgba(string key, TextureAssetClass assetClass,
            TextureAssetChannel channel, int width, int height, byte[] pixels)
        {
            ValidateDimensions(width, height);
            if (pixels == null || pixels.LongLength != (long)width * height * 4)
                throw new InvalidDataException("Texture RGBA byte count does not match its dimensions.");
            return new ModernTextureAsset(key, assetClass, channel, width, height, pixels);
        }

        public ModernTextureAsset Fit(int maximumDimension)
        {
            maximumDimension = Math.Clamp(maximumDimension, 1, MaximumDimension);
            if (Width <= maximumDimension && Height <= maximumDimension) return this;
            double scale = Math.Min((double)maximumDimension / Width, (double)maximumDimension / Height);
            int width = Math.Max(1, (int)Math.Round(Width * scale));
            int height = Math.Max(1, (int)Math.Round(Height * scale));
            byte[] pixels = Resample(Pixels, Width, Height, width, height, Channel == TextureAssetChannel.Normal);
            return new ModernTextureAsset(Key, AssetClass, Channel, width, height, pixels);
        }

        public long EstimateGpuBytes(bool mipmaps)
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

        private static byte[] Resample(byte[] source, int sourceWidth, int sourceHeight,
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
                    int a = (y0 * sourceWidth + x0) * 4;
                    int b = (y0 * sourceWidth + x1) * 4;
                    int c = (y1 * sourceWidth + x0) * 4;
                    int d = (y1 * sourceWidth + x1) * 4;
                    int target = (y * targetWidth + x) * 4;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        float top = source[a + channel] + (source[b + channel] - source[a + channel]) * fx;
                        float bottom = source[c + channel] + (source[d + channel] - source[c + channel]) * fx;
                        result[target + channel] = (byte)Math.Clamp((int)MathF.Round(top + (bottom - top) * fy), 0, 255);
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
