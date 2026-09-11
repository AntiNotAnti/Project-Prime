using System;
using System.IO;
using ReFuel.Stb;

namespace MphRead;

public static class RenderCapturePng
{
    public static void Write(RenderCaptureResult result, string path)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        byte[] pixels = RenderCapturePixels.Normalize(result.Bytes.Span, result.Width,
            result.Height, result.PixelFormat, result.RowOrientation,
            CapturePixelFormat.Rgb8, CaptureRowOrientation.TopDown,
            checked(result.Width * RenderCaptureResult.BytesPerPixel(result.PixelFormat)));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = Path.GetFullPath(path) + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                StbImage.WritePng<byte>(pixels, result.Width, result.Height,
                    StbiImageFormat.Rgb, stream);
            File.Move(temporary, Path.GetFullPath(path), overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
