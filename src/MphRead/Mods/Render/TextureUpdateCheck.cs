#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render;

// The same pixel assertions run against OpenGL and every desktop modern backend.
internal static class TextureUpdateCheck
{
    internal static int Run(string? renderer)
    {
        try
        {
            GraphicsBackendPolicy.Configure(renderer ?? "opengl");
            var settings = DesktopGlContext.Settings(background: true);
            settings.ClientSize = new Vector2i(32, 32);
            using var window = HostedLegacyGlCapabilityCheck.CreateWindow(settings);
            if (window == null)
            {
                Console.WriteLine("[textureupdatecheck] legacy OpenGL pixels UNAVAILABLE: hosted Apple Paravirtual device has no accelerated legacy CGL format; mandatory modern checks remain separate");
                return 0;
            }
            using var graphics = new DesktopGraphicsSession(window);
            DesktopGraphicsSession.Resize(window);
            Verify();
            AuthoredRgbaMipGpuCheck.Verify();
            Console.WriteLine($"[textureupdatecheck] PASS {GraphicsBackendPolicy.Resolved}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"[textureupdatecheck] FAIL {exception}");
            return 1;
        }
    }

    internal static void Verify()
    {
        if (Environment.GetEnvironmentVariable("PRIME_RENDERER_RAW_ATTACHMENT_PROBE") == "1")
            ProbeRawTextureFirstFramebufferAttach();
        VerifyLargeMutableResize();
        foreach (var format in new[] { PixelInternalFormat.Rgba8, PixelInternalFormat.Rgba16f })
        {
            int texture = GraphicsApi.GenTexture(), framebuffer = GraphicsApi.GenFramebuffer();
            try
            {
                GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, format, 4, 4, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, new byte[64]);
                byte[] fullUpdate = new byte[64];
                for (int i = 0; i < fullUpdate.Length; i += 4)
                    fullUpdate[i] = fullUpdate[i + 3] = 255;
                GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 4, 4,
                    PixelFormat.Rgba, PixelType.UnsignedByte, fullUpdate);
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
                byte[] initial = new byte[4];
                GraphicsApi.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, initial);
                if (initial[0] != 255 || initial[1] != 0 || initial[2] != 0 || initial[3] != 255)
                    throw new InvalidOperationException($"{format} full-image sub-upload mismatch.");
                GraphicsApi.Disable(EnableCap.ScissorTest);
                GraphicsApi.ColorMask(true, true, true, true);
                GraphicsApi.ClearColor(0, 0, 1, 1);
                GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
                // No readback/Finish before the update: queued draws must execute
                // before the upload. Different rows also catch vertical inversion.
                byte[] patch = { 255, 0, 0, 255, 0, 255, 0, 255 };
                GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 1, 0, 1, 2,
                    PixelFormat.Rgba, PixelType.UnsignedByte, patch);
                // A separate patch must not restore stale pixels between patches.
                GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 3, 3, 1, 1,
                    PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 255, 255, 255 });
                var pixels = new byte[64];
                GraphicsApi.ReadPixels(0, 0, 4, 4, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
                for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int at = (y * 4 + x) * 4;
                    bool red = x == 1 && y == 0, green = x == 1 && y == 1, white = x == 3 && y == 3;
                    int r = red || white ? 255 : 0, g = green || white ? 255 : 0;
                    int b = red || green ? 0 : 255;
                    if (pixels[at] != r || pixels[at + 1] != g || pixels[at + 2] != b || pixels[at + 3] != 255)
                        throw new InvalidOperationException($"{format} partial upload mismatch at {x},{y}: "
                            + $"{pixels[at]},{pixels[at + 1]},{pixels[at + 2]},{pixels[at + 3]}");
                }
            }
            finally
            {
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                GraphicsApi.DeleteFramebuffer(framebuffer);
                GraphicsApi.DeleteTexture(texture);
            }
        }
        Console.WriteLine("[textureupdatecheck] GPU pixels, patch ordering, orientation and HDR PASS");
    }

    // Diagnostic only: sampled-image and rendered-image orientation currently
    // differ in the facade. Attaching an already-uploaded asymmetric raw image
    // changes its origin metadata without migrating the existing GPU contents.
    // Report the GL contract discrepancy without conflating it with residency.
    private static void ProbeRawTextureFirstFramebufferAttach()
    {
        const int size = 4;
        int texture = GraphicsApi.GenTexture(), framebuffer = GraphicsApi.GenFramebuffer();
        int previousDraw = GraphicsApi.GetInteger(GetPName.DrawFramebufferBinding);
        int previousRead = GraphicsApi.GetInteger(GetPName.ReadFramebufferBinding);
        int previousUnit = GraphicsApi.GetInteger(GetPName.ActiveTexture);
        GraphicsApi.ActiveTexture(TextureUnit.Texture0);
        int previousTexture = GraphicsApi.GetInteger(GetPName.TextureBinding2D);
        try
        {
            var pixels = new byte[size * size * 4];
            for (int at = 0; at < pixels.Length; at += 4) pixels[at + 2] = pixels[at + 3] = 255;
            pixels[0] = 255; pixels[2] = 0; // logical first row: red
            int last = (size - 1) * size * 4;
            pixels[last + 1] = 255; pixels[last + 2] = 0; // logical last row: green
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            if (ModernGraphicsCompat.Active) ModernGraphicsCompat.EnsureBoundTextureResident();
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            byte[] bottom = new byte[4], top = new byte[4];
            GraphicsApi.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, bottom);
            GraphicsApi.ReadPixels(0, size - 1, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, top);
            bool matchesGl = bottom[0] == 255 && bottom[1] == 0 && bottom[2] == 0 && bottom[3] == 255
                && top[0] == 0 && top[1] == 255 && top[2] == 0 && top[3] == 255;
            Console.WriteLine($"[textureupdatecheck] raw first-FBO attachment origin probe "
                + $"{(matchesGl ? "PARITY_PASS" : "KNOWN_RESIDUAL")} backend={GraphicsBackendPolicy.Resolved} "
                + $"bottom={string.Join(',', bottom)} top={string.Join(',', top)} "
                + "GLexpectedBottom=255,0,0,255 GLexpectedTop=0,255,0,255; "
                + "origin migration remains outside actions 1-10");
        }
        finally
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
            GraphicsApi.DeleteFramebuffer(framebuffer);
            GraphicsApi.DeleteTexture(texture);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, previousTexture);
            GraphicsApi.ActiveTexture((TextureUnit)previousUnit);
        }
    }

    private static void VerifyLargeMutableResize()
    {
        // 16 MiB crosses the progressive replacement threshold (8 MiB), just
        // like enlarging the launcher/pause UI to a high-resolution window.
        const int size = 2048;
        int texture = GraphicsApi.GenTexture(), framebuffer = GraphicsApi.GenFramebuffer();
        try
        {
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                4, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, new byte[64]);
            if (ModernGraphicsCompat.Active) ModernGraphicsCompat.EnsureBoundTextureResident();

            var resized = new byte[size * size * 4];
            for (int at = 0; at < resized.Length; at += 4)
                resized[at + 2] = resized[at + 3] = 255;
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, resized);
            if (ModernGraphicsCompat.Active) ModernGraphicsCompat.EnsureBoundTextureResident();

            // A full new UI frame must not copy into the preceding 4x4 image.
            var next = new byte[resized.Length];
            for (int at = 0; at < next.Length; at += 4)
                next[at + 1] = next[at + 3] = 255;
            GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, size, size,
                PixelFormat.Rgba, PixelType.UnsignedByte, next);
            GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 0, 0, 255 });
            // This sampled UI texture becomes a readback attachment only below.
            // Raw image and framebuffer rows have opposite native origins, so
            // mark both ends to isolate residency/cancellation from orientation.
            // The small attached-target cases above verify row orientation.
            GraphicsApi.TexSubImage2D(TextureTarget.Texture2D, 0, 0, size - 1, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, new byte[] { 255, 0, 0, 255 });
            if (ModernGraphicsCompat.Active)
            {
                // Advance beyond the old no-mip upload's completion boundary.
                // A forgotten pending image must never replace the patched UI.
                for (int frame = 0; frame < 6; frame++) ModernGraphicsCompat.Present();
            }

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
            var sample = new byte[4];
            GraphicsApi.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            if (sample[0] != 255 || sample[1] != 0 || sample[2] != 0 || sample[3] != 255)
                throw new InvalidOperationException("Large resized texture lost its mutable patch after presentation: "
                    + $"{sample[0]},{sample[1]},{sample[2]},{sample[3]}.");
            GraphicsApi.ReadPixels(0, size - 1, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            if (sample[0] != 255 || sample[1] != 0 || sample[2] != 0 || sample[3] != 255)
                throw new InvalidOperationException("Large resized texture lost its opposite-row mutable patch after presentation: "
                    + $"{sample[0]},{sample[1]},{sample[2]},{sample[3]}.");
            GraphicsApi.ReadPixels(size - 1, size - 1, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            if (sample[0] != 0 || sample[1] != 255 || sample[2] != 0 || sample[3] != 255)
                throw new InvalidOperationException("Large resized texture did not preserve the current full-frame update: "
                    + $"{sample[0]},{sample[1]},{sample[2]},{sample[3]}.");
            Console.WriteLine("[textureupdatecheck] large mutable resize and progressive cancellation PASS");
        }
        finally
        {
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.DeleteFramebuffer(framebuffer);
            GraphicsApi.DeleteTexture(texture);
        }
    }
}
#endif
