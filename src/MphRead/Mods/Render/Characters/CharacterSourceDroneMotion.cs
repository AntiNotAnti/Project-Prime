using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render.Characters;

// Draw-only free-flight choice over completed native velocity. Threshold, axis
// convention and candidate hold adapted from SecondHunt DroneBody, 41452f2d
// (MIT, Copyright2020 NoneGiven; LICENSE retained). No movement or attack authority.
internal sealed class CharacterSourceDroneMotion
{
    internal int CurrentOrdinal {get;private set;}=13;
    internal int PendingOrdinal {get;private set;}=13;
    internal int PendingSteps {get;private set;}
    internal void Reset(){CurrentOrdinal=PendingOrdinal=13;PendingSteps=0;}
    internal static int Pick(Vector3 velocity,Vector3 facing)
    {
        if(!float.IsFinite(velocity.X)||!float.IsFinite(velocity.Y)||!float.IsFinite(velocity.Z)
            ||!float.IsFinite(facing.X)||!float.IsFinite(facing.Z))
            throw new ArgumentOutOfRangeException(nameof(velocity));
        float lengthSquared=facing.X*facing.X+facing.Z*facing.Z;
        float x=0,z=1;
        if(lengthSquared>1e-8f)
        {
            float length=MathF.Sqrt(lengthSquared);
            x=facing.X/length;z=facing.Z/length;
        }
        float forward=velocity.X*x+velocity.Z*z,left=velocity.X*z-velocity.Z*x,up=velocity.Y;
        float a=MathF.Abs(forward),b=MathF.Abs(left),u=MathF.Abs(up);
        if(MathF.Max(a,MathF.Max(b,u))<.9f)return 13;
        if(u>a&&u>b)return up>0?6:1;
        if(a>=b)return forward>0?4:3;
        return left>0?2:5;
    }
    internal int Sample(Vector3 velocity,Vector3 facing)
    {
        int candidate=Pick(velocity,facing);
        if(candidate!=PendingOrdinal){PendingOrdinal=candidate;PendingSteps=0;}
        PendingSteps=Math.Min(PendingSteps+1,8);
        if(candidate!=CurrentOrdinal&&(PendingSteps==8||CurrentOrdinal==13))CurrentOrdinal=candidate;
        return CurrentOrdinal;
    }
}
