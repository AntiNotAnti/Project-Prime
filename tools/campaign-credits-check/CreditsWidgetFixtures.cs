using System.Collections;
using System.Reflection;
using MphRead;
using MphRead.Formats;
using OpenTK.Mathematics;
using Widget=MphRead.Mods.Campaign.CampaignCreditsWidgetSampler;
using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Binding=MphRead.Mods.Campaign.CampaignCreditsAnimationBinding;
using Duration=MphRead.Mods.Campaign.CampaignCreditsAnimationDuration;
using F=CreditsRenderFixture;

// Invented native packets/pools and independent numeric triangles only. No
// source art, Scene, clocks, GL state, item placement or camera is involved.
internal static class CreditsWidgetFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;var paths=(Dictionary<string,string>)typeof(Paths).GetField("_allPaths",BindingFlags.NonPublic|BindingFlags.Static)!.GetValue(null)!;
        var previousPaths=new Dictionary<string,string>(paths,StringComparer.Ordinal);string priorKey=Paths.MphKey;var priorCache=Read.CachedModels.ToArray();bool priorFixes=Read.ApplyFixes;
        string impossible=Path.Combine(Path.GetTempPath(),"credits-widget-no-files-"+Guid.NewGuid().ToString("N"));
        try
        {
            Paths.MphKey="__credits_widget_fixture__";paths[Paths.MphKey]=impossible;
            QuadsAndStrips();SignedAndCarry();TransformsAndMeshReset();TextureIdentity();AnimationAndOwnership();
            Check(!Directory.Exists(impossible)&&Read.CachedModels.SequenceEqual(priorCache)&&Read.ApplyFixes==priorFixes,
                "sampling never consults missing configured root, adopts mutable model cache, or changes decode policy");
            Console.WriteLine($"CREDITS WIDGET PASS {checks} invented checks; pure private CPU sampling, no source art/UI/GL acceptance.");
        }
        finally{paths.Clear();foreach(var pair in previousPaths)paths.Add(pair.Key,pair.Value);Paths.MphKey=priorKey;Read.ApplyFixes=priorFixes;}
    }
    private static void QuadsAndStrips()
    {
        var source=Model.Read(F.Model().Bytes);var sampler=new Widget(source);var sample=sampler.Sample(0,false);
        Check(sampler.SourceSha256==source.SourceSha256&&sampler.AnimationSha256==null&&sampler.FrameCount==1&&sample.AnimationFrame==0&&sample.Triangles.Count==2,
            "still sampler retains admitted source identity and explicitframe0");
        var quad=new[]{new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,2,0),new Vector3(0,2,0)};
        Triangle(sample.Triangles[0],quad[0],quad[1],quad[2],"quad012 native winding with modelScale2");
        Triangle(sample.Triangles[1],quad[0],quad[2],quad[3],"quad023 winding retained without culling/source repair");
        Color(sample.Triangles[0].Color,new(1,1,1,1),"flat still material diffuse/alpha only");Near(sample.Triangles[0].Depth,0,"mean sourceZ remains zero, no itemdepth/projection");
        var strip=new Widget(Model.Read(F.Model(instructions:F.Strip()).Bytes)).Sample(0,false);
        Vector3[] vertices={new(0,0,0),new(2,0,0),new(2,2,0),new(0,2,0),new(4,0,0),new(4,2,0)};
        int[][] indices={new[]{0,1,3},new[]{0,3,2},new[]{2,3,5},new[]{2,5,4}};
        Check(strip.Triangles.Count==4,"six stripvertices expand to four triangles");
        for(int i=0;i<4;i++)Triangle(strip.Triangles[i],vertices[indices[i][0]],vertices[indices[i][1]],vertices[indices[i][2]],"independent quadstrip index oracle");
        var zero=F.Quad().Select(i=>i.Code is >=0x23 and <=0x26?new F.Instruction(0x23,0,0):i).ToArray();
        Check(new Widget(Model.Read(F.Model(instructions:zero).Bytes)).Sample(0,false).Triangles.Count==2,"exact degenerate source triangles are retained rather than normalized/filtered");
    }
    private static void SignedAndCarry()
    {
        var extrema=new F.Instruction[]{new(0x21,V10(-512,511,-1)),new(0x40,1),new(0x23,Pair(-32768,32767),unchecked((ushort)-32768)),
            new(0x24,V10(511,-512,511)),new(0x25,Pair(0,-1)),new(0x26,Pair(-1,-32768)),new(0x41)};
        var signed=new Widget(Model.Read(F.Model(instructions:extrema).Bytes)).Sample(0,false);
        Vector3 a=new(-16,15.99951171875f,-16),b=new(15.96875f,-16,15.96875f),c=new(0,-.00048828125f,15.96875f),d=new(-.00048828125f,-.00048828125f,-16);
        Triangle(signed.Triangles[0],a,b,c,"signed16/10 extrema andXY retainedZ");Triangle(signed.Triangles[1],a,c,d,"XZ retains signed tinyY");
        Color(signed.Triangles[0].Color,Vector4.One,"signedNORMAL changes no flat UI color/lighting");
        var carry=new F.Instruction[]{new(0x40,1),new(0x23,Pair(4096,8192),12288),new(0x25,Pair(16384,20480)),new(0x26,Pair(24576,28672)),new(0x24,V10(-64,-128,-192)),new(0x41),
            new(0x40,1),new(0x25,Pair(0,0)),new(0x26,Pair(4096,0)),new(0x25,Pair(4096,4096)),new(0x26,Pair(0,0)),new(0x41)};
        var retained=new Widget(Model.Read(F.Model(instructions:carry).Bytes)).Sample(0,false);
        Triangle(retained.Triangles[0],new(2,4,6),new(8,10,6),new(12,10,14),"partialXY/XZ use exact prior position");
        Triangle(retained.Triangles[1],new(2,4,6),new(12,10,14),new(-2,-4,-6),"signedVTX10 replaces all three channels");
        Position(retained.Triangles[2].A.Position,new(0,0,-6),"BEGIN/END retain lastZ between primitive groups");Near(retained.Triangles[0].Depth,26/3f,"triangle depth is independent mean transformedZ");
    }
    private static void TransformsAndMeshReset()
    {
        var rotated=F.Model();int root=rotated.At["node0"];F.Half(rotated.Bytes,root+96,16384);PositionFx(rotated.Bytes,root,3,-2,5);
        var rotation=new Widget(Model.Read(rotated.Bytes)).Sample(0,false);
        Triangle(rotation.Triangles[0],new(3,-2,5),new(3,0,5),new(1,0,5),"row-vector90degreeZ then translation then finalScale2");
        Triangle(rotation.Triangles[1],new(3,-2,5),new(1,0,5),new(1,-2,5),"Y-up widget geometry has no text minus192 placement");Near(rotation.Triangles[0].Depth,5,"static translation contributes once to triangledepth");
        var switchCommands=new F.Instruction[]{new(0x40,1),new(0x23,0,0),new(0x14,1),new(0x24,64),new(0x25,Pair(4096,4096)),new(0x14,0),new(0x26,0),new(0x41)};
        var switching=F.Model(nodes:2,instructions:switchCommands);PositionFx(switching.Bytes,switching.At["node0"],1,0,0);PositionFx(switching.Bytes,switching.At["node1"],3,0,0);switching=TwoMatrices(switching);
        var selected=new Widget(Model.Read(switching.Bytes)).Sample(0,false);
        Triangle(selected.Triangles[0],new(1,0,0),new(6,0,0),new(6,2,0),"defaultmatrix0 then restore1 changes only subsequently emitted vertices");
        Triangle(selected.Triangles[1],new(1,0,0),new(6,2,0),new(1,2,0),"restore0 returns to independent root matrix without transposing translation");
        var firstCommands=new F.Instruction[]{new(0x14,1),new(0x22,Pair(-16,32)),new(0x40,1),new(0x23,0,20480),new(0x25,Pair(4096,0)),new(0x25,Pair(4096,4096)),new(0x25,Pair(0,4096)),new(0x41)};
        var secondCommands=new F.Instruction[]{new(0x40,1),new(0x25,0),new(0x26,Pair(4096,0)),new(0x25,Pair(4096,4096)),new(0x26,0),new(0x41)};
        var reset=F.Model(nodes:2,materials:2,width:8,height:16,instructions:firstCommands);PositionFx(reset.Bytes,reset.At["node0"],1,2,3);PositionFx(reset.Bytes,reset.At["node1"],3,4,5);reset=TwoMatrices(reset);
        var bytes=new F.Blob();bytes.Add(reset.Bytes);int second=AppendCommands(bytes,secondCommands);bytes.Word(reset.At["dlists"]+32,(uint)second);bytes.Word(reset.At["dlists"]+36,(uint)(bytes.Data.Count-second));
        var result=new Widget(Model.Read(bytes.Data.ToArray())).Sample(0,false);
        Position(result.Triangles[0].A.Position,new(4,6,18),"first dlist uses restored child matrix and own nonzero sourceZ");
        Position(result.Triangles[2].A.Position,new(1,2,3),"second dlist resets positionZ and matrix0 even when first ends with matrix1/Z5");
        Uv(result.Triangles[0].A.Texcoord,new(-.125f,.125f),"signed texcoord divided by16 and non-square8x16 dimensions");
        Uv(result.Triangles[2].A.Texcoord,Vector2.Zero,"second dlist resets inheritedUV to zero");
        var traversal=F.Model(nodes:3,materials:3);F.Half(traversal.Bytes,traversal.At["node0"]+76,0);
        F.Half(traversal.Bytes,traversal.At["node1"]+76,1);F.Half(traversal.Bytes,traversal.At["node1"]+78,4);
        F.Half(traversal.Bytes,traversal.At["node2"]+76,2);F.Half(traversal.Bytes,traversal.At["node2"]+78,0);
        for(int i=0;i<3;i++)traversal.Bytes[traversal.At["material"+i]+74]=(byte)(i+1);
        var ordered=new Widget(Model.Read(traversal.Bytes)).Sample(0,false);
        Check(ordered.Triangles.Count==6,"rootless-mesh fixture still emits each owned child/sibling mesh");
        Near(ordered.Triangles[0].Color.X,3/31f,"first child emits its mesh2 before later sibling owns meshes0and1");
        Near(ordered.Triangles[2].Color.X,1/31f,"next sibling source mesh span begins at mesh0");Near(ordered.Triangles[4].Color.X,2/31f,"sibling span preserves own consecutive mesh order");
    }
    private static void TextureIdentity()
    {
        var data=F.Model(width:8,height:16);F.Word(data.Bytes,data.At["argument2"],Pair(-16,32));
        F.Word(data.Bytes,data.At["material0"]+104,8192);F.Half(data.Bytes,data.At["material0"]+112,16384);F.Word(data.Bytes,data.At["material0"]+116,4096);F.Word(data.Bytes,data.At["material0"]+120,unchecked((uint)-4096));
        var source=Model.Read(data.Bytes);var sample=new Widget(source).Sample(0,false);var triangle=sample.Triangles[0];
        Uv(triangle.A.Texcoord,new(-.125f,.125f),"TexgenNone ignores static material scale/rotate/translateUV fields");
        Check(triangle.Texture.ModelSha256==source.SourceSha256&&triangle.Texture.TextureId==0&&triangle.Texture.PaletteId==0
            &&triangle.WrapS==RepeatMode.Repeat&&triangle.WrapT==RepeatMode.Clamp,"CPU texture pair identity and independent source repeat/clamp retained");
        var flippedWrap=(byte[])data.Bytes.Clone();flippedWrap[data.At["material0"]+72]=0;flippedWrap[data.At["material0"]+73]=1;
        var wrap=new Widget(Model.Read(flippedWrap)).Sample(0,false).Triangles[0];
        Check(wrap.WrapS==RepeatMode.Clamp&&wrap.WrapT==RepeatMode.Repeat,"source wrap axes can independently reverse clamp/repeat");
        F.Half(data.Bytes,data.At["material0"]+68,65535);F.Half(data.Bytes,data.At["material0"]+70,65535);var untextured=new Widget(Model.Read(data.Bytes)).Sample(0,false);
        Check(untextured.Triangles[0].Texture.TextureId==-1&&untextured.Triangles[0].Texture.PaletteId==-1,"untextured identity remains explicit absentpair");
        Uv(untextured.Triangles[0].A.Texcoord,Vector2.Zero,"untexturedTEXCOORD neither divides by zero nor borrows declared unused texture dimensions");
        var pair=F.Model(materials:2);var blob=new F.Blob();blob.Add(pair.Bytes);int palettes=blob.Reserve(32);
        for(int index=0;index<2;index++){blob.Word(palettes+16*index,(uint)pair.At["paletteData"]);blob.Word(palettes+16*index+4,4);}
        blob.Word(52,(uint)palettes);blob.Half(48,2);blob.Half(pair.At["material1"]+68,1);var pairedModel=Model.Read(blob.Data.ToArray());var paired=new Widget(pairedModel).Sample(0,false);
        Check(paired.Triangles[0].Texture.TextureId==0&&paired.Triangles[0].Texture.PaletteId==0&&paired.Triangles[2].Texture.TextureId==0&&paired.Triangles[2].Texture.PaletteId==1,
            "same encodedtexture with different referenced palette cannot collapse to one art identity");
        var another=F.Model();F.Word(another.Bytes,0,2);var anotherSource=Model.Read(another.Bytes);var other=new Widget(anotherSource).Sample(0,false);
        Check(other.Triangles[0].Texture.ModelSha256==anotherSource.SourceSha256&&other.Triangles[0].Texture.ModelSha256!=source.SourceSha256,"same literal node/material names across different model hashes cannot alias texture identity");
    }
    private static void AnimationAndOwnership()
    {
        var data=F.Model();data.Bytes[data.At["material0"]+66]=0;var model=Model.Read(data.Bytes);var animation=F.Animation(pool:12);int colors=animation.At["colors"],material=animation.At["materialRecords"];
        byte[] colorValues={0,15,31,31,0,31,0,31,0,0,15,31};Array.Copy(colorValues,0,animation.Bytes,colors,12);
        for(int axis=0;axis<3;axis++){F.Half(animation.Bytes,material+94+axis*2,3);F.Half(animation.Bytes,material+110+axis*2,6);}F.Half(animation.Bytes,material+136,9);
        F.Word(animation.Bytes,animation.At["translation"],0);F.Word(animation.Bytes,animation.At["translation"]+4,4096);F.Word(animation.Bytes,animation.At["translation"]+8,8192);
        var bound=Binding.Read(model,Duration.Read(animation.Bytes),animation.Bytes);var sampler=new Widget(model,bound);
        Check(sampler.FrameCount==3&&sampler.SourceSha256==model.SourceSha256&&sampler.AnimationSha256==bound.SourceSha256,"private sampler pins both immutable admitted identities and exactframecount");
        Check(sampler.Sample(0,false).Triangles.Count==0,"animated currentalpha0 omits only frame0");
        var a=sampler.Sample(1,false);var b=sampler.Sample(2,false);var again=sampler.Sample(1,false);
        Check(a.Triangles.Count==2&&b.Triangles.Count==2&&a.AnimationFrame==1&&b.AnimationFrame==2,"staticmaterialalpha0 is retained for visible animated laterframes");
        Color(a.Triangles[0].Color,new Vector4(15/31f),"flatframe1 uses diffuse15 and alpha15, ignoring different ambient0/specular31");
        Color(b.Triangles[0].Color,Vector4.One,"flatframe2 reaches diffuse31/alpha31");
        Position(a.Triangles[0].A.Position,new(1,1,1),"frame1 animatedtranslation remains source menu units after modelScale2");Position(b.Triangles[0].A.Position,new(2,2,2),"frame2 pose independently evaluates explicitage");
        Check(a.Triangles.SequenceEqual(again.Triangles)&&a.Triangles[0].A.Position==new Vector3(1)&&a.Triangles[0].Color==new Vector4(15/31f),"ageA/B/A is deterministic and old ageA output never aliases scratch state");
        var half=sampler.Sample(1,false,.5f);Color(half.Triangles[0].Color,new(15/31f,15/31f,15/31f,15/62f),"callerAlpha multiplies only sampled materialalpha");
        Check(sampler.Sample(2,false,0).Triangles.Count==0&&sampler.Sample(2,false).Triangles.Count==2,"callerAlpha0 does not permanently cache geometry invisibility");
        Check(sampler.Sample(3,true).AnimationFrame==0&&sampler.Sample(3,true).Triangles.Count==0&&sampler.Sample(3,false).AnimationFrame==2,"loop/hold reduceage atN before integer/frame indexing");
        Check(sampler.Sample(4,true).AnimationFrame==1&&sampler.Sample(long.MaxValue,true).AnimationFrame==1&&sampler.Sample(long.MaxValue,false).AnimationFrame==2,"Nplus1 and longMax select independent expected mod3/lastframe");
        Check(a.Triangles is IList{IsReadOnly:true},"published triangle collection immutable");Expect<NotSupportedException>(()=>((IList)a.Triangles).Clear(),"callers cannot clear published triangles");
        var position=a.Triangles[0].A.Position;position.X=999;Check(a.Triangles[0].A.Position.X==1,"published vertexposition is a detached structvalue");
        Expect<ArgumentOutOfRangeException>(()=>sampler.Sample(-1,false),"negativeage rejected");
        foreach(float alpha in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-.001f,1.001f})Expect<ArgumentOutOfRangeException>(()=>sampler.Sample(0,false,alpha),"callerAlpha finite0through1 required");
        Exception? foreign=null;var thread=new Thread(()=>{try{sampler.Sample(1,false);}catch(Exception error){foreign=error;}}){IsBackground=true};thread.Start();Check(thread.Join(TimeSpan.FromSeconds(5))&&foreign is InvalidOperationException,"creatingthread owns private mutable model sampler");
        Check(sampler.Sample(1,false).Triangles.SequenceEqual(a.Triangles),"wrongthread refusal leaves original owner's sample state unchanged");
    }
    private static F.Data TwoMatrices(F.Data source)
    {var bytes=new F.Blob();bytes.Add(source.Bytes);int matrices=bytes.Reserve(8),positions=bytes.Reserve(8);bytes.Word(matrices+4,1);bytes.Word(positions,1);bytes.Word(positions+4,1);bytes.Half(28,2);bytes.Word(32,(uint)matrices);bytes.Word(56,(uint)positions);return new(bytes.Data.ToArray(),source.At);}
    private static int AppendCommands(F.Blob bytes,IReadOnlyList<F.Instruction> instructions)
    {int start=bytes.Data.Count;for(int packet=0;packet<instructions.Count;packet+=4){int commands=bytes.Reserve(4),count=Math.Min(4,instructions.Count-packet);for(int i=0;i<count;i++)bytes.Byte(commands+i,instructions[packet+i].Code);for(int i=0;i<count;i++)foreach(uint argument in instructions[packet+i].Arguments){int at=bytes.Reserve(4);bytes.Word(at,argument);}}return start;}
    private static uint Pair(int low,int high)=>unchecked((ushort)low)|((uint)unchecked((ushort)high)<<16);
    private static uint V10(int x,int y,int z)=>((uint)x&1023)|(((uint)y&1023)<<10)|(((uint)z&1023)<<20);
    private static void PositionFx(byte[] bytes,int node,int x,int y,int z){F.Word(bytes,node+100,unchecked((uint)(x*4096)));F.Word(bytes,node+104,unchecked((uint)(y*4096)));F.Word(bytes,node+108,unchecked((uint)(z*4096)));}
    private static void Triangle(Widget.Triangle actual,Vector3 a,Vector3 b,Vector3 c,string label)
    {Position(actual.A.Position,a,label);Position(actual.B.Position,b,label);Position(actual.C.Position,c,label);}
    private static void Position(Vector3 actual,Vector3 expected,string label)=>Check((actual-expected).Length<.00001f&&float.IsFinite(actual.X)&&float.IsFinite(actual.Y)&&float.IsFinite(actual.Z),label);
    private static void Uv(Vector2 actual,Vector2 expected,string label)=>Check((actual-expected).Length<.00001f&&float.IsFinite(actual.X)&&float.IsFinite(actual.Y),label);
    private static void Color(Vector4 actual,Vector4 expected,string label)=>Check((actual-expected).Length<.00001f,label);
    private static void Near(float actual,float expected,string label)=>Check(float.IsFinite(actual)&&MathF.Abs(actual-expected)<.00001f,label);
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Widget fixture: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Widget expected "+typeof(T).Name+": "+label);}
}
