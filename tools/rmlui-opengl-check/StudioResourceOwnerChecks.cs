using System.Reflection;
using MphRead.Mods.Launcher;
using MphRead.Mods.Launcher.Gui;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

/// <summary>Actual retained-session resource authority; private synthetic declarations, no asset import/window/GL.</summary>
internal static class StudioResourceOwnerChecks
{
    internal static void Run(Action<bool,string> check)
    {
        using var owner=new MapStudioScreen(preview:true);
        check(owner.TryAcquireNativeEditor(out var acquired,out _)&&acquired!=null,"retained production owner grants native resource fixture lease");
        using var session=acquired!;var resources=(INativeStudioEditorResources)(INativeStudioEditorSession)session;
        var document=Field<MapDocument>(owner,"_document");var history=document.History;
        var writer=Field<MapAutosaveService>(owner,"_autosave");var watcher=Field<MapSourceWatchService>(owner,"_sourceWatch");
        var start=resources.CaptureResources();
        check(start.Alive&&start.Materials.Count>0&&Field<object?>(Field<MapViewport>(owner,"_viewport"),"_renderer")==null,
            "native resource reads retain actual document/writer without renderer construction");
        check(resources.ExecuteResource(Empty(start),NativeStudioResourceOperation.AddMaterial,new()).Success,"production resource add delegates existing writer");
        var snapshot=resources.CaptureResources();int index=snapshot.Materials.Count-1;
        var material=Ticket(snapshot,index);
        var fields=resources.ReadMaterial(material);
        string before=document.Project.Definition.Serialize();var beforeState=document.CurrentStateId;
        check(fields.Fields.Count>16&&resources.ApplyMaterial(material,new Dictionary<string,string>{{"name","OWNER_MATERIAL"},{"texScale","32"}}).Success,
            "production material schema applies known typed fields");
        string changed=document.Project.Definition.Serialize();var changedState=document.CurrentStateId;
        check(changed!=before&&beforeState!=changedState,"actual session material patch reaches retained history");
        check(!resources.ApplyMaterial(material,new Dictionary<string,string>{{"name","STALE"}}).Success&&document.Project.Definition.Serialize()==changed,
            "production resource owner rejects old material document ticket");
        check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Undo,default).Success&&document.Project.Definition.Serialize()==before
            &&session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Redo,default).Success&&document.Project.Definition.Serialize()==changed,
            "native session material changes undo and redo exact document bytes");
        snapshot=resources.CaptureResources();material=Ticket(snapshot,index);
        check(!resources.ApplyMaterial(material,new Dictionary<string,string>{{"name","PARTIAL"},{"texScale","NaN"}}).Success
            &&document.Project.Definition.Serialize()==changed,"production material patch validates all fields before any mutation");
        var notices=Field<HashSet<string>>(owner,"_changedSources");notices.Add("synthetic-completed-source-watch-notice");
        check(!resources.ApplyMaterial(material,new Dictionary<string,string>{{"name","STALE_SOURCE"}}).Success
            &&document.Project.Definition.Serialize()==changed,"completed existing source-watch notice invalidates production resource draft");
        snapshot=resources.CaptureResources();material=Ticket(snapshot,index);
        check(snapshot.ChangedSources==1&&snapshot.SourceNotices!=start.SourceNotices,"production resource snapshot exposes source notification generation");
        typeof(MapStudioScreen).GetField("_sourceChangeRevision",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(owner,1L);
        check(!resources.ApplyMaterial(material,new Dictionary<string,string>{{"name","STALE_REPEATED_SOURCE"}}).Success
            &&document.Project.Definition.Serialize()==changed,
            "a second observed notification for the same pending source path invalidates production resource draft again");
        snapshot=resources.CaptureResources();material=Ticket(snapshot,index);
        check(resources.ExecuteResource(material,NativeStudioResourceOperation.DuplicateMaterial,new()).Success,
            "production resource duplicate delegates one existing map history operation");
        snapshot=resources.CaptureResources();var duplicate=Ticket(snapshot,snapshot.Materials.Count-1);
        check(duplicate.MaterialId!=material.MaterialId&&duplicate.MaterialFingerprint!=material.MaterialFingerprint,
            "production duplicated material has distinct stable identity and fields");
        var confirmation=resources.PrepareResourceDelete(duplicate,asset:false);
        check(confirmation!=null&&resources.ConfirmResourceDelete(confirmation).Success&&!resources.ConfirmResourceDelete(confirmation).Success,
            "production unused-material confirmation is exact and single-use");
        document.Edit("Synthetic declared asset metadata",d=>d.Assets.Add(new(){Path="textures/declared.png",Kind="texture",Name="Declared"}));
        snapshot=resources.CaptureResources();var asset=snapshot.Assets.Single(a=>a.Path=="textures/declared.png");
        material=Ticket(snapshot,index) with {AssetPath=asset.Path,AssetFingerprint=asset.Fingerprint};
        check(resources.ExecuteResource(material,NativeStudioResourceOperation.RenameAsset,new("Native name")).Success,
            "production asset rename retains declared file path and owner");
        snapshot=resources.CaptureResources();asset=snapshot.Assets.Single(a=>a.Path==asset.Path);material=Ticket(snapshot,index) with {AssetPath=asset.Path,AssetFingerprint=asset.Fingerprint};
        check(resources.ExecuteResource(material,NativeStudioResourceOperation.BindAssetChannel,new(Channel:NativeStudioMaterialChannel.Albedo)).Success,
            "production declared texture binds a known HD material channel without importing bytes");
        snapshot=resources.CaptureResources();asset=snapshot.Assets.Single(a=>a.Path==asset.Path);material=Ticket(snapshot,index) with {AssetPath=asset.Path,AssetFingerprint=asset.Fingerprint};
        check(asset.Usages==1&&resources.PrepareResourceDelete(material,asset:true)==null,
            "production owner refuses removal of a referenced declared asset");
        check(resources.ExecuteResource(material,NativeStudioResourceOperation.ClearChannel,new(Channel:NativeStudioMaterialChannel.Albedo)).Success,
            "production channel clear uses existing material writer");
        snapshot=resources.CaptureResources();asset=snapshot.Assets.Single(a=>a.Path==asset.Path);material=Ticket(snapshot,index) with {AssetPath=asset.Path,AssetFingerprint=asset.Fingerprint};
        var remove=resources.PrepareResourceDelete(material,asset:true);
        check(remove!=null&&resources.ConfirmResourceDelete(remove).Success&&!document.Project.Definition.Assets.Any(a=>a.Path==asset.Path),
            "production unused asset confirmation removes only its project reference");
        check(session.Execute(session.Capture().Ticket,NativeStudioEditorOperation.Undo,default).Success&&document.Project.Definition.Assets.Any(a=>a.Path==asset.Path),
            "existing history restores removed asset declaration exactly");
        check(ReferenceEquals(document,Field<MapDocument>(owner,"_document"))&&ReferenceEquals(history,document.History)
            &&ReferenceEquals(writer,Field<MapAutosaveService>(owner,"_autosave"))&&ReferenceEquals(watcher,Field<MapSourceWatchService>(owner,"_sourceWatch")),
            "all resource commands retain exact document/history/writer/source-watch owners");
        snapshot=resources.CaptureResources();material=Ticket(snapshot,index);
        check(session.TryClose().Success&&!resources.ApplyMaterial(material,new Dictionary<string,string>{{"name","RETIRED"}}).Success,
            "retired production resource lease cannot mutate retained document");
        check(Field<object?>(Field<MapViewport>(owner,"_viewport"),"_renderer")==null,"production resource authority fixture creates no Scene or GL resources");
    }
    private static NativeStudioResourceTicket Empty(NativeStudioResourcesSnapshot snapshot)=>new(snapshot.Owner,-1,Guid.Empty,"","","",snapshot.SourceNotices);
    private static NativeStudioResourceTicket Ticket(NativeStudioResourcesSnapshot snapshot,int index)
    {var row=snapshot.Materials.Single(m=>m.Index==index);return new(snapshot.Owner,row.Index,row.Id,row.Fingerprint,"","",snapshot.SourceNotices);}
    private static T Field<T>(object instance,string name)=>(T)instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(instance)!;
}
