using System;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Render.Characters;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private CharacterSourceBodyOwner? _sourceBody;
    private long _sourceBodyLife;
    internal CharacterSourceBodyOwner? ModSourceBody => _sourceBody;
    internal Model ModSourceNativeGun => _gunModel.Model;
    internal Model ModSourceNativeBall => _altModel.Model;

    internal void ModBindSourceBody(CharacterSourcePreviewSelection selection)
    {
        ModRetireSourceBody();
        _sourceBody = new(_scene, this, selection);
    }
    internal void ModRetireSourceBody() { _sourceBody?.Dispose(); _sourceBody = null; }
    internal void ModResetSourceBodyLife() { unchecked { _sourceBodyLife++; } }

    private bool SourcePresentationStateSupported => Health > 0 && RenderOptions.Shadows == ShadowQuality.Off
        && !ModCrouched && !ModTucked && ModCrouchVisualAmount == 0
        && !ModParkourMantleActive && !ModParkourWallrunActive && !ModGrappleState.Active
        && _frozenTimer == 0 && _frozenGfxTimer == 0 && _burnTimer == 0
        && _doubleDmgTimer == 0 && _disruptedTimer == 0 && _shockCoilTimer == 0
        && !Flags2.TestFlag(PlayerFlags2.Cloaking) && _curAlpha >= 1
        && !BrightSkinStatusOverride && !BrightSkinFrozenOverlay && !ModMatchSpawnProtectionActive
        && _timeSinceDamage >= Values.DamageFlashTime * 2 && !_scene.GameState.Teams
        && !BrightSkins.ShouldApply(this) && PaletteOverride == null
        && _scene.CameraSequences.Current == null && !_scene.Services.IsReplica
        && !Mods.Network.NetSession.Active && !Mods.Network.DemoPlayback.IsActive
        && !Mods.KillCam.Active;
    private bool SourceBodyStateSupported => SourcePresentationStateSupported && !IsAltForm && !IsMorphing && !IsUnmorphing;
    private bool SourceBallStateSupported => SourcePresentationStateSupported && Hunter == Hunter.Samus
        && IsAltForm && !IsMorphing && !IsUnmorphing;

    internal void ModStepSourceBody()
    {
        if (_sourceBody == null) return;
        _sourceBody.Step(_scene.FrameCount, _sourceBodyLife, Biped1Anim, _bipedModel1.AnimInfo.Frame[0],
            Biped2Anim, _bipedModel2.AnimInfo.Frame[0], _facingVector.Y, SourceBodyStateSupported,SourceBallStateSupported);
    }

    private bool ModTryDrawSourceBall(Matrix4 nativeRoll,float alpha,Vector4? overrideColor,Vector4? outlineColor)
    {
        if (_sourceBody is not { } owner || !SourceBallStateSupported || alpha != 1 || overrideColor != null
            || outlineColor != null || !RenderOptions.ShowCustomCosmetics) return false;
        // The native specialized draw pose already owns interpolated rotation
        // and position. Add only the native sphere-center offset in world Y.
        nativeRoll.Row3.Y += Fixed.ToFloat(Values.AltColYPos);
        if(!owner.PrepareBall(nativeRoll,out var presentation))return false;
        var model=owner.BallModel!;int polygonId=_scene.GetNextPolygonId();
        var previous=_scene.SourceGxSubmission;
        try
        {
            foreach(var segment in model.Segments)
            {
                _scene.SourceGxSubmission=new(segment.SourceGx!,owner.BallMaterialSeconds,owner,AlternateForm:true);
                _scene.AddRenderItem(_altModel.Model.Materials[segment.NativeMaterialIndex],polygonId,1,
                    Vector3.Zero,GetLightInfo(),Matrix4.Identity,model.SourceBallSegmentPresentation(segment,presentation,owner.BallMaterialSeconds),segment.ListId,
                    segment.SourceJointPalette!.Count,segment.SourcePresentationPalette!,null,null,SelectionType.None,BillboardMode.None,1,
                    segment.AlbedoBinding,weightedSkinning:true,authoredTexture:true,authoredWrapS:segment.WrapS,
                    authoredWrapT:segment.WrapT,authoredDoubleSided:true,
                    authoredTransparent:segment.SourceGx!.Program.DrawPass!=CharacterSourceGxProgram.Pass.Opaque);
            }
        }
        finally{_scene.SourceGxSubmission=previous;}
        return true;
    }

    private bool ModTryDrawSourceBody(Vector3 drawPosition, Vector3 drawFacing, float alpha,
        Vector4? overrideColor, Vector4? outlineColor, int lod)
    {
        if (_sourceBody is not { } owner || !SourceBodyStateSupported || alpha != 1 || lod != 0
            || overrideColor != null || outlineColor != null || !RenderOptions.ShowCustomCosmetics) return false;
        // Feet and height follow the native actor. This positive-scale source
        // basis is distinct from the cartridge model's reversed body basis.
        Vector3 forward = new(drawFacing.X, 0, drawFacing.Z);
        if (forward.LengthSquared < .000001f) return false;
        forward.Normalize();
        var rotation = Matrix4.Identity;
        rotation.Row0.Xyz = Vector3.Cross(Vector3.UnitY, forward);
        rotation.Row2.Xyz = forward;
        rotation.Row3.Xyz = drawPosition.AddY(Fixed.ToFloat(Values.MinPickupHeight) - .03f);
        var model = owner.Model;
        Matrix4 transform = owner.FeetFit * rotation;
        if (!owner.Prepare(transform)) return false;
        int polygonId = _scene.GetNextPolygonId();
        var previous = _scene.SourceGxSubmission;
        try
        {
            foreach (var segment in model.Segments)
            {
                _scene.SourceGxSubmission = new(segment.SourceGx!, owner.MaterialSeconds, owner);
                _scene.AddRenderItem(_bipedModel2.Model.Materials[segment.NativeMaterialIndex], polygonId, 1,
                    Vector3.Zero, GetLightInfo(), Matrix4.Identity, transform, segment.ListId,
                    segment.SourceJointPalette!.Count, segment.SourcePresentationPalette!,
                    null, null, SelectionType.None, BillboardMode.None, 1, segment.AlbedoBinding,
                    weightedSkinning: true, authoredTexture: true, authoredWrapS: segment.WrapS,
                    authoredWrapT: segment.WrapT, authoredDoubleSided: true,
                    authoredTransparent: segment.SourceGx!.Program.DrawPass != CharacterSourceGxProgram.Pass.Opaque);
            }
        }
        finally { _scene.SourceGxSubmission = previous; }
        return true;
    }

    private bool ModTryDrawSourceCannon(Matrix4 gun)
    {
        if (_sourceBody is not { } owner || !SourceBodyStateSupported || !RenderOptions.ShowCustomCosmetics
            || _curAlpha != 1 || CurrentWeapon != BeamType.PowerBeam
            || !SourceCannonAnimationSupported(GunAnimation)
            || !ModGetFirstPersonGunTransform(out _) || !ModGetFirstPersonEffectTransform(out _)
            || !owner.PrepareCannon(gun, Fixed.ToFloat(Values.MuzzleOffset), out var presentation)) return false;
        var model = owner.CannonModel!;
        // Positions already contain the fixed source-to-cannon basis. The item
        // transform carries the same basis for raw-normal environment texgen.
        Matrix4 normalTransform = model.SourceCannon!.ToCannon * presentation;
        int polygonId = _scene.GetNextPolygonId();
        var previous = _scene.SourceGxSubmission;
        try
        {
            foreach (var segment in model.Segments)
            {
                _scene.SourceGxSubmission = new(segment.SourceGx!, owner.MaterialSeconds, owner, ViewModel: true);
                _scene.AddRenderItem(_gunModel.Model.Materials[segment.NativeMaterialIndex], polygonId, 1,
                    Vector3.Zero, GetLightInfo(), Matrix4.Identity, normalTransform, segment.ListId,
                    segment.SourceJointPalette!.Count, segment.SourcePresentationPalette!,
                    null, null, SelectionType.None, BillboardMode.None, 1, segment.AlbedoBinding,
                    weightedSkinning: true, authoredTexture: true, authoredWrapS: segment.WrapS,
                    authoredWrapT: segment.WrapT, authoredDoubleSided: true,
                    authoredTransparent: segment.SourceGx!.Program.DrawPass != CharacterSourceGxProgram.Pass.Opaque);
            }
        }
        finally { _scene.SourceGxSubmission = previous; }
        return true;
    }

    internal static bool SourceCannonAnimationSupported(GunAnimation state)
        => state is GunAnimation.Idle or GunAnimation.Shot or GunAnimation.Charging or GunAnimation.FullCharge or GunAnimation.ChargeShot;
}
