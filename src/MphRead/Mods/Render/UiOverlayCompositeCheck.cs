#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using G = MphRead.Mods.Render.GraphicsApi;

namespace MphRead.Mods.Render;

// Real-device content-free contract used by desktop and Android acceptance.
internal static class UiOverlayCompositeCheck
{
    internal static void Run(int width, int height)
    {
        if (width < 8 || height < 8) throw new ArgumentOutOfRangeException(nameof(width));
        var overlay = new FullscreenUiOverlay();
        int previousProgram = G.GetInteger(GetPName.CurrentProgram);
        int previousDraw = G.GetInteger(GetPName.DrawFramebufferBinding);
        int previousRead = G.GetInteger(GetPName.ReadFramebufferBinding);
        int framebuffer = 0, texture = 0, program = 0, vertex = 0, fragment = 0;
        G.PushAttrib(AttribMask.AllAttribBits);
        try
        {
            G.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            G.Viewport(0, 0, width, height);
            G.Disable(EnableCap.ScissorTest);
            G.ColorMask(true, true, true, true);
            G.ClearColor(0, 0, 1, 1);
            G.Clear(ClearBufferMask.ColorBufferBit);
            vertex = G.CreateShader(ShaderType.VertexShader);
            fragment = G.CreateShader(ShaderType.FragmentShader);
            G.ShaderSource(vertex, "#version 110\nvoid main(){ gl_Position=gl_Vertex; }");
            G.ShaderSource(fragment, "#version 110\nvoid main(){ gl_FragColor=vec4(1.0); }");
            G.CompileShader(vertex); G.CompileShader(fragment);
            program = G.CreateProgram();
            G.AttachShader(program, vertex); G.AttachShader(program, fragment); G.LinkProgram(program);
            G.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException("UI fixture program: " + G.GetProgramInfoLog(program));
            G.UseProgram(program);
            texture = G.GenTexture();
            G.ActiveTexture(TextureUnit.Texture1);
            G.BindTexture(TextureTarget.Texture2D, texture);
            // A top opaque row and bottom half-transparent premultiplied row.
            byte[] rgba = { 255, 0, 0, 255, 0, 128, 0, 128 };
            if (!overlay.Upload(rgba, 1, 2)) throw new InvalidOperationException("UI fixture upload rejected.");
            if (G.GetInteger(GetPName.ActiveTexture) != (int)TextureUnit.Texture1
                || G.GetInteger(GetPName.TextureBinding2D) != texture)
                throw new InvalidOperationException("UI upload changed active texture or binding.");
            framebuffer = G.GenFramebuffer();
            G.BindFramebuffer(FramebufferTarget.DrawFramebuffer, framebuffer);
            G.Viewport(1, 2, width - 2, height - 3);
            G.Enable(EnableCap.ScissorTest); G.Scissor(0, 0, 1, 1);
            G.Enable(EnableCap.DepthTest);
            G.ColorMask(false, true, false, true);
            G.BlendEquation(BlendEquationMode.FuncReverseSubtract);
            overlay.Draw(width, height);
            int[] viewport = new int[4];
            G.GetInteger(GetPName.Viewport, viewport);
            if (viewport[0] != 1 || viewport[1] != 2 || viewport[2] != width - 2 || viewport[3] != height - 3
                || G.GetInteger(GetPName.CurrentProgram) != program
                || G.GetInteger(GetPName.DrawFramebufferBinding) != framebuffer
                || G.GetInteger(GetPName.ActiveTexture) != (int)TextureUnit.Texture1
                || G.GetInteger(GetPName.TextureBinding2D) != texture
                || !G.IsEnabled(EnableCap.ScissorTest) || !G.IsEnabled(EnableCap.DepthTest))
                throw new InvalidOperationException("UI composition did not restore the caller's render state.");
            G.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            byte[] top = new byte[4], bottom = new byte[4];
            G.ReadPixels(width / 2, height * 7 / 8, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, top);
            G.ReadPixels(width / 2, height / 8, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, bottom);
            int half = ModernGraphicsCompat.Active && ModernGraphicsCompat.SurfaceUsesSrgb ? 188 : 128;
            if (top[0] < 240 || top[1] > 16 || top[2] > 16
                || bottom[0] > 16 || Math.Abs(bottom[1] - half) > 16 || Math.Abs(bottom[2] - half) > 16)
                throw new InvalidOperationException($"UI row origin/premultiplied composite failed: top={string.Join(',', top)}, bottom={string.Join(',', bottom)}.");
            overlay.Release();
            if (overlay.HasFrame) throw new InvalidOperationException("UI release retained a frame.");
            overlay.Upload(rgba, 1, 2);
            if (!overlay.HasFrame) throw new InvalidOperationException("Same-size UI upload after release did not recover.");
            Console.WriteLine("UIOVERLAY native premultiplied/row-origin/state/reupload PASS");
        }
        finally
        {
            overlay.Release();
            G.UseProgram(previousProgram);
            G.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
            G.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
            if (framebuffer != 0) G.DeleteFramebuffer(framebuffer);
            if (texture != 0) G.DeleteTexture(texture);
            if (program != 0) G.DeleteProgram(program);
            if (vertex != 0) G.DeleteShader(vertex);
            if (fragment != 0) G.DeleteShader(fragment);
            G.PopAttrib();
        }
    }
}
#endif
