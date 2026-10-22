using MphRead.Mods.Render.Characters;
using MphRead.Mods;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Loading boundary only: no file decode, image upload or model compilation
    // is left for the first spawn picture when a replacement pack is installed.
    internal void ModWarmCharacterPresentation()
    {
        if (Headless.Active || !RenderOptions.CharacterModelReplacements || _gunModel == null) return;
        void Warm(ModelInstance instance, CharacterModelPart part, int lod = 0, BeamType? weapon = null)
        {
            if (CharacterModelRuntime.TryGetWeighted(_scene, Hunter, part, instance.Model, out var weighted, lod, weapon))
            {
                weighted.UpdatePalette(instance.Model);
                foreach (var segment in weighted.Segments) segment.GetAlbedo(_scene, Recolor);
            }
            else if (CharacterModelRuntime.TryGetRigid(_scene, Hunter, part, instance.Model, out var rigid, lod, weapon))
                foreach (var segment in rigid.Segments) segment.GetAlbedo(_scene, Recolor);
        }
        for (int lod = 0; lod < _bipedModelLods.Length; lod++)
            Warm(_bipedModelLods[lod], CharacterModelPart.Biped, lod);
        Warm(_altModel, CharacterModelPart.AlternateForm);
        for (int weapon = 0; weapon < 9; weapon++)
        {
            Warm(_gunModel, CharacterModelPart.ViewModel, weapon: (BeamType)weapon);
            Warm(_gunModel, CharacterModelPart.ViewModelArms, weapon: (BeamType)weapon);
        }
    }
}
