#if MPHREAD_AVALONIA && !ANDROID
using System;
using Avalonia.OpenGL;
using OpenTK;
namespace MphRead.Mods.StudioRendering;
/// <summary>Loads the canonical OpenTK renderer from the current Avalonia GL context.</summary>
public static class StudioGlBindings
{
    private sealed class Bindings(GlInterface gl) : IBindingsContext
    { public IntPtr GetProcAddress(string procName)=>gl.GetProcAddress(procName); }
    public static void Initialize(GlInterface gl)
    {
        if((gl.Version ?? "").Contains("OpenGL ES",StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The current map renderer needs desktop compatibility OpenGL. CPU preview remains available.");
        MphRead.Mods.Render.GraphicsApi.LoadBindings(new Bindings(gl));
        string version=OpenTK.Graphics.OpenGL.GL.GetString(OpenTK.Graphics.OpenGL.StringName.Version)??"";
        // Apple reports e.g. "4.1 Metal ..." without the words "Core Profile".
        // Query the profile mask before attempting legacy shaders or GL state.
        bool core=version.Contains("Core Profile",StringComparison.OrdinalIgnoreCase);
        string number=version.Split(' ')[0];
        if(Version.TryParse(number,out var parsed)&&parsed>=new Version(3,2))
            core|=(OpenTK.Graphics.OpenGL.GL.GetInteger((OpenTK.Graphics.OpenGL.GetPName)0x9126)&1)!=0;
        if(core)
            throw new NotSupportedException("This toolkit provides core OpenGL; the current renderer requires a compatibility context.");
    }
}
#endif
