#if !ANDROID && !MPHREAD_SERVER
using System;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render;

/// <summary>Compatibility-context menu pass boundary; no resources or heap allocation.</summary>
internal readonly struct MenuGraphicsStateScope : IDisposable
{
    private readonly int _program, _active, _clientActive, _matrix, _draw, _read;

    public MenuGraphicsStateScope()
    {
        _program = GL.GetInteger(GetPName.CurrentProgram);
        _active = GL.GetInteger(GetPName.ActiveTexture);
        _clientActive = GL.GetInteger(GetPName.ClientActiveTexture);
        _matrix = GL.GetInteger(GetPName.MatrixMode);
        _draw = GL.GetInteger(GetPName.DrawFramebufferBinding);
        _read = GL.GetInteger(GetPName.ReadFramebufferBinding);
        GL.PushAttrib(AttribMask.AllAttribBits);
        // Texture creation changes pixel-unpack state on its first frame.
        OpenTK.Graphics.OpenGL.GL.PushClientAttrib(ClientAttribMask.ClientAllAttribBits);
        GL.MatrixMode(MatrixMode.Projection); GL.PushMatrix();
        GL.MatrixMode(MatrixMode.Modelview); GL.PushMatrix();
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.MatrixMode(MatrixMode.Texture); GL.PushMatrix();
        GL.MatrixMode((MatrixMode)_matrix);
    }

    public void Dispose()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.MatrixMode(MatrixMode.Texture); GL.PopMatrix();
        GL.MatrixMode(MatrixMode.Modelview); GL.PopMatrix();
        GL.MatrixMode(MatrixMode.Projection); GL.PopMatrix();
        OpenTK.Graphics.OpenGL.GL.PopClientAttrib();
        GL.PopAttrib();
        GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _draw);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _read);
        GL.UseProgram(_program);
        GL.ActiveTexture((TextureUnit)_active);
        OpenTK.Graphics.OpenGL.GL.ClientActiveTexture((TextureUnit)_clientActive);
        GL.MatrixMode((MatrixMode)_matrix);
    }
}
#endif
