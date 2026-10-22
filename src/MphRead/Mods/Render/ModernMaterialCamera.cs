using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace MphRead;

public partial class Scene
{
    // This visual experiment has a separate opt-in: do not inherit Android's
    // production performance enrollment or migrate the Original appearance.
    internal static readonly bool ModernMaterialCorrectionsEnabled =
        Mods.Render.OpenGlOptimizationPolicy.Requested("MATERIAL_CORRECTIONS")
        && !Mods.Render.OpenGlOptimizationPolicy.ForceLegacy;

    private void SetMaterialCamera(ref Matrix4 view)
    {
        if (!ModernMaterialCorrectionsEnabled || _shaderLocations.MaterialCameraPosition < 0) return;
        // Use the actual uploaded view, including preview, replay and temporary
        // viewmodel/pause-map views, rather than assuming the gameplay eye.
        Vector3 camera = Matrix4.Invert(view).Row3.Xyz;
        GL.Uniform3(_shaderLocations.MaterialCameraPosition, camera);
    }
}
