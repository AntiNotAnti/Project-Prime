using MphRead.Mods.Render;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    private bool _headDeathRigReady;
    internal Mods.Cosmetics.Death.DeathPresentationDefinition ModCorpseDeathDefinition=>CosmeticDeathDefinition;
    internal void ModPrewarmHeadDeathPose()
    {
        bool ready=true;
        for(int i=0;i<_bipedModelLods.Length;i++)
        {
            var model=_bipedModelLods[i];
            if(model==null){ready=false;continue;}
            var rig=CorpseRigProfile.For(model.Model);
            ready&=rig.ProceduralSupported&&rig.Hunter==Hunter&&rig.Lod==i&&HeadDeathPoseOverlay.Prewarm(model.Model);
        }
        _headDeathRigReady=ready;
    }
    private bool ModClassicHeadDeathVisible=>_headDeathRigReady&&CosmeticDeathDefinition.WireId==0
        &&_scene.ModHeadDeathPose(this,CosmeticDeathDefinition).Active;
}
