using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public static partial class MapMeshEditing
{
    public static MapMesh Clone(MapMesh mesh) => JsonSerializer.Deserialize<MapMesh>(JsonSerializer.Serialize(mesh))!;
    public static void CheckOperation(MapMesh mesh)
    {
        var errors = MapMeshValidator.Validate(mesh).Where(p => p.Severity == MapDiagnosticSeverity.Error).Take(3).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(string.Join(" ", errors.Select(p => p.Message)));
    }
    private static int[] Selection(MapMesh mesh, IEnumerable<int> faces)
    {
        int[] selected=faces.Distinct().Order().ToArray();
        if(selected.Length==0)throw new InvalidOperationException("Select faces first.");
        foreach(int f in selected)RequireFace(mesh,f);
        EnsureChannels(mesh);return selected;
    }
    private static void RemoveFaces(MapMesh mesh, IEnumerable<int> faces)
    {
        EnsureChannels(mesh);
        foreach(int f in faces.Distinct().OrderDescending().ToArray())
        {mesh.Faces.RemoveAt(f);mesh.FaceMaterials.RemoveAt(f);mesh.FaceTexcoords.RemoveAt(f);
            mesh.FaceUv=mesh.FaceUv.Where(p=>p.Key!=f).ToDictionary(p=>p.Key>f?p.Key-1:p.Key,p=>p.Value);}
    }
    private static void AddFace(MapMesh mesh, int[] face, int material, float[][]? uv=null, MapUv? projection=null)
    {if(projection!=null)mesh.FaceUv[mesh.Faces.Count]=projection;mesh.Faces.Add(face);mesh.FaceMaterials.Add(material);mesh.FaceTexcoords.Add(uv);}

    public static int[] SplitEdge(MapMesh mesh, MapEdge edge, float percentage=.5f, int cuts=1)
    {
        RequireVertex(mesh,edge.A);RequireVertex(mesh,edge.B);
        if(!float.IsFinite(percentage)||percentage<=0||percentage>=1||cuts<1||cuts>64)throw new ArgumentOutOfRangeException(nameof(percentage));
        var uses=new MapMeshTopology(mesh).Uses(edge);
        if(uses.Count==0)throw new InvalidOperationException("The selected edge no longer exists.");
        if(uses.Any(h=>mesh.Faces[h.Face].Length+cuts>32))throw new InvalidOperationException("Split would exceed 32 corners per face. Triangulate first.");
        EnsureChannels(mesh);var inserted=new int[cuts];
        float Fraction(int i)=>cuts==1?percentage:(i+1f)/(cuts+1);
        for(int i=0;i<cuts;i++){inserted[i]=mesh.Vertices.Count;mesh.Vertices.Add(A(Vector3.Lerp(V(mesh.Vertices[edge.A]),V(mesh.Vertices[edge.B]),Fraction(i))));}
        foreach(var use in uses)
        {
            var face=mesh.Faces[use.Face];var uv=mesh.FaceTexcoords[use.Face];
            var ring=face.ToList();var corners=uv?.Select(p=>(float[])p.Clone()).ToList();
            bool forward=use.From==edge.A;
            ring.InsertRange(use.Corner+1,forward?inserted:inserted.Reverse());
            if(corners!=null)
            {
                float[] a=uv![use.Corner], b=uv[(use.Corner+1)%uv.Length];
                corners.InsertRange(use.Corner+1,Enumerable.Range(0,cuts).Select(i=>
                {float t=forward?Fraction(i):1-Fraction(cuts-1-i);return new[]{a[0]+(b[0]-a[0])*t,a[1]+(b[1]-a[1])*t};}));
            }
            mesh.Faces[use.Face]=ring.ToArray();mesh.FaceTexcoords[use.Face]=corners?.ToArray();
        }
        return inserted;
    }
    public static void DissolveEdge(MapMesh mesh, MapEdge edge)
    {
        EnsureChannels(mesh);var uses=new MapMeshTopology(mesh).Uses(edge);
        if(uses.Count!=2 || uses[0].From==uses[1].From)throw new InvalidOperationException("Dissolve requires two consistently wound adjacent faces.");
        var first=uses[0];var second=uses[1];
        if(Material(mesh,first.Face)!=Material(mesh,second.Face))throw new InvalidOperationException("Dissolve cannot cross a material boundary.");
        if(Vector3.Dot(Normal(mesh,mesh.Faces[first.Face]),Normal(mesh,mesh.Faces[second.Face]))<.9999f)throw new InvalidOperationException("Dissolve requires coplanar faces.");
        var ring=new List<int>();var coords=new List<float[]>();bool explicitUv=mesh.FaceTexcoords[first.Face]!=null&&mesh.FaceTexcoords[second.Face]!=null;
        foreach(var use in uses)
        {
            int[] face=mesh.Faces[use.Face];
            for(int i=1;i<face.Length;i++)
            {int c=(use.Corner+i)%face.Length;ring.Add(face[c]);if(explicitUv)coords.Add((float[])mesh.FaceTexcoords[use.Face]![c].Clone());}
        }
        if(ring.Count>32||ring.Distinct().Count()!=ring.Count)throw new InvalidOperationException("Dissolve would create an invalid polygon.");
        var projection=mesh.FaceUv.GetValueOrDefault(first.Face);int material=Material(mesh,first.Face);RemoveFaces(mesh,uses.Select(h=>h.Face));AddFace(mesh,ring.ToArray(),material,explicitUv?coords.ToArray():null,projection);
    }
    public static void MergeVertices(MapMesh mesh, IEnumerable<int> vertices, string target="Center", Vector3? cursor=null)
    {
        int[] selected=vertices.Distinct().ToArray();if(selected.Length<2)throw new InvalidOperationException("Select at least two vertices.");
        foreach(int i in selected)RequireVertex(mesh,i);EnsureChannels(mesh);
        int keep=target=="Last"?selected[^1]:selected[0];
        Vector3 position=target=="First"?V(mesh.Vertices[selected[0]]):target=="Last"?V(mesh.Vertices[selected[^1]])
            :target=="Cursor"?cursor??throw new InvalidOperationException("Cursor position is required.")
            :selected.Select(i=>V(mesh.Vertices[i])).Aggregate(Vector3.Zero,(a,b)=>a+b)/selected.Length;
        if(!Finite(position))throw new ArgumentException("Merge target must be finite.");
        var set=selected.ToHashSet();mesh.Vertices[keep]=A(position);var removed=new List<int>();
        for(int f=0;f<mesh.Faces.Count;f++)
        {
            var face=mesh.Faces[f];var uv=mesh.FaceTexcoords[f];var ring=new List<int>();var corners=new List<float[]>();
            for(int c=0;c<face.Length;c++)
            {int index=set.Contains(face[c])?keep:face[c];if(ring.Count>0&&ring[^1]==index)continue;ring.Add(index);if(uv!=null)corners.Add(uv[c]);}
            if(ring.Count>1&&ring[0]==ring[^1]){ring.RemoveAt(ring.Count-1);if(uv!=null)corners.RemoveAt(corners.Count-1);}
            if(ring.Count<3){removed.Add(f);continue;}
            if(ring.Distinct().Count()!=ring.Count)throw new InvalidOperationException("Merge would create a pinched face.");
            mesh.Faces[f]=ring.ToArray();mesh.FaceTexcoords[f]=uv==null?null:corners.ToArray();
        }
        RemoveFaces(mesh,removed);Compact(mesh);
    }
    public static void CollapseEdge(MapMesh mesh, MapEdge edge, string target="Center", Vector3? cursor=null)
    {
        if(new MapMeshTopology(mesh).Uses(edge).Count==0)throw new InvalidOperationException("Select an existing edge.");
        MergeVertices(mesh,new[]{edge.A,edge.B},target,cursor);
    }
    public static void MergeByDistance(MapMesh mesh, IEnumerable<int> vertices, float distance)
    {
        if(!float.IsFinite(distance)||distance<=0)throw new ArgumentOutOfRangeException(nameof(distance));
        // Merge groups in one remap, retaining original indices until all groups have been processed.
        int[] selected=vertices.Distinct().Order().ToArray();foreach(int i in selected)RequireVertex(mesh,i);
        var representatives=new List<int>();var remap=new Dictionary<int,int>();
        foreach(int i in selected)
        {int match=representatives.FirstOrDefault(j=>Vector3.DistanceSquared(V(mesh.Vertices[i]),V(mesh.Vertices[j]))<=distance*distance,-1);if(match<0){representatives.Add(i);match=i;}remap[i]=match;}
        EnsureChannels(mesh);
        for(int f=0;f<mesh.Faces.Count;f++)
        {
            var ring=new List<int>();var corners=new List<float[]>();var uv=mesh.FaceTexcoords[f];
            for(int c=0;c<mesh.Faces[f].Length;c++)
            {
                int index=remap.GetValueOrDefault(mesh.Faces[f][c],mesh.Faces[f][c]);
                if(ring.Count>0&&ring[^1]==index)continue;
                ring.Add(index);if(uv!=null)corners.Add(uv[c]);
            }
            if(ring.Count>1&&ring[0]==ring[^1]){ring.RemoveAt(ring.Count-1);if(uv!=null)corners.RemoveAt(corners.Count-1);}
            mesh.Faces[f]=ring.ToArray();mesh.FaceTexcoords[f]=uv==null?null:corners.ToArray();
        }
        RemoveFaces(mesh,Enumerable.Range(0,mesh.Faces.Count).Where(f=>mesh.Faces[f].Distinct().Count()<3));
        // Nonadjacent repeated corners describe pinched faces and are rejected by validation.
        Compact(mesh);
    }
    public static void ConnectVertices(MapMesh mesh, int a, int b)
    {
        RequireVertex(mesh,a);RequireVertex(mesh,b);EnsureChannels(mesh);
        int face=mesh.Faces.FindIndex(f=>f.Contains(a)&&f.Contains(b));
        if(face<0)throw new InvalidOperationException("Vertices must share a face.");
        var source=mesh.Faces[face];int x=Array.IndexOf(source,a),y=Array.IndexOf(source,b);
        int distance=(y-x+source.Length)%source.Length;
        if(distance<2||distance>source.Length-2)throw new InvalidOperationException("These vertices already share an edge.");
        int[] first=Enumerable.Range(0,distance+1).Select(i=>(x+i)%source.Length).ToArray();
        int[] second=Enumerable.Range(0,source.Length-distance+1).Select(i=>(y+i)%source.Length).ToArray();
        var uv=mesh.FaceTexcoords[face];var projection=mesh.FaceUv.GetValueOrDefault(face);int material=Material(mesh,face);RemoveFaces(mesh,new[]{face});
        foreach(var corners in new[]{first,second})AddFace(mesh,corners.Select(i=>source[i]).ToArray(),material,uv==null?null:corners.Select(i=>(float[])uv[i].Clone()).ToArray(),projection);
    }
    public static int RipVertex(MapMesh mesh,int vertex,int face)
    {
        RequireVertex(mesh,vertex);RequireFace(mesh,face);int corner=Array.IndexOf(mesh.Faces[face],vertex);
        if(corner<0)throw new InvalidOperationException("The active face does not contain the vertex.");
        int index=mesh.Vertices.Count;mesh.Vertices.Add((float[])mesh.Vertices[vertex].Clone());mesh.Faces[face][corner]=index;return index;
    }
    public static void SlideVertex(MapMesh mesh,int vertex,int toward,float amount)
    {
        if(!float.IsFinite(amount)||amount<0||amount>=1)throw new ArgumentOutOfRangeException(nameof(amount));
        if(new MapMeshTopology(mesh).Uses(new(vertex,toward)).Count==0)throw new InvalidOperationException("Slide target must be connected by an edge.");
        mesh.Vertices[vertex]=A(Vector3.Lerp(V(mesh.Vertices[vertex]),V(mesh.Vertices[toward]),amount));
    }
    public static void SlideEdge(MapMesh mesh,MapEdge edge,int face,float amount)
    {
        RequireFace(mesh,face);var ring=mesh.Faces[face];int a=Array.IndexOf(ring,edge.A),b=Array.IndexOf(ring,edge.B);
        if(a<0||b<0||ring.Length!=4)throw new InvalidOperationException("Edge slide requires an adjacent quad.");
        int Other(int corner,int endpoint){int next=ring[(corner+1)%4];return next==endpoint?ring[(corner+3)%4]:next;}
        SlideVertex(mesh,edge.A,Other(a,edge.B),amount);SlideVertex(mesh,edge.B,Other(b,edge.A),amount);
    }
    public static void Flatten(MapMesh mesh,IEnumerable<int> vertices,int axis)
    {
        var selected=vertices.Distinct().ToArray();if(selected.Length==0||axis<0||axis>2)throw new ArgumentException("Select vertices and an axis.");
        float value=selected.Average(i=>VertexWorld(mesh,i)[axis]);
        foreach(int i in selected){var p=VertexWorld(mesh,i);p[axis]=value;SetVertexWorld(mesh,i,p);}
    }
    public static void ExtrudeRegion(MapMesh mesh,IEnumerable<int> faces,float distance)
    {
        int[] selected=Selection(mesh,faces);if(!float.IsFinite(distance)||MathF.Abs(distance)<1e-5f)throw new ArgumentOutOfRangeException(nameof(distance));
        var topology=new MapMeshTopology(mesh);var set=selected.ToHashSet();var normals=new Dictionary<int,Vector3>();
        foreach(int f in selected)foreach(int v in mesh.Faces[f])normals[v]=normals.GetValueOrDefault(v)+Normal(mesh,mesh.Faces[f]);
        var lifted=new Dictionary<int,int>();
        foreach(var (index,normal) in normals)
        {if(normal.LengthSquared()<1e-10f)throw new InvalidOperationException("Region contains opposing surfaces.");lifted[index]=mesh.Vertices.Count;mesh.Vertices.Add(A(V(mesh.Vertices[index])+Vector3.Normalize(normal)*distance));}
        foreach(var edge in selected.SelectMany(topology.EdgesOfFace).Distinct())
        {
            var uses=topology.Uses(edge);if(uses.Count>2)throw new InvalidOperationException("Cannot extrude a non-manifold region.");
            var boundary=uses.Where(h=>set.Contains(h.Face)).ToArray();if(boundary.Length!=1)continue;
            var h=boundary[0];AddFace(mesh,new[]{h.From,h.To,lifted[h.To],lifted[h.From]},Material(mesh,h.Face));
        }
        foreach(int f in selected)mesh.Faces[f]=mesh.Faces[f].Select(v=>lifted[v]).ToArray();
        Compact(mesh);
    }
    public static void InsetRegion(MapMesh mesh,IEnumerable<int> faces,float ratio)
    {
        int[] selected=Selection(mesh,faces);if(!float.IsFinite(ratio)||ratio<=0||ratio>=.95f)throw new ArgumentOutOfRangeException(nameof(ratio));
        var topology=new MapMeshTopology(mesh);var set=selected.ToHashSet();var pending=set.ToHashSet();
        while(pending.Count>0)
        {
            var region=new HashSet<int>();var queue=new Queue<int>();queue.Enqueue(pending.Min());
            while(queue.TryDequeue(out int f))if(pending.Remove(f)){region.Add(f);foreach(int n in topology.EdgesOfFace(f).SelectMany(topology.AdjacentFaces).Where(set.Contains))queue.Enqueue(n);}
            Vector3 normal=Normal(mesh,mesh.Faces[region.First()]);
            if(region.Any(f=>Vector3.Dot(normal,Normal(mesh,mesh.Faces[f]))<.9999f))throw new InvalidOperationException("Inset region requires a planar surface.");
            var vertices=region.SelectMany(f=>mesh.Faces[f]).Distinct().ToArray();Vector3 center=vertices.Select(i=>V(mesh.Vertices[i])).Aggregate(Vector3.Zero,(a,b)=>a+b)/vertices.Length;
            var inner=new Dictionary<int,int>();foreach(int i in vertices){inner[i]=mesh.Vertices.Count;mesh.Vertices.Add(A(Vector3.Lerp(V(mesh.Vertices[i]),center,ratio)));}
            var boundary=region.SelectMany(topology.EdgesOfFace).Distinct().SelectMany(e=>topology.Uses(e).Where(h=>region.Contains(h.Face)).Count()==1?topology.Uses(e).Where(h=>region.Contains(h.Face)):Array.Empty<MapMeshTopology.HalfEdge>()).ToArray();
            if(boundary.Length==0)throw new InvalidOperationException("Inset needs an open region boundary.");
            foreach(var h in boundary)AddFace(mesh,new[]{h.From,h.To,inner[h.To],inner[h.From]},Material(mesh,h.Face));
            foreach(int f in region)mesh.Faces[f]=mesh.Faces[f].Select(i=>inner[i]).ToArray();
        }
        Compact(mesh);
    }
    public static void BevelEdge(MapMesh mesh,MapEdge edge,float width,int segments=1,int? material=null)
    {
        if(!float.IsFinite(width)||width<=0||segments<1||segments>8)throw new ArgumentOutOfRangeException(nameof(width));
        EnsureChannels(mesh);var topology=new MapMeshTopology(mesh);var uses=topology.Uses(edge);
        if(uses.Count!=2||uses[0].From==uses[1].From)throw new InvalidOperationException("Bevel requires a manifold edge with consistent winding.");
        int[] endFaces=new int[2];
        for(int end=0;end<2;end++)
        {
            var adjacent=topology.FacesOfVertex(end==0?edge.A:edge.B);
            if(adjacent.Length!=3)throw new InvalidOperationException("Edge bevel currently requires three faces at each endpoint; dissolve extra coplanar edges first.");
            endFaces[end]=adjacent.Single(f=>uses.All(h=>h.Face!=f));
            if(mesh.Faces[endFaces[end]].Length+segments>32)throw new InvalidOperationException("Bevel would exceed the face corner limit.");
        }
        var endpoints=new int[2][];var neighbors=new int[2][];
        for(int side=0;side<2;side++)
        {
            var h=uses[side];var face=mesh.Faces[h.Face];var original=(int[])face.Clone();endpoints[side]=new int[2];neighbors[side]=new int[2];
            var originalUv=CloneUv(mesh.FaceTexcoords[h.Face]);
            for(int end=0;end<2;end++)
            {
                int vertex=end==0?edge.A:edge.B,corner=Array.IndexOf(original,vertex);
                int next=original[(corner+1)%face.Length];if(next==edge.A||next==edge.B)next=original[(corner+face.Length-1)%face.Length];neighbors[side][end]=next;
                Vector3 start=V(mesh.Vertices[vertex]),delta=V(mesh.Vertices[next])-start;float length=delta.Length();
                if(width>=length*.49f)throw new InvalidOperationException("Bevel width is too large for adjacent edges.");
                endpoints[side][end]=mesh.Vertices.Count;mesh.Vertices.Add(A(start+delta/length*width));face[corner]=endpoints[side][end];
                if(originalUv!=null){var uv=originalUv[corner];var target=originalUv[Array.IndexOf(original,next)];mesh.FaceTexcoords[h.Face]![corner]=new[]{uv[0]+(target[0]-uv[0])*width/length,uv[1]+(target[1]-uv[1])*width/length};}
            }
        }
        var rows=new List<int[]>{endpoints[0]};
        for(int segment=1;segment<segments;segment++)
        {var row=new int[2];for(int end=0;end<2;end++){row[end]=mesh.Vertices.Count;mesh.Vertices.Add(A(Vector3.Lerp(V(mesh.Vertices[endpoints[0][end]]),V(mesh.Vertices[endpoints[1][end]]),(float)segment/segments)));}rows.Add(row);}
        rows.Add(endpoints[1]);
        for(int segment=0;segment<segments;segment++)
        {var previous=rows[segment];var next=rows[segment+1];int[] strip=new[]{previous[1],previous[0],next[0],next[1]};if(uses[0].From!=edge.A)Array.Reverse(strip);AddFace(mesh,strip,material??Material(mesh,uses[0].Face));}
        for(int end=0;end<2;end++)
        {
            int face=endFaces[end],vertex=end==0?edge.A:edge.B;var ring=mesh.Faces[face].ToList();int corner=ring.IndexOf(vertex);int previous=ring[(corner+ring.Count-1)%ring.Count],next=ring[(corner+1)%ring.Count];
            if(!neighbors.Select(n=>n[end]).ToHashSet().SetEquals(new[]{previous,next}))throw new InvalidOperationException("Endpoint topology cannot be beveled safely.");
            var chain=rows.Select(r=>r[end]).ToArray();if(previous!=neighbors[0][end])Array.Reverse(chain);
            var uv=mesh.FaceTexcoords[face];
            if(uv!=null)
            {
                var old=uv[corner];var p=uv[(corner+uv.Length-1)%uv.Length];var n=uv[(corner+1)%uv.Length];
                float first=width/Vector3.Distance(V(mesh.Vertices[vertex]),V(mesh.Vertices[previous])),last=width/Vector3.Distance(V(mesh.Vertices[vertex]),V(mesh.Vertices[next]));
                var start=new Vector2(old[0]+(p[0]-old[0])*first,old[1]+(p[1]-old[1])*first);var finish=new Vector2(old[0]+(n[0]-old[0])*last,old[1]+(n[1]-old[1])*last);
                var coords=uv.ToList();coords.RemoveAt(corner);coords.InsertRange(corner,Enumerable.Range(0,chain.Length).Select(i=>{var value=Vector2.Lerp(start,finish,(float)i/segments);return new[]{value.X,value.Y};}));mesh.FaceTexcoords[face]=coords.ToArray();
            }
            ring.RemoveAt(corner);ring.InsertRange(corner,chain);mesh.Faces[face]=ring.ToArray();
        }
        Compact(mesh);
    }
    public static MapMesh DuplicateFaces(MapMesh mesh,IEnumerable<int> faces)
    {
        int[] selected=Selection(mesh,faces);var copy=Clone(mesh);copy.Id=Guid.NewGuid();copy.Label=mesh.Label+" Selection";
        copy.Faces=selected.Select(f=>(int[])mesh.Faces[f].Clone()).ToList();copy.FaceMaterials=selected.Select(f=>Material(mesh,f)).ToList();copy.FaceTexcoords=selected.Select(f=>CloneUv(mesh.FaceTexcoords[f])).ToList();
        copy.FaceUv=selected.Select((f,i)=>(f,i)).Where(x=>mesh.FaceUv.ContainsKey(x.f)).ToDictionary(x=>x.i,x=>mesh.FaceUv[x.f]);Compact(copy);return copy;
    }
    public static MapMesh Separate(MapMesh mesh,IEnumerable<int> faces)
    {var selected=Selection(mesh,faces);var copy=DuplicateFaces(mesh,selected);RemoveFaces(mesh,selected);Compact(mesh);return copy;}
    public static MapMesh Join(IEnumerable<MapMesh> meshes,float texScale=16)
    {
        var sources=meshes.ToArray();if(sources.Length==0)throw new InvalidOperationException("Select meshes first.");
        var result=Clone(sources[0]);result.Transform=new();result.Uv=new();result.FaceUv.Clear();result.Vertices.Clear();result.Faces.Clear();result.FaceMaterials.Clear();result.FaceTexcoords.Clear();
        foreach(var source in sources)
        {
            if(source.Solid!=result.Solid||source.CollisionOnly!=result.CollisionOnly||source.Terrain!=result.Terrain||source.Damaging!=result.Damaging||source.Shade!=result.Shade||source.Slipperiness!=result.Slipperiness||source.ReflectBeams!=result.ReflectBeams||source.IgnorePlayers!=result.IgnorePlayers||source.IgnoreBeams!=result.IgnoreBeams||source.IgnoreScan!=result.IgnoreScan)
                throw new InvalidOperationException("Joined meshes must have matching collision, terrain, damage and shade settings.");
            var compiled=GeometryCompiler.Compile(source,texScale);int offset=result.Vertices.Count;
            result.Vertices.AddRange(Enumerable.Range(0,source.Vertices.Count).Select(i=>A(VertexWorld(source,i))));
            for(int f=0;f<source.Faces.Count;f++)AddFace(result,source.Faces[f].Select(i=>i+offset).ToArray(),Material(source,f),compiled[f].Texcoords.Select(p=>new[]{p.X,p.Y}).ToArray());
        }
        return result;
    }
    public static void Fill(MapMesh mesh,IEnumerable<MapEdge> edges)
    {
        EnsureChannels(mesh);var selected=edges.ToHashSet();var loops=new MapMeshTopology(mesh).OpenBoundaries().Where(loop=>Enumerable.Range(0,loop.Length).All(i=>selected.Contains(new(loop[i],loop[(i+1)%loop.Length])))).ToArray();
        if(loops.Length==0)throw new InvalidOperationException("Select a complete, unbranched boundary loop.");
        foreach(var loop in loops)
        {
            if(loop.Length>32)throw new InvalidOperationException("Boundary exceeds the 32-corner face limit.");
            int face=mesh.Faces.Count;AddFace(mesh,loop,mesh.Material);var normal=Normal(mesh,loop);
            bool concave=Enumerable.Range(0,loop.Length).Any(i=>Vector3.Dot(Vector3.Cross(V(mesh.Vertices[loop[(i+1)%loop.Length]])-V(mesh.Vertices[loop[i]]),V(mesh.Vertices[loop[(i+2)%loop.Length]])-V(mesh.Vertices[loop[(i+1)%loop.Length]])),normal)<-1e-8f);
            if(concave)Triangulate(mesh,new[]{face});
        }
    }
    public static void Triangulate(MapMesh mesh,IEnumerable<int> faces)
    {
        foreach(int f in Selection(mesh,faces).OrderDescending())
        {
            var ring=mesh.Faces[f];if(ring.Length==3)continue;var uv=mesh.FaceTexcoords[f];var projection=mesh.FaceUv.GetValueOrDefault(f);int material=Material(mesh,f);
            // Ear clipping supports concave authored polygons and preserves corner UVs.
            Vector3 normal=Normal(mesh,ring);var corners=Enumerable.Range(0,ring.Length).ToList();var triangles=new List<int[]>();
            while(corners.Count>3)
            {
                bool found=false;
                for(int c=0;c<corners.Count;c++)
                {
                    int a=corners[(c+corners.Count-1)%corners.Count],b=corners[c],d=corners[(c+1)%corners.Count];
                    Vector3 pa=V(mesh.Vertices[ring[a]]),pb=V(mesh.Vertices[ring[b]]),pd=V(mesh.Vertices[ring[d]]);
                    if(Vector3.Dot(Vector3.Cross(pb-pa,pd-pb),normal)<=1e-8f)continue;
                    bool inside=corners.Where(i=>i!=a&&i!=b&&i!=d).Any(i=>{Vector3 p=V(mesh.Vertices[ring[i]]);return Vector3.Dot(Vector3.Cross(pb-pa,p-pa),normal)>=-1e-8f&&Vector3.Dot(Vector3.Cross(pd-pb,p-pb),normal)>=-1e-8f&&Vector3.Dot(Vector3.Cross(pa-pd,p-pd),normal)>=-1e-8f;});
                    if(inside)continue;triangles.Add(new[]{a,b,d});corners.RemoveAt(c);found=true;break;
                }
                if(!found)throw new InvalidOperationException("Cannot triangulate a self-intersecting or degenerate polygon.");
            }
            triangles.Add(corners.ToArray());RemoveFaces(mesh,new[]{f});foreach(var triangle in triangles)AddFace(mesh,triangle.Select(i=>ring[i]).ToArray(),material,uv==null?null:triangle.Select(i=>(float[])uv[i].Clone()).ToArray(),projection);
        }
    }
    public static void RecalculateWinding(MapMesh mesh)
    {
        var topology=new MapMeshTopology(mesh);var flip=new Dictionary<int,bool>();
        foreach(var island in topology.FaceIslands())
        {
            var queue=new Queue<int>();flip[island[0]]=false;queue.Enqueue(island[0]);
            while(queue.TryDequeue(out int face))foreach(var edge in topology.EdgesOfFace(face))
            {
                var uses=topology.Uses(edge);if(uses.Count!=2)continue;var a=uses.First(h=>h.Face==face);var b=uses.First(h=>h.Face!=face);
                bool desired=flip[face]^(a.From==b.From);
                if(flip.TryGetValue(b.Face,out bool current)){if(current!=desired)throw new InvalidOperationException("Surface is not orientable.");}
                else{flip[b.Face]=desired;queue.Enqueue(b.Face);}
            }
            double volume=0;foreach(int f in island){var ring=mesh.Faces[f];for(int i=1;i+1<ring.Length;i++)volume+=Vector3.Dot(V(mesh.Vertices[ring[0]]),Vector3.Cross(V(mesh.Vertices[ring[i]]),V(mesh.Vertices[ring[i+1]])))*(flip.GetValueOrDefault(f)?-1:1);}
            if(island.SelectMany(topology.EdgesOfFace).All(e=>topology.Uses(e).Count==2)&&volume<0)foreach(int f in island)flip[f]=!flip[f];
        }
        foreach(var entry in flip.Where(p=>p.Value))FlipFace(mesh,entry.Key);
    }
}
