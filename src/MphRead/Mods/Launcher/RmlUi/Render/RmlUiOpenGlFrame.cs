#if MPHREAD_RMLUI_GL_SMOKE && MPHREAD_SHELL && !ANDROID && !MPHREAD_SERVER
using System;
using MphRead.Mods.Launcher.RmlUi.Host;
using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Launcher.RmlUi.Render;

/// <summary>
/// One terminal OpenGL compatibility overlay within the caller's current
/// game context. Does not create or own a window, device or swapchain.
/// </summary>
public static class RmlUiOpenGlFrame
{
    public static void Draw(RmlUiHost host, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!host.Active || width <= 0 || height <= 0)
            throw new InvalidOperationException("The native RmlUi frame must belong to an active viewport.");

        // glPushAttrib/glPopAttrib in the native GL2 adapter preserve the
        // compatibility matrix, client array, scissor, depth, blend and
        // stencil *state*. Framebuffer/program bindings are not saved by
        // glPushAttrib, so preserve those here. The terminal pass may clear
        // only the default framebuffer's stencil for its clipping masks.
        int drawFramebuffer = GL.GetInteger(GetPName.DrawFramebufferBinding);
        int readFramebuffer = GL.GetInteger(GetPName.ReadFramebufferBinding);
        int program = GL.GetInteger(GetPName.CurrentProgram);
        int activeTexture = GL.GetInteger(GetPName.ActiveTexture);
        try
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.UseProgram(0);
            GL.ActiveTexture(TextureUnit.Texture0);
            host.Render(width, height);
        }
        finally
        {
            GL.ActiveTexture((TextureUnit)activeTexture);
            GL.UseProgram(program);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, drawFramebuffer);
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
        }
    }
}
#endif
