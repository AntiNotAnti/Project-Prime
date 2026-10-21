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
    private void ModRetireSourceDrone(){_sourceDrone?.Dispose();_sourceDrone=null;}
    private bool SourceDroneStateSupported => SourcePresentationStateSupported&&ScoutDroneEnabled
        &&IsAltForm&&!IsMorphing&&!IsUnmorphing&&_droneFireState==0&&_droneLaserFrame<0;
    private bool ModTryDrawSourceDrone(Vector3 drawPosition,Vector3 drawFacing,Vector4? color,Vector4? outline)
    {
        if(_sourceDrone is not { } owner||!SourceDroneStateSupported||color!=null||outline!=null
            ||_curAlpha!=1||!RenderOptions.ShowCustomCosmetics)return false;
        // Source-only clamped aim from SecondHunt DroneBody (MIT, NoneGiven 2020).
        // Keep the native fallback transform and gameplay aim untouched.
        Vector3 lateral=NormalizeSpatialOr(new Vector3(drawFacing.X,0,drawFacing.Z),Vector3.UnitZ);
        float sine=Math.Clamp(drawFacing.Y,-Fixed.ToFloat(2896),Fixed.ToFloat(2896));
        Vector3 facing=lateral*MathF.Sqrt(1-sine*sine)+Vector3.UnitY*sine;
        if(!owner.Prepare(GetTransformMatrix(facing,Vector3.UnitY,drawPosition.AddY(Fixed.ToFloat(Values.AltColYPos))),out var presentation))return false;
        var model=owner.Model;int polygonId=_scene.GetNextPolygonId();var previous=_scene.SourceGxSubmission;
        try
        {
            foreach(var segment in model.Segments)
            {
                _scene.SourceGxSubmission=new(segment.SourceGx!,owner.MaterialSeconds,owner,AlternateForm:true);
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
