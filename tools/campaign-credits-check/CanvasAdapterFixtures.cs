using MphRead;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using OpenTK.Mathematics;
using Adapter=MphRead.Mods.Launcher.RmlUi.Pages.Adventure.CampaignCreditsCanvasAdapter;
using Catalog=MphRead.Mods.Campaign.CampaignCreditsCatalog;
using Owner=MphRead.Mods.Campaign.CampaignCreditsFrameOwner;
using Model=MphRead.Mods.Campaign.CampaignCreditsModelSource;
using Source=MphRead.Mods.Campaign.CampaignCreditsSource;
using Widget=MphRead.Mods.Campaign.CampaignCreditsWidgetSampler;
using Shared=CreditsFrameFixtures;
using F=CreditsRenderFixture;

// Invented already-admitted source fixtures -> actual immutable canvas packets.
// The sink only copies managed ABI arguments; no Host/native library/GL/IO runs.
internal static class CanvasAdapterFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;Transport();BackgroundsAndRefusals();AggregateAdmission();
        Console.WriteLine($"CREDITS CANVAS ADAPTER PASS {checks} invented packet/admission checks; no native IO, original media, clock or GL.");
    }
    private static void Transport()
    {
        var f=Shared.Make(animate:false);var raw=F.Model(materials:3);
        // Materials0/1 share one exact policy;2 references the same native pair
        // with opposite independent wrap axes, and must occupy a separate slot.
        raw.Bytes[raw.At["material2"]+72]=0;raw.Bytes[raw.At["material2"]+73]=1;
        f.Models[f.Models.Keys.Single()]=Model.Read(raw.Bytes);var catalog=Shared.Create(f);var summary=Shared.Summary();
        var bounds=Adapter.Admit(catalog,summary);
        Check(bounds.Count==9&&bounds[6]==14&&bounds[7]==6,"all selected pages receive conservative independent triangle bounds before playback");
        Expect<NotSupportedException>(()=>((IDictionary<int,int>)bounds).Clear(),"page admission map immutable");
        using var timeline=new CampaignCreditsTimeline(f.Document,catalog.Frames,true);using var owner=new Owner(catalog,timeline,summary);
        var adapter=new Adapter(catalog,summary,owner);var sink=new PacketSink();var lease=RmlUiCanvasNativeLease.Request();
        Check(adapter.Table.Count==7&&adapter.Table.ByteLength==918016,"duplicate material policy shares slot; alternate wrap duplicates only required image; font and four backgrounds explicit");
        Check(adapter.Table.Publish(sink,ref lease)==1&&sink.TextureCount==7&&sink.PixelsLength==918016,"actual immutable table publishes exact packed ABI extent");
        Check(sink.Texture(0) is{Size:40,Version:1,Width:8,Height:8,Offset:0,Length:256,Filter:0,WrapS:1,WrapT:0}
            &&sink.Texture(1) is{Width:8,Height:8,Offset:256,Filter:0,WrapS:0,WrapT:1}
            &&sink.Texture(2) is{Width:128,Height:256,Offset:512,Filter:0,WrapS:0,WrapT:0},"widget/font sampler ordinals retain nearest plus exact independent source wraps");
        for(int i=3;i<7;i++)Check(sink.Texture(i) is{Width:256,Height:192,Filter:1,WrapS:0,WrapT:0},"each background is linear/clamp rather than widget sampling");
        Check(sink.Pixel(0)==8&&sink.Pixel(3)==255&&sink.Pixel(512)==8&&sink.Pixel(515)>0&&sink.Pixel(515)<255,
            "native-model RGB and partial font alpha remain straight in table, no adapter premultiplication");
        Check(sink.Pixel(131584)==8&&sink.Pixel(131587)==255,"first invented background preserves separate floor-expanded opaque native bitmap bytes");
        var model=f.Models.Values.Single();var key=new Widget.TextureKey(model.SourceSha256,0,0);var font=new Widget.TextureKey(f.Font.ModelSha256,0,0);
        var triangles=new[]{Triangle(key,10,100,new(0,.5f,1,.5f)),Triangle(key,20,-100,Vector4.One),
            Triangle(font,30,-500,Vector4.One,RepeatMode.Clamp,RepeatMode.Clamp),Triangle(key,40,999,Vector4.One),
            Triangle(key,50,0,Vector4.One,RepeatMode.Clamp,RepeatMode.Repeat),Triangle(new(model.SourceSha256,-1,-1),60,-999,Vector4.One)};
        var frame=new Owner.Snapshot(6,0,false,null,triangles,Array.Empty<Owner.Background>());
        var packet=adapter.Build(frame);triangles[0]=triangles[0] with{A=new(new(999,999,999),new(99,99)),Color=Vector4.Zero};
        Publish(packet,adapter.Table,sink,ref lease);
        Check(sink.Header is{Size:40,Version:1,Revision:7,Width:256,Height:384,Vertices:18,Indices:18,Batches:5,Reserved:0},"source transport emits exact256x384 virtual ABI with one vertex/index per source corner");
        var a=sink.Vertex(0);var b=sink.Vertex(1);var c=sink.Vertex(2);
        Check(a.X==10&&a.Y==172&&b.X==12&&b.Y==172&&c.X==10&&c.Y==170,"only Y192-minus-sourceY projection occurs; sourceZ is not applied to XY");
        Check(a is{R:0,G:128,B:255,A:128},"straight RGBA quantizes0/.5/1 once without multiplying RGB by alpha");
        Check(a.U==-.5f&&a.V==.25f&&b.U==2&&b.V==-1&&c.U==.125f&&c.V==1.5f,"source UVs remain exact, signed and unflipped");
        Check(Enumerable.Range(0,6).Select(i=>sink.Vertex(i*3).X).SequenceEqual(new float[]{10,20,30,40,50,60}),
            "adapter preserves frame-owner source order instead of sorting its deliberately different depth keys");
        Check(Enumerable.Range(0,18).All(i=>sink.Index(i)==i),"published indices preserve consecutive expanded triangle corners");
        Check(sink.Batch(0)==new RmlUiCanvasBatch(0,0,6)&&sink.Batch(1)==new RmlUiCanvasBatch(2,6,3)
            &&sink.Batch(2)==new RmlUiCanvasBatch(0,9,3)&&sink.Batch(3)==new RmlUiCanvasBatch(1,12,3)
            &&sink.Batch(4)==new RmlUiCanvasBatch(uint.MaxValue,15,3),"only consecutive same-policy triangles coalesce; nonconsecutive duplicates and UINTMAX untextured slot preserve order");
        Publish(packet,adapter.Table,sink,ref lease);Check(sink.Vertex(0).X==10&&sink.Vertex(0).G==128,"packet detaches caller frame array and repeat publication does not mutate color/coordinates");
        foreach(var bad in new[]{Triangle(key,0,0,Vector4.One,(RepeatMode)2),Triangle(new("unknown",0,0),0,0,Vector4.One),
            Triangle(new(model.SourceSha256,-1,0),0,0,Vector4.One),Triangle(new(model.SourceSha256,-2,-2),0,0,Vector4.One)})
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Triangles=new[]{bad}}),"unknown sampler, mirror, and mixed/invalid untextured identity refused rather than substituted");
        var invalidColor=Triangle(key,0,0,new(float.NaN,1,1,1));Expect<ArgumentOutOfRangeException>(()=>adapter.Build(frame with{Triangles=new[]{invalidColor}}),"invalid source color cannot silently quantize into packet");
        var closed=adapter.Build(frame with{Closed=true});Publish(closed,adapter.Table,sink,ref lease);
        Check(sink.Header is{Width:256,Height:384,Vertices:0,Indices:0,Batches:0},"closed frame yields explicit empty canvas clear");
    }
    private static void BackgroundsAndRefusals()
    {
        foreach(bool good in new[]{true,false})
        {
            var f=Shared.Make(animate:false);var catalog=Shared.Create(f);var summary=Shared.Summary(good);
            using var timeline=new CampaignCreditsTimeline(f.Document,catalog.Frames,good);using var owner=new Owner(catalog,timeline,summary);
            var adapter=new Adapter(catalog,summary,owner);var sink=new PacketSink();var lease=RmlUiCanvasNativeLease.Request();adapter.Table.Publish(sink,ref lease);
            var images=new[]{new Owner.Background(f.Pictures[good?0:2],0,192,256,192),new(f.Pictures[good?1:3],0,0,256,192)};
            var key=new Widget.TextureKey(f.Models.Values.Single().SourceSha256,0,0);
            var frame=new Owner.Snapshot(good?49:50,0,false,null,new[]{Triangle(key,3,-999,Vector4.One)},images);
            Publish(adapter.Build(frame),adapter.Table,sink,ref lease);
            Check(sink.Header.Vertices==15&&sink.Header.Indices==15&&sink.Header.Batches==3,"results emit two background quads then one source triangle without depth reorder");
            Check(sink.Vertex(0) is{X:0,Y:0,U:0,V:0}&&sink.Vertex(1) is{X:256,Y:0,U:1,V:0}
                &&sink.Vertex(2) is{X:256,Y:192,U:1,V:1}&&sink.Vertex(6) is{X:0,Y:192,U:0,V:0}
                &&sink.Vertex(8) is{X:256,Y:384,U:1,V:1},"top and bottom source rectangles cover exact stacked256x384 with top-first unchanged UV");
            Check(sink.Batch(0)==new RmlUiCanvasBatch((uint)(good?2:4),0,6)&&sink.Batch(1)==new RmlUiCanvasBatch((uint)(good?3:5),6,6)
                &&sink.Batch(2)==new RmlUiCanvasBatch(0,12,3),"good/bad role ordinals remain distinct and backgrounds precede arbitrarily negative foreground depth");
            Check(sink.Vertex(12).X==3&&sink.Vertex(12).Y==172,"foreground widget receives no background-dependent position transform");
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Page=good?50:49}),"opposite ending outcome page refused");
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Backgrounds=Array.Empty<Owner.Background>()}),"missing result background role cannot silently fall back");
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Page=6}),"credits page cannot carry result backgrounds");
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Page=999}),"unselected source page refused");
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Failure="invented failure"}),"failed open frame cannot publish stale presentation");
            var foreign=CampaignCreditsBackgroundSource.Read(new byte[49664]);
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Backgrounds=new[]{images[0] with{Image=foreign},images[1]}}),"background from another admitted catalog refused");
            var bound=Adapter.Admit(catalog,summary)[good?49:50];
            Expect<InvalidDataException>(()=>adapter.Build(frame with{Triangles=Enumerable.Repeat(frame.Triangles[0],bound).ToArray()}),"actual frame counts cannot overrun preadmitted page budget");
        }
    }
    private static void AggregateAdmission()
    {
        var f=Shared.Make(animate:false);var strings=f.Document.Strings.ToDictionary(p=>p.Key,p=>p.Value);
        strings.Add(7,new(7,2049,new string('A',2049)));var pages=f.Document.Pages.ToArray();int p=Array.FindIndex(pages,x=>x.Index==8);
        var item=pages[p].Items[0];pages[p]=pages[p] with{Items=new[]{item with{States=item.States.Select(s=>s with{Text=s.Text! with{StringId=7}}).ToArray()}}};
        f=CopyDocument(f,pages:pages,strings:strings);var catalog=Shared.Create(f);
        Check(catalog.Budget.TextCharacters<65536,"unvisited-page oversized canvas fixture is valid under distinct source catalog bounds");
        Expect<InvalidDataException>(()=>Adapter.Admit(catalog,Shared.Summary()),"all-page preflight rejects page8 before starting initially small page6");
        var mirror=F.Model();mirror.Bytes[mirror.At["material0"]+72]=2;
        Expect<InvalidDataException>(()=>Model.Read(mirror.Bytes),"source mirror flag is already refused before adapter policy admission");
        var overSlots=Shared.Make(animate:false);overSlots.Models[overSlots.Models.Keys.Single()]=PaletteModel(61,8,8);var slotCatalog=Shared.Create(overSlots);
        Check(slotCatalog.TexturePairs.Count==62,"many-palette source fixture remains valid before native canvas's tighter64-slot table budget");
        Expect<InvalidDataException>(()=>Adapter.Admit(slotCatalog,Shared.Summary()),"sixtyone widget pairs plus font/four backgrounds exceed64 canvas slots");
        var exactSlots=Shared.Make(animate:false);exactSlots.Models[exactSlots.Models.Keys.Single()]=PaletteModel(59,8,8);var exactCatalog=Shared.Create(exactSlots);
        _=Adapter.Admit(exactCatalog,Shared.Summary());
        using(var timeline=new CampaignCreditsTimeline(exactSlots.Document,exactCatalog.Frames,true))using(var owner=new Owner(exactCatalog,timeline,Shared.Summary()))
            Check(new Adapter(exactCatalog,Shared.Summary(),owner).Table.Count==64,"exact64 unique native table slots admitted without arbitrary truncation");
        var overBytes=Shared.Make(animate:false);overBytes.Models[overBytes.Models.Keys.Single()]=PaletteModel(4,512,512);var byteCatalog=Shared.Create(overBytes);
        Check(byteCatalog.Budget.TextureRgbaBytes<8*1024*1024,"large source inventory is valid under catalog's distinct image budget");
        Expect<InvalidDataException>(()=>Adapter.Admit(byteCatalog,Shared.Summary()),"four512square palette policies plus required font/background exceed4MiB canvas byte budget");
        long before=GC.GetAllocatedBytesForCurrentThread();Expect<InvalidDataException>(()=>Adapter.Admit(byteCatalog,Shared.Summary()),"warmed overbudget adapter refusal deterministic");
        Check(GC.GetAllocatedBytesForCurrentThread()-before<1024*1024,"all-table preflight refuses before allocating decoded multi-megabyte canvas copies");
        var boundary=Shared.Make(animate:false);string first=boundary.Models.Keys.Single(),second="gameoptions/inventedSmall_Model.bin";
        boundary.Models[first]=PaletteModel(3,512,512);boundary.Models.Add(second,Model.Read(F.Model(width:128,height:256).Bytes));
        var widgets=boundary.Document.Widgets.ToDictionary(pair=>pair.Key,pair=>pair.Value);
        widgets[2]=widgets[2] with{Files=new[]{new Source.WidgetFile(0,(byte)(second.Length+1),0,0,second)}};
        boundary=CopyDocument(boundary,widgets:widgets);var boundaryCatalog=Shared.Create(boundary);
        Check(3*1048576+2*131072+4*196608==4194304,"independent exact byte-budget fixture arithmetic includes native font and four backgrounds");
        Check(Adapter.Admit(boundaryCatalog,Shared.Summary()).Count==9,"exact4MiB policies admitted before any mutable Model sampler construction");
    }
    private static Shared.Fixture CopyDocument(Shared.Fixture f,Source.Page[]? pages=null,Dictionary<int,Source.Widget>? widgets=null,Dictionary<int,Source.Text>? strings=null)
    {
        var d=f.Document;strings??=d.Strings.ToDictionary(p=>p.Key,p=>p.Value);widgets??=d.Widgets.ToDictionary(p=>p.Key,p=>p.Value);
        return f with{Document=new(d.MarmSha256,d.TextSha256,d.HeaderField4,d.HeaderField6,d.HeaderField7,d.HeaderField16,d.GlobalPageCount,
            Math.Max(d.GlobalWidgetCount,widgets.Keys.Max()+1),Math.Max(d.GlobalStringCount,strings.Keys.Max()+1),d.PointerAndRecordVisits,
            pages??d.Pages.ToArray(),widgets,strings)};
    }
    private static Model.Snapshot PaletteModel(int count,int width,int height)
    {
        var source=F.Model(materials:count,width:width,height:height);var bytes=new F.Blob();bytes.Add(source.Bytes);int palettes=bytes.Reserve(count*16);
        for(int i=0;i<count;i++){bytes.Word(palettes+i*16,(uint)source.At["paletteData"]);bytes.Word(palettes+i*16+4,4);bytes.Half(source.At["material"+i]+68,(ushort)i);}
        bytes.Word(52,(uint)palettes);bytes.Half(48,(ushort)count);return Model.Read(bytes.Data.ToArray());
    }
    private static Widget.Triangle Triangle(Widget.TextureKey key,float x,float depth,Vector4 color,RepeatMode s=RepeatMode.Repeat,RepeatMode t=RepeatMode.Clamp)
        =>new(new(new(x,20,999),new(-.5f,.25f)),new(new(x+2,20,-999),new(2,-1)),new(new(x,22,123),new(.125f,1.5f)),color,key,s,t,depth);
    private static void Publish(RmlUiCanvasFrame frame,RmlUiCanvasTextureTable table,PacketSink sink,ref RmlUiCanvasNativeLease lease)
        =>Check(frame.Publish(sink,ref lease,7,table.Count)==1,"actual immutable frame publishes through native ABI seam");
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Canvas adapter: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Canvas adapter expected "+typeof(T).Name+": "+label);}

    private sealed class PacketSink:IRmlUiNativeBridge
    {
        private RmlUiCanvasNativeTexture[] textures=Array.Empty<RmlUiCanvasNativeTexture>();private byte[] pixels=Array.Empty<byte>();
        private RmlUiCanvasVertex[] vertices=Array.Empty<RmlUiCanvasVertex>();private int[] indices=Array.Empty<int>();private RmlUiCanvasBatch[] batches=Array.Empty<RmlUiCanvasBatch>();
        internal RmlUiCanvasNativeFrame Header{get;private set;}internal int TextureCount=>textures.Length;internal int PixelsLength=>pixels.Length;
        internal RmlUiCanvasNativeTexture Texture(int i)=>textures[i];internal byte Pixel(int i)=>pixels[i];
        internal RmlUiCanvasVertex Vertex(int i)=>vertices[i];internal int Index(int i)=>indices[i];internal RmlUiCanvasBatch Batch(int i)=>batches[i];
        public int CanvasTextures(ref RmlUiCanvasNativeLease lease,RmlUiCanvasNativeTexture[] descriptors,uint count,byte[] rgba,uint length)
        {Check(count==descriptors.Length&&length==rgba.Length,"published texture pointer counts match actual private arrays");textures=descriptors.ToArray();pixels=rgba.ToArray();return 1;}
        public int CanvasFrame(ref RmlUiCanvasNativeLease lease,ref RmlUiCanvasNativeFrame header,RmlUiCanvasVertex[] v,int[] i,RmlUiCanvasBatch[] b)
        {Check(header.Vertices==v.Length&&header.Indices==i.Length&&header.Batches==b.Length,"published frame pointer counts match actual private arrays");Header=header;vertices=v.ToArray();indices=i.ToArray();batches=b.ToArray();return 1;}
        // No native lifecycle method should be reached by this transport-only fixture.
        public uint ProtocolVersion()=>throw new NotSupportedException();public ulong Generation()=>throw new NotSupportedException();public ulong HomeDocument()=>throw new NotSupportedException();
        public int Initialize(int w,int h,float d,string r)=>throw new NotSupportedException();public int InitializeBackend(int w,int h,float d,string r,int b)=>throw new NotSupportedException();
        public void Shutdown()=>throw new NotSupportedException();public void Resize(int w,int h,float d)=>throw new NotSupportedException();public void Update()=>throw new NotSupportedException();public void Render(int w,int h)=>throw new NotSupportedException();
        public int TakeIntent(ref RmlUiNativeIntent p)=>0;public int TakeAction(byte[] b,int c)=>0;public ulong OpenDocument(string p,int l)=>0;public int CloseDocument(ulong d)=>0;public int ShowDocument(ulong d,int s)=>0;public int FocusDocument(ulong d,string e)=>0;
        public void SetText(string n,string v)=>throw new NotSupportedException();public void SetBool(string n,int v)=>throw new NotSupportedException();public void SetField(string n,string v)=>throw new NotSupportedException();public int ReadField(string n,byte[] b,int c)=>0;
        public int DocumentSetText(ulong d,string n,string v)=>0;public int DocumentSetBool(ulong d,string n,int v)=>0;public int DocumentSetField(ulong d,string n,string v)=>0;public int DocumentReadField(ulong d,string n,byte[] b,int c)=>0;
        public void SetLobbyAnchor(int s,float x,float y)=>throw new NotSupportedException();public int Back()=>0;public int MouseMove(int x,int y,int m)=>0;public int MouseButton(int b,int d,int m)=>0;public int MouseWheel(float d,int m)=>0;public int Key(int k,int d,int m)=>0;public int Text(uint c)=>0;public void FocusLost()=>throw new NotSupportedException();
        public int TextInputActive()=>0;public int FocusedElement(byte[] b,int c)=>0;public void SetClipboard(string t)=>throw new NotSupportedException();public int ReadClipboard(byte[] b,int c)=>0;
        public int TextInputState(ref RmlUiNativeTextInputState s)=>0;public int Composition(ulong g,ulong d,ulong e,int s,string t,int c,int l)=>0;public int HoveredElement(byte[] b,int c)=>0;
    }
}
