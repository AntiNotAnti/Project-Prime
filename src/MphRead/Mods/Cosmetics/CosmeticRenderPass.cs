using MphRead.Mods.Cosmetics;
namespace MphRead
{
    public partial class Scene
    {
        internal CosmeticSurface CosmeticSubmission { get; set; }
        private CosmeticUniforms? _cosmeticUniforms;
        private void InitCosmeticUniforms() => _cosmeticUniforms = new(_shaderProgramId);
        private void ApplyCosmeticUniforms(CosmeticSurface surface) => _cosmeticUniforms?.Apply(surface);

        private void ClearCosmeticSubmissionState()
        {
            // Submission state is only a short-lived bridge while an entity builds
            // its RenderItems. Never let one player's selection become the default
            // for the room or for a later entity on the next picture.
            CosmeticSubmission = default;
            CosmeticMaterialSubmission = default;
        }

        private void ResetCosmeticUniforms() => _cosmeticUniforms?.Reset();
    }
}
