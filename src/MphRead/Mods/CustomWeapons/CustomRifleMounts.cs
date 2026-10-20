using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.CustomWeapons;

// Arm-mounted module: native arms/cannon remain the support. The +Z authored
// module becomes +X on each presented R_elbow; no native joint is rewritten.
internal static class CustomRifleMounts
{
    private static readonly float[] Scales={.45f,.45f,.48f,.43f,.48f,.50f,.46f};
    private static readonly Vector3[] NativeTips={new(.63f,-.02f,0),new(.6f,0,0),new(1.2f,0,-.2f),
        new(.765f,-.05f,-.16f),new(1.11f,.15f,0),new(.71f,0,0),new(.89f,.15f,-.172f)};
    internal static bool TryTransform(Hunter hunter,bool firstPerson,float firstPersonTip,in Matrix4 anchor,
        Vector3 socket,in CustomRiflePose pose,float scopeBlend,out Matrix4 transform,out Vector3 muzzle)
    {
        transform=Matrix4.Identity;muzzle=default;
        if((uint)hunter>=7||!Finite(anchor)||!Finite(socket)||!float.IsFinite(firstPersonTip)
            ||!float.IsFinite(pose.Kick)||!float.IsFinite(pose.Raise)||!float.IsFinite(pose.Bolt)
            ||!float.IsFinite(pose.Magazine)||!float.IsFinite(pose.Flash)||!float.IsFinite(scopeBlend))return false;
        float scale=Scales[(int)hunter]*(firstPerson?.5f:1);
        var local=Matrix4.CreateScale(scale);
        if(!firstPerson)local*=Matrix4.CreateRotationY(MathF.PI/2);
        // Keep the entire receiver ahead of the viewmodel near plane. The
        // native cannon's short muzzle offset is not a safe model origin.
        float fpTip=MathF.Max(Math.Clamp(firstPersonTip,0,2)+.18f,.20f+scale*(socket.Z+.61f));
        Vector3 tip=firstPerson?new(0,.10f,fpTip):NativeTips[(int)hunter]+new Vector3(.18f,.18f,0);
        var shift=tip-Matrix.Vec3MultMtx4(socket,local);
        if(firstPerson)
        {
            float scope=float.IsFinite(scopeBlend)?Math.Clamp(scopeBlend,0,1):0;
            shift.Y-=Math.Clamp(pose.Raise,0,1)*.2f+scope*.2f;
            shift.Z-=Math.Clamp(pose.Kick,0,1)*.06f;
        }
        local.Row3.Xyz=shift;transform=local*anchor;muzzle=Matrix.Vec3MultMtx4(socket,transform);
        if(Finite(transform)&&Finite(muzzle))return true;
        transform=Matrix4.Identity;muzzle=default;return false;
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z);
    private static bool Finite(in Matrix4 m)=>Finite(m.Row0.Xyz)&&Finite(m.Row1.Xyz)&&Finite(m.Row2.Xyz)&&Finite(m.Row3.Xyz)
        &&float.IsFinite(m.M14)&&float.IsFinite(m.M24)&&float.IsFinite(m.M34)&&float.IsFinite(m.M44);
}
