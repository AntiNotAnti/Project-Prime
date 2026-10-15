using System;
using System.Runtime.InteropServices;
using MphRead.Mods.StudioRendering;
using OpenTK.Graphics.OpenGL;

internal static class Program
{
    private const string OpenGl = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    private static void Main()
    {
        if (!OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("This check requires macOS hardware OpenGL.");
        Check(CGLChoosePixelFormat([99, 0x3200, 73, 0], out var format, out _));
        Check(CGLCreateContext(format, IntPtr.Zero, out var core));
        Check(CGLDestroyPixelFormat(format));
        Check(CGLSetCurrentContext(core));
        try
        {
            MphRead.Mods.Render.GraphicsApi.FailNextLoad = true;
            try
            {
                using var rejected = new MacLegacyGlSurface();
                throw new Exception("Expected binding initialization failure was swallowed.");
            }
            catch (InvalidOperationException exception) when (exception.Message == "Injected binding initialization failure.")
            {
                if (CGLGetCurrentContext() != core) throw new Exception("Constructor failure did not restore the core context.");
            }
            for (int lifetime = 0; lifetime < 12; lifetime++)
            {
                CheckLifetime(core, lifetime);
                if (GL.GetError() != ErrorCode.NoError) throw new Exception("GL error after device disposal.");
                if (MphRead.Mods.Render.GraphicsApi.Loads != lifetime + 1)
                    throw new Exception("Canonical bindings were not loaded exactly once per shared device.");
                if (MphRead.Mods.Render.OpenGlGpuProfiler.Releases != lifetime + 1)
                    throw new Exception("Profiler resources were not released once per shared device.");
            }
        }
        finally
        {
            Check(CGLSetCurrentContext(IntPtr.Zero));
            Check(CGLDestroyContext(core));
        }
        Console.WriteLine("PASS: GPU transport helper, 12 lifetimes, 120 clear draw callbacks, 120 resizes, no GL errors.");
        Console.WriteLine("PASS: canonical facade hook observed once per device; profiler release in legacy context; constructor failure rolled back.");
    }

    private static void CheckLifetime(IntPtr core, int lifetime)
    {
        using var first = new MacLegacyGlSurface();
        using var second = new MacLegacyGlSurface();
        IntPtr firstDevice = IntPtr.Zero, secondDevice = IntPtr.Zero;
        first.RunInLegacy(() => firstDevice = CGLGetCurrentContext());
        second.RunInLegacy(() => secondDevice = CGLGetCurrentContext());
        if (firstDevice == IntPtr.Zero || firstDevice == core || firstDevice != secondDevice)
            throw new Exception("Concurrent surfaces did not share one compatibility device.");

        GL.GenFramebuffers(1, out int target);
        GL.GenTextures(1, out int texture);
        try
        {
            for (int resize = 0; resize < 5; resize++)
            {
                int width = 32 + resize * 13, height = 25 + resize * 7;
                GL.BindTexture(TextureTarget.Texture2D, texture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, width, height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, target);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, texture, 0);
                if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                    throw new Exception("Test presentation FBO is incomplete.");
                foreach (var surface in new[] { first, second })
                {
                    GL.Enable(EnableCap.ScissorTest);
                    GL.Enable((EnableCap)0x8DB9); // core framebuffer sRGB
                    GL.Scissor(0, 0, 1, 1);
                    GL.Viewport(3, 4, width - 5, height - 6);
                    surface.Render(width, height, target, () =>
                    {
                        GL.Disable(EnableCap.ScissorTest);
                        GL.ClearColor(.25f, .5f, .75f, 1);
                        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
                    });
                    if (CGLGetCurrentContext() != core) throw new Exception("Presentation context was not restored.");
                    if (!GL.IsEnabled(EnableCap.ScissorTest)) throw new Exception("Scissor state was not restored.");
                    if (!GL.IsEnabled((EnableCap)0x8DB9)) throw new Exception("sRGB state was not restored.");
                    if (GL.GetInteger((GetPName)0x84F6) != 0) throw new Exception("Rectangle texture binding changed.");
                    if (GL.GetInteger(GetPName.FramebufferBinding) != target) throw new Exception("Presentation FBO was not restored.");
                    int[] viewport = new int[4];
                    GL.GetInteger(GetPName.Viewport, viewport);
                    if (viewport[0] != 3 || viewport[1] != 4 || viewport[2] != width - 5 || viewport[3] != height - 6)
                        throw new Exception("Presentation viewport changed.");
                    GL.Disable(EnableCap.ScissorTest);
                    byte[] pixel = new byte[4];
                    // Test oracle only: production transport never reads pixels.
                    GL.ReadPixels(width / 2, height / 2, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                    if (pixel[0] != 64 || pixel[1] != 128 || pixel[2] != 191 || pixel[3] != 255)
                        throw new Exception($"Unexpected transferred pixel: {string.Join(',', pixel)}.");
                    if (GL.GetError() != ErrorCode.NoError) throw new Exception("GL error during transport.");
                }
            }
            try
            {
                first.Render(84, 53, target, () => throw new InjectedDrawFailure());
                throw new Exception("Expected producer failure was swallowed.");
            }
            catch (InjectedDrawFailure)
            {
                if (CGLGetCurrentContext() != core) throw new Exception("Presentation context was not restored after producer failure.");
            }
        }
        finally
        {
            GL.DeleteTextures(1, ref texture);
            GL.DeleteFramebuffers(1, ref target);
        }
        Console.WriteLine($"lifetime {lifetime + 1}: shared device, 2 surfaces, 5 sizes, pixels+state restored, draw failure restored");
    }

    private sealed class InjectedDrawFailure : Exception { }
    private static void Check(int error)
    {
        if (error != 0) throw new Exception($"CGL failed: {error}.");
    }
    [DllImport(OpenGl)] private static extern int CGLChoosePixelFormat(int[] attributes, out IntPtr format, out int count);
    [DllImport(OpenGl)] private static extern int CGLCreateContext(IntPtr format, IntPtr share, out IntPtr context);
    [DllImport(OpenGl)] private static extern int CGLSetCurrentContext(IntPtr context);
    [DllImport(OpenGl)] private static extern IntPtr CGLGetCurrentContext();
    [DllImport(OpenGl)] private static extern int CGLDestroyPixelFormat(IntPtr format);
    [DllImport(OpenGl)] private static extern int CGLDestroyContext(IntPtr context);
}
