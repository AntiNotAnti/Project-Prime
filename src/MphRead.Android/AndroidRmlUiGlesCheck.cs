#if MPHREAD_RMLUI_ANDROID_CHECK
using System;
using System.Collections.Generic;
using Android.Opengl;
using MphRead.Mods.Launcher.RmlUi.Render;
using ES = OpenTK.Graphics.ES30;
using Gl = OpenTK.Graphics.ES30.GL;

namespace MphRead.Droid;

// Runs only in explicitly built validation APKs, on a real current ES3 owner.
internal static unsafe class AndroidRmlUiGlesCheck
{
    internal static string Run()
    {
        const int size = 64;
        int framebuffer = Gl.GenFramebuffer(), color = Gl.GenTexture(), stencil = Gl.GenRenderbuffer();
        using var renderer = new AndroidRmlUiGlesRenderer();
        int assertions = 0;
        try
        {
            Gl.BindTexture(ES.TextureTarget.Texture2D, color);
            Gl.TexImage2D(ES.TextureTarget2d.Texture2D, 0, ES.TextureComponentCount.Rgba, size, size, 0, ES.PixelFormat.Rgba, ES.PixelType.UnsignedByte, nint.Zero);
            Gl.BindRenderbuffer(ES.RenderbufferTarget.Renderbuffer, stencil);
            Gl.RenderbufferStorage(ES.RenderbufferTarget.Renderbuffer, ES.RenderbufferInternalFormat.Depth24Stencil8, size, size);
            Gl.BindFramebuffer(ES.FramebufferTarget.Framebuffer, framebuffer);
            Gl.FramebufferTexture2D(ES.FramebufferTarget.Framebuffer, ES.FramebufferAttachment.ColorAttachment0, ES.TextureTarget2d.Texture2D, color, 0);
            Gl.FramebufferRenderbuffer(ES.FramebufferTarget.Framebuffer, ES.FramebufferAttachment.DepthStencilAttachment, ES.RenderbufferTarget.Renderbuffer, stencil);
            Check(Gl.CheckFramebufferStatus(ES.FramebufferTarget.Framebuffer) == ES.FramebufferErrorCode.FramebufferComplete, "validation framebuffer");

            var geometry = new Dictionary<ulong, RmlUiDrawGeometry>
            {
                [1] = Quad(0, 0, 64, 64, 0xFF0000FF),
                [2] = Quad(0, 0, 16, 16, 0x80000080), // premultiplied red
                [3] = Quad(0, 0, 32, 64, 0xFFFFFFFF),
                [4] = Quad(0, 0, 64, 32, 0xFFFFFFFF),
                [5] = Quad(0, 0, 16, 16, 0xFFFFFFFF)
            };
            var textures = new Dictionary<ulong, RmlUiDrawTexture> { [7] = new(1, 1, new byte[] { 0, 64, 0, 128 }) };
            ulong generation = 1;
            void Draw(params RmlUiDrawCommand[] commands)
            {
                Clear(); renderer.DrawFrame(new(generation++, commands, geometry, textures), size, size);
                Check(GLES30.GlGetError() == GLES30.GlNoError, "ES3 command error");
            }
            Draw(Geometry(2, 8, 8)); Pixel(10, 10, 128, 0, 127, "premultiplied blend"); Pixel(4, 4, 0, 0, 255, "scene color load");
            var atlas = Geometry(5, 8, 8); atlas.Texture = 7;
            Draw(atlas); Pixel(10, 10, 0, 64, 127, "RGBA atlas");
            var scissor = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.Scissor); scissor.X = 12; scissor.Y = 12; scissor.Width = 8; scissor.Height = 8;
            var enableScissor = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.EnableScissor); enableScissor.Enabled = 1;
            Draw(scissor, enableScissor, Geometry(1)); Pixel(14, 14, 255, 0, 0, "top-left scissor inside"); Pixel(14, 8, 0, 0, 255, "top-left scissor outside");
            var transform = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.Transform); transform.Enabled = 1;
            transform.Transform[0] = transform.Transform[5] = 2; transform.Transform[10] = transform.Transform[15] = 1;
            transform.Transform[12] = 20; transform.Transform[13] = 4;
            Draw(transform, Geometry(2, 3, 2)); Pixel(28, 10, 128, 0, 127, "column-major transform and local translation"); Pixel(24, 10, 0, 0, 255, "translation transformed before matrix");
            var clip = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.ClipMask); clip.Geometry = 3;
            var enabledClip = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.EnableClipMask); enabledClip.Enabled = 1;
            Draw(enabledClip, clip, Geometry(1)); Pixel(12, 12, 255, 0, 0, "clip set inside"); Pixel(44, 12, 0, 0, 255, "clip set outside");
            clip.Operation = RmlUiClipOperation.SetInverse;
            Draw(enabledClip, clip, Geometry(1)); Pixel(12, 12, 0, 0, 255, "inverse clip inside"); Pixel(44, 12, 255, 0, 0, "inverse clip outside");
            clip.Operation = RmlUiClipOperation.Set;
            var intersect = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.ClipMask); intersect.Geometry = 4; intersect.Operation = RmlUiClipOperation.Intersect;
            Draw(enabledClip, clip, intersect, Geometry(1)); Pixel(12, 12, 255, 0, 0, "intersect clip inside"); Pixel(12, 44, 0, 0, 255, "intersect clip outside");

            Clear();
            GLES30.GlEnable(GLES30.GlScissorTest); GLES30.GlScissor(3, 7, 20, 17);
            GLES30.GlEnable(GLES30.GlStencilTest); GLES30.GlStencilFuncSeparate(GLES30.GlFront, GLES30.GlEqual, 37, 0x6F);
            GLES30.GlStencilFuncSeparate(GLES30.GlBack, 0x0205, 29, 0x3F);
            GLES30.GlStencilMaskSeparate(GLES30.GlFront, 0x5F); GLES30.GlStencilMaskSeparate(GLES30.GlBack, 0x2F);
            GLES30.GlStencilOpSeparate(GLES30.GlFront, GLES30.GlKeep, GLES30.GlIncr, GLES30.GlReplace);
            GLES30.GlStencilOpSeparate(GLES30.GlBack, GLES30.GlDecr, GLES30.GlKeep, GLES30.GlInvert);
            GLES30.GlViewport(4, 5, 27, 29); GLES30.GlColorMask(true, false, true, false);
            GLES30.GlEnable(GLES30.GlDepthTest); GLES30.GlDepthMask(true);
            GLES30.GlEnable(GLES30.GlBlend); GLES30.GlBlendFuncSeparate(GLES30.GlSrcAlpha, GLES30.GlOneMinusSrcAlpha, GLES30.GlOne, GLES30.GlZero);
            GLES30.GlClearColor(.2f, .3f, .4f, .5f);
            string signature = AndroidRmlUiGlesRenderer.StateSignature();
            renderer.DrawFrame(new(generation++, new[] { enabledClip, clip, intersect, Geometry(1) }, geometry, textures), size, size);
            Check(signature == AndroidRmlUiGlesRenderer.StateSignature(), "owner GL state restored");
            GLES30.GlDisable(GLES30.GlScissorTest); GLES30.GlDisable(GLES30.GlStencilTest); GLES30.GlDisable(GLES30.GlDepthTest);
            GLES30.GlColorMask(true, true, true, true); GLES30.GlViewport(0, 0, size, size);
            // Render green only where the original scene stencil is still 37.
            GLES30.GlEnable(GLES30.GlStencilTest); GLES30.GlStencilFunc(GLES30.GlEqual, 37, 255);
            GLES30.GlStencilMask(0); GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlKeep);
            DrawGreen(); Pixel(12, 12, 0, 255, 0, "scene stencil inside native mask preserved");
            Pixel(44, 44, 0, 255, 0, "scene stencil outside native mask preserved");
            Check(GLES30.GlGetError() == GLES30.GlNoError, "state restoration GL error");
            GLES30.GlDisable(GLES30.GlStencilTest);
            var stable = new RmlUiDrawListFrame(generation, new[] { atlas }, geometry, textures);
            for (int i = 0; i < 100; i++) { Clear(); renderer.DrawFrame(stable, size, size); Check(renderer.ResourceCounts == (1, 1), "stable GPU resources"); }
            Pixel(10, 10, 0, 64, 127, "repeat frame render");
            return $"PASS ES3 {assertions} assertions: premultiplied color/atlas, scissor, transform, clip masks, owner state, 100 resource cycles";

            void Check(bool okay, string name) { if (!okay) throw new InvalidOperationException("Android RmlUi GLES check failed: " + name); assertions++; }
            void Clear()
            {
                GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, framebuffer); GLES30.GlViewport(0, 0, size, size);
                GLES30.GlDisable(GLES30.GlScissorTest); GLES30.GlDisable(GLES30.GlStencilTest); GLES30.GlDisable(GLES30.GlDepthTest);
                GLES30.GlColorMask(true, true, true, true); GLES30.GlStencilMask(255); GLES30.GlClearStencil(37);
                GLES30.GlClearColor(0, 0, 1, 1); GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlStencilBufferBit);
            }
            void Pixel(int x, int y, int r, int g, int b, string name)
            {
                byte[] pixel = new byte[4];
                fixed (byte* pointer = pixel) Gl.ReadPixels(x, size - y - 1, 1, 1, ES.PixelFormat.Rgba, ES.PixelType.UnsignedByte, (nint)pointer);
                Check(Math.Abs(pixel[0] - r) <= 2 && Math.Abs(pixel[1] - g) <= 2 && Math.Abs(pixel[2] - b) <= 2 && pixel[3] >= 253, name + $" ({pixel[0]},{pixel[1]},{pixel[2]},{pixel[3]})");
            }
        }
        finally
        {
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, 0);
            Gl.DeleteFramebuffer(framebuffer); Gl.DeleteTexture(color); Gl.DeleteRenderbuffer(stencil);
        }
    }
    private static RmlUiDrawCommand Geometry(ulong id, float x = 0, float y = 0)
    { var c = RmlUiDrawCommand.Create(RmlUiDrawCommandKind.Geometry); c.Geometry = id; c.TranslationX = x; c.TranslationY = y; return c; }
    private static RmlUiDrawGeometry Quad(float x, float y, float width, float height, uint color) => new(new[]
    {
        new RmlUiVertex { X=x,Y=y,Color=color }, new RmlUiVertex { X=x+width,Y=y,Color=color },
        new RmlUiVertex { X=x+width,Y=y+height,Color=color }, new RmlUiVertex { X=x,Y=y+height,Color=color }
    }, new[] { 0,1,2,0,2,3 });
    private static void DrawGreen()
    {
        int vertex = Gl.CreateShader(ES.ShaderType.VertexShader), fragment = Gl.CreateShader(ES.ShaderType.FragmentShader);
        int program = Gl.CreateProgram(), array = Gl.GenVertexArray();
        try
        {
            Gl.ShaderSource(vertex, "#version 300 es\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2.0-1.0,0.0,1.0);}"); Gl.CompileShader(vertex);
            Gl.ShaderSource(fragment, "#version 300 es\nprecision mediump float;out vec4 output_color;void main(){output_color=vec4(0.0,1.0,0.0,1.0);}"); Gl.CompileShader(fragment);
            Gl.AttachShader(program, vertex); Gl.AttachShader(program, fragment); Gl.LinkProgram(program);
            Gl.GetProgram(program, ES.GetProgramParameterName.LinkStatus, out int okay);
            if (okay == 0) throw new InvalidOperationException("Stencil validation shader: " + Gl.GetProgramInfoLog(program));
            GLES30.GlDisable(GLES30.GlBlend); GLES30.GlUseProgram(program); GLES30.GlBindVertexArray(array);
            GLES30.GlDrawArrays(GLES30.GlTriangles, 0, 3);
        }
        finally { Gl.DeleteVertexArray(array); Gl.DeleteProgram(program); Gl.DeleteShader(vertex); Gl.DeleteShader(fragment); }
    }
}
#endif
