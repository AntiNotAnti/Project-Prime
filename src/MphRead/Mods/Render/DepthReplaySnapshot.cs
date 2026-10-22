#if !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

// Scene-owned, single-sample D24S8 storage. Only depth is copied: the mask pass's
// current stencil winners and scene color are never restored from the snapshot.
internal sealed class DepthReplaySnapshot : IDisposable
{
    private const long MaxBytes = 64L * 1024 * 1024;
    private int _framebuffer, _depth, _width, _height, _generation = -1;
    private bool _valid, _failed;
    private long _bytes;
    [ThreadStatic] private static long _residentBytes;
    internal static long ResidentBytes => _residentBytes;
    internal static void ResetContext() => _residentBytes = 0;

    private void ObserveContext()
    {
        if (_generation == OpenGlContextPolicy.Generation) return;
        // Names belong to the old context; deleting them in the new one could
        // delete unrelated objects with recycled names.
        _framebuffer = _depth = _width = _height = 0;
        _bytes = 0; _valid = _failed = false;
        _generation = OpenGlContextPolicy.Generation;
    }

    internal bool Capture(int source, int width, int height)
    {
        ObserveContext();
        _valid = false;
        long bytes = (long)width * height * 4;
        if (_failed || source <= 0 || width <= 0 || height <= 0 || bytes > MaxBytes
            || !OpenGlContextPolicy.Current.FramebufferBlit) return false;
        if (_width != width || _height != height || _framebuffer == 0)
        {
            Release();
            int previousRenderbuffer = GraphicsApi.GetInteger(GetPName.RenderbufferBinding);
            try
            {
                _framebuffer = GraphicsApi.GenFramebuffer();
                if (_framebuffer != 0) OpenGlFailureInjection.Hit("depth-snapshot-after-framebuffer");
                _depth = GraphicsApi.GenRenderbuffer();
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, _framebuffer);
                GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, _depth);
                GraphicsApi.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                    RenderbufferStorage.Depth24Stencil8, width, height);
                GraphicsApi.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                    FramebufferAttachment.DepthStencilAttachment, RenderbufferTarget.Renderbuffer, _depth);
                GraphicsApi.DrawBuffer(DrawBufferMode.None);
                GraphicsApi.ReadBuffer(ReadBufferMode.None);
                if (GraphicsApi.CheckFramebufferStatus(FramebufferTarget.Framebuffer)
                    != FramebufferErrorCode.FramebufferComplete)
                {
                    Release(); _failed = true; return false;
                }
                _width = width; _height = height; _bytes = bytes;
                _residentBytes += bytes;
            }
            catch (OpenGlInjectedFailureException)
            {
                Release(); _failed = true; return false;
            }
            finally
            {
                GraphicsApi.BindRenderbuffer(RenderbufferTarget.Renderbuffer, previousRenderbuffer);
                GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, source);
            }
        }
        Copy(source, _framebuffer, source);
        _valid = true;
        OpenGlPassProfiler.Count(OpenGlFrameCounter.DepthSnapshotCopies);
        return true;
    }

    internal bool Restore(int destination, int width, int height)
    {
        if (!_valid || _generation != OpenGlContextPolicy.Generation
            || width != _width || height != _height || destination <= 0) return false;
        _valid = false;
        Copy(_framebuffer, destination, destination);
        OpenGlPassProfiler.Count(OpenGlFrameCounter.DepthSnapshotCopies);
        return true;
    }

    private void Copy(int read, int draw, int restore)
    {
        GraphicsApi.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read);
        GraphicsApi.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw);
        try
        {
            // Caller owns the scene's single-sample D24S8 framebuffer and has
            // disabled scissor. Identical formats/dimensions, no scaling/filtering.
            GraphicsApi.BlitFramebuffer(0, 0, _width, _height, 0, 0, _width, _height,
                ClearBufferMask.DepthBufferBit, BlitFramebufferFilter.Nearest);
        }
        finally { GraphicsApi.BindFramebuffer(FramebufferTarget.Framebuffer, restore); }
    }

    internal void End() => _valid = false;
    private void Release()
    {
        if (_framebuffer != 0) GraphicsApi.DeleteFramebuffer(_framebuffer);
        if (_depth != 0) GraphicsApi.DeleteRenderbuffer(_depth);
        _residentBytes -= _bytes;
        _framebuffer = _depth = _width = _height = 0; _bytes = 0; _valid = false;
    }
    public void Dispose()
    {
        if (_generation == OpenGlContextPolicy.Generation) Release();
        else { _framebuffer = _depth = 0; _bytes = 0; _valid = false; }
    }
}
#endif
