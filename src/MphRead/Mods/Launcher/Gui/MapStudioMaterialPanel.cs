using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MphRead.Mods.MapEditor;
using MphRead.AvaloniaShared;
using MphRead.Mods.MapGen;
using MphRead.Mods.Render;

namespace MphRead.Mods.Launcher.Gui
{
    internal sealed partial class MapStudioScreen
    {
        private sealed record MaterialTarget(string Label,int Index,bool Source)
        {
            public override string ToString()=>Label;
        }

        private void MaterialInspector()
        {
            ClearAnimatedMaterialPreviews();
            _inspector.Children.Clear();if(_document==null)return;
            _inspector.Children.Add(Text("MATERIAL BROWSER"));
            FaceUvControls(_inspector);
            var checker = new CheckBox { Content = "UV checker (preview only)", IsChecked = _viewport?.UvChecker == true };
            checker.IsCheckedChanged += (_, _) => { if (_viewport != null) { _viewport.UvChecker = checker.IsChecked == true; _viewport.InvalidateVisual(); } };
            _inspector.Children.Add(checker);
            var definition=_document.Project.Definition;

            _inspector.Children.Add(Text("EYEDROPPER & REPLACE ALL"));
            if(_pickedMaterialHit is { } picked)
            {
                int sourceSlot=picked.SourceMaterial>=0?picked.SourceMaterial:picked.Material;
                _inspector.Children.Add(Text(picked.ObjectId==Guid.Empty
                    ?$"Picked source surface · source slot {sourceSlot} · runtime material {picked.Material}"
                    :$"Picked authored surface · material {picked.Material}"));
            }
            else _inspector.Children.Add(Text("No surface material picked yet."));
            AddButton(_inspector,"Eyedropper · click surface",()=>
            {
                if(_viewport==null)return;
                _viewport.MaterialEyedropper=true;
                _status.Text="Material eyedropper active · click any rendered surface.";
            });

            var targets=new List<MaterialTarget>();
            if(definition.Import is {} imported)
            {
                try
                {
                    MapTexturePack? pack=imported.LoadTexturePack();
                    if(pack!=null)
                        targets.AddRange(pack.Entries.Select((entry,index)=>
                            new MaterialTarget($"Source {index} · {entry.Name}",index,true)));
                }
                catch(Exception ex) when(ex is IOException or InvalidDataException or ProgramException)
                { _inspector.Children.Add(Text("Source material list unavailable: "+ex.Message)); }
            }
            targets.AddRange(definition.Materials.Select((material,index)=>
                new MaterialTarget($"Authored {index} · {material.Name}",index,false)));
            var replacementTarget=new ComboBox{ItemsSource=targets,SelectedIndex=targets.Count>0?0:-1};
            _inspector.Children.Add(Text("Replacement material"));_inspector.Children.Add(replacementTarget);
            AddButton(_inspector,"Replace all uses",()=>
            {
                if(_pickedMaterialHit is not {} hit||replacementTarget.SelectedItem is not MaterialTarget target)
                { _status.Text="Pick a source surface and replacement material first.";return; }
                try
                {
                    _document.Edit("Replace all material uses",d=>
                    {
                        if(hit.ObjectId!=Guid.Empty)
                        {
                            if(target.Source)throw new InvalidOperationException("Authored geometry must target an authored material.");
                            int source=hit.Material;
                            foreach(MapGeometry geometry in d.Geometry)
                            {
                                if(geometry.Material==source)geometry.Material=target.Index;
                                if(geometry is MapMesh mesh)
                                    for(int i=0;i<mesh.FaceMaterials.Count;i++)
                                        if(mesh.FaceMaterials[i]==source)mesh.FaceMaterials[i]=target.Index;
                            }
                            foreach(MapBrush brush in d.Brushes)
                                if(brush.Material==source)brush.Material=target.Index;
                        }
                        else if(d.Import is {} import)
                        {
                            if(hit.SourceMaterial>=0)
                            {
                                int source=hit.SourceMaterial;
                                import.MaterialReplacements.RemoveAll(value=>value.Source==source);
                                import.MaterialReplacements.Add(new()
                                {
                                    Source=source,Target=target.Index,TargetSource=target.Source
                                });
                            }
                            else
                            {
                                if(target.Source)throw new InvalidOperationException("Borrowed-material imports must target an authored material.");
                                int source=hit.Material;
                                if(import.DefaultMaterial==source)import.DefaultMaterial=target.Index;
                                foreach(string shader in import.ShaderMaterials.Keys.ToArray())
                                    if(import.ShaderMaterials[shader]==source)import.ShaderMaterials[shader]=target.Index;
                            }
                        }
                        else if(d.NativeRoom is {} native)
                        {
                            int source=hit.SourceMaterial>=0?hit.SourceMaterial:hit.Material;
                            if(target.Source)throw new InvalidOperationException("Native remix replacements use Map Studio material slots.");
                            native.MaterialReplacements.RemoveAll(value=>value.Source==source);
                            native.MaterialReplacements.Add(new(){Source=source,Target=target.Index});
                        }
                    },MapChangeDomain.Material|MapChangeDomain.Geometry|MapChangeDomain.Import);
                    _status.Text="Material replacement applied across the map.";
                    _=Validate();MaterialInspector();
                }
                catch(Exception ex){Failure(ex);}
            });
            AddButton(_inspector,"Clear picked source override",()=>
            {
                if(_pickedMaterialHit is not MapPickHit hit||hit.ObjectId!=Guid.Empty)return;
                int source=hit.SourceMaterial>=0?hit.SourceMaterial:hit.Material;
                _document.Edit("Clear material replacement",d=>
                {
                    d.Import?.MaterialReplacements.RemoveAll(value=>value.Source==source);
                    d.NativeRoom?.MaterialReplacements.RemoveAll(value=>value.Source==source);
                },MapChangeDomain.Material|MapChangeDomain.Import);
                _status.Text=$"Cleared source material {source} replacement.";_=Validate();MaterialInspector();
            });

            var filter=new TextBox{PlaceholderText="Search materials"};_inspector.Children.Add(filter);
            var panels=new List<(Control Panel,string Search)>();
            var order=Enumerable.Range(0,definition.Materials.Count)
                .OrderByDescending(i=>_studioState.FavoriteMaterials.Contains(MaterialKey(definition.Materials[i]),StringComparer.OrdinalIgnoreCase))
                .ThenBy(i=>definition.Materials[i].Name,StringComparer.OrdinalIgnoreCase).ToArray();
            foreach(int i in order)
            {
                int index=i;var m=definition.Materials[i];
                var usageIds=MapMaterialEditing.Usages(definition,index).ToHashSet();
                int uses=usageIds.Count;
                int modelUses=definition.ModelSources.Count(source=>source.Objects.Any(o=>usageIds.Contains(o.Id)));
                int faceUses=definition.Geometry.OfType<MapMesh>().Sum(mesh=>Enumerable.Range(0,mesh.Faces.Count).Count(f=>(f<mesh.FaceMaterials.Count?mesh.FaceMaterials[f]:mesh.Material)==index));
                bool favorite=_studioState.FavoriteMaterials.Contains(MaterialKey(m),StringComparer.OrdinalIgnoreCase);
                var panel=new StackPanel{Spacing=4,Margin=new Thickness(0,4,0,8)};
                panel.Children.Add(Text($"{(favorite?"★ ":"")}{index} · {m.Name} · {uses} objects · {faceUses} mesh faces · {modelUses} imported models"));
                try
                {
                    if(m.Texture!=null||_services.GameFilesReady)
                    {
                        string key=PreviewCacheKey(definition,m);
                        if(!_materialPreviewCache.TryGetValue(key,out var preview))
                        {preview=MapMaterialPreview.Create(definition,m);_materialPreviewCache[key]=preview;}
                        panel.Children.Add(new Image{Source=preview.Bitmap,Width=72,Height=72,HorizontalAlignment=HorizontalAlignment.Left});
                        panel.Children.Add(Text(preview.Details));
                    }
                }
                catch(Exception ex)when(ex is IOException or InvalidDataException or ProgramException or ArgumentException or InvalidOperationException)
                {panel.Children.Add(Text("Preview unavailable: "+ex.Message));}
                var source=new TextBox{Text=m.SourceMaterial.ToString(CultureInfo.InvariantCulture)};
                var scale=new TextBox{Text=m.TexScale.ToString(CultureInfo.InvariantCulture)};
                panel.Children.Add(Text("Source material / texels per unit"));panel.Children.Add(source);panel.Children.Add(scale);
                AddButton(panel,"Select usages",()=>
                {
                    _document.Selection.Clear();
                    foreach (Guid id in MapMaterialEditing.Usages(_document.Project.Definition,index)) _document.Selection.Add(id);
                    _document.SelectionChanged(); RefreshHierarchy(); _viewport?.FrameSelection();
                });
                AddButton(panel,"Isolate usages",()=>
                {
                    _document.Selection.Clear();
                    foreach (Guid id in MapMaterialEditing.Usages(_document.Project.Definition,index)) _document.Selection.Add(id);
                    _document.SelectionChanged(); _document.IsolateSelection(); _viewport?.FrameSelection();
                });
                AddButton(panel,"Replace usages with selected replacement",()=>
                {
                    if(replacementTarget.SelectedItem is not MaterialTarget target || target.Source)
                    { _status.Text="Choose an authored replacement material above."; return; }
                    _document.Edit("Replace material usages",d=>MapMaterialEditing.Replace(d,index,target.Index),MapChangeDomain.Geometry|MapChangeDomain.Material|MapChangeDomain.Import);
                    MaterialInspector();
                });
                AddButton(panel,"Delete if unused",()=>
                {
                    try { _document.Edit("Delete unused material",d=>MapMaterialEditing.DeleteUnused(d,index),MapChangeDomain.Geometry|MapChangeDomain.Material|MapChangeDomain.Import); MaterialInspector(); }
                    catch(Exception ex) { Failure(ex); }
                });
                AddButton(panel,"Paint this material · P",()=>
                {
                    if (_viewport != null) { _viewport.ActivePaintMaterial = index; _viewport.MaterialPaint = true; }
                    _status.Text = "Click a mesh face to paint · Shift: connected faces · Ctrl: sample · Alt: base material · P: exit.";
                });
                AddButton(panel,"Assign to selection",()=>
                {
                    if(_document.Selection.Count==0){_status.Text="Select authored geometry first.";return;}
                    var ids=_document.Selection.ToHashSet();
                    if (_viewport is { ElementMode: "Face" } viewport && viewport.SelectedFaceIndices.Count > 0)
                    {
                        int[] faces = viewport.SelectedFaceIndices.ToArray();
                        Guid objectId = viewport.SelectedFaceObjectId;
                        _document.EditObjects("Assign face material", new[] { objectId }, d =>
                        {
                            if (MapObjects.Find(d, objectId)?.Value is MapMesh target)
                                MapMeshEditing.AssignMaterial(target, faces, index);
                        });
                        return;
                    }
                    _document.EditObjects("Assign material",ids,d=>
                    {
                        foreach(var g in d.Geometry)
                        {
                            g.Material=index;
                            if(g is MapMesh mesh)
                                for(int i=0;i<mesh.FaceMaterials.Count;i++)mesh.FaceMaterials[i]=index;
                        }
                        foreach(var b in d.Brushes)b.Material=index;
                    });
                });
                AddButton(panel,favorite?"Unfavorite":"Favorite",()=>
                {
                    string key=MaterialKey(m);
                    _studioState.FavoriteMaterials.RemoveAll(x=>x.Equals(key,StringComparison.OrdinalIgnoreCase));
                    if(!favorite)_studioState.FavoriteMaterials.Add(key);
                    MapStudioStateStore.Save(definition,_studioState,_services.UserMapDirectory);MaterialInspector();
                });
                AddButton(panel,"Apply material",()=>{try{_document.EditMaterial(index,value=>{value.SourceMaterial=int.Parse(source.Text??"",CultureInfo.InvariantCulture);value.TexScale=Number(scale.Text??"");});}catch(Exception ex){Failure(ex);}});
                if(m.Texture==null&&_services.GameFilesReady)
                {
                    try
                    {
                        var materials=Read.GetRoomModelInstance(definition.TextureSource).Model.Materials;
                        var choices=new ComboBox{ItemsSource=materials.Select((material,n)=>$"{n} · {material.Name}").ToArray(),SelectedIndex=m.SourceMaterial};
                        panel.Children.Add(choices);
                        choices.SelectionChanged+=(_,_)=>{if(choices.SelectedIndex>=0)source.Text=choices.SelectedIndex.ToString(CultureInfo.InvariantCulture);};
                    }
                    catch(Exception ex){panel.Children.Add(Text("Source materials unavailable: "+ex.Message));}
                }
                AnimatedMaterialControls(panel, definition, m, index);
                EnhancedMaterialControls(panel, definition, m);
                _inspector.Children.Add(panel);
                panels.Add((panel,$"{index} {m.Name} {m.Texture} {m.SourceMaterial}"));
            }
            filter.TextChanged+=(_,_)=>
            {
                string q=(filter.Text??"").Trim();
                foreach(var item in panels)item.Panel.IsVisible=q.Length==0||item.Search.Contains(q,StringComparison.OrdinalIgnoreCase);
            };
            AddButton(_inspector,"Add material",()=>_document.Edit("Add material",d=>d.Materials.Add(new(){Id=Guid.NewGuid(),Name="Material "+d.Materials.Count})));
        }

        private static string MaterialKey(MapMaterial material)
            => material.Id==Guid.Empty?material.Name:material.Id.ToString("N");

    }
}
