using System;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Network;

namespace MphRead.Entities;

public partial class PlayerEntity
{
    private WeaponOwnerContext _customScopeOwner;
    private WorldBootstrapIdentity _customScopeBootstrap;
    private WeaponDigest _customScopeManifest;
    private bool _customScopeSelected,_customScopeTransition;
    private byte _customScopeOptic,_customScopeLens;
    private float _customScopeTargetFov,_customScopeLensFov;
    internal bool ModCustomScopeSelected=>_customScopeSelected;
    internal bool ModCustomScopeActive=>_customScopeSelected&&_customScopeOptic!=0;
    private float CustomNormalFov=>Fixed.ToFloat(Values.NormalFov)*2;
    internal float ModCustomScopeBlend=>_customScopeTransition
        ?Math.Clamp((CustomNormalFov-CameraInfo.Fov)/MathF.Max(.001f,CustomNormalFov-_customScopeLensFov),0,1):0;
    internal float ModCustomScopeSensitivity
        =>1+((_customScopeLens==2?InputSettings.CustomScope4Sensitivity:InputSettings.CustomScope25Sensitivity)-1)*ModCustomScopeBlend;
    internal void ModSetCustomScope(WeaponOwnerContext owner,bool selected,byte optic,int magnification=1000,WorldBootstrapIdentity bootstrap=default,WeaponDigest manifest=default)
    {
        bool eligible=Health>0&&!IsAltForm&&!IsMorphing&&!IsUnmorphing&&!ScanVisor
            &&_scene.CameraSequences.Current==null&&!_scene.Services.IsReplica&&!DemoPlayback.IsActive;
        if(_customScopeOwner!=owner||_customScopeBootstrap!=bootstrap||_customScopeManifest!=manifest)
        {
            if(_customScopeTransition)CameraInfo.Fov=CustomNormalFov;
            _customScopeOwner=owner;_customScopeBootstrap=bootstrap;_customScopeManifest=manifest;_customScopeTransition=false;_customScopeLens=0;
        }
        _customScopeSelected=eligible&&selected;
        _customScopeOptic=_customScopeSelected?optic:(byte)0;
        _customScopeTargetFov=CustomScopeMath.TargetFov(CustomNormalFov,_customScopeOptic!=0?magnification:1000);
        if(_customScopeOptic!=0)
        {_customScopeLens=_customScopeOptic;_customScopeLensFov=_customScopeTargetFov;_customScopeTransition=true;}
        // Readiness/lifecycle loss removes a stale custom lens immediately.
        if(!eligible||owner==default)
        {
            if(_customScopeTransition)CameraInfo.Fov=CustomNormalFov;
            _customScopeSelected=_customScopeTransition=false;_customScopeOptic=_customScopeLens=0;
        }
    }
    internal bool ModStepCustomScopeFov()
    {
        if(!_customScopeSelected&&!_customScopeTransition)return false;
        if(!_customScopeSelected&&EquipInfo.Zoomed)
        {_customScopeTransition=false;_customScopeLens=0;return false;}
        float target=_customScopeSelected?_customScopeTargetFov:CustomNormalFov;
        float current=float.IsFinite(CameraInfo.Fov)?CameraInfo.Fov:CustomNormalFov;
        // Fixed60Hz presentation; no recoil/camera shake is required to scope.
        current+= (target-current)*.25f;
        CameraInfo.Fov=MathF.Abs(target-current)<.01f?target:current;
        if(!_customScopeSelected&&CameraInfo.Fov==CustomNormalFov)
        {_customScopeTransition=false;_customScopeLens=0;}
        return true;
    }
}
