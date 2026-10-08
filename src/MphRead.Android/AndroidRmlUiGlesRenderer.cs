#if MPHREAD_RMLUI_ANDROID
using System;
using System.Collections.Generic;
using System.Linq;
using Android.Opengl;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Render;
using ES = OpenTK.Graphics.ES30;
using RmlGl = OpenTK.Graphics.ES30.GL;

namespace MphRead.Droid;

// Owner-thread ES3 consumer of the native ABI. A private stencil attachment
// keeps rounded/transformed clip masks from destroying the game's stencil.
internal sealed unsafe class AndroidRmlUiGlesRenderer : IDisposable
{
    private readonly RmlUiDrawListReader _reader = new();
    private readonly Dictionary<ulong, (int Vao, int Vertices, int Indices, int Count)> _geometry = new();
    private readonly Dictionary<ulong, int> _textures = new();
    private ulong _generation;
    private int _program, _composite, _quad, _quadBuffer, _target, _color, _stencil, _white, _width, _height;
    private readonly float[] _matrix = new float[16];
    private int _clipReference;
    private const string Vertex = "#version 300 es\n" + """
        layout(location=0) in vec2 position;
        layout(location=1) in vec4 color;
        layout(location=2) in vec2 uv;
        uniform mat4 transform;
        uniform vec2 translation;
        uniform vec2 viewport;
        out vec4 vertex_color;
        out vec2 vertex_uv;
        void main() {
            vec4 p = transform * vec4(position + translation, 0.0, 1.0);
            gl_Position = vec4(2.0*p.x/viewport.x-p.w, p.w-2.0*p.y/viewport.y, -p.z, p.w);
            vertex_color = color;
            vertex_uv = uv;
        }
        """;
    private const string Fragment = "#version 300 es\n" + """
        precision mediump float;
        uniform sampler2D atlas;
        in vec4 vertex_color;
        in vec2 vertex_uv;
        out vec4 output_color;
        void main() { output_color = vertex_color * texture(atlas, vertex_uv); }
        """;
    private const string CompositeVertex = "#version 300 es\n" + """
        layout(location=0) in vec2 position;
        out vec2 uv;
        void main() { gl_Position=vec4(position,0.0,1.0); uv=position*0.5+0.5; }
        """;
    private const string CompositeFragment = "#version 300 es\n" + """
        precision mediump float;
        uniform sampler2D atlas;
        in vec2 uv;
        out vec4 output_color;
        void main() { output_color=texture(atlas,uv); }
        """;

    internal void Draw(int width, int height, int left = 0, int top = 0, int surfaceWidth = 0, int surfaceHeight = 0)
        => DrawFrame(_reader.Capture(), width, height, left, top, surfaceWidth, surfaceHeight);

    internal void DrawFrame(RmlUiDrawListFrame frame, int width, int height, int left = 0, int top = 0, int surfaceWidth = 0, int surfaceHeight = 0)
    {
        if (width <= 0 || height <= 0) return;
        var state = State.Capture();
        try
        {
            EsBindings.Load();
            Ensure(width, height);
            if (_generation != frame.Generation) { ReleaseCaches(); _generation = frame.Generation; }
            Prune(frame);
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, _target);
            GLES30.GlViewport(0, 0, width, height);
            GLES30.GlDisable(GLES30.GlDepthTest); GLES30.GlDepthMask(false);
            GLES30.GlDisable(0x0B44); GLES30.GlDisable(GLES30.GlScissorTest);
            GLES30.GlDisable(GLES30.GlStencilTest);
            GLES30.GlColorMask(true, true, true, true);
            GLES30.GlStencilMask(255); GLES30.GlClearStencil(0);
            GLES30.GlClearColor(0, 0, 0, 0);
            GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlStencilBufferBit);
            GLES30.GlEnable(GLES30.GlBlend); GLES30.GlBlendEquation(GLES30.GlFuncAdd);
            GLES30.GlBlendFunc(GLES30.GlOne, GLES30.GlOneMinusSrcAlpha);
            GLES30.GlActiveTexture(GLES30.GlTexture0);
            GLES30.GlUseProgram(_program);
            GLES30.GlUniform2f(GLES30.GlGetUniformLocation(_program, "viewport"), width, height);
            Identity(); _clipReference = 0;
            foreach (RmlUiDrawCommand command in frame.Commands)
            {
                switch (command.Kind)
                {
                    case RmlUiDrawCommandKind.Transform:
                        if (command.Enabled == 0) Identity();
                        else for (int i = 0; i < 16; i++) _matrix[i] = command.Transform[i];
                        break;
                    case RmlUiDrawCommandKind.EnableScissor: Enable(GLES30.GlScissorTest, command.Enabled != 0); break;
                    case RmlUiDrawCommandKind.Scissor:
                        GLES30.GlScissor(command.X, height - command.Y - command.Height, Math.Max(0, command.Width), Math.Max(0, command.Height)); break;
                    case RmlUiDrawCommandKind.EnableClipMask: Enable(GLES30.GlStencilTest, command.Enabled != 0); break;
                    case RmlUiDrawCommandKind.ClipMask:
                        GLES30.GlStencilMask(255);
                        if (command.Operation != RmlUiClipOperation.Intersect)
                        {
                            // Clear the entire private mask, regardless of the active scissor.
                            bool scissor = GLES30.GlIsEnabled(GLES30.GlScissorTest);
                            GLES30.GlDisable(GLES30.GlScissorTest);
                            GLES30.GlClear(GLES30.GlStencilBufferBit);
                            Enable(GLES30.GlScissorTest, scissor);
                            _clipReference = command.Operation == RmlUiClipOperation.Set ? 1 : 0;
                            GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlReplace);
                        }
                        else { _clipReference++; GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlIncr); }
                        GLES30.GlColorMask(false, false, false, false);
                        GLES30.GlStencilFunc(GLES30.GlAlways, 1, 255);
                        Geometry(frame, command, false);
                        GLES30.GlColorMask(true, true, true, true);
                        GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlKeep);
                        GLES30.GlStencilFunc(GLES30.GlEqual, _clipReference, 255);
                        break;
                    case RmlUiDrawCommandKind.Geometry: Geometry(frame, command, true); break;
                }
            }
            // Load the scene color already present in the owner framebuffer.
            GLES30.GlBindFramebuffer(GLES30.GlDrawFramebuffer, state.Framebuffer);
            int sh = surfaceHeight > 0 ? surfaceHeight : height;
            GLES30.GlViewport(left, sh - top - height, width, height);
            GLES30.GlDisable(GLES30.GlStencilTest); GLES30.GlDisable(GLES30.GlScissorTest);
            GLES30.GlUseProgram(_composite);
            GLES30.GlBindTexture(GLES30.GlTexture2d, _color);
            GLES30.GlBindVertexArray(_quad);
            GLES30.GlDrawArrays(GLES30.GlTriangleStrip, 0, 4);
        }
        finally { state.Restore(); }
    }

    private void Identity()
    {
        Array.Clear(_matrix); _matrix[0] = _matrix[5] = _matrix[10] = _matrix[15] = 1;
    }
    private void Geometry(RmlUiDrawListFrame frame, RmlUiDrawCommand command, bool textured)
    {
        if (!_geometry.TryGetValue(command.Geometry, out var gpu))
        {
            var source = frame.Geometry[command.Geometry];
            int vao = RmlGl.GenVertexArray(), vbo = RmlGl.GenBuffer(), ibo = RmlGl.GenBuffer();
            RmlGl.BindVertexArray(vao);
            RmlGl.BindBuffer(ES.BufferTarget.ArrayBuffer, vbo);
            fixed (RmlUiVertex* data = source.Vertices)
                RmlGl.BufferData(ES.BufferTarget.ArrayBuffer, source.Vertices.Length * sizeof(RmlUiVertex), (nint)data, ES.BufferUsageHint.StaticDraw);
            RmlGl.BindBuffer(ES.BufferTarget.ElementArrayBuffer, ibo);
            fixed (int* data = source.Indices)
                RmlGl.BufferData(ES.BufferTarget.ElementArrayBuffer, source.Indices.Length * sizeof(int), (nint)data, ES.BufferUsageHint.StaticDraw);
            RmlGl.EnableVertexAttribArray(0); RmlGl.VertexAttribPointer(0, 2, ES.VertexAttribPointerType.Float, false, 20, 0);
            RmlGl.EnableVertexAttribArray(1); RmlGl.VertexAttribPointer(1, 4, ES.VertexAttribPointerType.UnsignedByte, true, 20, 8);
            RmlGl.EnableVertexAttribArray(2); RmlGl.VertexAttribPointer(2, 2, ES.VertexAttribPointerType.Float, false, 20, 12);
            gpu = (vao, vbo, ibo, source.Indices.Length); _geometry.Add(command.Geometry, gpu);
        }
        int texture = _white;
        if (textured && command.Texture != 0 && !_textures.TryGetValue(command.Texture, out texture))
        {
            var source = frame.Textures[command.Texture];
            texture = CreateTexture(source.Width, source.Height, source.Pixels); _textures.Add(command.Texture, texture);
        }
        GLES30.GlBindVertexArray(gpu.Vao); GLES30.GlBindTexture(GLES30.GlTexture2d, texture);
        GLES30.GlUniformMatrix4fv(GLES30.GlGetUniformLocation(_program, "transform"), 1, false, _matrix, 0);
        GLES30.GlUniform2f(GLES30.GlGetUniformLocation(_program, "translation"), command.TranslationX, command.TranslationY);
        GLES30.GlDrawElements(GLES30.GlTriangles, gpu.Count, GLES30.GlUnsignedInt, 0);
    }
    private static int CreateTexture(int width, int height, byte[]? pixels)
    {
        int texture = RmlGl.GenTexture();
        RmlGl.BindTexture(ES.TextureTarget.Texture2D, texture);
        RmlGl.TexParameter(ES.TextureTarget.Texture2D, ES.TextureParameterName.TextureMinFilter, (int)ES.TextureMinFilter.Linear);
        RmlGl.TexParameter(ES.TextureTarget.Texture2D, ES.TextureParameterName.TextureMagFilter, (int)ES.TextureMagFilter.Linear);
        RmlGl.TexParameter(ES.TextureTarget.Texture2D, ES.TextureParameterName.TextureWrapS, (int)ES.TextureWrapMode.ClampToEdge);
        RmlGl.TexParameter(ES.TextureTarget.Texture2D, ES.TextureParameterName.TextureWrapT, (int)ES.TextureWrapMode.ClampToEdge);
        RmlGl.BindBuffer(ES.BufferTarget.PixelUnpackBuffer, 0);
        RmlGl.PixelStore(ES.PixelStoreParameter.UnpackAlignment, 1);
        RmlGl.PixelStore(ES.PixelStoreParameter.UnpackRowLength, 0);
        RmlGl.PixelStore(ES.PixelStoreParameter.UnpackSkipRows, 0);
        RmlGl.PixelStore(ES.PixelStoreParameter.UnpackSkipPixels, 0);
        fixed (byte* data = pixels)
            RmlGl.TexImage2D(ES.TextureTarget2d.Texture2D, 0, ES.TextureComponentCount.Rgba, width, height, 0, ES.PixelFormat.Rgba, ES.PixelType.UnsignedByte, (nint)data);
        return texture;
    }
    private void Ensure(int width, int height)
    {
        if (_program == 0)
        {
            _program = Link(Vertex, Fragment); _composite = Link(CompositeVertex, CompositeFragment);
            _quad = RmlGl.GenVertexArray(); _quadBuffer = RmlGl.GenBuffer();
            RmlGl.BindVertexArray(_quad); RmlGl.BindBuffer(ES.BufferTarget.ArrayBuffer, _quadBuffer);
            float[] points = { -1, -1, 1, -1, -1, 1, 1, 1 };
            fixed (float* data = points) RmlGl.BufferData(ES.BufferTarget.ArrayBuffer, points.Length * 4, (nint)data, ES.BufferUsageHint.StaticDraw);
            RmlGl.EnableVertexAttribArray(0); RmlGl.VertexAttribPointer(0, 2, ES.VertexAttribPointerType.Float, false, 8, 0);
            _white = CreateTexture(1, 1, new byte[] { 255, 255, 255, 255 });
        }
        if (_width == width && _height == height) return;
        ReleaseTarget();
        _color = CreateTexture(width, height, null);
        _stencil = RmlGl.GenRenderbuffer(); _target = RmlGl.GenFramebuffer();
        RmlGl.BindRenderbuffer(ES.RenderbufferTarget.Renderbuffer, _stencil);
        RmlGl.RenderbufferStorage(ES.RenderbufferTarget.Renderbuffer, ES.RenderbufferInternalFormat.Depth24Stencil8, width, height);
        RmlGl.BindFramebuffer(ES.FramebufferTarget.Framebuffer, _target);
        RmlGl.FramebufferTexture2D(ES.FramebufferTarget.Framebuffer, ES.FramebufferAttachment.ColorAttachment0, ES.TextureTarget2d.Texture2D, _color, 0);
        RmlGl.FramebufferRenderbuffer(ES.FramebufferTarget.Framebuffer, ES.FramebufferAttachment.DepthStencilAttachment, ES.RenderbufferTarget.Renderbuffer, _stencil);
        if (RmlGl.CheckFramebufferStatus(ES.FramebufferTarget.Framebuffer) != ES.FramebufferErrorCode.FramebufferComplete)
            throw new InvalidOperationException("RmlUi private framebuffer is incomplete.");
        _width = width; _height = height;
    }
    private static int Link(string vertex, string fragment)
    {
        int vs = Compile(ES.ShaderType.VertexShader, vertex), fs = Compile(ES.ShaderType.FragmentShader, fragment), program = RmlGl.CreateProgram();
        try
        {
            RmlGl.AttachShader(program, vs); RmlGl.AttachShader(program, fs); RmlGl.LinkProgram(program);
            RmlGl.GetProgram(program, ES.GetProgramParameterName.LinkStatus, out int ok);
            if (ok == 0) throw new InvalidOperationException(RmlGl.GetProgramInfoLog(program));
            return program;
        }
        catch { RmlGl.DeleteProgram(program); throw; }
        finally { RmlGl.DeleteShader(vs); RmlGl.DeleteShader(fs); }
    }
    private static int Compile(ES.ShaderType type, string source)
    {
        int shader = RmlGl.CreateShader(type); RmlGl.ShaderSource(shader, source); RmlGl.CompileShader(shader);
        RmlGl.GetShader(shader, ES.ShaderParameter.CompileStatus, out int ok);
        if (ok == 0) { string error = RmlGl.GetShaderInfoLog(shader); RmlGl.DeleteShader(shader); throw new InvalidOperationException(error); }
        return shader;
    }
    private void Prune(RmlUiDrawListFrame frame)
    {
        foreach (ulong id in _geometry.Keys.Where(id => !frame.Geometry.ContainsKey(id)).ToArray()) { Delete(_geometry[id]); _geometry.Remove(id); }
        foreach (ulong id in _textures.Keys.Where(id => !frame.Textures.ContainsKey(id)).ToArray()) { RmlGl.DeleteTexture(_textures[id]); _textures.Remove(id); }
    }
    private static void Delete((int Vao, int Vertices, int Indices, int Count) gpu)
    { RmlGl.DeleteVertexArray(gpu.Vao); RmlGl.DeleteBuffer(gpu.Vertices); RmlGl.DeleteBuffer(gpu.Indices); }
    private void ReleaseCaches()
    { foreach (var gpu in _geometry.Values) Delete(gpu); foreach (int texture in _textures.Values) RmlGl.DeleteTexture(texture); _geometry.Clear(); _textures.Clear(); }
    private void ReleaseTarget()
    { if (_target != 0) RmlGl.DeleteFramebuffer(_target); if (_color != 0) RmlGl.DeleteTexture(_color); if (_stencil != 0) RmlGl.DeleteRenderbuffer(_stencil); _target = _color = _stencil = _width = _height = 0; }
    internal void Abandon()
    {
        _geometry.Clear(); _textures.Clear(); _reader.Forget();
        _program = _composite = _quad = _quadBuffer = _white = _target = _color = _stencil = _width = _height = 0;
    }
    public void Dispose()
    {
        ReleaseCaches(); ReleaseTarget(); _reader.Forget();
        if (_program != 0) RmlGl.DeleteProgram(_program); if (_composite != 0) RmlGl.DeleteProgram(_composite);
        if (_quad != 0) RmlGl.DeleteVertexArray(_quad); if (_quadBuffer != 0) RmlGl.DeleteBuffer(_quadBuffer); if (_white != 0) RmlGl.DeleteTexture(_white);
        _program = _composite = _quad = _quadBuffer = _white = 0;
    }
    private static void Enable(int cap, bool value) { if (value) GLES30.GlEnable(cap); else GLES30.GlDisable(cap); }
#if MPHREAD_RMLUI_ANDROID_CHECK
    internal static string StateSignature() => State.Capture().Signature();
    internal (int Geometry, int Textures) ResourceCounts => (_geometry.Count, _textures.Count);
#endif

    private sealed class State
    {
        private static readonly int[] Names = { 0x8B8D, 0x85B5, 0x8894, 0x8CA6, 0x8CAA, 0x8CA7, 0x84E0, 0x0CF5, 0x80C9, 0x80C8, 0x80CB, 0x80CA, 0x8009, 0x883D, 0x0B72,
            0x0B92, 0x0B97, 0x0B93, 0x0B98, 0x0B94, 0x0B95, 0x0B96, 0x8800, 0x8CA3, 0x8CA4, 0x8CA5, 0x8801, 0x8802, 0x8803, 0x0B91, 0x88EF, 0x0CF2, 0x0CF3, 0x0CF4 };
        private static readonly int[] Caps = { GLES30.GlDepthTest, 0x0B44, GLES30.GlBlend, GLES30.GlStencilTest, GLES30.GlScissorTest };
        private readonly int[] _values = new int[Names.Length], _viewport = new int[4], _scissor = new int[4], _mask = new int[4];
        private readonly float[] _clear = new float[4];
        private readonly bool[] _enabled = new bool[Caps.Length];
        private int _texture;
        internal int Framebuffer => _values[3];
#if MPHREAD_RMLUI_ANDROID_CHECK
        internal string Signature() => string.Join(',', _values) + "/" + string.Join(',', _viewport)
            + "/" + string.Join(',', _scissor) + "/" + string.Join(',', _mask) + "/" + string.Join(',', _clear)
            + "/" + string.Join(',', _enabled) + "/" + _texture;
#endif
        internal static State Capture()
        {
            var s = new State(); var scratch = new int[1];
            for (int i = 0; i < Names.Length; i++) { GLES30.GlGetIntegerv(Names[i], scratch, 0); s._values[i] = scratch[0]; }
            for (int i = 0; i < Caps.Length; i++) s._enabled[i] = GLES30.GlIsEnabled(Caps[i]);
            GLES30.GlActiveTexture(GLES30.GlTexture0); GLES30.GlGetIntegerv(GLES30.GlTextureBinding2d, scratch, 0); s._texture = scratch[0]; GLES30.GlActiveTexture(s._values[6]);
            GLES30.GlGetIntegerv(0x0BA2, s._viewport, 0); GLES30.GlGetIntegerv(0x0C10, s._scissor, 0); GLES30.GlGetIntegerv(GLES30.GlColorWritemask, s._mask, 0); GLES30.GlGetFloatv(0x0C22, s._clear, 0);
            return s;
        }
        internal void Restore()
        {
            GLES30.GlUseProgram(_values[0]); GLES30.GlBindVertexArray(_values[1]); GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _values[2]);
            GLES30.GlBindFramebuffer(GLES30.GlDrawFramebuffer, _values[3]); GLES30.GlBindFramebuffer(GLES30.GlReadFramebuffer, _values[4]); GLES30.GlBindRenderbuffer(GLES30.GlRenderbuffer, _values[5]);
            GLES30.GlViewport(_viewport[0], _viewport[1], _viewport[2], _viewport[3]); GLES30.GlScissor(_scissor[0], _scissor[1], _scissor[2], _scissor[3]);
            GLES30.GlActiveTexture(GLES30.GlTexture0); GLES30.GlBindTexture(GLES30.GlTexture2d, _texture); GLES30.GlActiveTexture(_values[6]); GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, _values[7]);
            GLES30.GlBlendFuncSeparate(_values[8], _values[9], _values[10], _values[11]); GLES30.GlBlendEquationSeparate(_values[12], _values[13]); GLES30.GlDepthMask(_values[14] != 0);
            GLES30.GlStencilFuncSeparate(GLES30.GlFront, _values[15], _values[16], _values[17]); GLES30.GlStencilMaskSeparate(GLES30.GlFront, _values[18]); GLES30.GlStencilOpSeparate(GLES30.GlFront, _values[19], _values[20], _values[21]);
            GLES30.GlStencilFuncSeparate(GLES30.GlBack, _values[22], _values[23], _values[24]); GLES30.GlStencilMaskSeparate(GLES30.GlBack, _values[25]); GLES30.GlStencilOpSeparate(GLES30.GlBack, _values[26], _values[27], _values[28]); GLES30.GlClearStencil(_values[29]);
            GLES30.GlBindBuffer(0x88EC, _values[30]); GLES30.GlPixelStorei(0x0CF2, _values[31]); GLES30.GlPixelStorei(0x0CF3, _values[32]); GLES30.GlPixelStorei(0x0CF4, _values[33]);
            GLES30.GlColorMask(_mask[0] != 0, _mask[1] != 0, _mask[2] != 0, _mask[3] != 0); GLES30.GlClearColor(_clear[0], _clear[1], _clear[2], _clear[3]);
            for (int i = 0; i < Caps.Length; i++) Enable(Caps[i], _enabled[i]);
        }
    }
}
#endif
