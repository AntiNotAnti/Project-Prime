using System;
using System.IO;
using System.Linq;
using System.Reflection;
using MphRead;
using MphRead.Mods;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.Launcher.RmlUi.Host;
using MphRead.Mods.Launcher.RmlUi.Pages.Studio;
using MphRead.Mods.Launcher.RmlUi.Render;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;
using OpenTK.Graphics.OpenGL;

/// <summary>
/// PRIVATE SOURCE STAGING ONLY. Actual RML controls and the caller's existing retained renderer/context.
/// No execution is claimed until the Studio C owner explicitly promotes and runs all three framebuffer sizes.
/// </summary>
internal static class StudioModelingGlChecks
{
    internal static void Run(RmlUiHost host,RmlUiPageManager pages,NativeStudioEditorPage page,
        MapStudioScreen owner,int width,int height,string root,Action<bool,string> check)
    {
        var document=Field<MapDocument>(owner,"_document");var history=document.History;
        var viewport=Field<MapViewport>(owner,"_viewport");var renderer=Field<Scene>(viewport,"_renderer");
        var session=Field<INativeStudioEditorSession>(page,"_session");
        var modeling=(INativeStudioEditorModeling)session;var modelViewport=(INativeStudioEditorModelingViewport)session;
        Action("native_resource_objects",87);Action("native_model_object_mode",139);Action("native_create_box",60);
        Guid id=document.ActiveObjectId;
        check(MapObjects.Find(document.Project.Definition,id)?.Value is MapBox&&!document.Project.Definition.Geometry.Single(g=>g.Id==id).Locked,
            "native modeling fixture creates an unlocked authored box through current owner control");
        // Resolve actual paged row identity rather than injecting selection or synthetic intents.
        var objects=Field<NativeStudioEditorController>(page,"_controller");
        int index=objects.Current().Owner.Rows.ToList().FindIndex(row=>row.Id==id);
        check(index>=0,"new native box has an actual current object row");
        while(objects.Current().Page<index/8)Action("native_editor_next",46);
        while(objects.Current().Page>index/8)Action("native_editor_previous",45);
        Action("native_editor_row"+index%8,47+index%8);
        check(document.Selection.SetEquals(new[]{id})&&document.ActiveObjectId==id,"actual focused object row selects exact created geometry identity");
        string primitive=Bytes();var primitiveState=document.CurrentStateId;int commands=history.CommandCount;
        Action("native_model_convert_to_mesh",173);string converted=Bytes();
        check(MapObjects.Find(document.Project.Definition,id)?.Value is MapMesh&&converted!=primitive&&history.CommandCount==commands+1,
            "native Convert to Mesh preserves selected ID and creates one retained command");
        Action("native_editor_undo",33);check(Bytes()==primitive&&document.CurrentStateId==primitiveState,"native conversion undo restores exact authored primitive and state");
        Action("native_editor_redo",34);check(Bytes()==converted,"native conversion redo restores exact converted source");

        Action("native_model_edge_mode",141);var controls=Field<NativeStudioModelingController>(page,"_modelControls");
        check(controls.Snapshot.Mode==NativeStudioElementMode.Edge&&controls.Snapshot.Components.Count==12&&controls.Rows.Count==8&&controls.Pages==2,
            "actual edge mode presents twelve converted-box edges in bounded eight-row pages");
        Action("native_model_next",164);var edge=controls.Rows[0];Action("native_model_row0",165);
        check(controls.Page==1&&controls.Rows.Count==4&&viewport.SubSelection.Edges.Contains(new MapEdge(edge.A,edge.B)),
            "actual next-page component button resolves the displayed edge endpoints");
        Action("native_model_previous",163);check(controls.Page==0&&controls.Rows.Count==8,"actual previous component page restores eight rows");
        Action("native_model_vertex_mode",142);Action("native_model_row7",172);
        check(controls.Snapshot.Mode==NativeStudioElementMode.Vertex&&viewport.SubSelection.Vertices.SetEquals(new[]{7}),
            "native vertex mode row seven selects exact retained mesh vertex");
        Action("native_model_face_mode",140);Action("native_model_row0",165);
        check(viewport.SubSelection.Faces.SetEquals(new[]{0}),"native face row selects exact current face before UV edits");
        string beforeUv=Bytes();var beforeUvState=document.CurrentStateId;commands=history.CommandCount;
        Draft("uv_u","2");Draft("uv_v","3");Draft("uv_offset_u","5");Draft("uv_offset_v","7");
        Action("native_model_uv_apply",175);string appliedUv=Bytes();
        check(appliedUv!=beforeUv&&history.CommandCount==commands+1,"actual native UV drafts apply together through retained object history");
        Action("native_editor_undo",33);check(Bytes()==beforeUv&&document.CurrentStateId==beforeUvState,"native UV Apply undo restores exact authored bytes/state");
        Action("native_editor_redo",34);check(Bytes()==appliedUv,"native UV Apply redo restores exact authored bytes");
        Action("native_model_reset_uv_draft",189);Draft("uv_offset_u","777");Draft("uv_v","NaN");
        commands=history.CommandCount;var validUvState=document.CurrentStateId;Action("native_model_uv_apply",175);
        check(Bytes()==appliedUv&&document.CurrentStateId==validUvState&&history.CommandCount==commands,
            "actual malformed simultaneous native UV drafts reject atomically");
        Action("native_model_reset_uv_draft",189);Action("native_model_uv_copy",178);
        check(Bytes()==appliedUv&&history.CommandCount==commands,"native UV Copy changes neither document nor history");
        Action("native_model_uv_rotate_positive",180);string rotated=Bytes();
        check(rotated!=appliedUv,"native selected-face Rotate UV dispatch reaches real UV operation");
        Action("native_editor_undo",33);check(Bytes()==appliedUv,"native Rotate UV exact undo");
        Action("native_editor_redo",34);check(Bytes()==rotated,"native Rotate UV exact redo");
        Capture("modeling-face-uv");
        var uvActions=new (string Id,int Action,string Label)[]{
            ("native_model_uv_apply",175,"UV APPLY"),("native_model_uv_fit64",176,"FIT TO 64 TEXELS"),
            ("native_model_uv_match_density",177,"APPLY TEXEL DENSITY"),("native_model_uv_copy",178,"UV COPY"),
            ("native_model_uv_paste",179,"UV PASTE"),("native_model_uv_rotate_positive",180,"UV ROTATE POSITIVE"),
            ("native_model_uv_rotate_negative",181,"UV ROTATE NEGATIVE"),("native_model_uv_flip_u",182,"UV FLIP U"),
            ("native_model_uv_flip_v",183,"UV FLIP V"),("native_model_uv_explicit",184,"STORE EXPLICIT UVS"),
            ("native_model_uv_projected",185,"USE PROJECTED UVS"),("native_model_checker",186,"UV CHECKER: OFF"),
            ("native_model_reset_uv_draft",189,"RESET UV DRAFT")};
        foreach(var uvAction in uvActions)
        {
            check(host.FocusDocument(page.Document,uvAction.Id),"every UV action is reachable by real native focus: "+uvAction.Id);Frame();
            check(NativeSemanticReadback.Read(host,page.Document).Label(uvAction.Id)==uvAction.Label,"full exact semantic label belongs to one UV button: "+uvAction.Id);
            check(host.TryGetElementBounds(page.Document,uvAction.Id,out float bx,out float by,out float bw,out float bh)
                &&host.TryGetElementBounds(page.Document,"native_model_uv_actions",out float gx,out float gy,out float gw,out float gh)
                &&host.TryGetElementBounds(page.Document,"native_editor_left",out float lx,out float ly,out float lw,out float lh)
                &&bw>0&&bh>0&&bx>=gx-.5f&&bx+bw<=gx+gw+.5f&&bx>=lx-.5f&&bx+bw<=lx+lw+.5f
                &&by>=ly-.5f&&by+bh<=ly+lh+.5f,"focused full UV button stays horizontally contained and vertically reachable in left scroller: "+uvAction.Id);
            Capture("modeling-uv-control-"+uvAction.Action);
            if(uvAction.Action==177)Draft("uv_density","24");
            string beforeAction=Bytes();var beforeActionState=document.CurrentStateId;bool beforeChecker=controls.Snapshot.Checker;
            Action(uvAction.Id,uvAction.Action);string afterAction=Bytes();var afterActionState=document.CurrentStateId;
            if(uvAction.Action==186)
            {
                check(controls.Snapshot.Checker!=beforeChecker&&afterAction==beforeAction&&afterActionState==beforeActionState,"native checker toggles existing preview only");
                Action(uvAction.Id,uvAction.Action);check(controls.Snapshot.Checker==beforeChecker,"native checker returns to original preview state");
            }
            else if(afterActionState!=beforeActionState)
            {
                Action("native_editor_undo",33);check(Bytes()==beforeAction&&document.CurrentStateId==beforeActionState,"UV action has one-step exact native undo: "+uvAction.Id);
                Action("native_editor_redo",34);check(Bytes()==afterAction&&document.CurrentStateId==afterActionState,"UV action has exact native redo: "+uvAction.Id);
            }
            else check(afterAction==beforeAction,"UV no-op/copy/reset leaves authored bytes unchanged: "+uvAction.Id);
        }

        // All vertices keep the synthetic cube valid while exercising a real detached component preview.
        Action("native_model_vertex_mode",142);Action("native_model_component_select_all",157);
        Action("native_model_tool_move",143);Action("native_model_axis_free",146);
        Draft("grid","0");Action("native_model_snap_apply",154);Action("native_editor_frame_selection",40);
        check(viewport.SubSelection.Vertices.Count==8,"native Select All binds actual converted mesh vertices for drag");
        Settle();var down=GizmoPoint();var target=DragTarget(down);
        string beforeDrag=Bytes();var beforeDragState=document.CurrentStateId;commands=history.CommandCount;
        string sourceGeometry=PresentedGeometry();byte[] sourcePixels=FinalCompositeCapture.Read(width,height);
        check(page.PointerButton(0,down.X,down.Y,true)&&modeling.CaptureModeling().GestureActive&&viewport.NativeTransformActive,
            "page pointer-down on actual projected gizmo center acquires real component gesture");
        check(page.PointerMoved(target.X,target.Y),"actual page pointer move routes component preview");Frame();
        check(Bytes()==beforeDrag&&document.CurrentStateId==beforeDragState&&history.CommandCount==commands,
            "actual drawn gizmo preview freezes authored bytes and history before release");
        check(PresentedGeometry()!=sourceGeometry&&!FinalCompositeCapture.Read(width,height).SequenceEqual(sourcePixels),
            "actual renderer receives changed detached component positions and paints a changed composite");
        Capture("modeling-detached-preview");
        check(page.PointerButton(0,target.X,target.Y,false),"actual pointer-up is consumed by captured gesture");Settle();
        string committed=Bytes();var committedState=document.CurrentStateId;
        check(committed!=beforeDrag&&history.CommandCount==commands+1&&!modeling.CaptureModeling().GestureActive&&!viewport.NativeTransformActive,
            "actual in-rectangle gizmo release commits exactly one retained history command");
        Action("native_editor_undo",33);check(Bytes()==beforeDrag&&document.CurrentStateId==beforeDragState,"gizmo one-step undo restores exact source/state");
        Action("native_editor_redo",34);check(Bytes()==committed&&document.CurrentStateId==committedState,"gizmo one-step redo restores exact release/state");

        foreach(string reason in new[]{"out-of-rectangle","focus","release-focus","escape","modal","ratio"})
        {
            Settle();var start=GizmoPoint();var end=DragTarget(start);string before=Bytes();var state=document.CurrentStateId;
            int count=history.CommandCount;bool redo=history.CanRedo;
            check(page.PointerButton(0,start.X,start.Y,true)&&modeling.CaptureModeling().GestureActive,"cancellation fixture starts actual gizmo capture: "+reason);
            page.PointerMoved(end.X,end.Y);Frame();
            check(Bytes()==before&&document.CurrentStateId==state,"canceled "+reason+" gesture remains detached before cancellation");
            if(reason=="out-of-rectangle")page.PointerMoved(-1,-1);
            else if(reason=="focus")
            {check(host.FocusDocument(page.Document,"native_model_tool_move"),"focus cancellation transfers real native focus");Frame();}
            else if(reason=="release-focus")
            {check(host.FocusDocument(page.Document,"native_model_tool_move"),"release-time focus transfers before another editor refresh");host.Update();page.PointerButton(0,end.X,end.Y,false);}
            else if(reason=="escape")check(!page.Back()&&!page.Closed&&session.Capture().Alive,"Escape/Back cancels active gesture while retaining editor lease");
            else if(reason=="modal")
            {
                var modal=pages.OpenModal(new("studio-modeling-cancel-fixture","pages/studio/native-entry.rml","native_studio_back"));host.Update();
                page.PointerMoved(end.X,end.Y);
                check(pages.Top==modal&&!modeling.CaptureModeling().GestureActive&&!page.PointerButton(0,end.X,end.Y,true),
                    "actual modal activation cancels captured move and prevents background reacquisition");
                check(pages.CloseModal(),"modeling cancellation fixture closes only its actual modal");host.Update();
            }
            else check(!page.ValidateInputBasis(width,height,Math.Max(1,width/2),height),"client/framebuffer ratio change invalidates old drawn basis");
            page.PointerButton(0,end.X,end.Y,false);Settle();
            check(Bytes()==before&&document.CurrentStateId==state&&history.CommandCount==count&&history.CanRedo==redo
                &&!modeling.CaptureModeling().GestureActive&&!viewport.NativeTransformActive,
                "actual "+reason+" cancellation preserves exact authored bytes/history and retires preview");
            // This point is calculated from the new successful draw, so old capture state cannot authorize it.
            var fresh=GizmoPoint();check(page.PointerButton(0,fresh.X,fresh.Y,true)&&modeling.CaptureModeling().GestureActive,
                "fresh drawn basis reacquires after "+reason+" cancellation");page.ReleaseInput();Settle();
            check(Bytes()==before&&!modeling.CaptureModeling().GestureActive,"reacquired gesture explicitly cancels without committing");
        }
        Capture("modeling-components");
        check(ReferenceEquals(document,Field<MapDocument>(owner,"_document"))&&ReferenceEquals(history,document.History)
            &&ReferenceEquals(viewport,Field<MapViewport>(owner,"_viewport"))&&ReferenceEquals(renderer,Field<Scene>(viewport,"_renderer"))
            &&ReferenceEquals(session,Field<INativeStudioEditorSession>(page,"_session"))&&session.Capture().Alive,
            "native modeling retains exact document/history/viewport/renderer/editor lease owners");
        check(GL.GetError()==ErrorCode.NoError,"native modeling actions, previews and cancellations leave no GL error");

        string Bytes()=>document.Project.Definition.Serialize();
        void Frame()
        {
            page.Refresh();host.Update();GL.BindFramebuffer(FramebufferTarget.Framebuffer,0);GL.UseProgram(0);GL.ActiveTexture(TextureUnit.Texture0);
            GL.Viewport(0,0,width,height);GL.Disable(EnableCap.ScissorTest);GL.ClearColor(.02f,.04f,.06f,1);
            GL.Clear(ClearBufferMask.ColorBufferBit|ClearBufferMask.DepthBufferBit);
            var state=GlState();check(page.DrawViewport(width,height,width,height),"modeling frame draws the existing contained viewport");
            check(state==GlState(),"actual retained modeling draw restores framebuffer/program/viewport/scissor/texture/depth/blend state");
            RmlUiOpenGlFrame.Draw(host,width,height);
        }
        void Settle(){for(int i=0;i<3;i++)Frame();}
        void Action(string element,int argument)
        {
            host.DiscardIntents();check(host.FocusDocument(page.Document,element),"native modeling control focuses: "+element);Frame();
            host.Input.Key(2,true);host.Input.Key(2,false);host.Update();
            check(host.TryTakeIntent(out var intent)&&intent.Document==page.Document&&intent.Kind==RmlUiIntentKind.StudioAction
                &&intent.Argument==argument&&page.Handle(intent)&&!host.TryTakeIntent(out _),"one actual typed native modeling intent: "+element);Settle();
        }
        void Draft(string field,string value)
        {string element="native_model_"+field;check(host.FocusDocument(page.Document,element),"actual native modeling field focuses: "+element);host.SetField(page.Document,element,value);}
        NativeStudioViewportBasis Basis()
        {var value=modelViewport.CaptureModelingBasis(width,height,width,height);check(value.HasValue,"existing renderer published an exact modeling framebuffer/client basis");return value!.Value;}
        (double X,double Y) GizmoPoint()
        {
            var basis=Basis();var axes=viewport.NativeGizmoAxes;check(axes.HasValue,"selected actual mesh exposes a visible native gizmo");
            var layout=new MapViewportLayout(basis.Width,basis.Height,1,basis.X,basis.Y);
            var camera=new MapViewportCamera(basis.CameraPosition,basis.CameraTarget,basis.Perspective);
            var projected=camera.Project(layout,axes!.Value.Center);check(projected.HasValue,"actual gizmo center projects in the explicit drawn pixel rectangle");
            var p=projected!.Value;double x=basis.X+p.X,y=basis.Y+p.Y;
            check(basis.Contains(x,y)&&p.X>=40&&p.Y>=40&&p.X<basis.Width-40&&p.Y<basis.Height-40,
                "actual projected center is visible with room for a contained drag");return(x,y);
        }
        (double X,double Y) DragTarget((double X,double Y) start)=>(start.X+28,start.Y+12);
        string PresentedGeometry()
        {
            var b=Basis();var frame=viewport.BuildRenderFrame(new(b.Width,b.Height,1,b.X,b.Y));
            return string.Join("|",frame.Meshes.Where(mesh=>mesh.ObjectId==id).SelectMany(mesh=>mesh.Faces)
                .SelectMany(face=>face.Points).Select(point=>$"{point.X:R},{point.Y:R},{point.Z:R}"));
        }
        void Capture(string name)
        {
            Frame();string path=Path.Combine(root,name+".png");check(ScreenCapture.SaveWindow(width,height,path),"actual native modeling capture: "+name);
            FinalCompositeCapture.WriteEvidence(path,width,height,"Actual Studio C RML controls, actual retained synthetic-map geometry and detached modeling preview in caller's existing renderer/context. Private fixture only; no asset import, game/account/network/performance claim.");
        }
    }

    private static string GlState()
    {
        int[] viewport=new int[4],scissor=new int[4];GL.GetInteger(GetPName.Viewport,viewport);GL.GetInteger(GetPName.ScissorBox,scissor);
        return string.Join(",",viewport)+"|"+string.Join(",",scissor)+"|"+GL.GetInteger(GetPName.FramebufferBinding)+"|"
            +GL.GetInteger(GetPName.ReadFramebufferBinding)+"|"+GL.GetInteger(GetPName.CurrentProgram)+"|"+GL.GetInteger(GetPName.ActiveTexture)+"|"
            +GL.IsEnabled(EnableCap.ScissorTest)+"|"+GL.IsEnabled(EnableCap.DepthTest)+"|"+GL.IsEnabled(EnableCap.Blend);
    }
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
}
