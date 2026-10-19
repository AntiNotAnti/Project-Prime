using System.Reflection;
using System.Numerics;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Requires existing --studio-owner isolated process setup; no GL resources.</summary>
internal static class StudioModelingOwnerChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var owner=new MapStudioScreen(preview:true);
        check(owner.TryAcquireNativeEditor(out var acquired,out _)&&acquired!=null,"production retained owner grants native modeling lease");
        using var session=acquired!;var model=(INativeStudioEditorModeling)(INativeStudioEditorSession)session;
        var document=Field<MapDocument>(owner,"_document");var viewport=Field<MapViewport>(owner,"_viewport");var history=document.History;
        var writer=Field<MapAutosaveService>(owner,"_autosave");var watcher=Field<MapSourceWatchService>(owner,"_sourceWatch");
        var details=(INativeStudioEditorObjects)(INativeStudioEditorSession)session;
        check(details.CreateObject(session.Capture().Ticket,NativeStudioCreateKind.Box).Success,"production modeling fixture creates primitive through retained owner");
        Guid id=document.ActiveObjectId;string primitive=document.Project.Definition.Serialize();
        check(model.ExecuteModeling(model.CaptureModeling().Ticket,NativeStudioModelingOperation.ConvertMesh,new()).Success,"production native conversion uses ordinary document history");
        string converted=document.Project.Definition.Serialize();check(MapObjects.Find(document.Project.Definition,id)?.Value is MapMesh,"native conversion preserves exact selected object identity");
        check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Undo,default).Success&&document.Project.Definition.Serialize()==primitive
            &&session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Redo,default).Success&&document.Project.Definition.Serialize()==converted,"conversion undo/redo retains exact authored bytes");
        var ticket=model.CaptureModeling().Ticket;
        check(model.ConfigureModeling(ticket,mode:NativeStudioElementMode.Face,gridSnap:.5f,angleSnap:15,scaleSnap:.25f,pivot:NativeStudioPivot.Center).Success
            &&document.Project.Definition.Serialize()==converted,"mode/snap/pivot configuration never edits authored document");
        check(!model.ConfigureModeling(ticket,mode:NativeStudioElementMode.Vertex).Success,"model settings reject previous configuration ticket");
        var state=model.CaptureModeling();var row=state.Components[0];
        check(model.SelectComponent(state.Ticket,row).Success&&viewport.SubSelection.Faces.SetEquals(new[]{0}),"native component row resolves real mesh face selection");
        check(!model.SelectComponent(state.Ticket,state.Components[1]).Success,"native component selection invalidates prior component ticket");
        state=model.CaptureModeling();string beforeUv=document.Project.Definition.Serialize();
        check(model.ExecuteModeling(state.Ticket,NativeStudioModelingOperation.ApplyUv,new(Scale:2,ScaleV:3,OffsetU:4,OffsetV:5)).Success,"production UV Apply reaches retained mesh edit operation");
        string uv=document.Project.Definition.Serialize();
        check(uv!=beforeUv&&session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Undo,default).Success&&document.Project.Definition.Serialize()==beforeUv
            &&session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Redo,default).Success&&document.Project.Definition.Serialize()==uv,"production UV change undo/redo restores exact bytes");
        state=model.CaptureModeling();
        check(!model.ExecuteModeling(state.Ticket,NativeStudioModelingOperation.ApplyUv,new(Scale:float.NaN)).Success&&document.Project.Definition.Serialize()==uv,
            "native owner rejects nonfinite UV values atomically");
        check(model.ExecuteModeling(model.CaptureModeling().Ticket,NativeStudioModelingOperation.CopyUv,new()).Success,"native copy uses retained editor UV clipboard");
        var copied=Field<float[][]>(owner,"_copiedFaceUv");check(copied.Length==((MapMesh)MapObjects.Find(document.Project.Definition,id)!.Value).Faces[0].Length,"retained clipboard contains detached selected face corners");
        state=model.CaptureModeling();var old=state.Components[0];
        document.EditObjects("Owner topology edit",new[]{id},d=>{var mesh=(MapMesh)MapObjects.Find(d,id)!.Value;mesh.Faces[0]=mesh.Faces[0].Reverse().ToArray();});
        string changed=document.Project.Definition.Serialize();
        check(!model.SelectComponent(state.Ticket,old).Success&&document.Project.Definition.Serialize()==changed,"external retained history topology change rejects painted component row");
        state=model.CaptureModeling();var source=(MapMesh)MapObjects.Find(document.Project.Definition,id)!.Value;
        int[] oldFace=source.Faces[0];source.Faces[0]=oldFace.Reverse().ToArray();
        check(!model.SelectComponent(state.Ticket,state.Components[0]).Success,"same-reference topology mutation is rejected independently of document state cache");source.Faces[0]=oldFace;
        check(model.ConfigureModeling(model.CaptureModeling().Ticket,mode:NativeStudioElementMode.Vertex).Success,"native component mode can change after owner edit");
        state=model.CaptureModeling();check(model.ExecuteModeling(state.Ticket,NativeStudioModelingOperation.SelectAll,new()).Success&&viewport.SubSelection.Vertices.Count==source.Vertices.Count,"native Select All uses existing component selection owner");
        check(model.ExecuteModeling(model.CaptureModeling().Ticket,NativeStudioModelingOperation.Clear,new()).Success&&viewport.SubSelection.VertexIndices(source,"Vertex").Length==0,"native Clear retains mesh and clears only components");
        state=model.CaptureModeling();check(model.SelectComponent(state.Ticket,state.Components[0]).Success,"component fixture selects original mesh vertex before changing objects");
        check(details.CreateObject(session.Capture().Ticket,NativeStudioCreateKind.Box).Success,"retained owner creates second authored object for component isolation");
        Guid second=document.ActiveObjectId;check(model.ExecuteModeling(model.CaptureModeling().Ticket,NativeStudioModelingOperation.ConvertMesh,new()).Success,"second object converts through same retained owner");
        check(session.Select(session.Capture().Ticket,id).Success,"native object selection restores first mesh");
        state=model.CaptureModeling();check(model.SelectComponent(state.Ticket,state.Components[0]).Success,"first mesh component selection is current");
        check(session.Select(session.Capture().Ticket,second).Success&&viewport.SubSelection.ObjectId==Guid.Empty,"changing native object clears the prior mesh's component selection");
        string isolated=document.Project.Definition.Serialize();
        check(!model.ExecuteModeling(model.CaptureModeling().Ticket,NativeStudioModelingOperation.ApplyTransform,new(X:2)).Success&&document.Project.Definition.Serialize()==isolated,
            "numeric component transform cannot mutate previous mesh after selecting a different mesh with no components");
        check(ReferenceEquals(document,Field<MapDocument>(owner,"_document"))&&ReferenceEquals(history,document.History)&&ReferenceEquals(writer,Field<MapAutosaveService>(owner,"_autosave"))
            &&ReferenceEquals(watcher,Field<MapSourceWatchService>(owner,"_sourceWatch"))&&Field<object?>(viewport,"_renderer")==null,"native modeling retains exact document/history/writer/watcher and creates no renderer");
        ticket=model.CaptureModeling().Ticket;check(session.TryClose().Success&&!model.ExecuteModeling(ticket,NativeStudioModelingOperation.ConvertMesh,new()).Success,"retired modeling lease cannot author retained document");
    }
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
}
