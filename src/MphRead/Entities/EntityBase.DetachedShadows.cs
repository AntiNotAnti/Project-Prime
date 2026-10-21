using OpenTK.Mathematics;

namespace MphRead.Entities;

public abstract partial class EntityBase
{
    protected Model? PrepareDetachedPlatformShadow(int index, Matrix4 transform, bool submit = true)
    {
        ModelInstance source = _models[index];
        if (!source.Active || source.IsPlaceholder
            || !_scene.TryPrepareDetachedShadow(this,source,out var pose)) return null;
        Model model = pose.Instance.Model;
        model.AnimateMaterials(source.AnimInfo); model.AnimateTextures(source.AnimInfo);
        model.ComputeNodeMatrices(0);
        model.AnimateNodes(0,UseNodeTransform || _scene.TransformRoomNodes,
            transform,model.Scale,source.AnimInfo);
        model.UpdateMatrixStack();
        _scene.UpdateDetachedShadowMaterials(source.Model,model,GetModelRecolor(source,index));
        if (submit)
            using (_scene.BeginDetachedShadowSubmission(pose))
                GetDrawItems(pose.Instance,index,detachedSource: source);
        return model;
    }
}
