#if !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Replay;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // One bounded RGB atlas belongs to this scene and is released with its
    // replay output. It contains no authoritative or camera-editor state.
    private int _replayAuxiliaryFramebuffer, _replayAuxiliaryTexture;
    private Vector4i _replayAuxiliaryBounds;
    private Vector2i _replayAuxiliaryWindow;
    private int _replayAuxiliarySubject, _replayAuxiliaryTarget;
    private long _replayAuxiliaryCompleted;
    private bool _replayAuxiliaryValid;

    private bool TryPreviewReplayAuxiliaryCache(Vector4i bounds, Vector2i framebuffer,
        int subject, int target, bool interacting, long now)
    {
        if (!interacting) _replayAuxiliaryValid = false;
        bool valid = _replayAuxiliaryValid && _replayAuxiliaryFramebuffer != 0 && _replayAuxiliaryBounds == bounds
            && _replayAuxiliaryWindow == framebuffer && _replayAuxiliarySubject == subject
            && _replayAuxiliaryTarget == target;
        if (!ReplayAuxiliaryPreviewPolicy.Reuse(interacting, valid, now, _replayAuxiliaryCompleted)) return false;
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _replayAuxiliaryFramebuffer);
        GL.ReadBuffer(ReadBufferMode.ColorAttachment0);
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        GL.Disable(EnableCap.ScissorTest);
        int left = bounds.Z / 2, top = bounds.W / 2;
        int bottom = framebuffer.Y - bounds.Y - bounds.W;
        // Copy only the other three tiles; the main tile just received the
        // current completed primary image and must never be overwritten.
        GL.BlitFramebuffer(left, bounds.W - top, bounds.Z, bounds.W,
            bounds.X + left, bottom + bounds.W - top, bounds.X + bounds.Z, bottom + bounds.W,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        GL.BlitFramebuffer(0, 0, bounds.Z, bounds.W - top,
            bounds.X, bottom, bounds.X + bounds.Z, bottom + bounds.W - top,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        GL.Viewport(0, 0, framebuffer.X, framebuffer.Y);
        return true;
    }

    private void StoreReplayAuxiliaryCache(Vector4i bounds, Vector2i framebuffer,
        int subject, int target, long completed)
    {
        // Large preview windows keep the normal safe path instead of silently
        // allocating an unbounded extra GPU target.
        if (bounds.Z > 3840 || bounds.W > 2160) return;
        if (_replayAuxiliaryFramebuffer != 0
            && (_replayAuxiliaryBounds.Z != bounds.Z || _replayAuxiliaryBounds.W != bounds.W))
            ReleaseReplayAuxiliaryCache();
        try
        {
            if (_replayAuxiliaryFramebuffer == 0)
            {
                if (!CanAllocateTargetBytes((long)bounds.Z * bounds.W * 4)
                    || bounds.Z > GL.GetInteger(GetPName.MaxTextureSize)
                    || bounds.W > GL.GetInteger(GetPName.MaxTextureSize)) return;
                _replayAuxiliaryTexture = GL.GenTexture();
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, _replayAuxiliaryTexture);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb, bounds.Z, bounds.W,
                    0, PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
                GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
                _replayAuxiliaryFramebuffer = GL.GenFramebuffer();
                GL.BindFramebuffer(FramebufferTarget.Framebuffer, _replayAuxiliaryFramebuffer);
                GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                    TextureTarget.Texture2D, _replayAuxiliaryTexture, 0);
                ValidateFramebuffer("replay auxiliary preview cache");
            }
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
            GL.ReadBuffer(ReadBufferMode.Back);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _replayAuxiliaryFramebuffer);
            GL.Disable(EnableCap.ScissorTest);
            int bottom = framebuffer.Y - bounds.Y - bounds.W;
            GL.BlitFramebuffer(bounds.X, bottom, bounds.X + bounds.Z, bottom + bounds.W,
                0, 0, bounds.Z, bounds.W, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
            _replayAuxiliaryBounds = bounds;
            _replayAuxiliaryWindow = framebuffer;
            _replayAuxiliarySubject = subject;
            _replayAuxiliaryTarget = target;
            _replayAuxiliaryCompleted = completed;
            _replayAuxiliaryValid = true;
        }
        catch
        {
            ReleaseReplayAuxiliaryCache();
            throw;
        }
        finally
        {
            // PopAttrib in the enclosing auxiliary scope restores default
            // back-buffer state, so leave our private FBO before that restore.
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, framebuffer.X, framebuffer.Y);
        }
    }

    private void ReleaseReplayAuxiliaryCache()
    {
        if (_replayAuxiliaryFramebuffer != 0) GL.DeleteFramebuffer(_replayAuxiliaryFramebuffer);
        if (_replayAuxiliaryTexture != 0) GL.DeleteTexture(_replayAuxiliaryTexture);
        _replayAuxiliaryFramebuffer = _replayAuxiliaryTexture = 0;
        _replayAuxiliaryCompleted = 0;
        _replayAuxiliaryValid = false;
    }
}
#endif
