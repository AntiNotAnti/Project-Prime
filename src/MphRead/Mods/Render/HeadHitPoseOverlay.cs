using System;
using System.Runtime.CompilerServices;
using MphRead.Formats;
using OpenTK.Mathematics;

namespace MphRead.Mods.Render;

// The shared native model is borrowed only during body submission. The returned
// scope restores every composed node and regenerates the matrix palette before
// the weapon, muzzle, frozen shell or another actor can read it.
internal static class HeadHitPoseOverlay
{
    private static readonly ConditionalWeakTable<Model,Profile> Profiles=new();
    internal static void Prewarm(Model model)=>Profiles.GetValue(model,m=>new Profile(m));
    internal static Scope Begin(Model model,HeadHitPose pose)
    {
        if(!float.IsFinite(pose.Amount)||pose.Amount<=0||!Profiles.TryGetValue(model,out var p)
            ||!p.Valid||p.InUse)return default;
        return new Scope(p,pose);
    }
    internal sealed class Profile
    {
        internal readonly Model Model;
        internal readonly Matrix4[] Saved;
        internal readonly float[] Weights;
        internal readonly int Head,Spine;
        internal readonly bool Valid;
        internal bool InUse;
        internal ulong Version;
        internal Profile(Model model)
        {
            Model=model;int count=model.Nodes.Count;
            if(count is <=0 or >128)
            {Saved=Array.Empty<Matrix4>();Weights=Array.Empty<float>();Head=Spine=-1;Valid=false;return;}
            Saved=new Matrix4[count];Weights=new float[count];
            int Find(string name){for(int i=0;i<count;i++)if(model.Nodes[i].Name==name)return i;return -1;}
            Head=Find("Head_1");Spine=Find("Spine_2");
            int spine1=Find("Spine_1"),left=Find("L_shoulder"),right=Find("R_shoulder"),shoot=Find("R_elbow");
            Valid=count is >0 and <=128&&Head>=0&&Spine>=0&&spine1>=0&&left>=0&&right>=0&&shoot>=0;
            if(!Valid)return;
            for(int i=0;i<count;i++)
            {
                if(i==shoot)continue;
                // Nearest named ancestor wins. Native Spire's head is not
                // weighted; Spine_2 remains an intentional visible fallback.
                int ancestor=i;
                for(int depth=0;ancestor>=0&&ancestor<count&&depth<count;depth++,ancestor=model.Nodes[ancestor].ParentIndex)
                {
                    if(ancestor==shoot)break;
                    if(ancestor==Head){Weights[i]=1;break;}
                    if(ancestor==left||ancestor==right){Weights[i]=.28f;break;}
                    if(ancestor==Spine){Weights[i]=.34f;break;}
                    if(ancestor==spine1){Weights[i]=.14f;break;}
                }
            }
        }
        internal void Restore()
        {
            try{for(int i=0;i<Saved.Length;i++)Model.Nodes[i].Animation=Saved[i];Model.UpdateMatrixStack();}
            finally{InUse=false;}
        }
    }
    internal readonly struct Scope : IDisposable
    {
        private readonly Profile? _profile;
        private readonly ulong _version;
        internal Scope(Profile profile,HeadHitPose pose)
        {
            _profile=profile;_version=++profile.Version;profile.InUse=true;
            var model=profile.Model;
            for(int i=0;i<profile.Saved.Length;i++)profile.Saved[i]=model.Nodes[i].Animation;
            try
            {
                Vector3 direction=pose.Direction;
                if(!float.IsFinite(direction.X)||!float.IsFinite(direction.Y)||!float.IsFinite(direction.Z)
                    ||direction.WithY(0).LengthSquared<1e-8f||!float.IsFinite(direction.LengthSquared))
                    direction=profile.Saved[profile.Spine].Row2.Xyz;
                Vector3 axis=Vector3.Cross(Vector3.UnitY,direction.WithY(0));
                if(axis.LengthSquared<1e-8f||!float.IsFinite(axis.LengthSquared))axis=Vector3.UnitX;
                else axis.Normalize();
                float angle=Math.Clamp(pose.Amount,0,1)*(MathF.PI/15); //12 degrees maximum
                Vector3 head=profile.Saved[profile.Head].Row3.Xyz,torso=profile.Saved[profile.Spine].Row3.Xyz;
                for(int i=0;i<profile.Weights.Length;i++)
                {
                    float weight=profile.Weights[i];if(weight==0)continue;
                    Vector3 pivot=weight==1?head:torso;
                    var rotation=Matrix4.CreateTranslation(-pivot)*Matrix4.CreateFromAxisAngle(axis,angle*weight)
                        *Matrix4.CreateTranslation(pivot);
                    model.Nodes[i].Animation=profile.Saved[i]*rotation;
                }
                model.UpdateMatrixStack();
            }
            catch{profile.Restore();throw;}
        }
        public void Dispose(){if(_profile is {InUse:true} p&&p.Version==_version)p.Restore();}
    }
}
