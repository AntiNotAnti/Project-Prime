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
            using var window = new NativeWindow(settings);
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
}
#endif
