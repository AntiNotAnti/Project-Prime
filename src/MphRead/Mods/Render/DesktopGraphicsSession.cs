#if !ANDROID
using System;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Render;

/// <summary>
/// OpenGL context ownership for game-adjacent windows such as previews and
/// replay tools. No modern device or NoAPI window is created on this path.
/// </summary>
internal sealed class DesktopGraphicsSession : IDisposable
{
    internal DesktopGraphicsSession(NativeWindow window)
    {
        window.Context.MakeCurrent();
        OpenTK.Graphics.OpenGL.GL.LoadBindings(new GLFWBindingsContext());
#if !MPHREAD_SERVER
        GraphicsApi.ResetLegacyState();
#endif
    }

    // The Scene owns OpenGL framebuffer resizing. The old WebGPU surface
    // resize path is intentionally gone.
    internal static void Resize(NativeWindow window) { }

    internal static void Present(NativeWindow window) => window.Context.SwapBuffers();

    public void Dispose() { }
}
#endif
