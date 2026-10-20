#if PROJECTPRIME_RMLUI_ANDROID_EXPERIMENT
using Android.Opengl;

namespace MphRead.Droid;

// Snapshot only the bindings and raster state touched by the opt-in overlay.
// GL ES has no glPushAttrib. Preserve the game renderer's state on both success
// and failure, including separate read/draw framebuffer and texture-unit state.
internal sealed class RmlUiAndroidGlesState
{
    private const int DrawFramebufferBinding = 0x8CA6;
    private const int ReadFramebufferBinding = 0x8CAA;
    private const int VertexArrayBinding = 0x85B5;
    private const int RenderbufferBinding = 0x8CA7;
    private const int Viewport = 0x0BA2;
    private const int CullFaceEnable = 0x0B44; // GL_CULL_FACE; GlCullFace is a method in Android bindings.
    private const int CurrentProgram = 0x8B8D;
    private const int ActiveTexture = 0x84E0;
    private const int TextureBinding2d = 0x8069;
    private const int ArrayBufferBinding = 0x8894;
    private const int ElementArrayBufferBinding = 0x8895;
    private const int ColorClearValue = 0x0C22;
    private const int ColorWriteMask = 0x0C23;
    private const int DepthWriteMask = 0x0B72;
    private const int StencilClearValue = 0x0B91;
    private const int StencilWriteMask = 0x0B98;
    private const int StencilFunc = 0x0B92;
    private const int StencilRef = 0x0B97;
    private const int StencilValueMask = 0x0B93;
    private const int StencilFail = 0x0B94;
    private const int StencilPassDepthFail = 0x0B95;
    private const int StencilPassDepthPass = 0x0B96;
    private const int BlendSrcRgb = 0x80C9;
    private const int BlendDstRgb = 0x80C8;
    private const int BlendSrcAlpha = 0x80CB;
    private const int BlendDstAlpha = 0x80CA;
    private const int BlendEquationRgb = 0x8009;
    private const int BlendEquationAlpha = 0x883D;
    private const int UnpackAlignment = 0x0CF5;
    private const int UnpackRowLength = 0x0CF2, UnpackSkipRows = 0x0CF3, UnpackSkipPixels = 0x0CF4;
    private const int PixelUnpackBufferBinding = 0x88EF, SamplerBinding = 0x8919;

    internal int DrawFramebuffer { get; }
    private readonly int _readFramebuffer, _renderbuffer, _vao, _program, _activeTexture;
    private readonly int _texture0, _arrayBuffer, _elementBuffer, _sampler0, _pixelUnpackBuffer;
    private readonly int _unpackRowLength, _unpackSkipRows, _unpackSkipPixels;
    private readonly int _clearStencil, _writeStencil, _stencilFunc, _stencilRef, _stencilMask;
    private readonly int _stencilFail, _stencilZFail, _stencilZPass, _unpackAlignment;
    private readonly int _srcRgb, _dstRgb, _srcAlpha, _dstAlpha, _equationRgb, _equationAlpha;
    private readonly int _depthWrite;
    private readonly int[] _viewport = new int[4];
    private readonly int[] _scissorRect = new int[4];
    private readonly int[] _colorMask = new int[4];
    private readonly float[] _clearColor = new float[4];
    private readonly bool _depth, _stencil, _blend, _scissor, _cull;

    private static int Read(int name)
    {
        int[] value = new int[1];
        GLES30.GlGetIntegerv(name, value, 0);
        return value[0];
    }

    internal RmlUiAndroidGlesState()
    {
        DrawFramebuffer = Read(DrawFramebufferBinding);
        _readFramebuffer = Read(ReadFramebufferBinding);
        _renderbuffer = Read(RenderbufferBinding);
        _vao = Read(VertexArrayBinding);
        _program = Read(CurrentProgram);
        _activeTexture = Read(ActiveTexture);
        _arrayBuffer = Read(ArrayBufferBinding);
        _elementBuffer = Read(ElementArrayBufferBinding);
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        _texture0 = Read(TextureBinding2d);
        _sampler0 = Read(SamplerBinding);
        GLES30.GlActiveTexture(_activeTexture);
        _clearStencil = Read(StencilClearValue);
        _writeStencil = Read(StencilWriteMask);
        _stencilFunc = Read(StencilFunc);
        _stencilRef = Read(StencilRef);
        _stencilMask = Read(StencilValueMask);
        _stencilFail = Read(StencilFail);
        _stencilZFail = Read(StencilPassDepthFail);
        _stencilZPass = Read(StencilPassDepthPass);
        _unpackAlignment = Read(UnpackAlignment);
        _pixelUnpackBuffer = Read(PixelUnpackBufferBinding);
        _unpackRowLength = Read(UnpackRowLength); _unpackSkipRows = Read(UnpackSkipRows); _unpackSkipPixels = Read(UnpackSkipPixels);
        _srcRgb = Read(BlendSrcRgb); _dstRgb = Read(BlendDstRgb);
        _srcAlpha = Read(BlendSrcAlpha); _dstAlpha = Read(BlendDstAlpha);
        _equationRgb = Read(BlendEquationRgb); _equationAlpha = Read(BlendEquationAlpha);
        _depthWrite = Read(DepthWriteMask);
        GLES30.GlGetIntegerv(Viewport, _viewport, 0);
        GLES30.GlGetIntegerv(GLES30.GlScissorBox, _scissorRect, 0);
        GLES30.GlGetIntegerv(ColorWriteMask, _colorMask, 0);
        GLES30.GlGetFloatv(ColorClearValue, _clearColor, 0);
        _depth = GLES30.GlIsEnabled(GLES30.GlDepthTest);
        _stencil = GLES30.GlIsEnabled(GLES30.GlStencilTest);
        _blend = GLES30.GlIsEnabled(GLES30.GlBlend);
        _scissor = GLES30.GlIsEnabled(GLES30.GlScissorTest);
        _cull = GLES30.GlIsEnabled(CullFaceEnable);
    }

    private static void Toggle(int capability, bool enabled)
    {
        if (enabled) GLES30.GlEnable(capability);
        else GLES30.GlDisable(capability);
    }

    internal void Restore()
    {
        GLES30.GlBindFramebuffer(GLES30.GlDrawFramebuffer, DrawFramebuffer);
        GLES30.GlBindFramebuffer(GLES30.GlReadFramebuffer, _readFramebuffer);
        GLES30.GlUseProgram(_program);
        GLES30.GlBindRenderbuffer(GLES30.GlRenderbuffer, _renderbuffer);
        GLES30.GlBindVertexArray(_vao);
        GLES30.GlBindBuffer(GLES30.GlArrayBuffer, _arrayBuffer);
        GLES30.GlBindBuffer(GLES30.GlElementArrayBuffer, _elementBuffer);
        GLES30.GlActiveTexture(GLES30.GlTexture0);
        GLES30.GlBindTexture(GLES30.GlTexture2d, _texture0);
        GLES30.GlBindSampler(0, _sampler0);
        GLES30.GlActiveTexture(_activeTexture);
        GLES30.GlViewport(_viewport[0], _viewport[1], _viewport[2], _viewport[3]);
        GLES30.GlScissor(_scissorRect[0], _scissorRect[1], _scissorRect[2], _scissorRect[3]);
        GLES30.GlColorMask(_colorMask[0] != 0, _colorMask[1] != 0,
            _colorMask[2] != 0, _colorMask[3] != 0);
        GLES30.GlDepthMask(_depthWrite != 0);
        GLES30.GlStencilMask(_writeStencil);
        GLES30.GlStencilFunc(_stencilFunc, _stencilRef, _stencilMask);
        GLES30.GlStencilOp(_stencilFail, _stencilZFail, _stencilZPass);
        GLES30.GlClearStencil(_clearStencil);
        GLES30.GlClearColor(_clearColor[0], _clearColor[1], _clearColor[2], _clearColor[3]);
        GLES30.GlBlendFuncSeparate(_srcRgb, _dstRgb, _srcAlpha, _dstAlpha);
        GLES30.GlBlendEquationSeparate(_equationRgb, _equationAlpha);
        GLES30.GlPixelStorei(GLES30.GlUnpackAlignment, _unpackAlignment);
        GLES30.GlBindBuffer(GLES30.GlPixelUnpackBuffer, _pixelUnpackBuffer);
        GLES30.GlPixelStorei(GLES30.GlUnpackRowLength, _unpackRowLength);
        GLES30.GlPixelStorei(GLES30.GlUnpackSkipRows, _unpackSkipRows);
        GLES30.GlPixelStorei(GLES30.GlUnpackSkipPixels, _unpackSkipPixels);
        Toggle(GLES30.GlDepthTest, _depth);
        Toggle(GLES30.GlStencilTest, _stencil);
        Toggle(GLES30.GlBlend, _blend);
        Toggle(GLES30.GlScissorTest, _scissor);
        Toggle(CullFaceEnable, _cull);
    }
}
#endif
