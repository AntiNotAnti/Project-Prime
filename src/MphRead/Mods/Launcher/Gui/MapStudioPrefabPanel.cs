using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapStudioScreen
{
    private Task InsertPrefabAsync(string path,MapTransform? transform=null)=>Job("Preparing prefab insertion",async token=>
    {
        if(_document is not {} document)return;
        string root=document.Project.Definition.BaseDirectory??_services.MapLibraryDirectory;
        using var prepared=await document.PreparePrefabInsertAsync(path,root,transform,token);
        GuardJob(token);
        var result=document.ApplyPreparedPrefabEdit(prepared,token);
        _studioState.RecentPrefabs.RemoveAll(item=>item.Equals(path,StringComparison.OrdinalIgnoreCase));
        _studioState.RecentPrefabs.Insert(0,path);
        if(_studioState.RecentPrefabs.Count>12)_studioState.RecentPrefabs.RemoveRange(12,_studioState.RecentPrefabs.Count-12);
        MapStudioStateStore.Save(document.Project.Definition,_studioState,_services.UserMapDirectory);
        Dismiss();_viewport?.FrameSelection();PrefabInspector();
        _status.Text=$"Inserted {result.ObjectIds.Count} prefab objects · one undo step";
    });
    private Task UpdatePrefabAsync(Guid id)=>Job("Preparing prefab update",async token=>
    {
        if(_document is not {} document)return;
        string root=document.Project.Definition.BaseDirectory??_services.MapLibraryDirectory;
        using var prepared=await document.PreparePrefabUpdateAsync(id,root,token);
        GuardJob(token);
        var result=document.ApplyPreparedPrefabEdit(prepared,token);
        PrefabInspector();_status.Text=$"Updated prefab · {result.ObjectIds.Count} objects · {result.PreservedOverrides} overrides preserved";
    });
    private void PrefabInspector()
    {
        _inspector.Children.Clear();if(_document==null)return;
        _inspector.Children.Add(Text("LINKED PREFABS"));
        AddButton(_inspector,"Save selection as prefab",SavePrefab);AddButton(_inspector,"Insert prefab",InsertPrefab);
        _inspector.Children.Add(Text("Instances keep stable source identities, revisions and local overrides. Runtime packages contain resolved objects and do not need the prefab source files."));
        foreach(var instance in _document.Project.Definition.PrefabInstances.ToArray())
        {
            _inspector.Children.Add(Text(System.IO.Path.GetFileName(instance.SourcePath)+" · "+instance.Members.Count+" members"));
            _inspector.Children.Add(Text("Source ID · "+instance.SourceId+"\nRevision · "+instance.SourceRevision));
            int edits=instance.Members.Count(member=>member.Overrides!=null||member.Deleted)+instance.Materials.Count(material=>material.Overrides!=null);
            _inspector.Children.Add(Text(edits+" retained overrides"));
            AddButton(_inspector,"Select instance",()=> { _document.Selection.Clear();foreach(var member in instance.Members.Where(member=>!member.Deleted))_document.Selection.Add(member.ObjectId);_document.ActiveObjectId=_document.Selection.FirstOrDefault();_document.SelectionChanged();_viewport?.FrameSelection(); });
            var position=new TextBox {Text=string.Join(",",instance.Transform.Position)};
            var rotation=new TextBox {Text=string.Join(",",instance.Transform.Rotation)};
            var scale=new TextBox {Text=string.Join(",",instance.Transform.Scale)};
            _inspector.Children.Add(Text("Position X,Y,Z"));_inspector.Children.Add(position);
            _inspector.Children.Add(Text("Rotation quaternion X,Y,Z,W"));_inspector.Children.Add(rotation);
            _inspector.Children.Add(Text("Scale X,Y,Z"));_inspector.Children.Add(scale);
            AddButton(_inspector,"Apply instance transform",()=> {try {_document.SetPrefabTransform(instance.Id,new MapTransform {Position=ParseVector(position.Text??"",3),Rotation=ParseVector(rotation.Text??"",4),Scale=ParseVector(scale.Text??"",3)});PrefabInspector();}catch(Exception ex){Failure(ex);} });
            AddButton(_inspector,"Update from source",()=>_=UpdatePrefabAsync(instance.Id));
            AddButton(_inspector,"Detach instance",()=> {try {_document.DetachPrefab(instance.Id);PrefabInspector();_status.Text="Prefab detached. Geometry and gameplay objects remain editable.";}catch(Exception ex){Failure(ex);} });
        }
    }
}
