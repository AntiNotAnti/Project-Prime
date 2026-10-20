using System.Reflection;
using System.Runtime.InteropServices;
using MphRead.Mods.Launcher.RmlUi.Host;

// Only invented straight-RGBA packets and the real managed Host are exercised.
// Native rendering/ABI behavior has a separate ctypes CPU acceptance owner.
internal static class CanvasOwnerChecks
{
    private static int checks;
    internal static void Run()
    {
        checks=0;AbiAndQuantization();PacketOwnership();PacketBounds();CapabilityAndTokens();PublicationAndState();EpochAndLifetime();
        Console.WriteLine($"CANVAS OWNER PASS {checks} invented managed packet/Host checks; no native library, GL or source media.");
    }
    private static void AbiAndQuantization()
    {
        Size<RmlUiCanvasNativeLease>(32);Size<RmlUiCanvasNativeTexture>(40);Size<RmlUiCanvasVertex>(20);
        Size<RmlUiCanvasBatch>(16);Size<RmlUiCanvasNativeFrame>(40);Size<RmlUiCanvasNativeState>(32);
        Offset<RmlUiCanvasNativeLease>("Size",0);Offset<RmlUiCanvasNativeLease>("Version",4);Offset<RmlUiCanvasNativeLease>("Generation",8);
        Offset<RmlUiCanvasNativeLease>("Document",16);Offset<RmlUiCanvasNativeLease>("Epoch",24);
        string[] textureFields={"Size","Version","Width","Height","Offset","Length","Filter","WrapS","WrapT","Reserved"};
        for(int i=0;i<textureFields.Length;i++)Offset<RmlUiCanvasNativeTexture>(textureFields[i],i*4);
        Offset<RmlUiCanvasVertex>("X",0);Offset<RmlUiCanvasVertex>("Y",4);Offset<RmlUiCanvasVertex>("R",8);Offset<RmlUiCanvasVertex>("G",9);
        Offset<RmlUiCanvasVertex>("B",10);Offset<RmlUiCanvasVertex>("A",11);Offset<RmlUiCanvasVertex>("U",12);Offset<RmlUiCanvasVertex>("V",16);
        string[] batchFields={"Texture","First","Count","Reserved"};
        for(int i=0;i<batchFields.Length;i++)
        {
            string name=typeof(RmlUiCanvasBatch).GetFields(BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)
                .Single(f=>f.Name==batchFields[i]||f.Name=="<"+batchFields[i]+">k__BackingField").Name;
            Offset<RmlUiCanvasBatch>(name,i*4);
        }
        Offset<RmlUiCanvasNativeFrame>("Revision",8);Offset<RmlUiCanvasNativeFrame>("Width",16);Offset<RmlUiCanvasNativeFrame>("Height",20);
        Offset<RmlUiCanvasNativeFrame>("Vertices",24);Offset<RmlUiCanvasNativeFrame>("Indices",28);Offset<RmlUiCanvasNativeFrame>("Batches",32);Offset<RmlUiCanvasNativeFrame>("Reserved",36);
        Offset<RmlUiCanvasNativeState>("Revision",8);Offset<RmlUiCanvasNativeState>("TextureCount",16);Offset<RmlUiCanvasNativeState>("BatchCount",20);
        Offset<RmlUiCanvasNativeState>("Flags",24);Offset<RmlUiCanvasNativeState>("Reserved",28);
        Check(RmlUiCanvasVertex.Quantize(0)==0&&RmlUiCanvasVertex.Quantize(.5f)==128&&RmlUiCanvasVertex.Quantize(1)==255,
            "quantization rounds finite straight color endpoints/half to0/128/255");
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,-.001f,1.001f})
            Expect<ArgumentOutOfRangeException>(()=>RmlUiCanvasVertex.Quantize(value),"quantization rejects nonfinite/out-of-domain alpha or channel");
    }
    private static void PacketOwnership()
    {
        var native=new FakeBridge{CanvasAbiVersion=1};using var host=Host(native);using var canvas=host.AcquireCanvas(host.HomeDocument,"canvas")!;
        byte[] pixels={200,100,50,128,255,127,63,64,31,255,7,0};
        var images=new[]{new RmlUiCanvasImage(1,1,pixels.AsMemory(0,4),0,1,0),new RmlUiCanvasImage(2,1,pixels.AsMemory(4,8),1,0,1)};
        var table=new RmlUiCanvasTextureTable(images);Array.Fill(pixels,(byte)0);images[0]=new(1,1,new byte[4],1,0,0);
        Check(table.Count==2&&table.ByteLength==12&&canvas.SetTextures(table),"table owns exact copied packed extent before caller image/pixel mutation");
        Check(native.CanvasPixel(0)==200&&native.CanvasPixel(3)==128&&native.CanvasPixel(4)==255&&native.CanvasPixel(7)==64,
            "managed table remains straight RGBA; native alone owns premultiplication");
        var first=native.CanvasTexture(0);var second=native.CanvasTexture(1);
        Check(first is{Size:40,Version:1,Width:1,Height:1,Offset:0,Length:4,Filter:0,WrapS:1,WrapT:0,Reserved:0}
            &&second is{Width:2,Height:1,Offset:4,Length:8,Filter:1,WrapS:0,WrapT:1},"wire descriptors retain per-image nearest/linear and independent wrap axes");
        var vertices=Vertices();int[] indices={0,1,2};var batches=new[]{new RmlUiCanvasBatch(1,0,3)};
        var frame=new RmlUiCanvasFrame(320,240,vertices,indices,batches);vertices[0]=new(999,999,0,0,0,0,999,999);indices[0]=2;batches[0]=new(0,0,3);
        Check(frame.VertexCount==3&&frame.IndexCount==3&&frame.BatchCount==1&&canvas.Submit(frame),"frame owns copied vertices/indices/batches before caller mutation");
        Check(native.CanvasFrameMatches(Vertices(),new[]{0,1,2},new[]{new RmlUiCanvasBatch(1,0,3)}),"wire packet preserves detached position/straightcolor/UV/nativeordinal and source index order");
        Check(native.CanvasLastHeader is{Size:40,Version:1,Revision:1,Width:320,Height:240,Vertices:3,Indices:3,Batches:1,Reserved:0},"host emits exact ABI header and starts accepted revision1");
        Check(canvas.Submit(frame)&&native.CanvasLastHeader.Revision==2&&native.CanvasFrameMatches(Vertices(),new[]{0,1,2},new[]{new RmlUiCanvasBatch(1,0,3)}),
            "same immutable packet can be submitted again without retained scratch/premultiply mutation");
        var empty=new RmlUiCanvasFrame(1,16384,Array.Empty<RmlUiCanvasVertex>(),Array.Empty<int>(),Array.Empty<RmlUiCanvasBatch>());
        Check(canvas.Submit(empty)&&native.CanvasLastHeader is{Revision:3,Vertices:0,Indices:0,Batches:0},"allzero counts form explicit clear at finite virtual extent boundaries");
        using var untextured=host.AcquireCanvas(host.HomeDocument,"untextured")!;
        var noImages=new RmlUiCanvasTextureTable(Array.Empty<RmlUiCanvasImage>());
        Check(noImages.Count==0&&noImages.ByteLength==0&&untextured.SetTextures(noImages)&&untextured.Submit(Frame(texture:uint.MaxValue)),
            "explicit empty table permits UINTMAX untextured geometry without borrowing a native texture handle");
        int frameCalls=native.CanvasFrameCalls;
        Check(!untextured.Submit(Frame(texture:0))&&native.CanvasFrameCalls==frameCalls,"empty table rejects ordinal0 before calling native");
    }
    private static void PacketBounds()
    {
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,0,.5f,-1,16385})
        {Expect<ArgumentException>(()=>Frame(width:value),"invalid virtual width refused");Expect<ArgumentException>(()=>Frame(height:value),"invalid virtual height refused");}
        foreach(int axis in Enumerable.Range(0,4))foreach(float value in new[]{float.NaN,float.PositiveInfinity,float.NegativeInfinity,1048577,-1048577})
        {
            var vertices=Vertices();var vertex=vertices[0];if(axis==0)vertex.X=value;if(axis==1)vertex.Y=value;if(axis==2)vertex.U=value;if(axis==3)vertex.V=value;vertices[0]=vertex;
            Expect<ArgumentException>(()=>Frame(vertices:vertices),"finite position/UV coordinate domain enforced before wire publication");
        }
        var limits=Vertices();limits[0]=new(-1048576,1048576,0,255,0,255,-1048576,1048576);
        Check(Frame(vertices:limits).VertexCount==3,"coordinate domain exact signed boundaries admitted without silent clamp");
        foreach(int index in new[]{-1,3,int.MaxValue,int.MinValue})Expect<ArgumentException>(()=>Frame(indices:new[]{0,1,index}),"index must address packet's actual vertex table");
        foreach(var batches in new[]{new[]{new RmlUiCanvasBatch(64,0,3)},new[]{new RmlUiCanvasBatch(uint.MaxValue-1,0,3)},
            new[]{new RmlUiCanvasBatch(0,1,3)},new[]{new RmlUiCanvasBatch(0,0,0)},new[]{new RmlUiCanvasBatch(0,0,2)},
            new[]{new RmlUiCanvasBatch(0,0,6)},new[]{new RmlUiCanvasBatch(0,uint.MaxValue,uint.MaxValue)},new[]{new RmlUiCanvasBatch(0,0,3,1)}})
            Expect<ArgumentException>(()=>Frame(batches:batches),"closed texture ordinal, nonzero triangle batch, exact coverage and reserved field enforced");
        Expect<ArgumentException>(()=>Frame(indices:new[]{0,1}),"index count must be triangle multiple");
        Expect<ArgumentException>(()=>Frame(indices:Array.Empty<int>(),batches:Array.Empty<RmlUiCanvasBatch>()),"partial empty frame cannot impersonate clear");
        Expect<ArgumentException>(()=>Frame(vertices:new RmlUiCanvasVertex[65537]),"vertex count bound checked before private copy");
        Expect<ArgumentException>(()=>Frame(indices:new int[196609]),"index count bound checked before private copy");
        Expect<ArgumentException>(()=>Frame(batches:new RmlUiCanvasBatch[4097]),"batch count bound checked before private copy");
        var maxVertices=new RmlUiCanvasVertex[65536];
        Check(Frame(vertices:maxVertices,indices:new[]{65535,0,1}).VertexCount==65536,"exact65536 vertices and final valid signed index admitted");
        var maxIndices=new int[196608];var maxBatches=Enumerable.Range(0,4096).Select(i=>new RmlUiCanvasBatch(uint.MaxValue,(uint)(i*48),48)).ToArray();
        Check(Frame(vertices:new RmlUiCanvasVertex[1],indices:maxIndices,batches:maxBatches).IndexCount==196608,"exact196608 indices and4096 contiguous batches admitted");
        Expect<ArgumentNullException>(()=>new RmlUiCanvasTextureTable(null!),"table requires an inventory");
        foreach(var image in new[]{new RmlUiCanvasImage(0,1,new byte[4],0,0,0),new(513,1,new byte[4],0,0,0),new(1,0,new byte[4],0,0,0),
            new(1,513,new byte[4],0,0,0),new(1,1,new byte[3],0,0,0),new(1,1,new byte[5],0,0,0),new(1,1,new byte[4],2,0,0),
            new(1,1,new byte[4],0,2,0),new(1,1,new byte[4],0,0,2),null!})
            Expect<ArgumentException>(()=>new RmlUiCanvasTextureTable(new[]{image}),"image dimensions/exactRGBA/filter/wrap/null bounds enforced");
        var one=new RmlUiCanvasImage(1,1,new byte[4],0,0,0);
        Check(new RmlUiCanvasTextureTable(Enumerable.Repeat(one,64).ToArray()).Count==64,"exact64 images admitted");
        Expect<ArgumentException>(()=>new RmlUiCanvasTextureTable(Enumerable.Repeat(one,65).ToArray()),"sixtyfifth image refused");
        var large=new RmlUiCanvasImage(512,512,new byte[1048576],0,0,0);
        Check(new RmlUiCanvasTextureTable(Enumerable.Repeat(large,4).ToArray()).ByteLength==4194304,"exact4MiB and512square image bounds admitted");
        var five=Enumerable.Repeat(large,5).ToArray();
        Expect<ArgumentException>(()=>new RmlUiCanvasTextureTable(five),"aggregate5MiB rejected before packed allocation");
        long allocation=GC.GetAllocatedBytesForCurrentThread();Expect<ArgumentException>(()=>new RmlUiCanvasTextureTable(five),"warmed aggregate refusal deterministic");
        Check(GC.GetAllocatedBytesForCurrentThread()-allocation<1024*1024,"over-budget image plans do not allocate packed multi-megabyte pixel storage");
    }
    private static void CapabilityAndTokens()
    {
        var native=new FakeBridge();using var host=Host(native);
        Check(host.AcquireCanvas(host.HomeDocument,"canvas")==null&&native.CanvasAcquireCalls==0,"optional canvas version0 leaves ordinary host usable");
        native.CanvasAbiVersion=2;Check(host.AcquireCanvas(host.HomeDocument,"canvas")==null&&native.CanvasAcquireCalls==0,"unknown non1 canvas ABI cannot be activated");
        native.CanvasMissingVersion=true;Check(host.AcquireCanvas(host.HomeDocument,"canvas")==null,"missing optional capability entry falls back without retiring host");
        native.CanvasMissingVersion=false;native.CanvasAbiVersion=1;native.CanvasMissingAcquire=true;
        Check(host.AcquireCanvas(host.HomeDocument,"canvas")==null&&host.Active,"missing acquire entry leaves host active");native.CanvasMissingAcquire=false;
        native.CanvasRejectAcquire=true;Check(host.AcquireCanvas(host.HomeDocument,"canvas")==null,"native foreground/element refusal is preserved");native.CanvasRejectAcquire=false;
        foreach(string element in new[]{"",new string('x',257),new string('é',129),"canvas\0suffix"})
            Expect<ArgumentException>(()=>host.AcquireCanvas(host.HomeDocument,element),"element must be nonempty NULfree bounded UTF8");
        using(var utf8=host.AcquireCanvas(host.HomeDocument,new string('é',128)))Check(utf8!=null,"exact256 UTF8 bytes admitted without ASCII-only assumption");
        foreach(Func<RmlUiCanvasNativeLease,RmlUiCanvasNativeLease> filter in new Func<RmlUiCanvasNativeLease,RmlUiCanvasNativeLease>[]
        {p=>{p.Size=31;return p;},p=>{p.Version=0;return p;},p=>{p.Generation++;return p;},p=>{p.Document++;return p;},p=>{p.Epoch=0;return p;}})
        {native.CanvasFilterLease=filter;Expect<InvalidOperationException>(()=>host.AcquireCanvas(host.HomeDocument,"canvas"),"invalid returned native lease cannot create managed owner");}
        native.CanvasFilterLease=null;
        int calls=native.CanvasAcquireCalls;var modal=host.OpenDocument("invented-modal.rml",RmlUiDocumentLayer.Modal);
        Check(host.AcquireCanvas(host.HomeDocument,"canvas")==null&&native.CanvasAcquireCalls==calls,"background page blocked before native acquire");
        Check(host.ShowDocument(modal,false)&&host.AcquireCanvas(modal,"canvas")==null,"hidden document blocked before native acquire");
        Check(host.CloseDocument(modal)&&host.AcquireCanvas(modal,"canvas")==null,"closed token blocked before native acquire");
        Check(host.AcquireCanvas(new(host.HomeDocument.Generation+1,host.HomeDocument.DocumentId),"canvas")==null,"wrong generation token blocked");
        var legacyNative=new FakeBridge{Legacy=true,CanvasAbiVersion=1};using var legacy=Host(legacyNative);
        Check(legacy.AcquireCanvas(legacy.HomeDocument,"canvas")==null&&legacyNative.CanvasVersionCalls==0,"legacy protocol0 host never probes optional canvas capability");
    }
    private static void PublicationAndState()
    {
        var native=new FakeBridge{CanvasAbiVersion=1};using var host=Host(native);using var canvas=host.AcquireCanvas(host.HomeDocument,"canvas")!;
        var table=Table();var frame=Frame();
        Check(!canvas.Submit(frame)&&native.CanvasFrameCalls==0,"frame cannot publish before accepted immutable table");
        native.CanvasRejectTextures=true;Check(!canvas.SetTextures(table),"native texture rejection does not latch managed table state");
        native.CanvasRejectTextures=false;Check(canvas.SetTextures(table)&&!canvas.SetTextures(table)&&native.CanvasTextureCalls==2,"table retry allowed after refusal, accepted table publishes once only");
        int calls=native.CanvasFrameCalls;
        Check(!canvas.Submit(Frame(texture:1))&&native.CanvasFrameCalls==calls,"packet's legal ABI ordinal must also address this actual lease table before native call");
        Check(canvas.Submit(frame)&&native.CanvasLastHeader.Revision==1,"first native-accepted frame owns revision1");
        native.CanvasRejectFrames=true;Check(!canvas.Submit(frame)&&native.CanvasLastHeader.Revision==2,"failed candidate is attempted with next revision");
        Check(canvas.TryState(out var prior)&&prior.Revision==1,"native rejection preserves previous accepted managed revision");
        native.CanvasRejectFrames=false;Check(canvas.Submit(frame)&&native.CanvasLastHeader.Revision==2,"rejected frame never consumes a revision number");
        foreach(Func<RmlUiCanvasNativeState,RmlUiCanvasNativeState> filter in new Func<RmlUiCanvasNativeState,RmlUiCanvasNativeState>[]
        {p=>{p.Size=31;return p;},p=>{p.Version=2;return p;},p=>{p.Reserved=1;return p;},p=>{p.Flags=4;return p;},
            p=>{p.TextureCount=65;return p;},p=>{p.BatchCount=4097;return p;},p=>{p.Revision++;return p;},
            p=>{p.TextureCount=0;return p;},p=>{p.BatchCount=0;return p;},p=>{p.Flags=0;return p;}})
        {native.CanvasFilterState=filter;Check(!canvas.TryState(out _),"malformed/stale native state is not reported as current accepted frame");}
        native.CanvasFilterState=p=>{p.Flags=3;return p;};
        Check(canvas.TryState(out var failed)&&failed.Flags==3,"legitimate table-set plus texture-failure flag is accepted and reported without hiding native failure");
        native.CanvasFilterState=null;native.CanvasRejectState=true;Check(!canvas.TryState(out _),"native state refusal cannot create positive managed state");
        native.CanvasRejectState=false;Check(canvas.TryState(out var valid)&&valid is{Revision:2,TextureCount:1,BatchCount:1,Flags:2},"valid native state remains inspectable after malformed responses");
        var failures=new List<Exception?>();var worker=new Thread(()=>
        {foreach(Action action in new Action[]{()=>host.AcquireCanvas(host.HomeDocument,"other"),()=>canvas.SetTextures(table),()=>canvas.Submit(frame),()=>canvas.TryState(out _),()=>canvas.Dispose()})
            try{action();failures.Add(null);}catch(Exception error){failures.Add(error);}}){IsBackground=true};
        calls=native.CanvasFrameCalls;worker.Start();Check(worker.Join(TimeSpan.FromSeconds(5))&&failures.Count==5&&failures.All(e=>e is InvalidOperationException),"Host/canvas publication/state/disposal reject a foreign thread");
        Check(native.CanvasFrameCalls==calls&&canvas.TryState(out _),"wrong-thread refusal preserves owner/native packet state");
        var modal=host.OpenDocument("invented-modal.rml",RmlUiDocumentLayer.Modal);calls=native.CanvasFrameCalls;
        Check(!canvas.Submit(frame)&&canvas.TryState(out _)&&native.CanvasFrameCalls==calls,"foreground modal blocks publication while existing page state stays inspectable");
        host.ShowDocument(modal,false);Check(canvas.Submit(frame),"hiding modal restores exact existing page authority without new timeline/lease");
        host.ShowDocument(host.HomeDocument,false);calls=native.CanvasFrameCalls;Check(!canvas.Submit(frame)&&canvas.TryState(out _)&&native.CanvasFrameCalls==calls,"hidden page blocks publication but retains state inspection");
        int releases=native.CanvasReleaseCalls;canvas.Dispose();Check(native.CanvasReleaseCalls==releases+1,"hidden current owner can release native resources");
        canvas.Dispose();Check(native.CanvasReleaseCalls==releases+1&&!canvas.Submit(frame)&&!canvas.TryState(out _)&&!canvas.SetTextures(table),"disposed owner is inert and release idempotent");
    }
    private static void EpochAndLifetime()
    {
        var native=new FakeBridge{CanvasAbiVersion=1};using var host=Host(native);var token=host.HomeDocument;
        var old=host.AcquireCanvas(token,"canvas")!;Check(old.SetTextures(Table())&&old.Submit(Frame()),"old epoch begins with accepted content");
        using var replacement=host.AcquireCanvas(token,"canvas")!;
        Check(!old.Submit(Frame())&&!old.TryState(out _),"sameelement replacement fences old epoch even while document token remains alive");
        old.Dispose();Check(replacement.SetTextures(Table())&&replacement.Submit(Frame())&&replacement.TryState(out var fresh)&&fresh.Revision==1,
            "retired epoch release cannot destroy replacement or borrow its accepted revision");
        var page=host.OpenDocument("invented-next-page.rml",RmlUiDocumentLayer.Page);var pageCanvas=host.AcquireCanvas(page,"canvas")!;
        Check(pageCanvas.SetTextures(Table())&&pageCanvas.Submit(Frame()),"fresh page has its own canvas authority");
        Check(host.CloseDocument(page),"existing document owner retires page token");int releases=native.CanvasReleaseCalls,calls=native.CanvasFrameCalls;
        Check(!pageCanvas.Submit(Frame())&&!pageCanvas.TryState(out _),"route/document teardown prevents later frame/state publication");
        pageCanvas.Dispose();Check(native.CanvasReleaseCalls==releases&&native.CanvasFrameCalls==calls,"dead token disposal never issues native work against closed document");
        Check(host.Reinitialize(),"host can reinitialize through existing lifetime owner");calls=native.CanvasFrameCalls;releases=native.CanvasReleaseCalls;
        Check(!replacement.Submit(Frame())&&!replacement.TryState(out _)&&host.AcquireCanvas(token,"canvas")==null,"host generation replacement fences all old document/canvas tokens");
        replacement.Dispose();Check(native.CanvasFrameCalls==calls&&native.CanvasReleaseCalls==releases,"old generation disposal cannot touch fresh native context");
        using var current=host.AcquireCanvas(host.HomeDocument,"canvas")!;Check(current.SetTextures(Table())&&current.Submit(Frame()),"fresh host generation can use independent revision1");
        bool transitionBlocked=false;using var cancellation=host.DocumentCancellation(host.HomeDocument).Register(()=>
        {try{host.AcquireCanvas(host.HomeDocument,"canvas");}catch(InvalidOperationException){transitionBlocked=true;}});
        host.Shutdown();Check(transitionBlocked&&!host.Active&&!current.Submit(Frame()),"canvas acquire is fenced during cleanup callback lifetime transition and stale owner cannot revive host");
    }
    private static RmlUiHost Host(FakeBridge native){var host=new RmlUiHost(native);Check(host.Initialize(640,480,1,"."),"invented host initialized");return host;}
    private static RmlUiCanvasTextureTable Table()=>new(new[]{new RmlUiCanvasImage(1,1,new byte[]{200,100,50,128},0,1,0)});
    private static RmlUiCanvasVertex[] Vertices()=>new[]{new RmlUiCanvasVertex(0,0,200,100,50,128,-.5f,0),new(16,0,255,127,63,64,2,0),new(0,16,31,255,7,0,0,1)};
    private static RmlUiCanvasFrame Frame(float width=320,float height=240,RmlUiCanvasVertex[]? vertices=null,int[]? indices=null,RmlUiCanvasBatch[]? batches=null,uint texture=0)
        =>new(width,height,vertices??Vertices(),indices??new[]{0,1,2},batches??new[]{new RmlUiCanvasBatch(texture,0,3)});
    private static void Size<T>(int expected)where T:struct=>Check(Marshal.SizeOf<T>()==expected,"literal native size for "+typeof(T).Name);
    private static void Offset<T>(string field,int expected)where T:struct=>Check(Marshal.OffsetOf<T>(field).ToInt32()==expected,"literal native offset for "+typeof(T).Name+"."+field);
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Canvas owner: "+label);}
    private static void Expect<T>(Action action,string label)where T:Exception
    {checks++;try{action();}catch(T){return;}throw new InvalidOperationException("Canvas owner expected "+typeof(T).Name+": "+label);}
}

sealed partial class FakeBridge
{
    public uint CanvasAbiVersion;
    public bool CanvasMissingVersion,CanvasMissingAcquire,CanvasRejectAcquire,CanvasRejectTextures,CanvasRejectFrames,CanvasRejectState;
    public int CanvasVersionCalls,CanvasAcquireCalls,CanvasTextureCalls,CanvasFrameCalls,CanvasStateCalls,CanvasReleaseCalls;
    public Func<RmlUiCanvasNativeLease,RmlUiCanvasNativeLease>? CanvasFilterLease;
    public Func<RmlUiCanvasNativeState,RmlUiCanvasNativeState>? CanvasFilterState;
    private sealed class CanvasSlot
    {
        internal RmlUiCanvasNativeLease Lease;
        internal bool TableSet;
        internal uint Textures,Batches;
        internal ulong Revision;
    }
    private ulong _canvasEpoch;
    private readonly Dictionary<(ulong Document,string Element),ulong> _canvasCurrent=new();
    private readonly Dictionary<ulong,CanvasSlot> _canvasSlots=new();
    // Test-only copied receipts: no real consumer receives mutable wire arrays.
    private byte[] _canvasPixels=Array.Empty<byte>();
    private RmlUiCanvasNativeTexture[] _canvasTextures=Array.Empty<RmlUiCanvasNativeTexture>();
    private RmlUiCanvasVertex[] _canvasVertices=Array.Empty<RmlUiCanvasVertex>();
    private int[] _canvasIndices=Array.Empty<int>();
    private RmlUiCanvasBatch[] _canvasBatches=Array.Empty<RmlUiCanvasBatch>();
    public RmlUiCanvasNativeFrame CanvasLastHeader {get;private set;}
    public byte CanvasPixel(int index)=>_canvasPixels[index];
    public RmlUiCanvasNativeTexture CanvasTexture(int index)=>_canvasTextures[index];
    public bool CanvasFrameMatches(RmlUiCanvasVertex[] vertices,int[] indices,RmlUiCanvasBatch[] batches)
        =>_canvasVertices.SequenceEqual(vertices)&&_canvasIndices.SequenceEqual(indices)&&_canvasBatches.SequenceEqual(batches);
    public uint CanvasVersion(){CanvasVersionCalls++;if(CanvasMissingVersion)throw new EntryPointNotFoundException();return CanvasAbiVersion;}
    public int CanvasAcquire(ulong generation,ulong document,string element,ref RmlUiCanvasNativeLease lease)
    {
        CanvasAcquireCalls++;if(CanvasMissingAcquire)throw new EntryPointNotFoundException();
        if(CanvasRejectAcquire||generation!=_generation||!Documents.Contains(document))return 0;
        if(_canvasCurrent.TryGetValue((document,element),out ulong old))_canvasSlots.Remove(old);
        lease=new(){Size=32,Version=1,Generation=generation,Document=document,Epoch=++_canvasEpoch};
        _canvasCurrent[(document,element)]=lease.Epoch;_canvasSlots.Add(lease.Epoch,new(){Lease=lease});
        if(CanvasFilterLease is{} filter)lease=filter(lease);return 1;
    }
    private CanvasSlot? CanvasLive(RmlUiCanvasNativeLease lease)=>lease.Generation==_generation&&Documents.Contains(lease.Document)
        &&_canvasSlots.TryGetValue(lease.Epoch,out var slot)&&slot.Lease.Document==lease.Document?slot:null;
    public int CanvasTextures(ref RmlUiCanvasNativeLease lease,RmlUiCanvasNativeTexture[] textures,uint count,byte[] pixels,uint length)
    {
        CanvasTextureCalls++;_canvasTextures=textures.ToArray();_canvasPixels=pixels.ToArray();
        var slot=CanvasLive(lease);if(CanvasRejectTextures||slot==null||slot.TableSet)return 0;
        slot.TableSet=true;slot.Textures=count;return 1;
    }
    public int CanvasFrame(ref RmlUiCanvasNativeLease lease,ref RmlUiCanvasNativeFrame header,RmlUiCanvasVertex[] vertices,int[] indices,RmlUiCanvasBatch[] batches)
    {
        CanvasFrameCalls++;CanvasLastHeader=header;_canvasVertices=vertices.ToArray();_canvasIndices=indices.ToArray();_canvasBatches=batches.ToArray();
        var slot=CanvasLive(lease);if(CanvasRejectFrames||slot==null||!slot.TableSet||header.Revision<=slot.Revision)return 0;
        slot.Revision=header.Revision;slot.Batches=header.Batches;return 1;
    }
    public int CanvasState(ref RmlUiCanvasNativeLease lease,ref RmlUiCanvasNativeState state)
    {
        CanvasStateCalls++;var slot=CanvasLive(lease);if(CanvasRejectState||slot==null)return 0;
        state=new(){Size=32,Version=1,Revision=slot.Revision,TextureCount=slot.Textures,BatchCount=slot.Batches,Flags=slot.TableSet?2u:0u};
        if(CanvasFilterState is{} filter)state=filter(state);return 1;
    }
    public int CanvasRelease(ref RmlUiCanvasNativeLease lease)
    {
        CanvasReleaseCalls++;if(lease.Generation!=_generation)return 0;
        if(_canvasSlots.TryGetValue(lease.Epoch,out var slot)&&slot.Lease.Document!=lease.Document)return 0;
        _canvasSlots.Remove(lease.Epoch);return 1;
    }
}
