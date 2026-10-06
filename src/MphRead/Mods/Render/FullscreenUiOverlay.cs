#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;
using G = MphRead.Mods.Render.GraphicsApi;

namespace MphRead.Mods.Render;

// Shared CPU-raster upload and premultiplied fullscreen composition. Android's
// modern path calls only the facade; no EGL context or GLES shader is involved.
// Legacy Android retains its ES3 shader because its facade has no matrix stack.
internal sealed class FullscreenUiOverlay
{
    private static readonly object DesktopContextOwner = new();
    private object? _owner;
    private int _texture, _width, _height;
    internal bool HasFrame { get; private set; }
    internal object? ResourceOwner => _owner;

    private static object CurrentOwner(bool allowRecovery)
    {
        if (!ModernGraphicsCompat.Active) return DesktopContextOwner;
        if (allowRecovery) _ = ModernGraphicsCompat.DeviceGeneration;
        return ModernGraphicsCompat.UiResourceOwner
            ?? throw new InvalidOperationException("UI overlay has no active graphics owner.");
    }

    private void EnsureOwner()
    {
        object owner = CurrentOwner(allowRecovery: true);
        if (ReferenceEquals(_owner, owner)) return;
        // The former owner releases its native resources. Deleting its numeric
        // ID against a new registry/context could instead delete somebody else.
        Forget();
        _owner = owner;
    }

    internal bool Upload(byte[] pixels, int width, int height)
    {
        if (!UiOverlayRendererState.HasCompletePixels(width, height, pixels.Length)) return false;
        unsafe
        {
            fixed (byte* pointer = pixels) return Upload((nint)pointer, width, height);
        }
    }

    internal bool Upload(nint pixels, int width, int height)
    {
        if (pixels == 0 || !UiOverlayRendererState.HasCompletePixels(width, height, int.MaxValue)) return false;
        EnsureOwner();
        int active = G.GetInteger(GetPName.ActiveTexture);
        int unpack = G.GetInteger(GetPName.UnpackAlignment);
        G.ActiveTexture(TextureUnit.Texture0);
        int binding = G.GetInteger(GetPName.TextureBinding2D);
        try
        {
            if (_texture == 0)
            {
                _texture = G.GenTexture();
                G.BindTexture(TextureTarget.Texture2D, _texture);
                G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
                G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
                G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
                G.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            }
            else G.BindTexture(TextureTarget.Texture2D, _texture);
            G.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
            if (width != _width || height != _height)
                G.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            else
                G.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, width, height,
                    PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
            _width = width;
            _height = height;
            HasFrame = true;
            return true;
        }
        finally
        {
            G.BindTexture(TextureTarget.Texture2D, binding);
            G.PixelStore(PixelStoreParameter.UnpackAlignment, unpack);
            G.ActiveTexture((TextureUnit)active);
        }
    }

    internal void Draw(int width, int height)
    {
#if ANDROID
        if (!ModernGraphicsCompat.Active)
            throw new InvalidOperationException("Fullscreen facade composition requires the modern Android renderer.");
#endif
        EnsureOwner();
        if (!HasFrame || _texture == 0 || width <= 0 || height <= 0) return;
        bool modern = ModernGraphicsCompat.Active;
        var modernState = modern ? ModernGraphicsCompat.CaptureUiOverlayState() : default;
        int program = modern ? 0 : G.GetInteger(GetPName.CurrentProgram);
        int framebuffer = modern ? 0 : G.GetInteger(GetPName.DrawFramebufferBinding);
        var matrix = modern ? MatrixMode.Modelview
            : (MatrixMode)G.GetInteger(GetPName.MatrixMode);
        if (!modern) G.PushAttrib(AttribMask.AllAttribBits);
        try
        {
            G.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
            G.Viewport(0, 0, width, height);
            G.UseProgram(0);
            G.Disable(EnableCap.DepthTest);
            G.Disable(EnableCap.CullFace);
            G.Disable(EnableCap.AlphaTest);
            G.Disable(EnableCap.StencilTest);
            G.Disable(EnableCap.ScissorTest);
            G.DepthMask(false);
            G.ColorMask(true, true, true, true);
            G.PolygonMode(TriangleFace.FrontAndBack, OpenTK.Graphics.OpenGL.PolygonMode.Fill);
            G.Enable(EnableCap.Blend);
            G.BlendEquation(BlendEquationMode.FuncAdd);
            G.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
            G.ActiveTexture(TextureUnit.Texture1);
            G.BindTexture(TextureTarget.Texture2D, 0);
            G.Disable(EnableCap.Texture2D);
            G.ActiveTexture(TextureUnit.Texture0);
            G.Enable(EnableCap.Texture2D);
            G.BindTexture(TextureTarget.Texture2D, _texture);
            G.TexEnv(TextureEnvTarget.TextureEnv, TextureEnvParameter.TextureEnvMode, (int)TextureEnvMode.Replace);
            G.Color4(1f, 1f, 1f, 1f);
            G.MatrixMode(MatrixMode.Projection);
            G.PushMatrix();
            try
            {
                G.LoadIdentity();
                G.MatrixMode(MatrixMode.Modelview);
                G.PushMatrix();
                try
                {
                    G.LoadIdentity();
                    // CPU row zero is the top. Flip the UV, never the raster.
                    G.Begin(PrimitiveType.TriangleStrip);
                    G.TexCoord2(1f, 0f); G.Vertex3(1f, 1f, 0f);
                    G.TexCoord2(0f, 0f); G.Vertex3(-1f, 1f, 0f);
                    G.TexCoord2(1f, 1f); G.Vertex3(1f, -1f, 0f);
                    G.TexCoord2(0f, 1f); G.Vertex3(-1f, -1f, 0f);
                    G.End();
                }
                finally { G.MatrixMode(MatrixMode.Modelview); G.PopMatrix(); }
            }
            finally { G.MatrixMode(MatrixMode.Projection); G.PopMatrix(); }
        }
        finally
        {
            if (modern) modernState.Dispose();
            else
            {
                G.MatrixMode(matrix);
                G.UseProgram(program);
                G.BindFramebuffer(FramebufferTarget.DrawFramebuffer, framebuffer);
                G.PopAttrib();
            }
        }
    }

    internal void Release(bool nativeResourcesAvailable = true)
    {
        try
        {
            if (_texture != 0 && nativeResourcesAvailable
                && (!ModernGraphicsCompat.Active || ModernGraphicsCompat.UiNativeResourcesAvailable)
                && ReferenceEquals(_owner, CurrentOwner(allowRecovery: false)))
                G.DeleteTexture(_texture);
        }
        finally { Forget(); }
    }

    internal void Forget()
    {
        _texture = _width = _height = 0;
        _owner = null;
        HasFrame = false;
    }
}
#endif
