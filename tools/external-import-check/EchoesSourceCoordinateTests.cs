using System.Buffers.Binary;
using MphRead.Mods.Import;

// Own-generated MREA sections and GX corner indices only. No source assets,
// renderer, files, media probes or material readiness assertions.
internal static class EchoesSourceCoordinateTests
{
    private readonly record struct Corner(ushort P, ushort T0, ushort T1);
    private sealed record Model(float[][] Positions, float[][] FloatUvs, short[][] ShortUvs, byte[][] Surfaces);
    private sealed record MaterialFixture(byte[] Bytes, Dictionary<string, int> Fields);
    private static readonly float[][] P3 = { new[] { 0f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 0f, 1f, 0f } };
    private static readonly float[][] F0 = { new[] { 11f, 12f }, new[] { 21f, 22f }, new[] { -184.96622f, 207.88986f }, new[] { float.NaN, float.PositiveInfinity } };
    private static readonly short[][] S0 = { new short[] { 0, 32767 }, new short[] { -32768, -1 }, new short[] { 512, 1024 }, new short[] { 1536, 2048 } };
    private static int checks;

    public static void Run()
    {
        WholeTraversal(); SignatureQualification(); ReferenceRefusal(); PrimitiveBounds(); CornerVisitBudget();
        Console.WriteLine($"PASS: {checks} synthetic source-coordinate assertions (no renderer qualification)");
    }

    private static void WholeTraversal()
    {
        var common = Material();
        byte[] foreign = (byte[])common.Bytes.Clone(); Put(foreign, common.Fields["flags"], 0x36880);
        var a = new Model(P3, F0, S0, new[] { Surface(0, 0x90,
            new(0,0,0), new(1,1,1), new(2,2,2), new(0,3,2), new(2,2,1), new(1,1,0)) });
        float[][] p4 = { new[] { 10f, 0f, 0f }, new[] { 11f, 0f, 0f }, new[] { 10f, 1f, 0f }, new[] { 11f, 1f, 0f } };
        float[][] f1 = { new[] { 101f,102f }, new[] { 201f,202f }, new[] { 301f,302f }, new[] { 401f,402f }, new[] { 501f,502f } };
        short[][] s1 = { new short[] { 32767,0 }, new short[] { 16384,-16384 }, new short[] { -1,1 } };
        Corner[] ring = { new(0,0,1), new(1,1,2), new(2,2,3), new(3,0,4) };
        var b = new Model(p4, f1, s1, new[] { Surface(0,0x98,ring), Surface(0,0xa0,ring),
            Surface(0,0x90,new(0,0,1),new(0,1,2),new(1,2,3)),
            Surface(1,0x90,new(0,ushort.MaxValue,ushort.MaxValue),new(1,ushort.MaxValue,ushort.MaxValue),new(2,ushort.MaxValue,ushort.MaxValue)) });
        byte[] raw = Area(new[] { common.Bytes, foreign }, a,b);
        var sections = EchoesMreaSections.Parse(raw);
        var old = EchoesWorldGeometrySource.Read(sections);
        var result = EchoesWorldGeometrySource.ReadWithSourceCoordinates(sections);
        Check(result.Geometry.Positions.Select(BitConverter.SingleToInt32Bits).SequenceEqual(old.Positions.Select(BitConverter.SingleToInt32Bits))
            && result.Geometry.TriangleVertices.SequenceEqual(old.TriangleVertices), "opt-in output preserves all legacy positions/triangles exactly");
        Check(result.Geometry.PositionCount == 7 && result.Geometry.TriangleVertices.SequenceEqual(new[] {
            0,1,2,0,2,1,3,4,5,5,4,6,3,4,5,3,5,6,3,4,5 }), "two-model position offsets, strip parity, fan and duplicate-P skip");
        Check(result.SourceCornerVisits == 20, "source corner visits count input list/strip/fan/degenerate/unqualified corners rather than expanded triangles");
        Check(result.CandidateSourceCornerVisits == 17, "candidate GX occurrence count includes degenerate corners and excludes foreign-program visits");
        Check(result.Triangles.Count == 7 && result.Triangles.Take(6).All(t => t.MaterialOrdinal == 0 && t.Coordinates != null)
            && result.Triangles[6].MaterialOrdinal == 1 && result.Triangles[6].Coordinates == null,
            "per-triangle material and optional coordinates stay aligned; foreign program never guesses UVs");
        var first = result.Triangles[0].Coordinates!.Value;
        var seam = result.Triangles[1].Coordinates!.Value;
        Coordinates(first.A,11,12,0,32767/32768f,"first corner");
        Coordinates(first.B,21,22,-1,-1/32768f,"signed-short extrema");
        Coordinates(seam.A,F0[2][0],F0[2][1],1536/32768f,2048/32768f,"shared position keeps independent UV seam");
        var strip = result.Triangles[3].Coordinates!.Value;
        Coordinates(strip.A,401,402,-1/32768f,1/32768f,"second model odd strip corner A");
        Coordinates(strip.B,301,302,.5f,-.5f,"second model odd strip corner B");
        Coordinates(strip.C,501,502,32767/32768f,0,"second model odd strip corner C");
        var fan = result.Triangles[5].Coordinates!.Value;
        Coordinates(fan.A,201,202,32767/32768f,0,"fan anchor");
        Coordinates(fan.B,401,402,-1/32768f,1/32768f,"fan second corner");
        Coordinates(fan.C,501,502,32767/32768f,0,"fan third corner");
        Check(result.Materials.Count == 2 && result.Materials[0].Ordinal == 0 && result.Materials[0].CommonOpaqueCandidate
            && result.Materials[0].BaseResourceId == 0x12345678 && result.Materials[0].LightmapResourceId == 0xabcdef01
            && !result.Materials[1].CommonOpaqueCandidate && result.Materials[1].BaseResourceId == null
            && result.Materials[1].LightmapResourceId == null, "base is texture slot1, lightmap slot0, unknown program has no binding claim");
        Check(result.Materials.All(m => m.ProgramSha256.Length == 64 && m.ProgramSha256.All(Uri.IsHexDigit))
            && result.Materials[0].ProgramSha256 != result.Materials[1].ProgramSha256, "program digests distinguish changed signature");
        Check(result.Materials is not EchoesSourceMaterial[] && result.Triangles is not EchoesSourceTriangle[], "result collections do not expose mutable arrays");
        if (result.Materials is IList<EchoesSourceMaterial> materials)
            Expect<NotSupportedException>(() => materials[0] = materials[0], "material list is read-only");
        if (result.Triangles is IList<EchoesSourceTriangle> triangles)
            Expect<NotSupportedException>(() => triangles[0] = triangles[0], "triangle list is read-only");
        Array.Fill(raw,(byte)0);
        Coordinates(result.Triangles[0].Coordinates!.Value.A,11,12,0,32767/32768f,"caller source bytes cannot mutate detached coordinates");
    }

    private static void SignatureQualification()
    {
        MaterialFixture baseline = Material();
        foreach (var field in new[] { "flags", "channel", "blend", "stage0.color", "stage0.alpha", "stage0.colorOp", "stage0.alphaOp",
            "stage0.kalpha", "stage0.kcolor", "stage0.ras", "stage0.tex", "stage0.coord", "stage1.color", "stage1.alpha",
            "stage1.colorOp", "stage1.alphaOp", "stage1.kalpha", "stage1.kcolor", "stage1.ras", "stage1.tex", "stage1.coord",
            "texgen0", "texgen1" })
        {
            byte[] changed = (byte[])baseline.Bytes.Clone(); bool byteField = field.EndsWith(".kalpha") || field.EndsWith(".kcolor") || field.EndsWith(".ras") || field.EndsWith(".tex") || field.EndsWith(".coord");
            changed[baseline.Fields[field] + (byteField ? 0 : 3)] ^= 1;
            var result = Read(changed);
            Check(!result.Materials[0].CommonOpaqueCandidate && result.Triangles[0].Coordinates == null,
                "complete signature qualification rejects changed " + field);
        }
        for(int stage=0;stage<2;stage++)
            foreach(string selector in new[]{"color","alpha"})
                for(int input=0;input<4;input++)
                {
                    byte[] changed=(byte[])baseline.Bytes.Clone();int at=baseline.Fields[$"stage{stage}.{selector}"];
                    Put(changed,at,BinaryPrimitives.ReadUInt32BigEndian(changed.AsSpan(at,4))^(1u<<(5*input)));
                    var result=Read(changed);
                    Check(!result.Materials[0].CommonOpaqueCandidate && result.Triangles[0].Coordinates==null,
                        $"stage{stage} {selector} input{input} individually participates in qualification");
                }
        for(int generator=0;generator<2;generator++)
            foreach(var field in new[]{("type",1u),("source",1u<<4),("matrix",1u<<9),("normalize",1u<<14),("post",1u<<15)})
            {
                byte[] changed=(byte[])baseline.Bytes.Clone();int at=baseline.Fields["texgen"+generator];
                Put(changed,at,BinaryPrimitives.ReadUInt32BigEndian(changed.AsSpan(at,4))^field.Item2);
                var result=Read(changed);
                Check(!result.Materials[0].CommonOpaqueCandidate && result.Triangles[0].Coordinates==null,
                    $"texgen{generator} {field.Item1} individually participates in qualification");
            }
        foreach (string variant in new[] { "one-texture", "three-stages", "one-texgen", "two-channels", "konst", "indirect", "animation" })
        {
            var result = Read(Material(variant).Bytes);
            Check(!result.Materials[0].CommonOpaqueCandidate && result.Triangles[0].Coordinates == null,
                "complete signature qualification rejects " + variant);
        }
        byte[] remapped = (byte[])baseline.Bytes.Clone();
        Put(remapped, baseline.Fields["tex0"],1); Put(remapped, baseline.Fields["tex1"],0);
        var mapped = Read(remapped);
        Check(mapped.Materials[0].CommonOpaqueCandidate && mapped.Materials[0].BaseResourceId == 0xabcdef01
            && mapped.Materials[0].LightmapResourceId == 0x12345678, "resource indices resolve through the bounded material-set ID table");
        Check(Read(baseline.Bytes).Materials[0].ProgramSha256 == Read((byte[])baseline.Bytes.Clone()).Materials[0].ProgramSha256,
            "program digest is deterministic across detached input copies");
        byte[] opaque = (byte[])baseline.Bytes.Clone(); opaque[baseline.Fields["unknown0"]] ^= 1;
        var opaqueResult = Read(opaque);
        Check(opaqueResult.Materials[0].CommonOpaqueCandidate
            && opaqueResult.Materials[0].ProgramSha256 != Read(baseline.Bytes).Materials[0].ProgramSha256,
            "opaque material bytes retain digest identity without inventing semantic qualification fields");
    }

    private static void ReferenceRefusal()
    {
        MaterialFixture material = Material();
        foreach ((ushort t0,ushort t1) in new[] { ((ushort)S0.Length,(ushort)0), ((ushort)0,(ushort)F0.Length) })
            Expect<InvalidDataException>(() => Read(material.Bytes, Surface(0,0x90,new(0,t0,t1),new(1,1,1),new(2,2,2))), "model-local UV index at exclusive bound");
        foreach (float value in new[] { float.NaN,float.PositiveInfinity,float.NegativeInfinity })
        {
            float[][] uv = F0.Select(x=>(float[])x.Clone()).ToArray(); uv[0][0]=value;
            var model = new Model(P3,uv,S0,new[] { BasicSurface() });
            Expect<InvalidDataException>(() => EchoesWorldGeometrySource.ReadWithSourceCoordinates(EchoesMreaSections.Parse(Area(new[] { material.Bytes },model))), "referenced nonfinite float UV");
            Check(EchoesWorldGeometrySource.Read(EchoesMreaSections.Parse(Area(new[] { material.Bytes },model))).TriangleCount==1,
                "legacy positions-only read does not acquire UV restrictions");
        }
        byte[] outside = (byte[])material.Bytes.Clone(); Put(outside,material.Fields["tex1"],2);
        Expect<InvalidDataException>(()=>Read(outside),"material texture index at table bound");
        byte[] missing0=(byte[])material.Bytes.Clone(); Put(missing0,material.Fields["descriptor"],3u|(3u<<10));
        byte[] missing1=(byte[])material.Bytes.Clone(); Put(missing1,material.Fields["descriptor"],3u|(3u<<8));
        foreach (byte[] missing in new[] { missing0,missing1 })
            Expect<InvalidDataException>(()=>Read(missing),"qualified common program requires both indexed coordinate streams");
        byte[] eighth=(byte[])material.Bytes.Clone();
        Put(eighth,material.Fields["descriptor"],3u|(3u<<8)|(3u<<10)|(3u<<22));
        Expect<InvalidDataException>(()=>Read(eighth),"unhandled T7 attribute is refused instead of shifting subsequent primitives");
        byte[] hugeTextures=(byte[])material.Bytes.Clone(); Put(hugeTextures,material.Fields["textureCount"],uint.MaxValue);
        byte[] hugeStages=(byte[])material.Bytes.Clone(); Put(hugeStages,material.Fields["stageCount"],uint.MaxValue);
        foreach(byte[] bad in new[] { hugeTextures,hugeStages })
            Expect<InvalidDataException>(()=>Read(bad),"material count admission rejects forged allocation extent");
        byte[] tiny = Material().Bytes[..^4];
        Expect<InvalidDataException>(()=>Read(tiny),"material body truncated within declared boundary");
    }

    private static void PrimitiveBounds()
    {
        Expect<InvalidDataException>(()=>EchoesWorldGeometrySource.DecodeSurface(BasicSurface(),3,-1,3,new List<int>()),
            "public primitive reader rejects negative model-local position start");
        Expect<InvalidDataException>(()=>EchoesWorldGeometrySource.DecodeSurface(BasicSurface(),3,4,3,new List<int>()),
            "public primitive reader rejects reversed model-local position range");
        byte[] surface=BasicSurface(); Put16(surface,65,ushort.MaxValue);
        Expect<InvalidDataException>(()=>Read(Material().Bytes,surface),"primitive byte extent checked before corner allocation/read");
        surface=BasicSurface(); surface[64]=0x90; Put16(surface,65,4);
        Expect<InvalidDataException>(()=>Read(Material().Bytes,surface),"triangle list count must divide by three");
        surface=BasicSurface(); surface[64]=0xff;
        Expect<InvalidDataException>(()=>Read(Material().Bytes,surface),"unknown primitive is never relabeled as source geometry");
        surface=BasicSurface(); Put(surface,0x1c,uint.MaxValue);
        Expect<InvalidDataException>(()=>Read(Material().Bytes,surface),"surface byte offset overflow");
    }

    private static void CornerVisitBudget()
    {
        var material=Material(); byte[] foreign=(byte[])material.Bytes.Clone(); Put(foreign,material.Fields["flags"],0x36880);
        // The primitive lengths are valid and every later triangle is
        // duplicate-P degenerate: retained-triangle limits cannot substitute
        // for the independent input-corner work budget.
        Corner[] repeated=new Corner[ushort.MaxValue]; byte[] degenerate=Surface(0,0x90,repeated);
        byte[][] surfaces=new byte[32][]; surfaces[0]=BasicSurface();
        for(int i=1;i<surfaces.Length;i++)surfaces[i]=degenerate;
        var sections=EchoesMreaSections.Parse(Area(new[]{foreign},new Model(P3,F0,S0,surfaces)));
        try { EchoesWorldGeometrySource.ReadWithSourceCoordinates(sections); }
        catch(InvalidDataException error)
        {
            Check(error.Message.Contains("corner visits",StringComparison.Ordinal),
                "valid degenerate input reaches source-corner work budget rather than retained-triangle limit"); return;
        }
        throw new InvalidOperationException("Expected source-corner work budget refusal.");
    }

    private static EchoesWorldSourceGeometry Read(byte[] material,byte[]? surface=null) => EchoesWorldGeometrySource.ReadWithSourceCoordinates(
        EchoesMreaSections.Parse(Area(new[] { material },new Model(P3,F0,S0,new[] { surface??BasicSurface() }))));
    private static byte[] BasicSurface()=>Surface(0,0x90,new(0,0,0),new(1,1,1),new(2,2,2));
    private static byte[] Surface(uint material,byte opcode,params Corner[] corners)
    {
        var b=new byte[64+3+corners.Length*6+1]; Put(b,12,material); b[64]=opcode; Put16(b,65,checked((ushort)corners.Length));
        for(int i=0;i<corners.Length;i++){int at=67+i*6;Put16(b,at,corners[i].P);Put16(b,at+2,corners[i].T0);Put16(b,at+4,corners[i].T1);}return b;
    }
    private static byte[] Area(byte[][] materials,params Model[] models)
    {
        var sections=new List<byte[]>();
        using(var set=new MemoryStream())
        { Write(set,2);Write(set,0xabcdef01);Write(set,0x12345678);Write(set,(uint)materials.Length);uint end=0;
            foreach(byte[] material in materials){end+=(uint)material.Length;Write(set,end);}foreach(byte[] material in materials)set.Write(material);sections.Add(set.ToArray()); }
        foreach(Model model in models)
        {
            sections.Add(new byte[32]); sections.Add(Floats(model.Positions)); sections.Add(Array.Empty<byte>()); sections.Add(Array.Empty<byte>());
            sections.Add(Floats(model.FloatUvs));
            byte[] shorts=new byte[model.ShortUvs.Length*4];for(int i=0;i<model.ShortUvs.Length;i++)for(int j=0;j<2;j++)BinaryPrimitives.WriteInt16BigEndian(shorts.AsSpan(i*4+j*2,2),model.ShortUvs[i][j]);
            sections.Add(shorts);byte[] count=new byte[4];Put(count,0,(uint)model.Surfaces.Length);sections.Add(count);sections.AddRange(model.Surfaces);
            sections.Add(Array.Empty<byte>());sections.Add(Array.Empty<byte>());
        }
        int table=(0x80+sections.Count*4+31)&~31,payload=(table+16+31)&~31;int length=sections.Sum(x=>x.Length);
        var raw=new byte[payload+length];Put(raw,0,EchoesMreaSections.Magic);Put(raw,4,EchoesMreaSections.Version);Put(raw,0x38,(uint)models.Length);Put(raw,0x40,(uint)sections.Count);
        for(int i=0;i<11;i++)Put(raw,0x44+4*i,uint.MaxValue);Put(raw,0x44,0);Put(raw,0x70,1);Put(raw,table+4,(uint)length);
        int at=payload;for(int i=0;i<sections.Count;i++){Put(raw,0x80+4*i,(uint)sections[i].Length);sections[i].CopyTo(raw,at);at+=sections[i].Length;}return raw;
    }
    private static byte[] Floats(float[][] values)
    {using var b=new MemoryStream();foreach(float[] vector in values)foreach(float scalar in vector)Write(b,unchecked((uint)BitConverter.SingleToInt32Bits(scalar)));return b.ToArray();}
    private static MaterialFixture Material(string variant="common")
    {
        using var b=new MemoryStream();var fields=new Dictionary<string,int>();
        void U(string name,uint value){fields[name]=(int)b.Position;Write(b,value);}
        uint flags=0x36882u|(variant=="konst"?8u:0u)|(variant=="indirect"?0x400u:0u);
        U("flags",flags);int textures=variant=="one-texture"?1:2;U("textureCount",(uint)textures);for(int i=0;i<textures;i++)U("tex"+i,(uint)i);
        U("descriptor",3u|(3u<<8)|(3u<<10));U("unknown0",0);U("unknown1",0);U("group",0);
        if(variant=="konst"){U("konstCount",1);U("konst",uint.MaxValue);}
        U("blend",1); // big-endian ushort destination0, source1
        if(variant=="indirect")U("indirect",0);
        int channels=variant=="two-channels"?2:1;U("channelCount",(uint)channels);U("channel",0x3001);if(channels==2)U("channel1",0);
        int stages=variant=="three-stages"?3:2;U("stageCount",(uint)stages);
        for(int i=0;i<stages;i++)
        {string n="stage"+i;U(n+".color",Pack(i==0?new[]{15,4,8,10}:new[]{15,2,8,15}));U(n+".alpha",Pack(i==0?new[]{7,7,7,6}:new[]{7,7,7,4}));
            U(n+".colorOp",i==0?0x300u:0x100u);U(n+".alphaOp",i==0?0x300u:0x100u);b.WriteByte(0);
            foreach(string name in new[]{"kalpha","kcolor","ras"}){fields[n+"."+name]=(int)b.Position;b.WriteByte(name=="ras"?(byte)4:(byte)0);}}
        for(int i=0;i<stages;i++){b.WriteByte(0);b.WriteByte(0);fields["stage"+i+".tex"]=(int)b.Position;b.WriteByte((byte)Math.Min(i,1));fields["stage"+i+".coord"]=(int)b.Position;b.WriteByte((byte)Math.Min(i,1));}
        int gens=variant=="one-texgen"?1:2;U("texgenCount",(uint)gens);for(int i=0;i<gens;i++)U("texgen"+i,(uint)((4+i)<<4)|(30u<<9)|(61u<<15));
        U("animationSize",variant=="animation"?8u:4u);U("animationCount",variant=="animation"?1u:0u);if(variant=="animation")U("animationMode",0);
        return new(b.ToArray(),fields);
    }
    private static uint Pack(int[] values){uint n=0;for(int i=0;i<4;i++)n|=(uint)values[i]<<(5*i);return n;}
    private static void Coordinates(EchoesSourceCoordinates value,float u,float v,float s,float t,string name)
        =>Check(BitConverter.SingleToInt32Bits(value.BaseU)==BitConverter.SingleToInt32Bits(u)
            &&BitConverter.SingleToInt32Bits(value.BaseV)==BitConverter.SingleToInt32Bits(v)
            &&BitConverter.SingleToInt32Bits(value.LightmapU)==BitConverter.SingleToInt32Bits(s)
            &&BitConverter.SingleToInt32Bits(value.LightmapV)==BitConverter.SingleToInt32Bits(t),name+" exact float bits");
    private static void Write(Stream stream,uint value){Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,value);stream.Write(b);}
    private static void Put(byte[] b,int at,uint value)=>BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at,4),value);
    private static void Put16(byte[] b,int at,ushort value)=>BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at,2),value);
    private static void Check(bool okay,string name){checks++;if(!okay)throw new InvalidOperationException("Source coordinates: "+name);}
    private static void Expect<T>(Action action,string name)where T:Exception{try{action();}catch(T){checks++;return;}throw new InvalidOperationException("Expected "+typeof(T).Name+": "+name);}
}
