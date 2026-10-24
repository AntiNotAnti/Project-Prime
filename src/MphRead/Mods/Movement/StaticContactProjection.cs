using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// A bounded static-contact refinement must not push back through a plane it
// already resolved. This projects onto the intersection of its outward half
// spaces; it does not change contact detection, body shapes or allowed depth.
internal static class StaticContactProjection
{
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static bool Unit(Vector3 v)=>Finite(v)&&Math.Abs(v.LengthSquared-1)<.0001f;
    private static bool Valid(ReadOnlySpan<Vector3> normals)
    {
        if(normals.Length>4)return false;
        foreach(var n in normals)if(!Unit(n))return false;
        return true;
    }

    internal static bool TryCorrection(Vector3 normal,float depth,ReadOnlySpan<Vector3> previous,
        float remainingTravel,out Vector3 correction)
    {
        correction=Vector3.Zero;
        if(!Unit(normal)||!Valid(previous)||!float.IsFinite(depth)||depth<=0
            ||!float.IsFinite(remainingTravel)||remainingTravel<=0)return false;
        var direction=Project(normal,previous);
        float separation=Vector3.Dot(normal,direction);
        if(!float.IsFinite(separation)||separation<=1e-6f)return false;
        var delta=direction*(depth/separation);
        if(!Finite(delta)||delta.Length>remainingTravel)return false;
        correction=delta;return true;
    }

    internal static Vector3 ClipVelocity(Vector3 velocity,ReadOnlySpan<Vector3> normals)
        =>Finite(velocity)&&Valid(normals)?Project(velocity,normals):Vector3.Zero;

    private static bool Inside(Vector3 value,ReadOnlySpan<Vector3> normals)
    {
        foreach(var n in normals)if(Vector3.Dot(value,n)<-1e-6f)return false;
        return true;
    }

    private static Vector3 Project(Vector3 value,ReadOnlySpan<Vector3> normals)
    {
        if(Inside(value,normals))return value;
        // A closest point in a three-dimensional homogeneous cone is in its
        // interior, one plane, a two-plane crease, or the origin. Enumerating
        // these faces avoids iterative clipping re-entering another contact.
        Vector3 best=Vector3.Zero;float bestDistance=value.LengthSquared;
        for(int i=0;i<normals.Length;i++)
        {
            var candidate=value-normals[i]*Vector3.Dot(value,normals[i]);
            float distance=(candidate-value).LengthSquared;
            if(Finite(candidate)&&Inside(candidate,normals)&&distance<bestDistance)
            {best=candidate;bestDistance=distance;}
            for(int j=0;j<i;j++)
            {
                var crease=Vector3.Cross(normals[i],normals[j]);
                float lengthSquared=crease.LengthSquared;
                if(lengthSquared<=1e-10f)continue;
                candidate=crease*(Vector3.Dot(value,crease)/lengthSquared);
                distance=(candidate-value).LengthSquared;
                if(Finite(candidate)&&Inside(candidate,normals)&&distance<bestDistance)
                {best=candidate;bestDistance=distance;}
            }
        }
        return best;
    }
}
