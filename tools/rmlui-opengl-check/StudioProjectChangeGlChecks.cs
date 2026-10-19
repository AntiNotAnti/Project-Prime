using System.Diagnostics;
using System.Reflection;
using MphRead.Mods;
using MphRead.Mods.Render;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using OpenTK.Graphics.OpenGL;

/// <summary>Native modal decisions against the real retained owner; synthetic private files, caller's sole GL context.</summary>
internal static class StudioProjectChangeGlChecks
{
    internal static void Run(RmlUiHost host, RmlUiPageManager pages, int width, int height, string root, Action<bool,string> check)
    {
        using var owner = new MapStudioScreen(openLibrary: false);
        var backend = new Changes(owner);
        pages.OpenPage(new("studio-change-fixture", "pages/studio/native-entry.rml", "native_studio_back"));
        var spec = new NativeStudioChangeSpec(NativeStudioChangeKind.Create, TemplateId: "basic-ffa", Name: "NATIVE_COLD_CREATE");
        check(owner.BeginNativeChange(spec).Success, "native modal starts cold creation in one cached retained owner");
        using (var modal = Open())
        {
            Wait(modal, NativeStudioChangeStage.Ready);
            check(modal.TryTakeCommittedEditor(out var session) && session != null && !modal.TryTakeCommittedEditor(out _),
                "native cold Create consumes the committed production editor lease exactly once");
            modal.Dispose();
            using var editor = new NativeStudioEditorPage(host, pages, session!); editor.Open(); EditorFrames(editor);
            check(Field<MapDocument>(owner, "_document").Project.Definition.Name == spec.Name && editor.Back(),
                "cold native template opens actual viewport chrome and returns through existing recovery owner");
        }
        var original = Field<MapDocument>(owner, "_document");
        var originalHistory = original.History;
        string originalBytes = original.Project.Definition.Serialize();
        Background();
        check(owner.BeginNativeChange(spec with { Name = "CANCELLED_CREATE" }).Success, "dirty native New stages without retiring old owner");
        using (var modal = Open())
        {
            Wait(modal, NativeStudioChangeStage.NeedsDecision); Capture(modal, "project-decision");
            Action(modal, "native_change_cancel", 57);
            check(!modal.IsOpen && ReferenceEquals(original, Field<MapDocument>(owner,"_document"))
                && ReferenceEquals(originalHistory, original.History) && original.Project.Definition.Serialize() == originalBytes,
                "actual native Cancel preserves exact dirty document/history and closes only its modal");
        }
        string source = Path.Combine(root, "native-open-source.json");
        File.WriteAllText(source, MapTemplates.Create("NATIVE_OPEN_SOURCE", "basic-ffa").Definition.Serialize());
        string saved = Path.Combine(root, "native-save-before-open.json");
        check(owner.BeginNativeChange(new(NativeStudioChangeKind.Open, source)).Success, "native Open stages a private actual JSON project");
        using (var modal = Open())
        {
            Wait(modal, NativeStudioChangeStage.NeedsDecision);
            host.SetField(modal.Document, "native_change_path", saved); Action(modal,"native_change_save",55);
            Wait(modal,NativeStudioChangeStage.Ready);
            check(File.Exists(saved) && MapDefinition.Load(saved).MapId == original.Project.Definition.MapId
                && modal.TryTakeCommittedEditor(out var session) && session != null,
                "actual native Save and Continue saves old bytes before returning staged Open lease");
            // C# definite assignment is independent of the assertion callback.
            check(backend.LastTaken != null, "production Open committed session was captured by owner adapter");
            using var opened = backend.LastTaken!;
            check(opened is INativeStudioEditorObjects objects && objects.CreateObject(opened.Capture().Ticket, NativeStudioCreateKind.Box).Success
                && opened.TryClose().Success, "opened native project uses retained object command/history before next transition");
        }
        var retained = Field<MapDocument>(owner,"_document");
        string retainedBytes = retained.Project.Definition.Serialize();
        string overwrite = Path.Combine(root,"native-reviewed-overwrite.json"); File.WriteAllText(overwrite,"REVIEWED BY NATIVE MODAL");
        check(owner.BeginNativeChange(spec with { Name="OVERWRITE_CANDIDATE" }).Success,"native dirty transition prepares overwrite guard fixture");
        using(var modal=Open())
        {
            Wait(modal,NativeStudioChangeStage.NeedsDecision);
            host.SetField(modal.Document,"native_change_path",overwrite);Action(modal,"native_change_save",55);
            Wait(modal,NativeStudioChangeStage.NeedsOverwrite);Frames(modal);
            check(host.FocusedElement()=="native_change_overwrite" && NativeSemanticReadback.Read(host,modal.Document).ContainsText(overwrite),
                "reviewed native overwrite path is presented and confirmation takes keyboard focus");
            Capture(modal,"project-overwrite");
            File.WriteAllText(overwrite,"CHANGED AFTER NATIVE REVIEW");Action(modal,"native_change_overwrite",58);
            check(owner.CaptureNativeChange()?.Stage==NativeStudioChangeStage.NeedsDecision && File.ReadAllText(overwrite)=="CHANGED AFTER NATIVE REVIEW"
                && ReferenceEquals(retained,Field<MapDocument>(owner,"_document")) && retained.Project.Definition.Serialize()==retainedBytes,
                "actual native overwrite refuses changed target without writing or retiring dirty owner");
            Action(modal,"native_change_save",55);Wait(modal,NativeStudioChangeStage.NeedsOverwrite);
            Action(modal,"native_change_keep_file",59);Wait(modal,NativeStudioChangeStage.NeedsDecision);
            check(File.ReadAllText(overwrite)=="CHANGED AFTER NATIVE REVIEW","native Keep Existing File preserves reviewed file bytes");
            Action(modal,"native_change_cancel",57);
        }
        check(owner.BeginNativeChange(spec with {Name="PRESERVED_NATIVE_NEW"}).Success,"native New begins final preservation decision");
        using(var modal=Open())
        {
            Wait(modal,NativeStudioChangeStage.NeedsDecision);Action(modal,"native_change_preserve",56);Wait(modal,NativeStudioChangeStage.Ready);
            check(MapDocument.ReadRecovery(retained.RecoveryPath(CustomRooms.UserMapDirectory)).Definition.Serialize()==retainedBytes
                && modal.TryTakeCommittedEditor(out _),"actual native Keep Recovery preserves latest exact retained bytes before New");
            modal.Dispose();using var finalEditor=new NativeStudioEditorPage(host,pages,backend.LastTaken!);finalEditor.Open();EditorFrames(finalEditor);
            check(Field<MapDocument>(owner,"_document").Project.Definition.Name=="PRESERVED_NATIVE_NEW" && finalEditor.Back(),
                "preserved New returns native editor viewport with one current retained document owner");
        }
        check(File.Exists(retained.RecoveryPath(CustomRooms.UserMapDirectory)) && GL.GetError()==ErrorCode.NoError,"native project modal/editor transitions retain recovery evidence and leave no GL error");

        NativeStudioProjectChangePage Open(){var modal=new NativeStudioProjectChangePage(host,pages,backend);check(modal.Open(),"real native project change modal opens");Frames(modal);return modal;}
        void Background()=>pages.OpenPage(new("studio-change-fixture","pages/studio/native-entry.rml","native_studio_back"));
        void Frame(NativeStudioProjectChangePage modal){modal.Refresh();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.02f,.04f,.06f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);RmlUiOpenGlFrame.Draw(host,width,height);}
        void Frames(NativeStudioProjectChangePage modal){for(int i=0;i<4;i++)Frame(modal);}
        void Wait(NativeStudioProjectChangePage modal,NativeStudioChangeStage stage){var timer=Stopwatch.StartNew();while(owner.CaptureNativeChange()?.Stage!=stage){Frame(modal);if(timer.ElapsedMilliseconds>5000)throw new InvalidOperationException("Native project modal did not reach "+stage+": "+owner.CaptureNativeChange());Thread.Sleep(1);}Frames(modal);}
        void Action(NativeStudioProjectChangePage modal,string id,int argument){host.DiscardIntents();check(host.FocusDocument(modal.Document,id),"native project control focuses: "+id);Frame(modal);host.Input.Key(2,true);host.Input.Key(2,false);host.Update();check(host.TryTakeIntent(out var intent)&&intent.Document==modal.Document&&intent.Kind==RmlUiIntentKind.StudioAction&&intent.Argument==argument&&modal.Handle(intent)&&!host.TryTakeIntent(out _),"one exact typed native project action: "+id);if(modal.IsOpen)Frames(modal);}
        void Capture(NativeStudioProjectChangePage modal,string name){Frames(modal);string path=Path.Combine(root,name+".png");check(ScreenCapture.SaveWindow(width,height,path),"native project modal capture: "+name);FinalCompositeCapture.WriteEvidence(path,width,height,"Actual native RmlUi project transition modal; one retained production owner, private synthetic JSON/recovery, no account/network/application loop.");}
        void EditorFrames(NativeStudioEditorPage editor){for(int i=0;i<4;i++){editor.Refresh();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.02f,.04f,.06f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);check(editor.DrawViewport(width,height,width,height),"committed native project draws through existing retained viewport renderer");RmlUiOpenGlFrame.Draw(host,width,height);}}
    }
    private sealed class Changes(MapStudioScreen owner):INativeStudioProjectChanges
    {
        internal INativeStudioEditorSession? LastTaken;
        public NativeStudioChangeSnapshot? CaptureChange()=>owner.CaptureNativeChange();
        public NativeStudioResult ResolveChange(Guid ticket,NativeStudioChangeDecision decision,string path)=>owner.ResolveNativeChange(ticket,decision,path);
        public void CancelChange(Guid ticket)=>owner.CancelNativeChange(ticket);
        public bool TryTakeCommittedEditor(Guid ticket,out INativeStudioEditorSession? session){bool result=owner.TryTakeNativeCommittedEditor(ticket,out session);if(result)LastTaken=session;return result;}
    }
    private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;
}
