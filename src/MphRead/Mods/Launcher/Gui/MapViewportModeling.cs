using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using Vector = System.Numerics.Vector3;

namespace MphRead.Mods.Launcher.Gui;

internal sealed partial class MapViewport
{
    private static double Distance(Point a,Point b) => Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Y-b.Y,2));
    private Avalonia.Controls.ContextMenu? _modelingMenu;
    private void InitializeModelingMenu()
    {
        var menu=new Avalonia.Controls.ContextMenu();
        foreach(string action in new[]{"Extrude region","Extrude individual","Inset region","Bevel","Split edge","Collapse edge","Merge center","Snap to surface","Select linked","Select boundary","Fill","Separate","Delete"})
        {var item=new Avalonia.Controls.MenuItem{Header=action};item.Click+=(_,_)=>RunModeling(action);menu.Items.Add(item);}
        _modelingMenu=menu;
    }
    public event Action<string>? ModelingError;
    private bool _keyboardTransform;
    private string _numericTransform="";
    private bool HasSubSelection => ActiveSelection?.Value is MapMesh mesh && SubSelection.ObjectId == mesh.Id && SubSelection.VertexIndices(mesh,ElementMode).Length > 0;
    private Vector GizmoCenter
    {
        get
        {
            if(_subPreview!=null)return _subPreview.Pivot;
            if(ElementMode!="Object"&&HasSubSelection&&ActiveSelection?.Value is MapMesh mesh)
            {
                if(PivotMode=="World")return Vector.Zero;if(PivotMode=="Cursor")return CursorPivot;
                int[] indices=SubSelection.VertexIndices(mesh,ElementMode);
                if(PivotMode=="Active")indices=ElementMode=="Face"&&SubSelection.ActiveFace>=0?mesh.Faces[SubSelection.ActiveFace]:ElementMode=="Edge"&&SubSelection.ActiveEdge is {} edge?new[]{edge.A,edge.B}:SubSelection.ActiveVertex>=0?new[]{SubSelection.ActiveVertex}:indices;
                return indices.Select(i=>MapMeshEditing.VertexWorld(mesh,i)).Aggregate(Vector.Zero,(a,b)=>a+b)/indices.Length;
            }
            return ActiveSelection==null?Vector.Zero:MapViewportScene.Vector(ActiveSelection.Position);
        }
    }
    private Vector SubWorld(MapMesh mesh, int index) => _subPreview?.World(index) ?? MapMeshEditing.VertexWorld(mesh,index);
    private void SubChanged() { SelectionChanged?.Invoke(); InvalidateVisual(); }
    private void BeginSubInteraction(PointerPressedEventArgs e)
    {
        var mesh = (MapMesh)ActiveSelection!.Value;
        if(mesh.ModifierSource!=null){ModelingError?.Invoke("Bake modifier stack before editing mesh elements.");e.Handled=true;return;}
        if (mesh.Locked) { ModelingError?.Invoke("Unlock the mesh before editing it."); e.Handled=true; return; }
        SubSelection.Bind(mesh.Id); _axis=-1;
        if (HasSubSelection && !e.KeyModifiers.HasFlag(KeyModifiers.Control) && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var origin=Project(GizmoCenter);
            for (int i=0;i<3;i++)
            {
                var end=Project(GizmoCenter+GizmoAxis(ActiveSelection!,i)*3);
                if (origin!=null && end!=null && Distance(end.Value.Point,origin.Value.Point)>18 && Distance(end.Value.Point,_last)<12) { _axis=i; break; }
            }
            if (_axis>=0 || origin is {} center && Distance(center.Point,_last)<7)
            {
                try { _subPreview=new(mesh,SubSelection,ElementMode,PivotMode,CursorPivot); _drag=true; }
                catch (Exception ex) { ModelingError?.Invoke(ex.Message); }
                _interactionPointer=e.Pointer;e.Pointer.Capture(this);e.Handled=true;return;
            }
        }
        bool toggle=e.KeyModifiers.HasFlag(KeyModifiers.Control), add=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (PickSubElement(_last,add,toggle,e.ClickCount>1)) { SubChanged(); e.Handled=true; return; }
        _boxSelect=true;_boxCurrent=_start;_boxAdditive=add;_boxToggle=toggle;
        _interactionPointer=e.Pointer;e.Pointer.Capture(this);e.Handled=true;InvalidateVisual();
    }
    private static void SelectElements<T>(HashSet<T> set, IEnumerable<T> values, bool toggle) where T:notnull
    { foreach(var value in values) if(!toggle || !set.Remove(value)) set.Add(value); }
    private bool PickSubElement(Point point, bool add, bool toggle, bool linked)
    {
        if (ActiveSelection?.Value is not MapMesh mesh) return false;
        int face=-1, vertex=-1; MapEdge? edge=null;
        if (ElementMode=="Face")
        {
            var hit=MapViewportPicking.PickHit(BuildRenderFrame(Layout),point.X,point.Y,false);
            if(hit is not {} picked || picked.ObjectId!=mesh.Id)return false;
            face=picked.FaceIndex;
        }
        else
        {
            var projected=mesh.Vertices.Select((_,i)=>Project(SubWorld(mesh,i))).ToArray();
            double best=ElementMode=="Vertex"?12:10;
            if(ElementMode=="Vertex")
            {
                for(int i=0;i<projected.Length;i++)
                    if(projected[i] is {} p && Distance(p.Point,point)<best) {best=Distance(p.Point,point);vertex=i;}
                if(vertex<0)return false;
            }
            else
            {
                foreach(var candidate in new MapMeshTopology(mesh).Edges)
                    if(projected[candidate.A] is {} a && projected[candidate.B] is {} b)
                    {double distance=PointSegmentDistance(point,a.Point,b.Point);if(distance<best){best=distance;edge=candidate;}}
                if(edge==null)return false;
            }
        }
        if(!add&&!toggle)SubSelection.Clear();SubSelection.Bind(mesh.Id);
        var topology=new MapMeshTopology(mesh);
        if(face>=0){SelectElements(SubSelection.Faces,linked?topology.ConnectedFaces(face):new[]{face},toggle);SubSelection.ActiveFace=face;}
        else if(vertex>=0){SelectElements(SubSelection.Vertices,new[]{vertex},toggle);SubSelection.ActiveVertex=vertex;}
        else if(edge is {} selected){SelectElements(SubSelection.Edges,linked?topology.EdgeLoop(selected):new[]{selected},toggle);SubSelection.ActiveEdge=selected;}
        SubSelection.Validate(mesh);return true;
    }
    private void SelectSubBox(Rect box)
    {
        if(ActiveSelection?.Value is not MapMesh mesh)return;
        if(!_boxAdditive&&!_boxToggle)SubSelection.Clear();SubSelection.Bind(mesh.Id);
        var points=mesh.Vertices.Select((_,i)=>Project(SubWorld(mesh,i))).ToArray();
        bool Inside(int i)=>points[i] is {} p && box.Contains(p.Point);
        if(ElementMode=="Vertex")SelectElements(SubSelection.Vertices,Enumerable.Range(0,points.Length).Where(Inside),_boxToggle);
        else if(ElementMode=="Edge")SelectElements(SubSelection.Edges,new MapMeshTopology(mesh).Edges.Where(e=>Inside(e.A)&&Inside(e.B)),_boxToggle);
        else SelectElements(SubSelection.Faces,Enumerable.Range(0,mesh.Faces.Count).Where(f=>mesh.Faces[f].All(Inside)),_boxToggle);
        SubSelection.Validate(mesh);SubChanged();
    }
    private void DrawSubSelection(DrawingContext context)
    {
        if(ActiveSelection?.Value is not MapMesh mesh || SubSelection.ObjectId!=mesh.Id)return;
        foreach(int face in SubSelection.Faces.Where(f=>(uint)f<mesh.Faces.Count))
        {
            var points=mesh.Faces[face].Select(i=>Project(SubWorld(mesh,i))).ToArray();
            if(points.All(p=>p!=null))context.DrawGeometry(new SolidColorBrush(Color.FromArgb(55,255,196,64)),new Pen(Brushes.Gold,3),Polygon(points.Select(p=>p!.Value.Point).ToArray()));
        }
        foreach(var edge in SubSelection.Edges.Where(e=>(uint)e.A<mesh.Vertices.Count&&(uint)e.B<mesh.Vertices.Count))
            Line(context,SubWorld(mesh,edge.A),SubWorld(mesh,edge.B),Brushes.Gold,4);
        if(ElementMode=="Vertex")
            for(int i=0;i<mesh.Vertices.Count;i++)
                if(Project(SubWorld(mesh,i)) is {} p)context.DrawEllipse(SubSelection.Vertices.Contains(i)?Brushes.Gold:Brushes.SlateGray,null,p.Point,SubSelection.Vertices.Contains(i)?5:2,SubSelection.Vertices.Contains(i)?5:2);
    }
    private bool HandleSubKey(KeyEventArgs e)
    {
        if(ActiveSelection?.Value is not MapMesh mesh)return false;
        bool control=(e.KeyModifiers&(KeyModifiers.Control|KeyModifiers.Meta))!=0;
        bool alt=e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if(control&&e.Key==Key.A)SubSelection.SelectAll(mesh,ElementMode);
        else if(alt&&e.Key==Key.A)SubSelection.Clear();
        else if(!control&&!alt&&e.Key==Key.L)SubSelection.Linked(mesh,ElementMode);
        else if(control&&(e.Key==Key.Add||e.Key==Key.OemPlus))SubSelection.Resize(mesh,ElementMode,true);
        else if(control&&(e.Key==Key.Subtract||e.Key==Key.OemMinus))SubSelection.Resize(mesh,ElementMode,false);
        else if(!control&&!alt&&(e.Key==Key.G||e.Key==Key.R||e.Key==Key.S))
        {
            if(mesh.ModifierSource!=null){ModelingError?.Invoke("Bake modifier stack before editing mesh elements.");return true;}
            if(!HasSubSelection)return true;
            Tool=e.Key==Key.G?"Move":e.Key==Key.R?"Rotate":"Scale";
            try {_subPreview=new(mesh,SubSelection,ElementMode,PivotMode,CursorPivot);_keyboardTransform=true;_numericTransform="";_start=_last;_drag=true;_axis=-1;}
            catch(Exception ex){ModelingError?.Invoke(ex.Message);}
        }
        else if(!control&&!alt&&e.Key==Key.E)RunModeling("Extrude region");
        else if(!control&&!alt&&e.Key==Key.I)RunModeling("Inset region");
        else if(!control&&!alt&&e.Key==Key.B)RunModeling("Bevel");
        else if(!control&&!alt&&e.Key==Key.M)RunModeling("Merge center");
        else if(!control&&!alt&&(e.Key==Key.X||e.Key==Key.Delete))RunModeling("Delete");
        else if(!control&&!alt&&e.Key==Key.F)RunModeling("Fill");
        else return false;
        SubChanged();return true;
    }
    private bool TransformKey(KeyEventArgs e)
    {
        if(!_keyboardTransform||_subPreview==null)return false;
        if(e.Key==Key.Enter){var preview=_subPreview;_subPreview=null;preview.Commit(Document,Tool);CancelInteraction();return true;}
        if(e.Key==Key.X||e.Key==Key.Y||e.Key==Key.Z)
        {string axis=e.Key.ToString();Axes=e.KeyModifiers.HasFlag(KeyModifiers.Shift)?string.Concat("XYZ".Where(c=>c!=axis[0])):axis;_axis=-1;}
        else if(e.Key>=Key.D0&&e.Key<=Key.D9)_numericTransform+=(int)e.Key-(int)Key.D0;
        else if(e.Key==Key.OemMinus)_numericTransform="-"+_numericTransform.TrimStart('-');
        else if(e.Key==Key.OemPeriod)_numericTransform+=".";
        else if(e.Key==Key.Back&&_numericTransform.Length>0)_numericTransform=_numericTransform[..^1];
        else return false;
        if(float.TryParse(_numericTransform,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float value))
        {
            _preview=Tool=="Move"?ScaleAxes*value:Vector.Zero;_rotation=Tool=="Rotate"?value:0;_scale=Tool=="Scale"?value:1;
            _subPreview.Update(Tool,_preview,_rotation,_scale,LocalAxes,TurnAxis,ScaleAxes);
        }
        InvalidateVisual();return true;
    }
    public void RunModeling(string operation,float amount=.25f,int segments=1)
    {
        try
        {
            if(ActiveSelection?.Value is not MapMesh source)throw new InvalidOperationException("Select an editable mesh first.");
            if(source.ModifierSource!=null)throw new InvalidOperationException("Bake modifier stack before editing mesh elements.");
            if(source.Locked)throw new InvalidOperationException("Unlock the mesh first.");
            SubSelection.Bind(source.Id);SubSelection.Validate(source);
            int[] faces=SubSelection.Faces.Order().ToArray(),vertices=SubSelection.VertexIndices(source,ElementMode);
            var edges=SubSelection.Edges.OrderBy(e=>e.A).ThenBy(e=>e.B).ToArray();
            if(operation=="Select linked"){SubSelection.Linked(source,ElementMode);SubChanged();return;}
            if(operation=="Grow"||operation=="Shrink"){SubSelection.Resize(source,ElementMode,operation=="Grow");SubChanged();return;}
            if(operation=="Select boundary"){SubSelection.Edges.UnionWith(new MapMeshTopology(source).BoundaryEdges());ElementMode="Edge";SubChanged();return;}
            var ids=operation=="Join meshes"?Document.Selection.ToArray():new[]{source.Id};
            Document.EditObjects(operation,ids,d=>
            {
                var mesh=(MapMesh)MapObjects.Find(d,source.Id)!.Value;
                switch(operation)
                {
                    case "Extrude region":MapMeshEditing.ExtrudeRegion(mesh,faces,amount);break;
                    case "Extrude individual":foreach(int f in faces)MapMeshEditing.ExtrudeFace(mesh,f,amount);break;
                    case "Inset region":MapMeshEditing.InsetRegion(mesh,faces,amount);break;
                    case "Bevel":
                        if(ElementMode=="Edge"){if(edges.Length!=1)throw new InvalidOperationException("Select one edge for bevel.");MapMeshEditing.BevelEdge(mesh,edges[0],amount,segments);}
                        else foreach(int f in faces)MapMeshEditing.BevelFace(mesh,f,amount,amount*.25f);break;
                    case "Split edge":foreach(var edge in edges)MapMeshEditing.SplitEdge(mesh,edge,amount,segments);break;
                    case "Dissolve edge":foreach(var edge in edges)MapMeshEditing.DissolveEdge(mesh,edge);break;
                    case "Collapse edge":case "Collapse to A":case "Collapse to B":case "Collapse to cursor":
                        if(edges.Length!=1)throw new InvalidOperationException("Select one edge to collapse.");
                        MapMeshEditing.CollapseEdge(mesh,edges[0],operation=="Collapse to A"?"First":operation=="Collapse to B"?"Last":operation=="Collapse to cursor"?"Cursor":"Center",MapMeshEditing.WorldToLocal(mesh,CursorPivot));break;
                    case "Slide edge":if(edges.Length!=1)throw new InvalidOperationException("Select one edge to slide.");MapMeshEditing.SlideEdge(mesh,edges[0],new MapMeshTopology(mesh).AdjacentFaces(edges[0]).First(),amount);break;
                    case "Merge center":case "Merge first":case "Merge last":
                        var ordered=ElementMode=="Vertex"?SubSelection.OrderedVertices:vertices;
                        MapMeshEditing.MergeVertices(mesh,ordered,operation=="Merge first"?"First":operation=="Merge last"?"Last":"Center");break;
                    case "Merge by distance":MapMeshEditing.MergeByDistance(mesh,vertices,amount);break;
                    case "Connect vertices":if(vertices.Length!=2)throw new InvalidOperationException("Select two vertices.");MapMeshEditing.ConnectVertices(mesh,vertices[0],vertices[1]);break;
                    case "Rip vertex":if(vertices.Length!=1)throw new InvalidOperationException("Select one vertex.");MapMeshEditing.RipVertex(mesh,vertices[0],new MapMeshTopology(mesh).FacesOfVertex(vertices[0]).First());break;
                    case "Slide vertex":if(vertices.Length!=2)throw new InvalidOperationException("Select the vertex and its neighboring target.");MapMeshEditing.SlideVertex(mesh,vertices[0],vertices[1],amount);break;
                    case "Flatten X":case "Flatten Y":case "Flatten Z":MapMeshEditing.Flatten(mesh,vertices,"XYZ".IndexOf(operation[^1]));break;
                    case "Snap to surface":foreach(int v in vertices){var world=MapMeshEditing.VertexWorld(mesh,v);var hit=MapLayoutCommands.NearestSurface(world,Cache.SurfaceNear(world),new HashSet<Guid>{source.Id});if(hit is {} contact)MapMeshEditing.SetVertexWorld(mesh,v,contact.Point);}break;
                    case "Duplicate faces":
                        var copy=MapMeshEditing.DuplicateFaces(mesh,faces);var joined=MapMeshEditing.Join(new[]{mesh,copy});d.Geometry.Remove(mesh);d.Geometry.Add(joined);break;
                    case "Duplicate to object":d.Geometry.Add(MapMeshEditing.DuplicateFaces(mesh,faces));break;
                    case "Separate":d.Geometry.Add(MapMeshEditing.Separate(mesh,faces));if(mesh.Faces.Count==0)d.Geometry.Remove(mesh);break;
                    case "Join meshes":
                        var sources=d.Geometry.OfType<MapMesh>().ToArray();if(sources.Length!=ids.Length)throw new InvalidOperationException("Join requires only meshes.");
                        var combined=MapMeshEditing.Join(sources);d.Geometry.Clear();d.Geometry.Add(combined);break;
                    case "Fill":MapMeshEditing.Fill(mesh,edges.Length>0?edges:new MapMeshTopology(mesh).BoundaryEdges().Where(e=>vertices.Contains(e.A)&&vertices.Contains(e.B)));break;
                    case "Triangulate":MapMeshEditing.Triangulate(mesh,faces);break;
                    case "Dissolve triangles":
                        foreach(var edge in new MapMeshTopology(mesh).Edges.ToArray())
                        {var adjacent=new MapMeshTopology(mesh).AdjacentFaces(edge);if(adjacent.Length==2&&adjacent.All(f=>mesh.Faces[f].Length==3))try{MapMeshEditing.DissolveEdge(mesh,edge);}catch(InvalidOperationException){}}break;
                    case "Flip normals":foreach(int f in faces)MapMeshEditing.FlipFace(mesh,f);break;
                    case "Recalculate winding":MapMeshEditing.RecalculateWinding(mesh);break;
                    case "Clean unused vertices":MapMeshEditing.Compact(mesh);break;
                    case "Delete":
                        var removed=(ElementMode=="Face"?faces:Enumerable.Range(0,mesh.Faces.Count).Where(f=>ElementMode=="Vertex"?mesh.Faces[f].Any(vertices.Contains):new MapMeshTopology(mesh).EdgesOfFace(f).Any(edges.Contains))).OrderDescending().ToArray();
                        foreach(int f in removed)MapMeshEditing.DeleteFace(mesh,f);if(mesh.Faces.Count==0)d.Geometry.Remove(mesh);break;
                    default:throw new InvalidOperationException("Unknown modeling command.");
                }
                foreach(var changed in d.Geometry.OfType<MapMesh>())MapMeshEditing.CheckOperation(changed);
            });
            ClearSubSelection();SubChanged();
        }
        catch(Exception ex){ModelingError?.Invoke(ex.Message);}
    }
}
