using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>Actual native build panel, typed pointer actions and bounded scrolling against a controlled build authority.</summary>
internal static class StudioBuildGlAcceptance
{
    private const string Scope = "Actual NativeStudioEditorPage RmlUi build panel, native pointer routing, field review, busy/cancel/late-result presentation, diagnostics/budget paging and final GL framebuffer. Synthetic held session/build authority only; no compiler, retained geometry renderer, package writes, game assets, playtest, audit child, network, legacy handoff or physical Cocoa input qualification.";
    internal static void Run(RmlUiHost host, int width, int height)
    {
        string output = Path.GetFullPath(Environment.GetEnvironmentVariable("PROJECT_PRIME_RMLUI_STUDIO_BUILD_OUTPUT")
            ?? Path.Combine("artifacts", "rmlui-studio-build", width + "x" + height));
        Directory.CreateDirectory(output);
        using var owner = new Session(); using var pages = new RmlUiPageManager(host);
        using var page = new NativeStudioEditorPage(host, pages, owner);
        int checks = 0; var captures = new List<string>();
        host.Input.SetFramebufferScale(1, 1); page.Open();
        try
        {
            Settle(); Activate("native_build_toggle", 223);
            Check(Has("BUILD & TEST") && Has("NO BUILD RESULT"), "native Build & Test opens actual authored panel");
            Activate("native_build_validate", 200); Guid canceled = owner.Work.Operation;
            Check(owner.Begins == 1 && owner.Work.Busy && Has("BUSY"), "native Validate starts held work and displays busy state");
            foreach (string id in new[] { "native_build_validate", "native_build_package", "native_build_destination", "native_editor_move", "native_editor_save" })
                Check(!host.FocusDocument(page.Document, id), "busy native control cannot receive focus: " + id);
            Check(owner.Edits == 0 && !page.LegacyRequested, "busy build does not mutate project or hand off to legacy");
            Capture("held-busy"); Activate("native_build_cancel", 207);
            Check(owner.Cancels == 1 && !owner.Work.Busy && Has("CANCELED"), "actual Cancel retires held validation");
            Check(!owner.Finish(canceled), "late completion is refused by retired synthetic owner operation"); Settle();
            Check(!Has("SYNTHETIC PROBLEM 0") && owner.Publications == 0, "late canceled result cannot populate native report or publish bytes");
            Activate("native_build_validate", 200);
            Check(owner.Finish(owner.Work.Operation), "live held operation can finish with controlled diagnostics"); Settle();
            Check(Has("SYNTHETIC PROBLEM 0") && Has("FP-TEST-000") && Has("12 TOTAL") && Has("RESULT MATCHES CURRENT PROJECT"),
                "native report presents actual severity code message and immutable result identity");
            Activate("native_build_problem0", 212);
            Check(owner.ProblemSelections == 1 && owner.Selected == owner.ObjectId, "native diagnostic selects exact result object identity");
            Activate("native_build_problems_next", 209);
            Check(Has("SYNTHETIC PROBLEM 8") && Has("PROBLEMS 2 / 2"), "native next problems shows bounded second page");
            Activate("native_build_problems_previous", 208);
            Check(Has("PROBLEMS 1 / 2"), "native previous problems restores first page");
            Activate("native_build_budgets_next", 211);
            Check(Has("SYNTHETIC BUDGET 8") && Has("BUDGETS 2 / 2"), "native next budgets presents second bounded page");
            Activate("native_build_budgets_previous", 210);
            Check(Has("BUDGETS 1 / 2"), "native previous budgets restores first page"); Capture("report-pages");
            owner.ChangeState(); Settle();
            Check(Has("PROJECT CHANGED") && !host.FocusDocument(page.Document,"native_build_problem0"),
                "stale result remains readable but cannot retarget an object click");
            Activate("native_build_clear", 220); Check(Has("NO BUILD RESULT"), "native Clear removes stale report");
            string destination = Path.Combine(output, "é exact reviewed package.ppmap");
            Reach("native_build_destination"); host.SetField(page.Document,"native_build_destination",destination);
            Check(host.ReadField(page.Document,"native_build_destination",32767)==destination,"native package input preserves exact Unicode path");
            Activate("native_build_package",202);
            Check(owner.Work.Stage==NativeStudioBuildStage.ReadyPackage&&owner.Work.Destination==destination&&owner.Publications==0
                &&Has(destination)&&Has("CONFIRM REVIEWED PACKAGE EXPORT"),"package button reviews exact destination before publication");
            Check(!host.FocusDocument(page.Document,"native_build_destination"),"reviewed package path cannot silently change before confirmation"); Capture("package-reviewed");
            Activate("native_build_package",202);
            Check(owner.Publications==1&&owner.ConfirmedDestination==destination&&Has("PACKAGE EXPORTED"),"same native package button explicitly confirms reviewed operation once");
            Activate("native_build_clear",220);
            Activate("native_build_toggle",223); Check(!host.FocusDocument(page.Document,"native_build_validate"),"native toggle collapses build panel");
            Activate("native_build_toggle",223); Check(Has("NO BUILD RESULT"),"native toggle reopens usable cleared panel"); Capture("cleared");
            Check(owner.Edits==0&&!page.LegacyRequested&&GL.GetError()==ErrorCode.NoError,"build panel uses native route without edits legacy handoff or GL error");
            Activate("native_editor_back",32); Check(page.Closed&&owner.Closes==1&&!page.LegacyRequested,"native Back releases fixture without advanced-tools handoff");
            Save(true,null);
        }
        catch(Exception error){try{Capture("failure");}catch{}Save(false,error.ToString());throw;}

        bool Has(string value)=>NativeSemanticReadback.Read(host,page.Document).ContainsText(value);
        void Check(bool condition,string label){if(!condition)throw new InvalidOperationException(label);checks++;Console.WriteLine("STUDIO BUILD GL PASS "+label);}
        void Frame()
        {
            page.Refresh();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.Viewport(0,0,width,height);
            GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.025f,.05f,.08f,1);
            GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit|ClearBufferMask.StencilBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);
        }
        void Settle(int milliseconds=150){var timer=Stopwatch.StartNew();do{Frame();Thread.Sleep(5);}while(timer.ElapsedMilliseconds<milliseconds);}
        (float X,float Y,float W,float H) Reach(string id)
        {
            Frame();Check(host.FocusDocument(page.Document,id),"native focus reaches "+id);Settle();
            for(int i=0;i<180;i++)
            {
                bool valid=host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h);
                bool toolbar=id is "native_build_toggle" or "native_editor_back";
                string scroll=toolbar?"native_editor_tools":"native_editor_left";
                Check(host.TryGetElementBounds(page.Document,scroll,out float sx,out float sy,out float sw,out float sh)&&sw>0&&sh>0,"native scroll container exists");
                if(valid&&w>0&&h>0&&x>=0&&y>=0&&x+w<=width+1&&y+h<=height+1&&x>=sx-1&&y>=sy-1&&x+w<=sx+sw+1&&y+h<=sy+sh+1)return(x,y,w,h);
                host.Input.PointerMoved(sx+sw/2,sy+sh/2);host.Input.PointerWheel(i==0?10000:-1);Settle(15);
            }
            var diagnostic = new[] { id, "native_editor_tools", "native_editor_left", "native_editor_back" }.Select(key =>
            {
                bool visible = host.TryGetElementBounds(page.Document,key,out float x,out float y,out float w,out float h);
                return new { id=key,visible,x,y,width=w,height=h };
            }).ToArray();
            File.WriteAllText(Path.Combine(output,"unreachable-"+id+".json"),JsonSerializer.Serialize(diagnostic,new JsonSerializerOptions{WriteIndented=true}));
            throw new InvalidOperationException("Native build target cannot be scrolled into framebuffer and its container: "+id);
        }
        void Activate(string id,int argument)
        {
            var b=Reach(id);host.DiscardIntents();host.Input.PointerMoved(b.X+b.W/2,b.Y+b.H/2);
            host.Input.PointerButton(0,b.X+b.W/2,b.Y+b.H/2,true);host.Input.PointerButton(0,b.X+b.W/2,b.Y+b.H/2,false);host.Update();
            Check(host.TryTakeIntent(out var intent)&&intent.Kind==RmlUiIntentKind.StudioAction&&intent.Argument==argument,"native pointer emits exact build action "+argument);
            Check(page.Handle(intent)&&!page.Handle(intent)&&!host.TryTakeIntent(out _),"native page consumes one action without replay");if(!page.Closed)Settle();
        }
        void Capture(string name)
        {
            Frame();string path=Path.Combine(output,"studio-build-"+name+".png");Check(ScreenCapture.SaveWindow(width,height,path),"native screenshot "+name);
            FinalCompositeCapture.WriteEvidence(path,width,height,Scope);File.WriteAllText(path+".semantic.json",NativeSemanticReadback.Read(host,page.Document).RawJson);captures.Add(path);
        }
        void Save(bool passed,string? error)=>File.WriteAllText(Path.Combine(output,"studio-build-summary.json"),JsonSerializer.Serialize(
            new{passed,checks,width,height,scope=Scope,owner.Begins,owner.Cancels,owner.Publications,owner.ProblemSelections,captures,error},new JsonSerializerOptions{WriteIndented=true}));
    }
    private sealed class Session:INativeStudioEditorSession,INativeStudioEditorBuild
    {
        internal readonly Guid ObjectId=Guid.NewGuid();private readonly Guid _lease=Guid.NewGuid();private long _revision=1;private ulong _state=1;private bool _disposed;
        private readonly IReadOnlyList<NativeStudioObjectRow> _rows;
        internal NativeStudioBuildSnapshot Work;internal Guid Selected;internal int Begins,Cancels,Publications,ProblemSelections,Edits,Closes;internal string ConfirmedDestination="";
        private NativeStudioEditTicket Ticket=>new(_lease,_state,_revision,Selected);
        internal Session(){_rows=Array.AsReadOnly(new[]{new NativeStudioObjectRow(ObjectId,"SYNTHETIC OBJECT",false)});Work=Empty("BUILD READY");}
        private NativeStudioBuildSnapshot Empty(string status)=>new(_revision,Guid.Empty,Ticket,NativeStudioBuildKind.Validate,NativeStudioBuildStage.Idle,status,"","",Array.Empty<MapDiagnostic>(),Array.Empty<MapBudget>(),false);
        public NativeStudioEditorSnapshot Capture()=>new(_revision,Ticket,!_disposed,"SYNTHETIC BUILD PROJECT","",false,false,false,Selected==Guid.Empty?"NO OBJECT SELECTED":"SYNTHETIC OBJECT",_rows,"SYNTHETIC BUILD OWNER",Work.Busy);
        public NativeStudioResult Select(NativeStudioEditTicket ticket,Guid objectId){if(Work.Busy||ticket!=Ticket||objectId!=ObjectId)return new(false,"STALE");Selected=objectId;_revision++;return new(true,"SELECTED");}
        public NativeStudioResult Execute(NativeStudioEditTicket ticket,NativeStudioEditorOperation operation,NativeStudioEditorValues values){Edits++;return new(false,"UNEXPECTED EDIT");}
        public NativeStudioResult TryClose(){Closes++;_disposed=true;return new(true,"CLOSED");}
        public void Dispose()=>_disposed=true;
        public NativeStudioBuildSnapshot CaptureBuild()=>Work;
        public NativeStudioResult BeginBuild(NativeStudioEditTicket ticket,NativeStudioBuildKind kind,string destination="")
        {
            if(_disposed||ticket!=Ticket||Work.Busy)return new(false,"STALE OR BUSY");Begins++;_revision++;
            Work=new(_revision,Guid.NewGuid(),Ticket,kind,kind==NativeStudioBuildKind.Package?NativeStudioBuildStage.ReadyPackage:NativeStudioBuildStage.Running,
                kind==NativeStudioBuildKind.Package?"PACKAGE REVIEW READY":"VALIDATION HELD",destination,"synthetic-fingerprint",Array.Empty<MapDiagnostic>(),Array.Empty<MapBudget>(),true);
            return new(true,Work.Status);
        }
        internal bool Finish(Guid operation)
        {
            if(Work.Operation!=operation||Work.Stage!=NativeStudioBuildStage.Running)return false;_revision++;
            Work=Work with{Revision=_revision,Stage=NativeStudioBuildStage.Complete,Status="BUILD BLOCKED // CONTROLLED ERRORS AND WARNINGS",
                Diagnostics=Array.AsReadOnly(Enumerable.Range(0,12).Select(i=>new MapDiagnostic("FP-TEST-"+i.ToString("000"),i%2==0?MapDiagnosticSeverity.Error:MapDiagnosticSeverity.Warning,"SYNTHETIC PROBLEM "+i,ObjectId)).ToArray()),
                Budgets=Array.AsReadOnly(Enumerable.Range(0,12).Select(i=>new MapBudget("SYNTHETIC BUDGET "+i,i+10,100)).ToArray())};return true;
        }
        public void CancelBuild(Guid operation){if(operation!=Work.Operation||!Work.Busy)return;Cancels++;_revision++;Work=Work with{Revision=_revision,Stage=NativeStudioBuildStage.Complete,Status="VALIDATION CANCELED",ResultCurrent=false};}
        public void ClearBuild(Guid operation){if(operation!=Work.Operation||Work.Busy)return;_revision++;Work=Empty("BUILD RESULT CLEARED");}
        public NativeStudioResult ConfirmBuildPackage(Guid operation)
        {
            if(operation!=Work.Operation||Work.Stage!=NativeStudioBuildStage.ReadyPackage||!Work.ResultCurrent)return new(false,"STALE REVIEW");
            Publications++;ConfirmedDestination=Work.Destination;_revision++;Work=Work with{Revision=_revision,Stage=NativeStudioBuildStage.Complete,Status="PACKAGE EXPORTED // SYNTHETIC PUBLICATION"};return new(true,Work.Status);
        }
        public NativeStudioResult SelectBuildProblem(Guid operation,int absoluteIndex)
        {
            if(operation!=Work.Operation||!Work.ResultCurrent||Work.Busy||absoluteIndex<0||absoluteIndex>=Work.Diagnostics.Count)return new(false,"STALE RESULT");
            Selected=Work.Diagnostics[absoluteIndex].ObjectId??Guid.Empty;ProblemSelections++;_revision++;return new(true,"PROBLEM OBJECT SELECTED");
        }
        internal void ChangeState(){_state++;_revision++;Work=Work with{Revision=_revision,ResultCurrent=false};}
    }
}
