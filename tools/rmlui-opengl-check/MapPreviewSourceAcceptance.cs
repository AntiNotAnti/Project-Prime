using System.Diagnostics;
using MphRead.Mods.Launcher.RmlUi.Render;
using ReFuel.Stb;

internal static class MapPreviewSourceAcceptance
{
    internal static int Run(string source)
    {
        byte[] png = File.ReadAllBytes(source);
        RmlUiMapPreviewCache.ReadDimensions(png, out int width, out int height);
        using (var input = new MemoryStream(png, false))
        using (StbImage image = StbImage.Load(input, StbiImageFormat.Rgba))
            Console.WriteLine($"MAP SOURCE width={image.Width} height={image.Height} format={image.Format} spanBytes={image.AsSpan<byte>().Length} expectedRgba={width * height * 4} pointerValid={image.ImagePointer != IntPtr.Zero}");
        using var cache = new RmlUiMapPreviewCache(Path.Combine(Environment.GetEnvironmentVariable("PROJECT_PRIME_USER_DATA")!, "map-source-cache"));
        var timer = Stopwatch.StartNew(); string ready = "";
        do { ready = cache.Read(source); if (ready.Length == 0) Thread.Sleep(10); }
        while (ready.Length == 0 && timer.ElapsedMilliseconds < 5000);
        if (ready.Length == 0 || new FileInfo(ready).Length != 18L + width * height * 4)
            throw new InvalidOperationException("Actual map source did not produce its complete native RGBA texture.");
        Console.WriteLine("ACTUAL MAP SOURCE PASS " + ready);
        return 0;
    }
}
