using System.Security.Cryptography;
using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using F=CreditsRenderFixture;

internal static class CreditsModelFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;var f=F.Model();var snapshot=Model.Read(f.Bytes);
        Check(snapshot.SourceSha256==Hash(f.Bytes)&&snapshot.ByteLength==f.Bytes.Length,"model identity hashes exact admitted bytes");
        Check(snapshot.NodeCount==1&&snapshot.MaterialCount==1&&snapshot.MeshCount==1&&snapshot.TextureCount==1&&snapshot.MatrixCount==1
            &&snapshot.PixelCount==64&&snapshot.VertexCount==4&&snapshot.TriangleCount==2&&snapshot.MaximumNodeDepth==1,
            "invented quad has four emitted vertices and two expanded triangles despite one declared primitive");
        string identity=snapshot.SourceSha256;Array.Fill(f.Bytes,(byte)0);
        Check(snapshot.SourceSha256==identity&&snapshot.TriangleCount==2,"admitted model snapshot detached from caller bytes");
        Check(Model.Read(F.Model(instructions:F.Strip()).Bytes).TriangleCount==4,"six independent quadstrip vertices expand to two quads/four triangles");
        Check(Model.Read(F.Model(nodes:3).Bytes).MaximumNodeDepth==2,"one root with two sibling children traverses exactly once");
        Check(Model.Read(F.Model(nodes:16,chain:true).Bytes).MaximumNodeDepth==16,"graph depth16 admitted inclusively");
        Check(Model.Read(F.Model(nodes:64).Bytes).NodeCount==64,"bounded64-node sibling graph admitted without requiring every node own meshes");
        var unusedTextureCount=F.Model();F.Half(unusedTextureCount.Bytes,98,1);
        Check(Model.Read(unusedTextureCount.Bytes).MatrixCount==1,"unused texture-matrix count1 with absent runtime pointer and no texgen admitted");
        var extraTextureCount=F.Model();F.Half(extraTextureCount.Bytes,98,2);Reject(extraTextureCount.Bytes,"unused texture-matrix metadata remains bounded to observed0or1");
        foreach(int format in new[]{0,1,2,6})
        {var shortPalette=F.Model(format:format,paletteEntries:2);Check(Model.Read(shortPalette.Bytes).PixelCount==64,"actual index1 permits short valid two-entry palette for format"+format);}
        var invisible=F.Model();invisible.Bytes[invisible.At["material0"]+66]=0;
        Check(Model.Read(invisible.Bytes).TriangleCount==2,"static alpha0 does not silently discard potentially animated geometry");
        var untextured=F.Model();F.Half(untextured.Bytes,untextured.At["material0"]+68,65535);F.Half(untextured.Bytes,untextured.At["material0"]+70,65535);
        Check(Model.Read(untextured.Bytes) is {MaterialCount:1,VertexCount:4,TriangleCount:2},"exact minus1 texture/palette pair preserves legitimate untextured material geometry");
        foreach(var pair in new[]{(-1,0),(0,-1),(-2,-2),(-2,0),(0,-2),(-1,-2),(-2,-1)})
        {var bad=F.Model();F.Half(bad.Bytes,bad.At["material0"]+68,unchecked((ushort)pair.Item1));F.Half(bad.Bytes,bad.At["material0"]+70,unchecked((ushort)pair.Item2));Reject(bad.Bytes,"only exact paired minus1 permits absent texture/palette; mixed or other negative IDs refused");}
        var signedScale=F.Model();F.Word(signedScale.Bytes,signedScale.At["node0"]+80,unchecked((uint)-65536));F.Word(signedScale.Bytes,signedScale.At["node0"]+84,65536);
        Check(Model.Read(signedScale.Bytes).TriangleCount==2,"nonzero signed static scales admit exact plus/minus16 boundary");
        var countBoundary=F.Model(textures:64);Check(Model.Read(countBoundary.Bytes).TextureCount==64&&Model.Read(countBoundary.Bytes).PixelCount==4096,"texture count and aggregate pixel accounting include unreferenced declared entries");
        foreach(int length in new[]{0,1,28,99})Reject(F.Model().Bytes[..length],"truncated native100-byte header");
        Reject(new byte[4*1024*1024+1],"model source byte cap");
        foreach(int field in new[]{16,20,24,32,36,44,52,56})
        {var bad=F.Model();F.Word(bad.Bytes,field,uint.MaxValue);Reject(bad.Bytes,"declared table range/offset overflow");}
        foreach(var count in new[]{(28,65),(72,65),(74,65),(40,65),(48,65),(96,129)})
        {var bad=F.Model();F.Half(bad.Bytes,count.Item1,(ushort)count.Item2);Reject(bad.Bytes,"count admission occurs before source-sized arrays");}
        foreach(int pointer in new[]{60,64,68,76,80,84,88,92})
        {var bad=F.Model();F.Word(bad.Bytes,pointer,100);Reject(bad.Bytes,"unsupported skin/runtime pointer never followed");}
        foreach(var change in new[]{(0,13u),(4,4095u),(8,2u),(12,5u)})
        {var bad=F.Model();F.Word(bad.Bytes,change.Item1,change.Item2);Reject(bad.Bytes,"scale or declared primitive/vertex metadata refused");}
        var flags=F.Model();flags.Bytes[30]=1;Reject(flags.Bytes,"unsupported file flags");
        foreach(int relative in new[]{80,84,88})foreach(uint value in new[]{0u,65537u,unchecked((uint)-65537)})
        {var bad=F.Model();F.Word(bad.Bytes,bad.At["node0"]+relative,value);Reject(bad.Bytes,"static node scale must be nonzero and bounded to16abs");}
        foreach(var change in new[]{(64,0),(66,0),(68,0),(66,3),(64,-2)})
        {var bad=F.Model(nodes:3);F.Half(bad.Bytes,bad.At["node0"]+change.Item1,unchecked((ushort)change.Item2));Reject(bad.Bytes,"root graph identity/cycle/range refused");}
        var orphan=F.Model(nodes:3);F.Half(orphan.Bytes,orphan.At["node1"]+68,65535);Reject(orphan.Bytes,"unreachable advertised node refused");
        var mismatch=F.Model(nodes:3);F.Half(mismatch.Bytes,mismatch.At["node1"]+64,2);Reject(mismatch.Bytes,"parent must agree with actual rooted traversal");
        var duplicate=F.Model(nodes:3);F.Half(duplicate.Bytes,duplicate.At["node1"]+66,2);Reject(duplicate.Bytes,"node cannot be reached by both child and sibling edges");
        Reject(F.Model(nodes:17,chain:true).Bytes,"depth17 exceeds bounded graph traversal");
        foreach(var field in new[]{("node0",72),("node0",140)})
        {var bad=F.Model();F.Word(bad.Bytes,bad.At[field.Item1]+field.Item2,field.Item2==72?0u:1u);Reject(bad.Bytes,"unsupported disabled/billboard profile");}
        foreach(int relative in new[]{76,78}){var bad=F.Model();F.Half(bad.Bytes,bad.At["node0"]+relative,3);Reject(bad.Bytes,"mesh span/odd native mesh identifier refused");}
        var badMatrix=F.Model();F.Word(badMatrix.Bytes,badMatrix.At["matrix"],1);Reject(badMatrix.Bytes,"matrix node ordinal must exist");
        var matrixCap=F.Model();var matrixBytes=new F.Blob();matrixBytes.Add(matrixCap.Bytes);int matrixTable=matrixBytes.Reserve(32*4),matrixPositions=matrixBytes.Reserve(32*4);
        for(int i=0;i<32;i++)matrixBytes.Word(matrixPositions+i*4,1);
        matrixBytes.Half(28,32);matrixBytes.Word(32,(uint)matrixTable);matrixBytes.Word(56,(uint)matrixPositions);
        Reject(matrixBytes.Data.ToArray(),"native31-entry matrix bound refuses32 complete otherwise in-range entries");
        var skin=F.Model();F.Word(skin.Bytes,skin.At["posCounts"],2);Reject(skin.Bytes,"multi-position skin profile refused");
        foreach(int offset in new[]{0,2}){var bad=F.Model();F.Half(bad.Bytes,bad.At["meshes"]+offset,1);Reject(bad.Bytes,"mesh material/display-list reference bounded");}
        var duplicateDlist=F.Model(materials:2);F.Half(duplicateDlist.Bytes,duplicateDlist.At["meshes"]+6,0);
        Reject(duplicateDlist.Bytes,"each display-list ID has exactly one mesh owner; duplicate ID cannot hide unreferenced list");
        var duplicateMesh=F.Model(nodes:2);F.Half(duplicateMesh.Bytes,duplicateMesh.At["node1"]+76,1);
        Reject(duplicateMesh.Bytes,"each mesh has one node owner rather than duplicate graph adoption");
        var orphanMesh=F.Model();F.Half(orphanMesh.Bytes,orphanMesh.At["node0"]+76,0);
        Reject(orphanMesh.Bytes,"unowned mesh cannot become detached counted geometry");
        foreach(string kind in new[]{"node","material"})
        {
            var noTerm=F.Model();Array.Fill(noTerm.Bytes,(byte)'A',noTerm.At[kind+"0"],64);Reject(noTerm.Bytes,"name requires in-span ASCII terminator");
            var nonAscii=F.Model();nonAscii.Bytes[nonAscii.At[kind+"0"]]=0x80;Reject(nonAscii.Bytes,"name must be printable ASCII");
        }
        var names=F.Model(nodes:3);Array.Copy(names.Bytes,names.At["node1"],names.Bytes,names.At["node2"],64);Reject(names.Bytes,"node names unique");
        var matNames=F.Model(materials:2);Array.Copy(matNames.Bytes,matNames.At["material0"],matNames.Bytes,matNames.At["material1"],64);Reject(matNames.Bytes,"material names unique");
        foreach(var change in new[]{(64,0),(65,1),(66,32),(67,1),(72,2),(73,2),(88,1),(89,1)})
        {var bad=F.Model();bad.Bytes[bad.At["material0"]+change.Item1]=(byte)change.Item2;Reject(bad.Bytes,"unsupported observed material profile");}
        foreach(int relative in new[]{84,92,100}){var bad=F.Model();F.Word(bad.Bytes,bad.At["material0"]+relative,1);Reject(bad.Bytes,"material polygon/texgen/matrix profile refused");}
        foreach(int relative in new[]{96,124,126})
        {var bad=F.Model();F.Half(bad.Bytes,bad.At["material0"]+relative,1);Reject(bad.Bytes,"file material cannot preinstall runtime animation identity at byte"+relative);}
        foreach(int relative in new[]{68,70}){var bad=F.Model();F.Half(bad.Bytes,bad.At["material0"]+relative,1);Reject(bad.Bytes,"material texture/palette reference bounded");}
        foreach(int format in new[]{3,4,5,7,255}){var bad=F.Model();bad.Bytes[bad.At["textures"]]=(byte)format;Reject(bad.Bytes,"unobserved texture encoding refused");}
        foreach(ushort dimension in new ushort[]{0,4,9,1024}){var bad=F.Model();F.Half(bad.Bytes,bad.At["textures"]+2,dimension);Reject(bad.Bytes,"texture dimensions bounded power-of-two8..512");}
        foreach(uint size in new[]{15u,17u,uint.MaxValue}){var bad=F.Model();F.Word(bad.Bytes,bad.At["textures"]+12,size);Reject(bad.Bytes,"encoded image size must exactly match dimensions/format");}
        foreach(int relative in new[]{8,12}){var bad=F.Model();F.Word(bad.Bytes,bad.At["textures"]+relative,uint.MaxValue);Reject(bad.Bytes,"texture range/length overflow refused");}
        var oddPalette=F.Model();F.Word(oddPalette.Bytes,oddPalette.At["palette"]+4,3);Reject(oddPalette.Bytes,"palette entries must be complete16-bit colors");
        foreach(int format in new[]{0,1,2,6})
        {var bad=F.Model(format:format,paletteEntries:2);bad.Bytes[bad.At["image0"]]=(byte)(format==0?0x03:format==1?0x03:format==6?0xe3:3);Reject(bad.Bytes,"actual referenced palette index out of short palette for format"+format);}
        var pairs=F.Model(materials:2);var appended=new F.Blob();appended.Add(pairs.Bytes);int paletteTable=appended.Reserve(32);
        for(int p=0;p<2;p++){appended.Word(paletteTable+p*16,(uint)pairs.At["paletteData"]);appended.Word(paletteTable+p*16+4,(uint)(p==0?4:2));}
        appended.Word(52,(uint)paletteTable);appended.Half(48,2);appended.Half(pairs.At["material1"]+68,1);
        Reject(appended.Data.ToArray(),"same encoded texture must fit each referenced material palette pair");
        Reject(F.Model(textures:2,width:512,height:512).Bytes,"aggregate pixels exceed262144 even when each image and source byte extent fit");
        Commands();var tiny=F.Model();F.Half(tiny.Bytes,74,ushort.MaxValue);for(int i=0;i<8;i++)Reject(tiny.Bytes,"warm impossible declared node count");
        long before=GC.GetAllocatedBytesForCurrentThread();Reject(tiny.Bytes,"impossible counts refuse before huge arrays");long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Check(allocated<1024*1024,"small malicious count allocates below1MiB warmed allowance");
        Console.WriteLine($"CREDITS MODEL PASS {checks} invented checks; tiny invalid allocation {allocated} bytes; admission only, no decoder/GPU.");
    }
    private static void Commands()
    {
        foreach(byte command in new byte[]{0x20,0x27,0x28,0x30,0xff})
        {var bad=F.Model();bad.Bytes[bad.At["command1"]]=command;Reject(bad.Bytes,"unobserved or unknown DS command refused");}
        foreach(uint mode in new[]{0u,2u,4u,uint.MaxValue}){var bad=F.Model();F.Word(bad.Bytes,bad.At["argument3"],mode);Reject(bad.Bytes,"only quads and quadstrips admitted");}
        var restore=F.Model();F.Word(restore.Bytes,restore.At["argument0"],1);Reject(restore.Bytes,"MTX_RESTORE ID bounded to admitted matrix table");
        var nested=F.Quad().ToArray();nested[2]=new(0x40,1);Reject(F.Model(instructions:nested).Bytes,"nested BEGIN refused");
        Reject(F.Model(instructions:F.Quad().Where(i=>i.Code!=0x40).ToArray()).Bytes,"vertex outside primitive refused");
        Reject(F.Model(instructions:F.Quad().Where(i=>i.Code!=0x41).ToArray()).Bytes,"unterminated primitive refused");
        Reject(F.Model(instructions:new[]{new F.Instruction(0x41)}).Bytes,"END without BEGIN refused");
        Reject(F.Model(instructions:F.Quad().Where((_,index)=>index!=7).ToArray()).Bytes,"incomplete three-vertex quad refused");
        Reject(F.Model(instructions:F.Strip().Where((_,index)=>index!=9).ToArray()).Bytes,"odd-length quadstrip refused");
        var extent=F.Model();F.Word(extent.Bytes,extent.At["dlists"]+4,5);Reject(extent.Bytes,"display-list byte extent must contain full command words");
        var truncated=F.Model();F.Word(truncated.Bytes,truncated.At["dlists"]+4,4);Reject(truncated.Bytes,"command arguments cannot read into following tables/data past declared list");
        var many=Enumerable.Repeat(new F.Instruction(0),32769).Concat(F.Quad()).ToArray();Reject(F.Model(instructions:many).Bytes,"aggregate command guard before expanded allocation");
    }
    private static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Model fixture: "+label);}
    private static void Reject(byte[] bytes,string label){checks++;try{Model.Read(bytes);}catch(InvalidDataException){return;}throw new InvalidOperationException("Model expected refusal: "+label);}
}
