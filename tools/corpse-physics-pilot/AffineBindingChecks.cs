using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using MphRead.CorpsePhysics;

// Standalone affine math only. Synthetic composed skin matrices and rigid frames;
// no model, native rig binding, rendering, authority, collision or physics claim.
public static class AffineBindingChecks
{
    private static readonly Vector3[] Points={Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,new(.35f,-.7f,1.2f),new(-2,.4f,.8f)};
    private static Matrix4x4 Frame(CorpsePose p)
    {var m=Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(p.Orientation));m.Translation=p.Position;return m;}
    private static bool Near(Matrix4x4 actual,Matrix4x4 expected)
    {
        foreach(var point in Points)
        {
            var a=Vector3.Transform(point,actual);var e=Vector3.Transform(point,expected);
            float size=MathF.Max(1,MathF.Max(MathF.Abs(e.X),MathF.Max(MathF.Abs(e.Y),MathF.Abs(e.Z))));
            if(!float.IsFinite(a.LengthSquared())||Vector3.Distance(a,e)>5e-6f*size)return false;
        }
        return true;
    }
    private static CorpsePose[] Bodies()=>Enumerable.Range(0,15).Select(i=>new CorpsePose(new(i*.2f-1,1+i*.07f,-i*.13f),
        Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(i*.11f,i*-.07f,i*.03f)))).ToArray();
    private static CorpsePose[] IdentityBodies()=>Enumerable.Repeat(new CorpsePose(Vector3.Zero,Quaternion.Identity),15).ToArray();
    private static Matrix4x4 Offset(int i)
    {
        // Nonuniform signed scale and shear: these are intentionally not rigid.
        var m=new Matrix4x4((i%2==0?1:-1)*1.5f,.17f,-.08f,0,.23f,.7f,.13f,0,-.11f,.19f,1.2f,0,0,0,0,1);
        m.Translation=new((i%5-2)*.12f,(i%3-1)*.15f,(i%7-3)*.08f);return m;
    }
    private static Matrix4x4 Set(Matrix4x4 m,int field,float value)
    {
        switch(field)
        {case 0:m.M11=value;break;case 1:m.M12=value;break;case 2:m.M13=value;break;case 3:m.M14=value;break;
         case 4:m.M21=value;break;case 5:m.M22=value;break;case 6:m.M23=value;break;case 7:m.M24=value;break;
         case 8:m.M31=value;break;case 9:m.M32=value;break;case 10:m.M33=value;break;case 11:m.M34=value;break;
         case 12:m.M41=value;break;case 13:m.M42=value;break;case 14:m.M43=value;break;case 15:m.M44=value;break;}
        return m;
    }
    private static CorpsePose Move(CorpsePose pose,Matrix4x4 world)
    {
        var frame=Frame(pose)*world;
        return new(frame.Translation,Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(frame)));
    }
    public static void Run(Action<bool,string> check)
    {
        var initial=Bodies();var map=Enumerable.Range(0,37).Select(i=>(byte)(i%15)).ToArray();
        var offsets=Enumerable.Range(0,map.Length).Select(Offset).ToArray();
        var nodes=offsets.Select((m,i)=>m*Frame(initial[map[i]])).ToArray();
        var savedNodes=(Matrix4x4[])nodes.Clone();var savedBodies=(CorpsePose[])initial.Clone();var savedMap=(byte[])map.Clone();
        check(CorpseAffineBinding.MaximumNodes==128&&CorpseAffineBinding.TryCapture(nodes,map,initial,out var binding)&&binding!=null&&binding.NodeCount==37,
            "capture owns exact bounded node count and accepts full affine scale/shear");
        var output=new Matrix4x4[40];var sentinel=Matrix4x4.CreateScale(-3)*Matrix4x4.CreateTranslation(7,8,9);Array.Fill(output,sentinel);
        check(binding!.TrySample(initial,output)&&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i]))&&output.Skip(nodes.Length).All(m=>m==sentinel),
            "rest reconstruction preserves affine-transformed origin/basis/probe points and span tail");
        check(output.Take(nodes.Length).Any(m=>m.GetDeterminant()<0)&&output.Take(nodes.Length).Any(m=>MathF.Abs(Vector3.Dot(new(m.M11,m.M12,m.M13),new(m.M21,m.M22,m.M23)))>.01f),
            "negative determinant and genuine native shear survive rather than being decomposed into rigid TRS");
        var moved=initial.Select((p,i)=>new CorpsePose(p.Position+new Vector3(i*.01f,.3f,-.2f),Quaternion.Normalize(Quaternion.CreateFromYawPitchRoll(.2f+i*.03f,-.1f,.25f)))).ToArray();
        check(binding.TrySample(moved,output)&&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],offsets[i]*Frame(moved[map[i]]))),
            "independently moved15 body frames preserve each known residual native affine mapping");
        var global=Matrix4x4.CreateFromYawPitchRoll(.7f,-.35f,.22f)*Matrix4x4.CreateTranslation(11,-3,5);
        var common=initial.Select(p=>Move(p,global)).ToArray();
        check(binding.TrySample(common,output)&&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i]*global)),
            "common rigid world motion covaries complete native affine matrices");
        var transformedNodes=nodes.Select(m=>m*global).ToArray();
        check(CorpseAffineBinding.TryCapture(transformedNodes,map,common,out var transformed)&&transformed!.TrySample(common,output)
            &&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],transformedNodes[i])),"capture itself is covariant under a common rigid world change");
        var negated=initial.Select(p=>p with{Orientation=new(-p.Orientation.X,-p.Orientation.Y,-p.Orientation.Z,-p.Orientation.W)}).ToArray();
        check(CorpseAffineBinding.TryCapture(nodes,map,negated,out var negative)&&negative!.TrySample(negated,output)
            &&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i])),"quaternion sign aliases produce the same residual affine rest pose");
        var nearUnit=initial.Select(p=>p with{Orientation=p.Orientation*1.0002f}).ToArray();
        CorpseAffineBinding? near=null;
        check(nearUnit.All(p=>p.IsValid)&&CorpseAffineBinding.TryCapture(nodes,map,nearUnit,out near)&&near!.TrySample(nearUnit,output)
            &&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i])),"accepted near-unit frames normalize consistently during capture and sample");
        check(near!.TrySample(initial,output)&&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i])),"normalized and accepted near-unit frame representations do not change binding offsets");
        nodes[0]=default;map[0]=14;initial[0]=default;
        check(binding.TrySample(savedBodies,output)&&Enumerable.Range(0,savedNodes.Length).All(i=>Near(output[i],savedNodes[i]))&&binding.NodeCount==37,
            "later mutation of all capture input arrays cannot alias immutable binding");
        output[0]=default;check(binding.TrySample(savedBodies,output)&&Near(output[0],savedNodes[0]),"caller output mutation cannot alias stored offsets");
        nodes=(Matrix4x4[])savedNodes.Clone();map=(byte[])savedMap.Clone();initial=(CorpsePose[])savedBodies.Clone();

        foreach(int count in new[]{1,128})
        {
            var many=Enumerable.Range(0,count).Select(i=>Offset(i)*Frame(initial[i%15])).ToArray();var manyMap=Enumerable.Range(0,count).Select(i=>(byte)(i%15)).ToArray();
            check(CorpseAffineBinding.TryCapture(many,manyMap,initial,out var bound)&&bound!.NodeCount==count&&bound.TrySample(initial,new Matrix4x4[count]),
                "inclusive node-count span boundary captures/samples "+count);
        }
        foreach(int count in new[]{0,129})check(!CorpseAffineBinding.TryCapture(new Matrix4x4[count],new byte[count],initial,out var invalid)&&invalid==null,
            "node count outside1..128 refuses without a binding");
        foreach(int count in new[]{0,36,38})check(!CorpseAffineBinding.TryCapture(nodes,new byte[count],initial,out var invalid)&&invalid==null,
            "node-to-body span length must match node span exactly");
        foreach(int count in new[]{0,14,16})check(!CorpseAffineBinding.TryCapture(nodes,map,new CorpsePose[count],out var invalid)&&invalid==null,
            "capture requires exactly15 initial rigid body frames");
        var badMap=(byte[])map.Clone();badMap[^1]=15;
        check(!CorpseAffineBinding.TryCapture(nodes,badMap,initial,out var badBinding)&&badBinding==null,"late invalid body mapping rejects entire capture");
        foreach(float invalidNumber in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity})for(int field=0;field<16;field++)
        {
            var malformed=(Matrix4x4[])nodes.Clone();malformed[^1]=Set(malformed[^1],field,invalidNumber);
            check(!CorpseAffineBinding.TryCapture(malformed,map,initial,out var invalid)&&invalid==null,"every nonfinite affine matrix component refuses capture field="+field);
        }
        foreach(var malformed in new[]{Set(Matrix4x4.Identity,3,float.Epsilon),Set(Matrix4x4.Identity,7,float.Epsilon),Set(Matrix4x4.Identity,11,float.Epsilon),Set(Matrix4x4.Identity,15,MathF.BitIncrement(1))})
            check(!CorpseAffineBinding.TryCapture(new[]{malformed},new byte[1],IdentityBodies(),out var invalid)&&invalid==null,"perspective/projective tail cannot enter full-affine binding");
        foreach(var badPose in new[]{new CorpsePose(new(float.NaN,0,0),Quaternion.Identity),new(new(8193,0,0),Quaternion.Identity),new(Vector3.Zero,default),new(Vector3.Zero,new(float.NaN,0,0,1)),new(Vector3.Zero,new(0,0,float.PositiveInfinity,1)),new(Vector3.Zero,new(0,0,0,1.001f))})
        {
            var malformed=IdentityBodies();malformed[^1]=badPose;
            check(!CorpseAffineBinding.TryCapture(new[]{Matrix4x4.Identity},new byte[1],malformed,out var invalid)&&invalid==null,
                "all15 initial frames must be valid even when invalid last body is unreferenced");
            Array.Fill(output,sentinel);
            check(!binding.TrySample(malformed,output)&&output.All(m=>m==sentinel),"late invalid sampled frame refuses without partial output");
        }
        foreach(float sign in new[]{-1f,1f})
        {
            var body=IdentityBodies();body[0]=new(new(sign*8192,0,0),Quaternion.Identity);var node=Matrix4x4.CreateTranslation(body[0].Position);
            check(CorpseAffineBinding.TryCapture(new[]{node},new byte[1],body,out var edge)&&edge!.TrySample(body,output)&&Near(output[0],node),"inclusive8192 world-coordinate boundary is accepted");
            node.Translation=new(sign*8193,0,0);
            check(!CorpseAffineBinding.TryCapture(new[]{node},new byte[1],body,out var invalid)&&invalid==null,"outside8192 world-coordinate boundary refuses capture");
        }
        foreach(float edge in new[]{8f,-8f})
        {var node=Matrix4x4.CreateScale(edge,1,1);check(CorpseAffineBinding.TryCapture(new[]{node},new byte[1],IdentityBodies(),out var b)&&b!.TrySample(IdentityBodies(),output)&&Near(output[0],node),"inclusive signed native row-length8 bound survives binding");}
        foreach(var node in new[]{Matrix4x4.CreateScale(MathF.BitIncrement(8),1,1),Matrix4x4.Identity with{M11=6,M12=6},Matrix4x4.CreateTranslation(MathF.BitIncrement(4),0,0),Matrix4x4.CreateTranslation(3,3,0)})
            check(!CorpseAffineBinding.TryCapture(new[]{node},new byte[1],IdentityBodies(),out var invalid)&&invalid==null,"over-budget linear row or local-offset norm refuses capture");
        var edgeOffset=Matrix4x4.CreateTranslation(4,0,0);
        check(CorpseAffineBinding.TryCapture(new[]{edgeOffset},new byte[1],IdentityBodies(),out var offsetEdge)&&offsetEdge!.TrySample(IdentityBodies(),output)&&Near(output[0],edgeOffset),"inclusive local-offset norm4 boundary remains valid");
        var singular=Matrix4x4.CreateScale(0,.7f,-1.3f);singular.M12=.3f;
        check(CorpseAffineBinding.TryCapture(new[]{singular},new byte[1],IdentityBodies(),out var flat)&&flat!.TrySample(IdentityBodies(),output)&&Near(output[0],singular),"singular native affine linear part is preserved without requiring native matrix inversion");

        foreach(int count in new[]{0,14,16})
        {Array.Fill(output,sentinel);check(!binding.TrySample(new CorpsePose[count],output)&&output.All(m=>m==sentinel),"sample body span must be exactly15 with atomic refusal");}
        Array.Fill(output,sentinel);check(!binding.TrySample(initial,output.AsSpan(0,binding.NodeCount-1))&&output.All(m=>m==sentinel),"short destination refuses before any output write");
        var lateNodes=new[]{Matrix4x4.Identity,Matrix4x4.CreateTranslation(4,0,0)};byte[] lateMap={0,14};
        check(CorpseAffineBinding.TryCapture(lateNodes,lateMap,IdentityBodies(),out var late),"late output-bounds fixture captures valid full affine inputs");
        var lateBodies=IdentityBodies();lateBodies[14]=new(new(8191,0,0),Quaternion.Identity);Array.Fill(output,sentinel);
        check(!late!.TrySample(lateBodies,output)&&output.All(m=>m==sentinel),"valid rigid frames producing out-of-world final node reject before valid output prefix is copied");
        check(binding.TrySample(initial,output)&&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i])),"failed unrelated bindings cannot poison existing immutable mapping");

        for(int n=0;n<2000;n++)binding.TrySample(initial,output);
        long allocated=GC.GetAllocatedBytesForCurrentThread();int sampled=0;
        for(int n=0;n<100_000;n++)if(binding.TrySample(initial,output))sampled++;
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(sampled==100_000&&allocated==0,"100k warmed affine samples allocate0B using borrowed caller storage");
        var workerResults=new bool[4];var threads=new Thread[workerResults.Length];
        for(int worker=0;worker<threads.Length;worker++)
        {
            int index=worker;threads[worker]=new Thread(()=>
            {
                var target=new Matrix4x4[nodes.Length];bool valid=true;
                for(int n=0;n<1000;n++)
                {valid&=binding.TrySample(moved,target);for(int i=0;i<target.Length;i++)valid&=Near(target[i],offsets[i]*Frame(moved[map[i]]));}
                workerResults[index]=valid;
            });threads[worker].Start();
        }
        foreach(var thread in threads)thread.Join();
        check(workerResults.All(x=>x)&&binding.TrySample(initial,output)&&Enumerable.Range(0,nodes.Length).All(i=>Near(output[i],nodes[i])),
            "four concurrent readers with independent destinations preserve immutable affine mapping and original rest state");
        check(initial.SequenceEqual(savedBodies)&&map.SequenceEqual(savedMap)&&nodes.SequenceEqual(savedNodes),"capture/sample never rewrites supplied body/map/node inputs");
    }
}
