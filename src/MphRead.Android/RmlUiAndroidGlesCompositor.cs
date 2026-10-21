#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using System;
using System.Collections.Generic;
using System.Buffers;
using System.Runtime.InteropServices;
using Android.Opengl;
using Java.Nio;
using MphRead.Mods.Launcher.RmlUi.Render;

namespace MphRead.Droid;

/// <summary>
/// GLES 3 terminal pass for the pinned RmlUi draw-list ABI. Call only while
/// GameView's EGL context is current, after scene/HUD composition, before Swap.
/// Owns neither a second EGL surface nor a window or graphics device.
/// The experiment does not activate the Android launcher or input itself.
/// </summary>
internal sealed class RmlUiAndroidGlesCompositor : IDisposable
{
    private const int CullFaceEnable = 0x0B44; // GL_CULL_FACE; Android GlCullFace is a method.
    private const string VertexShader = @"#version 300 es
layout(location=0) in vec2 a_position;
layout(location=1) in vec4 a_color;
layout(location=2) in vec2 a_uv;
uniform mat4 u_transform;
uniform vec2 u_translate;
uniform vec2 u_extent;
out vec4 v_color;
out vec2 v_uv;
void main() {
    vec4 p = u_transform * vec4(a_position + u_translate, 0.0, 1.0);
    gl_Position = vec4(2.0*p.x/u_extent.x-p.w, p.w-2.0*p.y/u_extent.y,
                       p.z*0.0001, p.w);
    v_color = a_color;
    v_uv = a_uv;
}";
    private const string FragmentShader = @"#version 300 es
precision mediump float;
uniform sampler2D u_atlas;
in vec4 v_color;
in vec2 v_uv;
out vec4 o_color;
void main() { o_color = texture(u_atlas, v_uv) * v_color; }";
    private const string CompositeVertexShader = @"#version 300 es
layout(location=0) in vec2 a_position;
out vec2 v_uv;
void main() {
    v_uv = a_position * 0.5 + 0.5;
    gl_Position = vec4(a_position, 0.0, 1.0);
}";
    private const string CompositeFragmentShader = @"#version 300 es
precision mediump float;
uniform sampler2D u_image;
in vec2 v_uv;
out vec4 o_color;
void main() { o_color = texture(u_image, v_uv); }";

    private sealed class Geometry
    {
        internal int Vao, VertexBuffer, IndexBuffer, Indices;
    }

    private readonly Dictionary<ulong, Geometry> _geometry = new();
    private readonly Dictionary<ulong, int> _textures = new();
    private readonly List<ulong> _releasedHandles = new();
    private RmlUiAndroidGlesState? _stateSnapshot;

    private RmlUiAndroidGlesState CaptureState()
    {
        if (_stateSnapshot == null) return _stateSnapshot = new RmlUiAndroidGlesState();
        _stateSnapshot.Capture();
        return _stateSnapshot;
    }
    private int _ownerThread;
    private EGLContext? _context;
    private ulong _generation;
    private int _uiProgram, _compositeProgram;
    private int _transformUniform, _translateUniform, _extentUniform;
    private int _quadVao, _quadBuffer, _whiteTexture;
    private int _framebuffer, _surfaceTexture, _stencilBuffer;
    private int _width, _height;
    private RmlUiDrawListFrame? _cachedFrame;
    private RmlUiGlesOperation[]? _cachedPlan;
    private int _cachedWidth, _cachedHeight;

    internal long UploadedGeometry { get; private set; }
    internal long UploadedTextures { get; private set; }
    internal long ReusedPlans { get; private set; }

    internal void Draw(RmlUiDrawListFrame frame, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (width <= 0 || height <= 0) return;
        if (width > 16384 || height > 16384)
            throw new ArgumentOutOfRangeException(nameof(width), "RmlUi target exceeds the permitted framebuffer range.");
        VerifyContext();
        RmlUiAndroidGlesState state = CaptureState();
        try
        {
            // Explicit canvas texture-object sampling must not be overridden by an inherited unit0 sampler.
            // Normalize upload state for tightly packed UI images; the shared pass snapshot restores it all.
            GLES30.GlActiveTexture(GLES30.GlTexture0);
            GLES30.GlBindSampler(0, 0);
            GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, 1);
            GLES30.GlBindBuffer(GLES30.GlPixelUnpackBuffer, 0);
            GLES30.GlPixelStorei(GLES30.GlUnpackRowLength, 0);
            GLES30.GlPixelStorei(GLES30.GlUnpackSkipRows, 0);
            GLES30.GlPixelStorei(GLES30.GlUnpackSkipPixels, 0);
            if (_generation != frame.Generation)
            {
                ReleaseCachedGeometryAndTextures();
                _generation = frame.Generation;
                _cachedFrame = null;
            }
            EnsureProgramAndQuad();
            EnsureSurface(width, height);
            SynchronizeResources(frame);
            RmlUiGlesOperation[] plan;
            if (ReferenceEquals(frame, _cachedFrame) && _cachedWidth == width
                && _cachedHeight == height && _cachedPlan != null)
            {
                ReusedPlans++;
                plan = _cachedPlan;
            }
            else
            {
                plan = RmlUiGlesPlan.Build(frame, width, height);
                _cachedFrame = frame;
                _cachedPlan = plan;
                _cachedWidth = width;
                _cachedHeight = height;
            }
            RenderUi(frame, plan, width, height);
            Composite(state.DrawFramebuffer, width, height);
        }
        finally
        {
            state.Restore();
        }
    }

    private void VerifyContext()
    {
        int thread = Environment.CurrentManagedThreadId;
        if (_ownerThread != 0 && _ownerThread != thread)
            throw new InvalidOperationException("RmlUi GLES objects must stay on the GameView EGL owner thread.");
        EGLContext? current = EGL14.EglGetCurrentContext();
        if (current == null || current.Equals(EGL14.EglNoContext))
            throw new InvalidOperationException("An existing GameView EGL context must be current.");
        if (_context != null && !_context.Equals(current))
        {
            // The old context is no longer current. IDs from it are invalid
            // here; the game must retire its old context independently.
            ForgetLostContext();
        }
        _ownerThread = thread;
        _context = current;
    }

    private void ForgetLostContext()
    {
        _geometry.Clear(); _textures.Clear();
        _uiProgram = _compositeProgram = 0;
        _quadVao = _quadBuffer = _whiteTexture = 0;
        _framebuffer = _surfaceTexture = _stencilBuffer = 0;
        _width = _height = 0;
        _generation = 0;
        _cachedFrame = null; _cachedPlan = null;
    }

    private static int GenTexture()
    {
        int[] names = new int[1];
        GLES30.GlGenTextures(1, names, 0);
        return names[0];
    }
    private static int GenBuffer()
    {
        int[] names = new int[1];
        GLES30.GlGenBuffers(1, names, 0);
        return names[0];
    }
    private static int GenVertexArray()
    {
        int[] names = new int[1];
        GLES30.GlGenVertexArrays(1, names, 0);
        return names[0];
    }
    private static void TextureParameters()
    {
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, GLES30.GlLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, GLES30.GlLinear);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, GLES30.GlClampToEdge);
        GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, GLES30.GlClampToEdge);
    }
    private static ByteBuffer Direct(byte[] contents, int length = -1)
    {
        if (length < 0) length = contents.Length;
        ByteBuffer bytes = ByteBuffer.AllocateDirect(length)!;
        bytes.Put(contents, 0, length);
        bytes.Position(0);
        return bytes;
    }

    private static void CheckAllocation(string stage)
    {
        int error = GLES30.GlGetError();
        if (error != GLES30.GlNoError)
            throw new InvalidOperationException($"RmlUi GLES {stage} failed (0x{error:X}).");
    }

    private void EnsureProgramAndQuad()
    {
        if (_uiProgram != 0) return;
        int ui = 0, composite = 0, vao = 0, buffer = 0, whiteTexture = 0;
        try
        {
            ui = Link(VertexShader, FragmentShader);
            composite = Link(CompositeVertexShader, CompositeFragmentShader);
            int transform = GLES30.GlGetUniformLocation(ui, "u_transform");
            int translate = GLES30.GlGetUniformLocation(ui, "u_translate");
            int extent = GLES30.GlGetUniformLocation(ui, "u_extent");
            int atlas = GLES30.GlGetUniformLocation(ui, "u_atlas");
            int image = GLES30.GlGetUniformLocation(composite, "u_image");
            if (transform < 0 || translate < 0 || extent < 0 || atlas < 0 || image < 0)
                throw new InvalidOperationException("RmlUi GLES shader uniforms are missing.");
            GLES30.GlUseProgram(ui);
            GLES30.GlUniform1i(atlas, 0);
            GLES30.GlUseProgram(composite);
            GLES30.GlUniform1i(image, 0);
            MphRead.Mods.Render.OpenGlFailureInjection.Hit("android-rmlui-quad");
            vao = GenVertexArray(); buffer = GenBuffer();
            if (vao == 0 || buffer == 0) throw new InvalidOperationException("RmlUi GLES quad allocation failed.");
            GLES30.GlBindVertexArray(vao);
            GLES30.GlBindBuffer(GLES30.GlArrayBuffer, buffer);
            float[] corners = { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
            using (ByteBuffer bytes = ByteBuffer.AllocateDirect(corners.Length * sizeof(float))!)
            {
                bytes.Order(ByteOrder.NativeOrder()!);
                using var floats = bytes.AsFloatBuffer()!;
                floats.Put(corners); floats.Position(0);
                GLES30.GlBufferData(GLES30.GlArrayBuffer, corners.Length * sizeof(float), floats, GLES30.GlStaticDraw);
            }
            GLES30.GlEnableVertexAttribArray(0);
            GLES30.GlVertexAttribPointer(0, 2, GLES30.GlFloat, false, 2 * sizeof(float), 0);
            MphRead.Mods.Render.OpenGlFailureInjection.Hit("android-rmlui-white-texture");
            whiteTexture = GenTexture();
            if (whiteTexture == 0) throw new InvalidOperationException("RmlUi GLES white texture allocation failed.");
            GLES30.GlBindTexture(GLES30.GlTexture2d, whiteTexture);
            TextureParameters();
            using (ByteBuffer white = Direct(new byte[] { 255, 255, 255, 255 }))
                GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba, 1, 1, 0,
                    GLES30.GlRgba, GLES30.GlUnsignedByte, white);
            CheckAllocation("program/quad initialization");
            _uiProgram = ui; _compositeProgram = composite;
            _transformUniform = transform; _translateUniform = translate; _extentUniform = extent;
            _quadVao = vao; _quadBuffer = buffer; _whiteTexture = whiteTexture;
            ui = composite = vao = buffer = whiteTexture = 0;
        }
        finally
        {
            if (ui != 0) GLES30.GlDeleteProgram(ui);
            if (composite != 0) GLES30.GlDeleteProgram(composite);
            if (vao != 0) GLES30.GlDeleteVertexArrays(1, new[] { vao }, 0);
            if (buffer != 0) GLES30.GlDeleteBuffers(1, new[] { buffer }, 0);
            DeleteTexture(whiteTexture);
        }
    }

    private void EnsureSurface(int width, int height)
    {
        if (_framebuffer != 0 && _width == width && _height == height) return;
        int texture = 0, stencil = 0, framebuffer = 0;
        try
        {
            MphRead.Mods.Render.OpenGlFailureInjection.Hit("android-rmlui-surface");
            texture = GenTexture();
            GLES30.GlBindTexture(GLES30.GlTexture2d, texture);
            TextureParameters();
            GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba8,
                width, height, 0, GLES30.GlRgba, GLES30.GlUnsignedByte, (Java.Nio.Buffer?)null);
            int[] one = new int[1];
            GLES30.GlGenRenderbuffers(1, one, 0); stencil = one[0];
            GLES30.GlBindRenderbuffer(GLES30.GlRenderbuffer, stencil);
            GLES30.GlRenderbufferStorage(GLES30.GlRenderbuffer, GLES30.GlStencilIndex8, width, height);
            GLES30.GlGenFramebuffers(1, one, 0); framebuffer = one[0];
            if (texture == 0 || stencil == 0 || framebuffer == 0)
                throw new InvalidOperationException("RmlUi GLES surface allocation failed.");
            GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, framebuffer);
            GLES30.GlFramebufferTexture2D(GLES30.GlFramebuffer, GLES30.GlColorAttachment0, GLES30.GlTexture2d, texture, 0);
            GLES30.GlFramebufferRenderbuffer(GLES30.GlFramebuffer, GLES30.GlStencilAttachment, GLES30.GlRenderbuffer, stencil);
            if (GLES30.GlCheckFramebufferStatus(GLES30.GlFramebuffer) != GLES30.GlFramebufferComplete)
                throw new InvalidOperationException("RmlUi GLES color/stencil framebuffer is incomplete.");
            CheckAllocation("surface initialization");
            ReleaseSurface();
            _framebuffer = framebuffer; _stencilBuffer = stencil; _surfaceTexture = texture;
            _width = width; _height = height;
            framebuffer = stencil = texture = 0;
        }
        finally
        {
            if (framebuffer != 0) GLES30.GlDeleteFramebuffers(1, new[] { framebuffer }, 0);
            if (stencil != 0) GLES30.GlDeleteRenderbuffers(1, new[] { stencil }, 0);
            DeleteTexture(texture);
        }
    }

    private void SynchronizeResources(RmlUiDrawListFrame frame)
    {
        List<ulong> released = _releasedHandles;
        released.Clear();
        foreach (ulong handle in _geometry.Keys)
            if (!frame.Geometry.ContainsKey(handle)) released.Add(handle);
        foreach (ulong handle in released) { DeleteGeometry(_geometry[handle]); _geometry.Remove(handle); }
        released.Clear();
        foreach (ulong handle in _textures.Keys)
            if (!frame.Textures.ContainsKey(handle)) released.Add(handle);
        foreach (ulong handle in released) { DeleteTexture(_textures[handle]); _textures.Remove(handle); }

        foreach (var (handle, source) in frame.Geometry)
        {
            if (_geometry.ContainsKey(handle)) continue;
            if (source.Vertices.Length > 4_000_000 || source.Indices.Length > 12_000_000)
                throw new InvalidOperationException("RmlUi geometry exceeds the GLES upload limit.");
            Geometry gpu = new() { Indices = source.Indices.Length };
            try
            {
                gpu.Vao = GenVertexArray(); gpu.VertexBuffer = GenBuffer(); gpu.IndexBuffer = GenBuffer();
                if (gpu.Vao == 0 || gpu.VertexBuffer == 0 || gpu.IndexBuffer == 0)
                    throw new InvalidOperationException("RmlUi GLES geometry allocation failed.");
                GLES30.GlBindVertexArray(gpu.Vao);
                GLES30.GlBindBuffer(GLES30.GlArrayBuffer, gpu.VertexBuffer);
                ReadOnlySpan<byte> sourceVertexBytes = MemoryMarshal.AsBytes(source.Vertices.AsSpan());
                int vertexLength = sourceVertexBytes.Length;
                byte[] vertexScratch = ArrayPool<byte>.Shared.Rent(vertexLength);
                try
                {
                    sourceVertexBytes.CopyTo(vertexScratch.AsSpan(0, vertexLength));
                    using ByteBuffer vertices = Direct(vertexScratch, vertexLength);
                    GLES30.GlBufferData(GLES30.GlArrayBuffer, vertexLength, vertices, GLES30.GlStaticDraw);
                }
                finally { ArrayPool<byte>.Shared.Return(vertexScratch); }
                GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, gpu.IndexBuffer);
                int indexLength = checked(source.Indices.Length * sizeof(int));
                byte[] indexScratch = ArrayPool<byte>.Shared.Rent(indexLength);
                try
                {
                    System.Buffer.BlockCopy(source.Indices, 0, indexScratch, 0, indexLength);
                    using ByteBuffer indices = Direct(indexScratch, indexLength);
                    GLES30.GlBufferData(GLES30.GlElementArrayBuffer, indexLength, indices, GLES30.GlStaticDraw);
                }
                finally { ArrayPool<byte>.Shared.Return(indexScratch); }
                GLES30.GlEnableVertexAttribArray(0);
                GLES30.GlVertexAttribPointer(0, 2, GLES30.GlFloat, false, 20, 0);
                GLES30.GlEnableVertexAttribArray(1);
                GLES30.GlVertexAttribPointer(1, 4, GLES30.GlUnsignedByte, true, 20, 8);
                GLES30.GlEnableVertexAttribArray(2);
                GLES30.GlVertexAttribPointer(2, 2, GLES30.GlFloat, false, 20, 12);
                GLES30.GlBindVertexArray(0);
                CheckAllocation("geometry upload");
                _geometry.Add(handle, gpu);
                gpu = null!;
                UploadedGeometry++;
            }
            finally { if (gpu != null) DeleteGeometry(gpu); }
        }

        foreach (var (handle, source) in frame.Textures)
        {
            if (_textures.ContainsKey(handle)) continue;
            int texture = GenTexture();
            try
            {
                if (texture == 0) throw new InvalidOperationException("RmlUi GLES texture allocation failed.");
                GLES30.GlActiveTexture(GLES30.GlTexture0);
                GLES30.GlBindTexture(GLES30.GlTexture2d, texture);
                TextureParameters();
                if (source.Sampling is { } sampling)
                {
                    GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMinFilter, sampling.Filter == 0 ? GLES30.GlNearest : GLES30.GlLinear);
                    GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureMagFilter, sampling.Filter == 0 ? GLES30.GlNearest : GLES30.GlLinear);
                    GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapS, sampling.WrapS == 0 ? GLES30.GlClampToEdge : GLES30.GlRepeat);
                    GLES30.GlTexParameteri(GLES30.GlTexture2d, GLES30.GlTextureWrapT, sampling.WrapT == 0 ? GLES30.GlClampToEdge : GLES30.GlRepeat);
                }
                GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, 1);
                using (ByteBuffer pixels = Direct(source.Pixels))
                    GLES30.GlTexImage2D(GLES30.GlTexture2d, 0, GLES30.GlRgba,
                        source.Width, source.Height, 0, GLES30.GlRgba, GLES30.GlUnsignedByte, pixels);
                CheckAllocation("texture upload");
                _textures.Add(handle, texture);
                texture = 0;
                UploadedTextures++;
            }
            finally { DeleteTexture(texture); }
        }
    }

    private void RenderUi(RmlUiDrawListFrame frame, RmlUiGlesOperation[] operations, int width, int height)
    {
        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, _framebuffer);
        GLES30.GlViewport(0, 0, width, height);
        GLES30.GlDisable(GLES30.GlDepthTest);
        GLES30.GlDisable(CullFaceEnable);
        GLES30.GlDisable(GLES30.GlScissorTest);
        GLES30.GlDepthMask(false);
        GLES30.GlColorMask(true, true, true, true);
        GLES30.GlClearColor(0, 0, 0, 0);
        GLES30.GlClearStencil(0);
        GLES30.GlStencilMask(0xff);
        GLES30.GlClear(GLES30.GlColorBufferBit | GLES30.GlStencilBufferBit);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlBlendEquation(GLES30.GlFuncAdd);
        GLES30.GlBlendFunc(GLES30.GlOne, GLES30.GlOneMinusSrcAlpha);
        GLES30.GlUseProgram(_uiProgram);
        GLES30.GlUniform2f(_extentUniform, width, height);
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlEnable(GLES30.GlScissorTest);

        int boundTexture = -1, boundVao = -1;
        RmlUiGlesRect? lastScissor = null;
        foreach (RmlUiGlesOperation op in operations)
        {
            if (!lastScissor.HasValue || lastScissor.Value != op.Scissor)
            {
                GLES30.GlScissor(op.Scissor.X, op.Scissor.Y, op.Scissor.Width, op.Scissor.Height);
                lastScissor = op.Scissor;
            }
            GLES30.GlUniformMatrix4fv(_transformUniform, 1, false, op.Transform, 0);
            GLES30.GlUniform2f(_translateUniform, op.TranslationX, op.TranslationY);
            switch (op.Kind)
            {
                case RmlUiGlesOperationKind.MaskSet:
                case RmlUiGlesOperationKind.MaskSetInverse:
                    GLES30.GlEnable(GLES30.GlStencilTest);
                    GLES30.GlStencilMask(0xff);
                    GLES30.GlClearStencil(op.Kind == RmlUiGlesOperationKind.MaskSetInverse ? 1 : 0);
                    GLES30.GlClear(GLES30.GlStencilBufferBit);
                    GLES30.GlColorMask(false, false, false, false);
                    GLES30.GlStencilFunc(GLES30.GlAlways, op.StencilReference, 0xff);
                    GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlReplace);
                    break;
                case RmlUiGlesOperationKind.MaskIntersect:
                    GLES30.GlEnable(GLES30.GlStencilTest);
                    GLES30.GlColorMask(false, false, false, false);
                    GLES30.GlStencilMask(0xff);
                    GLES30.GlStencilFunc(GLES30.GlEqual, op.StencilReference, 0xff);
                    GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlIncr);
                    break;
                default:
                    GLES30.GlColorMask(true, true, true, true);
                    GLES30.GlStencilMask(0);
                    if (op.ClipEnabled)
                    {
                        GLES30.GlEnable(GLES30.GlStencilTest);
                        GLES30.GlStencilFunc(GLES30.GlEqual, op.StencilReference, 0xff);
                        GLES30.GlStencilOp(GLES30.GlKeep, GLES30.GlKeep, GLES30.GlKeep);
                    }
                    else GLES30.GlDisable(GLES30.GlStencilTest);
                    break;
            }
            int texture = op.Texture == 0 ? _whiteTexture : _textures[op.Texture];
            if (boundTexture != texture)
            {
                GLES30.GlBindTexture(GLES30.GlTexture2d, texture);
                boundTexture = texture;
            }
            Geometry mesh = _geometry[op.Geometry];
            if (boundVao != mesh.Vao)
            {
                GLES30.GlBindVertexArray(mesh.Vao);
                boundVao = mesh.Vao;
            }
            GLES30.GlDrawElements(GLES30.GlTriangles, mesh.Indices, GLES30.GlUnsignedInt, 0);
        }
        GLES30.GlBindVertexArray(0);
        GLES30.GlColorMask(true, true, true, true);
    }

    private void Composite(int targetFramebuffer, int width, int height)
    {
        GLES30.GlBindFramebuffer(GLES30.GlFramebuffer, targetFramebuffer);
        GLES30.GlViewport(0, 0, width, height);
        GLES30.GlDisable(GLES30.GlScissorTest);
        GLES30.GlDisable(GLES30.GlStencilTest);
        GLES30.GlDisable(GLES30.GlDepthTest);
        GLES30.GlDisable(CullFaceEnable);
        GLES30.GlDepthMask(false);
        GLES30.GlColorMask(true, true, true, true);
        GLES30.GlEnable(GLES30.GlBlend);
        GLES30.GlBlendEquation(GLES30.GlFuncAdd);
        GLES30.GlBlendFunc(GLES30.GlOne, GLES30.GlOneMinusSrcAlpha);
        GLES30.GlUseProgram(_compositeProgram);
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _surfaceTexture);
        GLES30.GlBindVertexArray(_quadVao);
        GLES30.GlDrawArrays(GLES30.GlTriangleStrip, 0, 4);
        GLES30.GlBindVertexArray(0);
    }

    private static int Link(string vertexSource, string fragmentSource)
    {
        int vertex = 0, fragment = 0, program = 0;
        try
        {
            vertex = Compile(GLES30.GlVertexShader, vertexSource);
            fragment = Compile(GLES30.GlFragmentShader, fragmentSource);
            program = GLES30.GlCreateProgram();
            if (program == 0) throw new InvalidOperationException("RmlUi GLES program allocation failed.");
            MphRead.Mods.Render.OpenGlFailureInjection.ShaderObject("android-rmlui-link", program);
            GLES30.GlAttachShader(program, vertex);
            GLES30.GlAttachShader(program, fragment);
            GLES30.GlLinkProgram(program);
            int[] status = new int[1];
            GLES30.GlGetProgramiv(program, GLES30.GlLinkStatus, status, 0);
            if (status[0] == 0)
                throw new InvalidOperationException("RmlUi GLES shader link failed: " + GLES30.GlGetProgramInfoLog(program));
            int ready = program; program = 0;
            return ready;
        }
        finally
        {
            if (program != 0) GLES30.GlDeleteProgram(program);
            if (vertex != 0) GLES30.GlDeleteShader(vertex);
            if (fragment != 0) GLES30.GlDeleteShader(fragment);
        }
    }

    private static int Compile(int type, string source)
    {
        int shader = GLES30.GlCreateShader(type);
        if (shader == 0) throw new InvalidOperationException("RmlUi GLES shader allocation failed.");
        try
        {
            MphRead.Mods.Render.OpenGlFailureInjection.ShaderObject(
                type == GLES30.GlVertexShader ? "android-rmlui-vertex" : "android-rmlui-fragment", shader);
            GLES30.GlShaderSource(shader, source);
            GLES30.GlCompileShader(shader);
            int[] status = new int[1];
            GLES30.GlGetShaderiv(shader, GLES30.GlCompileStatus, status, 0);
            if (status[0] == 0)
                throw new InvalidOperationException("RmlUi GLES shader compile failed: " + GLES30.GlGetShaderInfoLog(shader));
            return shader;
        }
        catch { GLES30.GlDeleteShader(shader); throw; }
    }

    private static void DeleteTexture(int texture)
    {
        if (texture != 0) GLES30.GlDeleteTextures(1, new[] { texture }, 0);
    }
    private static void DeleteGeometry(Geometry mesh)
    {
        if (mesh.Vao != 0) GLES30.GlDeleteVertexArrays(1, new[] { mesh.Vao }, 0);
        if (mesh.VertexBuffer != 0) GLES30.GlDeleteBuffers(1, new[] { mesh.VertexBuffer }, 0);
        if (mesh.IndexBuffer != 0) GLES30.GlDeleteBuffers(1, new[] { mesh.IndexBuffer }, 0);
    }
    private void ReleaseCachedGeometryAndTextures()
    {
        foreach (Geometry mesh in _geometry.Values) DeleteGeometry(mesh);
        foreach (int texture in _textures.Values) DeleteTexture(texture);
        _geometry.Clear(); _textures.Clear();
        _cachedFrame = null; _cachedPlan = null;
    }
    private void ReleaseSurface()
    {
        if (_framebuffer != 0) GLES30.GlDeleteFramebuffers(1, new[] { _framebuffer }, 0);
        if (_stencilBuffer != 0) GLES30.GlDeleteRenderbuffers(1, new[] { _stencilBuffer }, 0);
        DeleteTexture(_surfaceTexture);
        _framebuffer = _stencilBuffer = _surfaceTexture = 0;
        _width = _height = 0;
    }

    // Shutdown must normally happen on the EGL owner before context teardown.
    // A context-loss path deliberately discards stale names without GL deletes.
    internal void Release()
    {
        if (_ownerThread != 0 && _ownerThread != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("RmlUi GLES shutdown must run on its owner thread.");
        EGLContext? current = EGL14.EglGetCurrentContext();
        if (current == null || current.Equals(EGL14.EglNoContext)
            || _context == null || !_context.Equals(current))
        {
            ForgetLostContext();
        }
        else
        {
            RmlUiAndroidGlesState state = CaptureState();
            try
            {
                ReleaseCachedGeometryAndTextures();
                ReleaseSurface();
                if (_quadVao != 0) GLES30.GlDeleteVertexArrays(1, new[] { _quadVao }, 0);
                if (_quadBuffer != 0) GLES30.GlDeleteBuffers(1, new[] { _quadBuffer }, 0);
                DeleteTexture(_whiteTexture);
                if (_uiProgram != 0) GLES30.GlDeleteProgram(_uiProgram);
                if (_compositeProgram != 0) GLES30.GlDeleteProgram(_compositeProgram);
            }
            finally
            {
                // Deleted bindings are reset by GL itself; restoring a deleted
                // name is not valid. The game should perform release at its
                // regular renderer teardown boundary.
                state.Restore();
                ForgetLostContext();
            }
        }
        _context = null;
        _ownerThread = 0;
    }

    public void Dispose() => Release();
}
#endif
