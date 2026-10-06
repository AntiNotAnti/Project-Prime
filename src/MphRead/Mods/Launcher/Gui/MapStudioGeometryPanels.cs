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
        private void LayerInspector()
        {
            _inspector.Children.Clear(); if(_document==null)return;
            _inspector.Children.Add(Text("LAYERS"));
            var layers=_document.Project.Definition.Geometry.Select(g=>String.IsNullOrWhiteSpace(g.Layer)?"Architecture":g.Layer)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray();
            if(layers.Length==0)_inspector.Children.Add(Text("No authored geometry layers yet."));
            foreach(string layer in layers)
            {
                string current=layer;
                var members=_document.Project.Definition.Geometry.Where(g=>g.Layer.Equals(current,StringComparison.OrdinalIgnoreCase)).ToArray();
                _inspector.Children.Add(Text($"{current} · {members.Length} objects · {members.Count(g=>g.Hidden)} hidden · {members.Count(g=>g.Locked)} locked"));
                AddButton(_inspector,"Show "+current,()=>{_document.SetLayerState(current,hidden:false);LayerInspector();});
                AddButton(_inspector,"Hide "+current,()=>{_document.SetLayerState(current,hidden:true);LayerInspector();});
                AddButton(_inspector,"Unlock "+current,()=>{_document.SetLayerState(current,locked:false);LayerInspector();});
                AddButton(_inspector,"Lock "+current,()=>{_document.SetLayerState(current,locked:true);LayerInspector();});
            }
            var layerName=new TextBox{Text="Gameplay"};_inspector.Children.Add(Text("Assign selected geometry to layer"));_inspector.Children.Add(layerName);
            AddButton(_inspector,"Assign layer",()=>{
                string value=String.IsNullOrWhiteSpace(layerName.Text)?"Architecture":layerName.Text.Trim();
                var ids=_document.Selection.ToHashSet();
                _document.EditObjects("Assign layer",ids,d=>{foreach(var g in d.Geometry)g.Layer=value;});
                LayerInspector();
            });
            AddButton(_inspector,"Isolate selection",()=>{_document.IsolateSelection();LayerInspector();});
            AddButton(_inspector,"Show all geometry",()=>{_document.ShowAllGeometry();LayerInspector();});
        }

        private static bool? CommonBool<T>(IReadOnlyList<T> values,Func<T,bool> read)
        {
            if(values.Count==0)return null;bool first=read(values[0]);
            return values.All(value=>read(value)==first)?first:null;
        }

        private void MultiInspect(MapObject[] selection)
        {
            _inspector.Children.Clear();if(_document==null)return;
            _inspector.Children.Add(Text($"MULTI-OBJECT INSPECTOR · {selection.Length} selected"));
            string types=String.Join(" · ",selection.GroupBy(o=>o.Kind).Select(g=>$"{g.Key} {g.Count()}"));
            _inspector.Children.Add(Text(types));
            Guid[] ids=selection.Select(o=>o.Id).ToArray();

            MapGeometry[] geometry=selection.Select(o=>o.Value).OfType<MapGeometry>().ToArray();
            ComboBox? material=null,team=null;
            TextBox? layer=null,terrain=null;
            CheckBox? applyLayer=null,applyTerrain=null,solid=null,damaging=null,hidden=null,locked=null;

            if(geometry.Length>0)
            {
                _inspector.Children.Add(Text($"GEOMETRY · {geometry.Length}"));
                string[] materials=new[]{"No change"}.Concat(_document.Project.Definition.Materials
                    .Select((m,i)=>$"{i} · {m.Name}")).ToArray();
                int commonMaterial=geometry.Select(g=>g.Material).Distinct().Count()==1?geometry[0].Material+1:0;
                material=new ComboBox{ItemsSource=materials,SelectedIndex=Math.Clamp(commonMaterial,0,materials.Length-1)};
                _inspector.Children.Add(Text("Material"));_inspector.Children.Add(material);

                string commonLayer=geometry.Select(g=>g.Layer).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1
                    ?geometry[0].Layer:"";
                applyLayer=new CheckBox{Content="Apply layer",IsChecked=false};
                layer=new TextBox{Text=commonLayer,PlaceholderText="Layer name"};
                _inspector.Children.Add(applyLayer);_inspector.Children.Add(layer);

                string commonTerrain=geometry.Select(g=>g.Terrain).Distinct(StringComparer.OrdinalIgnoreCase).Count()==1
                    ?geometry[0].Terrain:"";
                applyTerrain=new CheckBox{Content="Apply terrain",IsChecked=false};
                terrain=new TextBox{Text=commonTerrain,PlaceholderText="Metal"};
                _inspector.Children.Add(applyTerrain);_inspector.Children.Add(terrain);

                CheckBox Tri(string label,Func<MapGeometry,bool> read)
                {
                    var check=new CheckBox{Content=label,IsThreeState=true,IsChecked=CommonBool(geometry,read)};
                    _inspector.Children.Add(check);return check;
                }
                solid=Tri("Collision",g=>g.Solid);
                damaging=Tri("Damaging",g=>g.Damaging);
                hidden=Tri("Hidden",g=>g.Hidden);
                locked=Tri("Locked",g=>g.Locked);
            }

            MapSpawn[] spawns=selection.Select(o=>o.Value).OfType<MapSpawn>().ToArray();
            if(spawns.Length>0)
            {
                _inspector.Children.Add(Text($"SPAWNS · {spawns.Length}"));
                int? common=spawns.Select(s=>s.Team).Distinct().Count()==1?spawns[0].Team:null;
                team=new ComboBox
                {
                    ItemsSource=new[]{"No change","Neutral (-1)","Team A (0)","Team B (1)","Team C (2)","Team D (3)"},
                    SelectedIndex=common.HasValue?Math.Clamp(common.Value+2,1,5):0
                };
                _inspector.Children.Add(team);
            }

            AddButton(_inspector,"Apply to selection",()=>
            {
                try
                {
                    _document.EditObjects("Edit multiple objects",ids,d=>
                    {
                        foreach(MapObject item in MapObjects.All(d).Where(o=>ids.Contains(o.Id)))
                        {
                            if(item.Value is MapGeometry g)
                            {
                                if(material is {SelectedIndex:>0})
                                {
                                    int target=material.SelectedIndex-1;g.Material=target;
                                    if(g is MapMesh mesh)
                                        for(int i=0;i<mesh.FaceMaterials.Count;i++)mesh.FaceMaterials[i]=target;
                                }
                                if(applyLayer?.IsChecked==true)g.Layer=String.IsNullOrWhiteSpace(layer?.Text)?"Architecture":layer!.Text!.Trim();
                                if(applyTerrain?.IsChecked==true&&!String.IsNullOrWhiteSpace(terrain?.Text))g.Terrain=terrain!.Text!.Trim();
                                if(solid?.IsChecked is bool s)g.Solid=s;
                                if(damaging?.IsChecked is bool damage)g.Damaging=damage;
                                if(hidden?.IsChecked is bool hide)g.Hidden=hide;
                                if(locked?.IsChecked is bool l)g.Locked=l;
                            }
                            if(item.Value is MapSpawn spawn&&team is {SelectedIndex:>0})
                                spawn.Team=team.SelectedIndex-2;
                        }
                    });
                    MultiInspect(MapObjects.All(_document.Project.Definition).Where(o=>ids.Contains(o.Id)).ToArray());
                }
                catch(Exception ex){Failure(ex);}
            });
        }

        private void Inspect()
        {
            _inspector.Children.Clear();if(_document==null)return;
            var selectedObjects=MapObjects.All(_document.Project.Definition).Where(o=>_document.Selection.Contains(o.Id)).ToArray();
            if(selectedObjects.Length>1){MultiInspect(selectedObjects);return;}
            var selected=selectedObjects.FirstOrDefault();
            if(selected==null){EnvironmentInspector();return;}
            _inspector.Children.Add(Text(selected.Kind));Guid id=selected.Id;
            var edits=new List<Action<object>>();
            void Field(string label,object? value,Action<object,string> apply)
            { _inspector.Children.Add(Text(label));var input=new TextBox {Text=Convert.ToString(value,CultureInfo.InvariantCulture)};_inspector.Children.Add(input);edits.Add(o=>apply(o,input.Text??"")); }
            void Vec(string label,float[] values,Action<object,float[]> apply)
            {Field(label,string.Join(", ",values.Select(v=>v.ToString(CultureInfo.InvariantCulture))),(o,text)=>apply(o,ParseVector(text,values.Length)));}
            void Material(int selectedIndex,Action<object,int> apply)
            {
                _inspector.Children.Add(Text("Material"));var choice=new ComboBox {ItemsSource=_document.Project.Definition.Materials.Select((m,i)=>$"{i} · {m.Name}").ToArray(),SelectedIndex=selectedIndex};_inspector.Children.Add(choice);edits.Add(o=>apply(o,choice.SelectedIndex));
            }
            if(selected.Value is not MapBrush)Vec("Position (X, Y, Z)",selected.Position,(o,v)=>{var current=MapObjects.All(_document.Project.Definition).First(x=>x.Id==id).Position;new MapObject(id,"","",o,_=>{}).Move(v.Zip(current,(a,b)=>a-b).ToArray());});
            if(selected.Value is MapEntityDefinition entity)Field("Label",entity.Label,(o,value)=>((MapEntityDefinition)o).Label=value);
            switch(selected.Value)
            {
                case MapGeometry g:
                    Field("Label",g.Label,(o,s)=>((MapGeometry)o).Label=s);
                    Field("Layer",g.Layer,(o,s)=>((MapGeometry)o).Layer=s);
                    Vec("Size",g.Transform.Scale,(o,v)=>((MapGeometry)o).Transform.Scale=v);
                    var rotation=new OpenTK.Mathematics.Quaternion(g.Transform.Rotation[0],g.Transform.Rotation[1],g.Transform.Rotation[2],g.Transform.Rotation[3]).ToEulerAngles()* (180/MathF.PI);
                    Vec("Rotation X, Y, Z (degrees)",new[]{rotation.X,rotation.Y,rotation.Z},(o,v)=>{var q=OpenTK.Mathematics.Quaternion.FromEulerAngles(new OpenTK.Mathematics.Vector3(v[0],v[1],v[2])*(MathF.PI/180));((MapGeometry)o).Transform.Rotation=new[]{q.X,q.Y,q.Z,q.W};});
                    Material(g.Material,(o,index)=>((MapGeometry)o).Material=index);
                    Field("Shade",g.Shade,(o,s)=>((MapGeometry)o).Shade=Number(s));
                    Field("Terrain",g.Terrain,(o,s)=>((MapGeometry)o).Terrain=s);
                    Vec("UV scale",g.Uv.Scale,(o,v)=>((MapGeometry)o).Uv.Scale=v);Vec("UV offset",g.Uv.Offset,(o,v)=>((MapGeometry)o).Uv.Offset=v);
                    Field("UV rotation",g.Uv.Rotation,(o,s)=>((MapGeometry)o).Uv.Rotation=Number(s));
                    foreach(var pair in new[]{("Collision",g.Solid),("Damaging",g.Damaging),("Hidden",g.Hidden),("Locked",g.Locked)})
                    {var check=new CheckBox {Content=pair.Item1,IsChecked=pair.Item2};_inspector.Children.Add(check);edits.Add(o=>{var geometry=(MapGeometry)o;switch(pair.Item1){case "Collision":geometry.Solid=check.IsChecked==true;break;case "Damaging":geometry.Damaging=check.IsChecked==true;break;case "Hidden":geometry.Hidden=check.IsChecked==true;break;case "Locked":geometry.Locked=check.IsChecked==true;break;}});}
                    if(g is MapMesh collisionMesh)
                    {
                        _inspector.Children.Add(Text("COLLISION CHANNELS"));
                        foreach(var pair in new[]{
                            ("Collision only (invisible in play)",collisionMesh.CollisionOnly),
                            ("Reflect beams",collisionMesh.ReflectBeams),
                            ("Ignore players + camera",collisionMesh.IgnorePlayers),
                            ("Ignore beams / projectiles",collisionMesh.IgnoreBeams),
                            ("Ignore scan",collisionMesh.IgnoreScan)})
                        {
                            var check=new CheckBox{Content=pair.Item1,IsChecked=pair.Item2};_inspector.Children.Add(check);
                            edits.Add(o=>{var mesh=(MapMesh)o;switch(pair.Item1)
                            {
                                case "Collision only (invisible in play)":mesh.CollisionOnly=check.IsChecked==true;mesh.Solid|=mesh.CollisionOnly;break;
                                case "Reflect beams":mesh.ReflectBeams=check.IsChecked==true;break;
                                case "Ignore players + camera":mesh.IgnorePlayers=check.IsChecked==true;break;
                                case "Ignore beams / projectiles":mesh.IgnoreBeams=check.IsChecked==true;break;
                                case "Ignore scan":mesh.IgnoreScan=check.IsChecked==true;break;
                            }});
                        }
                        Field("Slipperiness",collisionMesh.Slipperiness,(o,s)=>((MapMesh)o).Slipperiness=int.Parse(s,CultureInfo.InvariantCulture));
                        _inspector.Children.Add(Text("Camera collision currently follows the player collision channel in the MPH runtime format."));
                    }
                    else
                    {
                        AddButton(_inspector,"Convert to collision proxy",()=>
                        {
                            _document.EditObjects("Convert to collision proxy",new[]{id},d=>
                            {
                                int index=d.Geometry.FindIndex(item=>item.Id==id);
                                if(index<0||d.Geometry[index] is MapMesh)return;
                                MapGeometry source=d.Geometry[index];
                                var mesh=MapMeshEditing.Convert(source,d.Materials[source.Material].TexScale);
                                mesh.CollisionOnly=true;mesh.Solid=true;mesh.Layer="Collision";
                                d.Geometry[index]=mesh;
                            });
                            Inspect();
                        });
                    }
                    if(g is MapPrism prism)Field("Sides",prism.Sides,(o,s)=>((MapPrism)o).Sides=int.Parse(s,CultureInfo.InvariantCulture));
                    break;
                case MapSpawn s:Field("Yaw",s.Yaw,(o,v)=>((MapSpawn)o).Yaw=Number(v));Field("Team (-1 = neutral)",s.Team,(o,v)=>((MapSpawn)o).Team=int.Parse(v,CultureInfo.InvariantCulture));break;
                case MapNavigationLink link:
                    Vec("Destination",link.To,(o,v)=>((MapNavigationLink)o).To=v);
                    Field("Traversal type",link.Kind,(o,v)=>((MapNavigationLink)o).Kind=Enum.Parse<MapNavigationLinkKind>(v,true));
                    Field("From node type",link.FromNodeKind,(o,v)=>((MapNavigationLink)o).FromNodeKind=Enum.Parse<MapNavigationAnchorKind>(v,true));
                    Field("To node type",link.ToNodeKind,(o,v)=>((MapNavigationLink)o).ToNodeKind=Enum.Parse<MapNavigationAnchorKind>(v,true));
                    _inspector.Children.Add(Text("Node type Auto preserves geometry-derived hazard/vantage semantics and applies safe traversal defaults."));
                    var both=new CheckBox {Content="Bidirectional",IsChecked=link.Bidirectional};_inspector.Children.Add(both);edits.Add(o=>((MapNavigationLink)o).Bidirectional=both.IsChecked==true);break;
                case MapItem i:
                    var itemType=new ComboBox {ItemsSource=MapBuilder.MultiplayerItems.Select(t=>t.ToString()).Order().ToArray(),SelectedItem=i.Type};_inspector.Children.Add(itemType);edits.Add(o=>((MapItem)o).Type=itemType.SelectedItem as string??i.Type);
                    Field("Respawn frames",i.SpawnInterval,(o,v)=>((MapItem)o).SpawnInterval=ushort.Parse(v,CultureInfo.InvariantCulture));
                    var hasBase=new CheckBox {Content="Has base",IsChecked=i.HasBase};_inspector.Children.Add(hasBase);edits.Add(o=>((MapItem)o).HasBase=hasBase.IsChecked==true);break;
                case MapJumpPad p:
                    Vec("Trigger size",p.Size,(o,v)=>((MapJumpPad)o).Size=v);
                    var launchMode=new ComboBox {ItemsSource=new[]{"Target","Vector and speed"},SelectedIndex=p.Vector==null?0:1};_inspector.Children.Add(launchMode);
                    Vec("Target",p.Target??new[]{0f,4,0},(o,v)=>((MapJumpPad)o).Target=launchMode.SelectedIndex==0?v:null);
                    Vec("Direction",p.Vector??new[]{0f,1,0},(o,v)=>((MapJumpPad)o).Vector=launchMode.SelectedIndex==1?v:null);
                    Field("Speed",p.Speed,(o,v)=>((MapJumpPad)o).Speed=Number(v));
                    Field("Control lock",p.ControlLockTime,(o,v)=>((MapJumpPad)o).ControlLockTime=ushort.Parse(v,CultureInfo.InvariantCulture));
                    Field("Cooldown",p.CooldownTime,(o,v)=>((MapJumpPad)o).CooldownTime=ushort.Parse(v,CultureInfo.InvariantCulture));break;
                case MapBrush b:Vec("Minimum",b.Min,(o,v)=>((MapBrush)o).Min=v);Vec("Maximum",b.Max,(o,v)=>((MapBrush)o).Max=v);Material(b.Material,(o,index)=>((MapBrush)o).Material=index);break;
            }
            AddButton(_inspector,"Apply",()=>{try{_document.EditObjects("Edit properties",new[]{id},d=>{var target=MapObjects.All(d).First(o=>o.Id==id).Value;foreach(var edit in edits)edit(target);});}catch(Exception ex){Failure(ex);}});
        }
        private void PartitionInspector()
        {
            _inspector.Children.Clear();if(_document==null)return;
            _inspector.Children.Add(Text("RUNTIME PARTITIONING"));
            var current=MapRuntimePartitioner.Effective(_document.Project.Definition.Partitioning);
            var enabled=new CheckBox{Content="Enable spatial render partitioning",IsChecked=current.Enabled};
            var portals=new CheckBox{Content="Generate room-part portal culling when safe",IsChecked=current.PortalCulling};
            var cell=new TextBox{Text=current.CellSize.ToString(CultureInfo.InvariantCulture)};
            var threshold=new TextBox{Text=current.FaceThreshold.ToString(CultureInfo.InvariantCulture)};
            var vertices=new TextBox{Text=current.MaxVerticesPerDisplayList.ToString(CultureInfo.InvariantCulture)};
            var margin=new TextBox{Text=current.PortalVerticalMargin.ToString(CultureInfo.InvariantCulture)};
            _inspector.Children.Add(enabled);_inspector.Children.Add(portals);
            _inspector.Children.Add(Text("Cell size (8–512)"));_inspector.Children.Add(cell);
            _inspector.Children.Add(Text("Partition after face count"));_inspector.Children.Add(threshold);
            _inspector.Children.Add(Text("Max vertices per display list"));_inspector.Children.Add(vertices);
            _inspector.Children.Add(Text("Portal vertical margin"));_inspector.Children.Add(margin);
            _inspector.Children.Add(Text($"Portal culling is capped at {MapRuntimePartitioner.MaxPortalParts} room parts. Disconnected or larger plans automatically fall back to render-only partitioning."));
            AddButton(_inspector,"Apply",()=>
            {
                try
                {
                    float size=Number(cell.Text??"");int faces=int.Parse(threshold.Text??"",CultureInfo.InvariantCulture);
                    int maxVertices=int.Parse(vertices.Text??"",CultureInfo.InvariantCulture);float portalMargin=Number(margin.Text??"");
                    if(size is <8 or >512||faces is <256 or >1_000_000||maxVertices is <1024 or >65000||portalMargin is <0 or >64)
                        throw new FormatException("Partition settings are outside their supported ranges.");
                    _document.Edit("Runtime partitioning",d=>d.Partitioning=new()
                    {
                        Enabled=enabled.IsChecked==true,PortalCulling=portals.IsChecked==true,
                        CellSize=size,FaceThreshold=faces,MaxVerticesPerDisplayList=maxVertices,
                        PortalVerticalMargin=portalMargin
                    },MapChangeDomain.Metadata|MapChangeDomain.Import);
                    if(_viewport!=null){_viewport.PartitionCellSize=size;_viewport.PartitionOverlay=true;_viewport.InvalidateVisual();}
                    _=Validate();
                }
                catch(Exception ex){Failure(ex);}
            });
            AddButton(_inspector,"Reset to automatic defaults",()=>
            {
                _document.Edit("Reset partitioning",d=>d.Partitioning=null,MapChangeDomain.Metadata|MapChangeDomain.Import);
                if(_viewport!=null){_viewport.PartitionCellSize=64;_viewport.InvalidateVisual();}
                PartitionInspector();
            });
            AddButton(_inspector,"Show partition overlay",()=>
            {
                if(_viewport==null)return;_viewport.PartitionCellSize=current.CellSize;_viewport.PartitionOverlay=true;_viewport.InvalidateVisual();
            });
            AddButton(_inspector,"Analyze runtime budgets",()=>_=Validate());
            var budgets=_document.Diagnostics.Budgets.Where(b=>b.Name.StartsWith("Render ",StringComparison.Ordinal)
                ||b.Name.StartsWith("Portal ",StringComparison.Ordinal)||b.Name=="Generated portals").ToArray();
            foreach(var budget in budgets)
                _inspector.Children.Add(Text($"{budget.Name}: {budget.Used:N0}"+(budget.Limit.HasValue?$" / {budget.Limit.Value:N0}":"")));
        }

        private void ModelingInspector()
        {
            _inspector.Children.Clear();if(_document==null)return;
            _inspector.Children.Add(Text("MODELING"));
            if(_viewport!=null)
            {
                var elementMode=new ComboBox{ItemsSource=new[]{"Object","Face","Edge","Vertex"},SelectedItem=_viewport.ElementMode};
                _inspector.Children.Add(Text("Viewport selection mode · 1/2/3/4"));_inspector.Children.Add(elementMode);
                elementMode.SelectionChanged+=(_,_)=>{if(elementMode.SelectedItem is string mode){_viewport.ElementMode=mode;_viewport.ClearSubSelection();}};
            }
            var selected=MapObjects.All(_document.Project.Definition).Where(o=>_document.Selection.Contains(o.Id)).ToArray();
            var geometry=selected.Where(o=>o.Value is MapGeometry).ToArray();
            _inspector.Children.Add(Text($"{geometry.Length} geometry objects selected"));
            AddButton(_inspector,"Convert selection to editable mesh",()=>
            {
                var ids=geometry.Select(o=>o.Id).ToHashSet();
                if(ids.Count==0){_status.Text="Select authored geometry first.";return;}
                _document.EditObjects("Convert to mesh",ids,d=>
                {
                    for(int i=0;i<d.Geometry.Count;i++)
                    {
                        MapGeometry source=d.Geometry[i];
                        if(!ids.Contains(source.Id)||source is MapMesh)continue;
                        d.Geometry[i]=MapMeshEditing.Convert(source,_document.Project.Definition.Materials[source.Material].TexScale);
                    }
                });ModelingInspector();
            });

            if(selected.FirstOrDefault(o=>o.Value is MapMesh) is {Value:MapMesh mesh} active)
            {
                _inspector.Children.Add(Text($"MESH · {mesh.Vertices.Count} vertices · {mesh.Faces.Count} faces"));
                AppendModelingEnhancements(mesh);
                if(mesh.ModifierSource!=null)return;
                int pickedFace=_viewport?.SelectedFaceIndex??-1;
                int pickedVertex=_viewport?.SelectedVertexIndex??-1;
                var face=new TextBox{Text=(pickedFace>=0?pickedFace:0).ToString(CultureInfo.InvariantCulture)};
                var amount=new TextBox{Text=".25"};
                if(_viewport?.SelectedEdge is {} edge)
                    _inspector.Children.Add(Text($"Selected edge · vertex {edge.A} ↔ {edge.B}"));
                _inspector.Children.Add(Text("Face index"));_inspector.Children.Add(face);
                _inspector.Children.Add(Text("Amount / ratio"));_inspector.Children.Add(amount);
                void FaceEdit(string label,Action<MapMesh,int,float> edit)
                {
                    try
                    {
                        int index=int.Parse(face.Text??"",CultureInfo.InvariantCulture);float value=Number(amount.Text??"");
                        var id=active.Id;
                        _document.EditObjects(label,new[]{id},d=>
                        {
                            var target=(MapMesh)MapObjects.Find(d,id)!.Value;edit(target,index,value);
                        });ModelingInspector();
                    }
                    catch(Exception ex){Failure(ex);}
                }
                if(_viewport!=null)
                {
                    _inspector.Children.Add(Text("Selected elements · amount above; bevel segments 1–8"));
                    var segments=new TextBox{Text="1"};_inspector.Children.Add(segments);
                    foreach(string command in new[]{"Extrude region","Extrude individual","Inset region","Bevel","Split edge","Dissolve edge","Collapse edge","Collapse to A","Collapse to B","Collapse to cursor","Slide edge","Merge center","Merge first","Merge last","Merge by distance","Connect vertices","Rip vertex","Slide vertex","Flatten X","Flatten Y","Flatten Z","Snap to surface","Duplicate faces","Duplicate to object","Separate","Join meshes","Select boundary","Fill","Triangulate","Dissolve triangles","Flip normals","Recalculate winding","Clean unused vertices","Select linked","Grow","Shrink"})
                        AddButton(_inspector,command,()=>{try{_viewport.RunModeling(command,Number(amount.Text??".25"),int.Parse(segments.Text??"1",CultureInfo.InvariantCulture));}catch(Exception ex){Failure(ex);}});
                    foreach(var problem in MapMeshValidator.Validate(mesh).Take(30))
                    {
                        _inspector.Children.Add(Text(problem.Severity+" · "+problem.Message));
                        if(problem.Edge is {} selectedEdge)AddButton(_inspector,"Select edge",()=>{_viewport.ElementMode="Edge";_viewport.SubSelection.Bind(mesh.Id);_viewport.SubSelection.Edges.Add(selectedEdge);_viewport.InvalidateVisual();});
                        if(problem.Face is {} selectedFace)AddButton(_inspector,"Focus face",()=>{_viewport.ElementMode="Face";_viewport.SubSelection.Bind(mesh.Id);_viewport.SubSelection.Faces.Add(selectedFace);_viewport.FocusWorld(mesh.Faces[selectedFace].Select(v=>MapMeshEditing.VertexWorld(mesh,v)));});
                    }
                }
                AddButton(_inspector,"Extrude face",()=>FaceEdit("Extrude face",(m,i,v)=>MapMeshEditing.ExtrudeFace(m,i,v)));
                AddButton(_inspector,"Inset face",()=>FaceEdit("Inset face",(m,i,v)=>MapMeshEditing.InsetFace(m,i,v)));
                AddButton(_inspector,"Bevel face",()=>FaceEdit("Bevel face",(m,i,v)=>MapMeshEditing.BevelFace(m,i,Math.Clamp(MathF.Abs(v),.01f,.9f),v*.25f)));
                AddButton(_inspector,"Subdivide face",()=>FaceEdit("Subdivide face",(m,i,_)=>{MapMeshEditing.SubdivideFace(m,i);}));
                AddButton(_inspector,"Flip face",()=>FaceEdit("Flip face",(m,i,_)=>{MapMeshEditing.FlipFace(m,i);}));
                AddButton(_inspector,"Delete face",()=>FaceEdit("Delete face",(m,i,_)=>{MapMeshEditing.DeleteFace(m,i);}));

                var vertex=new TextBox{Text=(pickedVertex>=0?pickedVertex:0).ToString(CultureInfo.InvariantCulture)};
                var delta=new TextBox{Text="0,0.25,0"};
                _inspector.Children.Add(Text("Vertex index"));_inspector.Children.Add(vertex);
                _inspector.Children.Add(Text("Vertex delta X,Y,Z"));_inspector.Children.Add(delta);
                AddButton(_inspector,"Move vertex",()=>
                {
                    try
                    {
                        int index=int.Parse(vertex.Text??"",CultureInfo.InvariantCulture);float[] v=ParseVector(delta.Text??"",3);var id=active.Id;
                        _document.EditObjects("Move mesh vertex",new[]{id},d=>MapMeshEditing.MoveVertex((MapMesh)MapObjects.Find(d,id)!.Value,index,new(v[0],v[1],v[2])));
                        ModelingInspector();
                    }
                    catch(Exception ex){Failure(ex);}
                });
                AddButton(_inspector,"Snap vertex to nearest surface",()=>
                {
                    try
                    {
                        if(_viewport==null)throw new InvalidOperationException("Viewport is unavailable.");
                        int index=int.Parse(vertex.Text??"",CultureInfo.InvariantCulture);
                        System.Numerics.Vector3 world=MapMeshEditing.VertexWorld(mesh,index);
                        var contact=MapLayoutCommands.NearestSurface(world,_viewport.Cache.SurfaceNear(world),new HashSet<Guid>{active.Id});
                        if(contact==null)throw new InvalidOperationException("No nearby collision/render surface was found.");
                        Guid id=active.Id;
                        _document.EditObjects("Snap mesh vertex to surface",new[]{id},d=>
                            MapMeshEditing.SetVertexWorld((MapMesh)MapObjects.Find(d,id)!.Value,index,contact.Value.Point));
                        _status.Text=$"Vertex {index} snapped {contact.Value.Distance:0.###} units to surface.";
                        ModelingInspector();
                    }
                    catch(Exception ex){Failure(ex);}
                });
                AddButton(_inspector,"Weld nearby vertices",()=>
                {
                    try
                    {
                        float tolerance=Math.Max(.00001f,MathF.Abs(Number(amount.Text??"")));var id=active.Id;int removed=0;
                        _document.EditObjects("Weld mesh vertices",new[]{id},d=>removed=MapMeshEditing.Weld((MapMesh)MapObjects.Find(d,id)!.Value,tolerance));
                        _status.Text=$"Welded {removed} duplicate/nearby vertices.";ModelingInspector();
                    }
                    catch(Exception ex){Failure(ex);}
                });
            }

            var boxes=selected.Where(o=>o.Value is MapBox or MapWedge or MapPrism or MapConvexBrush).ToArray();
            if(boxes.Length==2)
            {
                _inspector.Children.Add(Text("AUTHORED BRUSH CSG"));
                void Csg(string mode)
                {
                    try
                    {
                        Guid aId=boxes[0].Id,bId=boxes[1].Id;
                        _document.Edit("Box CSG "+mode,d=>
                        {
                            var a=(MapGeometry)MapObjects.Find(d,aId)!.Value;var b=(MapGeometry)MapObjects.Find(d,bId)!.Value;
                            d.Geometry.RemoveAll(g=>g.Id==aId||g.Id==bId);
                            d.Geometry.AddRange(MapCsgService.Execute(a,b,mode,d.Materials[a.Material].TexScale,d.Materials[b.Material].TexScale));
                        },MapChangeDomain.Geometry);
                        _document.Selection.Clear();_document.SelectionChanged();ModelingInspector();
                    }
                    catch(Exception ex){Failure(ex);}
                }
                AddButton(_inspector,"Union",()=>Csg("Union"));
                AddButton(_inspector,"Intersect",()=>Csg("Intersect"));
                AddButton(_inspector,"Subtract second from first",()=>Csg("Subtract"));
            }
        }

        private void ArrangeInspector()
        {
            _inspector.Children.Clear(); if (_document == null) return;
            _inspector.Children.Add(Text("ARRANGE SELECTION"));
            var axis = new ComboBox { ItemsSource = new[] { "X", "Y", "Z" }, SelectedIndex = 0 };
            _inspector.Children.Add(axis);
            foreach (var edge in new[] { -1, 0, 1 })
                AddButton(_inspector, "Align " + (edge < 0 ? "minimum" : edge > 0 ? "maximum" : "center"),
                    () => EditSelection("Align", (d, ids) => MapLayoutCommands.Align(d, ids, axis.SelectedIndex, edge)));
            AddButton(_inspector, "Distribute centers", () => EditSelection("Distribute", (d, ids) => MapLayoutCommands.Distribute(d, ids, axis.SelectedIndex)));
            AddButton(_inspector, "Snap to grid", () => EditSelection("Snap to grid", (d, ids) => MapLayoutCommands.Snap(d, ids, Math.Max(.01f, _viewport?.Snap ?? 1))));
            AddButton(_inspector, "Snap to floor", () =>
            {
                if (_viewport == null) return;
                EditSelection("Snap to floor", (d, ids) => MapLayoutCommands.SnapToFloor(d, ids,
                    point => _viewport.Cache.CollisionNear(point)));
            });
            AddButton(_inspector,"Snap base to nearest surface",()=>
            {
                if(_viewport==null)return;
                EditSelection("Snap to surface",(d,ids)=>MapLayoutCommands.SnapToSurface(d,ids,
                    point=>_viewport.Cache.SurfaceNear(point),align:false));
            });
            AddButton(_inspector,"Snap + align to surface",()=>
            {
                if(_viewport==null)return;
                EditSelection("Align to surface",(d,ids)=>MapLayoutCommands.SnapToSurface(d,ids,
                    point=>_viewport.Cache.SurfaceNear(point),align:true));
            });
            var count = new TextBox { Text = "4" }; var spacing = new TextBox { Text = "4" };
            _inspector.Children.Add(Text("Copies (1–256)")); _inspector.Children.Add(count);
            _inspector.Children.Add(Text("Spacing / radial offset")); _inspector.Children.Add(spacing);
            void Duplicate(bool radial)
            {
                try
                {
                    int copies = int.Parse(count.Text ?? "", CultureInfo.InvariantCulture);
                    float distance = Number(spacing.Text ?? "");
                    var delta = axis.SelectedIndex == 0 ? new System.Numerics.Vector3(distance, 0, 0)
                        : axis.SelectedIndex == 1 ? new System.Numerics.Vector3(0, distance, 0) : new System.Numerics.Vector3(0, 0, distance);
                    EditSelection(radial ? "Radial array" : "Array", (d, ids) => MapLayoutCommands.Array(d, ids, copies, delta, radial));
                }
                catch (Exception ex) { Failure(ex); }
            }
            AddButton(_inspector, "Create array", () => Duplicate(false));
            AddButton(_inspector, "Create radial array", () => Duplicate(true));
            AddButton(_inspector, "Duplicate in place", () => EditSelection("Duplicate in place", (d, ids) => MapLayoutCommands.Array(d, ids, 1, System.Numerics.Vector3.Zero)));
            _inspector.Children.Add(Text("SELECTION SETS"));
            var setName=new TextBox{PlaceholderText="Selection set name"};_inspector.Children.Add(setName);
            AddButton(_inspector,"Save current selection",()=>
            {
                string name=(setName.Text??"").Trim();
                if(String.IsNullOrWhiteSpace(name)||_document.Selection.Count==0){_status.Text="Name the set and select at least one object.";return;}
                _studioState.SelectionSets[name]=_document.Selection.ToArray();
                MapStudioStateStore.Save(_document.Project.Definition,_studioState,_services.UserMapDirectory);ArrangeInspector();
            });
            foreach(var pair in _studioState.SelectionSets.OrderBy(p=>p.Key,StringComparer.OrdinalIgnoreCase))
            {
                string name=pair.Key;Guid[] ids=pair.Value;
                _inspector.Children.Add(Text($"{name} · {ids.Length} objects"));
                AddButton(_inspector,"Recall "+name,()=>
                {
                    var existing=MapObjects.All(_document.Project.Definition).Select(o=>o.Id).ToHashSet();
                    _document.Selection.Clear();foreach(Guid id in ids.Where(existing.Contains))_document.Selection.Add(id);
                    _document.ActiveObjectId=_document.Selection.FirstOrDefault();_document.SelectionChanged();_viewport?.FrameSelection();
                });
                AddButton(_inspector,"Delete "+name,()=>
                {
                    _studioState.SelectionSets.Remove(name);MapStudioStateStore.Save(_document.Project.Definition,_studioState,_services.UserMapDirectory);ArrangeInspector();
                });
            }
            AddButton(_inspector, "Save selection as prefab", SavePrefab);
            AddButton(_inspector, "Insert prefab", InsertPrefab);
        }

        private void SavePrefab()
        {
            if(_document==null||_document.Selection.Count==0){_status.Text="Select objects to save as a prefab.";return;}
            var panel=new StackPanel{Spacing=8};panel.Children.Add(Text("SAVE PREFAB"));
            var name=new TextBox{Text="My prefab"};panel.Children.Add(name);
            AddButton(panel,"Save",()=>{
                try
                {
                    string safe=new string((name.Text??"prefab").Trim().Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch).ToArray());
                    if(String.IsNullOrWhiteSpace(safe))safe="prefab";
                    string directory=Path.Combine(_services.MapLibraryDirectory,".prefabs");Directory.CreateDirectory(directory);
                    string target=Path.Combine(directory,safe+".json");
                    MapPrefabService.Save(_document.Project.Definition,_document.Selection,target);
                    Dismiss();_status.Text="Prefab saved: "+target;
                }
                catch(Exception ex){Failure(ex);}
            });
            AddButton(panel,"Cancel",Dismiss);Modal(panel);
        }

        private void InsertPrefab()
        {
            if(_document==null)return;
            string directory=Path.Combine(_services.MapLibraryDirectory,".prefabs");
            var panel=new StackPanel{Spacing=8};panel.Children.Add(Text("PREFAB BROWSER"));
            var search=new TextBox{PlaceholderText="Search prefabs"};panel.Children.Add(search);
            var list=new ListBox{MaxHeight=340};panel.Children.Add(list);
            PrefabRow[] ReadRows()
            {
                if(!Directory.Exists(directory))return Array.Empty<PrefabRow>();
                var rows=new List<PrefabRow>();
                foreach(string path in Directory.EnumerateFiles(directory,"*.json"))
                {
                    try
                    {
                        var d=MapDefinition.Load(path);
                        int count=d.Geometry.Count+d.Brushes.Count+d.Spawns.Count+d.Items.Count+d.JumpPads.Count+d.NavigationLinks.Count;
                        rows.Add(new(path,count,d.Materials.Count));
                    }
                    catch(Exception ex) when(ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ProgramException or ArgumentException){ }
                }
                return rows.OrderByDescending(r=>_studioState.RecentPrefabs.Contains(r.Path,StringComparer.OrdinalIgnoreCase))
                    .ThenBy(r=>System.IO.Path.GetFileName(r.Path),StringComparer.OrdinalIgnoreCase).ToArray();
            }
            var rows=ReadRows();list.ItemsSource=rows;
            search.TextChanged+=(_,_)=>
            {
                string q=(search.Text??"").Trim();
                list.ItemsSource=rows.Where(r=>q.Length==0||r.ToString().Contains(q,StringComparison.OrdinalIgnoreCase)).ToArray();
            };
            AddButton(panel,"Insert",()=>{
                if(list.SelectedItem is not PrefabRow row)return;
                _=InsertPrefabAsync(row.Path);
            });
            AddButton(panel,"Refresh",()=>{rows=ReadRows();list.ItemsSource=rows;});
            AddButton(panel,"Cancel",Dismiss);Modal(panel);
        }
        private void NavigationInspector()
        {
            _inspector.Children.Clear();
            _inspector.Children.Add(Text("NAVIGATION PATH"));
            AddButton(_inspector, "Generate navigation", () => _ = Navigation());
            var start = new TextBox { Text = "0" }; var end = new TextBox { Text = "1" };
            _inspector.Children.Add(Text("Start node")); _inspector.Children.Add(start);
            _inspector.Children.Add(Text("Destination node")); _inspector.Children.Add(end);
            AddButton(_inspector, "Show path", () =>
            {
                try
                {
                    if (_viewport?.Navigation is not { } graph) throw new InvalidOperationException("Generate navigation first.");
                    int from = int.Parse(start.Text ?? ""), to = int.Parse(end.Text ?? "");
                    var path = MapNavigationInspection.Find(graph, from, to);
                    _viewport.NavigationPath = path; _viewport.InvalidateVisual();
                    float length = 0; for (int i = 1; i < path.Length; i++) length += (graph.Positions[path[i]] - graph.Positions[path[i-1]]).Length;
                    _status.Text = path.Length == 0 ? "Destination is unreachable." : $"Path: {path.Length} nodes · {length:0.0} units · region {graph.Components[from]}";
                }
                catch (Exception ex) { Failure(ex); }
            });
        }
        private void Statistics()
        {
            _inspector.Children.Clear();
            _diagnostics = Text(""); _diagnostics.TextWrapping = TextWrapping.Wrap;
            _inspector.Children.Add(_diagnostics); RefreshStatistics();
        }
        private void RefreshStatistics()
        {
            if (_diagnostics == null || _document == null || _viewport == null) return;
            var cache = _viewport.Cache; var jobs = _services.BuildScheduler as MapBuildScheduler;
            var d = _document.Project.Definition;
            _diagnostics.Text = $"MAP HEALTH\nGeometry: {cache.NativeFaces.Count + cache.ImportedFaces.Count:N0} faces\n"
                + $"Spawns: {d.Spawns.Count} · Entities: {cache.Entities.Count}\nAssets: {d.Assets.Count} · Materials: {d.Materials.Count}\n"
                + (_viewport.Navigation is { } nav ? $"Navigation: {nav.Positions.Length:N0} nodes · {nav.Components.Distinct().Count()} regions\n" : "Navigation: not generated\n")
                + $"Autosave: {_autosave.Result?.Milliseconds ?? 0:0.0} ms\n\nViewport rebuilds\nGeometry: {cache.GeometryRebuildCount} ({cache.GeometryObjectsRebuilt} objects)\n"
                + $"Imported: {cache.ImportedRebuildCount} · {cache.ImportedChunks.Count:N0} spatial chunks\nSelection: {cache.SelectionRebuildCount}\nEntities: {cache.EntityRebuildCount}\n"
                + $"Collision: {cache.CollisionRebuildCount}\nNavigation invalidations: {cache.NavigationInvalidationCount}\n\n"
                + $"History: {_document.History.CommandCount} commands / {_document.History.ApproximateBytes / 1024d:0.0} KiB\n\n"
                + $"Build queue: {jobs?.PendingCount ?? 0}\nShared requests: {jobs?.SharedRequests ?? 0}\nCompilations: {jobs?.CompilationCount ?? 0}\n"
                + $"Compiler cache: {jobs?.CompiledCacheCount ?? 0} entries / {(jobs?.CompiledCacheBytes ?? 0) / 1048576d:0.0} MiB\n\n"
                + (_lastBuild == null ? "Build this map to measure its runtime cache."
                    : $"Last runtime build: {_lastBuild.Milliseconds:0.0} ms / {(_lastBuild.CacheHit ? "cache hit" : "cache miss")}\n{_lastBuild.Fingerprint}");
        }
        private static float Number(string value){float number=float.Parse(value,CultureInfo.InvariantCulture);if(!float.IsFinite(number))throw new FormatException("Enter a finite number.");return number;}
        private static float[] ParseVector(string value,int count)
        {var result=value.Split(',',StringSplitOptions.TrimEntries).Select(Number).ToArray();if(result.Length!=count)throw new FormatException($"Enter {count} comma-separated numbers.");return result;}
    }
}
