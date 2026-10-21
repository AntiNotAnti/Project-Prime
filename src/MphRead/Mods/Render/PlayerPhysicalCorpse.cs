#if !MPHREAD_SERVER
using System;
using MphRead.CorpsePhysics;
using MphRead.Formats;
using MphRead.Mods;
using MphRead.Mods.Multiplayer;
using MphRead.Mods.Render;
using OpenTK.Mathematics;
using NM=System.Numerics.Matrix4x4;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    // Do not evaluate ModCharacterGameplay/ScoutDroneEnabled here: those
    // getters may retire actual gameplay. Cosmetic exclusion is a raw read.
    internal bool ModNativePhysicalCorpseCandidate=>_modCharacterGameplay.Variant==CharacterGameplayVariant.Native
        &&_standingEntCol==null&&_headDeathRigReady&&_bipedModelLods.Length==2
        &&!RenderOptions.CharacterModelReplacements;
    internal bool ModPhysicalCorpseModelsMatch(NativeCorpseCapture data)
        =>data.Palettes.Length==2&&ReferenceEquals(data.Palettes[0].Model,_bipedModelLods[0].Model)
            &&ReferenceEquals(data.Palettes[1].Model,_bipedModelLods[1].Model);
    internal bool ModCapturePhysicalCorpse(Model selected,HeadDeathPose pose,out NativeCorpseCapture? capture)
    {
        capture=null;
        if(!ModNativePhysicalCorpseCandidate||!_scene.ModHeadDeathAnchor(this,out var anchor,out var facing))return false;
        int selectedLod=ReferenceEquals(selected,_bipedModelLods[0].Model)?0:ReferenceEquals(selected,_bipedModelLods[1].Model)?1:-1;
        if(selectedLod<0||ReferenceEquals(_bipedModelLods[0].Model,_bipedModelLods[1].Model))return false;
        var matrices=new NM[2][];var maps=new byte[2][];
        var world=ModBuildBipedDrawTransform(anchor,facing,new Vector3(_field70,0,_field74),Metadata.HunterScales[Hunter],
            Fixed.ToFloat(Values.MinPickupHeight),out float pitch);
        AnimationSamples.Sample? lower=TryHunterNodeSample(_bipedModel1,out var l)?l:null;
        AnimationSamples.Sample? upper=TryHunterNodeSample(_bipedModel2,out var u)?u:null;
        for(int lod=0;lod<2;lod++)
        {
            var model=_bipedModelLods[lod].Model;var known=CorpseRigProfile.For(model);
            if(!known.ProceduralSupported||!known.WeightedHead||known.Hunter!=Hunter||known.Lod!=lod)return false;
            var names=new string[model.Nodes.Count];var parents=new int[names.Length];maps[lod]=new byte[names.Length];
            for(int i=0;i<names.Length;i++){names[i]=model.Nodes[i].Name;parents[i]=model.Nodes[i].ParentIndex;}
            if(!CorpseHumanoidMap.TryMap(names,parents,maps[lod]))return false;
            matrices[lod]=new NM[names.Length];
            void Copy(){for(int i=0;i<names.Length;i++)matrices[lod][i]=CorpseMatrixConversion.Copy(model.Nodes[i].Animation);}
            if(lod==selectedLod)Copy(); // Already inside the actual CW-08 scope.
            else
            {
                var state=new NativeCorpseModelState(model);
                try
                {
                    UpdateHunterBipedVisual(model,model.GetNodeByName("Spine_1")!,world,pitch,lower,upper,ModCrouchVisualAmount);
                    using var fallback=HeadDeathPoseOverlay.Begin(model,pose);Copy();
                }
                finally{state.Restore();}
            }
        }
        var profile=NativeCorpseCalibration.For(selected,Hunter);
        if(profile==null||!profile.TryCapture(matrices[selectedLod],CorpseMatrixConversion.Copy(anchor),out var fitted))return false;
        var palettes=new NativeCorpsePalette[2];
        for(int lod=0;lod<2;lod++)
        {
            if(!CorpseAffineBinding.TryCapture(matrices[lod],maps[lod],fitted!.InitialBodies,out var binding))return false;
            palettes[lod]=new(_bipedModelLods[lod].Model,binding!);
        }
        capture=new(fitted!,palettes);return true;
    }
}
#endif
