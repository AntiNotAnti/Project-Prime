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

                    byte[] worldPixels = RunWorldCompositeCheck();
                    bool topLeftGreen = worldPixels[0] <= 30 && worldPixels[1] >= 220
                        && worldPixels[2] <= 30 && worldPixels[3] >= 220;
                    bool topRightBlend = worldPixels[4] >= 170 && worldPixels[4] <= 205
                        && worldPixels[5] >= 170 && worldPixels[5] <= 205
                        && worldPixels[6] <= 35 && worldPixels[7] >= 220;
                    bool bottomBlue = worldPixels[8] <= 30 && worldPixels[9] <= 30
                        && worldPixels[10] >= 220 && worldPixels[11] >= 220;
                    bool whiteout = worldPixels[12] >= 220 && worldPixels[13] >= 220
                        && worldPixels[14] >= 220 && worldPixels[15] >= 220;
                    bool linearBlend = worldPixels[16] >= 120 && worldPixels[16] <= 136
                        && worldPixels[17] >= 120 && worldPixels[17] <= 136
                        && worldPixels[18] <= 20 && worldPixels[19] >= 220;
                    bool blitTop = worldPixels[20] <= 30 && worldPixels[21] >= 220
                        && worldPixels[22] <= 30 && worldPixels[23] >= 220;
                    bool blitBottom = worldPixels[24] <= 30 && worldPixels[25] <= 30
                        && worldPixels[26] >= 220 && worldPixels[27] >= 220;
                    if (!topLeftGreen || !topRightBlend || !bottomBlue || !whiteout
                        || !linearBlend || !blitTop || !blitBottom)
                    {
                        Console.Error.WriteLine(
                            $"[renderwindowcheck] FAIL world/rtt "
                            + $"topLeft=rgba({worldPixels[0]},{worldPixels[1]},{worldPixels[2]},{worldPixels[3]}) "
                            + $"topRight=rgba({worldPixels[4]},{worldPixels[5]},{worldPixels[6]},{worldPixels[7]}) "
                            + $"bottom=rgba({worldPixels[8]},{worldPixels[9]},{worldPixels[10]},{worldPixels[11]}) "
                            + $"whiteout=rgba({worldPixels[12]},{worldPixels[13]},{worldPixels[14]},{worldPixels[15]}) "
                            + $"linearBlend=rgba({worldPixels[16]},{worldPixels[17]},{worldPixels[18]},{worldPixels[19]}) "
                            + $"blitTop=rgba({worldPixels[20]},{worldPixels[21]},{worldPixels[22]},{worldPixels[23]}) "
                            + $"blitBottom=rgba({worldPixels[24]},{worldPixels[25]},{worldPixels[26]},{worldPixels[27]})");
                        return 1;
                    }

                    Console.WriteLine(
                        $"[renderwindowcheck] PASS backend={GraphicsBackendPolicy.DisplayName(backend)} "
                        + $"launcher=rgba({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]}) "
                        + $"worldTop=rgba({worldPixels[0]},{worldPixels[1]},{worldPixels[2]},{worldPixels[3]}) "
                        + $"worldBlend=rgba({worldPixels[4]},{worldPixels[5]},{worldPixels[6]},{worldPixels[7]}) "
                        + $"worldBottom=rgba({worldPixels[8]},{worldPixels[9]},{worldPixels[10]},{worldPixels[11]}) "
                        + $"whiteout=rgba({worldPixels[12]},{worldPixels[13]},{worldPixels[14]},{worldPixels[15]}) "
                        + $"linearBlend=rgba({worldPixels[16]},{worldPixels[17]},{worldPixels[18]},{worldPixels[19]}) "
                        + $"blitTop=rgba({worldPixels[20]},{worldPixels[21]},{worldPixels[22]},{worldPixels[23]}) "
                        + $"blitBottom=rgba({worldPixels[24]},{worldPixels[25]},{worldPixels[26]},{worldPixels[27]})");
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
            int shift = Link(MphRead.Shaders.RttVertexShader, MphRead.Shaders.ShiftFragmentShader);

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

            int depth = GraphicsApi.GenTexture();
            GraphicsApi.BindTexture(TextureTarget.Texture2D, depth);
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0,
                PixelInternalFormat.Depth24Stencil8, 64, 64, 0,
                PixelFormat.DepthStencil, PixelType.UnsignedInt248, IntPtr.Zero);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);

            int framebuffer = GraphicsApi.GenFramebuffer();
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, color, 0);
            GraphicsApi.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                FramebufferAttachment.DepthStencilAttachment,
                TextureTarget.Texture2D, depth, 0);
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

            // Far red control surface. Everything below should beat this in
            // the depth buffer; the later yellow surface should not.
            GraphicsApi.Color4(1, 0, 0, 1);
            DrawQuad(-0.9f, -0.9f, 0.9f, 0.9f, 0.55f);

            // Top half: a real sampled world texture, not just vertex colour.
            int greenTexture = GraphicsApi.GenTexture();
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, greenTexture);
            byte[] green =
            {
                0, 255, 0, 255, 0, 255, 0, 255,
                0, 255, 0, 255, 0, 255, 0, 255
            };
            GraphicsApi.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                2, 2, 0, PixelFormat.Rgba, PixelType.UnsignedByte, green);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GraphicsApi.TexParameter(TextureTarget.Texture2D,
                TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GraphicsApi.Enable(EnableCap.Texture2D);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 1);
            GraphicsApi.Color4(1, 1, 1, 1);
            DrawTexturedQuad(-0.9f, 0.0f, 0.9f, 0.9f, -0.45f);

            // Bottom half: untextured blue.
            GraphicsApi.Disable(EnableCap.Texture2D);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "use_texture"), 0);
            GraphicsApi.Color4(0, 0, 1, 1);
            DrawQuad(-0.9f, -0.9f, 0.9f, 0.0f, -0.45f);

            // This is farther than both near halves and must fail depth.
            GraphicsApi.Color4(1, 1, 0, 1);
            DrawQuad(-0.9f, -0.9f, 0.9f, 0.9f, 0.75f);

            // Blend 50% red over only the top-right quadrant. Preserve target
            // alpha with ColorMask, matching several game overlay paths.
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Enable(EnableCap.Blend);
            GraphicsApi.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GraphicsApi.ColorMask(true, true, true, false);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 0.5f);
            GraphicsApi.Color4(1, 0, 0, 1);
            DrawQuad(0.0f, 0.0f, 0.9f, 0.9f, -0.7f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(world, "mat_alpha"), 1f);
            GraphicsApi.ColorMask(true, true, true, true);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.Enable(EnableCap.DepthTest);
            byte[] linearBlendPixel = new byte[4];
            GraphicsApi.ReadPixels(48, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, linearBlendPixel);
            GraphicsApi.DeleteTexture(greenTexture);

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

            byte[] pixels = new byte[28];
            byte[] sample = new byte[4];
            GraphicsApi.ReadPixels(24, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 0, 4);
            GraphicsApi.ReadPixels(72, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 4, 4);
            GraphicsApi.ReadPixels(48, 16, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 8, 4);
            ModernGraphicsCompat.Present();

            // Second frame: replay-preview style scaled framebuffer blit using
            // independent read/draw bindings.
            GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer);
            GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.BlitFramebuffer(0, 0, 64, 64,
                0, 0, 48, 64, ClearBufferMask.ColorBufferBit,
                BlitFramebufferFilter.Linear);
            GraphicsApi.ReadPixels(24, 48, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 20, 4);
            GraphicsApi.ReadPixels(24, 16, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 24, 4);
            ModernGraphicsCompat.Present();

            // Third frame: run the real disruption/whiteout program through
            // its array uniforms. A table of ones with white_fac=1 must
            // produce white regardless of the source scene colour.
            GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GraphicsApi.Viewport(0, 0, 96, 64);
            GraphicsApi.ClearColor(0, 0, 0, 1);
            GraphicsApi.Clear(ClearBufferMask.ColorBufferBit);
            GraphicsApi.UseProgram(shift);
            GraphicsApi.Disable(EnableCap.DepthTest);
            GraphicsApi.Disable(EnableCap.Blend);
            GraphicsApi.ActiveTexture(TextureUnit.Texture0);
            GraphicsApi.BindTexture(TextureTarget.Texture2D, color);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "tex"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "shift_idx"), 0);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "shift_fac"), 0f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "lerp_fac"), 0f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "shift_table"),
                64, new float[64]);
            float[] whiteTable = new float[192];
            Array.Fill(whiteTable, 1f);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "white_table"),
                192, whiteTable);
            GraphicsApi.Uniform1(GraphicsApi.GetUniformLocation(shift, "white_fac"), 1f);
            GraphicsApi.Color4(1, 1, 1, 1);
            GraphicsApi.Begin(PrimitiveType.TriangleStrip);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3( 1,  1, 0);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(-1,  1, 0);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3( 1, -1, 0);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(-1, -1, 0);
            GraphicsApi.End();
            GraphicsApi.ReadPixels(48, 32, 1, 1,
                PixelFormat.Rgba, PixelType.UnsignedByte, sample);
            Array.Copy(sample, 0, pixels, 12, 4);
            Array.Copy(linearBlendPixel, 0, pixels, 16, 4);
            ModernGraphicsCompat.Present();

            GraphicsApi.BindTexture(TextureTarget.Texture2D, 0);
            GraphicsApi.DeleteFramebuffer(framebuffer);
            GraphicsApi.DeleteTexture(depth);
            GraphicsApi.DeleteTexture(color);
            GraphicsApi.DeleteProgram(world);
            GraphicsApi.DeleteProgram(rtt);
            GraphicsApi.DeleteProgram(shift);
            return pixels;
        }

        private static void DrawQuad(float left, float bottom, float right, float top, float z)
        {
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.Vertex3(left, bottom, z);
            GraphicsApi.Vertex3(right, bottom, z);
            GraphicsApi.Vertex3(right, top, z);
            GraphicsApi.Vertex3(left, top, z);
            GraphicsApi.End();
        }

        private static void DrawTexturedQuad(float left, float bottom, float right, float top, float z)
        {
            GraphicsApi.Begin(PrimitiveType.Quads);
            GraphicsApi.TexCoord3(0, 0, 0); GraphicsApi.Vertex3(left, bottom, z);
            GraphicsApi.TexCoord3(1, 0, 0); GraphicsApi.Vertex3(right, bottom, z);
            GraphicsApi.TexCoord3(1, 1, 0); GraphicsApi.Vertex3(right, top, z);
            GraphicsApi.TexCoord3(0, 1, 0); GraphicsApi.Vertex3(left, top, z);
            GraphicsApi.End();
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
