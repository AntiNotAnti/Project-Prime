using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>Deterministic BSP clipping for authored convex brushes. Imported meshes are deliberately excluded.</summary>
public static class MapCsgService
{
    private sealed record Corner(Vector3 Position,Vector2 Uv)
    { public Corner Lerp(Corner other,float t)=>new(Vector3.Lerp(Position,other.Position,t),Vector2.Lerp(Uv,other.Uv,t)); }
    private sealed class Polygon
    {
        public List<Corner> Corners;
        public Vector3 Normal;
        public float Distance;
        public MapGeometry Source;
        public int Material;
        public Polygon(IEnumerable<Corner> corners,MapGeometry source,int material)
        {
            Corners=corners.ToList();Source=source;Material=material;
            Vector3 normal=Vector3.Zero;for(int i=0;i<Corners.Count;i++)normal+=Vector3.Cross(Corners[i].Position-Corners[0].Position,Corners[(i+1)%Corners.Count].Position-Corners[0].Position);
            Normal=Vector3.Normalize(normal);Distance=Vector3.Dot(Normal,Corners[0].Position);
        }
        public void Flip(){Corners.Reverse();Normal=-Normal;Distance=-Distance;}
    }
    private sealed class Node
    {
        private Vector3 _normal;
        private float _distance;
        private bool _plane;
        private readonly float _epsilon;
        private readonly List<Polygon> _polygons=new();
        private Node? _front,_back;
        public Node(IEnumerable<Polygon> polygons,float epsilon){_epsilon=epsilon;Build(polygons.ToList());}
        private void Split(Polygon polygon,List<Polygon> coplanarFront,List<Polygon> coplanarBack,List<Polygon> front,List<Polygon> back)
        {
            int type=0;var types=new int[polygon.Corners.Count];
            for(int i=0;i<types.Length;i++){float t=Vector3.Dot(_normal,polygon.Corners[i].Position)-_distance;types[i]=t < -_epsilon?2:t>_epsilon?1:0;type|=types[i];}
            if(type==0){(Vector3.Dot(_normal,polygon.Normal)>0?coplanarFront:coplanarBack).Add(polygon);return;}
            if(type==1){front.Add(polygon);return;}if(type==2){back.Add(polygon);return;}
            var f=new List<Corner>();var b=new List<Corner>();
            for(int i=0;i<types.Length;i++)
            {
                int j=(i+1)%types.Length;var a=polygon.Corners[i];var next=polygon.Corners[j];
                if(types[i]!=2)f.Add(a);if(types[i]!=1)b.Add(a);
                if((types[i]|types[j])==3){float denominator=Vector3.Dot(_normal,next.Position-a.Position);float t=Math.Clamp((_distance-Vector3.Dot(_normal,a.Position))/denominator,0,1);var corner=a.Lerp(next,t);f.Add(corner);b.Add(corner);}
            }
            void Add(List<Corner> corners,List<Polygon> list)
            {
                for(int i=corners.Count-1;i>=0&&corners.Count>2;i--)if(Vector3.DistanceSquared(corners[i].Position,corners[(i+1)%corners.Count].Position)<_epsilon*_epsilon)corners.RemoveAt(i);
                if(corners.Count<3)return;var value=new Polygon(corners,polygon.Source,polygon.Material);if(float.IsFinite(value.Normal.X))list.Add(value);
            }
            Add(f,front);Add(b,back);
        }
        public List<Polygon> Clip(List<Polygon> polygons)
        {
            if(!_plane)return polygons.ToList();var front=new List<Polygon>();var back=new List<Polygon>();
            foreach(var p in polygons)Split(p,front,back,front,back);
            if(_front!=null)front=_front.Clip(front);back=_back?.Clip(back)??new();front.AddRange(back);return front;
        }
        public void ClipTo(Node other){var clipped=other.Clip(_polygons);_polygons.Clear();_polygons.AddRange(clipped);_front?.ClipTo(other);_back?.ClipTo(other);}
        public void Invert(){foreach(var p in _polygons)p.Flip();_normal=-_normal;_distance=-_distance;_front?.Invert();_back?.Invert();(_front,_back)=(_back,_front);}
        public List<Polygon> All(){var result=_polygons.ToList();if(_front!=null)result.AddRange(_front.All());if(_back!=null)result.AddRange(_back.All());return result;}
        public void Build(List<Polygon> polygons)
        {
            if(polygons.Count==0)return;if(!_plane){_plane=true;_normal=polygons[0].Normal;_distance=polygons[0].Distance;}
            var front=new List<Polygon>();var back=new List<Polygon>();foreach(var p in polygons)Split(p,_polygons,_polygons,front,back);
            if(front.Count>0){if(_front==null)_front=new(front,_epsilon);else _front.Build(front);}
            if(back.Count>0){if(_back==null)_back=new(back,_epsilon);else _back.Build(back);}
        }
    }
    public static IReadOnlyList<MapMesh> Execute(MapGeometry a,MapGeometry b,string operation,float texScaleA=16,float texScaleB=16)
    {
        List<Polygon> Read(MapGeometry geometry,float scale)
        {
            if(geometry is not (MapBox or MapWedge or MapPrism or MapConvexBrush))throw new InvalidOperationException("CSG accepts authored boxes, wedges, prisms and convex brushes.");
            if(geometry.Locked)throw new InvalidOperationException("Unlock CSG operands first.");
            return GeometryCompiler.Compile(geometry,scale).Select(face=>new Polygon(face.Points.Select((p,i)=>new Corner(new(p.X,p.Y,p.Z),new(face.Texcoords[i].X,face.Texcoords[i].Y))),geometry,face.Material)).ToList();
        }
        var left=Read(a,texScaleA);var right=Read(b,texScaleB);
        var positions=left.Concat(right).SelectMany(p=>p.Corners).Select(c=>c.Position).ToArray();
        float extent=(positions.Aggregate(Vector3.Min)-positions.Aggregate(Vector3.Max)).Length();
        float epsilon=Math.Clamp(extent*1e-7f,1e-5f,.001f);
        var x=new Node(left,epsilon);var y=new Node(right,epsilon);
        switch(operation)
        {
            case "Union":x.ClipTo(y);y.ClipTo(x);y.Invert();y.ClipTo(x);y.Invert();x.Build(y.All());break;
            case "Difference":case "Subtract":x.Invert();x.ClipTo(y);y.ClipTo(x);y.Invert();y.ClipTo(x);y.Invert();x.Build(y.All());x.Invert();break;
            case "Intersection":case "Intersect":x.Invert();y.ClipTo(x);y.Invert();x.ClipTo(y);y.ClipTo(x);x.Build(y.All());x.Invert();break;
            default:throw new ArgumentException("Unknown CSG operation.");
        }
        var polygons=x.All();
        // Cut walls inherit the minuend's physical properties while retaining the cutter's material/UVs.
        if(operation is "Difference" or "Subtract")foreach(var p in polygons)p.Source=a;
        var result=new List<MapMesh>();
        foreach(var group in polygons.GroupBy(p=>new{p.Source.Solid,p.Source.Damaging,p.Source.Terrain,p.Source.Layer,p.Source.Shade}))
        {
            var source=group.First().Source;
            var mesh=new MapMesh{Label=operation+" "+a.Label,Material=source.Material,Solid=source.Solid,Damaging=source.Damaging,Terrain=source.Terrain,Layer=source.Layer,Shade=source.Shade};
            var vertices=new Dictionary<(long,long,long),int>();
            int Vertex(Vector3 p){var key=((long)Math.Round(p.X/epsilon),(long)Math.Round(p.Y/epsilon),(long)Math.Round(p.Z/epsilon));if(vertices.TryGetValue(key,out int i))return i;i=mesh.Vertices.Count;mesh.Vertices.Add(new[]{p.X,p.Y,p.Z});vertices.Add(key,i);return i;}
            foreach(var polygon in group)
            {
                IEnumerable<Corner[]> rings=polygon.Corners.Count<=32?new[]{polygon.Corners.ToArray()}:Enumerable.Range(1,polygon.Corners.Count-2).Select(i=>new[]{polygon.Corners[0],polygon.Corners[i],polygon.Corners[i+1]});
                foreach(var ring in rings){var indices=ring.Select(c=>Vertex(c.Position)).ToArray();if(indices.Distinct().Count()!=indices.Length)continue;mesh.Faces.Add(indices);mesh.FaceMaterials.Add(polygon.Material);mesh.FaceTexcoords.Add(ring.Select(c=>new[]{c.Uv.X,c.Uv.Y}).ToArray());}
            }
            if(mesh.Faces.Count>0){MapMeshEditing.Compact(mesh);MapMeshEditing.CheckOperation(mesh);result.Add(mesh);}
        }
        return result;
    }
}
