using System;
using System.Numerics;

namespace MphRead.Mods.Render;

/// <summary>Shared runtime/creator environment math without a graphics context or document owner.</summary>
public static class GraphicsEnvironmentMath
{
    public static (float Minimum,float Maximum) FogRange(int offset,int slope)
        => (offset/(float)0x7FFF,(offset+32*(0x400>>slope))/(float)0x7FFF);

    public static (Matrix4x4 View,Matrix4x4 Projection) DirectionalShadowCamera(Vector3 cameraPosition,Vector3 cameraFacing,
        Vector3 lightDirection,int targetSize,bool lowQuality)
    {
        var direction=lightDirection;
        if(!float.IsFinite(direction.X)||!float.IsFinite(direction.Y)||!float.IsFinite(direction.Z)||direction.LengthSquared()<.0001f)
            direction=new(-.45f,-.82f,-.35f);
        direction=Vector3.Normalize(direction);
        var center=cameraPosition+cameraFacing*24;
        var up=MathF.Abs(Vector3.Dot(direction,Vector3.UnitY))>.92f ? Vector3.UnitZ : Vector3.UnitY;
        float span=lowQuality ? 100 : 130;
        var right=Vector3.Normalize(Vector3.Cross(direction,up));var lightUp=Vector3.Normalize(Vector3.Cross(right,direction));
        float worldPerTexel=span/Math.Max(1,targetSize),alongRight=Vector3.Dot(center,right),alongUp=Vector3.Dot(center,lightUp);
        center+=right*(MathF.Round(alongRight/worldPerTexel)*worldPerTexel-alongRight)
            +lightUp*(MathF.Round(alongUp/worldPerTexel)*worldPerTexel-alongUp);
        var view=Matrix4x4.CreateLookAt(center-direction*96,center,up);
        var projection=Matrix4x4.CreateOrthographic(span,span,1,220);
        // Authoritative runtime shaders consume GL clip depth (-1..1).
        projection.M33=-2f/(220-1);projection.M43=-(220+1f)/(220-1);
        return (view,projection);
    }
}
