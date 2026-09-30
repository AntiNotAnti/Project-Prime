#if MPHREAD_SHELL
using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.Desktop;

namespace MphRead.Mods.Diagnostics
{
    internal static class ThumbnailWindowCheck
    {
        public static int Run(bool legacyCheck = false)
        {
            try
            {
                // Use the worker's actual settings in a separate process, before
                // the launcher's context can initialize GLFW or mask a failure.
                var settings = ThumbnailCapture.WindowSettings(64, 64);
                if (legacyCheck)
                {
                    settings.APIVersion = new Version(2, 1);
                    settings.Profile = OpenTK.Windowing.Common.ContextProfile.Any;
                }
                using var window = new GameWindow(new GameWindowSettings(), settings);
                using var graphics = new MphRead.Mods.Render.DesktopGraphicsSession(window);
                bool debugSkipped = false;
                ScreenCapture.EnableDebugOutput(line =>
                {
                    Console.WriteLine(line);
                    debugSkipped |= line.Contains("GL debug output unavailable", StringComparison.Ordinal);
                });
                Console.WriteLine(ScreenCapture.DescribeContext());
                CompileRendererShaders();
                if (GL.GetError() != ErrorCode.NoError)
                    throw new InvalidOperationException("Thumbnail diagnostics raised an OpenGL error.");
                if (legacyCheck && (!debugSkipped
                    || !(GL.GetString(StringName.Version) ?? "").StartsWith("2.1", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Legacy regression requires GL 2.1 without KHR_debug.");
                int texture = GL.GenTexture();
                int framebuffer = GL.GenFramebuffer();
                try
                {
                    GL.BindTexture(TextureTarget.Texture2D, texture);
                    GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8,
                        64, 64, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
                    GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);
                    if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                        != FramebufferErrorCode.FramebufferComplete)
                        throw new InvalidOperationException("Thumbnail framebuffer is incomplete.");
                    GL.DrawBuffer(DrawBufferMode.ColorAttachment0);
                    GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
                    GL.Viewport(0, 0, 64, 64);
                    GL.ClearColor(0, 0, 0, 1);
                    GL.Clear(ClearBufferMask.ColorBufferBit);
                    // Immediate mode is required by the real thumbnail renderer.
                    GL.Begin(PrimitiveType.Quads);
                    GL.Color3(1f, 0.25f, 0.5f);
                    GL.Vertex2(-1f, -1f); GL.Vertex2(1f, -1f);
                    GL.Vertex2(1f, 1f); GL.Vertex2(-1f, 1f);
                    GL.End();
                    byte[] pixel = new byte[4];
                    GL.ReadPixels(32, 32, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);
                    if (GL.GetError() != ErrorCode.NoError || pixel[0] < 240
                        || Math.Abs(pixel[1] - 64) > 4 || Math.Abs(pixel[2] - 128) > 4)
                        throw new InvalidOperationException("Thumbnail legacy rendering/readback failed.");
                    Console.WriteLine("Thumbnail window check passed.");
                    return 0;
                }
                finally
                {
                    GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
                    GL.DeleteFramebuffer(framebuffer);
                    GL.DeleteTexture(texture);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[thumbnailwindowcheck] {ex}");
                return 1;
            }
        }
        private static void CompileRendererShaders()
        {
            CompileProgram("world", Shaders.VertexShader, Shaders.FragmentShader);
            CompileProgram("graphics post-process",
                Render.GraphicsPipelineShader.VertexSource,
                Render.GraphicsPipelineShader.FragmentSource);
            CompileProgram("graphics HDR tone map",
                Render.GraphicsToneMapShader.VertexSource,
                Render.GraphicsToneMapShader.FragmentSource);
            CompileProgram("deferred PBR G-buffer",
                Render.DeferredPbrShader.VertexSource,
                Render.DeferredPbrShader.FragmentSource);
        }

        private static void CompileProgram(string label, string vertexSource, string fragmentSource)
        {
            int vertex = 0, fragment = 0, program = 0;
            try
            {
                vertex = GL.CreateShader(ShaderType.VertexShader);
                GL.ShaderSource(vertex, vertexSource);
                GL.CompileShader(vertex);
                GL.GetShader(vertex, ShaderParameter.CompileStatus, out int vertexOk);
                if (vertexOk == 0)
                    throw new InvalidOperationException($"{label} vertex shader: {GL.GetShaderInfoLog(vertex)}");

                fragment = GL.CreateShader(ShaderType.FragmentShader);
                GL.ShaderSource(fragment, fragmentSource);
                GL.CompileShader(fragment);
                GL.GetShader(fragment, ShaderParameter.CompileStatus, out int fragmentOk);
                if (fragmentOk == 0)
                    throw new InvalidOperationException($"{label} fragment shader: {GL.GetShaderInfoLog(fragment)}");

                program = GL.CreateProgram();
                GL.AttachShader(program, vertex);
                GL.AttachShader(program, fragment);
                GL.LinkProgram(program);
                GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
                if (linked == 0)
                    throw new InvalidOperationException($"{label} program: {GL.GetProgramInfoLog(program)}");
                Console.WriteLine($"[thumbnailwindowcheck] {label} shaders linked");
            }
            finally
            {
                if (program != 0) GL.DeleteProgram(program);
                if (fragment != 0) GL.DeleteShader(fragment);
                if (vertex != 0) GL.DeleteShader(vertex);
            }
        }

    }
}
#endif