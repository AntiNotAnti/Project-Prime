using System;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.CustomWeapons.Packages;

namespace MphRead;

public partial class Scene
{
    private CustomRifleModel? _customRifleModel;
    private bool _customRifleModelAttempted;
    internal CustomRifleModel? CustomRifleModel=>_customRifleModel;
    // Called only by explicit offline package activation, before simulation starts.
    // Decode detached immutable resources together before publishing scene ownership.
    internal void PrepareCustomRiflePackageResources()
    {
        var package=LocalWeaponPackage??throw new InvalidOperationException("No local weapon package is bound.");
        _weaponPackageResources=WeaponPackageResources.Prepare(package);
        ApplyCustomRiflePackageResources(_weaponPackageResources);
    }
    private void ApplyCustomRiflePackageResources(WeaponPackageResources resources)
    {
        ReleaseCustomRifleAudio(); // Retire any builtin/device voices before rebinding.
        _customRifleModel=resources.Model;_customRifleModelAttempted=true;
        _customRifleAudioWaves=resources.AudioSamples;
    }
    private void PrewarmCustomRifleModel()
    {
        if(LocalWeaponPackage!=null)
        {
            // Context recreation restores detached prewarmed objects, never bytes.
            if(_customRifleModel==null&&_weaponPackageResources!=null)
            {_customRifleModel=_weaponPackageResources.Model;_customRifleModelAttempted=true;}
            return;
        }
        if(_customRifleModelAttempted||Headless.Active||Services.IsReplica)return;
        _customRifleModelAttempted=true;
        try{_customRifleModel=Mods.CustomWeapons.CustomRifleModel.LoadBuiltin();}
        catch(Exception error) when(error is InvalidDataException or IOException)
        {DebugLog.Line("custom-weapons","Original M7 model unavailable; using original fallback: "+error.Message);}
    }
    // No GL IDs are retained: boxes use the scene's existing pipeline. Dropping
    // CPU ownership at visual teardown also permits clean context recreation.
    private void ReleaseCustomRifleModel(){_customRifleModel=null;_customRifleModelAttempted=false;ReleaseCustomRifleAudio();_customPublicFacts=null;_customPublicFactIndex=0;}
}
