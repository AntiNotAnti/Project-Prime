using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // A separate composite target preserves native export dimensions, including
    // HUD, even when the window or Android surface is smaller than the movie.
    private int _replayOutputFramebuffer, _replayOutputTexture;
    private Vector2i _replayOutputSize;
#if !ANDROID && !MPHREAD_SERVER
    private bool _replayPrimaryComplete;
    private double _replayPrimaryFrame;
    private Matrix4 _replayPrimaryView, _replayPrimaryProjection;
#endif
    internal Vector2i ReplayPreviewSize { get; set; }
    internal Vector4i? ReplayPreviewBounds { get; set; }
    private bool ExportingReplay => Mods.Network.DemoPlayback.Owns(this) && Mods.Replay.ReplayVideoExporter.Rendering;
    private int ReplayOutputFramebuffer()
    {
        if (!DsTopPass && !ExportingReplay && !ReplayPreviewBounds.HasValue)
        {
            if (!UseDsLayout) ReleaseReplayOutput();
            return 0;
        }
        if (_replayOutputFramebuffer != 0 && _replayOutputSize == Size) return _replayOutputFramebuffer;
        ReleaseReplayOutput();
        _replayOutputSize = Size;
        _replayOutputTexture = GL.GenTexture();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _replayOutputTexture);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, Size.X, Size.Y, 0,
            PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        _replayOutputFramebuffer = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, _replayOutputFramebuffer);
        GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, _replayOutputTexture, 0);
        ValidateFramebuffer("replay export");
        return _replayOutputFramebuffer;
    }
    private void PreviewReplayOutput()
    {
        if ((!DsTopPass && !ExportingReplay && !ReplayPreviewBounds.HasValue) || _replayOutputFramebuffer == 0) return;
#if !ANDROID && !MPHREAD_SERVER
        if (!NativeReplayMultiViewActive)
        {
            // This is the finished HUD composite, after the primary draw and
            // before editor chrome. Auxiliary renders must never renew its lease.
            _replayPrimaryComplete = true;
            _replayPrimaryFrame = ReplayPresentationFrame;
            _replayPrimaryView = _viewMatrix;
            _replayPrimaryProjection = _perspectiveMatrix;
        }
#endif
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _replayOutputFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        var previewSize = DsTopPass ? _dsWindowSize : ReplayPreviewSize;
        var bounds = DsTopPass ? Mods.Input.DsLayout.Bounds(previewSize.X, previewSize.Y).Top
            : ReplayPreviewBounds ?? new Vector4i(0, 0, previewSize.X, previewSize.Y);
        GL.ClearColor(0, 0, 0, 1);
        #if !ANDROID && !MPHREAD_SERVER
        if(!NativeReplayMultiViewPreserveCanvas)
#endif
        GL.Clear(ClearBufferMask.ColorBufferBit);
        int bottom = previewSize.Y - bounds.Y - bounds.W;
        GL.BlitFramebuffer(0, 0, Size.X, Size.Y, bounds.X, bottom, bounds.X + bounds.Z, bottom + bounds.W,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, previewSize.X, previewSize.Y);
    }
#if !ANDROID && !MPHREAD_SERVER
    /// <summary>
    /// Reuse the normal frame's finished primary picture for the first of four tiles.
    /// Must be invoked inside ReplayAuxiliaryRenderScope, which owns GL restoration.
    /// A missing/obsolete target means Studio can retain its original single view.
    /// </summary>
    private bool BlitNativeReplayPrimaryTile(Vector4i bounds, Vector2i framebuffer)
    {
        if (!_replayPrimaryComplete || _replayOutputFramebuffer == 0 || _replayOutputSize != Size
            || _replayPrimaryFrame != ReplayPresentationFrame
            || _replayPrimaryView != _viewMatrix || _replayPrimaryProjection != _perspectiveMatrix
            || Size.X <= 0 || Size.Y <= 0 || bounds.Z <= 0 || bounds.W <= 0)
            return false;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _replayOutputFramebuffer);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        int bottom = framebuffer.Y - bounds.Y - bounds.W;
        GL.BlitFramebuffer(0, 0, Size.X, Size.Y,
            bounds.X, bottom, bounds.X + bounds.Z, bottom + bounds.W,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, framebuffer.X, framebuffer.Y);
        return true;
    }
#endif
    private void ReleaseReplayOutput()
    {
#if !ANDROID && !MPHREAD_SERVER
        ReleaseReplayAuxiliaryCache();
        _replayPrimaryComplete = false;
#endif
        if (_replayOutputFramebuffer != 0) GL.DeleteFramebuffer(_replayOutputFramebuffer);
        if (_replayOutputTexture != 0) GL.DeleteTexture(_replayOutputTexture);
        _replayOutputFramebuffer = _replayOutputTexture = 0;
    }
}
