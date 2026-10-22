using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

internal readonly record struct CaptureSource(int Framebuffer, int Width, int Height,
    string Scope, string Encoding = "native-encoded-rgba8", long Generation = 0);

/// <summary>Bounded, bottom-up RGB output from a portable RGBA8 framebuffer read.</summary>
internal static class CaptureReadback
{
    internal static byte[] Read(CaptureSource source)
    {
        int width = source.Width, height = source.Height;
        // Include both temporary RGBA and delivered RGB in the CPU budget.
        if (width <= 0 || height <= 0 || width > 8192 || height > 8192
            || (long)width * height * 7 > 64 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(source), "Readback exceeds its 64 MiB working budget.");
        int framebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int readBuffer = GL.GetInteger(GetPName.ReadBuffer);
        int alignment = GL.GetInteger(GetPName.PackAlignment);
        int rowLength = GL.GetInteger(GetPName.PackRowLength);
        int skipRows = GL.GetInteger(GetPName.PackSkipRows);
        int skipPixels = GL.GetInteger(GetPName.PackSkipPixels);
        int packBuffer = GL.GetInteger(GetPName.PixelPackBufferBinding);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, source.Framebuffer);
            GL.ReadBuffer(source.Framebuffer == 0 ? ReadBufferMode.Back : ReadBufferMode.ColorAttachment0);
            if (GL.CheckFramebufferStatus(FramebufferTarget.ReadFramebuffer) != FramebufferErrorCode.FramebufferComplete)
                throw new InvalidOperationException("Capture source framebuffer is incomplete.");
            BindPackBuffer(0);
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.PixelStore(PixelStoreParameter.PackRowLength, 0);
            GL.PixelStore(PixelStoreParameter.PackSkipRows, 0);
            GL.PixelStore(PixelStoreParameter.PackSkipPixels, 0);
            byte[] rgba = new byte[checked(width * height * 4)];
            GL.ReadPixels(0, 0, width, height, PixelFormat.Rgba, PixelType.UnsignedByte, rgba);
            if (GL.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("Framebuffer readback failed.");
            byte[] rgb = new byte[checked(width * height * 3)];
            for (int src = 0, dst = 0; src < rgba.Length; src += 4, dst += 3)
            {
                rgb[dst] = rgba[src]; rgb[dst + 1] = rgba[src + 1]; rgb[dst + 2] = rgba[src + 2];
            }
            return rgb;
        }
        finally
        {
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GL.ReadBuffer((ReadBufferMode)readBuffer);
            BindPackBuffer(packBuffer);
            GL.PixelStore(PixelStoreParameter.PackAlignment, alignment);
            GL.PixelStore(PixelStoreParameter.PackRowLength, rowLength);
            GL.PixelStore(PixelStoreParameter.PackSkipRows, skipRows);
            GL.PixelStore(PixelStoreParameter.PackSkipPixels, skipPixels);
        }
    }

    private static void BindPackBuffer(int buffer)
    {
#if ANDROID
        OpenTK.Graphics.ES30.GL.BindBuffer(OpenTK.Graphics.ES30.BufferTarget.PixelPackBuffer, buffer);
#else
        OpenTK.Graphics.OpenGL.GL.BindBuffer(BufferTarget.PixelPackBuffer, buffer);
#endif
    }
}
