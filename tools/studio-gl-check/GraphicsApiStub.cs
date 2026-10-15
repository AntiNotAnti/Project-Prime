using System;
using OpenTK;
using OpenTK.Graphics.OpenGL;

// The transport-only project deliberately does not compile the game engine.
// These test stubs observe the helper's canonical lifecycle calls; full facade
// cache/capability behavior is covered by the Studio/native integration checks.
namespace MphRead.Mods.Render;

internal static class GraphicsApi
{
    internal static int Loads;
    internal static bool FailNextLoad;
    internal static void LoadBindings(IBindingsContext context)
    {
        if (FailNextLoad)
        {
            FailNextLoad = false;
            throw new InvalidOperationException("Injected binding initialization failure.");
        }
        if (context.GetProcAddress("glBindFramebuffer") == IntPtr.Zero)
            throw new Exception("Native CGL resolver did not provide framebuffer entry points.");
        OpenTK.Graphics.OpenGL.GL.LoadBindings(context);
        if (!(GL.GetString(StringName.Version) ?? "").StartsWith("2.1", StringComparison.Ordinal))
            throw new Exception("Canonical bindings were initialized outside the legacy context.");
        Loads++;
    }
}

internal static class OpenGlGpuProfiler
{
    internal static int Releases;
    internal static void ReleaseCurrentContext()
    {
        if (!(GL.GetString(StringName.Version) ?? "").StartsWith("2.1", StringComparison.Ordinal))
            throw new Exception("Profiler release ran outside the legacy context.");
        Releases++;
    }
}
