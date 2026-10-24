using System;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Called only inside the ordinary Draw visit when its camera path omits the
    // body. No Draw/GetDrawInfo recursion, native animation advancement, trails,
    // weapon emitters, death callbacks or collision/effect-anchor updates.
    private void CollectDetachedPlayerShadow(Vector3 position, Vector3 facing, bool drawAltForm, int lod)
    {
        if (!_scene.CanPrepareDetachedShadows || _health <= 0 || _curAlpha < .999f) return;
        // The ordinary gameplay getter can retire a stale private rule owner.
        // Shadow preparation only reads an already valid owner and never drives
        // that retirement, its drone cleanup or weapon/effect lifetimes.
        if (_modCharacterGameplay.Variant != Mods.Multiplayer.CharacterGameplayVariant.Native
            && (!ModCharacterGameplayEnabled
                || _scene.ForegroundLease.ThreadId != Environment.CurrentManagedThreadId)) return;
        bool drone = _modCharacterGameplay.UsesScoutDrone;
        ModelInstance source = drawAltForm ? drone ? _droneModel! : _altModel : _bipedModel2;
        if (source == null || !_scene.TryPrepareDetachedShadow(this,source,out var pose)) return;
        Model model = pose.Instance.Model;
        Matrix4 bipedWorld = Matrix4.Identity;
        model.ComputeNodeMatrices(0);
        if (!drawAltForm)
        {
            int spine = -1;
            for (int i = 0; i < source.Model.Nodes.Count; i++)
                if (ReferenceEquals(source.Model.Nodes[i],_spineNodes[lod])) { spine = i; break; }
            if (spine < 0) return;
            if (ModTucked) position.Y += ModTuckRise;
            Matrix4 world = ModBuildBipedDrawTransform(position,facing,new(_field70,0,_field74),
                Metadata.HunterScales[Hunter],Fixed.ToFloat(Values.MinPickupHeight),out float pitch);
            bipedWorld = world;
            AnimationSamples.Sample? lower = TryHunterNodeSample(_bipedModel1,out var l) ? l : null;
            AnimationSamples.Sample? upper = TryHunterNodeSample(_bipedModel2,out var u) ? u : null;
            UpdateHunterBipedPose(model,model.Nodes[spine],world,pitch,lower,upper,
                ModCrouchVisualAmount,ModCrouchGaitActive,pose.Neutral);
        }
        else if (drone)
        {
            Vector3 flat = NormalizeSpatialOr(new(facing.X,0,facing.Z),Vector3.UnitZ);
            Matrix4 world = Matrix4.CreateScale(.45f) * GetTransformMatrix(flat,Vector3.UnitY,
                position.AddY(Fixed.ToFloat(Values.AltColYPos)));
            model.AnimateNodes(0,UseNodeTransform,world,model.Scale,source.AnimInfo);
            model.UpdateMatrixStack();
        }
        else if (Hunter == Hunter.Kanden)
        {
            for (int i = 0; i < _kandenSegMtx.Length; i++)
                model.Nodes[i].Animation = ModGetKandenSegmentDrawPose(i,position,out _);
            model.UpdateMatrixStack();
        }
        else
        {
            Matrix4 world = ModGetAlternateFormDrawPose(position,out _);
            AnimationSamples.Sample? sampled = TryHunterNodeSample(source,out var sample) ? sample : null;
            if (Hunter == Hunter.Spire && Flags2.TestFlag(PlayerFlags2.AltAttack))
            {
                model.AnimateNodes(0,false,SpireAttackDrawRotation(),Vector3.One,source.AnimInfo,sampled);
                model.Nodes[0].Animation = world;
                for (int i = 1; i < model.Nodes.Count; i++)
                {
                    Matrix4 node = model.Nodes[i].Animation; node.Row3.Xyz += world.Row3.Xyz;
                    model.Nodes[i].Animation = node;
                }
            }
            else model.AnimateNodes(0,UseNodeTransform,world,model.Scale,source.AnimInfo,sampled);
            model.UpdateMatrixStack();
        }
        model.AnimateMaterials(source.AnimInfo); model.AnimateTextures(source.AnimInfo);
        _scene.UpdateDetachedShadowMaterials(source.Model,model,drone && drawAltForm ? 0 : Recolor);
        using (_scene.BeginDetachedShadowSubmission(pose))
        {
            using (drawAltForm ? default(ParkourPoseOverlay.Scope) : ParkourPoseOverlay.Begin(model,bipedWorld,ModGetParkourAnimationPose()))
            using (drawAltForm ? default(HeadHitPoseOverlay.Scope) : HeadHitPoseOverlay.Begin(model,_scene.ModHeadHitPose(this)))
            {
                if (drone && drawAltForm || !TryDrawModernCharacter(pose.Instance,
                    drawAltForm ? CharacterModelPart.AlternateForm : CharacterModelPart.Biped,
                    _curAlpha,lod: lod,detachedSource: source))
                    GetDrawItems(pose.Instance,model.Nodes[0],_curAlpha,detachedSource: source);
            }
        }
        if (_frozenGfxTimer > 0)
            CollectDetachedFrozenShadow(model,drawAltForm,position);
    }

    private void CollectDetachedFrozenShadow(Model body, bool alternate, Vector3 position)
    {
        ModelInstance source = alternate ? _altIceModel : _bipedIceModel;
        if (!_scene.TryPrepareDetachedShadow(this,source,out var pose)) return;
        Model model = pose.Instance.Model;
        if (alternate)
        {
            Matrix4 world = Matrix4.CreateScale(_volume.SphereRadius + .2f)
                * ModGetAlternateFormDrawPose(position,out _);
            world.Row3.Y += Fixed.ToFloat(Values.AltColYPos);
            model.ComputeNodeMatrices(0);
            model.AnimateNodes(0,UseNodeTransform,world,model.Scale,source.AnimInfo);
        }
        else
        {
            if (model.Nodes.Count > body.Nodes.Count) return;
            for (int i = 0; i < model.Nodes.Count; i++) model.Nodes[i].Animation = body.Nodes[i].Animation;
        }
        model.UpdateMatrixStack();model.AnimateMaterials(source.AnimInfo);model.AnimateTextures(source.AnimInfo);
        _scene.UpdateDetachedShadowMaterials(source.Model,model,0);
        using (_scene.BeginDetachedShadowSubmission(pose))
            GetDrawItems(pose.Instance,model.Nodes[0],1,recolor:0,detachedSource:source);
    }
}
