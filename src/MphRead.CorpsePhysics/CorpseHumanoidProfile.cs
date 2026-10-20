using System.Numerics;

namespace MphRead.CorpsePhysics;

// Owns calibration values only. The engine must separately authenticate its
// native profile, LOD, actor, accepted death and timestamp. Virtual wrists,
// hands, head center and soles are explicitly authored primitive approximations.
public sealed class CorpseHumanoidProfile
{
    private readonly string _name;
    private readonly int[] _anchors;
    private readonly byte[] _map;
    private readonly float[] _neutralScales;
    private readonly float _size, _virtualForearm, _handLength;
    private readonly Vector3 _headCenter;
    private readonly Vector3[] _footCenters, _footForward, _footUp;
    private readonly int _pelvis;
    private CorpseHumanoidProfile(string name, int[] anchors, byte[] map, float[] scales,
        float size, float virtualForearm, Vector3 headCenter, Vector3[] centers,
        Vector3[] forward, Vector3[] up, int pelvis)
    {
        _name=name;_anchors=anchors;_map=map;_neutralScales=scales;_size=size;
        _virtualForearm=virtualForearm;_handLength=.12f*size;_headCenter=headCenter;
        _footCenters=centers;_footForward=forward;_footUp=up;_pelvis=pelvis;
    }
    public static bool TryCreate(string name, ReadOnlySpan<string> names, ReadOnlySpan<int> parents,
        ReadOnlySpan<Matrix4x4> neutralNodes, Vector3 neutralUp, Vector3 neutralForward,
        float virtualForearmLocalLength, out CorpseHumanoidProfile? profile)
    {
        profile=null;
        if(string.IsNullOrWhiteSpace(name)||name.Length>80||names.Length is < 1 or > CorpseAffineBinding.MaximumNodes
            ||neutralNodes.Length!=names.Length
            ||!Limits.Finite(neutralUp)||!Limits.Finite(neutralForward)
            ||MathF.Abs(neutralUp.LengthSquared()-1)>.001f||MathF.Abs(neutralForward.LengthSquared()-1)>.001f
            ||MathF.Abs(Vector3.Dot(neutralUp,neutralForward))>.001f
            ||!float.IsFinite(virtualForearmLocalLength)||virtualForearmLocalLength is < .1f or > .65f)return false;
        var map=new byte[names.Length];
        if(!CorpseHumanoidMap.TryMap(names,parents,map)||!Matrices(neutralNodes))return false;
        var anchors=new int[15];Array.Fill(anchors,-1);int pelvis=-1;
        for(int i=0;i<names.Length;i++)
        {
            int body=names[i] switch
            {
                "Pelvis"=>0,"Spine_2"=>1,"Head_1"=>2,"L_shoulder"=>3,"L_elbow"=>4,"L_wrist"=>5,
                "R_shoulder"=>6,"R_elbow"=>7,"R_wrist"=>8,"L_hip"=>9,"L_knee"=>10,"L_ankle"=>11,
                "R_hip"=>12,"R_knee"=>13,"R_ankle"=>14,_=>-1
            };
            if(body>=0)anchors[body]=i;
            if(names[i]=="Spine_1")pelvis=i;
        }
        if(anchors[0]>=0)pelvis=anchors[0];
        var scales=new float[names.Length];
        for(int i=0;i<anchors.Length;i++)if(anchors[i]>=0
            &&!Rigid(neutralNodes[anchors[i]],out scales[anchors[i]],out _))return false;
        if(pelvis>=0&&!Rigid(neutralNodes[pelvis],out scales[pelvis],out _))return false;
        var neutral=neutralNodes.ToArray();
        Vector3 At(int body)=>neutral[anchors[body]].Translation;
        float leg=(Vector3.Distance(At(9),At(10))+Vector3.Distance(At(10),At(11))
            +Vector3.Distance(At(12),At(13))+Vector3.Distance(At(13),At(14)))*.5f;
        float size=leg/.9855957f;
        if(!float.IsFinite(size)||size is < .5f or > 1.75f)return false;
        if(!Matrix4x4.Invert(neutralNodes[anchors[2]],out var inverseHead))return false;
        var headCenter=Vector3.Transform(At(2)+neutralUp*(.04f*size),inverseHead);
        var centers=new Vector3[2];var forward=new Vector3[2];var up=new Vector3[2];
        for(int side=0;side<2;side++)
        {
            int body=side==0?11:14;var matrix=neutralNodes[anchors[body]];
            if(!Matrix4x4.Invert(matrix,out var inverse))return false;
            centers[side]=Vector3.Transform(matrix.Translation+(-neutralUp+neutralForward)*(.055f*size),inverse);
            forward[side]=Vector3.TransformNormal(neutralForward,inverse);
            up[side]=Vector3.TransformNormal(neutralUp,inverse);
        }
        var candidate=new CorpseHumanoidProfile(name,anchors,map,scales,size,virtualForearmLocalLength,headCenter,centers,forward,up,pelvis);
        var origin=pelvis>=0?neutral[pelvis].Translation:(At(9)+At(12))*.5f;
        if(!candidate.TryCapture(neutralNodes,origin,out _))return false;
        profile=candidate;
        return true;
    }
    private static bool Matrices(ReadOnlySpan<Matrix4x4> nodes)
    {
        if(nodes.Length is < 1 or > CorpseAffineBinding.MaximumNodes)return false;
        foreach(var m in nodes)
            if(!Limits.Position(m.Translation)||m.M14!=0||m.M24!=0||m.M34!=0||m.M44!=1
                ||!Limits.Finite(new(m.M11,m.M12,m.M13))||!Limits.Finite(new(m.M21,m.M22,m.M23))
                ||!Limits.Finite(new(m.M31,m.M32,m.M33))
                ||new Vector3(m.M11,m.M12,m.M13).LengthSquared()>64
                ||new Vector3(m.M21,m.M22,m.M23).LengthSquared()>64
                ||new Vector3(m.M31,m.M32,m.M33).LengthSquared()>64)return false;
        return true;
    }
    private static bool Rigid(Matrix4x4 m,out float scale,out Quaternion orientation)
    {
        scale=0;orientation=default;
        var x=new Vector3(m.M11,m.M12,m.M13);var y=new Vector3(m.M21,m.M22,m.M23);var z=new Vector3(m.M31,m.M32,m.M33);
        scale=x.Length();if(!float.IsFinite(scale)||scale is < .05f or > 8)return false;
        x/=scale;y/=scale;z/=scale;
        if(MathF.Abs(y.LengthSquared()-1)>.005f||MathF.Abs(z.LengthSquared()-1)>.005f
            ||MathF.Abs(Vector3.Dot(x,y))>.005f||MathF.Abs(Vector3.Dot(x,z))>.005f||MathF.Abs(Vector3.Dot(y,z))>.005f
            ||Vector3.Dot(Vector3.Cross(x,y),z)<.995f)return false;
        orientation=Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(
            x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1)));
        return Limits.UnitQuaternion(orientation);
    }
    private static Quaternion Frame(Vector3 y,Vector3 hint,Vector3 alternate=default)
    {
        y=Vector3.Normalize(y);var z=hint-y*Vector3.Dot(y,hint);
        if(z.LengthSquared()<.000001f)
            z=alternate-y*Vector3.Dot(y,alternate);
        if(z.LengthSquared()<.000001f)throw new ArgumentException("Unsupported primitive roll frame.");
        z=Vector3.Normalize(z);var x=Vector3.Normalize(Vector3.Cross(y,z));z=Vector3.Cross(x,y);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new(
            x.X,x.Y,x.Z,0,y.X,y.Y,y.Z,0,z.X,z.Y,z.Z,0,0,0,0,1)));
    }
    public bool TryCapture(ReadOnlySpan<Matrix4x4> currentNodes,Vector3 worldOrigin,out CapturedCorpseRig? capture)
    {
        capture=null;
        if(currentNodes.Length!=_map.Length||!Matrices(currentNodes)||!Limits.Position(worldOrigin))return false;
        // Span parameters cannot be captured by the local construction helpers.
        // This bounded copy is once per corpse, never a sample/step allocation.
        var nodes=currentNodes.ToArray();
        for(int i=0;i<_neutralScales.Length;i++)if(_neutralScales[i]>0
            &&(!Rigid(nodes[i],out float scale,out _)||MathF.Abs(scale/_neutralScales[i]-1)>.02f))return false;
        Vector3 At(int body)=>nodes[_anchors[body]].Translation;
        Matrix4x4 Matrix(int body)=>nodes[_anchors[body]];
        var parts=new CorpsePart[15];
        Quaternion JointBasis(Quaternion frame)=>Quaternion.Normalize(frame*Quaternion.CreateFromAxisAngle(Vector3.UnitX,-MathF.PI/2));
        CorpsePart Segment(Vector3 from,Vector3 to,Matrix4x4 frameSource,float width,float mass,int parent,float swing,float twist)
        {
            var d=to-from;float length=d.Length();
            if(!float.IsFinite(length)||length is < .05f or > 1.25f)throw new ArgumentException("Unsupported posed segment length.");
            var frame=Frame(d,Vector3.TransformNormal(Vector3.UnitZ,frameSource),Vector3.TransformNormal(Vector3.UnitY,frameSource));
            return new(new((from+to)*.5f-worldOrigin,frame),new(width*_size,length,width*_size),mass,parent,from-worldOrigin,swing,twist)
                {JointBasis=JointBasis(frame)};
        }
        Vector3 Hint(int body)=>Vector3.TransformNormal(Vector3.UnitZ,Matrix(body));
        try
        {
            var pelvis=_pelvis>=0?nodes[_pelvis].Translation:(At(9)+At(12))*.5f;
            var shoulders=(At(3)+At(6))*.5f;var torso=shoulders-pelvis;
            if(torso.LengthSquared()<.0064f)return false;
            var torsoFrame=Frame(torso,Hint(1),Vector3.TransformNormal(Vector3.UnitY,Matrix(1)));
            parts[0]=new(new(pelvis-worldOrigin,torsoFrame),new Vector3(.29f,.22f,.22f)*_size,3,-1,pelvis-worldOrigin,.5f,.2f)
                {JointBasis=JointBasis(torsoFrame)};
            parts[1]=Segment(pelvis,shoulders,Matrix(1),.32f,4,0,.45f,.25f);
            Rigid(Matrix(2),out _,out var headFrame);
            parts[2]=new(new(Vector3.Transform(_headCenter,Matrix(2))-worldOrigin,headFrame),new Vector3(.2f*_size),1,1,At(2)-worldOrigin,.65f,.4f)
                {JointBasis=JointBasis(headFrame)};
            for(int side=0;side<2;side++)
            {
                int arm=side==0?3:6,hip=side==0?9:12;
                var shoulder=At(arm);var elbow=At(arm+1);var forearm=Matrix(arm+1);
                bool realWrist=_anchors[arm+2]>=0;
                var wrist=realWrist?At(arm+2):Vector3.Transform(new(_virtualForearm,0,0),forearm);
                var handFrame=realWrist?Matrix(arm+2):forearm;
                float handScale=_neutralScales[_anchors[realWrist?arm+2:arm+1]];
                var tip=realWrist?Vector3.Transform(new(_handLength/handScale,0,0),handFrame)
                    :Vector3.Transform(new(_virtualForearm+_handLength/handScale,0,0),handFrame);
                parts[arm]=Segment(shoulder,elbow,Matrix(arm),.10f,.8f,1,1.3f,.45f);
                parts[arm+1]=Segment(elbow,wrist,Matrix(arm+1),.09f,.7f,arm,1.2f,.25f);
                parts[arm+2]=Segment(wrist,tip,handFrame,.07f,.5f,arm+1,.45f,.2f);
                parts[hip]=Segment(At(hip),At(hip+1),Matrix(hip),.14f,2,0,1.1f,.3f);
                parts[hip+1]=Segment(At(hip+1),At(hip+2),Matrix(hip+1),.13f,1.5f,hip,1.2f,.2f);
                var ankle=Matrix(hip+2);
                var footFrame=Frame(Vector3.TransformNormal(_footForward[side],ankle),Vector3.TransformNormal(_footUp[side],ankle));
                parts[hip+2]=new(new(Vector3.Transform(_footCenters[side],ankle)-worldOrigin,footFrame),new Vector3(.13f,.24f,.12f)*_size,
                    .75f,hip+1,At(hip+2)-worldOrigin,.45f,.2f){JointBasis=JointBasis(footFrame)};
            }
            var rig=new CorpseRig(_name,parts);var origin=new CorpsePose(worldOrigin,Quaternion.Identity);
            var poses=new CorpsePose[15];for(int i=0;i<poses.Length;i++)poses[i]=new(worldOrigin+rig.Parts[i].Rest.Position,rig.Parts[i].Rest.Orientation);
            if(!CorpseAffineBinding.TryCapture(currentNodes,_map,poses,out var binding))return false;
            capture=new(rig,origin,binding!,poses);return true;
        }
        catch(ArgumentException){return false;}
    }
}

public sealed class CapturedCorpseRig
{
    private readonly CorpsePose[] _initial;
    public CorpseRig Rig {get;}
    public CorpsePose Origin {get;}
    public CorpseAffineBinding Binding {get;}
    public ReadOnlySpan<CorpsePose> InitialBodies=>_initial;
    internal CapturedCorpseRig(CorpseRig rig,CorpsePose origin,CorpseAffineBinding binding,CorpsePose[] initial)
    {Rig=rig;Origin=origin;Binding=binding;_initial=initial;}
}
