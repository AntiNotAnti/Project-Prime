#if MPHREAD_RMLUI_POC
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
#if !ANDROID
using ReFuel.Stb;
#endif

namespace MphRead.Mods.Launcher.RmlUi.Pages.Theatre;

public readonly record struct TheatrePreviewPixels(int Width, int Height, byte[] Rgb);

/// <summary>Converts authoritative replay previews to the native renderer's local TGA format.</summary>
public sealed class TheatreImageCache
{
    private readonly string _directory;
    private readonly int _maximumDimension;
    private readonly Func<string, CancellationToken, TheatrePreviewPixels>? _decode;
    public TheatreImageCache(string? directory = null, Func<string, CancellationToken, TheatrePreviewPixels>? decode = null, int maximumDimension = 512)
        => (_directory, _decode, _maximumDimension) = (directory ?? Path.Combine(LauncherPrefs.Directory, "rmlui-thumbnail-cache"), decode, Math.Clamp(maximumDimension, 64, 4096));

    public string Load(string source, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string path = Path.GetFullPath(source);
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Replay preview was removed.", path);
        if (info.Length > 32 * 1024 * 1024) throw new InvalidDataException("Replay preview is too large.");
        string signature = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}|tga-v1|{_maximumDimension}";
        string name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(signature)));
        string output = Path.Combine(_directory, name + ".tga");
        if (File.Exists(output)) return output;
        TheatrePreviewPixels image = _decode != null ? _decode(path, cancellation) : Decode(path);
        if (image.Width is <= 0 or > 8192 || image.Height is <= 0 or > 8192
            || (long)image.Width * image.Height > 16 * 1024 * 1024 || image.Rgb == null || image.Rgb.Length != (long)image.Width * image.Height * 3)
            throw new InvalidDataException("Replay preview has invalid dimensions.");
        byte[] pixels = image.Rgb;
        float scale = Math.Min(1, (float)_maximumDimension / Math.Max(image.Width, image.Height));
        int width = Math.Max(1, (int)(image.Width * scale)), height = Math.Max(1, (int)(image.Height * scale));
        byte[] tga = new byte[18 + width * height * 3];
        tga[2] = 2; tga[12] = (byte)width; tga[13] = (byte)(width >> 8);
        tga[14] = (byte)height; tga[15] = (byte)(height >> 8); tga[16] = 24; tga[17] = 0x20;
        for (int y = 0; y < height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++)
            {
                int from = ((y * image.Height / height) * image.Width + x * image.Width / width) * 3;
                int to = 18 + (y * width + x) * 3;
                tga[to] = pixels[from + 2]; tga[to + 1] = pixels[from + 1]; tga[to + 2] = pixels[from];
            }
        }
        cancellation.ThrowIfCancellationRequested();
        Directory.CreateDirectory(_directory);
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, tga); cancellation.ThrowIfCancellationRequested(); File.Move(temporary, output, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return output;
    }
    private static TheatrePreviewPixels Decode(string path)
    {
#if ANDROID
        throw new InvalidOperationException("An Android replay preview decoder is unavailable.");
#else
        using FileStream file = File.OpenRead(path);
        using StbImage image = StbImage.Load(file, StbiImageFormat.Rgb);
        if (image.Width is <= 0 or > 8192 || image.Height is <= 0 or > 8192
            || (long)image.Width * image.Height > 16 * 1024 * 1024 || image.ImagePointer == IntPtr.Zero)
            throw new InvalidDataException("Replay preview has invalid dimensions.");
        byte[] pixels = new byte[checked(image.Width * image.Height * 3)];
        // Copy the requested RGB surface; AsSpan reports source channels for some STB formats.
        Marshal.Copy(image.ImagePointer, pixels, 0, pixels.Length);
        return new(image.Width, image.Height, pixels);
#endif
    }
}
#endif
