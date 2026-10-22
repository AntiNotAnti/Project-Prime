using System.Reflection;
using MphRead;
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

/// <summary>PRIVATE D: existing saved synthetic owner/context, real native IO actions, no real OS dialog or application loop.</summary>
internal static class StudioAssetIoGlChecks
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,NativeStudioEditorPage page,MapStudioScreen owner,int width,int height,string root,Action<bool,string> check)
    {
        var document=Field<MapDocument>(owner,"_document");var history=document.History;var viewport=Field<MapViewport>(owner,"_viewport");var renderer=Field<Scene>(viewport,"_renderer");
        var session=Field<INativeStudioEditorSession>(page,"_session");var io=(INativeStudioEditorAssetIo)session;
        var writer=Field<MapAutosaveService>(owner,"_autosave");var watcher=Field<MapSourceWatchService>(owner,"_sourceWatch");
        check(document.FilePath!=null&&File.Exists(document.FilePath),"actual native file IO GL helper starts after real project Save");
        string folder=Path.Combine(root,"asset-io-sources");Directory.CreateDirectory(folder);
        string source=Path.Combine(folder,"blue.png"),replaceSource=Path.Combine(folder,"green.png"),export=Path.Combine(root,"exported-original.png");
        byte[] blue=Image(SkiaSharp.SKColors.CornflowerBlue),green=Image(SkiaSharp.SKColors.SeaGreen);File.WriteAllBytes(source,blue);File.WriteAllBytes(replaceSource,green);
        var picker=new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Set(session,"AssetPickerOverride",(Func<Task<string?>>)(()=>picker.Task));
        Action("native_resource_assets",86);string before=document.Project.Definition.Serialize();int commands=history.CommandCount;
        Action("native_asset_io_choose",190);check(io.CaptureAssetIo() is{Stage:NativeStudioAssetIoStage.ChoosingSource},"actual native OS boundary remains asynchronous while editor draws");
        picker.SetResult(source);WaitIo();check(host.ReadField(page.Document,"native_asset_io_source",32768)==source,"generation-owned picker completion updates actual native typed source");
        Draft("name","D_GL_IMPORTED");Action("native_asset_io_review_import",191);var reviewed=WaitIo();
        check(reviewed.Stage==NativeStudioAssetIoStage.Ready&&reviewed.Width==4&&reviewed.Height==4&&document.Project.Definition.Serialize()==before,"actual native review reads/probes/bakes private texture without authoring");
        check(NativeSemanticReadback.Read(host,page.Document).Label("native_asset_io_source_name")=="blue.png"
            &&NativeSemanticReadback.Read(host,page.Document).Label("native_asset_io_source_path")==NativeSemanticReadback.Normalize(Path.GetFullPath(source)),"native reviewed filename and complete source path are projected by exact IDs");
        Reach("native_asset_io_source_path");Capture("asset-io-import-review");Action("native_asset_io_apply_import",192);var applied=WaitIo();
        check(applied.Stage==NativeStudioAssetIoStage.Complete&&history.CommandCount==commands+1,"actual native Apply imports one declared pair/material in one retained command");
        var material=document.Project.Definition.Materials.Last();Guid materialId=material.Id;string native=material.Texture!,hd=material.Albedo!;
        check(MapAssets.Read(document.Project.Definition,hd).SequenceEqual(blue)&&MapAssets.Read(document.Project.Definition,native).SequenceEqual(MapTextureBake.BakeImage(blue)),"native import publishes exact original and existing native-baked texture bytes");
        string imported=document.Project.Definition.Serialize();Action("native_editor_undo",33);check(document.Project.Definition.Serialize()==before,"native imported pair/material exact Undo");
        Action("native_editor_redo",34);check(document.Project.Definition.Serialize()==imported,"native imported pair/material exact Redo");
        Action("native_resource_materials",85);var resources=Field<NativeStudioResourcesController>(page,"_resourceControls");
        int materialIndex=document.Project.Definition.Materials.FindIndex(m=>m.Id==materialId);
        while(resources.MaterialPage<materialIndex/8)Action("native_material_next",89);while(resources.MaterialPage>materialIndex/8)Action("native_material_previous",88);
        Action("native_material_row"+materialIndex%8,109+materialIndex%8);int uploads=renderer.EditorTextureUploads;Action("native_material_assign",92);
        check(renderer.EditorTextureUploads>uploads&&ReferenceEquals(renderer,Field<Scene>(viewport,"_renderer")),"native imported material assignment uploads through the existing renderer resource owner");Capture("asset-io-import-assigned");
        Action("native_resource_assets",86);SelectAsset(hd);Draft("source",replaceSource);before=document.Project.Definition.Serialize();commands=history.CommandCount;
        Action("native_asset_io_review_replace",193);reviewed=WaitIo();check(reviewed.Stage==NativeStudioAssetIoStage.Ready,"actual selected texture replacement reaches reviewed ready state");
        Action("native_asset_io_apply_replace",194);applied=WaitIo();string replacement=document.Project.Definition.Materials.Single(m=>m.Id==materialId).Albedo!;
        check(applied.Stage==NativeStudioAssetIoStage.Complete&&replacement!=hd&&MapAssets.Read(document.Project.Definition,replacement).SequenceEqual(green)
            &&File.Exists(Path.Combine(Path.GetDirectoryName(document.FilePath!)!,hd))&&history.CommandCount==commands+1,"native replacement changes selected reference once and retains previous actual bytes");
        string replaced=document.Project.Definition.Serialize();Action("native_editor_undo",33);check(document.Project.Definition.Serialize()==before,"native replacement exact Undo");
        Action("native_editor_redo",34);check(document.Project.Definition.Serialize()==replaced,"native replacement exact Redo");Capture("asset-io-replaced");
        SelectAsset(replacement);Draft("destination",export);Action("native_asset_io_review_export",195);reviewed=WaitIo();
        check(reviewed.Stage==NativeStudioAssetIoStage.Ready&&NativeSemanticReadback.Read(host,page.Document).Label("native_asset_io_destination_name")==Path.GetFileName(export)
            &&NativeSemanticReadback.Read(host,page.Document).Label("native_asset_io_destination_path")==NativeSemanticReadback.Normalize(Path.GetFullPath(export)),"native export review exposes exact basename and full destination");
        Reach("native_asset_io_destination_path");Capture("asset-io-export-review");before=document.Project.Definition.Serialize();Action("native_asset_io_export",196);applied=WaitIo();
        check(applied.Stage==NativeStudioAssetIoStage.Complete&&File.ReadAllBytes(export).SequenceEqual(green)&&document.Project.Definition.Serialize()==before,"actual native new-target export writes exact original bytes without authoring mutation");
        Action("native_asset_io_review_export",195);reviewed=WaitIo();check(reviewed.Stage==NativeStudioAssetIoStage.NeedsOverwrite&&host.FocusedElement()=="native_asset_io_overwrite","native overwrite review takes explicit visible confirmation focus");
        Capture("asset-io-overwrite-review");Action("native_asset_io_overwrite",197);applied=WaitIo();check(applied.Stage==NativeStudioAssetIoStage.Complete&&File.ReadAllBytes(export).SequenceEqual(green),"actual native reviewed overwrite verifies original asset bytes");
        // Reload uses exactly the selected asset's retained provenance, with no injected source intent.
        SelectAsset(replacement);Action("native_asset_io_reload_source",199);reviewed=WaitIo();check(reviewed.Stage==NativeStudioAssetIoStage.Ready&&reviewed.Source==Path.GetFullPath(replaceSource),"native recorded-source reload reviews the actual selected asset source");
        Action("native_asset_io_cancel",198);check(document.Project.Definition.Serialize()==before,"native cancel of reviewed reload keeps exact authoring state");
        picker=new(TaskCreationOptions.RunContinuationsAsynchronously);Set(session,"AssetPickerOverride",(Func<Task<string?>>)(()=>picker.Task));
        string typed=host.ReadField(page.Document,"native_asset_io_source",32768);Action("native_asset_io_choose",190);Action("native_asset_io_cancel",198);picker.SetResult(source);Settle();
        check(host.ReadField(page.Document,"native_asset_io_source",32768)==typed&&document.Project.Definition.Serialize()==before,"late canceled OS boundary cannot overwrite native source draft or document");
        Set(session,"AssetPickerOverride",null);
        check(ReferenceEquals(document,Field<MapDocument>(owner,"_document"))&&ReferenceEquals(history,document.History)&&ReferenceEquals(viewport,Field<MapViewport>(owner,"_viewport"))
            &&ReferenceEquals(renderer,Field<Scene>(viewport,"_renderer"))&&ReferenceEquals(writer,Field<MapAutosaveService>(owner,"_autosave"))&&ReferenceEquals(watcher,Field<MapSourceWatchService>(owner,"_sourceWatch"))&&session.Capture().Alive,
            "all native file IO keeps sole document/history/autosave/sourcewatch/viewport/renderer and active lease");
        Action("native_editor_save",35);var reopened=MapProjectSerializer.Load(document.FilePath!);
        check(!document.IsDirty&&reopened.Definition.Materials.Single(m=>m.Id==materialId).Albedo==replacement
            &&MapAssets.Read(reopened.Definition,replacement).SequenceEqual(green),"actual native Save/reopen preserves exact imported/replaced asset reference and bytes");
        check(GL.GetError()==ErrorCode.NoError,"native asset IO frames/texture uploads leave no GL error");Action("native_resource_objects",87);

        void Frame(){page.Refresh();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture0);GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.02f,.04f,.06f,1);GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);string state=GlState();check(page.DrawViewport(width,height,width,height),"asset IO frame borrows existing contained viewport");check(state==GlState(),"asset IO renderer restores compositor GL contract");RmlUiOpenGlFrame.Draw(host,width,height);}
        void Settle(){for(int i=0;i<3;i++)Frame();}
        NativeStudioAssetIoSnapshot WaitIo(){for(int i=0;i<10000;i++){Frame();var state=io.CaptureAssetIo()??throw new InvalidOperationException("native IO snapshot missing");if(!state.Busy){Settle();return state;}Thread.Sleep(1);}throw new InvalidOperationException("native IO worker did not drain");}
        void Draft(string name,string value){string id="native_asset_io_"+name;check(host.FocusDocument(page.Document,id),"native asset IO draft focuses: "+name);host.SetField(page.Document,id,value);}
        void Action(string id,int action){host.DiscardIntents();check(host.FocusDocument(page.Document,id),"native asset IO control focuses: "+id);Frame();host.Input.Key(2,true);host.Input.Key(2,false);host.Update();check(host.TryTakeIntent(out var intent)&&intent.Document==page.Document&&intent.Kind==RmlUiIntentKind.StudioAction&&intent.Argument==action&&page.Handle(intent)&&!host.TryTakeIntent(out _),"one actual typed asset IO intent: "+id);Settle();}
        void Reach(string id){check(host.FocusDocument(page.Document,id),"reviewed full path is keyboard reachable: "+id);Frame();check(host.TryGetElementBounds(page.Document,id,out float x,out float y,out float w,out float h)&&host.TryGetElementBounds(page.Document,"native_editor_left",out float lx,out float ly,out float lw,out float lh)&&w>0&&h>0&&x>=lx-.5f&&x+w<=lx+lw+.5f&&y>=ly-.5f&&y+h<=ly+lh+.5f,"reviewed full path is contained/reachable in native scroller: "+id);}
        void SelectAsset(string path){int at=resources.Snapshot.Assets.ToList().FindIndex(a=>a.Path==path);check(at>=0,"native declared asset has exact actual row: "+path);while(resources.AssetPage<at/8)Action("native_asset_next",118);while(resources.AssetPage>at/8)Action("native_asset_previous",117);Action("native_asset_row"+at%8,124+at%8);check(resources.Selected.AssetPath==path,"native paging selects exact asset identity");}
        void Capture(string name){Settle();string path=Path.Combine(root,name+".png");check(ScreenCapture.SaveWindow(width,height,path),"native asset IO capture: "+name);FinalCompositeCapture.WriteEvidence(path,width,height,"Actual native Studio texture IO controls with private generated PNG/native bytes, same retained document/history/renderer; no proprietary import, app/account/network/device/performance claim.");}
    }
    private static byte[] Image(SkiaSharp.SKColor color){using var bitmap=new SkiaSharp.SKBitmap(4,4);bitmap.Erase(color);using var encoded=bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png,100);return encoded.ToArray();}
    private static string GlState(){int[] v=new int[4],s=new int[4];GL.GetInteger(GetPName.Viewport,v);GL.GetInteger(GetPName.ScissorBox,s);return string.Join(",",v)+"|"+string.Join(",",s)+"|"+GL.GetInteger(GetPName.CurrentProgram)+"|"+GL.GetInteger(GetPName.DrawFramebufferBinding)+"|"+GL.GetInteger(GetPName.ReadFramebufferBinding)+"|"+GL.GetInteger(GetPName.ActiveTexture)+"|"+GL.IsEnabled(EnableCap.ScissorTest);}
    private static T Field<T>(object value,string name)=>(T)value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;
    private static void Set(object value,string name,object? field)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(value,field);
}
