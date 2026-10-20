using System.Security.Cryptography;
using System.Text.Json;
using MphRead;
using MphRead.CorpsePhysics;
using MphRead.Entities;
using MphRead.Formats;
using MphRead.Formats.Collision;
using MphRead.Mods;
using MphRead.Mods.CustomWeapons;
using MphRead.Mods.Input;
using MphRead.Mods.Network;
using OpenTK.Mathematics;
using V=System.Numerics.Vector3;
using P=System.Numerics.Vector4;
using Q=System.Numerics.Quaternion;

// Actual filtered room data, no simulation/native collision queries. Rejection
// is a normal fallback. Independent BEPU uses a declared synthetic primitive
// spawn, never an accepted death or a game/dynamic-barrier integration.
internal static class CorpseStaticNativeChecks
{
    private static readonly string[] Rooms=[
        "AD1 TRANSFER LOCK BT","AD1 TRANSFER LOCK DM","AD2 ALINOS PERCH","AD2 MAGMA VENTS",
        "CTF1 FAULT LINE - EXPANDED","CTF1_FAULT LINE","E3 FIRST HUNT","Gorea Prison",
        "MP1 SANCTORUS","MP10 OVERLOAD","MP11 BREAKTHROUGH","MP12 SIC TRANSIT","MP13 ACCELERATOR",
        "MP14 OUTER REACH","MP2 HARVESTER","MP3 PROVING GROUND","MP4 HIGHGROUND","MP4 HIGHGROUND - EXPANDED",
        "MP5 FUEL SLUICE","MP6 HEADSHOT","MP7 PROCESSOR CORE","MP8 FIRE CONTROL","MP9 CRYOCHASM",
        "UNIT 3 VESPER STARPORT","UNIT 4 ARCTERRA BASE","UNIT1 ALINOS LANDFALL","UNIT2 LANDING BAY"];
    private sealed record Face(V[] Points,P Plane,bool IgnorePlayers,bool IgnoreBeams,string Chunk,int Ordinal);
    private sealed record State(CollisionInstance Chunk,CollisionInfo Info,bool Active,Vector3 Translation,object[] Refs);
    private static V N(Vector3 p)=>new(p.X,p.Y,p.Z);
    private static bool Bounded(V p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)
        &&Math.Abs(p.X)<=8192&&Math.Abs(p.Y)<=8192&&Math.Abs(p.Z)<=8192;
    private static object[] Refs(CollisionInfo c)=>c is MphCollisionInfoBase m
        ?[m.Points,m.Planes,m.RuntimePointIndices,m.RuntimeData,m.RuntimeDataIndices,m.RuntimeEntries]:[c.Points,c.Planes];
    private static State[] Save(IReadOnlyList<CollisionInstance> chunks)=>chunks.Select(c=>new State(c,c.Info,c.Active,c.Translation,Refs(c.Info))).ToArray();
    private static bool TranslationBits(Vector3 a,Vector3 b)=>BitConverter.SingleToInt32Bits(a.X)==BitConverter.SingleToInt32Bits(b.X)
        &&BitConverter.SingleToInt32Bits(a.Y)==BitConverter.SingleToInt32Bits(b.Y)&&BitConverter.SingleToInt32Bits(a.Z)==BitConverter.SingleToInt32Bits(b.Z);
    private static bool Same(IReadOnlyList<CollisionInstance> chunks,State[] saved)=>chunks.Count==saved.Length
        &&chunks.Select((c,i)=>ReferenceEquals(c,saved[i].Chunk)&&ReferenceEquals(c.Info,saved[i].Info)
            &&c.Active==saved[i].Active&&TranslationBits(c.Translation,saved[i].Translation)
            &&Refs(c.Info).Zip(saved[i].Refs,(a,b)=>ReferenceEquals(a,b)).All(x=>x)).All(x=>x);
    private static string Digest(IReadOnlyList<CollisionInstance> chunks)
    {
        using var stream=new MemoryStream();using var w=new BinaryWriter(stream);w.Write(chunks.Count);
        foreach(var c in chunks)
        {
            w.Write(c.Name);w.Write(c.ConnectorName??"");w.Write(c.Active);w.Write(c.IsEntity);
            w.Write(c.Translation.X);w.Write(c.Translation.Y);w.Write(c.Translation.Z);
            var info=c.Info;w.Write(info.GetType().FullName!);w.Write(info.FirstHunt);
            w.Write(info.Points.Count);foreach(var p in info.Points){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
            w.Write(info.Planes.Count);foreach(var p in info.Planes){w.Write(p.X);w.Write(p.Y);w.Write(p.Z);w.Write(p.W);}
            if(info is not MphCollisionInfoBase m)continue;
            w.Write(m.RuntimePointIndices.Count);foreach(int i in m.RuntimePointIndices)w.Write(i);
            w.Write(m.RuntimeData.Count);foreach(var f in m.RuntimeData)
            {w.Write(f.Counter);w.Write(f.PlaneIndex);w.Write((uint)f.Flags);w.Write(f.LayerMask);w.Write(f.PaddingA);w.Write(f.PointIndexCount);w.Write(f.PointStartIndex);}
            w.Write(m.RuntimeDataIndices.Count);foreach(int i in m.RuntimeDataIndices)w.Write(i);
            w.Write(m.RuntimeEntries.Count);foreach(var e in m.RuntimeEntries){w.Write(e.DataCount);w.Write(e.DataStartIndex);}
        }
        w.Flush();return Convert.ToHexStringLower(SHA256.HashData(stream.GetBuffer().AsSpan(0,checked((int)stream.Length))));
    }
    private static bool Copy(IReadOnlyList<CollisionInstance> chunks,out Face[] faces,out string reason)
    {
        var result=new List<Face>();faces=[];reason="";
        foreach(var c in chunks)
        {
            if(!c.Active||c.IsEntity)continue;
            if(c.Info is not MphCollisionInfoBase m){reason="unsupported active source format "+c.Info.GetType().Name;return false;}
            if(!Bounded(N(c.Translation))){reason="invalid chunk translation";return false;}
            if((long)result.Count+m.RuntimeData.Count>CorpseStaticMeshBuilder.MaximumFaces)
            {reason="global8192 unique RuntimeData face budget";return false;}
            foreach(var p in m.Points)if(!Bounded(N(p))||!Bounded(N(p+c.Translation)))
            {reason="nonfinite/unbounded source or translated point";return false;}
            foreach(var p in m.Planes)if(!Bounded(N(p.Xyz))||!float.IsFinite(p.W)||Math.Abs(p.W)>8192)
            {reason="nonfinite/unbounded stored plane";return false;}
            foreach(int i in m.RuntimePointIndices)if(i<0||i>=m.Points.Count)
            {reason="wide point index outside source table";return false;}
            for(int ordinal=0;ordinal<m.RuntimeData.Count;ordinal++)
            {
                var f=m.RuntimeData[ordinal];long end=(long)f.PointStartIndex+f.PointIndexCount;
                if(f.PointStartIndex<0||f.PointIndexCount<0||end>m.RuntimePointIndices.Count||f.PlaneIndex<0||f.PlaneIndex>=m.Planes.Count)
                {reason=$"wide face span/plane invalid {c.Name}/{ordinal}";return false;}
                if(!f.IgnorePlayers&&(f.PointIndexCount<3||f.PointIndexCount>CorpseStaticMeshBuilder.MaximumFaceVertices))
                {reason=$"stored face vertex count {f.PointIndexCount} at {c.Name}/{ordinal}";return false;}
                var points=f.IgnorePlayers?Array.Empty<V>():new V[f.PointIndexCount];
                for(int j=0;j<points.Length;j++)points[j]=N(m.Points[m.RuntimePointIndices[f.PointStartIndex+j]]+c.Translation);
                var plane=m.Planes[f.PlaneIndex];float worldW=plane.W+Vector3.Dot(plane.Xyz,c.Translation);
                result.Add(new(points,new(plane.X,plane.Y,plane.Z,worldW),f.IgnorePlayers,f.IgnoreBeams,c.Name,ordinal));
            }
        }
        faces=result.ToArray();return true;
    }
    private static bool Nearby(Face f,V center,float extent)
    {
        if(f.Points.Length==0)return false;V min=f.Points[0],max=min;
        foreach(var p in f.Points){min=V.Min(min,p);max=V.Max(max,p);}
        return max.X>=center.X-extent&&min.X<=center.X+extent&&max.Y>=center.Y-extent&&min.Y<=center.Y+extent&&max.Z>=center.Z-extent&&min.Z<=center.Z+extent;
    }
    private static double? PlaneError(Face f)
    {
        var n=new V(f.Plane.X,f.Plane.Y,f.Plane.Z);float length=n.Length();
        return !float.IsFinite(length)||length<1e-8f||!float.IsFinite(f.Plane.W)?null:
            f.Points.Length==0?0:f.Points.Max(p=>(double)Math.Abs(V.Dot(n,p)-f.Plane.W)/length);
    }
    private static bool Height(StaticTriangle t,float x,float z,out float y)
    {
        y=0;var n=V.Cross(t.B-t.A,t.C-t.A);if(n.LengthSquared()<1e-8f||V.Normalize(n).Y<.99f)return false;
        float E(V a,V b)=>(b.X-a.X)*(z-a.Z)-(b.Z-a.Z)*(x-a.X);
        float a=E(t.A,t.B),b=E(t.B,t.C),c=E(t.C,t.A);
        if(!(a>=-1e-4f&&b>=-1e-4f&&c>=-1e-4f)&&!(a<=1e-4f&&b<=1e-4f&&c<=1e-4f))return false;
        y=t.A.Y-(n.X*(x-t.A.X)+n.Z*(z-t.A.Z))/n.Y;return float.IsFinite(y);
    }
    private static bool FlatSupport(CorpseStaticWorld world,V center,out float floor)
    {
        floor=0;var triangles=world.Triangles.ToArray();var levels=new List<float>();
        foreach(var t in triangles)if(Height(t,center.X,center.Z,out float y)&&y<=center.Y+.1f&&y>=center.Y-2)levels.Add(y);
        foreach(float y in levels.OrderByDescending(x=>x))
        {
            bool okay=true;
            foreach(float x in new[]{-1f,0,1f})foreach(float z in new[]{-.75f,0,.75f})
                if(!triangles.Any(t=>Height(t,center.X+x,center.Z+z,out float at)&&Math.Abs(at-y)<.01f))okay=false;
            if(okay){floor=y;return true;}
        }
        return false;
    }
    internal static int Run(string data,string output)
    {
        output=Path.GetFullPath(output);string cwd=Directory.GetCurrentDirectory();bool force=MapAudit.ForceEveryone,script=NetTestScript.Enabled;
        int checks=0,loaded=0,accepted=0,physical=0;var failures=new List<string>();var rooms=new List<object>();
        void Check(bool good,string label){checks++;if(!good){failures.Add(label);Console.Error.WriteLine("STATIC NATIVE FAIL "+label);}}
        try
        {
            Directory.CreateDirectory(output);Directory.SetCurrentDirectory(Path.GetFullPath(data));Paths.UpdatePaths();Paths.ChooseMphPath();Headless.Enter();
            MapAudit.ForceEveryone=true;NetTestScript.Enabled=false;Check(!NetSession.Active,"fresh offline headless role");
            foreach(string room in Rooms)
            {
                Scene? scene=null;
                try
                {
                    Check(StockRiftlinePickups.TryPosition(room,out var fixedCenter),room+" authored center");
                    V center=new(fixedCenter.X/4096f,fixedCenter.Y/4096f,fixedCenter.Z/4096f);
                    scene=new(new Vector2i(256,192),SyntheticInput.CreateKeyboard(),SyntheticInput.CreateMouse(),_=>{},()=>{});
                    scene.AddPlayer(Hunter.Samus);scene.AddRoom(room,GameMode.Battle,playerCount:1,nodeLayerMask:0);scene.OnLoad();loaded++;
                    var chunks=scene.Room!.RoomCollision;var originalList=chunks;var saved=Save(chunks);string before=Digest(chunks);
                    var rng=Rng.Current;var rngBefore=(rng.Rng1,rng.Rng2);float time=scene.ElapsedTime;
                    bool copied=Copy(chunks,out var faces,out string copyReason);var candidates=new List<object>();
                    foreach(float extent in new[]{1f,2f,4f})
                    {
                        var builder=new CorpseStaticMeshBuilder(center,extent);string reason=copyReason;object? rejected=null,probe=null;
                        int added=0,skipped=0,ignoreBeams=0;double planeError=0;CorpseStaticWorld? world=null;
                        if(copied)foreach(var f in faces)
                        {
                            bool nearby=Nearby(f,center,extent);double? error=PlaneError(f);if(nearby&&error is{} finite)planeError=Math.Max(planeError,finite);
                            int staged=builder.StagedTriangles;var result=builder.TryAddFace(f.Points,f.Plane,f.IgnorePlayers);
                            if(result==CorpseSurfaceResult.Added){added++;if(f.IgnoreBeams)ignoreBeams++;}
                            if(result==CorpseSurfaceResult.Skipped)skipped++;
                            if(result!=CorpseSurfaceResult.Rejected)continue;
                            reason=builder.VisitedFaces>CorpseStaticMeshBuilder.MaximumFaces?"global face budget":error>CorpseStaticMeshBuilder.MaximumPlaneError&&nearby?"stored-plane mismatch":
                                staged+f.Points.Length-2>CorpseStaticWorld.MaximumTriangles?"triangle budget":"stored face failed normal/convexity/edge/degeneracy/bounds policy";
                            rejected=new{f.Chunk,f.Ordinal,vertices=f.Points.Length,nearby,planeError=error,
                                normalLengthSquared=new V(f.Plane.X,f.Plane.Y,f.Plane.Z).LengthSquared(),stagedBefore=staged,f.IgnoreBeams};break;
                        }
                        bool built=copied&&!builder.Failed&&builder.TryBuild(out world);
                        if(built){accepted++;reason="accepted copied static candidate";}else if(reason.Length==0)reason="no eligible overlapping triangles";
                        if(built&&physical==0&&FlatSupport(world!,center,out float floor))
                        {
                            var rig=ReferenceScene.Samus();var poses=new CorpsePose[15];using var backend=new BepuCorpseBackend(world!,CorpseQuality.Low,true);
                            var descriptor=new CorpseSpawnDescriptor(new(new(1,0,0,0,0,1,0),new(1,0,0,0)),rig,
                                new(new(center.X,floor+.1f,center.Z),Q.Identity),V.Zero,V.Zero,null,1,0,180);
                            bool spawned=backend.TrySpawn(descriptor,out var handle);Check(spawned,room+" synthetic copied-world spawn");bool okay=spawned;float minBottom=float.PositiveInfinity;
                            for(uint tick=1;tick<=120&&okay;tick++)
                            {
                                okay=backend.Step(tick)&&backend.Sample(handle,1,poses)&&poses.All(p=>p.IsValid);if(!okay)break;
                                for(int part=0;part<poses.Length;part++)foreach(int x in new[]{-1,1})foreach(int y in new[]{-1,1})foreach(int z in new[]{-1,1})
                                    minBottom=Math.Min(minBottom,(poses[part].Position+V.Transform(rig.Parts[part].Size*new V(x,y,z)*.5f,poses[part].Orientation)).Y);
                            }
                            Check(okay&&backend.Statistics.Faults==0,room+" copied world120 finite steps");
                            Check(float.IsFinite(minBottom)&&minBottom>=floor-.15f,room+" outward winding supports bodies above verified floor");physical++;
                            probe=new{scope="CPU-only BEPU synthetic original primitive spawn; not genuine death/dynamic barrier/game qualification",steps=120,spawned,okay,floor,minBottom,stats=backend.Statistics};
                        }
                        candidates.Add(new{halfExtent=extent,accepted=built,reason,visited=builder.VisitedFaces,sourceFaces=faces.Length,addedFaces=added,skippedFaces=skipped,
                            retainedIgnoreBeamFaces=ignoreBeams,triangles=world==null?0:world.Triangles.Length,nearbyMaximumPlaneError=planeError,rejected,physicalProbe=probe,
                            physicalSkipped=built&&probe==null?"no broad flat floor verified, or one physical probe already completed":null});
                    }
                    string after=Digest(chunks);bool unchanged=before==after&&ReferenceEquals(originalList,scene.Room.RoomCollision)&&Same(chunks,saved);
                    Check(unchanged,room+" full geometry digest/chunk Active/Translation/source refs unchanged");
                    Check(ReferenceEquals(rng,Rng.Current)&&(rng.Rng1,rng.Rng2)==rngBefore&&scene.ElapsedTime==time,room+" gameplay RNG/time unchanged");
                    rooms.Add(new{room,center=new[]{center.X,center.Y,center.Z},copied,copyReason,sourceDigestBefore=before,sourceDigestAfter=after,unchanged,
                        chunks=chunks.Select(c=>new{c.Name,c.Active,c.IsEntity,sourceFormat=c.Info.GetType().Name,c.Info.FirstHunt,
                            translation=new[]{c.Translation.X,c.Translation.Y,c.Translation.Z},points=c.Info.Points.Count,planes=c.Info.Planes.Count,
                            faceCount=(c.Info as MphCollisionInfoBase)?.RuntimeData.Count}).ToArray(),candidates});
                    Console.WriteLine($"STATIC NATIVE {room}: copied={copied} acceptedTotal={accepted} physical={physical}");
                }
                catch(Exception error){failures.Add(room+": "+error);rooms.Add(new{room,error=error.ToString()});}
                finally{scene?.DoCleanup();}
            }
            Check(loaded==27,"all27 actual stock rooms loaded without simulation/query calls");Check(accepted>0,"at least one native copied candidate accepted (none is RED)");
            Check(physical>0,"at least one actual copied-world120tick support probe (none is RED)");
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{
                scope="Actual27 filtered headless native rooms; stock M7 centers; extents1/2/4; no simulation/native collision query/game/dynamic barrier/render/device qualification",
                checks,loaded,acceptedCandidates=accepted,physicalProbes=physical,failures,
                gameAssemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(Scene).Assembly.Location))),
                coreAssemblySha256=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CorpseStaticMeshBuilder).Assembly.Location))),rooms
            },new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"STATIC NATIVE checks={checks} loaded={loaded} accepted={accepted} physical={physical} failures={failures.Count}");return failures.Count==0?0:1;
        }
        finally{MapAudit.ForceEveryone=force;NetTestScript.Enabled=script;Directory.SetCurrentDirectory(cwd);}
    }
}
