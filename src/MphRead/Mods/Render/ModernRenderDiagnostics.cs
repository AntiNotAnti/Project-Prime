#if !MPHREAD_SERVER
using System;
namespace MphRead;
public partial class Scene
{
#if !ANDROID
    internal void CapturePbrBuffers(string directory)
    {
        if (!_pbrReady) throw new InvalidOperationException("PBR capture requested without a ready G-buffer.");
        foreach (var target in new[] { ("albedo", _pbrAlbedoTexture), ("normal", _pbrNormalTexture), ("material", _pbrMaterialTexture) })
        {
            GL.BindFramebuffer(OpenTK.Graphics.OpenGL.FramebufferTarget.ReadFramebuffer, _pbrFramebuffer);
            GL.FramebufferTexture2D(OpenTK.Graphics.OpenGL.FramebufferTarget.ReadFramebuffer,
                OpenTK.Graphics.OpenGL.FramebufferAttachment.ColorAttachment0,
                OpenTK.Graphics.OpenGL.TextureTarget.Texture2D, target.Item2, 0);
            byte[] pixels = new byte[_targetSize.X * _targetSize.Y * 4];
            GL.ReadPixels(0, 0, _targetSize.X, _targetSize.Y,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba, OpenTK.Graphics.OpenGL.PixelType.UnsignedByte, pixels);
            using var output = System.IO.File.Create(System.IO.Path.Combine(directory, "pbr-" + target.Item1 + ".png"));
            ReFuel.Stb.StbImage.FlipVerticallyOnSave = true;
            ReFuel.Stb.StbImage.WritePng<byte>(pixels, _targetSize.X, _targetSize.Y, ReFuel.Stb.StbiImageFormat.Rgba, output);
        }
        GL.BindFramebuffer(OpenTK.Graphics.OpenGL.FramebufferTarget.ReadFramebuffer, 0);
    }
#endif
    internal bool ValidateModernAdvancedRendering()
    {
        if (_shadowRefused || _pbrRefused || _graphicsPipelineRefused || _graphicsHdrRefused)
            throw new InvalidOperationException($"Advanced rendering refused: shadow={_shadowRefused} pbr={_pbrRefused} post={_graphicsPipelineRefused} hdr={_graphicsHdrRefused}");
        return _shadowReady && _pbrReady && _graphicsOutputReady && _graphicsOutputHdr && _graphicsHistoryValid;
    }
}
#endif
