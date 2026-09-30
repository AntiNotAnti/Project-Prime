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
                            $"[renderwindowcheck] FAIL launcher rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})");
                        return 1;
                    }

                    byte[] worldPixel = RunWorldCompositeCheck();
                    bool greenPixel = worldPixel[0] <= 24 && worldPixel[1] >= 220
                        && worldPixel[2] <= 24 && worldPixel[3] >= 220;
                    if (!greenPixel)
                    {
                        Console.Error.WriteLine(
                            $"[renderwindowcheck] FAIL world/rtt rgba({worldPixel[0]},{worldPixel[1]},{worldPixel[2]},{worldPixel[3]})");
                        return 1;
                    }

                    Console.WriteLine(
                        $"[renderwindowcheck] PASS backend={GraphicsBackendPolicy.DisplayName(backend)} "
                        + $"launcher=rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}) "
                        + $"world=rgba({worldPixel[0]},{worldPixel[1]},{worldPixel[2]},{worldPixel[3]})");
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

        private static byte[] RunWorldCompositeCheck()
        {
            int world = Link(MphRead.Shaders.VertexShader, MphRead.Shaders.FragmentShader);
            int rtt = Link(MphRead.Shaders.RttVertexShader, MphRead.Shaders.RttFragmentShader);

            int color = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                64, 64, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);

            int depth = GraphicsApi.GenRenderbuffer();
            GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depth);
            GraphicsApi.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                RenderbufferStorage.Depth24Stencil8, 64, 64);
            GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);

            int framebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
            GraphicsApi.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, depth);
            if (GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                != FramebufferErrorCode.FramebufferComplete)
            {
                throw new InvalidOperationException("World compatibility framebuffer did not complete.");
            }

            GraphicsApi.Viewport(0, 0, 64, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.ClearStencil(0);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit
                | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit);
            GraphicsApi.UseProgram(world);
            GraphicsApi.Disable(EnableCap.Texture2D);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Enable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(true);
            GraphicsApi.DepthFunc(DepthFunction.Less);
            GraphicsApi.Disable(EnableCap.CullFace);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_light"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "show_colors"), 1);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "fog_enable"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_mode"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "texgen_mode"), 0);
            Matrix4 identity = Matrix4.Identity;
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "proj_mtx"), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "view_mtx"), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "view_inv_mtx"), false, ref identity);
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "tex_mtx"), false, ref identity);
            float[] stack = IdentityStack();
            GraphicsApi.UniformMatrix4(GraphicsApi.GetUniformLocation(world, "mtx_stack"),
                32, false, stack);

            GraphicsApi.Color4(0, 1, 0, 1);
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.Vertex3(-0.7f, -0.7f, 0);
            GraphicsApi.Vertex3( 0.7f, -0.7f, 0);
            GraphicsApi.Vertex3( 0.7f,  0.7f, 0);
            GraphicsApi.Vertex3(-0.7f,  0.7f, 0);
            GraphicsApi.End();

            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.UseProgram(rtt);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.DepthMask(false);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "alpha"), 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "use_mask"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "view_width"), 96f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(rtt, "view_height"), 64f);
            GraphicsApi.Uniform4(GraphicsApi.GetUniformLocation(rtt, "fade_color"), Vector4.Zero);

            GraphicsApi.Color4(1, 1, 1, 1);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();

            byte[] pixel = new byte[4];
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
            ModernGraphicsCompat.Present();

            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteFramebuffer(framebuffer);
            GraphicsApi.DeleteRenderbuffer(depth);
            GraphicsApi.DeleteTexture(color);
            GraphicsApi.DeleteProgram(world);
            GraphicsApi.DeleteProgram(rtt);
            return pixel;
        }

        private static int Link(string vertexSource, string fragmentSource)
        {
            int vertex = GraphicsApi.CreateShader(ShaderType.VertexShader);
            GraphicsApi.ShaderSource(vertex, vertexSource);
            GraphicsApi.CompileShader(vertex);
            GraphicsApi.GetShader(vertex, ShaderParameter.CompileStatus, out int vertexOk);
            int fragment = GraphicsApi.CreateShader(ShaderType.FragmentShader);
            GraphicsApi.ShaderSource(fragment, fragmentSource);
            GraphicsApi.CompileShader(fragment);
            GraphicsApi.GetShader(fragment, ShaderParameter.CompileStatus, out int fragmentOk);
            if (vertexOk == 0 || fragmentOk == 0)
                throw new InvalidOperationException("Compatibility shader bookkeeping did not compile.");

            int program = GraphicsApi.CreateProgram();
            GraphicsApi.AttachShader(program, vertex);
            GraphicsApi.AttachShader(program, fragment);
            GraphicsApi.LinkProgram(program);
            GraphicsApi.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            GraphicsApi.DetachShader(program, vertex);
            GraphicsApi.DetachShader(program, fragment);
            GraphicsApi.DeleteShader(vertex);
            GraphicsApi.DeleteShader(fragment);
            if (linked == 0) throw new InvalidOperationException("Compatibility program did not link.");
            return program;
        }

        private static float[] IdentityStack()
        {
            var result = new float[32 * 16];
            for (int m = 0; m < 32; m++)
            {
                int b = m * 16;
                result[b + 0] = 1;
                result[b + 5] = 1;
                result[b + 10] = 1;
                result[b + 15] = 1;
            }
            return result;
        }
    }
}
#endif
