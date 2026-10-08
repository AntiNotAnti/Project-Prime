#if MPHREAD_RMLUI_ANDROID
using System;
using System.IO;
using System.Threading;
using Android.Graphics;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

namespace MphRead.Droid;

internal static class AndroidRmlUiImages
{
    internal static TheatrePreviewPixels Decode(string path, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        if (bounds.OutWidth is <= 0 or > 8192 || bounds.OutHeight is <= 0 or > 8192
            || (long)bounds.OutWidth * bounds.OutHeight > 16 * 1024 * 1024)
            throw new InvalidDataException("Replay preview has invalid dimensions.");
        int sample = 1;
        while (bounds.OutWidth / sample > 1024 || bounds.OutHeight / sample > 1024) sample *= 2;
        using var options = new BitmapFactory.Options { InPreferredConfig = Bitmap.Config.Argb8888, InSampleSize = sample };
        using var bitmap = BitmapFactory.DecodeFile(path, options)
            ?? throw new InvalidDataException("Android could not decode this replay preview.");
        int count = checked(bitmap.Width * bitmap.Height);
        var colors = new int[count]; bitmap.GetPixels(colors, 0, bitmap.Width, 0, 0, bitmap.Width, bitmap.Height);
        var rgb = new byte[checked(count * 3)];
        for (int i = 0; i < count; i++)
        {
            if ((i & 4095) == 0) cancellation.ThrowIfCancellationRequested();
            rgb[i * 3] = (byte)(colors[i] >> 16); rgb[i * 3 + 1] = (byte)(colors[i] >> 8); rgb[i * 3 + 2] = (byte)colors[i];
        }
        return new(bitmap.Width, bitmap.Height, rgb);
    }
}
#endif
