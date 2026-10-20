using System;
using System.Linq;
using System.Numerics;
using MphRead.CorpsePhysics;

// Synthetic named trees/composed matrices only. These checks do not load or
// qualify any native hunter, skin, animation, collision export or game caller.
public static class HumanoidFitChecks
{
    private sealed record Fixture(string[] Names,int[] Parents,Matrix4x4[] Nodes);
    private static Fixture Shape(bool leftWrist=true,bool rightWrist=false,float scale=1,int count=19)
    {
        string[] names={"Dummy_Root","Skeleton_Root","Pelvis","L_hip","L_knee","L_ankle","R_hip","R_knee","R_ankle","Spine_1","Spine_2","Head_1","L_shoulder","L_elbow","L_wrist","L_varias2_SDK","R_shoulder","R_elbow","R_varias2_SDK"};
        int[] parents={-1,0,1,2,3,4,2,6,7,1,9,10,10,12,13,10,10,16,10};
        if(!leftWrist)names[14]="LeftForearmAttachment";
        if(rightWrist){names=names.Append("R_wrist").ToArray();parents=parents.Append(17).ToArray();}
        Vector3[] at={Vector3.Zero,Vector3.Zero,new(0,1.184f,-.039f),new(-.107f,1.095f,-.026f),new(-.107f,.627f,-.064f),new(-.107f,.115f,0),new(.107f,1.095f,-.026f),new(.107f,.627f,-.064f),new(.107f,.115f,0),new(0,1.25f,-.04f),new(0,1.4f,-.04f),new(0,1.664f,-.064f),new(-.295f,1.515f,-.045f),new(-.530f,1.508f,-.034f),new(-.811f,1.497f,-.049f),Vector3.Zero,new(.295f,1.515f,-.045f),new(.530f,1.508f,-.034f),Vector3.Zero};
        if(rightWrist)at=at.Append(new Vector3(.811f,1.497f,-.049f)).ToArray();
        int length=Math.Max(count,names.Length);
        names=names.Concat(Enumerable.Range(names.Length,length-names.Length).Select(i=>"Helper"+i)).ToArray();
        parents=parents.Concat(Enumerable.Repeat(0,length-parents.Length)).ToArray();
        var nodes=new Matrix4x4[length];
        for(int i=0;i<length;i++)
        {
            Quaternion q=i is 12 or 13 or 14?Quaternion.CreateFromAxisAngle(Vector3.UnitZ,MathF.PI):Quaternion.Identity;
            nodes[i]=Matrix4x4.CreateScale(scale)*Matrix4x4.CreateFromQuaternion(q)*Matrix4x4.CreateTranslation((i<at.Length?at[i]:Vector3.Zero)*scale);
        }
        return new(names,parents,nodes);
    }
    private static bool Create(Fixture f,out CorpseHumanoidProfile? profile,float forearm=.32f)
        =>CorpseHumanoidProfile.TryCreate("Synthetic fit",f.Names,f.Parents,f.Nodes,Vector3.UnitY,Vector3.UnitZ,forearm,out profile);
    private static bool Throws<T>(Action action) where T:Exception {try{action();return false;}catch(T){return true;}}
    private static bool Near(Vector3 a,Vector3 b,float tolerance=3e-5f)=>Vector3.Distance(a,b)<=tolerance*MathF.Max(1,MathF.Max(a.Length(),b.Length()));
    private static bool Rotation(Quaternion a,Quaternion b)=>MathF.Abs(Quaternion.Dot(a,b))>1-2e-5f;
    private static bool MatrixNear(Matrix4x4 a,Matrix4x4 b)
        =>new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,new Vector3(.7f,-.3f,.9f)}.All(p=>Near(Vector3.Transform(p,a),Vector3.Transform(p,b)));
    private static Vector3 Endpoint(CorpsePart p,bool end)=>p.Rest.Position+Vector3.Transform(Vector3.UnitY*(p.Size.Y*(end?.5f:-.5f)),p.Rest.Orientation);
    public static void Run(Action<bool,string> check)
    {
        var f=Shape();var original=(Matrix4x4[])f.Nodes.Clone();
        check(Create(f,out var profile)&&profile!=null,"finite anatomical synthetic tree calibrates with real left and virtual right wrist");
        check(profile!.TryCapture(f.Nodes,Vector3.Zero,out var first)&&first!=null,"calibrated current composed pose captures15 independent rigid proxies");
        var captured=first!;var parts=captured.Rig.Parts.ToArray();var bodies=captured.InitialBodies.ToArray();
        check(parts.Length==15&&bodies.Length==15&&parts.All(p=>p.Rest.IsValid&&p.Mass>=.5f&&p.Size.X>=.03f),"fitted rig supplies15 finite bounded rigid parts");
        check(parts.All(p=>float.IsFinite(p.JointBasis.LengthSquared())&&MathF.Abs(p.JointBasis.LengthSquared()-1)<1e-6f)
            &&parts.Select(p=>p.JointBasis).Distinct().Count()>3&&parts.Count(p=>!Rotation(p.JointBasis,Quaternion.Identity))>10,
            "fitted common joint bases are normalized and genuinely differ across anatomical axes");
        foreach(int i in new[]{1,3,4,5,6,7,8,9,10,12,13})
            check(Near(Vector3.Transform(Vector3.UnitZ,parts[i].JointBasis),Vector3.Transform(Vector3.UnitY,parts[i].Rest.Orientation)),
                "fitted twist axis follows each segment longitudinal axis part="+i);
        check(ReferenceScene.Samus().Parts.ToArray().All(p=>p.JointBasis==Quaternion.Identity),"legacy primitive pilot retains exact identity joint policy");
        var sampled=new Matrix4x4[f.Nodes.Length+2];sampled[^1]=Matrix4x4.CreateTranslation(99,98,97);var sentinel=sampled[^1];
        check(captured.Binding.TrySample(captured.InitialBodies,sampled)&&f.Nodes.Select((m,i)=>MatrixNear(m,sampled[i])).All(x=>x)&&sampled[^1]==sentinel,
            "affine binding reconstructs all rest composed matrices and never writes destination tail");
        check(profile.TryCapture(f.Nodes,Vector3.Zero,out var independent)&&independent!=null&&!ReferenceEquals(independent,captured)&&!ReferenceEquals(independent.Rig,captured.Rig)&&!ReferenceEquals(independent.Binding,captured.Binding),
            "independent captures own independent rig/binding storage");
        var savedBodies=(CorpsePose[])bodies.Clone();bodies[0]=default;
        check(captured.InitialBodies.SequenceEqual(savedBodies)&&captured.Rig.Parts.SequenceEqual(parts),"mutating copies never changes captured body or part storage");
        // Profile/calibration must survive caller metadata and matrix mutation.
        Array.Fill(f.Names,"Mutated");Array.Fill(f.Parents,-1);Array.Fill(f.Nodes,default);
        check(profile.TryCapture(original,Vector3.Zero,out var afterMutation)&&afterMutation!=null&&afterMutation.Rig.Parts.SequenceEqual(parts),
            "profile owns calibration/map values rather than retaining caller arrays");
        var current=(Matrix4x4[])original.Clone();
        var elbowTurn=Quaternion.CreateFromAxisAngle(Vector3.UnitZ,-.8f);current[17]=Matrix4x4.CreateFromQuaternion(elbowTurn)*Matrix4x4.CreateTranslation(original[17].Translation);
        check(profile.TryCapture(current,Vector3.Zero,out var bent)&&bent!=null,"rotated elbow with fixed shoulder captures a virtual wrist");
        var expectedWrist=Vector3.Transform(new Vector3(.32f,0,0),current[17]);
        check(Near(bent!.Rig.Parts[8].Joint,expectedWrist)&&Near(Endpoint(bent.Rig.Parts[7],true),expectedWrist)
            &&Near(bent.Rig.Parts[6].Rest.Position,parts[6].Rest.Position)&&!Near(bent.Rig.Parts[7].Rest.Position,parts[7].Rest.Position),
            "virtual wrist and hand follow current elbow rotation while shoulder/upper arm stay fixed");
        var headTurn=Quaternion.CreateFromYawPitchRoll(.4f,-.2f,.1f);var footTurn=Quaternion.CreateFromAxisAngle(Vector3.UnitY,.7f);
        current=(Matrix4x4[])original.Clone();current[11]=Matrix4x4.CreateFromQuaternion(headTurn)*Matrix4x4.CreateTranslation(original[11].Translation);
        current[5]=Matrix4x4.CreateFromQuaternion(footTurn)*Matrix4x4.CreateTranslation(original[5].Translation);
        check(profile.TryCapture(current,Vector3.Zero,out var turned)&&turned!=null,"current head and ankle orientations can differ from calibration");
        var headOffset=parts[2].Rest.Position-original[11].Translation;var footOffset=parts[11].Rest.Position-original[5].Translation;
        check(Near(turned!.Rig.Parts[2].Rest.Position,original[11].Translation+Vector3.Transform(headOffset,headTurn))&&Rotation(turned.Rig.Parts[2].Rest.Orientation,headTurn),
            "head center and rigid orientation follow current head frame rather than bind/world axes");
        check(Near(turned.Rig.Parts[11].Rest.Position,original[5].Translation+Vector3.Transform(footOffset,footTurn))
            &&Near(Vector3.Transform(Vector3.UnitY,turned.Rig.Parts[11].Rest.Orientation),Vector3.Transform(Vector3.Transform(Vector3.UnitY,parts[11].Rest.Orientation),footTurn)),
            "foot sole center and longitudinal frame follow current ankle transform");
        var rolled=(Matrix4x4[])original.Clone();
        rolled[14]=Matrix4x4.CreateRotationX(.7f)*original[14];
        check(profile.TryCapture(rolled,Vector3.Zero,out var wristRoll)&&wristRoll!=null
            &&!Rotation(wristRoll.Rig.Parts[5].Rest.Orientation,parts[5].Rest.Orientation)
            &&Enumerable.Range(0,15).All(i=>Near(wristRoll.Rig.Parts[i].Rest.Position,parts[i].Rest.Position)),
            "real wrist axial roll changes fitted hand orientation without inventing translation or elbow movement");
        var aligned=(Matrix4x4[])original.Clone();aligned[17].Translation=aligned[16].Translation+Vector3.UnitZ*.235f;
        var commonTurn=Quaternion.CreateFromYawPitchRoll(.5f,.4f,.3f);var commonWorld=Matrix4x4.CreateFromQuaternion(commonTurn);
        check(profile.TryCapture(aligned,Vector3.Zero,out var alignedCapture)&&alignedCapture!=null
            &&profile.TryCapture(aligned.Select(m=>m*commonWorld).ToArray(),Vector3.Zero,out var rotatedAligned)&&rotatedAligned!=null
            &&Enumerable.Range(0,15).All(i=>Near(rotatedAligned.InitialBodies[i].Position,Vector3.Transform(alignedCapture.InitialBodies[i].Position,commonTurn))
                &&Rotation(rotatedAligned.InitialBodies[i].Orientation,Quaternion.Normalize(commonTurn*alignedCapture.InitialBodies[i].Orientation))),
            "segment parallel to primary roll hint uses current bone alternate, preserving common rigid covariance");
        foreach(var wrists in new[]{(Left:false,Right:false),(Left:true,Right:true),(Left:false,Right:true)})
        {
            var v=Shape(wrists.Left,wrists.Right);
            check(Create(v,out var p)&&p!.TryCapture(v.Nodes,Vector3.Zero,out var c)&&c!=null&&c.InitialBodies.Length==15,
                "optional real/virtual left-right wrists fit independently left="+wrists.Left+" right="+wrists.Right);
            if(!wrists.Left)
            {
                Create(v,out p);p!.TryCapture(v.Nodes,Vector3.Zero,out var before);var changed=(Matrix4x4[])v.Nodes.Clone();var turn=Quaternion.CreateFromYawPitchRoll(.2f,.4f,.7f);
                changed[13]=Matrix4x4.CreateFromQuaternion(turn)*Matrix4x4.CreateTranslation(changed[13].Translation);
                check(p.TryCapture(changed,Vector3.Zero,out c)&&c!=null&&Near(c.Rig.Parts[5].Joint,Vector3.Transform(new Vector3(.32f,0,0),changed[13]))
                    &&Near(c.Rig.Parts[3].Rest.Position,before!.Rig.Parts[3].Rest.Position),"missing left wrist follows rotated left elbow independent of fixed shoulder");
            }
        }
        foreach(float scale in new[]{1f,.706f})
        {
            var v=Shape(scale:scale);check(Create(v,out var p)&&p!=null,"inclusive supported native-like calibration scale="+scale);
            check(p!.TryCapture(v.Nodes,Vector3.Zero,out var baseline)&&baseline!=null,"scaled calibration captures without double scaling="+scale);
            var rotation=Quaternion.CreateFromYawPitchRoll(.8f,-.3f,.4f);var origin=new Vector3(27,-30,19);var world=Matrix4x4.CreateFromQuaternion(rotation)*Matrix4x4.CreateTranslation(origin);
            var transformed=v.Nodes.Select(n=>n*world).ToArray();
            check(p.TryCapture(transformed,origin,out var moved)&&moved!=null&&moved.Origin.Position==origin&&moved.Origin.Orientation==Quaternion.Identity,
                "world-rotated current matrices use translation-only spawn origin and no double rotation/scale="+scale);
            for(int i=0;i<15;i++)check(Near(moved!.InitialBodies[i].Position,Vector3.Transform(baseline!.InitialBodies[i].Position,world))
                &&Rotation(moved.InitialBodies[i].Orientation,Quaternion.Normalize(rotation*baseline.InitialBodies[i].Orientation))
                &&Rotation(moved.Rig.Parts[i].JointBasis,Quaternion.Normalize(rotation*baseline.Rig.Parts[i].JointBasis)),
                "all body frames covary once under common world rigid transform scale="+scale+" part="+i);
            var restore=new Matrix4x4[transformed.Length];
            check(moved!.Binding.TrySample(moved.InitialBodies,restore)&&transformed.Select((m,i)=>MatrixNear(m,restore[i])).All(x=>x),"scaled/rotated world capture restores complete native affine matrices="+scale);
        }
        var many=Shape(count:128);check(Create(many,out var max)&&max!.TryCapture(many.Nodes,Vector3.Zero,out var maxCapture)&&maxCapture!.Binding.NodeCount==128,"inclusive128 composed nodes calibrate/capture with bounded helper map");
        var over=Shape(count:129);check(!Create(over,out var refused)&&refused==null,"129 calibration nodes refuse without partial profile");
        var valid=Shape();
        foreach(float forearm in new[]{.1f,.65f})check(Create(valid,out _,forearm),"inclusive virtual forearm calibration length accepts="+forearm);
        foreach(float forearm in new[]{.099f,.651f,float.NaN,float.PositiveInfinity})check(!Create(valid,out refused,forearm)&&refused==null,"invalid virtual forearm calibration refuses atomically");
        void RejectCalibration(Fixture v,string label)=>check(!Create(v,out var p)&&p==null,label+" calibration refuses atomically");
        RejectCalibration(valid with {Nodes=valid.Nodes[..^1]},"wrong matrix count");
        RejectCalibration(valid with {Nodes=Array.Empty<Matrix4x4>()},"empty matrix span");
        foreach(int anchor in new[]{2,3,4,5,6,7,8,10,11,12,13,14,16,17})
        foreach(string fault in new[]{"mirror","shear","singular"})
        {
            var nodes=(Matrix4x4[])valid.Nodes.Clone();var m=nodes[anchor];
            if(fault=="mirror"){m.M11=-m.M11;m.M12=-m.M12;m.M13=-m.M13;}
            if(fault=="shear")m.M21+=.12f;
            if(fault=="singular"){m.M11=0;m.M12=0;m.M13=0;}
            nodes[anchor]=m;RejectCalibration(valid with{Nodes=nodes},fault+" anchor="+anchor);
            check(!profile.TryCapture(nodes,Vector3.Zero,out var c)&&c==null,fault+" current anchor refuses atomically="+anchor);
        }
        var withoutPelvisNames=(string[])valid.Names.Clone();withoutPelvisNames[2]="BodyRoot";
        var withoutPelvis=valid with{Names=withoutPelvisNames};
        check(Create(withoutPelvis,out var spinePelvis)&&spinePelvis!.TryCapture(withoutPelvis.Nodes,Vector3.Zero,out _),"Spine_1 can supply actual pelvis anchor when explicit Pelvis is absent");
        var reflectedSpine=(Matrix4x4[])valid.Nodes.Clone();reflectedSpine[9].M11=-1;
        RejectCalibration(withoutPelvis with{Nodes=reflectedSpine},"mirror of active Spine_1 fallback pelvis");
        foreach(string fault in new[]{"nan","infinity","perspective","homogeneous","world","row"})
        {
            var nodes=(Matrix4x4[])valid.Nodes.Clone();var m=nodes[0];
            if(fault=="nan")m.M23=float.NaN;if(fault=="infinity")m.M32=float.PositiveInfinity;if(fault=="perspective")m.M14=.01f;
            if(fault=="homogeneous")m.M44=0;if(fault=="world")m.M41=8193;if(fault=="row")m.M11=8.01f;
            nodes[0]=m;RejectCalibration(valid with{Nodes=nodes},fault+" helper matrix");
            check(!profile.TryCapture(nodes,Vector3.Zero,out var c)&&c==null,fault+" current helper refuses atomically");
        }
        foreach(var up in new[]{Vector3.Zero,new Vector3(float.NaN),Vector3.UnitY*1.01f,Vector3.UnitZ})
            check(!CorpseHumanoidProfile.TryCreate("x",valid.Names,valid.Parents,valid.Nodes,up,Vector3.UnitZ,.32f,out refused)&&refused==null,"malformed/nonorthogonal anatomical axis refuses");
        foreach(string? name in new string?[]{null,"",new('x',81)})
            check(!CorpseHumanoidProfile.TryCreate(name!,valid.Names,valid.Parents,valid.Nodes,Vector3.UnitY,Vector3.UnitZ,.32f,out refused)&&refused==null,"invalid profile label refuses");
        foreach(var origin in new[]{new Vector3(float.NaN),new Vector3(float.PositiveInfinity),new Vector3(8193,0,0)})
            check(!profile.TryCapture(valid.Nodes,origin,out var c)&&c==null,"invalid world origin refuses without captured storage");
        check(!profile.TryCapture(valid.Nodes[..^1],Vector3.Zero,out var shortCapture)&&shortCapture==null,"current matrix count must match calibrated map exactly");
        foreach(float badScale in new[]{.97f,1.03f})
        {var nodes=(Matrix4x4[])valid.Nodes.Clone();nodes[11]=Matrix4x4.CreateScale(badScale)*Matrix4x4.CreateTranslation(nodes[11].Translation);check(!profile.TryCapture(nodes,Vector3.Zero,out var c)&&c==null,"current anchor scale drift outside2percent refuses");}
        var malformedNeutral=(Matrix4x4[])valid.Nodes.Clone();malformedNeutral[4].Translation=malformedNeutral[3].Translation;
        RejectCalibration(valid with{Nodes=malformedNeutral},"coincident neutral hip/knee despite valid total leg scale");
        malformedNeutral=(Matrix4x4[])valid.Nodes.Clone();malformedNeutral[12].Translation=malformedNeutral[13].Translation;
        RejectCalibration(valid with{Nodes=malformedNeutral},"coincident neutral shoulder/elbow despite valid total leg scale");
        malformedNeutral=(Matrix4x4[])valid.Nodes.Clone();malformedNeutral[14].Translation=malformedNeutral[13].Translation;
        RejectCalibration(valid with{Nodes=malformedNeutral},"coincident neutral real wrist/elbow despite valid total leg scale");
        var collapsed=(Matrix4x4[])valid.Nodes.Clone();collapsed[4].Translation=collapsed[3].Translation;
        check(!profile.TryCapture(collapsed,Vector3.Zero,out var invalid)&&invalid==null,"zero posed thigh segment refuses without publishing rig/binding");
        var stretched=(Matrix4x4[])valid.Nodes.Clone();stretched[4].Translation=stretched[3].Translation+new Vector3(0,-1.26f,0);
        check(!profile.TryCapture(stretched,Vector3.Zero,out invalid)&&invalid==null,"oversized posed segment refuses without partial captured result");
        var affineHelper=(Matrix4x4[])valid.Nodes.Clone();affineHelper[15]=Matrix4x4.CreateScale(1.7f,.6f,1.2f);affineHelper[15].M21=.23f;
        check(Create(valid with{Nodes=affineHelper},out var affineProfile)&&affineProfile!.TryCapture(affineHelper,Vector3.Zero,out var affineCapture)&&affineCapture!=null,
            "nonanchor helper retains supported full affine scale/shear rather than imposing rigid physics frame on every skin node");
        Create(valid with{Nodes=affineHelper},out affineProfile);affineProfile!.TryCapture(affineHelper,Vector3.Zero,out var helperCapture);var helperOutput=new Matrix4x4[affineHelper.Length];
        check(helperCapture!.Binding.TrySample(helperCapture.InitialBodies,helperOutput)&&affineHelper.Select((m,i)=>MatrixNear(m,helperOutput[i])).All(x=>x),
            "fitted capture preserves complete helper affine matrix at rest, including shear");
        var changedCalibration=Shape(scale:.4f);RejectCalibration(changedCalibration,"under minimum anatomical size");
        changedCalibration=Shape(scale:1.8f);RejectCalibration(changedCalibration,"over maximum anatomical size");
        foreach(var q in new[]{Quaternion.Zero,new Quaternion(float.NaN,0,0,1),new Quaternion(0,0,0,float.PositiveInfinity),new Quaternion(0,0,0,2)})
        {var malformed=ReferenceScene.Samus().Parts.ToArray();malformed[7]=malformed[7] with{JointBasis=q};check(Throws<ArgumentException>(()=>new CorpseRig("bad basis",malformed)),"zero/nonfinite/nonunit common joint basis refuses");}
        foreach(var q in new[]{Quaternion.Identity,-Quaternion.CreateFromAxisAngle(Vector3.UnitY,.6f),Quaternion.Identity*1.0001f})
        {var accepted=ReferenceScene.Samus().Parts.ToArray();accepted[7]=accepted[7] with{JointBasis=q};var rig=new CorpseRig("normalized basis",accepted);check(MathF.Abs(rig.Parts[7].JointBasis.LengthSquared()-1)<1e-6f,"unit/sign/near-unit joint basis accepts normalized copied value");}
        // Solver smoke uses fitted frames rather than ReferenceScene's default
        // global axes. Actual accepted contacts + 120 fixed ticks, no pose injection.
        using var physics=new BepuCorpseBackend(ReferenceScene.Floor(),CorpseQuality.Low,true);
        var key=new CorpseKey(new(1,0,0,0,0,1,0),new(17,2,3,4));
        check(physics.TrySpawn(new(key,captured.Rig,captured.Origin,Vector3.Zero,Vector3.Zero,null,17,0,180),out var handle),"actual BEPU accepts nonidentity per-joint fitted rig");
        var pose=new CorpsePose[15];int contacts=0;
        for(uint tick=1;tick<=120;tick++)
        {
            check(physics.Step(tick)&&physics.Sample(handle,1,pose)&&pose.All(p=>p.IsValid&&p.Position.Y>=-.1f),"fitted solver floor remains finite without fallthrough tick="+tick);
            contacts=Math.Max(contacts,physics.SolverConstraints-42);
        }
        check(contacts>0&&physics.Statistics.Faults==0&&physics.Statistics.DynamicBodies==15&&physics.Statistics.JointConstraints==42,"fitted15-part/42-joint backend creates actual contacts and retains owned world");
        var reconstruct=new Matrix4x4[original.Length];check(captured.Binding.TrySample(pose,reconstruct)&&reconstruct.All(m=>float.IsFinite(m.Translation.LengthSquared())),"actual stepped fitted bodies still sample original affine node binding");
        Console.WriteLine("FITTED CONTACT DIAGNOSTIC maxContactConstraints="+contacts+" fixedSteps="+physics.Statistics.FixedSteps);
    }
}
