using MphRead.Mods.Render;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    private bool _headDeathRigReady;
    internal void ModPrewarmHeadDeathPose()
    {
        bool ready=true;
        foreach(var model in _bipedModelLods)ready&=model!=null&&HeadDeathPoseOverlay.Prewarm(model.Model);
        _headDeathRigReady=ready;
    }
    private bool ModClassicHeadDeathVisible=>_headDeathRigReady&&CosmeticDeathDefinition.WireId==0
        &&_scene.ModHeadDeathPose(this,CosmeticDeathDefinition).Active;
}
