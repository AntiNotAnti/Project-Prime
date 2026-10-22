using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using MphRead.Mods.Import;
using MphRead.Mods.MapGen;
using MphRead.Mods.Multiplayer;
using OpenTK.Mathematics;


/// <summary>Synthetic regressions for the real bounded parser, native node compiler
/// and local importer. No private source copies, frozen runtime, media or GL.</summary>
internal static class EchoesTopologyNavigationFixtureTests
{
    internal static void Run()
    {
        var checks = 0;
        void Check(bool pass,string label) { if(!pass)throw new Exception(label); checks++;Console.WriteLine("CPP09 PASS "+label); }
        void Reject<T>(Action action,string label) where T:Exception {try{action();}catch(T){Check(true,label);return;}throw new Exception("Expected "+typeof(T).Name+": "+label);}
        float[] Pos(Vector3 p)=>new[]{p.X,p.Y,p.Z};
        string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
        // Captured from the pre-change production compiler on macOS arm64. These
        // binary goldens protect native node bytes without retaining old code/DLLs.
        // Other platforms still execute every functional contract below.
        bool byteGoldens = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        if (!byteGoldens) Console.WriteLine("CPP09 SKIP historical Mac-arm64 byte goldens on this platform");
        var goldens = new Dictionary<string,string>(StringComparer.Ordinal)
        {
         ["plain"]="61B8ED5E3E50A8DFB1125547DB07AB41D4AA9FD897A0689E2A30A1FCA3900ECC",
         ["authored-False"]="1218E160B297F02832C12F898048F37822AE7CF82A1208B6CE514D46227C70F9",
         ["authored-True"]="6F3DB2C2DD01535C2F9B90517C9CA2CA099F4962395D4CD8B9B3FEC2C8A187F0",
         ["derived-target-pad"]="3AB686B58989B35E8B5F7D6FC47F526BD8EAC75FB6D2AD624EC66BE8C169F77B",
         ["directed-disconnected"]="396B76180AC97DF95CEABB29A97593720AD355791E5129B308FA7163E6B40F49",
         ["large-ordinary"]="CE6F2AA4DA37E90E52BF084C80952D135320C8D3D09B5CB4736EEB5BA0721943",
         ["strict-project"]="4A1B2BE378393E132A009B643BAB5D1EF2A235044A018D6C614358BF05C0EF08"
        };
        bool Historical(byte[] bytes,string key)=>!byteGoldens||Hash(bytes)==goldens[key];
        byte[] Collision((ushort A,ushort B)[] edges,ushort[] triangle,int vertices=3,ulong flags=0)
        {
         int n=edges.Length;var b=new byte[95+4*n+vertices*12];
         void U32(int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at),value);
         void U16(int at,ushort value)=>BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at),value);
         U32(8,EchoesCollisionSource.Magic);U32(44,0);U32(48,1);BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(52),flags);
         U32(60,0);U32(64,0);U32(68,1);U32(73,(uint)n);
         for(int i=0;i<n;i++){U16(77+i*4,edges[i].A);U16(79+i*4,edges[i].B);}
         int after=77+4*n;U32(after,3);for(int i=0;i<3;i++)U16(after+4+i*2,triangle[i]);
         U32(after+10,0);U32(after+14,(uint)vertices);
         for(int i=0;i<vertices;i++)for(int axis=0;axis<3;axis++)BinaryPrimitives.WriteSingleBigEndian(b.AsSpan(after+18+i*12+axis*4),axis==0?i*2:axis==2?i%2:0);
         return b;
        }
        var baseEdges=new(ushort A,ushort B)[]{(0,1),(1,2),(2,0)};
        int[][] permutations={new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}};
        foreach(var order in permutations)for(int orientation=0;orientation<8;orientation++)foreach(ulong flags in new[]{0UL,1UL<<4,(1UL<<4)|EchoesCollisionSource.FlippedTri|EchoesCollisionSource.ShootThrough})
        {
         var edges=baseEdges.Select((e,i)=>(orientation&(1<<i))==0?e:(e.B,e.A)).ToArray();
         var bytes=Collision(edges,order.Select(x=>(ushort)x).ToArray(),3,flags);
         var actual=EchoesCollisionSource.Parse(bytes);
         int a=edges[order[0]].Item1,b=edges[order[0]].Item2;
         int c=Enumerable.Range(0,3).Single(v=>v!=a&&v!=b);
         if((flags&EchoesCollisionSource.FlippedTri)!=0)(a,b)=(b,a);
         Check(actual.TriangleVertices.SequenceEqual(new[]{a,b,c})&&actual.SurfaceFlags.SequenceEqual(new[]{flags})
             &&actual.Vertices.SequenceEqual(new[]{0f,0f,0f,2f,0f,1f,4f,0f,0f}),"valid edge order/orientation preserves original vertices/winding/flags "+string.Join(',',order)+"/"+orientation+"/"+flags);
        }
        foreach(var input in new[]{
         (Collision(new(ushort,ushort)[]{(0,1),(2,3),(2,0)},new ushort[]{0,1,2},4),"disconnected first and second edges"),
         (Collision(new(ushort,ushort)[]{(0,1),(1,2),(0,1)},new ushort[]{0,1,2}),"unrelated closing edge"),
         (Collision(baseEdges,new ushort[]{0,1,1}),"same edge reference repeated"),
         (Collision(new(ushort,ushort)[]{(0,1),(1,1),(2,0)},new ushort[]{0,1,2}),"self edge"),
         (Collision(new(ushort,ushort)[]{(0,1),(1,2),(2,9)},new ushort[]{0,1,2}),"closing endpoint out of range"),
         (Collision(new(ushort,ushort)[]{(0,1),(1,0),(2,0)},new ushort[]{0,1,2}),"duplicate undirected edge")})
         Reject<InvalidDataException>(()=>EchoesCollisionSource.Parse(input.Item1),"reject malformed topology: "+input.Item2);
        var formerlyAccepted=Collision(new(ushort,ushort)[]{(0,1),(2,3),(2,0)},new ushort[]{0,1,2},4);
        Reject<InvalidDataException>(()=>EchoesCollisionSource.Parse(formerlyAccepted),"regression: disjoint edges must not invent triangle012");

        MapDefinition Floor(float width=36,float depth=12)=>new(){MapId=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000001"),Name="CPP09 SYNTHETIC FLOOR",FormatVersion=3,
         Materials=new(){new(){Id=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000002"),Name="synthetic",SourceMaterial=0}},
         Brushes=new(){new(){Id=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000003"),Min=new[]{0f,-1f,0f},Max=new[]{width,0f,depth},Material=0}},
         Spawns=new(){new(){Id=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000004"),Position=new[]{2f,0f,2f}},new(){Id=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000005"),Position=new[]{4f,0f,2f}}}};
        MapNavigationLink Link(Vector3 from,Vector3 to,int id=1,bool reverse=false)=>new(){Id=new Guid(id,0,0,new byte[8]),Kind=MapNavigationLinkKind.JumpPad,From=Pos(from),To=Pos(to),Bidirectional=reverse,ToNodeKind=MapNavigationAnchorKind.Aerial};
        var map=Floor();var solid=MapBuilder.Build(map).Solid;var plain=MapNodePacker.Analyze(solid);
        Check(Historical(plain.Bytes,"plain")&&plain.AuthoredLinks.Count==0,"ordinary graph matches historical Mac-arm64 bytes with empty additive report");
        var start=plain.Positions[0];var finish=plain.Positions.Take(plain.Positions.Length-1).OrderByDescending(p=>(p-start).LengthSquared).First();var authored=Link(start,finish);
        foreach(bool reverse in new[]{false,true})
        {
         authored.Bidirectional=reverse;var graph=MapNodePacker.Analyze(solid,new[]{authored});
         Check(Historical(graph.Bytes,"authored-"+reverse)&&graph.Positions.Length==plain.Positions.Length&&graph.Types.Length==plain.Types.Length,"authored "+reverse+" historical Mac-arm64 graph bytes/types/positions unchanged");
         var report=graph.AuthoredLinks.Single();Check(report.Ordinal==0&&report.LinkId==authored.Id&&report.Encoded&&report.Refusal==MapNodePacker.NavigationLinkRefusal.None&&report.Bidirectional==reverse,"exact source link report identity and accepted disposition "+reverse);
         Check(graph.Positions[report.FromNode]==start&&graph.Positions[report.ToNode]==finish&&graph.Neighbours[report.FromNode].Contains(report.ToNode)&&(!reverse||graph.Neighbours[report.ToNode].Contains(report.FromNode)),"report uses final type-remapped chosen nodes and actual requested adjacency "+reverse);
         Check(report.ToNode!=Array.IndexOf(plain.Positions,finish),"semantic aerial classification actually changed final node index "+reverse);
         Check(((ICollection<MapNodePacker.NavigationLinkReport>)graph.AuthoredLinks).IsReadOnly,"published reports immutable "+reverse);

        }
        authored.Bidirectional=false;
        var collapsed=Link(start,start+new Vector3(.1f,0,.1f),2);var collapsedGraph=MapNodePacker.Analyze(solid,new[]{collapsed});var collapsedReport=collapsedGraph.AuthoredLinks.Single();
        Check(collapsedGraph.Bytes.SequenceEqual(plain.Bytes)&&!collapsedReport.Encoded&&collapsedReport.FromNode==collapsedReport.ToNode&&collapsedReport.Refusal==MapNodePacker.NavigationLinkRefusal.CollapsedEndpoints,"collapsed authored route reported explicitly while ordinary historical bytes retained");
        var duplicates=MapNodePacker.Analyze(solid,new[]{authored,authored});Check(duplicates.AuthoredLinks.Count==2&&duplicates.AuthoredLinks.Select(x=>x.Ordinal).SequenceEqual(new[]{0,1}),"report accounts for every authored occurrence even when edge already exists");
        var capturedReport=duplicates.AuthoredLinks[0];authored.Id=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000123");
        Check(capturedReport.LinkId!=authored.Id&&duplicates.AuthoredLinks[0]==capturedReport,"authored input mutation cannot rewrite a published report");
        var impliedMap=Floor();impliedMap.NavigationLinks.Add(Link(start,finish,601));impliedMap.JumpPads.Add(new(){Id=new Guid(602,0,0,new byte[8]),Position=Pos(finish),Target=Pos(start)});
        var effective=MapNodePacker.EffectiveLinks(impliedMap);var implied=MapNodePacker.Analyze(solid,effective);
        Check(implied.AuthoredLinks.Count==2&&implied.AuthoredLinks.Select(x=>x.LinkId).SequenceEqual(effective.Select(x=>x.Id)),"additive report includes explicit and derived target-pad link inputs in exact order");
        Check(Historical(implied.Bytes,"derived-target-pad"),"derived target-pad links retain historical Mac-arm64 compiler bytes");
        var renderOnly=Floor();renderOnly.Brushes.Clear();renderOnly.Geometry.Add(new MapMesh{Id=new Guid(603,0,0,new byte[8]),Solid=false,CollisionOnly=false,Material=0,Vertices=new(){new[]{0f,0f,0f},new[]{0f,0f,12f},new[]{36f,0f,0f}},Faces=new(){new[]{0,1,2}}});
        var rendered=MapBuilder.Build(renderOnly);Check(rendered.Faces.Count>0&&rendered.Solid.Count==0,"actual compiler keeps render-only floor out of source collision");
        Reject<MapAuthoringException>(()=>MapNodePacker.Analyze(rendered.Solid,new[]{Link(start,finish)}),"visual-only source cannot fabricate an encoded navigation route");
        var separate=Floor();separate.Brushes.Clear();separate.Brushes.Add(new(){Min=new[]{0f,-1f,0f},Max=new[]{6f,0f,6f},Material=0});separate.Brushes.Add(new(){Min=new[]{30f,-1f,0f},Max=new[]{36f,0f,6f},Material=0});
        var separateSolid=MapBuilder.Build(separate).Solid;var separatePlain=MapNodePacker.Analyze(separateSolid);var left=separatePlain.Positions.MinBy(v=>v.X);var right=separatePlain.Positions.MaxBy(v=>v.X);var directed=MapNodePacker.Analyze(separateSolid,new[]{Link(left,right)});var d=directed.AuthoredLinks.Single();
        Check(d.Encoded&&directed.Neighbours[d.FromNode].Contains(d.ToNode)&&!directed.Neighbours[d.ToNode].Contains(d.FromNode),"isolated source floors encode actual forward edge without invented reverse");
        Check(Historical(directed.Bytes,"directed-disconnected"),"directed disconnected historical Mac-arm64 graph binary unchanged");
        var tiny=Floor(2,2);var tinySolid=MapBuilder.Build(tiny).Solid;var point=MapNodePacker.Analyze(tinySolid).Positions.Single();
        Reject<MapAuthoringException>(()=>MapNodePacker.Analyze(tinySolid,new[]{Link(point+new Vector3(12,0,0),point)}),"endpoint exactly12 units retains existing strict radius refusal");
        Reject<MapAuthoringException>(()=>MapNodePacker.Analyze(Array.Empty<BuiltFace>()),"no source floor cannot produce encoded route");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();Reject<OperationCanceledException>(()=>MapNodePacker.Analyze(solid,new[]{authored},cancel.Token),"pre-cancel produces no graph/report");}
        var largeSolid=MapBuilder.Build(Floor(80,80)).Solid;var largePlain=MapNodePacker.Analyze(largeSolid);var many=largePlain.Positions.Skip(1).Take(20).Select((p,i)=>Link(largePlain.Positions[0],p,100+i)).ToArray();
        Reject<MapAuthoringException>(()=>MapNodePacker.Analyze(largeSolid,many),"authored route degree budget failure remains explicit");
        Check(Historical(largePlain.Bytes,"large-ordinary"),"larger historical Mac-arm64 graph binary unchanged");
        for(int i=0;i<5;i++)Check(MapNodePacker.Analyze(solid,new[]{authored}).Bytes.SequenceEqual(MapNodePacker.Analyze(solid,new[]{authored}).Bytes),"repeated actual compiler bytes deterministic on this Mac process "+i);

        var temp=Path.Combine(Path.GetTempPath(),"cpp09-private-installer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
         var routed=Floor();routed.Name="ROUTED ECHOES SPIDER COMPLEX";routed.LocalSourceOnly=true;routed.LocalSourceFingerprint="b8b92af37dc772ecd8234fb1ce664be0493188f6";routed.Capabilities=new(){SupportedModes=new(){"Battle"},MinPlayers=1,MaxPlayers=1};routed.NavigationLinks=new(){Link(start,finish,201),Link(finish,start,202)};
         Check(MapValidator.Validate(routed,checkSources:false).IsValid,"actual strict routed fixture satisfies existing authoring validator");
         var bytesBefore=routed.Serialize();var file=EchoesPrivateMapImport.InstallLocalDefinition(routed,Path.Combine(temp,"candidate"));
         var historicalProject=MapProjectSerializer.Clone(routed);historicalProject.LocalSourceFingerprint=null;
         Check(Historical(Encoding.UTF8.GetBytes(historicalProject.Serialize()),"strict-project"),"historical project bytes remain unchanged without optional local provenance");
         Check(File.ReadAllBytes(file).SequenceEqual(Encoding.UTF8.GetBytes(bytesBefore))&&routed.Serialize()==bytesBefore,"successful strict install persists exact project/mapID plus explicit source provenance without mutating input");
         Check(MapDefinition.Load(file).MapId==routed.MapId&&!Directory.EnumerateFiles(temp,"*.ppmap",SearchOption.AllDirectories).Any(),"private routed install preserves mapID and cannot create shareable package");
         var bad=MapProjectSerializer.Clone(routed);bad.NavigationLinks[0]=collapsed;var sentinel=File.ReadAllBytes(file);
         Reject<InvalidDataException>(()=>EchoesPrivateMapImport.InstallLocalDefinition(bad,Path.Combine(temp,"candidate")),"strict actual installer refuses every collapsed route");
         Check(File.ReadAllBytes(file).SequenceEqual(sentinel),"strict refusal preserves previously installed exact recipe");
         bad=MapProjectSerializer.Clone(routed);bad.NavigationLinks[1]=collapsed;
         Reject<InvalidDataException>(()=>EchoesPrivateMapImport.InstallLocalDefinition(bad,Path.Combine(temp,"last-collapsed")),"strict importer checks the last authored route, not merely the first");
         var badSpires=MapProjectSerializer.Clone(routed);badSpires.MapId=Guid.Parse("a3c09d80-1540-4cc9-8469-000000000099");badSpires.Name="ROUTED ECHOES SPIRES";
         badSpires.NavigationLinks=new(){Link(start,finish,301),Link(finish,start,302),Link(start,start+new Vector3(.1f,0,.1f),303)};
         Check(MapValidator.Validate(badSpires,checkSources:false).IsValid,"second batch fixture has unique arena/mapID/filename and valid authoring before compiler refusal");
         var noWriteRoot=Path.Combine(temp,"no-partial");Reject<InvalidDataException>(()=>EchoesPrivateMapImport.InstallLocalDefinitions(new[]{routed,badSpires},noWriteRoot),"validate-all final route refusal precedes publication of distinct otherwise valid recipe");
         Check(!Directory.Exists(noWriteRoot),"refused batch creates no local recipe directory");
         var budget=Floor(80,80);budget.Name=routed.Name;budget.LocalSourceOnly=true;budget.Capabilities=routed.Capabilities;
         budget.NavigationLinks=new(){Link(largePlain.Positions[0],largePlain.Positions[^1],401),Link(largePlain.Positions[^1],largePlain.Positions[0],402)};
         budget.JumpPads=many.Select((x,i)=>new MapJumpPad{Id=new Guid(700+i,0,0,new byte[8]),Position=x.From,Target=x.To}).ToList();
         Check(MapValidator.Validate(budget,checkSources:false).IsValid,"budget fixture reaches actual compiler after full authoring validation");
         Reject<MapAuthoringException>(()=>EchoesPrivateMapImport.InstallLocalDefinition(budget,Path.Combine(temp,"budget")),"strict importer propagates actual authored/effective route budget failure");
         Check(!Directory.Exists(Path.Combine(temp,"budget")),"route budget failure publishes no recipe");
         bad=MapProjectSerializer.Clone(routed);bad.NavigationLinks[0].From=new[]{1000f,0f,1000f};Reject<MapAuthoringException>(()=>EchoesPrivateMapImport.InstallLocalDefinition(bad,Path.Combine(temp,"far")),"strict installer refuses unencoded out-of-range route via real compiler");
         Check(!MapModeCapabilities.AllowsLocalSource(routed,false,out _)&&MapModeCapabilities.AllowsLocalSource(routed,true,out _),"actual owner retains default network refusal and explicit offline allowance");
         Reject<InvalidDataException>(()=>MapPackageBuilder.Build(routed,Path.Combine(temp,"forbidden.ppmap")),"actual package builder still refuses local commercial source");

        }
        finally{Directory.Delete(temp,true);}
        Console.WriteLine("CPP09 "+checks+" checks passed; synthetic production parser/compiler/importer; historical byte checks are Mac-arm64 only");
    }
}
