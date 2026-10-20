using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MphRead.Mods.Import;
using MphRead.Mods.Import.Retro;

// Own-generated v6 ANCS character and play Set, never an extracted source asset.
internal static class RetroGraphPartsCheck
{
    internal static void Run()
    {
        byte[] source=Fixture();
        var refs=new RetroCharacterRefs(1,2,3);
        const string id="darksamus-echoes",game="G2ME01",name="fixture";
        var parsed=Ancs.Parse(source);
        Check(parsed.Characters.Single().Name==name&&parsed.Set!.Anims.Single().Anim.Kind=="play"
            &&parsed.Set.Anims.Single().Anim.AnimId==7,"synthetic character and complete play Set parse through actual owner");
        var legacy=RetroAnimationGraphProof.FromSource(source,game,16,id,name,refs);
        var body=RetroAnimationGraphProof.FromSource(source,game,16,id,name,refs,sourcePart:"body");
        // Independently pinned original descriptor layout, including property order
        // and omitted part metadata. Body must remain byte-identical to this shape.
        string expected="{\"format\":1,\"sourceGameId\":\"G2ME01\",\"sourceAncsId\":16,\"sourceCharacterId\":\"darksamus-echoes\",\"sourceCharacterName\":\"fixture\",\"sourceSnapshotFile\":\"darksamus-echoes.ancs.bin\",\"sourceAncsBytes\":134,\"sourceAncsSha256\":\""
            +RetroAnimationGraphProof.Sha(source)+"\",\"cmdl\":1,\"cskr\":2,\"cinf\":3,\"animSetVersion\":1,\"animSetOffset\":90,\"animSetLength\":44,\"roots\":[{\"ordinal\":0,\"name\":\"idle\",\"node\":0}],\"nodes\":[{\"index\":0,\"type\":0,\"kind\":\"play\",\"offset\":101,\"length\":25,\"animId\":7,\"primId\":12,\"primName\":\"idle\",\"playParametersOffset\":118,\"playTimeBits\":0,\"playTimeTypeRaw\":0,\"blendParametersOffset\":-1,\"blendFactorBits\":null,\"blendFlagRaw\":null,\"children\":[],\"weights\":[]}],\"transitionNodes\":[{\"index\":0,\"type\":3,\"offset\":126,\"length\":4,\"parametersOffset\":-1,\"animationNode\":null,\"durationBits\":null,\"durationTypeRaw\":null,\"unknownRaw\":null,\"runARaw\":null,\"flagsRaw\":null}],\"fullTransitions\":[],\"defaultTransition\":0,\"halfTransitions\":[],\"resources\":[],\"uninterpretedSetBytesPreservedInSnapshot\":true,\"namedGraphSemanticsAccepted\":false}";
        Check(legacy.Descriptor.SequenceEqual(Encoding.UTF8.GetBytes(expected))&&body.Descriptor.SequenceEqual(legacy.Descriptor),
            "default and explicit body retain original descriptor bytes and raw filename");
        Check(RetroAnimationGraphProof.Verify(legacy.Descriptor,source).SourcePart==null
            &&RetroAnimationGraphProof.Verify(body.Descriptor,source).SourceSnapshotFile==id+".ancs.bin",
            "legacy descriptor without sourcePart remains verifiable");
        var ball=RetroAnimationGraphProof.FromSource(source,game,32,id,name,refs,sourcePart:"ball");
        var ballProof=RetroAnimationGraphProof.Verify(ball.Descriptor,source);
        Check(ballProof.SourcePart=="ball"&&ballProof.SourceCharacterId==id&&ballProof.SourceAncsId==32
            &&ballProof.SourceSnapshotFile==id+".ball.ancs.bin"&&ball.SourceSnapshot.SequenceEqual(source),
            "ball graph retains canonical appearance and distinct bound raw-snapshot namespace");
        using(var json=JsonDocument.Parse(ball.Descriptor))
            Check(json.RootElement.GetProperty("sourcePart").GetString()=="ball"
                &&!Encoding.UTF8.GetString(body.Descriptor).Contains("sourcePart",StringComparison.Ordinal),
                "part discriminator exists only for ball, preserving legacy body schema");
        foreach(string invalid in new[]{"","Body","BALL","weapon","../ball","ball.ancs.bin"})
            Refuse(()=>RetroAnimationGraphProof.CaptureOptional(source,game,16,id,name,refs,invalid),"unsupported capture part "+invalid);
        void Tamper(RetroAnimationGraphProof.Capture capture,Action<JsonObject> change,string why)
        {
            var json=JsonNode.Parse(capture.Descriptor)!.AsObject();change(json);
            Refuse(()=>RetroAnimationGraphProof.Verify(Encoding.UTF8.GetBytes(json.ToJsonString(new JsonSerializerOptions{WriteIndented=false})),source),why);
        }
        Tamper(ball,j=>j["sourcePart"]="body","ball cannot be relabeled as body while retaining its raw filename");
        Tamper(ball,j=>j.Remove("sourcePart"),"ball discriminator cannot be dropped");
        Tamper(ball,j=>j["sourcePart"]="weapon","unknown part is refused on verification");
        Tamper(ball,j=>j["sourcePart"]=7,"non-string part is refused on verification");
        Tamper(ball,j=>j["sourceSnapshotFile"]=id+".ancs.bin","ball raw filename cannot alias body");
        Tamper(body,j=>j["sourceSnapshotFile"]=id+".ball.ancs.bin","body raw filename cannot alias ball");
        Tamper(ball,j=>j["sourceCharacterId"]=id+"-ball","part suffix cannot replace canonical appearance identity");
        Tamper(ball,j=>j["sourceSnapshotFile"]="../"+id+".ball.ancs.bin","raw snapshot reference cannot escape its namespace");
        byte[] corrupt=(byte[])source.Clone();corrupt[105]^=1;
        Refuse(()=>RetroAnimationGraphProof.Verify(ball.Descriptor,corrupt),"raw snapshot tampering cannot reproduce ball proof");
        source[3]=0;
        Check(legacy.SourceSnapshot[3]==1&&ball.SourceSnapshot[3]==1
            &&!ReferenceEquals(legacy.SourceSnapshot,ball.SourceSnapshot),
            "captured body and ball own detached source bytes");
        Console.WriteLine("PASS: synthetic ANCS body/ball graph namespaces, legacy bytes and proof refusal");
    }

    private static byte[] Fixture()
    {
        using var bytes=new MemoryStream();
        void U32(uint v){Span<byte>b=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(b,v);bytes.Write(b);}
        void U16(ushort v){Span<byte>b=stackalloc byte[2];BinaryPrimitives.WriteUInt16BigEndian(b,v);bytes.Write(b);}
        void Text(string v)=>bytes.Write(Encoding.ASCII.GetBytes(v+"\0"));
        U32(0x00010001);U32(1);U32(0);U16(6);Text("fixture");U32(1);U32(2);U32(3);
        U32(0);bytes.Write("PAS4"u8);U32(0);U32(0);
        for(int i=0;i<4;i++)U32(0); // PART/SWHC/unknown/ELSC lists
        U32(0);U32(0);U32(0);U32(0);U32(0);U32(0); // unused, AABB, effects, frozen pair, used animations
        U16(1);U32(1);Text("idle");U32(0);U32(7);U32(12);Text("idle");U32(0);U32(0);U32(3);U32(0);
        return bytes.ToArray();
    }
    private static void Check(bool value,string why)
    {if(!value)throw new InvalidOperationException(why);Console.WriteLine("Graph parts PASS: "+why);}
    private static void Refuse(Action action,string why)
    {try{action();}catch(InvalidDataException){Console.WriteLine("Graph parts PASS: "+why);return;}throw new InvalidOperationException("Expected source proof refusal: "+why);}
}
