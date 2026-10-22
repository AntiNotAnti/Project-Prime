using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private CharacterSourceDroneOwner? _sourceDrone;
    internal CharacterSourceDroneOwner? ModSourceDrone => _sourceDrone;
    internal Model? ModSourceNativeDrone => _droneModel?.Model;
    internal void ModBindSourceDrone(CharacterSourceDroneSelection selection)
    {
        ModRetireSourceDrone();_sourceDrone=new(_scene,this,selection);
    }
    private void ModRetireSourceDrone(){_sourceDrone?.Dispose();_sourceDrone=null;ModRetireSourceArsenalDrone();}
    // Presentation reads existing native clocks; it never owns attack or damage.
    // Native Weavel flash remains complete fallback at ages0..3. Source hit
    // tail ends before72, matching the original float1.2f duration boundary.
    private bool SourceDroneHitTail => _timeSinceDamage is >=4 and <72;
    internal int ModSourceDroneState => _droneLaserFrame>=0?4:SourceDroneHitTail?11:_droneFireState;
    internal int ModSourceDroneFrame => _droneLaserFrame>=0?_droneLaserFrame:SourceDroneHitTail?_timeSinceDamage:_droneFireFrame;
    private bool SourceDroneStateSupported => SourcePresentationStateSupported&&ScoutDroneEnabled
        &&IsAltForm&&!IsMorphing&&!IsUnmorphing
        // Scout Drone clears Morphing immediately; its native camera still
        // moves from first to third person over the independent handoff.
        &&_camSwitchTimer>=Values.CamSwitchTime*2
        &&CharacterSourceDronePose.Supports(ModSourceDroneState,ModSourceDroneFrame);
    private bool ModTryDrawSourceDrone(Vector3 drawPosition,Vector3 drawFacing,Vector4? color,Vector4? outline)
    {
        if((_sourceDrone==null&&_sourceArsenalDrone==null)||!SourceDroneStateSupported||color!=null||outline!=null
            ||_curAlpha!=1||!RenderOptions.ShowCustomCosmetics)return false;
        // Source-only clamped aim from SecondHunt DroneBody (MIT, NoneGiven 2020).
        // Keep the native fallback transform and gameplay aim untouched.
        Vector3 lateral=NormalizeSpatialOr(new Vector3(drawFacing.X,0,drawFacing.Z),Vector3.UnitZ);
        float sine=Math.Clamp(drawFacing.Y,-Fixed.ToFloat(2896),Fixed.ToFloat(2896));
        Vector3 facing=lateral*MathF.Sqrt(1-sine*sine)+Vector3.UnitY*sine;
        Matrix4 nativeFacing=GetTransformMatrix(facing,Vector3.UnitY,drawPosition.AddY(Fixed.ToFloat(Values.AltColYPos)));
        CharacterWeightedRenderModel model;ICharacterSourcePresentationOwner owner;double seconds;Matrix4 presentation;
        if(_sourceArsenalDrone is { } arsenal)
        {
            if(!arsenal.Prepare(nativeFacing,out presentation))return false;
            model=arsenal.Model;owner=arsenal;seconds=arsenal.MaterialSeconds;
        }
        else if(_sourceDrone is { } solo)
        {
            if(!solo.Prepare(nativeFacing,out presentation))return false;
            model=solo.Model;owner=solo;seconds=solo.MaterialSeconds;
        }
        else return false;
        int polygonId=_scene.GetNextPolygonId();var previous=_scene.SourceGxSubmission;
        try
        {
            foreach(var segment in model.Segments)
            {
                _scene.SourceGxSubmission=new(segment.SourceGx!,seconds,owner,AlternateForm:true);
                _scene.AddRenderItem(_droneModel!.Model.Materials[segment.NativeMaterialIndex],polygonId,1,Vector3.Zero,
                    GetLightInfo(),Matrix4.Identity,presentation,segment.ListId,segment.SourceJointPalette!.Count,
                    segment.SourcePresentationPalette!,null,null,SelectionType.None,BillboardMode.None,1,segment.AlbedoBinding,
                    weightedSkinning:true,authoredTexture:true,authoredWrapS:segment.WrapS,authoredWrapT:segment.WrapT,
                    authoredDoubleSided:true,authoredTransparent:segment.SourceGx!.Program.DrawPass!=CharacterSourceGxProgram.Pass.Opaque);
            }
        }
        finally{_scene.SourceGxSubmission=previous;}
        return true;
    }
}
