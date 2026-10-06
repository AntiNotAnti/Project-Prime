using System;
using System.Numerics;
using MphRead.Formats.Collision;

namespace MphRead.Mods.MapEditor;

public static class MapViewportDiagnostics
{
    public const float TargetTexelsPerUnit=16;
    public static Vector3 TerrainColor(Terrain terrain)
    {var color=MphCollisionInfoBase.TerrainColor(terrain);return new(color.X,color.Y,color.Z);}
    public static Vector3 MaterialColor(bool imported,int index)
    {
        uint hash=unchecked((uint)index+1+(imported ? 0x100000u : 0u));
        hash^=hash>>16;hash*=0x7feb352d;hash^=hash>>15;hash*=0x846ca68b;hash^=hash>>16;
        return new(.2f+.8f*(hash&255)/255f,.2f+.8f*((hash>>8)&255)/255f,.2f+.8f*((hash>>16)&255)/255f);
    }
    public static Vector3 DensityColor(float texelsPerUnit)
    {
        float t=Math.Clamp(MathF.Log2(Math.Max(texelsPerUnit,.0001f)/TargetTexelsPerUnit)*.25f+.5f,0,1);
        return t<.5f ? Vector3.Lerp(new(.15f,.3f,1),new(.2f,1,.3f),t*2)
            : Vector3.Lerp(new(.2f,1,.3f),new(1,.2f,.1f),(t-.5f)*2);
    }
}
