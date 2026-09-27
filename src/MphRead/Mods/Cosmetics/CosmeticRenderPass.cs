using MphRead.Mods.Cosmetics;
namespace MphRead
{
    public partial class Scene
    {
        internal CosmeticSurface CosmeticSubmission { get; set; }
        private CosmeticUniforms? _cosmeticUniforms;
        private void InitCosmeticUniforms() => _cosmeticUniforms = new(_shaderProgramId);
        private void ApplyCosmeticUniforms(CosmeticSurface surface) => _cosmeticUniforms?.Apply(surface);
    }
}
