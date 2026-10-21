using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    private Vector2i _refusedSceneTarget;
    private bool _refusedSceneCel;

    private static long SceneTargetBytes(Vector2i size, bool cel)
        // Reserve readable-depth transition overlap, too (renderbuffer + texture).
        => checked((long)size.X * size.Y * (cel ? 16 : 12));

    private static long TargetBudgetBytes => OperatingSystem.IsAndroid()
        ? 96L * 1024 * 1024 : 384L * 1024 * 1024;

    private long EstimatedTargetBytes()
    {
        long bytes = (long)_targetSize.X * _targetSize.Y
            * ((_screenTexture != 0 ? 4 : 0) + (_celTexture != 0 ? 4 : 0)
                + (_renderBuffer != 0 ? 4 : 0) + (_depthTexture != 0 ? 4 : 0));
        bytes += (long)_graphicsOutputSize.X * _graphicsOutputSize.Y * 4;
        bytes += SpatialAntialiasingTargetBytes;
        foreach (var resolve in _supersampleResolveTargets)
            bytes += (long)resolve.Size.X * resolve.Size.Y * 4;
        bytes += (long)_shadowTargetSize * _shadowTargetSize
            * ((_shadowDepthTexture != 0 ? 4 : 0) + (_shadowColorTexture != 0 ? 4 : 0));
        foreach (FrameTransientTexture entry in _frameTransientTextures)
            bytes += (long)entry.Size.X * entry.Size.Y
                * (entry.Format == PixelInternalFormat.Rgba16f ? 8 : 4);
        return bytes;
    }

    private bool CanAllocateTargetBytes(long additional)
        => additional >= 0 && additional <= TargetBudgetBytes - EstimatedTargetBytes();

    private bool ReplaceSceneTargets(Vector2i requested)
    {
        bool cel = Mods.RenderOptions.CelShading && Mods.RenderOptions.CelEdge > 0;
        if (requested == _refusedSceneTarget && cel == _refusedSceneCel) return false;
        long budget = TargetBudgetBytes;
        long available = budget - EstimatedTargetBytes();
        Vector2i target = requested;
        long requestedBytes = SceneTargetBytes(target, cel);
        if (available < (cel ? 16 : 12) || target.X <= 0 || target.Y <= 0) return Refuse();
        if (requestedBytes > available)
        {
            double fit = Math.Sqrt(available / (double)requestedBytes);
            target = new(Math.Max(1, (int)(target.X * fit)), Math.Max(1, (int)(target.Y * fit)));
        }
        if (SceneTargetBytes(target, cel) > available) return Refuse();
        int previousDraw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int previousRead = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int framebuffer = 0, color = 0, celColor = 0, renderbuffer = 0, depth = 0;
        try
        {
            GL.ActiveTexture(TextureUnit.Texture0);
            framebuffer = GL.GenFramebuffer();
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            color = AllocateTargetColor(target, scaled: Mods.RenderOptions.ResolutionScale != 100);
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                TextureTarget.Texture2D, color, 0);
            renderbuffer = GL.GenRenderbuffer();
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, renderbuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8,
                target.X, target.Y);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthStencilAttachment,
                RenderbufferTarget.Renderbuffer, renderbuffer);
            ValidateFramebuffer("Replacement scene target");
            if (cel) celColor = AllocateTargetColor(target, scaled: false);
            Mods.Render.OpenGlFailureInjection.Hit("scene-target-before-publish");
            // Depth readability is restored by UpdateDepthAttachment before drawing.
            // Publish a complete drawable bundle; failure above leaves old storage intact.
            int oldFramebuffer = _frameBuffer;
            _frameBuffer = framebuffer; framebuffer = 0;
            int oldColor = _screenTexture; _screenTexture = color; color = oldColor;
            int oldCel = _celTexture; _celTexture = celColor; celColor = oldCel;
            int oldRenderbuffer = _renderBuffer; _renderBuffer = renderbuffer; renderbuffer = oldRenderbuffer;
            depth = _depthTexture; _depthTexture = 0;
            if (_celFrameBuffer != 0) GL.DeleteFramebuffer(_celFrameBuffer);
            _celFrameBuffer = _celFrameBufferColor = 0;
            _playerOutlineDepth = -1;
            _depthTextureRefused = false;
            _renderbufferDepthRefused = false;
            _targetSize = target;
            _sceneColorFormat = SceneColorFormat;
            FramebufferStatus = FramebufferErrorCode.FramebufferComplete;
            _graphicsOutputReady = false;
            if (oldFramebuffer != 0 && previousDraw == oldFramebuffer) previousDraw = _frameBuffer;
            if (oldFramebuffer != 0 && previousRead == oldFramebuffer) previousRead = _frameBuffer;
            if (oldFramebuffer != 0) GL.DeleteFramebuffer(oldFramebuffer);
            if (target != requested)
            {
                _refusedSceneTarget = requested; _refusedSceneCel = cel;
                Mods.DebugLog.Line("render", $"scene target budget: requested={requested}, effective={target}, peakBudget={budget}");
            }
            else _refusedSceneTarget = default;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            Mods.DebugLog.Line("render", "scene target replacement retained prior bundle: " + ex.Message);
            return Refuse();
        }
        finally
        {
            if (framebuffer != 0) GL.DeleteFramebuffer(framebuffer);
            if (renderbuffer != 0) GL.DeleteRenderbuffer(renderbuffer);
            DeleteTexture(ref color); DeleteTexture(ref celColor); DeleteTexture(ref depth);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, previousDraw);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, previousRead);
        }

        bool Refuse()
        {
            _refusedSceneTarget = requested; _refusedSceneCel = cel;
            return false;
        }
    }

    private static int AllocateTargetColor(Vector2i target, bool scaled)
    {
        int texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, SceneColorFormat, target.X, target.Y, 0,
                PixelFormat.Rgb, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                (int)(scaled ? TextureMinFilter.Linear : TextureMinFilter.Nearest));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                (int)(scaled ? TextureMagFilter.Linear : TextureMagFilter.Nearest));
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            if (GL.GetError() != ErrorCode.NoError) throw new InvalidOperationException("Scene color allocation failed.");
            return texture;
        }
        catch { GL.DeleteTexture(texture); throw; }
    }

    private void UpdateCelColorTarget()
    {
        TrimFrameTransientTextures();
        bool want = Mods.RenderOptions.CelShading && Mods.RenderOptions.CelEdge > 0;
        if (!want) { DeleteTexture(ref _celTexture); return; }
        if (_celTexture == 0) ReplaceSceneTargets(RenderSize);
    }
}
