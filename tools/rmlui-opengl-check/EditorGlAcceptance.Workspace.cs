using System;
using System.Collections.Immutable;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MphRead.Mods.Launcher.Core;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Theatre;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.Network;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

internal static partial class EditorGlAcceptance
{
    internal static void RunWorkspace(RmlUiHost host,int width,int height,float density=1)
    {
        const string scope="Actual native studio-workspace document, production editor/camera/tools/timeline/export controllers and injected memory boundaries. No recording, replay world, simulation, encoder, OS picker, shared audio device, Shell focus, or physical input. Native pointer and wheel events are supplied directly to RmlUiHost.";
        string output=Path.GetFullPath(Path.Combine("artifacts","rml98-persistent-workspace",$"{width}x{height}"));Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output,"README.txt"),scope+"\n");
        Check(!DemoPlayback.IsActive,"persistent workspace fixture has no replay scene");
        var editor=new MemoryEditor();var camera=new WorkspaceCamera(editor.Context);var exports=new WorkspaceExports(editor.Context);
        using var transport=new TheatrePlaybackController(new NoStudio());using var pages=new RmlUiPageManager(host);
        using var page=new TheatrePlaybackPagePresenter(host,pages,transport,camera:camera,exports:exports,editor:editor);
        host.Input.SetFramebufferScale(1,1);page.Open();var document=page.Document;var metrics=new TimelineMetrics(width,height,width,height,density);
        var captures=new List<object>();TimelineRenderTicket? presented=null;string timelineDiagnostic="";int timelineDiagnosticCount=0;Settle();
        Check(pages.PageKey=="studio-workspace"&&pages.Top==document&&pages.ModalCount==0,"actual persistent workspace owns one native page without an editor modal");
        Layout("initial");Capture("camera");
        foreach(string id in new[]{"studio_camera_x","studio_camera_y","studio_camera_z","studio_camera_yaw","studio_camera_pitch","studio_camera_roll","studio_camera_fov"})
            {Check(host.FocusDocument(document,id),"numeric field can scroll into view: "+id);WaitVisible(id,"studio_inspector_scroll");Check(Bounds(id).Inside(Bounds("studio_inspector_scroll")),"actual numeric camera field is reachable inside the inspector: "+id);}
        Check(host.FocusDocument(document,"studio_camera_x"),"native numeric camera draft receives focus");host.SetField(document,"studio_camera_x","12.5");
        Click("studio_camera_apply","workspace-camera-apply");
        Check(camera.Applies==1&&camera.Applied?.X==12.5f&&camera.Writes==0&&camera.Capture().Keys.Length==3,"native numeric Apply parses the draft into preview owner without saving authored keys");
        Click("studio_refresh","workspace-refresh");
        Click("studio_camera_overview","workspace-camera-overview");Check(camera.Overviews==1&&camera.Writes==0,"native Overview reaches reviewed optional owner without persistence");
        foreach(string tab in new[]{"markers","combat","analysis","audio","export","camera"})
        {
            Click("studio_tab_"+tab,"workspace-tab-"+tab);Layout(tab);
            Check(Bounds("studio_inspector_"+tab).Width>0,"selected inspector has actual native geometry: "+tab);
            Capture(tab);
        }
        Click("studio_tab_audio","workspace-tab-audio");
        foreach(var operation in Enum.GetValues<TheatreStudioTool>())
        {
            string name=operation.ToString();Click("studio_operation_"+name,"workspace-operation-"+name);
            Check(Read<TheatreStudioTool>(page,"_studioTool")==operation,"explicit native audio/comparison/bundle tool choice: "+name);Layout("tool "+name);
        }
        Click("studio_tab_export","workspace-tab-export");
        Click("studio_export_fps_144","workspace-export-fps-144");
        Check(Read<TheatreExportController>(page,"_exports").Snapshot().Options.Fps==144,"native explicit export rate reaches existing export controller");
        Click("studio_export_resolution_3","workspace-export-resolution-3");
        Check(Read<TheatreExportController>(page,"_exports").Snapshot().Options.Resolution==3,"native explicit export resolution reaches existing export controller");
        Layout("explicit export choices");Capture("export-explicit");
        Click("studio_tab_camera","workspace-tab-camera");var fixedTransport=Bounds("studio_transport");var fixedTimeline=Bounds("studio_timeline");var fixedTabs=Bounds("studio_tabs");
        Scroll("studio_inspector_scroll",-1000);Layout("scrolled inspector");
        Check(Bounds("studio_transport")==fixedTransport&&Bounds("studio_timeline")==fixedTimeline&&Bounds("studio_tabs")==fixedTabs,"wheel scroll moves only inspector content; transport timeline and tabs remain fixed");Capture("inspector-scrolled");
        Scroll("studio_inspector_scroll",1000);Settle();
        // The timeline is independently scrollable: lower tracks remain bounded
        // and only actually visible marker boxes enter the successful-picture ticket.
        var surface=Bounds("replay_timeline_surface");var cameraTrack=Bounds("replay_timeline_camera");var lowerBefore=Bounds("replay_timeline_exportrange");
        Check(surface.Inside(Bounds("studio_timeline"))&&surface.Height>8,"timeline has an actual bounded vertical scroll surface");
        Scroll("replay_timeline_surface",-1000);Settle();var lowerAfter=Bounds("replay_timeline_exportrange");
        Check(lowerAfter.Y<=lowerBefore.Y&&lowerAfter.Y+lowerAfter.Height<=surface.Y+surface.Height+2&&lowerAfter.Y>=surface.Y-2,"native timeline vertical scroll exposes the final export-range track without covering transport");
        File.WriteAllText(Path.Combine(output,"timeline-ticket.json"),JsonSerializer.Serialize(new{Surface=surface,Presented=presented},new JsonSerializerOptions{WriteIndented=true,IncludeFields=true}));
        Check(presented!=null&&presented.Clip.Inside(surface)&&presented.Lanes.All(row=>row.Box.Inside(presented.Clip))&&presented.Lanes.Any(row=>row.Id.StartsWith("replay_timeline_exportrange_",StringComparison.Ordinal)),"actual presented timeline ticket authorizes only markers inside its visible scroll clip");
        if(lowerBefore.Y+lowerBefore.Height>surface.Y+surface.Height+2)
            Check(lowerAfter.Y<lowerBefore.Y&&Bounds("replay_timeline_camera").Y<cameraTrack.Y,"overflowing compact timeline actually scrolls its track stack");
        Layout("scrolled timeline");Capture("timeline-lower-tracks");Scroll("replay_timeline_surface",1000);
        Click("studio_settings","workspace-settings");Check(page.TryTakeStudioSettingsRequest()&&!page.TryTakeStudioSettingsRequest()&&pages.Top==document,"always-visible native Settings emits one workspace request without retiring owners");
        Layout("settings request");
        Check(editor.Commits==0&&camera.Writes==0&&exports.Enqueues==0&&!DemoPlayback.IsActive,"synthetic workspace interaction wrote no recording metadata and opened no replay");
        Check(GL.GetError()==ErrorCode.NoError,"persistent workspace leaves no GL error");
        File.WriteAllText(Path.Combine(output,"captures.json"),JsonSerializer.Serialize(new{Scope=scope,Width=width,Height=height,Captures=captures},new JsonSerializerOptions{WriteIndented=true}));
        page.Close();Check(!page.IsOpen&&!host.IsAlive(document),"persistent native document tears down as a single owner");

        void Check(bool value,string message)
        {
            if(!value)
            {
                string failure=Path.Combine(output,"first-failure.ppm");var pixels=FinalCompositeCapture.Read(width,height);
                using(var stream=File.Create(failure)){stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC WORKSPACE FAILURE - NO REPLAY WORLD\n{width} {height}\n255\n"));for(int row=height-1;row>=0;row--)stream.Write(pixels,row*width*3,width*3);}
                FinalCompositeCapture.WriteEvidence(failure,width,height,scope);File.WriteAllText(Path.Combine(output,"first-failure.txt"),message+"\n");
                throw new InvalidOperationException(message);
            }
            Console.WriteLine("RML WORKSPACE GL PASS "+message);
        }
        void Frame()
        {
            page.Present();host.Update();var ticket=page.PrepareTimeline(metrics,true);var curves=page.PrepareCameraCurves(metrics,true);
            if(ticket==null&&timelineDiagnosticCount<20)
            {
                const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                object? Field(object owner,string name)=>owner.GetType().GetField(name,flags)?.GetValue(owner);
                var timeline=Field(page,"_timeline") as TheatreTimelineViewState;
                var tools=Field(page,"_cameraTools") as TheatreCameraToolsController;var snapshot=tools?.Snapshot();
                var expected=snapshot?.Context.Camera.Keys.Select(key=>key.Frame).ToArray();
                var actual=Enumerable.Range(0,3).Select(i=>
                {
                    bool found=host.TryGetElementBounds(document,"replay_timeline_key_"+i,out float x,out float y,out float w,out float h);
                    return new{Index=i,Found=found,Box=new TimelineBox(x,y,w,h)};
                }).ToArray();
                host.TryGetElementBounds(document,"replay_timeline_camera",out float cx,out float cy,out float cw,out float ch);
                var diagnostic=JsonSerializer.Serialize(new{Notice=timeline?.Notice,Current=snapshot?.Current,Error=snapshot?.Error,
                    SameReview=timeline!=null&&snapshot!=null&&ReferenceEquals(Field(timeline,"_review"),snapshot.Context),
                    InputDocument=host.CurrentInputDocument,ExpectedDocument=document,Focused=host.FocusedElement(),
                    CameraLane=new TimelineBox(cx,cy,cw,ch),ExpectedKeys=expected,ActualKeys=actual},
                    new JsonSerializerOptions{IncludeFields=true});
                if(diagnostic!=timelineDiagnostic){timelineDiagnostic=diagnostic;timelineDiagnosticCount++;Console.WriteLine("RML WORKSPACE TIMELINE NULL "+diagnostic);}
            }
            GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);
            GL.ClearColor(.12f,.18f,.24f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);
            RmlUiOpenGlFrame.Draw(host,width,height);
            if(ticket!=null&&page.TimelinePresented(ticket,metrics,true))presented=ticket;
            if(curves!=null)page.CameraCurvesPresented(curves,metrics,true);
        }
        void Settle(){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(10);}while(timer.ElapsedMilliseconds<160);}
        void WaitVisible(string id,string parent)
        {
            var timer=Stopwatch.StartNew();TimelineBox? previous=null;int stable=0;
            do
            {
                Frame();Thread.Sleep(10);
                if(host.TryGetElementBounds(document,id,out float x,out float y,out float w,out float h)
                    &&host.TryGetElementBounds(document,parent,out float px,out float py,out float pw,out float ph))
                {
                    var current=new TimelineBox(x,y,w,h);var pane=new TimelineBox(px,py,pw,ph);
                    stable=current.Inside(pane)&&previous==current?stable+1:0;previous=current;
                    if(stable>=3)return;
                }
            }while(timer.ElapsedMilliseconds<2000);
            Check(false,"focused native field settles fully inside its scroll region: "+id);
        }
        TimelineBox Bounds(string id)
        {
            Check(host.TryGetElementBounds(document,id,out float x,out float y,out float w,out float h)&&w>0&&h>0,"actual persistent native bounds: "+id);
            return new(x,y,w,h);
        }
        void Layout(string stage)
        {
            var screen=new TimelineBox(0,0,width,height);var top=Bounds("studio_transport");var timeline=Bounds("studio_timeline");var viewport=Bounds("replay_viewport");var inspector=Bounds("studio_inspector");var tabs=Bounds("studio_tabs");
            foreach(var box in new[]{top,timeline,viewport,inspector,tabs})Check(box.Inside(screen),"persistent region stays on screen: "+stage);
            Check(viewport.Width>=80&&viewport.Height>=48&&inspector.Width>=120&&inspector.Height>=48&&top.Y+top.Height<=viewport.Y+2
                &&viewport.X+viewport.Width<=inspector.X+2&&viewport.Y+viewport.Height<=timeline.Y+2,"transport viewport inspector and timeline coexist without overlap: "+stage);
            foreach(string id in new[]{"studio_settings","replay_back","studio_open","studio_save_as"})Check(Bounds(id).Inside(top),"persistent navigation remains reachable: "+id+" // "+stage);
            foreach(string tab in new[]{"camera","markers","combat","analysis","audio","export"})Check(Bounds("studio_tab_"+tab).Inside(tabs),"inspector tab remains reachable: "+tab+" // "+stage);
            Check(pages.Top==document&&pages.ModalCount==0,"tab and scroll stay in the same native workspace document: "+stage);
        }
        void Click(string id,string action)
        {
            Frame();Check(host.FocusDocument(document,id),"native workspace control can focus: "+id);Settle();var box=Bounds(id);var screen=new TimelineBox(0,0,width,height);
            Check(box.Inside(screen),"focused native control is on screen before pointer activation: "+id);
            Check(RmlUiIntentRegistry.TryParseLegacy("replay:"+action,document,1,out var expected),"expected action exists in the real native registry: "+action);
            host.DiscardIntents();host.Input.PointerButton(0,box.X+box.Width/2,box.Y+box.Height/2,true);host.Input.PointerButton(0,box.X+box.Width/2,box.Y+box.Height/2,false);host.Update();bool received=false;
            while(host.TryTakeIntent(out var intent))
            {
                bool accepted=page.HandleIntent(intent);
                if(intent.Kind!=expected.Kind||intent.Argument!=expected.Argument)continue;
                Check(accepted&&intent.Document==document,"native pointer emits current typed workspace action: "+id);
                Check(!page.HandleIntent(intent),"duplicate native workspace sequence is rejected: "+id);received=true;
            }
            Check(received,"expected actual native pointer action arrived: "+id);Settle();
        }
        void Scroll(string id,double delta)
        {var box=Bounds(id);host.Input.PointerMoved(box.X+box.Width/2,box.Y+box.Height/2);host.Input.PointerWheel(delta,RmlUiInputModifiers.Alt);Settle();}
        void Capture(string name)
        {
            Settle();string path=Path.Combine(output,name+".ppm");var pixels=FinalCompositeCapture.Read(width,height);
            using(var stream=File.Create(path)){stream.Write(Encoding.ASCII.GetBytes($"P6\n# SYNTHETIC NATIVE WORKSPACE - NO REPLAY WORLD\n{width} {height}\n255\n"));for(int row=height-1;row>=0;row--)stream.Write(pixels,row*width*3,width*3);}
            FinalCompositeCapture.WriteEvidence(path,width,height,scope);captures.Add(new{Name=name,Path=path,Transport=Bounds("studio_transport"),Timeline=Bounds("studio_timeline"),Viewport=Bounds("replay_viewport"),Inspector=Bounds("studio_inspector")});
        }
    }
    private static T Read<T>(object owner,string field)=>(T)(owner.GetType().GetField(field,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(owner)??throw new InvalidOperationException("Fixture field missing: "+field));
    private sealed partial class MemoryEditor:ITheatreSpatialBackend,ITheatreStudioToolsBackend,ITheatreWorkspaceCombatBackend
    {
        public Task<TheatreSpatialAnalysis> AnalyzeSpatial(TheatreEditorContext context,CancellationToken cancellation)=>Task.FromResult(new TheatreSpatialAnalysis(600,ImmutableArray.Create(new TheatreSpatialSample(10,0,1,2,3,TheatreSpatialKind.Movement,1))));
        public void ValidateSpatial(TheatreEditorContext context){if(!Current)throw new TheatreEditorStaleException("Synthetic owner retired.");}
        public Task<TheatreStudioToolResult> RunStudioTool(TheatreEditorContext context,TheatreStudioToolOptions options,CancellationToken cancellation)=>throw new InvalidOperationException("No file or audio export in native layout fixture.");
        public void ValidateStudioTool(TheatreEditorContext context)=>ValidateSpatial(context);
        public void PreviewStudioAudio(TheatreEditorContext context,string wavePath)=>throw new InvalidOperationException("No device in native layout fixture.");
        public void StopStudioAudioPreview(){}
        public TheatreCombatReview ReadCombat(TheatreEditorContext context)=>new(context,ImmutableArray.Create(new TheatreCombatChoice("SYNTHETIC SHOT P1 → P2","No authority world in layout fixture",true)),0,true,new object());
        public void SelectCombat(TheatreCombatReview review,int index){}
        public void ShowCombat(TheatreCombatReview review,bool shooterView){}
    }
    private sealed class WorkspaceCamera:ITheatreCameraBackend,ITheatreCameraToolsBackend,ITheatreWorkspaceCameraBackend,ITheatreWorkspaceOverviewBackend
    {
        private TheatreCameraToolsContext _context;internal int Applies,Overviews,Writes;internal TheatreWorkspaceCameraValues? Applied;private TheatreWorkspacePreview _preview=new(true,false,false);
        internal WorkspaceCamera(TheatreEditorContext owner)
        {
            var pose=new TheatreCameraPose(1,2,3,0,0,0,1,80,0,-1,0,0);
            var camera=new TheatreCameraContext(owner.SceneGeneration,owner.Transport,new object(),owner.SeekGeneration,owner.LogicalPath,owner.PlaybackPath,"SOURCE","CAMERA",owner.Frame,owner.Duration,true,pose,
                ImmutableArray.Create(new TheatreCameraKey(10,pose),new TheatreCameraKey(20,pose),new TheatreCameraKey(40,pose)),false){PresentationRevision=1};
            _context=new(camera,owner.Owner,owner.SourceRevision,owner.AnnotationRevision,ImmutableArray.Create(new TheatreCameraMarker("event:0",10,TheatreCameraMarkerKind.Event)),ImmutableArray.Create(0,1),new(false,true,false),new object());
        }
        public TheatreCameraContext Capture()=>_context.Camera;
        public bool IsCurrent(TheatreCameraContext context)=>ReferenceEquals(context,_context.Camera);
        public void SetFreeCamera(TheatreCameraContext context){}
        public void AdjustLens(TheatreCameraContext context,float fov,float roll){}
        public void Preview(TheatreCameraContext context,TheatreCameraKey key){}
        public void ToggleTrack(TheatreCameraContext context){}
        public void Commit(TheatreCameraEdit edit){Writes++;throw new InvalidOperationException("Native layout fixture forbids persistence.");}
        public TheatreCameraToolsContext CaptureTools()=>_context;
        public bool ToolsCurrent(TheatreCameraToolsContext context)=>ReferenceEquals(context,_context);
        public void ValidateReviewedTools(TheatreCameraToolsContext context){if(!ToolsCurrent(context))throw new TheatreCameraStaleException("Synthetic camera owner changed.");}
        public TheatreCameraToolsResult CommitTools(TheatreCameraToolsEdit edit){Writes++;throw new InvalidOperationException("Native layout fixture forbids persistence.");}
        public void PreviewTools(TheatreCameraToolsContext context,TheatreCameraKey focused){}
        public void ChangeMotion(TheatreCameraToolsContext context,TheatreCameraMotionChange change){}
        public TheatreWorkspaceCameraValues ReadDirectCamera(TheatreCameraContext context)=>new(context.Pose.X,context.Pose.Y,context.Pose.Z,0,0,context.Pose.Roll,context.Pose.Fov);
        public TheatreWorkspacePreview ReadPreview(TheatreCameraContext context)=>_preview;
        public void ApplyDirectCamera(TheatreCameraContext context,TheatreWorkspaceCameraValues values)
        {if(!IsCurrent(context))throw new TheatreCameraStaleException("Synthetic picture changed.");Applies++;Applied=values;_context=_context with{Camera=context with{Pose=context.Pose with{X=values.X,Y=values.Y,Z=values.Z,Fov=values.Fov},PresentationRevision=context.PresentationRevision+1}};}
        public void SetPreview(TheatreCameraContext context,TheatreWorkspacePreview preview)=>_preview=preview;
        public void SetOverview(TheatreCameraContext context){if(!IsCurrent(context))throw new TheatreCameraStaleException("Synthetic picture changed.");Overviews++;}
    }
    private sealed class WorkspaceExports:ITheatreExportBackend
    {
        private readonly TheatreExportContext _context;internal int Enqueues;
        internal WorkspaceExports(TheatreEditorContext owner)=>_context=new(owner.SceneGeneration,owner.Owner,owner.LogicalPath,owner.PlaybackPath,owner.SourceRevision,"CAMERA",owner.AnnotationRevision,owner.ReelRevision,owner.Duration,owner.MarkIn,owner.MarkOut,0,new object());
        public TheatreExportContext Capture()=>_context;
        public bool IsCurrent(TheatreExportContext context)=>ReferenceEquals(context,_context);
        public ImmutableArray<TheatreExportPreset> Presets=>ImmutableArray.Create(new TheatreExportPreset("Balanced",1,60,true,false));
        public ImmutableArray<string> Encoders()=>ImmutableArray.Create("libx264");
        public TheatreExportQueueState Queue()=>new(0,ImmutableArray<TheatreExportJob>.Empty,null,null,"Idle","SYNTHETIC - NO ENCODER",0,ImmutableArray<string>.Empty,"");
        public void Enqueue(TheatreExportContext reviewed,TheatreExportOptions options){Enqueues++;throw new InvalidOperationException("No encoder in native layout fixture.");}
        public bool CancelExact(TheatreExportQueueState reviewed)=>false;
        public bool RetryExact(TheatreExportQueueState reviewed)=>false;
        public bool ClearExact(TheatreExportQueueState reviewed)=>false;
        public void OpenOutput(string exactOutputPath)=>throw new InvalidOperationException("No filesystem output in native layout fixture.");
    }
}
