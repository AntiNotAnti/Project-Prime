using System;
using System.IO;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;

namespace MphRead;

public partial class Scene
{
    private CustomRifleModel? _customRifleModel;
    private bool _customRifleModelAttempted;
    internal CustomRifleModel? CustomRifleModel=>_customRifleModel;
    private void PrewarmCustomRifleModel()
    {
        if(_customRifleModelAttempted||Headless.Active||Services.IsReplica)return;
        _customRifleModelAttempted=true;
        try{_customRifleModel=Mods.CustomWeapons.CustomRifleModel.LoadBuiltin();}
        catch(Exception error) when(error is InvalidDataException or IOException)
        {DebugLog.Line("custom-weapons","Original M7 model unavailable; using original fallback: "+error.Message);}
    }
    // No GL IDs are retained: boxes use the scene's existing pipeline. Dropping
    // CPU ownership at visual teardown also permits clean context recreation.
    private void ReleaseCustomRifleModel(){_customRifleModel=null;_customRifleModelAttempted=false;}
}
