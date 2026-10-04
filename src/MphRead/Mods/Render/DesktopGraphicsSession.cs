#if !ANDROID
using System;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace MphRead.Mods.Render;

/// <summary>Renderer ownership for auxiliary windows (previews and replay tools).</summary>
internal sealed class DesktopGraphicsSession : IDisposable
{
    private readonly bool _modern;
    internal DesktopGraphicsSession(NativeWindow window)
    {
#if !MPHREAD_SERVER
        _modern = GraphicsBackendPolicy.ModernGameplayRequested;
        if (_modern) ModernGraphicsCompat.Initialize(window, GraphicsBackendPolicy.Resolved);
        else
#endif
        {
            window.Context.MakeCurrent();
            OpenTK.Graphics.OpenGL.GL.LoadBindings(new GLFWBindingsContext());
#if !MPHREAD_SERVER
            // This context was established through raw OpenTK rather than
            // GraphicsApi.LoadBindings, so explicitly discard any values
            // remembered from a previous compatibility context.
            GraphicsApi.ResetLegacyState();
#endif
        }
    }
    internal static void Resize(NativeWindow window)
    {
#if !MPHREAD_SERVER
        if (ModernGraphicsCompat.Active)
            ModernGraphicsCompat.Resize(window.FramebufferSize.X, window.FramebufferSize.Y);
#endif
    }
    internal static void Present(NativeWindow window)
    {
#if !MPHREAD_SERVER
        if (ModernGraphicsCompat.Active) ModernGraphicsCompat.Present();
        else
#endif
            window.Context.SwapBuffers();
    }
    public void Dispose()
    {
#if !MPHREAD_SERVER
        if (_modern) ModernGraphicsCompat.Shutdown();
#endif
    }
}
#endif
