using MphRead.Mods.Render;
namespace MphRead.Entities;
public partial class PlayerEntity
{
    internal void ModPrewarmHeadHitPose()
    {
        foreach(var model in _bipedModelLods)if(model!=null)HeadHitPoseOverlay.Prewarm(model.Model);
    }
}
