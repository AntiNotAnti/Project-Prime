using System;
using System.IO;
using System.Reflection;
using System.Threading;
using MphRead.Mods;
using OpenTK.Graphics.OpenGL;

internal static class CaptureRegionChecks
{
    internal static void Run()
    {
        const int width = 19, height = 13;
        int previousRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int previousDraw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int textureBinding = GL.GetInteger(GetPName.TextureBinding2D);
        int alignment = GL.GetInteger(GetPName.PackAlignment);
        int rowLength = GL.GetInteger(GetPName.PackRowLength);
        int skipRows = GL.GetInteger(GetPName.PackSkipRows);
        int skipPixels = GL.GetInteger(GetPName.PackSkipPixels);
        int previousPack = GL.GetInteger(GetPName.PixelPackBufferBinding);
        int texture = GL.GenTexture(), sourceFramebuffer = GL.GenFramebuffer();
        int inheritedRead = GL.GenFramebuffer(), pack = GL.GenBuffer();
        string directory = Path.Combine(Path.GetTempPath(), "prime-native-regions-" + Guid.NewGuid().ToString("N"));
        var writer = ScreenCapture.PngWriter;
        try
        {
            var pixels = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    pixels[offset] = (byte)x; pixels[offset + 1] = (byte)y;
                    pixels[offset + 2] = 123; pixels[offset + 3] = 127;
                }
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, sourceFramebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, texture, 0);
            Check(GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete,
                "region source is complete RGBA8");
            // Real preview draws need not share the caller's read framebuffer.
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, inheritedRead);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, pack);
            GL.BufferData(BufferTarget.PixelPackBuffer, 64, IntPtr.Zero, BufferUsageHint.StreamRead);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 8);
            GL.PixelStore(PixelStoreParameter.PackRowLength, 73);
            GL.PixelStore(PixelStoreParameter.PackSkipRows, 3);
            GL.PixelStore(PixelStoreParameter.PackSkipPixels, 5);
            var assembly = typeof(ScreenCapture).Assembly;
            Type sourceType = assembly.GetType("MphRead.Mods.Render.CaptureSource", true)!;
            Type helper = assembly.GetType("MphRead.Mods.Render.CaptureReadback", true)!;
            object Source(int framebuffer) => Activator.CreateInstance(sourceType,
                new object[] { framebuffer, width, height, "cosmetic-preview", "native-encoded-rgba8", 0L })!;
            object source = Source(GL.GetInteger(GetPName.DrawFramebufferBinding));
            byte[] rgb = (byte[])helper.GetMethod("ReadRegion", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { source, 2, 4, 3, 2 })!;
            Check(rgb.Length == 18 && rgb[0] == 2 && rgb[1] == 4 && rgb[2] == 123
                && rgb[9] == 2 && rgb[10] == 5 && rgb[^3] == 4 && rgb[^2] == 5,
                "region reads draw target despite different read target; exact bottom-up RGB crop");
            Restored();
            byte[] rgba = (byte[])helper.GetMethod("ReadRgbaRegion", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { source, 2, 4, 3, 2 })!;
            Check(rgba.Length == 24 && rgba[0] == 2 && rgba[1] == 4 && rgba[3] == 127
                && rgba[^3] == 5 && rgba[^1] == 127, "RGBA region preserves calibration channel and alpha");
            Restored();

            using var ready = new ManualResetEventSlim();
            byte[]? delivered = null; int deliveredWidth = 0, deliveredHeight = 0;
            ScreenCapture.PngWriter = (data, w, h, _) =>
            { delivered = data; deliveredWidth = w; deliveredHeight = h; ready.Set(); };
            typeof(ScreenCapture).GetMethod("QueueCosmeticThumbnail", BindingFlags.Static | BindingFlags.NonPublic)!
                .Invoke(null, new object[] { source, 2, 4, 3, 2, Path.Combine(directory, "fixture.png") });
            Restored();
            Check(ready.Wait(5000), "actual cosmetic thumbnail worker delivers detached pixels");
            var slots = (SemaphoreSlim)typeof(ScreenCapture).GetField("ThumbnailSlots", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Check(SpinWait.SpinUntil(() => slots.CurrentCount == 1, 5000), "thumbnail releases bounded worker slot");
            Check(deliveredWidth == 3 && deliveredHeight == 2 && delivered != null && delivered.AsSpan().SequenceEqual(rgb),
                "actual thumbnail preserves exact RGB bytes, dimensions and orientation");

            bool refused = false;
            try
            {
                helper.GetMethod("ReadRegion", BindingFlags.Static | BindingFlags.NonPublic)!
                    .Invoke(null, new object[] { Source(inheritedRead), 2, 4, 3, 2 });
            }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { refused = true; }
            Check(refused, "incomplete region source rejected"); Restored();
            Check(GL.GetError() == ErrorCode.NoError, "region/thumbnail success and refusal leave no GL errors");
            Console.WriteLine("CAPTUREREGIONS PASS Windows native RGB/RGBA/thumbnail/state/failure; calibration math tested separately");
        }
        finally
        {
            ScreenCapture.PngWriter = writer;
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
            GL.BindBuffer(BufferTarget.PixelPackBuffer, previousPack);
            GL.PixelStore(PixelStoreParameter.PackAlignment, alignment);
            GL.PixelStore(PixelStoreParameter.PackRowLength, rowLength);
            GL.PixelStore(PixelStoreParameter.PackSkipRows, skipRows);
            GL.PixelStore(PixelStoreParameter.PackSkipPixels, skipPixels);
            GL.BindTexture(TextureTarget.Texture2D, textureBinding);
            GL.DeleteBuffer(pack); GL.DeleteFramebuffer(sourceFramebuffer);
            GL.DeleteFramebuffer(inheritedRead); GL.DeleteTexture(texture);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
        void Restored() => Check(GL.GetInteger(GetPName.ReadFramebufferBinding) == inheritedRead
            && GL.GetInteger(GetPName.DrawFramebufferBinding) == sourceFramebuffer
            && GL.GetInteger(GetPName.PixelPackBufferBinding) == pack
            && GL.GetInteger(GetPName.PackAlignment) == 8 && GL.GetInteger(GetPName.PackRowLength) == 73
            && GL.GetInteger(GetPName.PackSkipRows) == 3 && GL.GetInteger(GetPName.PackSkipPixels) == 5,
            "region restores distinct read/draw targets and hostile pack/PBO state");
    }

    private static void Check(bool success, string description)
    {
        if (!success) throw new InvalidOperationException(description);
        Console.WriteLine("PASS " + description);
    }
}
