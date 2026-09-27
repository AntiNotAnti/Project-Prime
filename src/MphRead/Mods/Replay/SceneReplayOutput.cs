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
    internal Vector2i ReplayPreviewSize { get; set; }
    internal Vector4i? ReplayPreviewBounds { get; set; }
    private bool ExportingReplay => Mods.Network.DemoPlayback.Owns(this) && Mods.Replay.ReplayVideoExporter.Rendering;
    private int ReplayOutputFramebuffer()
    {
        if (!ExportingReplay && !ReplayPreviewBounds.HasValue) { ReleaseReplayOutput(); return 0; }
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
        if ((!ExportingReplay && !ReplayPreviewBounds.HasValue) || _replayOutputFramebuffer == 0) return;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _replayOutputFramebuffer);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        var bounds = ReplayPreviewBounds ?? new Vector4i(0, 0, ReplayPreviewSize.X, ReplayPreviewSize.Y);
        GL.ClearColor(0, 0, 0, 1);
        GL.Clear(ClearBufferMask.ColorBufferBit);
        int bottom = ReplayPreviewSize.Y - bounds.Y - bounds.W;
        GL.BlitFramebuffer(0, 0, Size.X, Size.Y, bounds.X, bottom, bounds.X + bounds.Z, bottom + bounds.W,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Linear);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, ReplayPreviewSize.X, ReplayPreviewSize.Y);
    }
    private void ReleaseReplayOutput()
    {
        if (_replayOutputFramebuffer != 0) GL.DeleteFramebuffer(_replayOutputFramebuffer);
        if (_replayOutputTexture != 0) GL.DeleteTexture(_replayOutputTexture);
        _replayOutputFramebuffer = _replayOutputTexture = 0;
    }
}
