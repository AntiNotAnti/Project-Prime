using System;
using System.Collections.Generic;
using System.IO;
using OpenTK.Mathematics;

namespace MphRead.Mods.Replay;

internal enum ReplayCameraChannelMode : byte { Inherit, Linear, Smooth, Bezier, Hold }
internal enum ReplayCameraChannelEase : byte { Inherit, None, In, Out, InOut }
internal readonly record struct ReplayCameraChannelCurves(
    ReplayCameraChannelMode Orientation = default, ReplayCameraChannelEase OrientationEase = default,
    ReplayCameraChannelMode Fov = default, ReplayCameraChannelEase FovEase = default,
    ReplayCameraChannelMode Roll = default, ReplayCameraChannelEase RollEase = default);

internal sealed partial class ReplayCameraTrack
{
    private readonly record struct ChannelSegment(Quaternion Left,Quaternion Right,Quaternion A,Quaternion B,bool Bezier,
        float FovLeft,float FovRight,float RollLeft,float RollRight);
    private readonly Dictionary<int,ChannelSegment> _channelSegments=new();

    internal static bool ValidCurves(ReplayCameraChannelCurves curves)
        => Enum.IsDefined(curves.Orientation)&&curves.Orientation!=ReplayCameraChannelMode.Bezier
        &&Enum.IsDefined(curves.Fov)&&Enum.IsDefined(curves.Roll)&&Enum.IsDefined(curves.OrientationEase)
        &&Enum.IsDefined(curves.FovEase)&&Enum.IsDefined(curves.RollEase);
    private static bool HasChannelCurves(List<ReplayCameraKeyframe> keys)=>keys.Exists(key=>key.Curves!=default);
    private static void WriteCurves(BinaryWriter writer,ReplayCameraChannelCurves curves)
    {
        writer.Write((byte)curves.Orientation);writer.Write((byte)curves.OrientationEase);
        writer.Write((byte)curves.Fov);writer.Write((byte)curves.FovEase);writer.Write((byte)curves.Roll);writer.Write((byte)curves.RollEase);
    }
    private static ReplayCameraChannelCurves ReadCurves(BinaryReader reader)=>new(
        (ReplayCameraChannelMode)reader.ReadByte(),(ReplayCameraChannelEase)reader.ReadByte(),
        (ReplayCameraChannelMode)reader.ReadByte(),(ReplayCameraChannelEase)reader.ReadByte(),
        (ReplayCameraChannelMode)reader.ReadByte(),(ReplayCameraChannelEase)reader.ReadByte());

    private ReplayCameraKeyframe SampleChannels(int segment,float raw,ReplayCameraKeyframe sample)
    {
        var left=_keys[segment];var right=_keys[segment+1];var curves=left.Curves;
        if(curves==default)return sample; // All-inherit is deliberately the exact legacy evaluator, including arc-remapped time.
        if(left.Interpolation==ReplayCameraInterpolation.Hold)
        {
            var held=raw>=1?right:left;
            return sample with{Rotation=held.Rotation,Fov=held.Fov,Roll=held.Roll,Curves=held.Curves};
        }
        bool orientation=curves.Orientation!=0||curves.OrientationEase!=0;
        bool fov=curves.Fov!=0||curves.FovEase!=0;
        bool roll=curves.Roll!=0||curves.RollEase!=0;
        ChannelSegment controls=default;
        if(curves.Orientation==ReplayCameraChannelMode.Smooth||curves.Fov==ReplayCameraChannelMode.Smooth||curves.Roll==ReplayCameraChannelMode.Smooth)
            controls=ChannelControls(segment);
        if(orientation)
        {
            float t=ChannelProgress(raw,curves.Orientation,curves.OrientationEase,left.Ease,segment);
            Quaternion rotation=curves.Orientation switch
            {
                ReplayCameraChannelMode.Hold=>raw>=1?right.Rotation:left.Rotation,
                ReplayCameraChannelMode.Smooth=>Squad(controls,t),
                _=>Quaternion.Slerp(Canonical(left.Rotation),Canonical(right.Rotation),t).Normalized()
            };
            sample=sample with{Rotation=rotation};
        }
        if(fov)sample=sample with{Fov=ScalarChannel(raw,segment,curves.Fov,curves.FovEase,true,controls)};
        if(roll)sample=sample with{Roll=ScalarChannel(raw,segment,curves.Roll,curves.RollEase,false,controls)};
        return sample with{Curves=raw>=1?right.Curves:left.Curves};
    }
    private float ChannelProgress(float raw,ReplayCameraChannelMode mode,ReplayCameraChannelEase ease,ReplayCameraEase inherited,int segment)
    {
        if(mode==ReplayCameraChannelMode.Hold)return raw>=1?1:0;
        if(ease!=ReplayCameraChannelEase.Inherit)return ApplyEase(raw,(ReplayCameraEase)((byte)ease-1));
        // Smooth defaults preserve C1 through interior keys; authored explicit easing intentionally changes that speed contract.
        return mode==ReplayCameraChannelMode.Smooth
            ?SegmentProgress(raw,ReplayCameraInterpolation.Spline,inherited,segment,_keys.Count-1):ApplyEase(raw,inherited);
    }
    private float ScalarChannel(float raw,int segment,ReplayCameraChannelMode mode,ReplayCameraChannelEase ease,bool fov,ChannelSegment controls)
    {
        var left=_keys[segment];var right=_keys[segment+1];
        double a=fov?left.Fov:left.Roll,b=fov?right.Fov:right.Roll;
        double t=ChannelProgress(raw,mode,ease,left.Ease,segment),u=1-t,value;
        if(mode==ReplayCameraChannelMode.Hold)value=raw>=1?b:a;
        else if(mode==ReplayCameraChannelMode.Smooth)
        {
            double h=right.Frame-left.Frame,m0=fov?controls.FovLeft:controls.RollLeft,m1=fov?controls.FovRight:controls.RollRight;
            double t2=t*t,t3=t2*t;
            value=(2*t3-3*t2+1)*a+(t3-2*t2+t)*h*m0+(-2*t3+3*t2)*b+(t3-t2)*h*m1;
        }
        else if(mode==ReplayCameraChannelMode.Bezier||mode==ReplayCameraChannelMode.Inherit&&left.Interpolation==ReplayCameraInterpolation.Bezier)
        {
            double outgoing=fov?left.FovOutgoingTangent:left.RollOutgoingTangent,incoming=fov?right.FovIncomingTangent:right.RollIncomingTangent;
            value=a*u*u*u+3*(a+outgoing)*u*u*t+3*(b+incoming)*u*t*t+b*t*t*t;
        }
        else value=a+(b-a)*t;
        double minimum=fov?MathHelper.DegreesToRadians(1):-MathHelper.TwoPi,maximum=fov?MathHelper.DegreesToRadians(175):MathHelper.TwoPi;
        return (float)Math.Clamp(double.IsFinite(value)?value:a,minimum,maximum);
    }
    private ChannelSegment ChannelControls(int segment)
    {
        if(_channelSegments.TryGetValue(segment,out var cached))return cached;
        var left=_keys[segment];var right=_keys[segment+1];
        Quaternion q0=Canonical(left.Rotation),q1=Aligned(q0,Canonical(right.Rotation));
        Vector3 delta=RotationLog(q0,q1);float dt=right.Frame-left.Frame;
        Vector3 v0=AngularTangent(segment),v1=AngularTangent(segment+1);
        Quaternion a=(q0*RotationExp((v0*dt-delta)*.5f)).Normalized(),b=(q1*RotationExp((delta-v1*dt)*.5f)).Normalized();
        bool bezier=QuaternionDot(q0,q1)<.00001||QuaternionDot(q0,a)<.00001||QuaternionDot(q0,b)<.00001
            ||QuaternionDot(q1,a)<.00001||QuaternionDot(q1,b)<.00001||QuaternionDot(a,b)<.00001;
        if(bezier){a=(q0*RotationExp(v0*(dt/3))).Normalized();b=(q1*RotationExp(-v1*(dt/3))).Normalized();}
        var result=new ChannelSegment(q0,q1,a,b,bezier,
            ScalarTangent(segment,true),ScalarTangent(segment+1,true),ScalarTangent(segment,false),ScalarTangent(segment+1,false));
        _channelSegments.Add(segment,result);return result;
    }
    private Vector3 AngularTangent(int key)
    {
        var current=_keys[key];Quaternion rotation=Canonical(current.Rotation);
        if(current.Interpolation==ReplayCameraInterpolation.Hold||key>0&&_keys[key-1].Interpolation==ReplayCameraInterpolation.Hold)return Vector3.Zero;
        Vector3 previous=Vector3.Zero,next=Vector3.Zero;float hp=0,hn=0;float limit=float.MaxValue;
        if(key>0){hp=current.Frame-_keys[key-1].Frame;previous=-RotationLog(rotation,Canonical(_keys[key-1].Rotation));limit=Math.Min(limit,(MathHelper.PiOver2-.0001f)/hp);}
        if(key+1<_keys.Count){hn=_keys[key+1].Frame-current.Frame;next=RotationLog(rotation,Canonical(_keys[key+1].Rotation));limit=Math.Min(limit,(MathHelper.PiOver2-.0001f)/hn);}
        Vector3 tangent=hp==0?next/hn:hn==0?previous/hp:(previous*(hn/hp)+next*(hp/hn))/(hp+hn);
        // The same key tangent bounds both adjacent controls. Extreme time ratios must not wrap an exp control across a shortest-path hemisphere.
        float length=tangent.Length;if(length>limit&&length>0)tangent*=limit/length;
        return tangent;
    }
    private float ScalarTangent(int key,bool fov)
    {
        double Value(int i)=>fov?_keys[i].Fov:_keys[i].Roll;
        int count=_keys.Count;if(count<2)return 0;
        if(_keys[key].Interpolation==ReplayCameraInterpolation.Hold||key>0&&_keys[key-1].Interpolation==ReplayCameraInterpolation.Hold)return 0;
        if(count==2)return (float)((Value(1)-Value(0))/(_keys[1].Frame-_keys[0].Frame));
        if(key==0||key==count-1)
        {
            int a=key==0?0:count-1,b=key==0?1:count-2,c=key==0?2:count-3;
            double h0=Math.Abs((double)_keys[b].Frame-_keys[a].Frame),h1=Math.Abs((double)_keys[c].Frame-_keys[b].Frame);
            double d0=(Value(b)-Value(a))/h0,d1=(Value(c)-Value(b))/h1;
            double slope=((2*h0+h1)*d0-h0*d1)/(h0+h1);
            if(Math.Sign(slope)!=Math.Sign(d0))slope=0;
            else if(Math.Sign(d0)!=Math.Sign(d1)&&Math.Abs(slope)>3*Math.Abs(d0))slope=3*d0;
            return (float)(key==0?slope:-slope);
        }
        double hp=_keys[key].Frame-_keys[key-1].Frame,hn=_keys[key+1].Frame-_keys[key].Frame;
        double previous=(Value(key)-Value(key-1))/hp,next=(Value(key+1)-Value(key))/hn;
        if(previous==0||next==0||Math.Sign(previous)!=Math.Sign(next))return 0;
        double w1=2*hn+hp,w2=hn+2*hp;
        return (float)((w1+w2)/(w1/previous+w2/next));
    }
    private static Quaternion Squad(ChannelSegment controls,float t)
    {
        if(t<=0)return controls.Left;if(t>=1)return controls.Right;
        if(controls.Bezier)
        {
            // Shortest SQUAD controls can occupy opposite lifts near half-turns. The same shared endpoint velocities
            // define a spherical Bezier whose controls lie in one open hemisphere, so lifted de Casteljau cannot jump.
            var a=LiftedSlerp(controls.Left,controls.A,t);var b=LiftedSlerp(controls.A,controls.B,t);var c=LiftedSlerp(controls.B,controls.Right,t);
            return LiftedSlerp(LiftedSlerp(a,b,t),LiftedSlerp(b,c,t),t);
        }
        var path=Quaternion.Slerp(controls.Left,controls.Right,t);
        var tangent=Quaternion.Slerp(controls.A,controls.B,t);
        return Quaternion.Slerp(path,tangent,2*t*(1-t)).Normalized();
    }
    private static double QuaternionDot(Quaternion a,Quaternion b)=>(double)a.X*b.X+(double)a.Y*b.Y+(double)a.Z*b.Z+(double)a.W*b.W;
    private static Quaternion LiftedSlerp(Quaternion a,Quaternion b,float t)
    {
        if(t<=0)return a;if(t>=1)return b;
        double dot=Math.Clamp(QuaternionDot(a,b),-1,1),angle=Math.Acos(dot),sin=Math.Sin(angle);
        if(Math.Abs(sin)<1e-7)return new Quaternion(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t,a.W+(b.W-a.W)*t).Normalized();
        double left=Math.Sin((1-t)*angle)/sin,right=Math.Sin(t*angle)/sin;
        return new Quaternion((float)(a.X*left+b.X*right),(float)(a.Y*left+b.Y*right),(float)(a.Z*left+b.Z*right),(float)(a.W*left+b.W*right)).Normalized();
    }
    private static Quaternion Canonical(Quaternion value)
    {
        value.Normalize();
        if(value.W<0||value.W==0&&(value.X<0||value.X==0&&(value.Y<0||value.Y==0&&value.Z<0)))value=new(-value.X,-value.Y,-value.Z,-value.W);
        return value;
    }
    private static Quaternion Aligned(Quaternion reference,Quaternion value)
        =>reference.X*value.X+reference.Y*value.Y+reference.Z*value.Z+reference.W*value.W<0?new(-value.X,-value.Y,-value.Z,-value.W):value;
    private static Vector3 RotationLog(Quaternion from,Quaternion to)
    {
        Quaternion delta=Quaternion.Invert(from)*Aligned(from,to);delta.Normalize();
        double length=Math.Sqrt((double)delta.X*delta.X+(double)delta.Y*delta.Y+(double)delta.Z*delta.Z);
        if(length<1e-10)return new(delta.X,delta.Y,delta.Z);
        float factor=(float)(Math.Atan2(length,Math.Clamp(delta.W,-1,1))/length);
        return new(delta.X*factor,delta.Y*factor,delta.Z*factor);
    }
    private static Quaternion RotationExp(Vector3 log)
    {
        double angle=Math.Sqrt((double)log.X*log.X+(double)log.Y*log.Y+(double)log.Z*log.Z);
        if(angle<1e-10)return new Quaternion(log.X,log.Y,log.Z,1).Normalized();
        float factor=(float)(Math.Sin(angle)/angle);return new(log*factor,(float)Math.Cos(angle));
    }
}
