using System;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using MphRead.CorpsePhysics;

// Standalone contract/primitive-physics tests only. No game assembly, authentic
// death/source authority, game collision export, humanoid mesh, GL or gameplay.
public static class PilotContractChecks
{
    private static readonly CorpsePose Identity=new(Vector3.Zero,Quaternion.Identity);
    private static CorpseContext Context(uint life=1)=>new(1,2,3,0,4,life,5);
    private static CorpseKey Key(ulong n=1,CorpseContext? context=null)=>new(context??Context(),new(n,2,3,4));
    private static CorpseSpawnDescriptor Spawn(ulong n=1,uint tick=0,uint lifetime=180,CorpseContext? context=null,
        CorpsePose? origin=null,Vector3? direction=null,Vector3? velocity=null,Vector3? impact=null)
        =>new(Key(n,context),ReferenceScene.Samus(),origin??Identity,direction??Vector3.UnitZ,velocity??Vector3.Zero,impact,17,tick,lifetime);
    private static bool Throws<T>(Action action) where T:Exception
    {try{action();return false;}catch(T){return true;}}
    private static bool Finite(CorpsePose p)=>p.IsValid&&float.IsFinite(p.Matrix.M11)&&float.IsFinite(p.Matrix.M44);
    private sealed class FaultProbe : CorpseBackend
    {
        public bool ThrowSpawn,ThrowStep,ThrowRetire;public int RetireCalls,DisposeCalls;
        public override string Name=>"declared-fault-probe";
        public FaultProbe():base(CorpseQuality.High){}
        protected override void OnSpawn(int slot,CorpseSpawnDescriptor spawn){if(ThrowSpawn)throw new InvalidOperationException("declared spawn fault");}
        protected override void OnStep(){if(ThrowStep)throw new InvalidOperationException("declared step fault");}
        protected override void OnRetire(int slot){RetireCalls++;if(ThrowRetire)throw new InvalidOperationException("declared retire fault");}
        protected override void OnDispose(){DisposeCalls++;}
    }
    public static void Run(Action<bool,string> check)
    {
        check(typeof(CorpseBackend).Assembly.GetReferencedAssemblies().All(n=>n.Name!="ProjectPrime"&&!n.Name!.StartsWith("MphRead",StringComparison.Ordinal)),
            "standalone pilot binary has no game assembly dependency");
        var rig=ReferenceScene.Samus();var parts=rig.Parts.ToArray();
        check(parts.Length==15&&parts[0].Parent==-1&&parts.Skip(1).Select((p,i)=>p.Parent>=0&&p.Parent<i+1).All(x=>x),"reference rig has15 parts and exactly14 ordered tree edges");
        check(parts.Skip(1).Count()*3==42&&parts.Sum(p=>p.Mass)>0,"fourteen anatomical joints imply42 constraint descriptions");
        check(parts.All(p=>p.Rest.IsValid&&p.Mass is >=.5f and <=4&&p.Size.X is >=.03f and <=2),"reference authored parts have finite valid transforms/masses/sizes");
        var copy=new CorpseRig("immutable",parts);var original=copy.Parts[0];parts[0]=parts[0] with{Mass=2};
        check(copy.Parts[0]==original,"rig owns immutable copy independent of input array mutation");
        check(Throws<ArgumentException>(()=>new CorpseRig("",rig.Parts))&&Throws<ArgumentException>(()=>new CorpseRig(new string('x',81),rig.Parts))&&Throws<ArgumentException>(()=>new CorpseRig("bad",new CorpsePart[14])),"strict rig name and exactly15 part budget");
        foreach(var bad in new[]{original with{Mass=float.NaN},original with{Mass=.49f},original with{Mass=4.01f},original with{Size=new(.02f,1,1)},original with{Size=new(1,float.PositiveInfinity,1)},original with{Rest=new(new(5,0,0),Quaternion.Identity)},original with{Rest=new(Vector3.Zero,default)},original with{Parent=0},original with{Swing=.049f},original with{Swing=float.PositiveInfinity},original with{Twist=.009f},original with{Joint=new(float.NaN,0,0)},original with{Joint=new(2,2,2)}})
        {var invalid=rig.Parts.ToArray();invalid[0]=bad;check(Throws<ArgumentException>(()=>new CorpseRig("invalid",invalid)),"malformed finite/tree/root/joint/mass/size rig rejects atomically");}
        foreach(int parent in new[]{-1,1,14})
        {var invalid=rig.Parts.ToArray();invalid[1]=invalid[1] with{Parent=parent};check(Throws<ArgumentException>(()=>new CorpseRig("not-tree",invalid)),"nonroot parent must be an earlier valid part");}
        var tolerance=rig.Parts.ToArray();tolerance[0]=tolerance[0] with{Rest=tolerance[0].Rest with{Orientation=new Quaternion(0,0,0,1.0001f)}};
        var normalizedRig=new CorpseRig("normalized",tolerance);check(MathF.Abs(normalizedRig.Parts[0].Rest.Orientation.LengthSquared()-1)<1e-6,"accepted near-unit rig orientation is normalized in private copy");

        check(Context().IsValid&&new CorpseContext(1,0,0,0,0,1,0).IsValid,"explicit online and offline context domains are valid");
        foreach(var bad in new[]{Context() with{Scene=0},Context() with{Slot=8},Context() with{Life=0},Context() with{Epoch=0},Context() with{Generation=0},new CorpseContext(1,0,3,0,0,1,0),new CorpseContext(1,0,0,0,4,1,0)})
            check(!bad.IsValid&&Throws<ArgumentException>(()=>Spawn(context:bad)),"full context rejects absent/mixed stream or life identity");
        check(!default(CorpseEvent).IsValid&&new CorpseEvent(0,0,0,1).IsValid&&!default(CorpseHandle).IsValid&&!new CorpseHandle(1,8,1).IsValid,"opaque event and generation-bearing handle validate independently");
        check(Throws<ArgumentException>(()=>new CorpseSpawnDescriptor(default,rig,Identity,Vector3.Zero,Vector3.Zero,null,0,0)),"invalid event descriptor cannot reach backend");
        check(Throws<ArgumentNullException>(()=>new CorpseSpawnDescriptor(Key(),null!,Identity,Vector3.Zero,Vector3.Zero,null,0,0)),"null rig is rejected explicitly");
        foreach(var pose in new[]{new CorpsePose(new(float.NaN,0,0),Quaternion.Identity),new(new(8193,0,0),Quaternion.Identity),new(Vector3.Zero,default),new(Vector3.Zero,new Quaternion(0,0,0,2))})
            check(!pose.IsValid&&Throws<ArgumentException>(()=>Spawn(origin:pose)),"descriptor refuses nonfinite/out-of-world/nonunit root pose");
        foreach(var vector in new[]{new Vector3(float.NaN,0,0),new Vector3(float.PositiveInfinity,0,0),new Vector3(float.MaxValue,0,0)})
        {check(Throws<ArgumentException>(()=>Spawn(direction:vector)),"direction rejects nonfinite or overflowing length");check(Throws<ArgumentException>(()=>Spawn(velocity:vector)),"velocity rejects nonfinite or over-budget length");}
        check(Throws<ArgumentException>(()=>Spawn(velocity:new(4.01f,0,0)))&&Throws<ArgumentException>(()=>Spawn(impact:new(5,0,0))),"inherited velocity4 and impact radius4 are bounded");
        check(Throws<ArgumentException>(()=>Spawn(lifetime:0))&&Throws<ArgumentException>(()=>Spawn(lifetime:241)),"lifetime accepts only1..240 fixed ticks");
        var spawn=Spawn(direction:new(3,0,4),origin:new(new(2,3,4),Quaternion.CreateFromAxisAngle(Vector3.UnitY,.4f)));
        check(Vector3.Distance(spawn.AcceptedDirection,new(.6f,0,.8f))<1e-6&&spawn.WorldPart(2).IsValid,"descriptor normalizes accepted direction and copies finite transformed rig pose");
        check(Spawn(direction:Vector3.Zero).AcceptedDirection==Vector3.Zero&&Spawn(direction:new(1e-6f,0,0)).AcceptedDirection==Vector3.Zero,"absent/tiny direction is neutral rather than invented force");
        check(Throws<IndexOutOfRangeException>(()=>spawn.WorldPart(-1))&&Throws<IndexOutOfRangeException>(()=>spawn.WorldPart(15)),"WorldPart cannot expose outside exact15 part descriptor");

        var floor=ReferenceScene.Floor();var triangle=floor.Triangles[0];var triangles=floor.Triangles.ToArray();var ownWorld=new CorpseStaticWorld(triangles);triangles[0]=default;
        check(ownWorld.Triangles[0]==triangle&&floor.Triangles.Length==2&&ReferenceScene.Floor(true).Triangles.Length<=128,"static world copies triangles and reference floor/varied scene stay within budget");
        check(Vector3.Cross(triangle.B-triangle.A,triangle.C-triangle.A).Y>0,"public static-export convention remains CCW outward +Y before BEPU boundary conversion");
        foreach(var invalid in new[]{Array.Empty<StaticTriangle>(),new StaticTriangle[129],new[]{default(StaticTriangle)},new[]{new StaticTriangle(Vector3.Zero,Vector3.UnitX,new(100,1,0))},new[]{new StaticTriangle(new(float.NaN,0,0),Vector3.UnitX,Vector3.UnitZ)}})
            check(Throws<ArgumentException>(()=>new CorpseStaticWorld(invalid)),"empty/overbudget/degenerate/nonfinite/oversized static export refuses");

        var poses=new CorpsePose[16];var sentinel=new CorpsePose(new(7,8,9),Quaternion.Identity);
        foreach(var pair in new[]{(CorpseQuality.Off,0),(CorpseQuality.Low,2),(CorpseQuality.Medium,4),(CorpseQuality.High,8),((CorpseQuality)255,0)})
        {
            using var backend=new ProceduralDeathFallback(pair.Item1);
            for(int n=0;n<pair.Item2;n++)check(backend.TrySpawn(Spawn((ulong)n+1),out var handle)&&handle.IsValid,"configured capacity admits exact bounded corpse "+pair);
            var before=backend.Statistics;check(!backend.TrySpawn(Spawn(99),out var denied)&&denied==default&&backend.Statistics.ActiveCorpses==pair.Item2&&backend.Statistics.Spawned==before.Spawned,"Off/Low2/Medium4/High8 and unknown quality cannot evict beyond capacity");
            check(backend.Statistics.DynamicBodies==0&&backend.Statistics.JointConstraints==0,"procedural fallback creates no physics bodies or joints");
        }
        using(var emptyClock=new ProceduralDeathFallback(CorpseQuality.Low))
            check(!emptyClock.Step(0)&&emptyClock.Statistics.FixedSteps==0&&emptyClock.Step(1)&&emptyClock.Statistics.FixedSteps==1,
                "first empty fixed tick initializes clock and only following tick advances solver");
        using(var backend=new ProceduralDeathFallback(CorpseQuality.High))
        {
            check(backend.TrySpawn(Spawn(),out var first)&&!backend.Step(0)&&backend.Statistics.FixedSteps==0,"spawn tick0 initializes previous/current; same tick never simulates");
            check(backend.Step(1)&&backend.Statistics.FixedSteps==1&&!backend.Step(1),"exact next fixed tick steps once and duplicate tick is inert");
            check(backend.Sample(first,1,poses),"valid borrowed output span receives15 poses");var after=poses[..15];poses[0]=sentinel;
            check(backend.Sample(first,1,poses)&&poses[0]==after[0],"borrowed output mutation cannot alias backend storage");
            poses[15]=sentinel;check(backend.Sample(first,.5f,poses)&&poses[15]==sentinel,"Sample writes only15 logical parts, preserving span tail");
            var half=poses[..15];backend.Sample(first,0,poses);var beforePose=poses[..15];backend.Sample(first,1,poses);
            check(Enumerable.Range(0,15).All(i=>Vector3.Distance(half[i].Position,Vector3.Lerp(beforePose[i].Position,poses[i].Position,.5f))<1e-6),"render interpolation is between actual previous and current fixed poses");
            foreach(float alpha in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-1,0,.5f,1,2})
                check(backend.Sample(first,alpha,poses)&&poses.Take(15).All(Finite),"render alpha clamps malformed/out-of-range inputs to finite poses");
            var fixedBefore=backend.Statistics.FixedSteps;for(int n=0;n<10_000;n++)backend.Sample(first,(n%100)/100f,poses);
            check(backend.Statistics.FixedSteps==fixedBefore,"ten thousand draw samples never step simulation");
            for(int warm=0;warm<1000;warm++)backend.Sample(first,.5f,poses);
            long allocated=GC.GetAllocatedBytesForCurrentThread();int sampled=0;for(int draw=0;draw<100_000;draw++)if(backend.Sample(first,.5f,poses))sampled++;
            allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
            check(sampled==100_000&&allocated==0,"100k warmed caller-owned interpolation samples allocate0B");
            Array.Fill(poses,sentinel);check(!backend.Sample(first,.5f,poses.AsSpan(0,14))&&poses.All(p=>p==sentinel),"short output span refuses without partial writes");
            check(!backend.Sample(new(first.World,8,1),0,poses)&&!backend.Sample(new(first.World,first.Slot,first.Generation+1),0,poses)&&poses.All(p=>p==sentinel),"invalid/stale handles refuse without touching borrowed output");
            check(!backend.TrySpawn(Spawn(2,tick:0),out _)&&backend.Statistics.ActiveCorpses==1,"spawn must match current fixed-tick context");
            check(backend.Retire(first)&&!backend.Retire(first)&&!backend.Sample(first,0,poses),"retirement is idempotent and stale output handle cannot revive");
            check(!backend.TrySpawn(Spawn(tick:1),out _),"full context+event duplicate remains consumed after retirement");
            check(backend.TrySpawn(Spawn(2,tick:1),out var reused)&&reused!=first&&!backend.Sample(first,0,poses),"slot reuse advances handle generation and rejects previous lifetime");
            check(!backend.Step(0)&&backend.Statistics.ActiveCorpses==0&&!backend.Sample(reused,0,poses),"backward tick clears active state and invalidates old handles");
            check(backend.TrySpawn(Spawn(3,tick:0),out reused)&&!backend.Step(5)&&backend.Statistics.ActiveCorpses==0,"forward tick jump clears instead of integrating arbitrary elapsed time");
            check(backend.TrySpawn(Spawn(4,tick:5),out reused),"cleared jump rebases exact current tick for later valid spawn");backend.Clear();
            check(backend.Statistics.ActiveCorpses==0&&!backend.Sample(reused,0,poses)&&backend.TrySpawn(Spawn(tick:77),out _),"explicit Clear resets bounded identity/tick window but stale handles remain invalid");
        }
        using(var left=new ProceduralDeathFallback(CorpseQuality.Low))using(var right=new ProceduralDeathFallback(CorpseQuality.Low))
        {
            left.TrySpawn(Spawn(),out var a);right.TrySpawn(Spawn(),out var b);Array.Fill(poses,sentinel);
            check(a.World!=b.World&&a.Slot==b.Slot&&a.Generation==b.Generation,"independent worlds separate otherwise-identical slot/generation handles");
            check(!right.Sample(a,0,poses)&&!right.Retire(a)&&!left.Sample(b,0,poses)&&!left.Retire(b)&&poses.All(p=>p==sentinel),
                "actual cross-backend stale handles cannot read or retire another corpse");
        }
        using(var exhausted=new ProceduralDeathFallback(CorpseQuality.Low))
        {
            // Declared reflection boundary seam avoids billions of reuse operations.
            var slots=(Array)typeof(CorpseBackend).GetField("Slots",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(exhausted)!;
            for(int n=0;n<2;n++){var slot=slots.GetValue(n)!;slot.GetType().GetField("Generation",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(slot,uint.MaxValue);}
            check(!exhausted.TrySpawn(Spawn(),out var refused)&&refused==default&&exhausted.Statistics.ActiveCorpses==0&&exhausted.Statistics.Spawned==0,
                "exhausted generation slots refuse without wrapping or partially spawning");
        }
        foreach(var changed in new[]{Context() with{Scene=2},Context() with{Match=3},Context() with{Epoch=4},Context() with{Slot=1},Context() with{Generation=5},Context() with{Life=2},Context() with{Timeline=6}})
        {using var backend=new ProceduralDeathFallback(CorpseQuality.High);check(backend.TrySpawn(Spawn(),out _)&&backend.TrySpawn(Spawn(context:changed),out _),"each full context component separates opaque event identity");}
        using(var backend=new ProceduralDeathFallback(CorpseQuality.High))
        {check(backend.TrySpawn(Spawn(),out _)&&backend.TrySpawn(new CorpseSpawnDescriptor(new(Context(),new(1,2,3,5)),rig,Identity,Vector3.Zero,Vector3.Zero,null,1,0),out _),"full256-bit opaque event digest separates same-context accepted events");}
        using(var backend=new ProceduralDeathFallback(CorpseQuality.High))
        {
            for(ulong n=1;n<=64;n++){check(backend.TrySpawn(Spawn(n),out var h)&&backend.Retire(h),"bounded accepted event tombstone retained "+n);}
            check(!backend.TrySpawn(Spawn(65),out _)&&!backend.TrySpawn(Spawn(1),out _),"64-event seen window refuses overflow without evicting or reopening first identity");
            for(uint tick=1;tick<=241;tick++)backend.Step(tick);
            check(backend.TrySpawn(Spawn(65,tick:241),out var afterRetention)&&backend.Retire(afterRetention)
                &&!backend.TrySpawn(Spawn(65,tick:241),out _),"normal bounded tick advance releases expired seen capacity while newly accepted event remains deduplicated");
        }
        using(var backend=new ProceduralDeathFallback(CorpseQuality.Low))
        {check(backend.TrySpawn(Spawn(lifetime:2),out var h)&&backend.Step(1)&&backend.Sample(h,0,poses),"lifetime2 survives exact first tick");check(backend.Step(2)&&!backend.Sample(h,0,poses)&&backend.Statistics.ActiveCorpses==0&&backend.Statistics.Retired==1,"lifespan expires at inclusive fixed deadline");}
        using(var backend=new ProceduralDeathFallback(CorpseQuality.Low))
        {check(backend.TrySpawn(Spawn(tick:uint.MaxValue,lifetime:2),out var h)&&backend.Step(0)&&backend.Sample(h,0,poses)&&backend.Step(1)&&!backend.Sample(h,0,poses),"fixed tick and lifespan wrap uint without discontinuity");}
        using(var backend=new ProceduralDeathFallback(CorpseQuality.Low))
        {check(!backend.TrySpawn(Spawn(origin:new(new(8192,0,0),Quaternion.Identity)),out _)&&backend.Statistics.ActiveCorpses==0&&backend.Statistics.Spawned==0,"invalid transformed world part rejects atomically before slot mutation");}
        using(var backend=new FaultProbe{ThrowSpawn=true})
        {check(!backend.TrySpawn(Spawn(),out var h)&&h==default&&backend.Statistics.Faults==1&&backend.Statistics.ActiveCorpses==0&&backend.RetireCalls==1,"spawn exception quarantines backend and retires partially initialized slot");check(!backend.TrySpawn(Spawn(2),out _)&&!backend.Step(1),"faulted backend refuses all later spawn/step rather than silently recovering");}
        using(var backend=new FaultProbe())
        {backend.TrySpawn(Spawn(),out var h);backend.ThrowStep=true;check(!backend.Step(1)&&backend.Statistics.Faults==1&&backend.Statistics.ActiveCorpses==0&&!backend.Sample(h,0,poses),"step exception retires active slots and stale handles");backend.Clear();check(!backend.TrySpawn(Spawn(2),out _),"Clear cannot erase fault quarantine");}
        var publicRetire=new FaultProbe();publicRetire.TrySpawn(Spawn(),out var publicHandle);publicRetire.ThrowRetire=true;
        check(!publicRetire.Retire(publicHandle)&&publicRetire.Statistics.Faults>=1&&!publicRetire.Sample(publicHandle,0,poses)
            &&!publicRetire.TrySpawn(Spawn(2),out _)&&!publicRetire.Step(1),
            "public Retire partial failure returnsfalse and quarantines sampling/spawn/step rather than leaking an exception or usable corpse");
        check(Throws<InvalidOperationException>(()=>publicRetire.Dispose())&&publicRetire.DisposeCalls==1,
            "public Retire failure still reaches exactly-once owned teardown on disposal");
        var failedRetire=new FaultProbe();failedRetire.TrySpawn(Spawn(lifetime:1),out var pendingRetire);failedRetire.ThrowRetire=true;
        check(!failedRetire.Step(1)&&failedRetire.Statistics.Faults>=1&&!failedRetire.Sample(pendingRetire,0,poses),
            "lifespan retirement exception is caught and quarantines further sampling");
        check(Throws<InvalidOperationException>(()=>failedRetire.Dispose())&&failedRetire.DisposeCalls==1
            &&failedRetire.Statistics.ActiveCorpses==0&&Throws<ObjectDisposedException>(()=>failedRetire.Step(2)),
            "failed retirement still invokes final backend teardown once and becomes permanently disposed");
        failedRetire.Dispose();check(failedRetire.DisposeCalls==1,"second dispose cannot repeat teardown after first disposal failure");
        var failedClear=new FaultProbe();failedClear.TrySpawn(Spawn(),out _);failedClear.TrySpawn(Spawn(2),out _);failedClear.ThrowRetire=true;
        check(Throws<InvalidOperationException>(()=>failedClear.Clear())&&failedClear.RetireCalls==2,
            "Clear attempts every owned slot even when earlier retirement fails");
        check(Throws<InvalidOperationException>(()=>failedClear.Dispose())&&failedClear.DisposeCalls==1,
            "multi-slot cleanup failure cannot bypass owned backend teardown");
        var disposed=new FaultProbe();disposed.TrySpawn(Spawn(),out _);disposed.Dispose();disposed.Dispose();
        check(disposed.DisposeCalls==1&&disposed.RetireCalls==1&&Throws<ObjectDisposedException>(()=>disposed.Step(1))&&Throws<ObjectDisposedException>(()=>disposed.Sample(default,0,poses))&&Throws<ObjectDisposedException>(()=>disposed.Clear()),"Dispose releases once and rejects all later owner operations");
        using(var backend=new ProceduralDeathFallback(CorpseQuality.Low))
        {
            backend.TrySpawn(Spawn(),out var h);var before=backend.Statistics;
            bool[]? errors=null;var worker=new Thread(()=>errors=new[]{Throws<InvalidOperationException>(()=>backend.Step(1)),Throws<InvalidOperationException>(()=>backend.TrySpawn(Spawn(2),out _)),Throws<InvalidOperationException>(()=>backend.Sample(h,0,new CorpsePose[15])),Throws<InvalidOperationException>(()=>backend.Retire(h)),Throws<InvalidOperationException>(()=>backend.Clear()),Throws<InvalidOperationException>(()=>backend.Dispose()),Throws<InvalidOperationException>(()=>{_ = backend.Statistics;})});worker.Start();worker.Join();
            check(errors!=null&&errors.All(x=>x)&&backend.Statistics==before,"foreign thread cannot mutate/sample/dispose owner backend; owner can still continue");
        }
        using(var frozen=new ProceduralDeathFallback(CorpseQuality.Low,fall:false))using(var procedural=new ProceduralDeathFallback(CorpseQuality.Low))
        {
            frozen.TrySpawn(Spawn(),out var f);procedural.TrySpawn(Spawn(),out var p);var initial=new CorpsePose[15];frozen.Sample(f,1,initial);
            for(uint tick=1;tick<=60;tick++){check(frozen.Step(tick)&&procedural.Step(tick),"standalone proxy fixed-step comparison "+tick);}
            frozen.Sample(f,1,poses);check(poses.Take(15).SequenceEqual(initial),"frozen baseline remains exact copied rest poses after60 ticks");procedural.Sample(p,1,poses);
            check(poses.Take(15).All(Finite)&&!poses.Take(15).SequenceEqual(initial)&&procedural.Statistics.FixedSteps==frozen.Statistics.FixedSteps,"procedural comparison moves finite copied bodies without physics or render-driven stepping");
        }
        check(Throws<InvalidOperationException>(()=>new BepuCorpseBackend(floor,CorpseQuality.High,experimentalOptIn:false)),"Bepu nonshipping backend requires explicit opt-in");
        check(Throws<ArgumentNullException>(()=>new BepuCorpseBackend(null!,CorpseQuality.High,true)),"null collision export refuses before Bepu initialization");
        foreach(int size in new[]{0,4095,4097,2097152})check(Throws<ArgumentOutOfRangeException>(()=>new CorpsePhysicsArena(size)),"arena block size must be power-of-two4KiB..1MiB");
        using(var physicalCaps=new BepuCorpseBackend(floor,CorpseQuality.High,true))
        {
            var handles=new CorpseHandle[8];
            for(int n=0;n<8;n++)check(physicalCaps.TrySpawn(Spawn((ulong)n+1,origin:new(new((n%4-1.5f)*2,0,(n/4-.5f)*3),Quaternion.Identity)),out handles[n]),
                "real physical High cap accepts corpse "+(n+1));
            check(physicalCaps.Statistics.DynamicBodies==120&&physicalCaps.Statistics.JointConstraints==336&&physicalCaps.SolverConstraints==336
                &&!physicalCaps.TrySpawn(Spawn(99),out _),"physical High is capped at8 corpses/120 bodies/336 anatomical constraints");
            foreach(var handle in handles)check(physicalCaps.Retire(handle),"bounded physical High corpse retirement");
            check(physicalCaps.Statistics.ActiveCorpses==0&&physicalCaps.Statistics.DynamicBodies==0&&physicalCaps.SolverConstraints==0,
                "retiring all8 physical corpses leaves no live bodies or constraints");
        }
        CorpseHandle retiredWorldHandle=default;
        using(var arena=new CorpsePhysicsArena())
        {
            using(var physics=new BepuCorpseBackend(floor,CorpseQuality.Low,true,arena))
            {
                check(Throws<InvalidOperationException>(()=>new BepuCorpseBackend(floor,CorpseQuality.Low,true,arena))&&Throws<InvalidOperationException>(()=>arena.Dispose()),"private arena cannot be shared by live worlds or cleared while leased");
                check(physics.TrySpawn(Spawn(lifetime:180,direction:new(.2f,0,.3f)),out var h)&&physics.Statistics.DynamicBodies==15&&physics.Statistics.JointConstraints==42&&physics.SolverConstraints==42,"real Bepu spawn owns15 dynamics and42 actual anatomical constraints");
                retiredWorldHandle=h;
                check(!physics.Step(0)&&physics.Statistics.FixedSteps==0,"real Bepu spawn tick does not integrate early");
                for(uint tick=1;tick<=120;tick++)
                {
                    check(physics.Step(tick)&&physics.Sample(h,1,poses)&&poses.Take(15).All(Finite),"real single-thread Bepu floor step finite through120: "+tick);
                    if(poses.Take(15).Any(p=>p.Position.Y<-.1f))
                        Console.WriteLine("FLOOR DIAGNOSTIC tick="+tick+" stats="+physics.Statistics+" solver="+physics.SolverConstraints
                            +" parts="+string.Join(";",poses.Take(15).Select((p,i)=>i+":"+p.Position)));
                    check(poses.Take(15).All(p=>p.Position.Y>=-.1f),"actual exported floor prevents primitive centers falling through: "+tick);
                }
                check(physics.Statistics.ActiveCorpses==1&&physics.Statistics.FixedSteps==120&&physics.Statistics.Faults==0&&physics.Statistics.DynamicBodies==15&&physics.Statistics.JointConstraints==42,"floor world remains alive without quarantine after120 real physics ticks");
                check(physics.Statistics.ReservedPoolBytes<=BepuCorpseBackend.MaximumPoolBytes&&physics.SolverConstraints<=42+BepuCorpseBackend.MaximumContactConstraints,"observed post-step pool/contact tripwire thresholds remain untriggered (not hard admission caps)");
                var steps=physics.Statistics.FixedSteps;for(int draw=0;draw<10_000;draw++)physics.Sample(h,draw%101/100f,poses);
                check(physics.Statistics.FixedSteps==steps,"real Bepu interpolation draws never advance solver");
                check(physics.Retire(h)&&physics.Statistics.DynamicBodies==0&&physics.Statistics.JointConstraints==0&&physics.SolverConstraints==0&&!physics.Sample(h,0,poses),"physical retirement removes bodies/constraints and invalidates handle");
            }
            using var replacement=new BepuCorpseBackend(floor,CorpseQuality.Low,true,arena);
            check(replacement.Statistics.DynamicBodies==0,"disposed world releases arena lease for fresh independent world");
            check(replacement.TrySpawn(Spawn(),out var newWorldHandle)&&newWorldHandle.World!=retiredWorldHandle.World
                &&!replacement.Sample(retiredWorldHandle,0,poses)&&!replacement.Retire(retiredWorldHandle),
                "reusing same native pool cannot alias disposed world's slot/generation handle");
        }
        foreach(var placement in new[]{(Name:"ramp",Origin:new Vector3(2,1,3)),(Name:"steps",Origin:new Vector3(2.5f,1,-3.5f))})
        {
            using var varied=new BepuCorpseBackend(ReferenceScene.Floor(varied:true),CorpseQuality.Low,true);
            check(varied.TrySpawn(Spawn(origin:new(placement.Origin,Quaternion.Identity),direction:Vector3.Zero),out var h),
                "varied static mesh admits real reference corpse above "+placement.Name);
            int maximumContacts=0;
            for(uint tick=1;tick<=120;tick++)
            {
                check(varied.Step(tick)&&varied.Sample(h,1,poses)&&poses.Take(15).All(p=>Finite(p)&&p.Position.Y>=-.1f),
                    "finite no-flat-floor-fallthrough simulation on varied "+placement.Name+" mesh: "+tick);
                maximumContacts=Math.Max(maximumContacts,varied.SolverConstraints-42);
            }
            Console.WriteLine("VARIED DIAGNOSTIC placement="+placement.Name+" contacts="+maximumContacts
                +" parts="+string.Join(";",poses.Take(15).Select((p,i)=>i+":"+p.Position)));
            check(maximumContacts>0&&varied.Statistics.DynamicBodies==15&&varied.Statistics.Faults==0,
                "real varied "+placement.Name+" mesh generated contacts and retained15 finite dynamics without fault");
        }
        MeasureStability(check);
    }
    // Measured solver quality, not a relaxed gameplay/art/physics acceptance bound.
    // Shared BallSocket offsets are independently reconstructed from public immutable
    // rest poses and joint points; no solver internal state is queried or changed.
    public static void MeasureStability(Action<bool,string> check)
    {
        var rig=ReferenceScene.Samus();var offsetsA=new Vector3[15];var offsetsB=new Vector3[15];
        var reference=new CorpsePose[2][];double maximumEndpointPosition=0,maximumEndpointOrientation=0;
        double maximumMidpointPosition=0,maximumMidpointOrientation=0;
        var summaries=new System.Collections.Generic.List<object>();
        foreach(int draws in new[]{1,2,4})
        {
            using var backend=new BepuCorpseBackend(ReferenceScene.Floor(varied:true),CorpseQuality.High,true);
            var descriptors=new CorpseSpawnDescriptor[8];var handles=new CorpseHandle[8];
            var last=new CorpsePose[8][];var current=new CorpsePose[15];var before=new CorpsePose[15];var middle=new CorpsePose[15];
            for(int n=0;n<8;n++)
            {
                var origin=new CorpsePose(new((n%4-1.5f)*2,1,(n/4-.5f)*6),Quaternion.Identity);
                descriptors[n]=new(Key((ulong)n+1,Context() with{Slot=(byte)n}),rig,origin,new(.15f,0,.3f),Vector3.Zero,null,(uint)(17+n),0,240);
                check(backend.TrySpawn(descriptors[n],out handles[n]),"stability run admits actual varied-mesh body "+n+" at draw ratio "+draws);
                last[n]=Enumerable.Range(0,15).Select(descriptors[n].WorldPart).ToArray();
            }
            for(int part=1;part<15;part++)
            {
                var p=rig.Parts[part];var a=descriptors[0].WorldPart(p.Parent);var b=descriptors[0].WorldPart(part);
                var joint=descriptors[0].Origin.Position+Vector3.Transform(p.Joint,descriptors[0].Origin.Orientation);
                offsetsA[part]=Vector3.Transform(joint-a.Position,Quaternion.Conjugate(a.Orientation));
                offsetsB[part]=Vector3.Transform(joint-b.Position,Quaternion.Conjugate(b.Orientation));
            }
            float maximum120=0,maximum180=0;int worstCorpse=-1,worstPart=-1;uint worstTick=0;
            for(uint tick=1;tick<=180;tick++)
            {
                check(backend.Step(tick),"actual8-corpse stability fixed tick "+tick+" at draw ratio "+draws);
                var endpoint=new CorpsePose[8*15];
                for(int n=0;n<8;n++)
                {
                    if(!backend.Sample(handles[n],0,before)||!backend.Sample(handles[n],1,current)||!backend.Sample(handles[n],.5f,middle))
                        throw new InvalidOperationException("Stability sample unavailable tick="+tick+" corpse="+n);
                    for(int part=0;part<15;part++)
                    {
                        if(!Finite(current[part]))throw new InvalidOperationException("Nonfinite stability output");
                        maximumEndpointPosition=Math.Max(maximumEndpointPosition,Vector3.Distance(before[part].Position,last[n][part].Position));
                        maximumEndpointOrientation=Math.Max(maximumEndpointOrientation,QuaternionDistance(before[part].Orientation,last[n][part].Orientation));
                        maximumMidpointPosition=Math.Max(maximumMidpointPosition,Vector3.Distance(middle[part].Position,Vector3.Lerp(before[part].Position,current[part].Position,.5f)));
                        maximumMidpointOrientation=Math.Max(maximumMidpointOrientation,QuaternionDistance(middle[part].Orientation,Quaternion.Slerp(before[part].Orientation,current[part].Orientation,.5f)));
                        endpoint[n*15+part]=current[part];
                        if(part==0)continue;
                        int parent=rig.Parts[part].Parent;
                        Vector3 a=current[parent].Position+Vector3.Transform(offsetsA[part],current[parent].Orientation);
                        Vector3 b=current[part].Position+Vector3.Transform(offsetsB[part],current[part].Orientation);
                        float error=Vector3.Distance(a,b);
                        if(tick<=120)maximum120=Math.Max(maximum120,error);
                        if(error>maximum180){maximum180=error;worstCorpse=n;worstPart=part;worstTick=tick;}
                    }
                    // Display60/120/240 analogue: extra samples use caller-owned
                    // arrays and must have no route to Step or the solver state.
                    for(int draw=0;draw<draws;draw++)backend.Sample(handles[n],draw/(float)draws,middle);
                    Array.Copy(current,last[n],15);
                }
                if(tick is 120 or 180)
                {
                    int index=tick==120?0:1;
                    if(draws==1)reference[index]=endpoint;
                    else check(reference[index].SequenceEqual(endpoint),"exact physical state at"+tick+" is invariant under1/2/4 render samples per fixed tick");
                }
            }
            check(backend.Statistics.FixedSteps==180&&backend.Statistics.DynamicBodies==120&&backend.Statistics.JointConstraints==336&&backend.Statistics.Faults==0,
                "8-corpse varied-mesh stability run retains exact physical counts without fault");
            summaries.Add(new{drawsPerFixedTick=draws,maximumBallSocketErrorThrough120=maximum120,maximumBallSocketErrorThrough180=maximum180,worstCorpse,worstPart,worstTick});
        }
        check(maximumEndpointPosition<=1e-5&&maximumEndpointOrientation<=1e-5&&maximumMidpointPosition<=1e-5&&maximumMidpointOrientation<=1e-5,
            "borrowed interpolation endpoints/midpoints agree within floating-point roundoff independently of solver joint error");
        Console.WriteLine("PILOT STABILITY MEASUREMENTS "+System.Text.Json.JsonSerializer.Serialize(new{runs=summaries,maximumEndpointPosition,maximumEndpointOrientation,maximumMidpointPosition,maximumMidpointOrientation,
            scope="Actual standalone8-corpse varied static world; measured BallSocket error without physical-quality threshold. Copied pinned pilot solver, no game/GL/Android acceptance."}));
    }
    private static double QuaternionDistance(Quaternion a,Quaternion b)
    {
        var direct=new Vector4(a.X-b.X,a.Y-b.Y,a.Z-b.Z,a.W-b.W).Length();
        var antipodal=new Vector4(a.X+b.X,a.Y+b.Y,a.Z+b.Z,a.W+b.W).Length();
        return Math.Min(direct,antipodal);
    }
}
