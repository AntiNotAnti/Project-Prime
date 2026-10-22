using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead
{
public partial class Scene
{
    // Separate experimental control: the retired AA preset remains inert.
    private static readonly bool SpatialAntialiasingRequested =
        Environment.GetEnvironmentVariable("PROJECT_PRIME_GL_SPATIAL_AA") == "1";
    private int _spatialAaProgram, _spatialAaTexture, _spatialAaFramebuffer;
    private int _spatialAaSampler, _spatialAaTexel;
    private Vector2i _spatialAaSize, _spatialAaRefusedSize;
    private bool _spatialAaRefused;
    private int[]? _spatialAaViewport, _spatialAaColorMask;
#if !ANDROID
    private int[]? _spatialAaPolygonMode;
#endif
    private long SpatialAntialiasingTargetBytes => (long)_spatialAaSize.X * _spatialAaSize.Y * 4;

    // World + outlines only. Copy back before model HUD/preview overlays, so
    // their color/depth ownership and all readback descriptors stay unchanged.
    private void ApplySpatialAntialiasing()
    {
        if (!SpatialAntialiasingRequested) return;
        Vector2i size = _targetSize;
        if (_screenTexture == 0 || size.X <= 0 || size.Y <= 0
            || (_spatialAaRefused && _spatialAaRefusedSize == size)) return;
        int draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int read = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int program = GL.GetInteger(GetPName.CurrentProgram);
        int active = GL.GetInteger(GetPName.ActiveTexture);
        GL.ActiveTexture(TextureUnit.Texture0);
        int texture = GL.GetInteger(GetPName.TextureBinding2D);
        bool depth = GL.IsEnabled(EnableCap.DepthTest), blend = GL.IsEnabled(EnableCap.Blend);
        bool cull = GL.IsEnabled(EnableCap.CullFace), scissor = GL.IsEnabled(EnableCap.ScissorTest);
        bool stencil = GL.IsEnabled(EnableCap.StencilTest), alpha = GL.IsEnabled(EnableCap.AlphaTest);
        bool offset = GL.IsEnabled(EnableCap.PolygonOffsetFill);
        bool depthMask = GL.GetInteger(GetPName.DepthWritemask) != 0;
        _spatialAaViewport ??= new int[4]; _spatialAaColorMask ??= new int[4];
        GL.GetInteger(GetPName.Viewport, _spatialAaViewport);
        GL.GetInteger(GetPName.ColorWritemask, _spatialAaColorMask);
#if !ANDROID
        _spatialAaPolygonMode ??= new int[2];
        GL.GetInteger(GetPName.PolygonMode, _spatialAaPolygonMode);
        GL.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);
#endif
        try
        {
            EnsureSpatialAntialiasing(size);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, _spatialAaFramebuffer);
            GL.Viewport(0, 0, size.X, size.Y);
            GL.Disable(EnableCap.DepthTest); GL.DepthMask(false);
            GL.Disable(EnableCap.Blend); GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.StencilTest);
            GL.Disable(EnableCap.AlphaTest); GL.Disable(EnableCap.PolygonOffsetFill);
            GL.ColorMask(true, true, true, true);
            GL.UseProgram(_spatialAaProgram);
            GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
            GL.Uniform1(_spatialAaSampler, 0);
            GL.Uniform2(_spatialAaTexel, 1f / size.X, 1f / size.Y);
            DrawGraphicsFullscreenQuad();
            if (GL.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("Spatial AA draw failed; original color retained.");
            // Exact-size copy, native encoded domain throughout. Never sample
            // a texture while it is attached to the current drawing target.
            GL.BindTexture(TextureTarget.Texture2D, _screenTexture);
            GL.CopyTexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, 0, 0, size.X, size.Y);
            if (GL.GetError() != ErrorCode.NoError)
                throw new InvalidOperationException("Spatial AA draw/copy failed.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            _spatialAaRefused = true; _spatialAaRefusedSize = size;
            // Preserve a previously valid owner until scene disposal or a
            // changed-size retry; a rejected replacement never publishes.
            Mods.DebugLog.Line("render", "experimental spatial AA unavailable: " + ex.Message);
        }
        finally
        {
            GL.UseProgram(program);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.ActiveTexture((TextureUnit)active);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
            GL.Viewport(_spatialAaViewport[0], _spatialAaViewport[1], _spatialAaViewport[2], _spatialAaViewport[3]);
            GL.ColorMask(_spatialAaColorMask[0] != 0, _spatialAaColorMask[1] != 0,
                _spatialAaColorMask[2] != 0, _spatialAaColorMask[3] != 0);
            GL.DepthMask(depthMask);
            Restore(EnableCap.DepthTest, depth); Restore(EnableCap.Blend, blend);
            Restore(EnableCap.CullFace, cull); Restore(EnableCap.ScissorTest, scissor);
            Restore(EnableCap.StencilTest, stencil); Restore(EnableCap.AlphaTest, alpha);
            Restore(EnableCap.PolygonOffsetFill, offset);
#if !ANDROID
            GL.PolygonMode(TriangleFace.Front, (OpenTK.Graphics.OpenGL.PolygonMode)_spatialAaPolygonMode[0]);
            GL.PolygonMode(TriangleFace.Back, (OpenTK.Graphics.OpenGL.PolygonMode)_spatialAaPolygonMode[1]);
#endif
        }
        static void Restore(EnableCap cap, bool enabled) { if (enabled) GL.Enable(cap); else GL.Disable(cap); }
    }

    private void EnsureSpatialAntialiasing(Vector2i size)
    {
        if (_spatialAaTexture != 0 && _spatialAaSize == size) return;
        long bytes = checked((long)size.X * size.Y * 4);
        if (!CanAllocateTargetBytes(bytes) || size.X > GL.GetInteger(GetPName.MaxTextureSize)
            || size.Y > GL.GetInteger(GetPName.MaxTextureSize))
            throw new InvalidOperationException("Spatial AA target exceeds capability/aggregate peak budget.");
        int vertex = 0, fragment = 0, program = 0, texture = 0, framebuffer = 0;
        try
        {
            if (_spatialAaProgram == 0)
            {
                vertex = CompileSceneShader(ShaderType.VertexShader, Mods.Render.SpatialAntialiasingShader.VertexSource, "spatial-aa-vertex");
                fragment = CompileSceneShader(ShaderType.FragmentShader, Mods.Render.SpatialAntialiasingShader.FragmentSource, "spatial-aa-fragment");
                program = LinkSceneProgram(vertex, fragment, "spatial-aa-program");
            }
            framebuffer = GL.GenFramebuffer(); texture = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, size.X, size.Y, 0,
                PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Spatial AA target allocation failed.");
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, texture, 0);
            ValidateFramebuffer("Spatial AA output");
            Mods.Render.OpenGlFailureInjection.Hit("spatial-aa-target-before-publish");
            if (_spatialAaFramebuffer != 0) GL.DeleteFramebuffer(_spatialAaFramebuffer);
            DeleteTexture(ref _spatialAaTexture);
            _spatialAaFramebuffer = framebuffer; framebuffer = 0;
            _spatialAaTexture = texture; texture = 0;
            _spatialAaSize = size;
            if (program != 0)
            {
                _spatialAaProgram = program; program = 0;
                _spatialAaSampler = GL.GetUniformLocation(_spatialAaProgram, "tex");
                _spatialAaTexel = GL.GetUniformLocation(_spatialAaProgram, "texel");
            }
            _spatialAaRefused = false;
        }
        finally
        {
            DeleteSceneShader(ref vertex); DeleteSceneShader(ref fragment);
            DeleteProgram(ref program);
            if (framebuffer != 0) GL.DeleteFramebuffer(framebuffer);
            DeleteTexture(ref texture);
        }
    }

    private void DisposeSpatialAntialiasing()
    {
        if (_spatialAaProgram != 0 && GL.GetInteger(GetPName.CurrentProgram) == _spatialAaProgram) GL.UseProgram(0);
        DeleteProgram(ref _spatialAaProgram);
        if (_spatialAaFramebuffer != 0) GL.DeleteFramebuffer(_spatialAaFramebuffer);
        _spatialAaFramebuffer = 0;
        DeleteTexture(ref _spatialAaTexture);
        _spatialAaSize = default; _spatialAaRefused = false;
    }
}
}

namespace MphRead.Mods.Render
{
    internal static class SpatialAntialiasingShader
    {
#if ANDROID
        internal static string VertexSource => "#version 300 es\nprecision highp float;\nlayout(location=0) in vec4 a_position;\nlayout(location=3) in vec3 a_texcoord;\nout vec2 uv;\nvoid main(){gl_Position=vec4(a_position.xy,0.0,1.0);uv=a_texcoord.xy;}";
        internal static string FragmentSource => "#version 300 es\nprecision highp float;\nin vec2 uv;\nout vec4 outputColor;\n#define SAMPLE texture\n#define OUTPUT outputColor\n" + Body;
#else
        internal static string VertexSource => "#version 120\nvarying vec2 uv;\nvoid main(){gl_Position=vec4(gl_Vertex.xy,0.0,1.0);uv=gl_MultiTexCoord0.xy;}";
        internal static string FragmentSource => "#version 120\nvarying vec2 uv;\n#define SAMPLE texture2D\n#define OUTPUT gl_FragColor\n" + Body;
#endif
        // Deliberately operates in existing native-encoded RGB. It is neither
        // HDR nor linear-light AA. Five texel-center taps need no sampler edits.
        private const string Body = @"
uniform sampler2D tex;
uniform vec2 texel;
float luma(vec3 c){return dot(c,vec3(0.299,0.587,0.114));}
void main(){
 vec2 p=clamp(uv,texel*0.5,vec2(1.0)-texel*0.5);
 vec4 c=SAMPLE(tex,p);
 vec4 n=SAMPLE(tex,clamp(p+vec2(0.0,texel.y),texel*0.5,vec2(1.0)-texel*0.5));
 vec4 s=SAMPLE(tex,clamp(p-vec2(0.0,texel.y),texel*0.5,vec2(1.0)-texel*0.5));
 vec4 e=SAMPLE(tex,clamp(p+vec2(texel.x,0.0),texel*0.5,vec2(1.0)-texel*0.5));
 vec4 w=SAMPLE(tex,clamp(p-vec2(texel.x,0.0),texel*0.5,vec2(1.0)-texel*0.5));
 float lc=luma(c.rgb),ln=luma(n.rgb),ls=luma(s.rgb),le=luma(e.rgb),lw=luma(w.rgb);
 float low=min(lc,min(min(ln,ls),min(le,lw))),high=max(lc,max(max(ln,ls),max(le,lw)));
 float threshold=max(0.03125,high*0.125),range=high-low;
 float alphaRange=max(c.a,max(max(n.a,s.a),max(e.a,w.a)))-min(c.a,min(min(n.a,s.a),min(e.a,w.a)));
 if(range<=threshold||alphaRange>0.003){OUTPUT=c;return;}
 float h=abs(ln+ls-2.0*lc),v=abs(le+lw-2.0*lc);
 vec3 smoothColor=abs(h-v)<0.001?(n.rgb+s.rgb+e.rgb+w.rgb)*0.25:(h<v?(n.rgb+s.rgb)*0.5:(e.rgb+w.rgb)*0.5);
 float blend=0.5*clamp((range-threshold)/max(range,0.0001),0.0,1.0);
 OUTPUT=vec4(mix(c.rgb,smoothColor,blend),c.a);
}";
    }
}
