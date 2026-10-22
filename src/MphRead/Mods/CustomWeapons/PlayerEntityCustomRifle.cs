using System;
using MphRead.Mods.CustomWeapons;
using OpenTK.Mathematics;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    internal void ModPlayCustomRifleFallback(CustomRifleCue cue)=>_soundSource.PlaySfx(cue switch
    {CustomRifleCue.Shot=>SfxId.SNIPER,CustomRifleCue.Bolt=>SfxId.SNIPER_RELOAD,
     CustomRifleCue.Reload=>SfxId.SNIPER_SWITCH,_=>SfxId.MENU_CANCEL});
    // A draw-only endpoint in the world projection, matching the exact
    // prepared viewmodel socket without rewriting a projectile or emitter.
    internal bool ModTryCustomRifleTrailStart(WeaponOwnerContext owner,WeaponIdentity weapon,out Vector3 point)
    {
        point=default;
        var hud=_scene.CustomWeaponHud(this);
        if(!IsMainPlayer||CameraType!=CameraType.First||_scene.CameraMode!=CameraMode.Player
            ||_scene.GameState.ThirdPersonCamera||_scene.ModThirdPersonCameraActive||_scene.Services.IsReplica
            ||Mods.SpectatorMode.IsSpectating||Mods.KillCam.Active||Mods.Network.DemoPlayback.IsActive
            ||ModCustomScopeBlend>0||!hud.Ready||!hud.Visible||hud.State.Owner!=owner||hud.State.Weapon!=weapon
            ||weapon.Kind!=WeaponIdentityKind.Custom
            ||!ModGetFirstPersonGunTransform(out _)||!ModTryCustomRifleTransform(true,0,out _,out var muzzle))return false;
        point=_scene.ModWorldSpaceViewModelPoint(muzzle);
        return float.IsFinite(point.X)&&float.IsFinite(point.Y)&&float.IsFinite(point.Z);
    }
    internal bool ModTryCustomRifleTransform(bool firstPerson,int lod,out Matrix4 transform,out Vector3 muzzle)
    {
        transform=Matrix4.Identity;muzzle=default;
        var hud=_scene.CustomWeaponHud(this);
        if(Mods.Headless.Active||!hud.Visible||!hud.Ready||Health<=0||IsAltForm||IsMorphing||IsUnmorphing||(uint)Hunter>=7)return false;
        Matrix4 anchor;
        if(firstPerson)
        {
            anchor=ModGetFirstPersonGunTransform(out var prepared)?prepared:GetTransformMatrix(_aimVec,_upVector,_gunDrawPos);
        }
        else
        {
            if((uint)lod>=2||_shootNodes[lod]==null)return false;
            anchor=_shootNodes[lod]!.Animation;
        }
        return CustomRifleMounts.TryTransform(Hunter,firstPerson,Fixed.ToFloat(Values.MuzzleOffset),anchor,
            _scene.CustomRifleModel?.Muzzle??new Vector3(0,.04f,1.42f),_scene.CustomRiflePose(this),
            firstPerson?ModCustomScopeBlend:0,out transform,out muzzle);
    }
    private void ModDrawCustomRifle(bool firstPerson,int lod,float alpha)
    {
        if(!ModTryCustomRifleTransform(firstPerson,lod,out var transform,out var muzzle))return;
        if(firstPerson)alpha*=1-ModCustomScopeBlend;
        if(alpha<=0)return;
        var pose=_scene.CustomRiflePose(this);
#if ANDROID
        if(firstPerson)lod=1; // Same immutable mobile fallback, no texture uploads.
#endif
        if(_scene.CustomRifleModel is {} model)CustomRiflePresentation.Draw(_scene,model,transform,alpha,lod,pose.Bolt,pose.Magazine);
        else CustomRiflePresentation.DrawFallback(_scene,transform,alpha);
        if(pose.Flash>0)CustomRiflePresentation.Flash(_scene,transform,
            _scene.CustomRifleModel?.Muzzle??new Vector3(0,.04f,1.42f),pose.Flash*alpha);
    }
}
