#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Render
{
    /// <summary>
    /// End-to-end modern-window smoke test: GLFW NoAPI window, native WebGPU
    /// surface, texture upload, compatibility Begin/End geometry, readback and
    /// present. It intentionally needs no game files.
    /// </summary>
    internal static class ModernGraphicsWindowCheck
    {
        internal static int Run(string? renderer)
        {
            try
            {
                GraphicsBackendPolicy.Configure(renderer ?? "auto");
                GraphicsBackend backend = GraphicsBackendPolicy.Resolved;
                if (!GraphicsBackendPolicy.IsModern(backend))
                {
                    Console.Error.WriteLine("[renderwindowcheck] choose a modern renderer");
                    return 2;
                }

                NativeWindowSettings settings = DesktopGlContext.Settings(background: true);
                settings.ClientSize = new Vector2i(96, 64);
                settings.StartVisible = false;
                settings.Title = "Project Prime modern renderer smoke";
                using var window = new NativeWindow(settings);
                ModernGraphicsCompat.Initialize(window, backend);
                try
                {
                    ModernGraphicsCompat.Resize(96, 64);
                    GraphicsApi.Viewport(0, 0, 96, 64);
                    GraphicsApi.ClearColor(0f, 0f, 0f, 1f);
                    GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);

                    int texture = GraphicsApi.GenTexture();
                    GraphicsApi.ActiveTexture(TextureUnit.Texture0);
                    GraphicsApi.BindTexture(TextureTarget.Texture2D, texture);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,
                        TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                    GraphicsApi.TexParameter(TextureTarget.Texture2D,
                        TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                    byte[] red =
                    {
                        255, 0, 0, 255, 255, 0, 0, 255,
                        255, 0, 0, 255, 255, 0, 0, 255
                    };
                    GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                        PixelInternalFormat.Rgba, 2, 2, 0,
                        PixelFormat.Rgba, PixelType.UnsignedByte, red);
                    GraphicsApi.Enable(EnableCap.Texture2D);
                    GraphicsApi.Disable(EnableCap.DepthTest);
                    GraphicsApi.Disable(EnableCap.CullFace);
                    GraphicsApi.Disable(EnableCap.Blend);
                    GraphicsApi.Color4(1f, 1f, 1f, 1f);

                    GraphicsApi.Begin(PrimitiveType.TriangleStrip);
                    GraphicsApi.TexCoord2(1, 0); GraphicsApi.Vertex3( 1,  1, 0);
                    GraphicsApi.TexCoord2(0, 0); GraphicsApi.Vertex3(-1,  1, 0);
                    GraphicsApi.TexCoord2(1, 1); GraphicsApi.Vertex3( 1, -1, 0);
                    GraphicsApi.TexCoord2(0, 1); GraphicsApi.Vertex3(-1, -1, 0);
                    GraphicsApi.End();

                    byte[] pixel = new byte[4];
                    GraphicsApi.ReadPixels(48, 32, 1, 1,
                        PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                    ModernGraphicsCompat.Present();
                    GraphicsApi.DeleteTexture(texture);

                    bool redPixel = pixel[0] >= 220 && pixel[1] <= 24
                        && pixel[2] <= 24 && pixel[3] >= 220;
                    if (!redPixel)
                    {
                        Console.Error.WriteLine(
                            $"[renderwindowcheck] FAIL rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})");
                        return 1;
                    }

                    Console.WriteLine(
                        $"[renderwindowcheck] PASS backend={GraphicsBackendPolicy.DisplayName(backend)} "
                        + $"rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})");
                    return 0;
                }
                finally
                {
                    ModernGraphicsCompat.Shutdown();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[renderwindowcheck] FAIL {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }
    }
}
#endif
