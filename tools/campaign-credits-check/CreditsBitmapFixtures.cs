using System.Security.Cryptography;
using MphRead;
using Background=MphRead.Mods.Campaign.CampaignCreditsBackgroundSource;
using Font=MphRead.Mods.Campaign.CampaignCreditsFontSource;
using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using F=CreditsRenderFixture;

// Synthetic palettes/indices/metrics only. No original background, font atlas,
// glyph metric, text, asset payload, decoded picture, or GPU resources are used.
internal static class CreditsBitmapFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;Backgrounds();Fonts();
        Console.WriteLine($"CREDITS BITMAP PASS {checks} invented background/font checks; CPU admission only, no source media or GL.");
    }
    private static void Backgrounds()
    {
        byte[] bytes=BackgroundBytes();string identity=Hash(bytes);var snapshot=Background.Read(bytes);
        Check(snapshot.SourceSha256==identity&&snapshot.ByteLength==49664&&snapshot.Width==256&&snapshot.Height==192&&snapshot.PixelCount==49152,
            "background captures exact49664-byte identity and256x192 row-major dimensions");
        for(int index=0;index<256;index++)
        {
            ColorRgba pixel=snapshot.PixelAt(index,0);
            Check(pixel.Red==(index&31)*255/31&&pixel.Green==((index>>3)&31)*255/31&&pixel.Blue==((255-index)&31)*255/31&&pixel.Alpha==255,
                "every byte index selects its invented RGB555 palette entry using floor expansion");
        }
        Check(snapshot.PixelAt(3,0).Red==24,"background expansion floors3*255/31 instead of reusing rounded model palette conversion");
        Check(snapshot.PixelAt(255,0) is {Red:255,Green:255,Blue:0,Alpha:255},"palette bit15 never changes opaque background alpha");
        foreach(var point in new[]{(255,0),(0,1),(255,1),(0,191),(255,191),(17,73)})
        {
            int index=(point.Item1+point.Item2*3)&255;var pixel=snapshot.PixelAt(point.Item1,point.Item2);
            Check(pixel.Red==(index&31)*255/31&&pixel.Green==((index>>3)&31)*255/31&&pixel.Blue==((255-index)&31)*255/31&&pixel.Alpha==255,
                "row transitions/final pixel/non-square source coordinates preserve top-first row-major indexing");
        }
        ColorRgba retained=snapshot.PixelAt(0,191);Array.Fill(bytes,(byte)0);
        Check(snapshot.SourceSha256==identity&&snapshot.PixelAt(0,191)==retained&&snapshot.PixelAt(255,0).Red==255,
            "background immutable pixels and identity detach from caller palette/index storage");
        Check(Background.Read(BackgroundBytes()).SourceSha256==identity,"invented background deterministic identity");
        foreach(int length in new[]{0,1,511,512,49152,49663,49665})
            Expect<InvalidDataException>(()=>Background.Read(new byte[length]),"background exact palette+payload extent refused before interpretation");
        foreach(var point in new[]{(-1,0),(0,-1),(256,0),(0,192),(int.MaxValue,int.MaxValue),(int.MinValue,0),(0,int.MinValue)})
            Expect<ArgumentOutOfRangeException>(()=>snapshot.PixelAt(point.Item1,point.Item2),"pixel coordinate guard rejects negatives/overflow before linear indexing");
    }
    private static byte[] BackgroundBytes()
    {
        var bytes=new byte[49664];
        for(int index=0;index<256;index++)
        {
            ushort color=(ushort)((index&31)|(((index>>3)&31)<<5)|(((255-index)&31)<<10)|((index&1)<<15));
            F.Half(bytes,index*2,color);
        }
        for(int y=0;y<192;y++)for(int x=0;x<256;x++)bytes[512+y*256+x]=(byte)((x+y*3)&255);
        return bytes;
    }
    private static void Fonts()
    {
        var fixture=F.Model(format:6,width:128,height:256);var model=Model.Read(fixture.Bytes);var widths=Widths();var offsets=Offsets();
        string metricsIdentity=Hash(widths.Concat(offsets).ToArray());var snapshot=Font.Read(model,widths,offsets);
        Check(snapshot.ModelSha256==model.SourceSha256&&snapshot.MetricsSha256==metricsIdentity&&snapshot.GlyphCount==480,
            "font binds exact nativeA3I5 atlas model identity and width-then-offset metric hash");
        for(int glyph=0;glyph<480;glyph++)
            Check(snapshot.Width(glyph)==glyph%9+1&&snapshot.VerticalOffset(glyph)==glyph%4-2,
                "all480 original glyph IDs retain independent width/signed offset values");
        Check(snapshot.Width(0)==1&&snapshot.VerticalOffset(0)==-2&&snapshot.Width(479)==3&&snapshot.VerticalOffset(479)==1,
            "first and last glyph boundary facts are exact, not padded/default values");
        Array.Fill(widths,(byte)0);Array.Fill(offsets,(byte)127);Array.Fill(fixture.Bytes,(byte)0);
        Check(snapshot.Width(0)==1&&snapshot.VerticalOffset(479)==1&&snapshot.MetricsSha256==metricsIdentity&&snapshot.ModelSha256==model.SourceSha256,
            "font owns metric values and model identity after all caller arrays mutate");
        var duplicate=Font.Read(model,Widths(),Offsets());Check(duplicate.MetricsSha256==metricsIdentity&&duplicate.Width(479)==3,"same admitted model and invented metrics produce stable detached identity");
        foreach(int length in new[]{0,1,479,481})
        {
            Expect<InvalidDataException>(()=>Font.Read(model,new byte[length],Offsets()),"exact480 width bytes required");
            Expect<InvalidDataException>(()=>Font.Read(model,Widths(),new byte[length]),"exact480 signed offset bytes required");
        }
        foreach(byte value in new byte[]{0,10,255})
        {var bad=Widths();bad[479]=value;Expect<InvalidDataException>(()=>Font.Read(model,bad,Offsets()),"width1through9 enforced including final glyph");}
        foreach(byte value in new byte[]{2,127,128,253})
        {var bad=Offsets();bad[479]=value;Expect<InvalidDataException>(()=>Font.Read(model,Widths(),bad),"signed vertical offsets restricted to minus2through1 including final glyph");}
        foreach(int glyph in new[]{-1,480,int.MaxValue,int.MinValue})
        {
            Expect<ArgumentOutOfRangeException>(()=>snapshot.Width(glyph),"glyph width rejects out-of-range original ID");
            Expect<ArgumentOutOfRangeException>(()=>snapshot.VerticalOffset(glyph),"glyph offset rejects out-of-range original ID");
        }
        RejectFont(F.Model(format:6,width:8,height:8),"font dimensions cannot borrow an arbitrary valid model texture");
        RejectFont(F.Model(format:6,width:256,height:128),"font atlas dimensions and cell orientation are exact128x256");
        RejectFont(F.Model(format:0,width:128,height:256),"font must use nativeA3I5 rather than another safely admitted texture format");
        RejectFont(F.Model(nodes:2,format:6,width:128,height:256),"font requires exactly one node");
        RejectFont(F.Model(materials:2,format:6,width:128,height:256),"font requires exactly one material and mesh");
        RejectFont(F.Model(textures:2,format:6,width:128,height:256),"font requires exactly one declared atlas texture");
        var twoMeshes=F.Model(materials:2,format:6,width:128,height:256);F.Half(twoMeshes.Bytes,72,1);F.Half(twoMeshes.Bytes,twoMeshes.At["meshes"]+4,0);
        RejectFont(twoMeshes,"two meshes refused even with one otherwise-valid material");
        var untextured=F.Model(format:6,width:128,height:256);F.Half(untextured.Bytes,untextured.At["material0"]+68,65535);F.Half(untextured.Bytes,untextured.At["material0"]+70,65535);
        RejectFont(untextured,"font cannot adopt an otherwise legitimate untextured material");
        var matrix=F.Model(format:6,width:128,height:256);var expanded=new F.Blob();expanded.Add(matrix.Bytes);
        int matrixTable=expanded.Reserve(8),positions=expanded.Reserve(8);expanded.Word(positions,1);expanded.Word(positions+4,1);
        expanded.Half(28,2);expanded.Word(32,(uint)matrixTable);expanded.Word(56,(uint)positions);
        RejectFont(new(expanded.Data.ToArray(),matrix.At),"font requires exactly one matrix entry");
        var palette=F.Model(format:6,width:128,height:256);var pair=new F.Blob();pair.Add(palette.Bytes);int palettes=pair.Reserve(32);
        for(int index=0;index<2;index++){pair.Word(palettes+16*index,(uint)palette.At["paletteData"]);pair.Word(palettes+16*index+4,4);}
        pair.Word(52,(uint)palettes);pair.Half(48,2);pair.Half(palette.At["material0"]+68,1);
        RejectFont(new(pair.Data.ToArray(),palette.At),"font material must bind palette0 rather than another valid palette pair");
    }
    private static byte[] Widths()=>Enumerable.Range(0,480).Select(glyph=>(byte)(glyph%9+1)).ToArray();
    private static byte[] Offsets()=>Enumerable.Range(0,480).Select(glyph=>unchecked((byte)(glyph%4-2))).ToArray();
    // Model admission succeeds first so font-specific failures cannot be masked
    // by unrelated malformed model metadata in these profile cases.
    private static void RejectFont(F.Data data,string label)
    {var model=Model.Read(data.Bytes);Expect<InvalidDataException>(()=>Font.Read(model,Widths(),Offsets()),label);}
    private static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Bitmap fixture: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Bitmap expected "+typeof(T).Name+": "+label);}
}
