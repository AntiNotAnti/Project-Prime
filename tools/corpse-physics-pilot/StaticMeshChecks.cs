using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using MphRead.CorpsePhysics;

// Synthetic copied geometry only. The actual BEPU smoke below consumes the
// builder's world; it does not qualify native room export, dynamic platforms,
// game collision/authentication, rendering or a shipping physics backend.
public static class StaticMeshChecks
{
    private static readonly Vector4 Up=new(0,1,0,0);
    private static Vector3[] Quad()=>new[]{new Vector3(-2,0,-2),new Vector3(-2,0,2),new Vector3(2,0,2),new Vector3(2,0,-2)};
    private static Vector3[] Polygon(int count,float radius=2)=>Enumerable.Range(0,count)
        .Select(i=>new Vector3(MathF.Cos(i*2*MathF.PI/count)*radius,0,MathF.Sin(i*2*MathF.PI/count)*radius)).ToArray();
    private static bool Throws<T>(Action action) where T:Exception
    {try{action();return false;}catch(T){return true;}}
    private static bool Valid(StaticTriangle t,Vector3 normal)=>float.IsFinite(t.A.LengthSquared())&&float.IsFinite(t.B.LengthSquared())&&float.IsFinite(t.C.LengthSquared())
        &&Vector3.Dot(Vector3.Cross(t.B-t.A,t.C-t.A),normal)>0;
    private static float Area(CorpseStaticWorld world)=>world.Triangles.ToArray().Sum(t=>Vector3.Cross(t.B-t.A,t.C-t.A).Length()*.5f);
    public static void Run(Action<bool,string> check)
    {
        check(CorpseStaticMeshBuilder.MaximumFaces==8192&&CorpseStaticMeshBuilder.MaximumFaceVertices==32&&CorpseStaticWorld.MaximumTriangles==128,
            "static capture has explicit8192 face/32 vertex/128 triangle budgets");
        foreach(var center in new[]{new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0),new Vector3(8193,0,0)})
            check(Throws<ArgumentException>(()=>new CorpseStaticMeshBuilder(center,2)),"nonfinite/out-of-world capture center refuses");
        foreach(float extent in new[]{float.NaN,float.PositiveInfinity,0,.99f,16.01f})
            check(Throws<ArgumentException>(()=>new CorpseStaticMeshBuilder(Vector3.Zero,extent)),"half extent outside finite1..16 refuses");
        check(new CorpseStaticMeshBuilder(Vector3.Zero,1).VisitedFaces==0&&new CorpseStaticMeshBuilder(new(8192,0,0),16).StagedTriangles==0,
            "inclusive center/extent boundaries create empty bounded capture owners");
        foreach(var placement in new[]{(Reversed:false,Offset:Vector3.Zero),(Reversed:true,Offset:new Vector3(100,7,-50)),(Reversed:false,Offset:new Vector3(-40,-30,25))})
        {
            bool reversed=placement.Reversed;var offset=placement.Offset;var points=Quad().Select(p=>p+offset).ToArray();if(reversed)Array.Reverse(points);
            var original=(Vector3[])points.Clone();var builder=new CorpseStaticMeshBuilder(offset,2);
            check(builder.TryAddFace(points,new(0,1,0,offset.Y))==CorpseSurfaceResult.Added&&builder.StagedTriangles==2&&builder.VisitedFaces==1&&!builder.Failed,
                "either authored floor winding adds exactly two triangles with translated plane agreement");
            Array.Fill(points,new Vector3(float.NaN));
            check(builder.TryBuild(out var world)&&world!=null&&world.Triangles.Length==2&&world.Triangles.ToArray().All(t=>Valid(t,Vector3.UnitY))
                &&MathF.Abs(Area(world)-16)<1e-5f&&world.Triangles.ToArray().All(t=>original.Contains(t.A)&&original.Contains(t.B)&&original.Contains(t.C)),
                "built floor is canonical CCW to plane and owns unchanged input vertices without repair/aliasing");
            var saved=world!.Triangles.ToArray();
            check(builder.TryAddFace(Quad(),Up)==CorpseSurfaceResult.Rejected&&!builder.Failed&&builder.StagedTriangles==2&&builder.VisitedFaces==1,
                "sealed Add refuses without poisoning already-published immutable world or counters");
            check(!builder.TryBuild(out var second)&&second==null&&world.Triangles.SequenceEqual(saved),"TryBuild is one-shot and later failure cannot mutate first copied world");
            var copied=world.Triangles.ToArray();copied[0]=default;check(world.Triangles.SequenceEqual(saved),"caller triangle copies cannot alias published world storage");
            bool portable=false;var reader=new Thread(()=>portable=world.Triangles.Length==2&&world.Triangles.ToArray().All(t=>Valid(t,Vector3.UnitY)));reader.Start();reader.Join();
            check(portable,"immutable published world is portable to a different read-only thread");
            using var physics=new BepuCorpseBackend(world,CorpseQuality.Low,experimentalOptIn:true);
            var key=new CorpseKey(new(1,0,0,0,0,1,0),new(1,2,3,4));
            check(physics.TrySpawn(new(key,ReferenceScene.Samus(),new(offset,Quaternion.Identity),Vector3.Zero,Vector3.Zero,null,17,0,180),out var handle),
                "actual BEPU accepts copied builder world and original primitive mannequin");
            var poses=new CorpsePose[15];int contacts=0;
            for(uint tick=1;tick<=120;tick++)
            {
                check(physics.Step(tick)&&physics.Sample(handle,1,poses)&&poses.All(p=>p.IsValid&&p.Position.Y>=offset.Y-.1f),
                    "actual builder floor prevents fallthrough with finite15-part body, reversed="+reversed+" originY="+offset.Y+" tick="+tick);
                contacts=Math.Max(contacts,physics.SolverConstraints-42);
            }
            check(contacts>0&&physics.Statistics.Faults==0&&physics.Statistics.DynamicBodies==15&&physics.Statistics.JointConstraints==42,
                "actual builder-produced floor generates contacts without backend quarantine");
        }
        // Real unsupported free fall, not an injected pose/tick shortcut: a
        // distant copied floor lets gravity exercise the relative12-unit guard.
        var distant=new CorpseStaticMeshBuilder(new(100,-100,0),2);distant.TryAddFace(Quad().Select(p=>p+new Vector3(100,-100,0)).ToArray(),new(0,1,0,-100));
        check(distant.TryBuild(out var distantWorld),"relative fall-guard fixture owns valid distant static geometry");
        foreach(float birthY in new[]{0f,-30f})
        {
            using var falling=new BepuCorpseBackend(distantWorld!,CorpseQuality.Low,true);
            var key=new CorpseKey(new(1,0,0,0,0,1,0),new(8,2,3,4));
            check(falling.TrySpawn(new(key,ReferenceScene.Samus(),new(new Vector3(0,birthY,0),Quaternion.Identity),Vector3.Zero,Vector3.Zero,null,17,0,240),out var h),
                "relative fall guard admits finite corpse at original birth height "+birthY);
            uint retiredAt=0;float lastMinimum=birthY;var sample=new CorpsePose[15];
            for(uint tick=1;tick<240;tick++)
            {
                if(!falling.Step(tick))throw new InvalidOperationException("Unexpected backend fault in fall-guard fixture");
                if(!falling.Sample(h,1,sample)){retiredAt=tick;break;}
                lastMinimum=sample.Min(p=>p.Position.Y);
                check(lastMinimum>=birthY-12,"alive free-fall samples remain inside birth-relative12-unit limit at birthY="+birthY);
            }
            Console.WriteLine("RELATIVE FALL DIAGNOSTIC birthY="+birthY+" retiredAt="+retiredAt+" lastMinimum="+lastMinimum);
            check(retiredAt>60&&retiredAt<240&&falling.Statistics.ActiveCorpses==0&&falling.Statistics.Retired==1&&falling.Statistics.Faults==0,
                "actual gravity retires unsupported corpse after relative fall rather than absolute negative-room height or lifetime expiry");
        }
        var negativePlane=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        check(negativePlane.TryAddFace(Quad(),new(0,-1,0,0))==CorpseSurfaceResult.Added&&negativePlane.TryBuild(out var down)
            &&down!.Triangles.ToArray().All(t=>Valid(t,-Vector3.UnitY)),"stored solid normal determines CCW side independently of incoming vertex order");
        var nearUnit=new CorpseStaticMeshBuilder(new(0,7,0),2);
        check(nearUnit.TryAddFace(Quad().Select(p=>p+new Vector3(0,7,0)).ToArray(),new(0,.9975f,0,7*.9975f))==CorpseSurfaceResult.Added,
            "near-unit plane normal and distance normalize consistently");
        var planeEdge=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        check(planeEdge.TryAddFace(Quad(),new(0,1,0,CorpseStaticMeshBuilder.MaximumPlaneError))==CorpseSurfaceResult.Added,
            "inclusive plane agreement tolerance accepts original vertices without projection repair");
        var worldEdge=new CorpseStaticMeshBuilder(new(8192,0,0),2);
        check(worldEdge.TryAddFace(new[]{new Vector3(8190,0,-1),new Vector3(8190,0,1),new Vector3(8192,0,1),new Vector3(8192,0,-1)},Up)==CorpseSurfaceResult.Added,
            "inclusive8192 vertex coordinate remains valid at capture-region edge");
        var longFace=new[]{new Vector3(-1,0,-.5f),new Vector3(-1,0,.5f),new Vector3(20,0,.5f),new Vector3(20,0,-.5f)};
        var crossing=new CorpseStaticMeshBuilder(Vector3.Zero,1);
        check(crossing.TryAddFace(longFace,Up)==CorpseSurfaceResult.Added&&crossing.TryBuild(out var crossed)&&crossed!.Triangles.Length==2,
            "long bounds-crossing face with centroid outside region is retained by its intersecting AABB");
        var far=new CorpseStaticMeshBuilder(Vector3.Zero,1);var farPoints=Quad().Select(p=>p+new Vector3(10,0,0)).ToArray();
        check(far.TryAddFace(farPoints,new(float.NaN))==CorpseSurfaceResult.Skipped&&!far.Failed&&far.VisitedFaces==1,
            "valid finite far geometry is skipped before irrelevant remote plane validation");
        check(far.TryAddFace(Array.Empty<Vector3>(),new(float.NaN),ignorePlayers:true)==CorpseSurfaceResult.Skipped&&far.VisitedFaces==2&&!far.Failed,
            "trusted IgnorePlayers source skips geometry but still consumes visited-face budget");
        var touching=new[]{new Vector3(1,0,-.5f),new Vector3(1,0,.5f),new Vector3(2,0,.5f),new Vector3(2,0,-.5f)};
        check(new CorpseStaticMeshBuilder(Vector3.Zero,1).TryAddFace(touching,Up)==CorpseSurfaceResult.Added,"AABB touching capture boundary is nearby inclusively");
        var justOutside=touching.Select(p=>p+new Vector3(MathF.BitIncrement(1)-1,0,0)).ToArray();
        check(new CorpseStaticMeshBuilder(Vector3.Zero,1).TryAddFace(justOutside,Up)==CorpseSurfaceResult.Skipped,"adjacent representable AABB beyond capture boundary skips");
        void Reject(Vector3[] points,Vector4 plane,string label)
        {
            var builder=new CorpseStaticMeshBuilder(Vector3.Zero,2);builder.TryAddFace(Quad(),Up);
            check(builder.TryAddFace(points,plane)==CorpseSurfaceResult.Rejected&&builder.Failed&&builder.StagedTriangles==0,label+" latches failure and clears earlier staged geometry");
            int visited=builder.VisitedFaces;
            check(!builder.TryBuild(out var world)&&world==null&&builder.TryAddFace(Quad(),Up)==CorpseSurfaceResult.Rejected&&builder.VisitedFaces==visited,
                label+" cannot publish a truncated prefix or resume failed capture");
        }
        foreach(var invalid in new[]{Array.Empty<Vector3>(),new Vector3[2],Polygon(33),Quad().Select((p,i)=>i==3?new Vector3(float.NaN,0,0):p).ToArray(),
            Quad().Select((p,i)=>i==3?new Vector3(0,0,float.PositiveInfinity):p).ToArray(),new[]{new Vector3(8193,0,0),new Vector3(8193,0,1),new Vector3(8194,0,0)}})
            Reject(invalid,Up,"invalid count/nonfinite/out-of-world face");
        foreach(var plane in new[]{new Vector4(0,0,0,0),new(0,.99f,0,0),new(0,1.01f,0,0),new(float.NaN,1,0,0),new(0,1,0,float.PositiveInfinity),new(0,1,0,.01f)})
            Reject(Quad(),plane,"nearby nonunit/nonfinite/disagreeing plane");
        Reject(Quad(),new(0,1,0,MathF.BitIncrement(CorpseStaticMeshBuilder.MaximumPlaneError)),"adjacent plane disagreement beyond inclusive tolerance");
        var warped=Quad();warped[2].Y=.01f;Reject(warped,Up,"warped quad beyond plane agreement tolerance");
        var warpedCollinear=new[]{new Vector3(-1,0,-1),new Vector3(0,.001f,-1),new Vector3(1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1)};
        Reject(warpedCollinear,Up,"within-plane-tolerance warped fan with zero projected area");
        var inward=(Vector3[])warpedCollinear.Clone();inward[1].Z=MathF.BitIncrement(-1f);
        Reject(inward,Up,"within-plane-tolerance inward fan opposing stored normal");
        Reject(new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(1,0,1),new Vector3(2,0,2),new Vector3(0,0,2)},Up,"concave polygon");
        var pentagon=Polygon(5);Reject(new[]{pentagon[0],pentagon[2],pentagon[4],pentagon[1],pentagon[3]},Up,"self-intersecting consistent-turn star");
        Reject(new[]{new Vector3(-1,0,-1),new Vector3(1,0,1),new Vector3(-1,0,1),new Vector3(1,0,-1)},Up,"self-crossing bowtie");
        var quad=Quad();Reject(quad.Append(quad[0]).ToArray(),Up,"duplicated closing vertex");Reject(new[]{quad[0],quad[1],quad[1],quad[2],quad[3]},Up,"consecutive duplicate vertex");
        Reject(new[]{quad[0],quad[1],quad[2],quad[1],quad[3]},Up,"nonconsecutive duplicate vertex");
        Reject(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitX*2},Up,"collinear zero-area triangle");
        Reject(new[]{Vector3.Zero,new Vector3(.04f,0,0),new Vector3(0,0,.01f)},Up,"tiny degenerate fan triangle");
        Reject(new[]{new Vector3(-33,0,0),new Vector3(33,0,0),new Vector3(0,0,1)},Up,"edge longer than64");
        Reject(new[]{new Vector3(-24,0,-24),new Vector3(-24,0,24),new Vector3(24,0,24),new Vector3(24,0,-24)},Up,"fan diagonal longer than64 despite short edges");
        var edge=new CorpseStaticMeshBuilder(Vector3.Zero,1);
        check(edge.TryAddFace(new[]{new Vector3(-32,0,0),new Vector3(32,0,0),new Vector3(0,0,32)},Up)==CorpseSurfaceResult.Added,
            "inclusive64 maximum edge remains valid with nondegenerate fan");
        var maxVertices=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        check(maxVertices.TryAddFace(Polygon(32),Up)==CorpseSurfaceResult.Added&&maxVertices.StagedTriangles==30,"inclusive32-vertex convex face triangulates to30 triangles");
        var exactly=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        for(int i=0;i<64;i++)check(exactly.TryAddFace(Quad(),Up)==CorpseSurfaceResult.Added,"exact triangle budget accepts authored face "+i);
        check(exactly.StagedTriangles==128&&exactly.TryBuild(out var full)&&full!.Triangles.Length==128,"exact128 triangle budget publishes complete immutable world");
        var overflow=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        for(int i=0;i<64;i++)overflow.TryAddFace(Quad(),Up);
        check(overflow.TryAddFace(new[]{Vector3.Zero,Vector3.UnitX,Vector3.UnitZ},Up)==CorpseSurfaceResult.Rejected&&overflow.Failed&&overflow.StagedTriangles==0&&!overflow.TryBuild(out _),
            "129th triangle clears staging and refuses whole world rather than truncating");
        var visitedLimit=new CorpseStaticMeshBuilder(Vector3.Zero,2);visitedLimit.TryAddFace(Quad(),Up);
        for(int i=1;i<8192;i++)visitedLimit.TryAddFace(Array.Empty<Vector3>(),default,ignorePlayers:true);
        check(visitedLimit.VisitedFaces==8192&&visitedLimit.StagedTriangles==2&&!visitedLimit.Failed&&visitedLimit.TryBuild(out _),"exact8192 visited faces includes ignored faces and can publish existing staged world");
        var visitedOverflow=new CorpseStaticMeshBuilder(Vector3.Zero,2);visitedOverflow.TryAddFace(Quad(),Up);
        for(int i=1;i<8192;i++)visitedOverflow.TryAddFace(Array.Empty<Vector3>(),default,ignorePlayers:true);
        check(visitedOverflow.TryAddFace(Array.Empty<Vector3>(),default,ignorePlayers:true)==CorpseSurfaceResult.Rejected&&visitedOverflow.Failed&&visitedOverflow.StagedTriangles==0&&!visitedOverflow.TryBuild(out _),
            "8193rd even-ignored face fails entire bounded capture without truncated world");
        var empty=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        check(!empty.TryBuild(out var emptyWorld)&&emptyWorld==null&&!empty.Failed&&empty.TryAddFace(Quad(),Up)==CorpseSurfaceResult.Rejected,
            "empty build fails once and seals rather than becoming a mutable late world");
        var owner=new CorpseStaticMeshBuilder(Vector3.Zero,2);owner.TryAddFace(Quad(),Up);bool[]? wrongThread=null;
        CorpseStaticWorld? retained=new(new[]{new StaticTriangle(Vector3.Zero,Vector3.UnitZ,Vector3.UnitX)});var originalRetained=retained;
        var thread=new Thread(()=>wrongThread=new[]{Throws<InvalidOperationException>(()=>owner.TryAddFace(quad,Up)),Throws<InvalidOperationException>(()=>owner.TryBuild(out retained)),
            Throws<InvalidOperationException>(()=>{_ = owner.Failed;}),Throws<InvalidOperationException>(()=>{_ = owner.VisitedFaces;}),Throws<InvalidOperationException>(()=>{_ = owner.StagedTriangles;})});thread.Start();thread.Join();
        check(wrongThread!=null&&wrongThread.All(b=>b)&&ReferenceEquals(retained,originalRetained)&&owner.VisitedFaces==1&&owner.StagedTriangles==2&&!owner.Failed&&owner.TryBuild(out _),
            "foreign thread operations/stats throw before any builder state/output mutation; owner still publishes");
        // Warm separate owner before measuring only successful bounded additions.
        var warm=new CorpseStaticMeshBuilder(Vector3.Zero,2);warm.TryAddFace(quad,Up);var noGc=new CorpseStaticMeshBuilder(Vector3.Zero,2);
        long allocated=GC.GetAllocatedBytesForCurrentThread();int added=0;for(int i=0;i<64;i++)if(noGc.TryAddFace(quad,Up)==CorpseSurfaceResult.Added)added++;
        allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
        check(allocated==0&&added==64&&noGc.StagedTriangles==128,"64 warmed successful face additions allocate0B without using failed/empty paths");
    }
}
