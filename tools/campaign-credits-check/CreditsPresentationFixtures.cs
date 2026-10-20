using System.Text;
using MphRead;
using MphRead.Mods.Campaign;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Adventure;
using Catalog=MphRead.Mods.Campaign.CampaignCreditsCatalog;
using Source=MphRead.Mods.Campaign.CampaignCreditsSource;
using Phase=MphRead.Mods.Launcher.RmlUi.Pages.Adventure.CampaignCreditsPresentation.Phase;
using Shared=CreditsFrameFixtures;

// Actual managed page/session owners, invented immutable catalogs, held jobs and
// a private native seam. No configured game paths, worker IO, native DLL or audio.
internal static class CreditsPresentationFixtures
{
    private static int checks;
    internal static void Run()
    {
        checks=0;
        var stationary=MakeCatalog(false);var navigating=MakeCatalog(true);
        OffAndCapability();LoadingAndCancellation(stationary);CurrentFailures(stationary);
        IdentityRetirement(stationary);ForegroundClock(stationary);AudioOwnership(stationary);Results(navigating);
        Console.WriteLine($"CREDITS PRESENTATION PASS {checks} invented lifecycle/page checks; no original media, native library, GL, saves or sound device.");
    }
    private static void OffAndCapability()
    {
        using(var h=new Harness(original:false))
        {
            Check(h.LoadCalls==0&&h.Native.AcquireCalls==0&&h.Page.CreditsPhase==null,"default OFF starts neither source job nor canvas");
            Check(h.Native.Text(h.Page.Document,"campaign_ending_totals").Contains("3h 07m",StringComparison.Ordinal),"OFF retains immutable receipt timing in ordinary report");
            h.Tick(1);Check(h.LoadCalls==0&&h.Native.AcquireCalls==0,"refresh cannot activate OFF source rendering");
            Check(h.Page.Back()&&h.Page.Closed&&h.Page.TryTakeReturn(out var receipt)&&receipt==h.Summary
                &&!h.Page.TryTakeReturn(out _)&&!h.Page.Back(),"OFF report returns exact captured receipt once");
        }
        using(var h=new Harness(canvasVersion:0))
            Check(h.LoadCalls==0&&h.Native.AcquireCalls==0&&h.Page.CreditsPhase==null,"optional canvas ABI absent refuses source job before loading");
    }
    private static void LoadingAndCancellation(Catalog catalog)
    {
        using(var h=new Harness())
        {
            Check(h.LoadCalls==1&&h.Page.CreditsPhase==Phase.Loading&&h.Native.AcquireCalls==0&&h.Audio.Starts==0,"held loading job starts once with no canvas or fallback/results audio");
            h.Tick(100);Check(h.Page.CreditsPhase==Phase.Loading&&h.LoadCalls==1,"arbitrary refresh time does not complete held worker");
            Check(h.Page.Back()&&h.Page.Closed&&h.Job.DisposeCount==1,"cancel during loading closes controller and retires worker without waiting");
            h.Job.Succeed(catalog);h.Tick(101);Check(h.Native.AcquireCalls==0&&h.Audio.Starts==0,"late success after cancel cannot adopt canvas or start audio");
            Check(h.Page.TryTakeReturn(out var receipt)&&receipt==h.Summary&&!h.Page.TryTakeReturn(out _),"loading cancellation carries exact receipt once");
        }
        foreach(bool fault in new[]{false,true})
        {
            using var h=new Harness();var document=h.Page.Document;h.Page.Dispose();
            if(fault)h.Job.Fail(new IOException("invented late fault"));else h.Job.Succeed(catalog);
            h.Tick(1);Check(h.Job.DisposeCount==1&&!h.Host.IsAlive(document)&&h.Native.AcquireCalls==0&&h.Audio.Starts==0,
                "page disposal fences late success/fault without publishing to retired document");
            Check(!h.Page.TryTakeReturn(out _),"disposing a loading page invents no return intent");
        }
        using(var h=new Harness(load:()=>null))
            Check(h.Page.CreditsPhase==Phase.Fallback&&h.Native.AcquireCalls==0,"unavailable current source uses ordinary fallback");
        using(var h=new Harness(load:()=>throw new InvalidDataException("invented admission refusal")))
            Check(h.Page.CreditsPhase==Phase.Fallback&&h.Native.AcquireCalls==0,"recoverable loader startup refusal uses ordinary fallback");
    }
    private static void CurrentFailures(Catalog catalog)
    {
        foreach(bool cancellation in new[]{false,true})
        {
            using var h=new Harness();if(cancellation)h.Job.Cancel();else h.Job.Fail(new InvalidDataException("invented invalid source"));h.Tick(1);
            Check(h.Page.CreditsPhase==Phase.Fallback&&h.Job.DisposeCount==1&&h.Native.AcquireCalls==0,"current canceled/faulted task falls back and releases source owner");
            Check(h.Audio.Starts==1&&h.Host.FocusedElement()=="campaign_ending_continue","genuine failure keeps results audio/report and focuses ordinary return");
            Check(h.Page.Back()&&h.Page.TryTakeReturn(out var receipt)&&receipt==h.Summary,"fallback report remains returnable");
        }
        foreach(int rejection in new[]{0,1,2})
        {
            using var h=new Harness();h.Native.RejectAcquire=rejection==0;h.Native.RejectTable=rejection==1;h.Native.RejectFrame=rejection==2;
            h.Job.Succeed(catalog);h.Tick(1);
            Check(h.Page.CreditsPhase==Phase.Fallback&&h.Job.DisposeCount==1&&!h.Page.CreditsCanAcknowledge,
                "acquire/table/first-frame rejection atomically abandons source session for report fallback");
            Check(h.Native.Bool(h.Page.Document,"visible:campaign_ending_source")==0
                &&h.Native.Bool(h.Page.Document,"visible:campaign_ending_scroll")==1,"failed adoption hides source wrapper and restores report");
            int attempts=h.Native.FrameCalls;h.Tick(2);Check(h.Native.FrameCalls==attempts&&h.LoadCalls==1,"fallback neither retries worker nor submits later source frames");
        }
        foreach(bool nativeFailure in new[]{false,true})
        {
            using var h=new Harness();h.Job.Succeed(catalog);h.Tick(1);Check(h.Page.CreditsPhase==Phase.Credits,"invented source adopts before steady failure test");
            if(nativeFailure)h.Native.TextureFailure=true;else h.Native.RejectState=true;
            h.Tick(2);Check(h.Page.CreditsPhase==Phase.Fallback&&h.Native.ReleaseCalls==1&&h.Job.DisposeCount==1,"native state refusal or explicit texture failure releases adopted lease and falls back");
        }
        using(var h=new Harness())
        {
            h.Job.Succeed(catalog);h.Tick(1);h.Native.RejectFrame=true;h.Tick(1.05);
            Check(h.Page.CreditsPhase==Phase.Fallback&&h.Native.ReleaseCalls==1,"later frame refusal releases existing source presentation");
        }
        var ambiguous=Shared.Make();var pages=ambiguous.Document.Pages.Select(p=>p.Index is 49 or 50
            ?p with{Items=p.Items.Select((item,i)=>i==4?item with{Actions=new[]{Acknowledgement(),Acknowledgement()}}:item).ToArray()}:p).ToArray();
        ambiguous=Shared.Replace(ambiguous,pages:pages);
        using(var h=new Harness())
        {h.Job.Succeed(Shared.Create(ambiguous));h.Tick(1);Check(h.Page.CreditsPhase==Phase.Fallback&&h.Native.AcquireCalls==0,"ambiguous results return action refuses before any canvas acquisition");}
    }
    private static void IdentityRetirement(Catalog catalog)
    {
        foreach(bool adopted in new[]{false,true})foreach(int fence in new[]{0,1,2,3})
        {
            using var h=new Harness();if(adopted){h.Job.Succeed(catalog);h.Tick(1);}
            int frames=h.Native.FrameCalls,starts=h.Audio.Starts;
            if(fence==0)h.Job.SelectionCurrent=false;
            if(fence==1)h.ReceiptCurrent=false;
            if(fence==2)h.Pages.OpenPage(new("replacement","invented-replacement.rml"));
            if(fence==3)Check(h.Host.Reinitialize(),"host generation replacement succeeds in invented bridge");
            h.Tick(2);if(!adopted)h.Job.Succeed(catalog);h.Tick(3);
            Check(h.Page.CreditsPhase==Phase.Retired&&h.Job.DisposeCount==1&&h.Native.FrameCalls==frames,"source/receipt/document/generation change retires both held and adopted session without late publication");
            Check(h.Audio.Starts==starts,"identity retirement cannot start fallback/results audio");
            Check(!h.Page.CreditsCanAcknowledge&&!h.Page.TryTakeReturn(out _),"identity retirement invents no acknowledgement or controller return");
            if(adopted)Check(h.Audio.Stops==1,"adopted session retirement releases only its owned audio epoch");
        }
    }
    private static void ForegroundClock(Catalog catalog)
    {
        using(var inactive=new Harness(ready:catalog,active:false))
        {
            Check(inactive.Page.CreditsPhase==Phase.Loading&&inactive.Native.AcquireCalls==0,"Open(false) cannot adopt an already completed source job");
            inactive.Tick(1);Check(inactive.Page.CreditsPhase==Phase.Credits&&inactive.Native.FrameCalls==1,"first active refresh may adopt completed source job");
        }
        using var h=new Harness();h.Job.Succeed(catalog);
        h.Pages.SetVisible(false);h.Tick(10);Check(h.Page.CreditsPhase==Phase.Loading&&h.Native.AcquireCalls==0,"completed worker cannot adopt into hidden page");
        h.Pages.SetVisible(true);var modal=h.Pages.OpenModal(new("modal","invented-modal.rml","modal_focus"));
        h.Tick(20);Check(h.Page.CreditsPhase==Phase.Loading&&h.Native.AcquireCalls==0,"completed worker cannot adopt under owned modal");
        h.Pages.CloseModal();h.Tick(30,false);Check(h.Page.CreditsPhase==Phase.Loading,"inactive application cannot adopt completed worker");
        h.Tick(31);Check(h.Page.CreditsPhase==Phase.Credits&&h.Native.FrameCalls==1,"foreground active adoption publishes one initial frame");
        foreach(int pause in new[]{0,1,2,3})
        {
            int before=h.Native.FrameCalls;double start=h.Time;RmlUiDocumentToken external=default;
            if(pause==0)h.Pages.SetVisible(false);
            if(pause==1)h.Pages.OpenModal(new("modal","invented-modal.rml","modal_focus"));
            if(pause==2)external=h.Host.OpenDocument("invented-external-modal.rml",RmlUiDocumentLayer.Modal);
            h.Tick(start+10,pause!=3);h.Tick(start+20,pause!=3);
            Check(h.Native.FrameCalls==before&&h.Page.CreditsPage==6,"hidden/modal/external-modal/inactive clock emits no source ticks or frames");
            if(pause==0)h.Pages.SetVisible(true);if(pause==1)h.Pages.CloseModal();if(pause==2)h.Host.CloseDocument(external);
            h.Tick(start+21);Check(h.Native.FrameCalls==before,"foreground restoration establishes new clock baseline without catch-up");
            h.Tick(start+21.05);Check(h.Native.FrameCalls==before+1,"subsequent foreground clock step resumes source publication");
        }
        int frames=h.Native.FrameCalls;h.Tick(h.Time);Check(h.Native.FrameCalls==frames,"same monotonic time cannot submit another source frame");
        h.Tick(h.Time-1);Check(h.Native.FrameCalls==frames,"clock regression resets baseline rather than running backward/catching up");
        int states=h.Native.StateCalls;h.Page.Suspend();
        Check(h.Native.FrameCalls==frames&&h.Native.StateCalls==states,"Suspend resets only local clock without native state/publication calls");
        h.Tick(h.Time+10000);Check(h.Native.FrameCalls==frames,"first refresh after suspended refresh loop does not accrue hidden clock gap");
        h.Tick(h.Time+.05);Check(h.Native.FrameCalls==frames+1,"next eligible clock interval resumes after explicit Suspend");
        _=modal;
    }
    private static void AudioOwnership(Catalog catalog)
    {
        using(var h=new Harness(ready:catalog,failAudio:true))
        {
            Check(h.Page.CreditsPhase==Phase.Credits&&h.Audio.Starts==1&&h.Audio.Cues==1,"failed sink start is attempted once on entering credits phase");
            for(int i=0;i<8;i++)h.Tick(h.Time+.05);
            Check(h.Audio.Starts==1&&h.Audio.Cues==1&&h.Audio.Stops==0,"failed audio start neither retries source IO/start nor repeats cues on refresh");
        }
        using(var h=new Harness(ready:catalog))
        {
            Check(h.Audio.Starts==1,"invented audio initially acquires one credits epoch");h.Audio.Stolen=true;
            for(int i=0;i<8;i++)h.Tick(h.Time+.05);
            Check(h.Audio.Starts==1&&h.Audio.Cues==1&&h.Audio.Stops==0,"stolen epoch is not reclaimed or stopped while source phase stays unchanged");
            h.Page.Dispose();Check(h.Audio.Stops==0,"retirement cannot stop another owner's stolen music epoch");
        }
    }
    private static void Results(Catalog catalog)
    {
        foreach(bool good in new[]{true,false})
        {
            using var h=new Harness(good:good);h.Job.Succeed(catalog);h.Tick(1);
            Check(!h.Page.CreditsCanAcknowledge&&!h.Page.Handle(new(RmlUiIntentKind.ResultsAction,0,h.Page.Document,1)),"credits pages cannot acknowledge results early");
            for(int i=0;i<20&&!h.Page.CreditsCanAcknowledge;i++)h.Tick(h.Time+.05);
            Check(h.Page.CreditsPhase==Phase.Results&&h.Page.CreditsPage==(good?49:50)&&h.Page.CreditsCanAcknowledge,"original callback12 selects captured good/bad results and exposes admitted return action");
            Check(h.Host.FocusedElement()=="campaign_ending_source_ok","settled results focus explicit source acknowledgement");
            var external=h.Host.OpenDocument("invented-external-modal.rml",RmlUiDocumentLayer.Modal);
            Check(!h.Page.Back()&&!h.Page.Handle(new(RmlUiIntentKind.ResultsAction,0,h.Page.Document,2)),"external modal fences both page Back and result acknowledgement");
            h.Host.CloseDocument(external);h.Pages.SetVisible(false);
            Check(!h.Page.Back()&&!h.Page.Handle(new(RmlUiIntentKind.ResultsAction,0,h.Page.Document,3)),"hidden page cannot return or acknowledge");
            h.Pages.SetVisible(true);var wrong=new RmlUiDocumentToken(h.Page.Document.Generation,h.Page.Document.DocumentId+1000);
            Check(!h.Page.Handle(new(RmlUiIntentKind.ResultsAction,0,wrong,4)),"different document cannot consume source acknowledgement");
            var accepted=new RmlUiIntent(RmlUiIntentKind.ResultsAction,0,h.Page.Document,5);
            Check(h.Page.Handle(accepted)&&h.Page.Closed&&h.Job.DisposeCount==1&&h.Native.ReleaseCalls==1,"source206 acknowledgement closes page controller and releases session once");
            Check(!h.Page.Handle(accepted)&&!h.Page.Back()&&h.Page.TryTakeReturn(out var receipt)&&receipt==h.Summary
                &&!h.Page.TryTakeReturn(out _),"duplicate result intent/back cannot duplicate captured receipt return");
            int frames=h.Native.FrameCalls,starts=h.Audio.Starts;h.Tick(h.Time+10);
            Check(h.Native.FrameCalls==frames&&h.Audio.Starts==starts&&h.Audio.Stops==h.Audio.Starts,"returned page cannot resume source frames/audio and stops each owned track once");
        }
        using(var h=new Harness())
        {
            var intent=new RmlUiIntent(RmlUiIntentKind.Navigate,(int)RmlUiRouteArgument.Adventure,h.Page.Document,1);
            Check(h.Page.Handle(intent)&&h.Page.Closed&&h.Job.DisposeCount==1&&h.Page.TryTakeReturn(out _),"explicit cancel route uses same loading lifetime owner as Back");
        }
    }
    private static Source.Action Acknowledgement()=>new(1,0,0,0,0,0,0,0,new[]{new Source.Call(0,206)},65535,255,0);
    private static Catalog MakeCatalog(bool navigate)
    {
        var f=Shared.Make(navigate:navigate,animate:false);
        var pages=f.Document.Pages.Select(p=>p.Index is 49 or 50?p with
        {Items=p.Items.Select((item,i)=>i==4?item with{Actions=new[]{Acknowledgement()}}:item).ToArray()}:p).ToArray();
        return Shared.Create(Shared.Replace(f,pages:pages));
    }
    private sealed class HeldJob:ICampaignCreditsLoadJob
    {
        private readonly TaskCompletionSource<Catalog> source=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SelectionCurrent=true;internal int DisposeCount;
        public Task<Catalog> Completion=>source.Task;public bool Current=>SelectionCurrent&&DisposeCount==0;
        internal void Succeed(Catalog catalog)=>source.TrySetResult(catalog);
        internal void Fail(Exception error){source.TrySetException(error);_=source.Task.Exception;}
        internal void Cancel()=>source.TrySetCanceled();
        public void Dispose(){if(DisposeCount==0)DisposeCount++;}
    }
    private sealed class Audio:ICampaignAudioSink
    {
        internal int Starts,Stops,Cues;internal bool FailStarts,Stolen;private long epoch;private SeqId sequence;
        public long Start(SeqId value){Starts++;if(FailStarts)return 0;sequence=value;return ++epoch;}
        public bool Owns(SeqId value,long owned)=>!Stolen&&sequence==value&&owned==epoch;
        public void Tick(){}public void Stop(SeqId value,long owned){Check(Owns(value,owned),"audio stop targets its own sequence epoch");Stops++;sequence=SeqId.None;}
        public void Cue(MphRead.Mods.Sound.UiFeedbackCue cue){Cues++;}
    }
    private sealed class Harness:IDisposable
    {
        internal readonly Bridge Native=new();internal readonly Audio Audio=new();internal readonly HeldJob Job=new();
        internal readonly RmlUiHost Host;internal readonly RmlUiPageManager Pages;internal readonly NativeCampaignEndingPage Page;
        internal readonly CampaignEndingSummary Summary;internal bool ReceiptCurrent=true;internal int LoadCalls;internal double Time;
        internal Harness(bool original=true,uint canvasVersion=1,bool good=true,Func<ICampaignCreditsLoadJob?>? load=null,
            Catalog? ready=null,bool active=true,bool failAudio=false)
        {
            Audio.FailStarts=failAudio;if(ready!=null)Job.Succeed(ready);
            Native.Version=canvasVersion;Host=new(Native);Check(Host.Initialize(640,480,1,".",RmlUiRenderBackend.DrawList),"invented native seam bootstraps actual managed Host");
            Pages=new(Host);Summary=Shared.Summary(good);Page=new(Host,Pages,Summary,Audio,original,
                ()=>{LoadCalls++;return load==null?Job:load();},()=>ReceiptCurrent,()=>Time);Page.Open(active);
        }
        internal void Tick(double now,bool active=true){Time=now;Page.Refresh(active);}
        public void Dispose(){Page.Dispose();Pages.Dispose();Host.Dispose();}
    }
    private sealed class Bridge:IRmlUiNativeBridge
    {
        private readonly HashSet<ulong> documents=new();private readonly Dictionary<(ulong,string),string> texts=new();
        private readonly Dictionary<(ulong,string),int> bools=new();private ulong generation,nextDocument,epoch,canvasDocument;
        private uint textureCount,batchCount;private ulong revision;private string focused="";
        internal uint Version=1;internal bool RejectAcquire,RejectTable,RejectFrame,RejectState,TextureFailure;
        internal int AcquireCalls,TableCalls,FrameCalls,ReleaseCalls,StateCalls;
        internal string Text(RmlUiDocumentToken d,string name)=>texts.GetValueOrDefault((d.DocumentId,name),"");
        internal int Bool(RmlUiDocumentToken d,string name)=>bools.GetValueOrDefault((d.DocumentId,name),-1);
        public uint ProtocolVersion()=>1;public ulong Generation()=>generation;public ulong HomeDocument()=>1;
        public int Initialize(int w,int h,float density,string root)=>InitializeBackend(w,h,density,root,1);
        public int InitializeBackend(int w,int h,float density,string root,int backend){generation++;nextDocument=1;documents.Add(1);return 1;}
        public void Shutdown(){documents.Clear();canvasDocument=0;}public void Resize(int w,int h,float d){}public void Update(){}public void Render(int w,int h){}
        public ulong OpenDocument(string path,int layer){ulong id=++nextDocument;documents.Add(id);return id;}
        public int CloseDocument(ulong d){if(canvasDocument==d)canvasDocument=0;return documents.Remove(d)?1:0;}
        public int ShowDocument(ulong d,int show)=>documents.Contains(d)?1:0;
        public int FocusDocument(ulong d,string element){if(!documents.Contains(d))return 0;focused=element;return 1;}
        public uint CanvasVersion()=>Version;
        public int CanvasAcquire(ulong g,ulong d,string element,ref RmlUiCanvasNativeLease lease)
        {AcquireCalls++;if(RejectAcquire||g!=generation||!documents.Contains(d))return 0;canvasDocument=d;epoch++;revision=0;textureCount=batchCount=0;
            lease=new(){Size=32,Version=1,Generation=g,Document=d,Epoch=epoch};return 1;}
        private bool Current(RmlUiCanvasNativeLease lease)=>lease.Generation==generation&&lease.Document==canvasDocument&&lease.Epoch==epoch&&canvasDocument!=0;
        public int CanvasTextures(ref RmlUiCanvasNativeLease lease,RmlUiCanvasNativeTexture[] textures,uint count,byte[] pixels,uint length)
        {TableCalls++;if(RejectTable||!Current(lease))return 0;Check(count==textures.Length&&length==pixels.Length,"session publishes exact private texture table extent");textureCount=count;return 1;}
        public int CanvasFrame(ref RmlUiCanvasNativeLease lease,ref RmlUiCanvasNativeFrame header,RmlUiCanvasVertex[] vertices,int[] indices,RmlUiCanvasBatch[] batches)
        {FrameCalls++;if(RejectFrame||!Current(lease))return 0;Check(header.Vertices==vertices.Length&&header.Indices==indices.Length&&header.Batches==batches.Length,"session publishes exact immutable frame extent");revision=header.Revision;batchCount=header.Batches;return 1;}
        public int CanvasState(ref RmlUiCanvasNativeLease lease,ref RmlUiCanvasNativeState state)
        {StateCalls++;if(RejectState||!Current(lease))return 0;state=new(){Size=32,Version=1,Revision=revision,TextureCount=textureCount,BatchCount=batchCount,Flags=TextureFailure?3u:2u};return 1;}
        public int CanvasRelease(ref RmlUiCanvasNativeLease lease){if(!Current(lease))return 0;ReleaseCalls++;canvasDocument=0;return 1;}
        public int DocumentSetText(ulong d,string name,string value){texts[(d,name)]=value;return documents.Contains(d)?1:0;}
        public int DocumentSetBool(ulong d,string name,int value){bools[(d,name)]=value;return documents.Contains(d)?1:0;}
        public int DocumentSetField(ulong d,string name,string value)=>DocumentSetText(d,name,value);
        public int DocumentReadField(ulong d,string id,byte[] buffer,int capacity)=>0;
        public void SetText(string n,string v){}public void SetBool(string n,int v){}public void SetField(string n,string v){}public int ReadField(string id,byte[] b,int c)=>0;
        public int TakeIntent(ref RmlUiNativeIntent packet)=>0;public int TakeAction(byte[] b,int c)=>0;public void SetLobbyAnchor(int s,float x,float y){}
        public int Back()=>0;public int MouseMove(int x,int y,int m)=>0;public int MouseButton(int b,int d,int m)=>0;public int MouseWheel(float d,int m)=>0;
        public int Key(int k,int d,int m)=>0;public int Text(uint codepoint)=>0;public void FocusLost(){}public int TextInputActive()=>0;
        public int FocusedElement(byte[] b,int c){byte[] value=Encoding.UTF8.GetBytes(focused);int length=Math.Min(value.Length,Math.Min(c,b.Length)-1);if(length<0)return 0;value.AsSpan(0,length).CopyTo(b);b[length]=0;return length;}
        public void SetClipboard(string text){}public int ReadClipboard(byte[] b,int c)=>0;public int TextInputState(ref RmlUiNativeTextInputState state)=>0;
        public int Composition(ulong g,ulong d,ulong e,int s,string text,int c,int l)=>0;public int HoveredElement(byte[] b,int c)=>0;
    }
    private static void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException("Presentation fixture: "+label);}
}
