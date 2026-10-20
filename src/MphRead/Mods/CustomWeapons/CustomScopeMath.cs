using System;

namespace MphRead.Mods.CustomWeapons;

// Presentation optics only. Never changes source aim, spread or authority state.
internal static class CustomScopeMath
{
    internal static float TargetFov(float normalFov,int magnificationMilli)
    {
        normalFov=float.IsFinite(normalFov)?Math.Clamp(normalFov,1f,175f):78f;
        if(magnificationMilli is <1000 or >16000)return normalFov;
        return Math.Clamp(2f*MathF.Atan(MathF.Tan(normalFov*MathF.PI/360f)/(magnificationMilli/1000f))*180f/MathF.PI,1f,normalFov);
    }
    internal static float AimScale(float normalFov,float currentFov)
    {
        if(!float.IsFinite(normalFov)||!float.IsFinite(currentFov)||normalFov is <1 or >175||currentFov<=0)return 1f;
        return Math.Clamp(MathF.Tan(Math.Clamp(currentFov,1f,normalFov)*MathF.PI/360f)
            /MathF.Tan(normalFov*MathF.PI/360f),.01f,1f);
    }
}
