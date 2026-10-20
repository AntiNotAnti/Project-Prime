using System.Collections;
using System.Security.Cryptography;
using System.Text;
using OpenTK.Mathematics;
using Layout=MphRead.Mods.Campaign.CampaignCreditsTextLayout;
using Font=MphRead.Mods.Campaign.CampaignCreditsFontSource;
using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Source=MphRead.Mods.Campaign.CampaignCreditsSource;
using F=CreditsRenderFixture;

// Invented Latin1 strings/metrics and numeric layout oracles only. No source
// game strings, glyph data, atlas pixels, screenshots or renderer are used.
internal static class CreditsTextFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;var font=OwnFont();Placement(font);GlyphBytes(font);Wrapping(font);Colors(font);Limits(font);
        Console.WriteLine($"CREDITS TEXT PASS {checks} invented checks; pure immutable CPU layout/color, no typography substitution or GL.");
    }
    private static void Placement(Font.Snapshot font)
    {
        var layout=Layout.Build(font,"ABCD",Style(),10*4096+2048,192*4096,-5*4096);
        Check(layout.FontModelSha256==font.ModelSha256&&layout.FontMetricsSha256==font.MetricsSha256
            &&layout.TextSha256==Hash("ABCD")&&layout.LineCount==1&&layout.LineWidths.SequenceEqual(new[]{25}),"layout pins both font identities and exactLatin1 body with independently measured25px width");
        Check(layout.Glyphs.Select(g=>g.GlyphId).SequenceEqual(new[]{33,34,35,36}),"ASCII minus32 retains original font glyph IDs");
        float[] xs={10.5f,17.5f,25.5f,34.5f},ys={9,8,7,10};
        for(int i=0;i<4;i++)
        {Near(layout.Glyphs[i].X,xs[i],"fractional source anchor and independent glyph-width pen advance");Near(layout.Glyphs[i].Y,ys[i],"signed metric offset shifts top-left inY-up menu coordinates");Near(layout.Glyphs[i].Depth,27,"negative textdepth uses28+depth/5");}
        var a=layout.Glyphs[0];Near(a.U0,.06265625f,"glyph33 independent insetU0");Near(a.V0,.062578125f,"glyph33 top rowV0 is not flipped");
        Near(a.U1,.12484375f,"glyph33 independent insetU1");Near(a.V1,.093671875f,"glyph33 independent insetV1");
        var positive=Layout.Build(font,"A",Style(),0,192*4096,2*4096);Near(positive.Glyphs[0].Depth,1002,"nonnegative textdepth uses1000+depth");
        var fractional=Layout.Build(font,"A",Style(),0,192*4096,-10240);Near(fractional.Glyphs[0].Depth,27.5f,"fractional negative20.12 depth preserved");
        foreach(var align in new[]{((byte)0,10.5f),((byte)1,3.5f),((byte)2,7.5f)})
        {var result=Layout.Build(font,"A",Style(align:align.Item1),10*4096+2048,192*4096,0);Near(result.Glyphs[0].X,align.Item2,"odd7px line alignment uses floor for centered3px displacement");}
        foreach(byte size in new byte[]{0,8,12,3})
        {
            var result=Layout.Build(font,"A\nD",Style(size:size),0,192*4096,0);float first=size is 0 or 8?9:size==12?13:4;
            Near(result.Glyphs[0].Y,first,"Size changes first baseline without scaling font quad");Near(result.Glyphs[1].Y,2,"next line top subtracts same lineHeight");
            Near(result.Glyphs[0].U1-result.Glyphs[0].U0,.0621875f,"Size never scales native7.96px inset atlas interval");
        }
        var typography=Layout.Build(font,"ABCD",Style(size:12)with{Format1=4,Format2=255,Flags=0xbb},10*4096+2048,192*4096,-5*4096);
        Check(typography.LineWidths.SequenceEqual(new[]{25})&&typography.Glyphs.Select(g=>g.X).SequenceEqual(xs),
            "largerSize and opaque formatting flags do not scale advances or invent typography");
        var spaces=Layout.Build(font,"A B",Style(),0,192*4096,0);
        Check(spaces.LineWidths.SequenceEqual(new[]{16})&&spaces.Glyphs.Count==2,"literalspace advances width1 while emitting no quad");Near(spaces.Glyphs[1].X,8,"literalspace shifts subsequent pen exactly1px");
        Check(layout.LineWidths is IList<int>{IsReadOnly:true}&&layout.Glyphs is IList{IsReadOnly:true},"line widths and value-glyph collection expose immutable wrappers");
        Expect<NotSupportedException>(()=>((IList<int>)layout.LineWidths).Add(99),"line widths cannot mutate captured layout");
        Expect<NotSupportedException>(()=>((IList)layout.Glyphs).Clear(),"glyph clearing cannot mutate captured layout");
        string identity=layout.TextSha256;var before=layout.Glyphs[0];layout.ColorAt(2,long.MaxValue);
        Check(layout.TextSha256==identity&&layout.Glyphs[0].Equals(before),"pure color sampling cannot mutate detached geometry/hash");
    }
    private static void GlyphBytes(Font.Snapshot font)
    {
        var pair=Layout.Build(font,"\u0081@",Style(),0,192*4096,0);
        Check(pair.Glyphs.Count==1&&pair.Glyphs[0].GlyphId==32&&pair.LineWidths.SequenceEqual(new[]{6}),"prefix consumes one byte with low6/low5 formula, not Unicode decoding");
        var encoded=Layout.Build(font,"\u0080 ",Style(),0,192*4096,0);
        Check(encoded.Glyphs.Count==1&&encoded.Glyphs[0].GlyphId==0&&encoded.LineWidths.SequenceEqual(new[]{1}),"pair decoding to space emits a glyph because original leading byte was not literalspace");
        Near(encoded.Glyphs[0].Y,10,"encodedspace retains signed minus2 metric");
        foreach(var single in new[]{("\u0080",96,7),("\u00ff",223,8)})
        {var result=Layout.Build(font,single.Item1,Style(),0,192*4096,0);Check(result.Glyphs.Count==1&&result.Glyphs[0].GlyphId==single.Item2&&result.LineWidths.Single()==single.Item3,"dangling prefix remains its singleLatin1 value before minus32");}
        var last=Layout.Build(font,"\u0087?",Style(),0,192*4096,0);
        Check(last.Glyphs.Single().GlyphId==479&&last.LineWidths.Single()==3,"two-byte original glyph479 reaches last admitted metric");
        Near(last.Glyphs[0].U0,.93765625f,"last metric atlas column15 insetU0");Near(last.Glyphs[0].V0,.906328125f,"last metric atlas row29 insetV0");
        Near(last.Glyphs[0].U1,.99984375f,"last metric insetU1 stays inside128px atlas");Near(last.Glyphs[0].V1,.937421875f,"last metric insetV1 stays in row29");
        var invalid=Layout.Build(font,"\rA\u0088@",Style(),0,192*4096,0);
        Check(invalid.Glyphs.Count==1&&invalid.Glyphs[0].GlyphId==33&&invalid.LineWidths.Single()==23,"invalid belowzero/above479 glyphs each advance8 without quad");Near(invalid.Glyphs[0].X,8,"invalidleading CR fallback moves real glyph pen");
        var newline=Layout.Build(font,"\u0080\nA",Style(),0,192*4096,0);
        Check(newline.LineCount==2&&newline.Glyphs.Select(g=>g.GlyphId).SequenceEqual(new[]{96,33})&&newline.LineWidths.SequenceEqual(new[]{7,7}),"newline split happens before pair decode, never pairs across source lines");
        var crlf=Layout.Build(font,"A\r\nB",Style(),0,192*4096,0);
        Check(crlf.LineWidths.SequenceEqual(new[]{15,8})&&crlf.Glyphs.Count==2,"CRLF is not normalized: retained CR contributes invalid8px on firstline");
        var raw="\u0080 A";var unwrapped=Layout.Build(font,raw,Style(wrap:8),0,192*4096,0);var wrapped=Layout.Build(font,raw,Style(wrap:7),0,192*4096,0);
        Check(unwrapped.LineCount==1&&unwrapped.Glyphs.Select(g=>g.GlyphId).SequenceEqual(new[]{0,33})&&unwrapped.LineWidths.Single()==8,"raw prefix-space pair is preserved when measured raw line fits");
        Check(wrapped.LineCount==2&&wrapped.Glyphs.Select(g=>g.GlyphId).SequenceEqual(new[]{96,33})&&wrapped.LineWidths.SequenceEqual(new[]{7,7}),"word splitting precedes glyph pairing only when actual wrapping is needed");
        Check(pair.TextSha256==Hash("\u0081@"),"body hash uses rawLatin1 bytes rather than UTF8 prefix expansion");
    }
    private static void Wrapping(Font.Snapshot font)
    {
        foreach(var test in new[]{("",0,new[]{0}),("\n",0,new[]{0,0}),("A\n\nD\n",0,new[]{7,0,1,0}),
            ("A B C",16,new[]{16,9}),("ABC",8,new[]{24}),("  A B",18,new[]{18}),("  A B",16,new[]{16}),
            ("A  ",8,new[]{8,0}),("A  B",16,new[]{8,8}),("  ",1,new[]{0}),("  ",2,new[]{2})})
        {
            var result=Layout.Build(font,test.Item1,Style(wrap:(ushort)test.Item2),0,192*4096,0);
            Check(result.LineCount==test.Item3.Length&&result.LineWidths.SequenceEqual(test.Item3),"independent rawword wrapping/emptybuilder/trailing newline width oracle");
        }
        var lines=Layout.Build(font,"A B C",Style(wrap:16),0,192*4096,0);
        Check(lines.Glyphs.Count==3,"wrapped line does not duplicate or drop real glyphs");Near(lines.Glyphs[1].X,8,"wrapped firstline retains literalspaceadvance");
        Near(lines.Glyphs[2].X,0,"wrapped nextline resets pen to ownanchor");Near(lines.Glyphs[2].Y,-1,"wrapped secondline uses8px spacing and positive1offset");
        var retained=Layout.Build(font,"  A B",Style(wrap:18),0,192*4096,0);var collapsed=Layout.Build(font,"  A B",Style(wrap:16),0,192*4096,0);
        Near(retained.Glyphs[0].X,2,"fitting raw line retains leading literalspaces");Near(collapsed.Glyphs[0].X,0,"wrapping-needed builder drops leading empty words as pinned");
    }
    private static void Colors(Font.Snapshot font)
    {
        const uint start=0x1f001f00,end=0x001f001f;var result=Layout.Build(font,"A",Style(start:start,end:end,duration:4096),0,0,0);
        Color(result.ColorAt(17,0),0,1,0,1,"transition frame0 startsRGBA5 color");Color(result.ColorAt(17,1),1,0,1,0,"one-tick transition reaches exactend including zeroalpha");
        Color(result.ColorAt(17,long.MaxValue),1,0,1,0,"transition clamps safely at huge age");
        Color(result.ColorAt(2,0),0,1,0,1,"steady pulse starts atstart");Color(result.ColorAt(2,1),1,0,1,0,"steady pulse reachesend at duration");Color(result.ColorAt(2,2),0,1,0,1,"steady pulse returnsstart after full period");
        var fraction=Layout.Build(font,"A",Style(start:start,end:end,duration:6144),0,0,0);
        Color(fraction.ColorAt(17,1),2/3f,1/3f,2/3f,1/3f,"fractional1.5tick transition uses trueFXduration, not ceiled2");Color(fraction.ColorAt(17,2),1,0,1,0,"fractional transition clamps after duration");
        Color(fraction.ColorAt(2,2),2/3f,1/3f,2/3f,1/3f,"steady fractionalpulse descends from end continuously");Color(fraction.ColorAt(2,3),0,1,0,1,"fractional1.5 duration has exact3tick period");
        Color(fraction.ColorAt(2,long.MaxValue),2/3f,1/3f,2/3f,1/3f,"longMax modulo3 equals1, preserving fractional pulse without overflowing frame times4096");
        var quarter=Layout.Build(font,"A",Style(start:start,end:end,duration:5120),0,0,0);
        Color(quarter.ColorAt(2,long.MaxValue),.4f,.6f,.4f,.6f,"longMax modulo5 equals2 for1.25tick duration's5sample phase period");
        var half=Layout.Build(font,"A",Style(start:start,end:end,duration:2048),0,0,0);
        Color(half.ColorAt(17,1),1,0,1,0,"half-tick transition reachesend at first integerstep");Color(half.ColorAt(2,1),0,1,0,1,"half-tick steady pulse completes full cycle per integerstep");
        var zero=Layout.Build(font,"A",Style(start:start,end:end,duration:0),0,0,0);
        Color(zero.ColorAt(17,long.MaxValue),0,1,0,1,"durationzero preservesstart at any transition age");Color(zero.ColorAt(2,long.MaxValue),0,1,0,1,"durationzero preservesstart forsteady age");
        var masks=Layout.Build(font,"A",Style(start:0xe0ffe0ff,end:0xff00ff00,duration:4096),0,0,0);
        Color(masks.ColorAt(17,0),1,0,1,0,"eachRGBA byte masks low5bits independently, not RGB555 packing");Color(masks.ColorAt(17,1),0,1,0,1,"highbytebits cannot raise normalizedRGBA above1");
        Expect<ArgumentOutOfRangeException>(()=>result.ColorAt(2,-1),"negative steady age refused");Expect<ArgumentOutOfRangeException>(()=>result.ColorAt(17,long.MinValue),"negative transition age refused without overflow");
    }
    private static void Limits(Font.Snapshot font)
    {
        var maximum=Layout.Build(font,new string('A',4096),Style(),0,0,0);Check(maximum.Glyphs.Count==4096&&maximum.LineWidths.Single()==28672,"exact4096Latin1 character boundary preserved without truncation");
        var lines=Layout.Build(font,new string('\n',511),Style(),0,0,0);Check(lines.LineCount==512&&lines.Glyphs.Count==0,"exact512 emptylines admitted with no fabricated glyphs");
        foreach(string text in new[]{new string('A',4097),new string('\n',512),"A\0B","\u0100"})
            Expect<InvalidDataException>(()=>Layout.Build(font,text,Style(),0,0,0),"source length/line budget/Latin1NUL contract refuses rather than truncates");
        Expect<InvalidDataException>(()=>Layout.Build(font,"A",Style(align:3),0,0,0),"unsupported alignment refused");
        Expect<InvalidDataException>(()=>Layout.Build(font,"A",Style(duration:-1),0,0,0),"negative source duration refused before color sampling");
    }
    private static Font.Snapshot OwnFont()
    {
        var data=F.Model(format:6,width:128,height:256);var model=Model.Read(data.Bytes);var widths=Enumerable.Range(0,480).Select(i=>(byte)(i%9+1)).ToArray();
        var offsets=Enumerable.Range(0,480).Select(i=>unchecked((byte)(i%4-2))).ToArray();var font=Font.Read(model,widths,offsets);
        Array.Fill(data.Bytes,(byte)0);Array.Fill(widths,(byte)0);Array.Fill(offsets,(byte)127);return font;
    }
    private static Source.TextStyle Style(ushort wrap=0,byte size=8,byte align=0,int duration=0,uint start=0x1f1f1f1f,uint end=0)
        =>new(0,0,start,end,wrap,0,duration,size,0,0,align,0,0,0);
    private static string Hash(string text)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.Latin1.GetBytes(text)));
    private static void Color(Vector4 value,float r,float g,float b,float a,string label)
    {Check(MathF.Abs(value.X-r)<.00001f&&MathF.Abs(value.Y-g)<.00001f&&MathF.Abs(value.Z-b)<.00001f&&MathF.Abs(value.W-a)<.00001f,label);}
    private static void Near(float actual,float expected,string label)=>Check(float.IsFinite(actual)&&MathF.Abs(actual-expected)<.00001f,label);
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Text fixture: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Text expected "+typeof(T).Name+": "+label);}
}
