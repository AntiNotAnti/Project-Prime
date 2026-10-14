using System;
using System.Numerics;

namespace MphRead.Mods.MapGen;

/// <summary>Optional classic Quake .map face metadata for native editable solids.</summary>
public sealed class MapQuakeFace
{
    public int Material { get; set; }
    public float ShiftU { get; set; }
    public float ShiftV { get; set; }
    public float Rotation { get; set; }
    public float ScaleU { get; set; } = 1;
    public float ScaleV { get; set; } = 1;
    public float UnitsPerPrimeUnit { get; set; } = 16;
    public bool Valid => Material >= 0
        && float.IsFinite(ShiftU) && MathF.Abs(ShiftU) <= 65536
        && float.IsFinite(ShiftV) && MathF.Abs(ShiftV) <= 65536
        && float.IsFinite(Rotation) && MathF.Abs(Rotation) <= 36000
        && float.IsFinite(ScaleU) && MathF.Abs(ScaleU) is >= 0.001f and <= 128
        && float.IsFinite(ScaleV) && MathF.Abs(ScaleV) is >= 0.001f and <= 128
        && float.IsFinite(UnitsPerPrimeUnit)
        && UnitsPerPrimeUnit is >= 1 and <= 1024;
}

/// <summary>
/// Quake 3 q3map classic axial texture projection with source UV shifts,
/// rotation and scale. Coordinates transform from Prime Y-up to Q3 Z-up.
/// This does not execute external shaders or guess texture dimensions.
/// </summary>
public static class QuakeClassicTexture
{
    private static readonly Vector3[] BaseAxis =
    {
        new(0,0,1), new(1,0,0), new(0,-1,0),
        new(0,0,-1), new(1,0,0), new(0,-1,0),
        new(1,0,0), new(0,1,0), new(0,0,-1),
        new(-1,0,0), new(0,1,0), new(0,0,-1),
        new(0,1,0), new(1,0,0), new(0,-1,0),
        new(0,-1,0), new(1,0,0), new(0,-1,0)
    };
    public static Vector2 Calculate(Vector3 primePoint, Vector3 primeNormal, MapQuakeFace face)
    {
        if (face == null || !face.Valid || !Finite(primePoint)
            || !Finite(primeNormal) || primeNormal.LengthSquared() < 0.000001f)
            throw new ArgumentException("Invalid classic Quake face projection.");
        Vector3 normal = Vector3.Normalize(new(
            primeNormal.X, -primeNormal.Z, primeNormal.Y));
        Vector3 position = new Vector3(primePoint.X, -primePoint.Z, primePoint.Y)
            * face.UnitsPerPrimeUnit;
        int best = 0;
        float score = 0;
        for (int i = 0; i < 6; i++)
        {
            float dot = Vector3.Dot(normal, BaseAxis[3*i]);
            if (dot > score) { score = dot; best = i; }
        }
        Vector3 u = BaseAxis[3*best+1], v = BaseAxis[3*best+2];
        int sv = u.X != 0 ? 0 : u.Y != 0 ? 1 : 2;
        int tv = v.X != 0 ? 0 : v.Y != 0 ? 1 : 2;
        float angle = face.Rotation * MathF.PI / 180f;
        float sine = MathF.Sin(angle), cosine = MathF.Cos(angle);
        u = Rotate(u, sv, tv, cosine, sine);
        v = Rotate(v, sv, tv, cosine, sine);
        float x = Vector3.Dot(position, u)/face.ScaleU+face.ShiftU;
        float y = Vector3.Dot(position, v)/face.ScaleV+face.ShiftV;
        if (!float.IsFinite(x) || !float.IsFinite(y)
            || MathF.Abs(x) >= 2048 || MathF.Abs(y) >= 2048)
            throw new ArgumentOutOfRangeException(nameof(face),
                "Quake UV exceeds native +/-2048 texel budget.");
        return new(x,y);
    }
    private static bool Finite(Vector3 v)
        => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static float Get(Vector3 v,int i) => i==0?v.X:i==1?v.Y:v.Z;
    private static Vector3 Set(Vector3 v,int i,float x)
        => i==0?new(x,v.Y,v.Z):i==1?new(v.X,x,v.Z):new(v.X,v.Y,x);
    private static Vector3 Rotate(Vector3 v,int a,int b,float c,float s)
    {
        float x=Get(v,a),y=Get(v,b);
        return Set(Set(v,a,c*x-s*y),b,s*x+c*y);
    }
}
