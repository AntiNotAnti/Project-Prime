using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MphRead.Mods.MapEditor;
using MphRead.Mods.MapGen;
using Vector = System.Numerics.Vector3;

namespace MphRead.Mods.Launcher.Gui
{
    // Game-rendered geometry with Avalonia authoring overlays and a CPU fallback.
    // It never packs collision or regenerates navigation while drawing.
    internal sealed partial class MapViewport : Control, IMapViewport
    {
        public MapDocument Document { get; }
        public Vector CameraPosition { get; private set; } = new(28,24,32);
        public Vector CameraTarget { get; private set; } = Vector.Zero;
        public string View { get; set; } = "Perspective";
        public string Tool { get; set; } = "Move";
        public float Snap { get; set; } = .25f;
        public float AngleSnap { get; set; } = 15;
        public float ScaleSnap {get;set;}=.25f;
        public bool LocalAxes {get;set;}
        public bool Wireframe { get; set; }
        public bool Collision { get; set; }
        public bool CollisionHeatmap { get; set; }
        public bool CollisionRepairsOverlay { get; set; }
        public bool PartitionOverlay { get; set; }
        public float PartitionCellSize { get; set; } = 64f;
        public bool KillPlane { get; set; }
        public int[] NavigationPath { get; set; } = Array.Empty<int>();
        public MapNodePacker.NavigationGraph? Navigation { get; set; }
        public string ElementMode { get; set; } = "Object";
        public int SelectedFaceIndex { get; private set; } = -1;
        public int SelectedVertexIndex { get; private set; } = -1;
        public (int A,int B)? SelectedEdge { get; private set; }
        public string PlacementMode { get; set; } = "Cursor";
        public MapPickHit? LastSurfaceHit { get; private set; }
        public bool MaterialEyedropper { get; set; }
        public bool MeasureMode { get; set; }
        public bool EntityVisualization { get; set; } = true;
        public event Action? SelectionChanged;
        public event Action<MapPickHit>? MaterialPicked;
        internal MapViewportCache Cache { get; } = new();
        private IEnumerable<MapViewportFace> Faces => Cache.NativeFaces.Concat(Collision ? Cache.ImportedCollisionFaces : Cache.ImportedFaces);
        private IEnumerable<MapViewportFace> VisibleFaces => Cache.NativeFaces.Concat(
            Cache.VisibleImportedFaces(Camera,Layout,Collision));
        private MapValidationResult? _overlayDiagnostics;
        private HashSet<Guid> _warningObjects = new();
        private readonly Dictionary<(Guid Id, string Text), FormattedText> _labels = new();
        private readonly List<(Guid Id, Point[] Points, double Depth)> _pick = new();
        private Point _last, _start;
        private bool _orbit, _pan, _drag, _boxSelect, _boxAdditive;
        private IPointer? _interactionPointer;
        private Point _boxCurrent;
        private int _axis = -1;
        public string Axes { get; set; } = "Free";
        public string PivotMode { get; set; } = "Individual";
        public Vector CursorPivot { get; set; }
        private Vector TurnAxis => _axis == 0 || Axes == "X" ? Vector.UnitX : _axis == 2 || Axes == "Z" ? Vector.UnitZ : Vector.UnitY;
        private Vector ScaleAxes => _axis >= 0 ? (_axis == 0 ? Vector.UnitX : _axis == 1 ? Vector.UnitY : Vector.UnitZ)
            : Axes == "Free" ? Vector.One : new(Axes.Contains('X') ? 1 : 0, Axes.Contains('Y') ? 1 : 0, Axes.Contains('Z') ? 1 : 0);
        private Vector? TransformPivot
        {
            get
            {
                if (PivotMode == "Individual") return null;
                if (PivotMode == "World") return Vector.Zero;
                if (PivotMode == "Cursor") return CursorPivot;
                var objects = MapObjects.All(Document.Project.Definition).Where(o => Document.Selection.Contains(o.Id)).ToArray();
                if (objects.Length == 0) return null;
                if (PivotMode == "Active") return MapViewportScene.Vector((objects.FirstOrDefault(o => o.Id == Document.ActiveObjectId) ?? objects[0]).Position);
                return objects.Select(o => MapViewportScene.Vector(o.Position)).Aggregate(Vector.Zero, (a,b) => a+b) / objects.Length;
            }
        }
        private Vector _preview;
        private float _rotation, _scale = 1;
        private Vector? _measureA,_measureB;
        private Guid _subObjectId=Guid.Empty;

        public MapViewport(MapDocument document)
        {
            Document=document; Focusable=true; ClipToBounds=true;
            Document.Invalidated += InvalidateDocument;
            InvalidateDocument(new(MapChangeDomain.All));
        }
        private void InvalidateDocument(MapDocumentChange change)
        {
            Cache.Invalidate(Document.Project.Definition, change);
            if ((change.Domains & (MapChangeDomain.Geometry | MapChangeDomain.Entity)) != 0) _labels.Clear();
            if ((change.Domains & (MapChangeDomain.Navigation | MapChangeDomain.Import)) != 0) Navigation = null;
            InvalidateVisual();
        }
        internal void DetachDocument()
        {
            CancelInteraction();
            Document.Invalidated -= InvalidateDocument;
        }

        public void SetImported(BuiltMap map)
        {
            Cache.SetImported(map); InvalidateVisual();
        }

        public void SetImported(MapAnalysisResult analysis)
        {
            Cache.SetImported(analysis); InvalidateVisual();
        }
        public void FrameAll()
        {
            var points=Faces.SelectMany(f=>f.Points)
                .Concat(Cache.Entities.Select(o=>MapViewportScene.Vector(o.Position))).ToArray();
            if(points.Length==0) return;
            Vector min=points.Aggregate(new Vector(float.MaxValue),Vector.Min), max=points.Aggregate(new Vector(float.MinValue),Vector.Max);
            CameraTarget=(min+max)/2; CameraPosition=CameraTarget+Vector.Normalize(new Vector(1,.8f,1))*Math.Max(8,(max-min).Length()*(View=="Perspective"?1:1.7f)); SetView(View);
        }
        public void FrameSelection()
        {
            var selected=MapObjects.All(Document.Project.Definition).Where(o=>Document.Selection.Contains(o.Id)).ToArray();
            if(selected.Length==0){FrameAll();return;}
            var ids=selected.Select(o=>o.Id).ToHashSet();
            FocusWorld(Cache.NativeFaces.Where(f=>ids.Contains(f.ObjectId)).SelectMany(f=>f.Points)
                .Concat(selected.Select(o=>MapViewportScene.Vector(o.Position))));
        }
        public Vector GetPlacementPoint()
            => PlacementMode switch
            {
                "Camera target" => CameraTarget,
                "Surface" when LastSurfaceHit is { } hit => hit.Point,
                "Surface" => CameraTarget,
                _ => CursorPivot
            };

        public MapPickHit? PickSurface(double x,double y)
            => MapViewportPicking.PickHit(BuildRenderFrame(Layout),x,y,true);

        public void FocusWorld(IEnumerable<Vector> points)
        {
            Vector[] all=points.ToArray();if(all.Length==0)return;
            Vector target=all.Aggregate(Vector.Zero,(a,b)=>a+b)/all.Length;
            float radius=Math.Max(3,all.Max(p=>Vector.Distance(p,target))*2.4f);
            var direction=CameraPosition-CameraTarget;
            if(direction.LengthSquared()<1e-6f)direction=new Vector(1,.8f,1);
            CameraTarget=target;CameraPosition=target+Vector.Normalize(direction)*radius;InvalidateVisual();
        }

        public void ClearSubSelection()
        {
            SelectedFaceIndex=SelectedVertexIndex=-1;SelectedEdge=null;_subObjectId=Guid.Empty;InvalidateVisual();
        }

        private MapViewportCamera Camera => new(CameraPosition, CameraTarget, View == "Perspective");
        private MapViewportLayout Layout => new(Bounds.Width, Bounds.Height, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        private (Vector Right,Vector Up,Vector Forward) Basis() => Camera.Basis();
        private (Point Point,double Depth)? Project(Vector point)
        {
            var projected = Camera.Project(Layout, point);
            return projected is { } p ? (new Point(p.X, p.Y), p.Depth) : null;
        }
        private MapObject? ActiveSelection => MapObjects.Find(Document.Project.Definition, Document.ActiveObjectId)
            ?? MapObjects.All(Document.Project.Definition).FirstOrDefault(o => Document.Selection.Contains(o.Id));
        private Matrix4x4 PreviewTransform(MapObject item) => MapTransformPreview.Matrix(item, Tool, _preview,
            _rotation, _scale, LocalAxes, TurnAxis, ScaleAxes, TransformPivot);
        private Vector GizmoAxis(MapObject item, int axis)
        {
            var direction = axis == 0 ? Vector.UnitX : axis == 1 ? Vector.UnitY : Vector.UnitZ;
            if (LocalAxes && item.Value is MapGeometry geometry)
            { var r = geometry.Transform.Rotation; direction = Vector.Transform(direction, new Quaternion(r[0],r[1],r[2],r[3])); }
            return direction;
        }
        private BuiltFace[] ActiveMeshFaces(out MapMesh? mesh)
        {
            mesh=ActiveSelection?.Value as MapMesh;
            if(mesh==null)return Array.Empty<BuiltFace>();
            int material=mesh.Material;
            float texScale=material>=0&&material<Document.Project.Definition.Materials.Count
                ?Document.Project.Definition.Materials[material].TexScale:16;
            try{return GeometryCompiler.Compile(mesh,texScale).ToArray();}
            catch(MapAuthoringException){return Array.Empty<BuiltFace>();}
        }

        private bool PickSubElement(Point point)
        {
            if(ElementMode=="Object")return false;
            BuiltFace[] faces=ActiveMeshFaces(out MapMesh? mesh);
            if(mesh==null||faces.Length==0||ActiveSelection is not { } active)return false;
            _subObjectId=active.Id;
            SelectedFaceIndex=SelectedVertexIndex=-1;SelectedEdge=null;

            if(ElementMode=="Face")
            {
                MapPickHit? hit=MapViewportPicking.PickHit(BuildRenderFrame(Layout),point.X,point.Y,false);
                if(hit is not { } picked||picked.ObjectId!=active.Id)return false;
                SelectedFaceIndex=Math.Clamp(picked.FaceIndex,0,faces.Length-1);
                return true;
            }

            double best=ElementMode=="Vertex"?144:100;
            int bestFace=-1,bestCorner=-1;
            for(int fi=0;fi<faces.Length&&fi<mesh.Faces.Count;fi++)
            {
                BuiltFace face=faces[fi];
                for(int i=0;i<face.Points.Length;i++)
                {
                    var p=Project(new Vector(face.Points[i].X,face.Points[i].Y,face.Points[i].Z));
                    if(p==null)continue;
                    if(ElementMode=="Vertex")
                    {
                        double sq=Math.Pow(p.Value.Point.X-point.X,2)+Math.Pow(p.Value.Point.Y-point.Y,2);
                        if(sq<best){best=sq;bestFace=fi;bestCorner=i;}
                    }
                    else
                    {
                        var q=Project(new Vector(face.Points[(i+1)%face.Points.Length].X,
                            face.Points[(i+1)%face.Points.Length].Y,face.Points[(i+1)%face.Points.Length].Z));
                        if(q==null)continue;
                        double distance=PointSegmentDistance(point,p.Value.Point,q.Value.Point);
                        if(distance*distance<best){best=distance*distance;bestFace=fi;bestCorner=i;}
                    }
                }
            }
            if(bestFace<0)return false;
            if(ElementMode=="Vertex")
            {
                int[] source=mesh.Faces[Math.Min(bestFace,mesh.Faces.Count-1)];
                SelectedVertexIndex=source[Math.Min(bestCorner,source.Length-1)];
            }
            else
            {
                int[] source=mesh.Faces[Math.Min(bestFace,mesh.Faces.Count-1)];
                int a=source[Math.Min(bestCorner,source.Length-1)];
                int b=source[(bestCorner+1)%source.Length];SelectedEdge=(a,b);
            }
            return true;
        }

        private static double PointSegmentDistance(Point p,Point a,Point b)
        {
            double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
            if(length<=1e-9)return Math.Sqrt(Math.Pow(p.X-a.X,2)+Math.Pow(p.Y-a.Y,2));
            double t=Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy)/length,0,1);
            double x=a.X+t*dx,y=a.Y+t*dy;
            return Math.Sqrt(Math.Pow(p.X-x,2)+Math.Pow(p.Y-y,2));
        }

        private void DrawSubSelection(DrawingContext context)
        {
            if(_subObjectId==Guid.Empty||ActiveSelection?.Id!=_subObjectId)return;
            BuiltFace[] faces=ActiveMeshFaces(out MapMesh? mesh);if(mesh==null)return;
            if(SelectedFaceIndex>=0&&SelectedFaceIndex<faces.Length)
            {
                var projected=faces[SelectedFaceIndex].Points.Select(p=>Project(new Vector(p.X,p.Y,p.Z))).ToArray();
                if(projected.All(p=>p!=null))
                {
                    Point[] points=projected.Select(p=>p!.Value.Point).ToArray();
                    context.DrawGeometry(new SolidColorBrush(Color.FromArgb(55,255,196,64)),
                        new Pen(Brushes.Gold,3),Polygon(points));
                }
            }
            if(SelectedEdge is { } edge&&edge.A>=0&&edge.B>=0&&edge.A<mesh.Vertices.Count&&edge.B<mesh.Vertices.Count)
            {
                Vector World(int index)
                {
                    // Apply the same geometry transform directly.
                    var p=new Vector(mesh.Vertices[index][0],mesh.Vertices[index][1],mesh.Vertices[index][2]);
                    var t=mesh.Transform;var q=new Quaternion(t.Rotation[0],t.Rotation[1],t.Rotation[2],t.Rotation[3]);
                    p*=new Vector(t.Scale[0],t.Scale[1],t.Scale[2]);p=Vector.Transform(p,q);
                    return p+new Vector(t.Position[0],t.Position[1],t.Position[2]);
                }
                Line(context,World(edge.A),World(edge.B),Brushes.Gold,5);
            }
            if(SelectedVertexIndex>=0&&SelectedVertexIndex<mesh.Vertices.Count)
            {
                var t=mesh.Transform;Vector p=new(mesh.Vertices[SelectedVertexIndex][0],mesh.Vertices[SelectedVertexIndex][1],mesh.Vertices[SelectedVertexIndex][2]);
                p*=new Vector(t.Scale[0],t.Scale[1],t.Scale[2]);p=Vector.Transform(p,new Quaternion(t.Rotation[0],t.Rotation[1],t.Rotation[2],t.Rotation[3]));
                p+=new Vector(t.Position[0],t.Position[1],t.Position[2]);
                var screen=Project(p);if(screen!=null)context.DrawEllipse(Brushes.Gold,new Pen(Brushes.White,1),screen.Value.Point,6,6);
            }
        }

        internal MapRenderFrame BuildRenderFrame(MapViewportLayout layout)
        {
            var transforms = new Dictionary<Guid, Matrix4x4>();
            if (_drag)
                foreach (var item in MapObjects.All(Document.Project.Definition))
                    if (Document.Selection.Contains(item.Id)) transforms[item.Id] = PreviewTransform(item);
            return new(layout, Camera, Cache.VisibleMeshes(Camera,layout), Document.Selection.ToHashSet(), transforms, Wireframe, Collision) { GridView=View, GridStep=VisibleGridStep };
        }
#if !MPHREAD_SHELL
        private bool GpuActive => false;
#endif
        private void Line(DrawingContext context,Vector a,Vector b,IBrush color,double width=1)
        { var x=Project(a);var y=Project(b);if(x!=null&&y!=null)context.DrawLine(new Pen(color,width),x.Value.Point,y.Value.Point); }

        private void Ring(DrawingContext context,Vector center,float radius,IBrush color,double width=1)
        {
            Vector previous=center+Vector.UnitX*radius;
            for(int i=1;i<=20;i++)
            {
                float angle=i*MathF.Tau/20;
                Vector point=center+new Vector(MathF.Cos(angle)*radius,0,MathF.Sin(angle)*radius);
                Line(context,previous,point,color,width);previous=point;
            }
        }

        private void WireBox(DrawingContext context,Vector center,Vector half,IBrush color,double width=1)
        {
            Vector[] p={
                center+new Vector(-half.X,-half.Y,-half.Z),center+new Vector(half.X,-half.Y,-half.Z),
                center+new Vector(half.X,-half.Y,half.Z),center+new Vector(-half.X,-half.Y,half.Z),
                center+new Vector(-half.X,half.Y,-half.Z),center+new Vector(half.X,half.Y,-half.Z),
                center+new Vector(half.X,half.Y,half.Z),center+new Vector(-half.X,half.Y,half.Z)};
            int[] edges={0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7};
            for(int i=0;i<edges.Length;i+=2)Line(context,p[edges[i]],p[edges[i+1]],color,width);
        }
        private static StreamGeometry Polygon(Point[] points)
        {
            var geometry=new StreamGeometry(); using var path=geometry.Open(); path.BeginFigure(points[0],true);
            foreach(var p in points.Skip(1))path.LineTo(p);path.EndFigure(true);return geometry;
        }
        private void Label(DrawingContext context, Guid id, string text, Vector position)
        {
            var projected = Project(position); if (projected == null) return;
            if (!_labels.TryGetValue((id, text), out var label))
            {
                if (_labels.Count > 1024) _labels.Clear();
                _labels[(id, text)] = label = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, GuiTheme.Face(bold: false), 11, Brushes.White);
            }
            var point = projected.Value.Point + new Avalonia.Vector(9, -8);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 18, 30, 42)), null,
                new Rect(point - new Avalonia.Vector(3, 2), new Size(label.Width + 6, label.Height + 4)));
            context.DrawText(label, point);
        }
        private float VisibleGridStep => Math.Max(Snap > 0 ? Snap : .25f, MathF.Pow(2,MathF.Floor(MathF.Log2(Math.Max(1,Vector.Distance(CameraPosition,CameraTarget))/32))));

        public override void Render(DrawingContext context)
        {
            base.Render(context);
#if MPHREAD_SHELL
            if (GpuActive) context.Custom(new ViewportHole(new Rect(Bounds.Size)));
            else
#endif
                context.FillRectangle(new SolidColorBrush(Color.Parse("#141c25")),new Rect(Bounds.Size));
            var grid=new SolidColorBrush(Color.Parse("#293641"));
            if (!GpuActive)
                foreach(var line in MapViewportGrid.Lines(View,VisibleGridStep))Line(context,line.A,line.B,grid);
            if(PartitionOverlay)
            {
                float cell=Math.Clamp(PartitionCellSize,8,512);
                bool have=false;Vector min=Vector.Zero,max=Vector.Zero;
                if(Cache.ImportedFaces.Count>0)
                {
                    var b=Cache.ImportedSpatial.Bounds;min=b.Min;max=b.Max;have=true;
                }
                foreach(var face in Cache.NativeFaces)
                    foreach(var point in face.Points)
                    {
                        if(!have){min=max=point;have=true;}
                        else{min=Vector.Min(min,point);max=Vector.Max(max,point);}
                    }
                if(have)
                {
                    float y=CameraTarget.Y;
                    int x0=(int)MathF.Floor(min.X/cell),x1=(int)MathF.Ceiling(max.X/cell);
                    int z0=(int)MathF.Floor(min.Z/cell),z1=(int)MathF.Ceiling(max.Z/cell);
                    int lines=(x1-x0+1)+(z1-z0+1);
                    int stride=Math.Max(1,(int)MathF.Ceiling(lines/160f));
                    var penBrush=new SolidColorBrush(Color.FromArgb(150,80,200,255));
                    for(int x=x0;x<=x1;x+=stride)
                        Line(context,new Vector(x*cell,y,z0*cell),new Vector(x*cell,y,z1*cell),penBrush,1.5);
                    for(int z=z0;z<=z1;z+=stride)
                        Line(context,new Vector(x0*cell,y,z*cell),new Vector(x1*cell,y,z*cell),penBrush,1.5);
                }
            }
            _pick.Clear();
            if (!GpuActive)
            {
            var projected=new List<(MapViewportFace Face,Point[] Points,double Depth)>();
            var transforms = BuildRenderFrame(Layout).PreviewTransforms;
            foreach(var face in VisibleFaces)
            {
                if(Collision&&!face.Solid)continue;
                var transform = transforms.GetValueOrDefault(face.ObjectId, Matrix4x4.Identity);
                var points=face.Points.Select(p=>Project(Vector.Transform(p, transform))).ToArray();
                if(points.Any(p=>p==null))continue;
                projected.Add((face,points.Select(p=>p!.Value.Point).ToArray(),points.Average(p=>p!.Value.Depth)));
            }
            foreach(var item in projected.OrderByDescending(p=>p.Depth))
            {
                bool selected=Document.Selection.Contains(item.Face.ObjectId);
                int shade=(int)Math.Clamp(100*item.Face.Shade,35,200);
                var color=selected?Color.FromRgb(187,140,71):Collision?Color.FromRgb(50,(byte)(shade+30),100):Color.FromRgb((byte)(shade+item.Face.Material%3*15),(byte)(shade+15),(byte)(shade+30));
                context.DrawGeometry(Wireframe?null:new SolidColorBrush(color),new Pen(selected?Brushes.Gold:Wireframe?new SolidColorBrush(Color.Parse("#7796AA")):grid,selected?2:1),Polygon(item.Points));
                if(item.Face.ObjectId!=Guid.Empty)_pick.Add((item.Face.ObjectId,item.Points,item.Depth));
            }
            }
            if(CollisionRepairsOverlay)
            {
                int shown=0;
                foreach(MapViewportRepair repair in Cache.CollisionRepairs
                    .OrderByDescending(r=>r.Confidence))
                {
                    if(repair.Points.Length==0||shown>=256)break;
                    IBrush color=repair.Kind switch
                    {
                        MapCollisionRepairKind.FloorProxyAdded => Brushes.LimeGreen,
                        MapCollisionRepairKind.BuriedRestored => Brushes.Cyan,
                        MapCollisionRepairKind.PhantomRemoved => repair.Confidence>=.9f?Brushes.OrangeRed:Brushes.Orange,
                        MapCollisionRepairKind.SpawnMoved or MapCollisionRepairKind.ItemMoved => Brushes.Gold,
                        MapCollisionRepairKind.ProbeFailure or MapCollisionRepairKind.ReachabilityWarning => Brushes.Magenta,
                        MapCollisionRepairKind.SeamStitched or MapCollisionRepairKind.TJunctionStitched => Brushes.DeepSkyBlue,
                        _ => Brushes.LightGreen
                    };
                    if(repair.Points.Length==1)
                    {
                        var point=Project(repair.Points[0]);
                        if(point!=null)context.DrawEllipse(color,new Pen(Brushes.White,1),point.Value.Point,5,5);
                    }
                    else
                    {
                        for(int i=0;i<repair.Points.Length;i++)
                            Line(context,repair.Points[i],repair.Points[(i+1)%repair.Points.Length],color,2);
                    }
                    if(shown<24)
                    {
                        Vector center=repair.Points.Aggregate(Vector.Zero,(a,b)=>a+b)/repair.Points.Length;
                        Label(context,Guid.Empty,$"{repair.Kind} · {repair.Confidence*100:0}% ",center);
                    }
                    shown++;
                }
            }
            if(CollisionHeatmap)
            {
                int shown=0;
                foreach(var cell in Cache.CollisionHeat)
                {
                    if(shown>=192)break;
                    if(!new MapViewportChunk(Guid.Empty,cell.Bounds,Array.Empty<MapViewportFace>(),Array.Empty<MapViewportFace>())
                        .Visible(Camera,Layout))continue;
                    var p=Project(cell.Bounds.Center);if(p==null)continue;
                    double radius=4+Math.Min(18,Math.Sqrt(cell.ReferenceCost)/5);
                    byte alpha=(byte)Math.Clamp(70+cell.Severity*150,70,220);
                    var brush=new SolidColorBrush(Color.FromArgb(alpha,255,(byte)(210*(1-cell.Severity)),40));
                    context.DrawEllipse(brush,new Pen(Brushes.OrangeRed,1),p.Value.Point,radius,radius);
                    if(shown<24)Label(context,Guid.Empty,$"{cell.Faces:N0} faces · {cell.ReferenceCost:N0} refs",cell.Bounds.Center);
                    shown++;
                }
            }
            if (!ReferenceEquals(_overlayDiagnostics, Document.Diagnostics))
            {
                _overlayDiagnostics = Document.Diagnostics;
                _warningObjects = Document.Diagnostics.Diagnostics.Where(d => d.ObjectId.HasValue && d.Severity != MapDiagnosticSeverity.Info)
                    .Select(d => d.ObjectId!.Value).ToHashSet();
                _labels.Clear();
            }
            int spawnIndex = 0;
            foreach(var o in Cache.Entities)
            {
                if (o.Value is MapSpawn) spawnIndex++;
                Vector position = Vector.Transform(MapViewportScene.Vector(o.Position), _drag && Document.Selection.Contains(o.Id) ? PreviewTransform(o) : Matrix4x4.Identity);
                var p=Project(position);if(p==null)continue;
                bool warning = _warningObjects.Contains(o.Id);
                IBrush color = warning ? Brushes.OrangeRed : o.Value is MapSpawn teamSpawn
                    ? teamSpawn.Team == 0 ? Brushes.DeepSkyBlue : teamSpawn.Team == 1 ? Brushes.Orange : Brushes.LimeGreen
                    : o.Value is MapItem ? Brushes.DeepSkyBlue : Brushes.Magenta;
                context.DrawEllipse(color,new Pen(Document.Selection.Contains(o.Id)?Brushes.Gold:Brushes.White,2),p.Value.Point,6,6);
                _pick.Add((o.Id,new[]{p.Value.Point-new Avalonia.Vector(8,8),p.Value.Point+new Avalonia.Vector(8,-8),p.Value.Point+new Avalonia.Vector(8,8),p.Value.Point+new Avalonia.Vector(-8,8)},0));
                if(o.Value is MapSpawn spawn)
                {
                    Label(context, o.Id, $"Spawn {spawnIndex} · {(spawn.Team < 0 ? "Neutral" : "Team " + (char)('A' + spawn.Team))}{(warning ? " · Check" : "")}", position + Vector.UnitY * 2.2f);
                    float angle=spawn.Yaw*MathF.PI/180;
                    Line(context,position,position+new Vector(MathF.Sin(angle),0,MathF.Cos(angle))*2,color,2);
                    Line(context,position,position+Vector.UnitY*1.9f,color);
                    if(EntityVisualization)
                    {
                        Ring(context,position,.45f,color,1.5);
                        Ring(context,position+Vector.UnitY*1.9f,.45f,color,1.5);
                        for(int i=0;i<4;i++)
                        {
                            float a=i*MathF.PI/2;Vector offset=new(MathF.Cos(a)*.45f,0,MathF.Sin(a)*.45f);
                            Line(context,position+offset,position+offset+Vector.UnitY*1.9f,color,1);
                        }
                    }
                }
                if(o.Value is MapItem item&&EntityVisualization)
                    Label(context,o.Id,$"{item.Type} · respawn {item.SpawnInterval/30f:0.0}s",position+Vector.UnitY*.8f);
                if(o.Value is MapNavigationLink link)Line(context,position,MapViewportScene.Vector(link.To),Brushes.Orange,3);
                if(o.Value is MapJumpPad triggerPad&&EntityVisualization&&triggerPad.Size?.Length==3)
                    WireBox(context,position,new Vector(triggerPad.Size[0],triggerPad.Size[1],triggerPad.Size[2])*.5f,Brushes.Magenta,1.5);
                if(o.Value is MapJumpPad pad && MapValidator.Vector(pad.Position) && ((pad.Target!=null)!=(pad.Vector!=null)))
                {
                    try
                    {
                        var(v,speed)=MapBuilder.SolveJumpPad(pad);Vector velocity=new(v.X*speed,v.Y*speed,v.Z*speed),previous=position;
                        const float gravity = 77 / 4096f;
                        float duration = pad.Target == null ? 90 : (velocity.Y + MathF.Sqrt(MathF.Max(0,
                            velocity.Y * velocity.Y - 2 * gravity * (pad.Target[1] - pad.Position[1])))) / gravity;
                        Vector At(float t) => position + velocity * t - Vector.UnitY * (.5f * gravity * t * t);
                        for(int i=1;i<=60;i++){Vector point=At(duration*i/60);Line(context,previous,point,color);previous=point;}
                        Vector apex = At(Math.Clamp(velocity.Y / gravity, 0, duration));
                        Label(context, o.Id, $"Apex · {duration / 30:0.0}s flight", apex);
                        var landing = Project(previous);
                        if (landing != null)
                        {
                            context.DrawEllipse(null, new Pen(color, 2), landing.Value.Point, 7, 7);
                            var point = landing.Value.Point;
                            _pick.Add((o.Id, new[] { point + new Avalonia.Vector(-9,-9), point + new Avalonia.Vector(9,-9),
                                point + new Avalonia.Vector(9,9), point + new Avalonia.Vector(-9,9) }, 0));
                        }
                        Label(context, o.Id, pad.Target == null ? "Landing estimate" : "Target", previous);
                    }
                    catch(ProgramException){ }
                }
            }
            if(KillPlane)
            {
                float y=Document.Project.Definition.KillHeight;
                for(int i=-64;i<=64;i+=8)Line(context,new(i,y,-64),new(i,y,64),Brushes.IndianRed);
            }
            if (Navigation is { } pathGraph)
                for (int i = 1; i < NavigationPath.Length; i++)
                {
                    int a = NavigationPath[i-1], b = NavigationPath[i];
                    if ((uint)a >= pathGraph.Positions.Length || (uint)b >= pathGraph.Positions.Length) continue;
                    var p = pathGraph.Positions[a]; var q = pathGraph.Positions[b];
                    Line(context, new(p.X,p.Y,p.Z), new(q.X,q.Y,q.Z), Brushes.Gold, 4);
                }
            if(Navigation!=null)
                for(int i=0;i<Navigation.Positions.Length;i++)
                {
                    var p=Navigation.Positions[i];Vector a=new(p.X,p.Y,p.Z);var projectedPoint=Project(a);
                    IBrush color=Navigation.Components[i]%2==0?Brushes.Cyan:Brushes.Orange;
                    if(projectedPoint!=null)context.DrawEllipse(color,null,projectedPoint.Value.Point,2,2);
                    foreach(int n in Navigation.Neighbours[i]){var q=Navigation.Positions[n];Line(context,a,new(q.X,q.Y,q.Z),color);}
                }
            if (_boxSelect)
            {
                Rect box = SelectionRectangle(_start, _boxCurrent);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(32, 64, 190, 255)), box);
                context.DrawRectangle(new Pen(Brushes.DeepSkyBlue, 1), box);
            }
            DrawSubSelection(context);
            if(_measureA is { } measureA)
            {
                var screen=Project(measureA);
                if(screen!=null)context.DrawEllipse(Brushes.Gold,new Pen(Brushes.White,1),screen.Value.Point,5,5);
                if(_measureB is { } measureB)
                {
                    Line(context,measureA,measureB,Brushes.Gold,3);
                    Vector midpoint=(measureA+measureB)/2;
                    Label(context,Guid.Empty,$"{Vector.Distance(measureA,measureB):0.###} units",midpoint);
                }
                else Label(context,Guid.Empty,"Measurement start",measureA);
            }
            var selectedObject=ActiveSelection;
            if(selectedObject!=null&&ElementMode=="Object")
            {
                Vector center=MapViewportScene.Vector(selectedObject.Position)+_preview;
                Line(context,center,center+GizmoAxis(selectedObject,0)*3,Brushes.Red,3);Line(context,center,center+GizmoAxis(selectedObject,1)*3,Brushes.Lime,3);Line(context,center,center+GizmoAxis(selectedObject,2)*3,Brushes.DeepSkyBlue,3);
            }
        }
        private static Rect SelectionRectangle(Point a, Point b)
        {
            double x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            return new Rect(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }
        private static bool Intersects(Rect selection, Point[] polygon)
        {
            if (polygon.Length == 0) return false;
            double minX = polygon.Min(p => p.X), maxX = polygon.Max(p => p.X);
            double minY = polygon.Min(p => p.Y), maxY = polygon.Max(p => p.Y);
            return selection.Intersects(new Rect(minX, minY, Math.Max(1, maxX - minX), Math.Max(1, maxY - minY)));
        }

        private static bool Contains(Point[] polygon,Point p)
        {
            bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++)
                if((polygon[i].Y>p.Y)!=(polygon[j].Y>p.Y)&&p.X<(polygon[j].X-polygon[i].X)*(p.Y-polygon[i].Y)/(polygon[j].Y-polygon[i].Y)+polygon[i].X)inside=!inside;
            return inside;
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (_drag || _boxSelect || _orbit || _pan) { e.Handled=true; return; }
            base.OnPointerPressed(e);Focus();_last=_start=e.GetPosition(this);var props=e.GetCurrentPoint(this).Properties;
            _orbit=props.IsRightButtonPressed;_pan=props.IsMiddleButtonPressed;
            if(props.IsLeftButtonPressed)
            {
                MapPickHit? surface=MapViewportPicking.PickHit(BuildRenderFrame(Layout),_last.X,_last.Y,true);
                if(surface!=null)LastSurfaceHit=surface;

                if(MaterialEyedropper)
                {
                    if(surface is { } materialHit){MaterialEyedropper=false;MaterialPicked?.Invoke(materialHit);}
                    e.Pointer.Capture(null);e.Handled=true;InvalidateVisual();return;
                }
                if(MeasureMode)
                {
                    Vector point=surface?.Point??CameraTarget;
                    if(_measureA==null||_measureB!=null){_measureA=point;_measureB=null;}
                    else _measureB=point;
                    e.Pointer.Capture(null);e.Handled=true;InvalidateVisual();return;
                }
                if(ElementMode!="Object"&&PickSubElement(_last))
                {
                    SelectionChanged?.Invoke();e.Pointer.Capture(null);e.Handled=true;InvalidateVisual();return;
                }

                Guid id=Guid.Empty;_axis=-1;
                var selected=ActiveSelection;
                if(selected!=null)
                    for(int i=0;i<3;i++)
                    {
                        var v=GizmoAxis(selected,i);
                        var p=Project(MapViewportScene.Vector(selected.Position)+v*3);
                        var center=Project(MapViewportScene.Vector(selected.Position));
                        // An axis pointing into the camera collapses onto the object center.
                        // It must not steal ordinary object drags in orthographic views.
                        if(p!=null&&center!=null&&Math.Pow(p.Value.Point.X-center.Value.Point.X,2)+Math.Pow(p.Value.Point.Y-center.Value.Point.Y,2)>324
                            &&Math.Pow(p.Value.Point.X-_last.X,2)+Math.Pow(p.Value.Point.Y-_last.Y,2)<144)
                        {_axis=i;id=selected.Id;break;}
                    }
                if(id==Guid.Empty)
                {
                    // Entity handles sit above geometry; brush hits use world distance.
                    id=_pick.Where(p=>p.Depth==0&&Contains(p.Points,_last)).Select(p=>p.Id).FirstOrDefault();
                    if(id==Guid.Empty)id=MapViewportPicking.Pick(BuildRenderFrame(Layout),_last.X,_last.Y);
                }
                _boxAdditive=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                if(!_boxAdditive&&!Document.Selection.Contains(id))Document.Selection.Clear();
                if(id!=Guid.Empty)
                {
                    if(Document.ActiveObjectId!=id)ClearSubSelection();
                    Document.Selection.Add(id);Document.ActiveObjectId=id;
                }
                else { _boxSelect=true; _boxCurrent=_start; }
                _drag=id!=Guid.Empty;Document.SelectionChanged();SelectionChanged?.Invoke();InvalidateVisual();
            }
            _interactionPointer=e.Pointer;e.Pointer.Capture(this);e.Handled=true;
        }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);Point p=e.GetPosition(this);var delta=p-_last;_last=p;
            if (_boxSelect) { _boxCurrent=p; InvalidateVisual(); return; }
            var(right,up,forward)=Basis();float distance=Vector.Distance(CameraPosition,CameraTarget);
            if(_orbit)
            {
                Vector offset=CameraPosition-CameraTarget;
                offset=Vector.Transform(offset,Quaternion.CreateFromAxisAngle(Vector.UnitY,(float)-delta.X*.01f));
                Vector rotated=Vector.Transform(offset,Quaternion.CreateFromAxisAngle(right,(float)-delta.Y*.01f));
                if(Math.Abs(Vector.Dot(Vector.Normalize(rotated),Vector.UnitY))<.98f)offset=rotated;
                CameraPosition=CameraTarget+offset;
            }
            if(_pan){Vector move=(-right*(float)delta.X+up*(float)delta.Y)*distance/500;CameraPosition+=move;CameraTarget+=move;}
            if(_drag)
            {
                var full=p-_start;_preview=(right*(float)full.X-up*(float)full.Y)*distance/500;
                if (LocalAxes && ActiveSelection?.Value is MapGeometry activeGeometry)
                { var r = activeGeometry.Transform.Rotation; _preview = Vector.Transform(_preview, Quaternion.Inverse(new Quaternion(r[0],r[1],r[2],r[3]))); }
                if(_axis>=0){float value=_preview[_axis];_preview=Vector.Zero;_preview[_axis]=value;}
                else if(Axes!="Free"){for(int i=0;i<3;i++)if(!Axes.Contains("XYZ"[i]))_preview[i]=0;}
                else if(View=="Perspective"||View=="Top")_preview.Y=0;
                else if(View=="Front")_preview.Z=0;
                else if(View=="Side")_preview.X=0;
                if(Snap>0)for(int i=0;i<3;i++)_preview[i]=MathF.Round(_preview[i]/Snap)*Snap;
                _rotation=MathF.Round((float)full.X/Math.Max(1,AngleSnap))*Math.Max(1,AngleSnap);
                float scaleStep=float.IsFinite(ScaleSnap)&&ScaleSnap>0?ScaleSnap:.01f;
                _scale=Math.Max(scaleStep,1+MathF.Round((float)full.X/100/scaleStep)*scaleStep);
                if(Tool!="Move")_preview=Vector.Zero;
            }
            InvalidateVisual();
        }
        internal void CancelInteraction()
        {
            _drag=_orbit=_pan=_boxSelect=false;
            _preview=Vector.Zero;_rotation=0;_scale=1;_axis=-1;
            var pointer=_interactionPointer;_interactionPointer=null;
            pointer?.Capture(null);
            InvalidateVisual();
        }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            CancelInteraction();
        }
        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            var buttons=e.GetCurrentPoint(this).Properties;
            if (((_drag||_boxSelect)&&buttons.IsLeftButtonPressed)
                ||(_orbit&&buttons.IsRightButtonPressed)||(_pan&&buttons.IsMiddleButtonPressed))return;
            if (_boxSelect)
            {
                Rect selection = SelectionRectangle(_start, _boxCurrent);
                if (!_boxAdditive) Document.Selection.Clear();
                foreach (Guid id in _pick.Where(p => p.Id != Guid.Empty && Intersects(selection, p.Points))
                    .Select(p => p.Id).Distinct())
                    Document.Selection.Add(id);
                Document.ActiveObjectId = Document.Selection.FirstOrDefault();
                _boxSelect = false;
                Document.SelectionChanged(); SelectionChanged?.Invoke();
            }
            else if(_drag)
            {
                var ids=Document.Selection.ToHashSet();Vector move=_preview;float angle=_rotation,scale=_scale;
                Document.TransformSelection(ids, Tool, move, angle, scale, LocalAxes, rotationAxis:TurnAxis, scaleAxes:ScaleAxes, pivot:TransformPivot);
            }
            CancelInteraction();e.Handled=true;
        }
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            float distance = Math.Clamp(Vector.Distance(CameraPosition, CameraTarget) * (float)Math.Pow(.88, e.Delta.Y), .25f, 5000);
            CameraPosition = CameraTarget - Camera.Basis().Forward * distance;
            InvalidateVisual(); e.Handled = true;
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool control=(e.KeyModifiers & (KeyModifiers.Control|KeyModifiers.Meta))!=0;
            bool shift=e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool alt=e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            if(e.Key==Key.Escape&&(_drag||_boxSelect||_orbit||_pan)){CancelInteraction();}
            else if(_drag||_boxSelect){e.Handled=true;return;}
            else if(!control&&!alt&&e.Key==Key.F)FrameSelection();
            else if(e.Key==Key.Escape&&MeasureMode){MeasureMode=false;_measureA=_measureB=null;InvalidateVisual();}
            else if(e.Key==Key.Escape&&_boxSelect){_boxSelect=false;InvalidateVisual();}
            else if(!control&&!alt&&e.Key==Key.D1){ElementMode="Object";ClearSubSelection();SelectionChanged?.Invoke();}
            else if(!control&&!alt&&e.Key==Key.D2){ElementMode="Face";ClearSubSelection();SelectionChanged?.Invoke();}
            else if(!control&&!alt&&e.Key==Key.D3){ElementMode="Edge";ClearSubSelection();SelectionChanged?.Invoke();}
            else if(!control&&!alt&&e.Key==Key.D4){ElementMode="Vertex";ClearSubSelection();SelectionChanged?.Invoke();}
            else if(!control&&!alt&&e.Key==Key.M){MeasureMode=!MeasureMode;if(!MeasureMode)_measureA=_measureB=null;InvalidateVisual();}
            else if(e.Key==Key.Delete){var ids=Document.Selection.ToHashSet();Document.EditObjects("Delete selection",ids,d=>MapObjects.Delete(d,ids));}
            else if(control&&e.Key==Key.A)Document.SelectAllObjects();
            else if(control&&e.Key==Key.C)Document.CopySelection();
            else if(control&&e.Key==Key.V)Document.PasteClipboard();
            else if(control&&e.Key==Key.D){var ids=Document.Selection.ToHashSet();Document.EditObjects("Duplicate selection",ids,d=>MapObjects.Duplicate(d,ids));}
            else if(control&&e.Key==Key.Z){if(shift)Document.History.Redo();else Document.History.Undo();}
            else if(control&&e.Key==Key.Y)Document.History.Redo();
            else if(alt&&e.Key==Key.H)Document.ShowAllGeometry();
            else if(shift&&e.Key==Key.H)Document.IsolateSelection();
            else if(e.Key==Key.H)Document.HideSelection();
            else if(!control&&!alt&&e.Key==Key.G){Tool="Move";}
            else if(!control&&!alt&&e.Key==Key.R){Tool="Rotate";}
            else if(!control&&!alt&&e.Key==Key.T){Tool="Scale";}
            else
            {
                if(control||alt){base.OnKeyDown(e);return;}
                var(right,up,forward)=Basis();Vector move=e.Key switch {Key.W=>forward,Key.S=>-forward,Key.A=>-right,Key.D=>right,Key.Q=>-up,Key.E=>up,_=>Vector.Zero};
                if(move==Vector.Zero){base.OnKeyDown(e);return;}CameraPosition+=move;CameraTarget+=move;InvalidateVisual();
            }
            e.Handled=true;
        }
        public void SetView(string view)
        {
            View=view;float distance=Vector.Distance(CameraPosition,CameraTarget);
            CameraPosition=CameraTarget+(view switch {"Top"=>new Vector(0,distance,.01f),"Front"=>new Vector(0,0,distance),"Side"=>new Vector(distance,0,0),_=>Vector.Normalize(new Vector(1,.8f,1))*distance});InvalidateVisual();
        }
    }
}
