using OpenTK.Graphics.OpenGL;

namespace MphRead.Mods.Render
{
    internal static class DepthCoveragePreparation
    {
        // Match the fragment shader alpha-only branch. Eligible procedural helpers
        // change only vertices/UVs and retain the same coverage inputs.
        internal static bool CanUse(RenderItem item, bool depthShaderActive, bool drawingOutline, bool procedural = false)
            => depthShaderActive && !drawingOutline && (item.Type == RenderItemType.Mesh
                || procedural && ProceduralStateReuse.IsProcedural(item.Type))
            && item.OverrideColor == null && item.Cosmetics.Skin == 0
            && item.Cosmetics.Effect == 0 && item.Cosmetics.Dissolve <= 0;
    }
}

namespace MphRead
{
    public partial class Scene
    {
        private static readonly bool _depthPreparationEnabled = Mods.Render.OpenGlOptimizationPolicy.Enabled("DEPTH_PREP");
        private bool _depthOnlyWorldPass;

        private void PrepareDepthCoverage(RenderItem item)
        {
            // Fragment coverage ignores RGB/companion maps only while all its
            // fallback guards are neutral. Update the cosmetic owner's cache too.
            ApplyCosmeticUniforms(default);
            GL.Uniform1(_shaderLocations.UseOverride, 0);
            GL.Uniform1(_playerOutlineMaskUniform, 0);
            // Normal-generated UVs depend on use_light even in a depth pass.
            GL.Uniform1(_shaderLocations.UseLight, (LightingOn || _editorMaterialPreview) && item.Lighting ? 1 : 0);
            GL.Uniform1(_shaderLocations.MaterialAlpha, item.Alpha);
            GL.Uniform1(_shaderLocations.MaterialMode, (int)item.PolygonMode);
            BindRenderItemBaseTexture(item);
        }
    }
}
