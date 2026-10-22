using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static class MapPreviewAcceptance
{
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "prime-rmlui-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            HeaderAndEncoding();
            string source = Path.Combine(temporary, "synthetic preview.png");
            byte[] rgba = Pixels();
            byte[] png = Png(rgba, 8, 8);
            File.WriteAllBytes(source, png);
            string directory = Path.Combine(temporary, "content cache");
            using var cache = new RmlUiMapPreviewCache(directory);
            string image = Ready(cache, source);
            byte[] expected = RmlUiMapPreviewCache.EncodeTga(rgba, 8, 8);
            Check(File.ReadAllBytes(image).SequenceEqual(expected),
                "actual ReFuel PNG decode preserves top-left RGBA and emits straight-alpha BGRA exactly once");
            // The real map renderer writes RGB, unlike the original RGBA-only fixture.
            byte[] rgb = new byte[8 * 8 * 3];
            byte[] opaque = new byte[8 * 8 * 4];
            for (int pixel = 0; pixel < 64; pixel++)
            {
                rgba.AsSpan(pixel * 4, 3).CopyTo(rgb.AsSpan(pixel * 3));
                rgba.AsSpan(pixel * 4, 3).CopyTo(opaque.AsSpan(pixel * 4));
                opaque[pixel * 4 + 3] = 255;
            }
            string rgbSource = Path.Combine(temporary, "renderer-rgb.png");
            File.WriteAllBytes(rgbSource, Png(rgb, 8, 8, channels: 3));
            using (var rgbCache = new RmlUiMapPreviewCache(Path.Combine(temporary, "rgb-cache")))
                Check(File.ReadAllBytes(Ready(rgbCache, rgbSource)).SequenceEqual(
                    RmlUiMapPreviewCache.EncodeTga(opaque, 8, 8)),
                    "renderer-style RGB PNG expands all pixels to opaque RGBA with exact channels and top-left orientation");
            Check(Path.GetFileName(image) == Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant() + ".tga",
                "decoded content selects exact SHA256 cache name");
            DateTime cachedStamp = File.GetLastWriteTimeUtc(image);
            Check(cache.Read(source) == image, "unchanged source reuses published cache path");
            string copy = Path.Combine(temporary, "same-content.png");
            File.WriteAllBytes(copy, png);
            using (var other = new RmlUiMapPreviewCache(directory))
                Check(Ready(other, copy) == image && File.GetLastWriteTimeUtc(image) == cachedStamp
                    && Directory.GetFiles(directory, "*.tga").Length == 1,
                    "another source/cache instance reuses content without rewriting disk texture");

            string blank = Path.Combine(temporary, "transparent.png");
            File.WriteAllBytes(blank, Png(new byte[8 * 8 * 4], 8, 8));
            string transparent = Ready(cache, blank);
            NativePixels(host, width, height, transparent, image, rgba);

            // A worker for the first request can finish after the next selection.
            // Every published path must still belong to the current selection.
            using (var switched = new RmlUiMapPreviewCache(Path.Combine(temporary, "switch-cache")))
            {
                switched.Read(source);
                string initial = switched.Read(blank);
                string current = Ready(switched, blank);
                Check((initial.Length == 0 || initial == current)
                    && File.ReadAllBytes(current).SequenceEqual(RmlUiMapPreviewCache.EncodeTga(new byte[256], 8, 8)),
                    "rapid map switch cannot publish a stale decoder result");
                Check(switched.Read("") == "", "leaving map selection immediately retires its visible path");
            }

            Ready(cache, source);
            byte[] replacement = Pixels();
            replacement[0] = 25;
            File.WriteAllBytes(source, Png(replacement, 8, 8));
            File.SetLastWriteTimeUtc(source, DateTime.UtcNow.AddSeconds(5));
            string updated = Wait(() => cache.Read(source), path => path.Length != 0 && path != image);
            Check(File.ReadAllBytes(updated).SequenceEqual(RmlUiMapPreviewCache.EncodeTga(replacement, 8, 8)),
                "source replacement retires old pixels and publishes only new decoded content");
            File.Delete(source);
            Check(Wait(() => cache.Read(source), path => path.Length == 0) == "",
                "removed source retires existing preview within bounded refresh poll");

            string oversized = Path.Combine(temporary, "oversized.png");
            using (var sparse = new FileStream(oversized, FileMode.CreateNew, FileAccess.Write))
                sparse.SetLength(16L * 1024 * 1024 + 1);
            Check(cache.Read(oversized) == "", "oversized source is refused before decode");
            string invalid = Path.Combine(temporary, "truncated-png.png");
            File.WriteAllBytes(invalid, png[..33]);
            RejectsForBoundedInterval(cache, invalid,
                "header-only PNG decode failure remains unavailable without throwing into UI");
            string tgaSource = Path.Combine(temporary, "not-a-png.tga");
            File.WriteAllBytes(tgaSource, expected);
            RejectsForBoundedInterval(cache, tgaSource, "non-PNG source never becomes a native preview");
            Symlinks(temporary, copy, image, expected);
            string poisonedDirectory = Path.Combine(temporary, "poisoned-cache");
            Directory.CreateDirectory(poisonedDirectory);
            string poisoned = Path.Combine(poisonedDirectory, Path.GetFileName(image));
            byte[] wrongBytes = new byte[expected.Length];
            File.WriteAllBytes(poisoned, wrongBytes);
            using (var poisonCache = new RmlUiMapPreviewCache(poisonedDirectory))
                RejectsForBoundedInterval(poisonCache, copy,
                    "existing digest-named file with wrong content is refused");
            Check(File.ReadAllBytes(poisoned).SequenceEqual(wrongBytes),
                "cache collision rejection never overwrites existing bytes");

            var retired = new RmlUiMapPreviewCache(Path.Combine(temporary, "retired-cache"));
            retired.Read(copy);
            retired.Dispose(); retired.Dispose();
            Thread.Sleep(50);
            Check(retired.Read(copy) == "" && retired.Read(blank) == "",
                "disposed cache cannot publish pending work or admit a new map");
            Check(!Directory.EnumerateFiles(temporary, ".preview-*.tmp", SearchOption.AllDirectories).Any(),
                "successful and rejected conversions leave no staging files");
        }
        finally
        {
            // Disposal cancels an optional decoder without synchronously joining
            // a compositor thread. Retry only this fixture's unique temp tree.
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Delete(temporary, recursive: true); break; }
                catch (IOException) when (attempt < 20) { Thread.Sleep(25); }
            }
        }
    }

    private static void HeaderAndEncoding()
    {
        byte[] valid = Png(Pixels(), 8, 8);
        RmlUiMapPreviewCache.ReadDimensions(valid, out int width, out int height);
        Check(width == 8 && height == 8, "bounded PNG header exposes exact dimensions");
        void Invalid(byte[] bytes, string label)
        {
            bool rejected = false;
            try { RmlUiMapPreviewCache.ReadDimensions(bytes, out _, out _); }
            catch (InvalidDataException) { rejected = true; }
            Check(rejected, label);
        }
        Invalid(valid[..32], "truncated IHDR is rejected");
        byte[] changed = valid.ToArray(); changed[0] = 0;
        Invalid(changed, "non-PNG signature is rejected");
        changed = valid.ToArray(); changed[11] = 12;
        Invalid(changed, "non-13-byte IHDR is rejected");
        changed = valid.ToArray(); changed[12] = (byte)'X';
        Invalid(changed, "non-IHDR first chunk is rejected");
        foreach ((int w, int h) in new[] { (0, 8), (-1, 8), (8, 0), (8, -1), (4097, 1), (1, 4097), (4096, 4096) })
        {
            changed = valid.ToArray();
            BinaryPrimitives.WriteInt32BigEndian(changed.AsSpan(16), w);
            BinaryPrimitives.WriteInt32BigEndian(changed.AsSpan(20), h);
            Invalid(changed, $"PNG dimensions {w}x{h} respect texture and allocation bounds");
        }
        Invalid(new byte[16 * 1024 * 1024 + 1], "PNG header reader enforces source byte ceiling");
        byte[] encoded = RmlUiMapPreviewCache.EncodeTga(new byte[] { 200, 100, 50, 128, 1, 2, 3, 0 }, 2, 1);
        Check(encoded.Length == 26 && encoded[2] == 2 && encoded[12] == 2 && encoded[14] == 1
            && encoded[16] == 32 && encoded[17] == 0x28
            && encoded.AsSpan(18).SequenceEqual(new byte[] { 50, 100, 200, 128, 3, 2, 1, 0 }),
            "TGA header specifies top-left eight-bit alpha and swizzle preserves straight RGB even at zero alpha");
        bool shortRejected = false;
        try { RmlUiMapPreviewCache.EncodeTga(new byte[3], 1, 1); }
        catch (InvalidDataException) { shortRejected = true; }
        Check(shortRejected, "TGA encoder rejects truncated pixel buffer");
    }

    private static void Symlinks(string temporary, string source, string image, byte[] expected)
    {
        try
        {
            string linkedSource = Path.Combine(temporary, "linked-source.png");
            File.CreateSymbolicLink(linkedSource, source);
            using var sourceCache = new RmlUiMapPreviewCache(Path.Combine(temporary, "source-link-cache"));
            RejectsForBoundedInterval(sourceCache, linkedSource, "symlink source is not decoded or published");
            string directory = Path.Combine(temporary, "destination-link-cache");
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, Path.GetFileName(image));
            File.CreateSymbolicLink(destination, image);
            using var outputCache = new RmlUiMapPreviewCache(directory);
            RejectsForBoundedInterval(outputCache, source, "symlink cache destination is refused even when target bytes are correct");
            Check(new FileInfo(destination).LinkTarget != null && File.ReadAllBytes(image).SequenceEqual(expected),
                "symlink rejection preserves the existing linked file and target bytes");
        }
        catch (Exception error) when (error is UnauthorizedAccessException or PlatformNotSupportedException)
        { Console.WriteLine("RML MAP PREVIEW SKIP symlink fixture unavailable on this host"); }
    }

    private static void NativePixels(RmlUiHost host, int width, int height,
        string transparent, string image, byte[] sourcePixels)
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "rmlui-map-preview-cpp05"));
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "README.txt"),
            "Actual native authored Lobby image binding and OpenGL framebuffer, using only synthetic PNG/TGA in a unique temporary directory. No game assets, account, map renderer, network or real session. Pixel checks establish orientation, channel order and one alpha premultiplication.\n");
        using var layout = new RmlUiLauncherPages(host);
        var document = layout.ShowBaseline(RmlUiMenuPage.Lobby);
        layout.SetText("lobby_name", "SYNTHETIC MAP PREVIEW");
        layout.SetText("lobby_map", "SYNTHETIC PNG");
        layout.SetText("lobby_map_preview_status", "SYNTHETIC LOCAL IMAGE // NO GAME DATA");
        layout.SetBool("lobby_map_image_ready", true);
        layout.SetText("lobby_map_image", transparent);
        Frame();
        Check(host.FocusDocument(document, "lobby_map_preview"), "authored map card can receive native focus and scroll");
        var settle = Stopwatch.StartNew();
        while (settle.ElapsedMilliseconds < 300) { Frame(); Thread.Sleep(5); }
        Frame();
        Check(host.TryGetElementBounds(document, "lobby_map_image", out float x, out float y, out float w, out float h)
            && w >= 8 && h >= 8, "actual authored Lobby image has drawable bounds");
        byte[] background = FinalCompositeCapture.Read(width, height);
        layout.SetText("lobby_map_image", image);
        Frame();
        byte[] painted = FinalCompositeCapture.Read(width, height);
        Console.WriteLine($"RML MAP PREVIEW BOUNDS framebuffer={width}x{height} image=({x},{y},{w},{h})");
        string capture = Path.Combine(output, $"synthetic-preview-{width}x{height}.ppm");
        using (var stream = File.Create(capture))
        {
            stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC PREVIEW - ACTUAL NATIVE LOBBY GL FRAMEBUFFER\n{width} {height}\n255\n"));
            for (int row = height - 1; row >= 0; row--) stream.Write(painted, row * width * 3, width * 3);
        }
        FinalCompositeCapture.WriteEvidence(capture, width, height,
            "Actual authored native Lobby renders a synthetic PNG decoded by production ReFuel cache to top-left straight-alpha TGA; no world or account.");
        foreach ((float fx, float fy, int sx, int sy) in new[] {
            (.25f,.25f,1,1),(.75f,.25f,6,1),(.25f,.75f,1,6),(.75f,.75f,6,6) })
        {
            int px = (int)(x + w * fx), py = (int)(y + h * fy);
            Check(px >= 0 && px < width && py >= 0 && py < height, "native preview sample lies inside framebuffer");
            int index = ((height - 1 - py) * width + px) * 3;
            int source = (sy * 8 + sx) * 4;
            int alpha = sourcePixels[source + 3];
            for (int channel = 0; channel < 3; channel++)
            {
                int expected = sourcePixels[source + channel] * alpha / 255
                    + background[index + channel] * (255 - alpha) / 255;
                Check(Math.Abs(painted[index + channel] - expected) <= 4,
                    $"actual native preview quadrant({fx},{fy}) channel{channel} has top-left orientation and exactly one alpha multiply (actual={painted[index + channel]},expected={expected})");
            }
        }
        Check(GL.GetError() == ErrorCode.NoError, "native map preview leaves no OpenGL errors");
        void Frame()
        {
            layout.Flush(); host.Update(); layout.AfterUpdate();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, width, height); GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.025f, .05f, .08f, 1);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host, width, height);
        }
    }

    private static string Ready(RmlUiMapPreviewCache cache, string source)
        => Wait(() => cache.Read(source), path => path.Length != 0);
    private static string Wait(Func<string> read, Func<string, bool> ready)
    {
        var timer = Stopwatch.StartNew();
        do { string value = read(); if (ready(value)) return value; Thread.Sleep(5); }
        while (timer.ElapsedMilliseconds < 5000);
        throw new TimeoutException("Synthetic map preview did not reach expected state.");
    }
    private static void RejectsForBoundedInterval(RmlUiMapPreviewCache cache, string source, string label)
    {
        var timer = Stopwatch.StartNew();
        do { CheckEmpty(cache.Read(source)); Thread.Sleep(5); }
        while (timer.ElapsedMilliseconds < 1100);
        Check(true, label);
        static void CheckEmpty(string value)
        { if (value.Length != 0) throw new InvalidOperationException("Rejected preview source published an image."); }
    }
    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("RML MAP PREVIEW FAIL " + label);
        Console.WriteLine("RML MAP PREVIEW PASS " + label);
    }
    private static byte[] Pixels()
    {
        byte[] pixels = new byte[8 * 8 * 4];
        byte[][] quadrants = { new byte[] { 200, 100, 50, 128 }, new byte[] { 10, 220, 30, 255 },
            new byte[] { 20, 40, 240, 255 }, new byte[] { 255, 200, 100, 0 } };
        for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++)
            quadrants[(y >= 4 ? 2 : 0) + (x >= 4 ? 1 : 0)].CopyTo(pixels, (y * 8 + x) * 4);
        return pixels;
    }
    private static byte[] Png(byte[] pixels, int width, int height, int channels = 4)
    {
        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; header[9] = channels == 3 ? (byte)2 : (byte)6;
        Chunk("IHDR", header);
        using var compressed = new MemoryStream();
        using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            for (int y = 0; y < height; y++)
            { deflate.WriteByte(0); deflate.Write(pixels, y * width * channels, width * channels); }
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return output.ToArray();
        void Chunk(string type, byte[] data)
        {
            byte[] count = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(count, data.Length); output.Write(count);
            byte[] kind = Encoding.ASCII.GetBytes(type); output.Write(kind); output.Write(data);
            uint crc = 0xffffffff;
            foreach (byte value in kind.Concat(data))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            BinaryPrimitives.WriteUInt32BigEndian(count, crc ^ 0xffffffff); output.Write(count);
        }
    }
}
