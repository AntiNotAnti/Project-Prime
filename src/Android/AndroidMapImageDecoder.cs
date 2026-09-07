using System;
using System.IO;
using Android.Graphics;
using MphRead.Mods.MapGen;

namespace MphRead.Droid
{
    internal static class AndroidMapImageDecoder
    {
        public static RgbImage Decode(ReadOnlyMemory<byte> encoded)
        {
            if (Imaging.TgaImageDecoder.IsTruecolor(encoded.Span))
                return Imaging.TgaImageDecoder.Decode(encoded);
            using var options = new BitmapFactory.Options
            {
                InScaled = false,
                InPremultiplied = false,
                InPreferredConfig = Bitmap.Config.Argb8888
            };
            using Bitmap? bitmap = BitmapFactory.DecodeByteArray(encoded.ToArray(), 0, encoded.Length, options);
            if (bitmap == null)
                throw new InvalidDataException("The map texture is not a supported image.");
            int count = checked(bitmap.Width * bitmap.Height);
            int[] colors = new int[count];
            bitmap.GetPixels(colors, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
            byte[] rgb = new byte[checked(count * 3)];
            for (int i = 0; i < count; i++)
            {
                rgb[i * 3] = (byte)(colors[i] >> 16);
                rgb[i * 3 + 1] = (byte)(colors[i] >> 8);
                rgb[i * 3 + 2] = (byte)colors[i];
            }
            return new RgbImage(bitmap.Width, bitmap.Height, rgb);
        }
    }
}
