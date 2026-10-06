using System;
using Android.Opengl;
using MphRead.Mods.Render;

namespace MphRead.Droid
{
    /// <summary>
    /// The launcher's picture, drawn inside the game's frame.
    ///
    /// Vulkan uses the same facade compositor as the desktop. The compatibility
    /// ES3 context retains a small shader because its facade has no matrix stack.
    ///
    /// Premultiplied alpha, because that is what Avalonia renders. Blending
    /// with SrcAlpha instead would darken every glyph edge against the match
    /// behind the panel.
    ///
    /// Everything here runs on the current render owner thread.
    /// </summary>
    internal static class AndroidUiOverlay
    {
        private const string VertexSource = @"#version 300 es
layout(location = 0) in vec2 a_pos;
out vec2 v_uv;
void main()
{
    // Avalonia's first row is the top of the screen and GL's is the bottom.
    v_uv = vec2(a_pos.x * 0.5 + 0.5, 0.5 - a_pos.y * 0.5);
    gl_Position = vec4(a_pos, 0.0, 1.0);
}";

        private const string FragmentSource = @"#version 300 es
precision mediump float;
uniform sampler2D u_screen;
in vec2 v_uv;
out vec4 o_colour;
void main()
{
    o_colour = texture(u_screen, v_uv);
}";

        private static int _program;

        /// <summary>
        /// <c>GL_CULL_FACE</c>. Spelled out because the binding gives the
        /// constant and <c>glCullFace</c> the same name, and the method wins.
        /// </summary>
        private const int CullFace = 0x0B44;
        private static int _vao;
        private static int _buffer;
        private static int _texture;
        private static int _width;
        private static int _height;
        private static bool _hasFrame;
        private static readonly UiOverlayRendererState _owner = new();
        private static readonly FullscreenUiOverlay _modernOverlay = new();

        // The ES bridge has no attrib stack. Capture exactly the native state
        // touched by upload/setup/draw, with reusable query scratch on its owner
        // thread rather than managed arrays allocated for every panel frame.
        private readonly struct GlesOverlayState
        {
            private static readonly int[] Integer = new int[1];
            private static readonly int[] Viewport = new int[4];
            private static readonly int[] ColorMask = new int[4];
            private readonly int _program, _vao, _buffer, _framebuffer, _active, _texture, _unpack;
            private readonly int _srcRgb, _dstRgb, _srcAlpha, _dstAlpha, _equationRgb, _equationAlpha;
            private readonly int _x, _y, _width, _height;
            private readonly bool _depth, _cull, _blend, _stencil, _scissor, _depthWrite;
            private readonly bool _red, _green, _blue, _alpha;

            private static int Get(int name)
            {
                GLES30.GlGetIntegerv(name, Integer, 0);
                return Integer[0];
            }

            private GlesOverlayState(bool capture)
            {
                _program = Get(GLES30.GlCurrentProgram);
                _vao = Get(GLES30.GlVertexArrayBinding);
                _buffer = Get(GLES30.GlArrayBufferBinding);
                _framebuffer = Get(GLES30.GlDrawFramebufferBinding);
                _active = Get(0x84E0); // GL_ACTIVE_TEXTURE (binding method shares the name)
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                _texture = Get(GLES30.GlTextureBinding2d);
                GLES30.GlActiveTexture(_active);
                _unpack = Get(GLES30.GlUnpackAlignment);
                _srcRgb = Get(GLES30.GlBlendSrcRgb);
                _dstRgb = Get(GLES30.GlBlendDstRgb);
                _srcAlpha = Get(GLES30.GlBlendSrcAlpha);
                _dstAlpha = Get(GLES30.GlBlendDstAlpha);
                _equationRgb = Get(GLES30.GlBlendEquationRgb);
                _equationAlpha = Get(GLES30.GlBlendEquationAlpha);
                _depthWrite = Get(GLES30.GlDepthWritemask) != 0;
                _depth = GLES30.GlIsEnabled(GLES30.GlDepthTest);
                _cull = GLES30.GlIsEnabled(CullFace);
                _blend = GLES30.GlIsEnabled(GLES30.GlBlend);
                _stencil = GLES30.GlIsEnabled(GLES30.GlStencilTest);
                _scissor = GLES30.GlIsEnabled(GLES30.GlScissorTest);
                GLES30.GlGetIntegerv(0x0BA2, Viewport, 0); // GL_VIEWPORT
                _x = Viewport[0]; _y = Viewport[1]; _width = Viewport[2]; _height = Viewport[3];
                GLES30.GlGetIntegerv(GLES30.GlColorWritemask, ColorMask, 0);
                _red = ColorMask[0] != 0; _green = ColorMask[1] != 0;
                _blue = ColorMask[2] != 0; _alpha = ColorMask[3] != 0;
            }

            internal static GlesOverlayState Capture() => new(capture: true);

            private static void Enable(int cap, bool enabled)
            {
                if (enabled) GLES30.GlEnable(cap); else GLES30.GlDisable(cap);
            }

            internal void Restore()
            {
                GLES30.GlUseProgram(_program);
                GLES30.GlBindVertexArray(_vao);
                GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _buffer);
                GLES30.GlBindFramebuffer(GLES30.GlDrawFramebuffer, _framebuffer);
                GLES30.GlViewport(_x, _y, _width, _height);
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                GLES30.GlBindTexture(GLES30.GlTexture2d, _texture);
                GLES30.GlActiveTexture(_active);
                GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, _unpack);
                GLES30.GlBlendFuncSeparate(_srcRgb, _dstRgb, _srcAlpha, _dstAlpha);
                GLES30.GlBlendEquationSeparate(_equationRgb, _equationAlpha);
                GLES30.GlDepthMask(_depthWrite);
                GLES30.GlColorMask(_red, _green, _blue, _alpha);
                Enable(GLES30.GlDepthTest, _depth); Enable(CullFace, _cull);
                Enable(GLES30.GlBlend, _blend); Enable(GLES30.GlStencilTest, _stencil);
                Enable(GLES30.GlScissorTest, _scissor);
            }
        }

        /// <summary>Whether the last uploaded frame should be drawn.</summary>
        public static bool Visible { get; set; }

        // Called before taking the producer's frame. A new generation needs
        // another upload even when the Avalonia raster version did not change.
        public static bool BeginRenderer(bool modern, int contextGeneration)
        {
            if (_owner.Matches(modern, contextGeneration)) return false;
            bool preservedModernRegistry = _owner.HasOwner && _owner.Modern && modern
                && ReferenceEquals(_modernOverlay.ResourceOwner, ModernGraphicsCompat.UiResourceOwner)
                && ModernGraphicsCompat.UiNativeResourcesAvailable;
            Release(preservedModernRegistry);
            _owner.Begin(modern, contextGeneration);
            return true;
        }

        /// <summary>
        /// Take a rendered frame. Tightly packed RGBA, top row first, which is
        /// what the offscreen top level hands over.
        /// </summary>
        public static void Upload(byte[] pixels, int width, int height)
        {
            if (!UiOverlayRendererState.HasCompletePixels(width, height, pixels.Length))
            {
                return;
            }
            if (ModernGraphicsCompat.Active)
            {
                BeginRenderer(true, ModernGraphicsCompat.DeviceGeneration);
                _hasFrame = _modernOverlay.Upload(pixels, width, height);
                return;
            }
            if (!_owner.HasOwner || _owner.Modern || _owner.Failed) return;
            var state = GlesOverlayState.Capture();
            try
            {
                if (!Ensure()) return;
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                GLES30.GlBindTexture(GLES30.GlTexture2d, _texture);
                GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, 4);
                using var buffer = Java.Nio.ByteBuffer.Wrap(pixels)!;
                if (width != _width || height != _height)
                    GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, width, height, 0,
                        GLES30.GlRgba, GLES30.GlUnsignedByte, buffer);
                else
                    GLES30.GlTexSubImage2D(GLES30.GlTexture2d, 0, 0, 0, width, height,
                        GLES30.GlRgba, GLES30.GlUnsignedByte, buffer);
                _width = width;
                _height = height;
                _hasFrame = true;
            }
            finally { state.Restore(); }
        }

        /// <summary>Put it on the screen, over everything drawn so far.</summary>
        public static void Draw(int width, int height)
        {
            if (ModernGraphicsCompat.Active)
            {
                BeginRenderer(true, ModernGraphicsCompat.DeviceGeneration);
                if (Visible && _hasFrame) _modernOverlay.Draw(width, height);
                return;
            }
            if (!_owner.HasOwner || _owner.Modern || _owner.Failed || !Visible || !_hasFrame || _program == 0)
            {
                return;
            }
            if (width <= 0 || height <= 0) return;
            var state = GlesOverlayState.Capture();
            try
            {
                GLES30.GlBindFramebuffer(GLES30.GlDrawFramebuffer, 0);
                GLES30.GlViewport(0, 0, width, height);
                GLES30.GlUseProgram(_program);
                GLES30.GlDisable(GLES30.GlDepthTest);
                GLES30.GlDisable(CullFace);
                GLES30.GlDisable(GLES30.GlStencilTest);
                GLES30.GlDisable(GLES30.GlScissorTest);
                GLES30.GlDepthMask(false);
                GLES30.GlColorMask(true, true, true, true);
                GLES30.GlEnable(GLES30.GlBlend);
                GLES30.GlBlendEquation(GLES30.GlFuncAdd);
                GLES30.GlBlendFunc(GLES30.GlOne, GLES30.GlOneMinusSrcAlpha);
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                GLES30.GlBindTexture(GLES30.GlTexture2d, _texture);
                GLES30.GlBindVertexArray(_vao);
                GLES30.GlDrawArrays(GLES30.GlTriangleStrip, 0, 4);
            }
            finally { state.Restore(); }
        }

        /// <summary>Give it all back. The context has to be current.</summary>
        public static void Release() => Release(nativeResourcesAvailable: true);

        public static void Release(bool nativeResourcesAvailable)
        {
            try
            {
                if (_owner.Modern) _modernOverlay.Release(nativeResourcesAvailable);
                else if (_owner.HasOwner && nativeResourcesAvailable)
                {
                    if (_texture != 0) GLES30.GlDeleteTextures(1, new[] { _texture }, 0);
                    if (_buffer != 0) GLES30.GlDeleteBuffers(1, new[] { _buffer }, 0);
                    if (_vao != 0) GLES30.GlDeleteVertexArrays(1, new[] { _vao }, 0);
                    if (_program != 0) GLES30.GlDeleteProgram(_program);
                }
            }
            finally
            {
                _modernOverlay.Forget();
                _texture = _buffer = _vao = _program = _width = _height = 0;
                _hasFrame = false;
                _owner.Forget();
                Visible = false;
            }
        }

        private static bool Ensure()
        {
            if (_program != 0)
            {
                return true;
            }
            try
            {
                _program = Link();
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                float[] quad = { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
                int[] names = new int[1];
                GLES30.GlGenVertexArrays(1, names, 0);
                _vao = names[0];
                GLES30.GlGenBuffers(1, names, 0);
                _buffer = names[0];
                GLES30.GlBindVertexArray(_vao);
                GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _buffer);
                using (var bytes = Java.Nio.ByteBuffer.AllocateDirect(quad.Length * 4)!)
                {
                    bytes.Order(Java.Nio.ByteOrder.NativeOrder());
                    Java.Nio.FloatBuffer floats = bytes.AsFloatBuffer()!;
                    floats.Put(quad);
                    floats.Position(0);
                    GLES30.GlBufferData(GLES30.GlArrayBuffer, quad.Length * 4, floats,
                        GLES30.GlStaticDraw);
                }
                GLES30.GlEnableVertexAttribArray(0);
                GLES30.GlVertexAttribPointer(0, 2, GLES30.GlFloat, false, 0, 0);
                GLES30.GlBindVertexArray(0);
                GLES30.GlGenTextures(1, names, 0);
                _texture = names[0];
                GLES30.GlBindTexture(GLES30.GlTexture2d, _texture);
                // Linear: the screens are drawn at the window's own resolution
                // so there is nothing to magnify, and it is what keeps the one
                // frame where a resize has not caught up from showing a seam.
                GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter,
                    GLES30.GlLinear);
                GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter,
                    GLES30.GlLinear);
                GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS,
                    GLES30.GlClampToEdge);
                GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT,
                    GLES30.GlClampToEdge);
                GLES30.GlBindTexture(GLES30.GlTexture2d, 0);
                GLES30.GlUseProgram(_program);
                GLES30.GlUniform1i(GLES30.GlGetUniformLocation(_program, "u_screen"), 0);
                GLES30.GlUseProgram(0);
                return true;
            }
            catch (Exception ex)
            {
                // No panel rather than no match: the engine's own picker is
                // still on the results screen underneath.
                int generation = _owner.Generation;
                try { Release(nativeResourcesAvailable: true); }
                finally { _owner.Begin(false, generation); _owner.MarkFailed(); }
                Console.WriteLine($"[ui] the overlay could not be set up: {ex}");
                return false;
            }
        }

        private static int Link()
        {
            int vertex = 0, fragment = 0, program = 0;
            try
            {
                vertex = Compile(GLES30.GlVertexShader, VertexSource);
                fragment = Compile(GLES30.GlFragmentShader, FragmentSource);
                program = GLES30.GlCreateProgram();
                GLES30.GlAttachShader(program, vertex);
                GLES30.GlAttachShader(program, fragment);
                GLES30.GlLinkProgram(program);
                int[] status = new int[1];
                GLES30.GlGetProgramiv(program, GLES30.GlLinkStatus, status, 0);
                if (status[0] == 0)
                    throw new InvalidOperationException(
                        "the overlay program would not link: " + GLES30.GlGetProgramInfoLog(program));
                return program;
            }
            catch
            {
                if (program != 0) GLES30.GlDeleteProgram(program);
                throw;
            }
            finally
            {
                if (vertex != 0) GLES30.GlDeleteShader(vertex);
                if (fragment != 0) GLES30.GlDeleteShader(fragment);
            }
        }

        private static int Compile(int type, string source)
        {
            int shader = GLES30.GlCreateShader(type);
            GLES30.GlShaderSource(shader, source);
            GLES30.GlCompileShader(shader);
            int[] status = new int[1];
            GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, status, 0);
            if (status[0] == 0)
            {
                string log = GLES30.GlGetShaderInfoLog(shader);
                GLES30.GlDeleteShader(shader);
                throw new InvalidOperationException(
                    "the overlay shader would not compile: " + log);
            }
            return shader;
        }
    }
}
