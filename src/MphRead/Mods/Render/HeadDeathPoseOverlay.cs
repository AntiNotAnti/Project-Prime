using System;
using System.Runtime.CompilerServices;
using MphRead.Entities;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// A short kinematic corpse pose, never a physics/collision object. Borrow only
// the body submission window, restoring all composed nodes/palette afterwards.
internal static class HeadDeathPoseOverlay
{
    private static readonly ConditionalWeakTable<Model,Profile> Profiles=new();
    internal static bool Prewarm(Model model)=>Profiles.GetValue(model,m=>new(m)).Valid;
    internal static Scope Begin(Model model,HeadDeathPose pose)
        =>pose.Active&&float.IsFinite(pose.Collapse)&&float.IsFinite(pose.Head)
            &&(pose.Collapse>0||pose.Head>0)
            &&Profiles.TryGetValue(model,out var p)&&p.Valid&&!p.InUse?new(p,pose):default;
    internal sealed class Profile
    {
        internal readonly Model Model;
        internal readonly Matrix4[] Saved;
        internal readonly int[] Groups,Left,Right;
        internal readonly int Head,Spine;
        internal readonly bool Valid;
        internal bool InUse;
        internal ulong Version;
        internal Profile(Model model)
        {
            Model=model;int count=model.Nodes.Count;
            var rig=CorpseRigProfile.For(model);
            if(!rig.ProceduralSupported||count is <=0 or >128)
            {Saved=Array.Empty<Matrix4>();Groups=Left=Right=Array.Empty<int>();Head=Spine=-1;return;}
            Saved=new Matrix4[count];Groups=new int[count];
            int Find(string name){for(int i=0;i<count;i++)if(model.Nodes[i].Name==name)return i;return -1;}
            Left=new[]{Find("L_hip"),Find("L_knee"),Find("L_ankle")};
            Right=new[]{Find("R_hip"),Find("R_knee"),Find("R_ankle")};Head=Find("Head_1");Spine=Find("Spine_2");
            Valid=Head>=0&&Spine>=0&&Array.TrueForAll(Left,i=>i>=0)&&Array.TrueForAll(Right,i=>i>=0);
            if(!Valid)return;
            // Trace/Noxus have no Pelvis node. Leg roots and actual world joint
            // positions define the shared hip pivot without assumed indices.
            for(int i=0;i<count;i++)for(int at=i,depth=0;at>=0&&at<count&&depth<count;at=model.Nodes[at].ParentIndex,depth++)
            {
                int l=Array.IndexOf(Left,at),r=Array.IndexOf(Right,at);
                if(l>=0){Groups[i]=l+1;break;}if(r>=0){Groups[i]=r+4;break;}
            }
        }
        internal void Restore()
        {try{for(int i=0;i<Saved.Length;i++)Model.Nodes[i].Animation=Saved[i];Model.UpdateMatrixStack();}finally{InUse=false;}}
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly Profile? _profile;
        private readonly ulong _version;
        internal Scope(Profile p,HeadDeathPose pose)
        {
            _profile=p;_version=++p.Version;p.InUse=true;var model=p.Model;
            for(int i=0;i<p.Saved.Length;i++)p.Saved[i]=model.Nodes[i].Animation;
            try
            {
                Vector3 forward=-p.Saved[p.Spine].Row2.Xyz.WithY(0);
                if(!Finite(forward)||forward.LengthSquared<1e-8f)forward=Vector3.UnitZ;else forward.Normalize();
                Vector3 fall=pose.Direction.WithY(0);
                if(!Finite(fall)||fall.LengthSquared<1e-8f)fall=forward;else fall.Normalize();
                fall=(fall+Vector3.Cross(Vector3.UnitY,fall)*Math.Clamp(pose.Side,-1,1)*.15f).Normalized();
                Vector3 axis=Vector3.Cross(Vector3.UnitY,fall).Normalized();
                float collapse=Math.Clamp(pose.Collapse,0,1),head=Math.Clamp(pose.Head,0,1);
                Vector3 lh=p.Saved[p.Left[0]].Row3.Xyz,rh=p.Saved[p.Right[0]].Row3.Xyz;
                float left=Length(p.Saved,p.Left),right=Length(p.Saved,p.Right);
                float drop=Math.Min(.45f,Math.Min(left,right)*.5f)*collapse;
                if(!float.IsFinite(drop))return;
                Vector3 pivot=(lh+rh)*.5f-Vector3.UnitY*drop;
                var tilt=Around(pivot,axis,(collapse*30-head*5)*(MathF.PI/180));
                var leftLeg=Leg(p.Saved,p.Left,drop,forward,tilt);
                var rightLeg=Leg(p.Saved,p.Right,drop,forward,tilt);
                for(int i=0;i<p.Saved.Length;i++)
                {
                    Matrix4 transform=p.Groups[i] switch
                    {1=>leftLeg.Hip,2=>leftLeg.Knee,3=>Matrix4.Identity,4=>rightLeg.Hip,5=>rightLeg.Knee,6=>Matrix4.Identity,
                        _=>Matrix4.CreateTranslation(0,-drop,0)*tilt};
                    model.Nodes[i].Animation=p.Saved[i]*transform;
                }
                Vector3 helmet=model.Nodes[p.Head].Animation.Row3.Xyz;
                model.Nodes[p.Head].Animation*=Around(helmet,axis,-head*(MathF.PI/9))
                    *Around(helmet,forward,head*Math.Clamp(pose.Side,-1,1)*(MathF.PI/36));
                model.UpdateMatrixStack();
            }
            catch{p.Restore();throw;}
        }
        public void Dispose(){if(_profile is {InUse:true} p&&p.Version==_version)p.Restore();}
    }
    private static bool Finite(Vector3 v)=>float.IsFinite(v.X)&&float.IsFinite(v.Y)&&float.IsFinite(v.Z)&&float.IsFinite(v.LengthSquared);
    private static Matrix4 Around(Vector3 pivot,Vector3 axis,float radians)
        =>Matrix4.CreateTranslation(-pivot)*Matrix4.CreateFromAxisAngle(axis,radians)*Matrix4.CreateTranslation(pivot);
    private static float Length(Matrix4[] saved,int[] leg)=>(saved[leg[1]].Row3.Xyz-saved[leg[0]].Row3.Xyz).Length
        +(saved[leg[2]].Row3.Xyz-saved[leg[1]].Row3.Xyz).Length;
    private static (Matrix4 Hip,Matrix4 Knee) Leg(Matrix4[] saved,int[] leg,float drop,Vector3 forward,Matrix4 tilt)
    {
        Vector3 hip=saved[leg[0]].Row3.Xyz,knee=saved[leg[1]].Row3.Xyz,foot=saved[leg[2]].Row3.Xyz;
        Vector3 lowered=Matrix.Vec3MultMtx4(hip-Vector3.UnitY*drop,tilt);
        Vector3 target=PlayerEntity.ModCrouchKnee(lowered,foot,(knee-hip).Length,(foot-knee).Length,forward);
        return (Move(hip,lowered,knee-hip,target-lowered),Move(knee,target,foot-knee,foot-target));
    }
    private static Matrix4 Move(Vector3 origin,Vector3 target,Vector3 before,Vector3 after)
    {
        Matrix4 rotation=Matrix4.Identity;
        if(before.LengthSquared>1e-8f&&after.LengthSquared>1e-8f)
        {
            before.Normalize();after.Normalize();var axis=Vector3.Cross(before,after);float length=axis.Length;
            float dot=Math.Clamp(Vector3.Dot(before,after),-1,1);
            if(length>1e-6f)rotation=Matrix4.CreateFromAxisAngle(axis/length,MathF.Atan2(length,dot));
            else if(dot<0)
            {axis=Vector3.Cross(before,MathF.Abs(before.X)<.9f?Vector3.UnitX:Vector3.UnitZ).Normalized();rotation=Matrix4.CreateFromAxisAngle(axis,MathF.PI);}
        }
        return Matrix4.CreateTranslation(-origin)*rotation*Matrix4.CreateTranslation(target);
    }
}
