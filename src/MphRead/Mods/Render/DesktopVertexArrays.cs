#if !MPHREAD_SERVER && !ANDROID
using NativeGL = OpenTK.Graphics.OpenGL.GL;

namespace MphRead.Mods.Render;

// APPLE and core/ARB names are distinct entry points, even though the binding
// query token is shared. Keep each object's dispatch with its owning geometry.
internal static class DesktopVertexArrays
{
    internal static int Create(OpenGlVertexArrayApi api) => api == OpenGlVertexArrayApi.Apple
        ? NativeGL.Apple.GenVertexArray() : NativeGL.GenVertexArray();
    internal static void Bind(OpenGlVertexArrayApi api, int id)
    {
        if (api == OpenGlVertexArrayApi.Apple) NativeGL.Apple.BindVertexArray(id);
        else NativeGL.BindVertexArray(id);
    }
    internal static void Delete(OpenGlVertexArrayApi api, int id)
    {
        if (api == OpenGlVertexArrayApi.Apple) NativeGL.Apple.DeleteVertexArray(id);
        else NativeGL.DeleteVertexArray(id);
    }
}
#endif
