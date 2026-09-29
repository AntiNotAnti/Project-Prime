using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed record MapMeshProblem(MapDiagnosticSeverity Severity,string Message,int? Face=null,int? Vertex=null,MapEdge? Edge=null,bool Cleanup=false);

public static class MapMeshValidator
{
    public static IReadOnlyList<MapMeshProblem> Validate(MapMesh mesh)
    {
        var result=new List<MapMeshProblem>();
        if(mesh.Vertices==null||mesh.Faces==null){result.Add(new(MapDiagnosticSeverity.Error,"Mesh channels are missing."));return result;}
        if(mesh.Vertices.Count<3||mesh.Faces.Count==0)result.Add(new(MapDiagnosticSeverity.Error,"Mesh has no surface geometry."));
        if(mesh.Vertices.Count>65535||mesh.Faces.Count>65535)result.Add(new(MapDiagnosticSeverity.Error,"Mesh exceeds the 65,535 vertex/face limit."));
        bool safe=true;
        for(int i=0;i<mesh.Vertices.Count;i++)if(mesh.Vertices[i] is not {Length:3} p || p.Any(v=>!float.IsFinite(v)))
        {result.Add(new(MapDiagnosticSeverity.Error,$"Vertex {i} has a non-finite or invalid position.",Vertex:i));safe=false;}
        var used=new HashSet<int>();var keys=new HashSet<string>(StringComparer.Ordinal);
        for(int f=0;f<mesh.Faces.Count;f++)
        {
            var face=mesh.Faces[f];
            if(face==null||face.Length<3||face.Length>32||face.Any(i=>i<0||i>=mesh.Vertices.Count)||face.Distinct().Count()!=face.Length)
            {result.Add(new(MapDiagnosticSeverity.Error,$"Face {f} has invalid or repeated indices.",Face:f));safe=false;continue;}
            used.UnionWith(face);
            // Same ring modulo rotation/reversal; vertex sets alone would misidentify different polygons.
            string Ring(bool reverse)=>string.Join(",",Enumerable.Range(0,face.Length).Select(i=>face[(Array.IndexOf(face,face.Min())+(reverse?face.Length-i:i))%face.Length]));
            string key=new[]{Ring(false),Ring(true)}.Order(StringComparer.Ordinal).First();
            if(!keys.Add(key))result.Add(new(MapDiagnosticSeverity.Warning,$"Face {f} duplicates another face.",Face:f,Cleanup:true));
            if(!safe)continue;
            Vector3 Point(int i)=>new(mesh.Vertices[i][0],mesh.Vertices[i][1],mesh.Vertices[i][2]);
            Vector3 normal=Vector3.Zero;
            for(int c=0;c<face.Length;c++)normal+=Vector3.Cross(Point(face[c]),Point(face[(c+1)%face.Length]));
            if(normal.LengthSquared()<1e-12f){result.Add(new(MapDiagnosticSeverity.Error,$"Face {f} has zero area.",Face:f));continue;}
            normal=Vector3.Normalize(normal);
            if(face.Any(i=>MathF.Abs(Vector3.Dot(Point(i)-Point(face[0]),normal))>.001f))result.Add(new(MapDiagnosticSeverity.Warning,$"Face {f} is not planar; triangulate it.",Face:f));
            int drop=MathF.Abs(normal.X)>MathF.Abs(normal.Y)?0:1;if(MathF.Abs(normal.Z)>MathF.Abs(normal[drop]))drop=2;
            Vector2 Project(int i){var p=Point(i);return drop==0?new(p.Y,p.Z):drop==1?new(p.X,p.Z):new(p.X,p.Y);}
            float Cross(Vector2 a,Vector2 b)=>a.X*b.Y-a.Y*b.X;
            bool crossed=false;
            for(int a=0;a<face.Length&&!crossed;a++)for(int b=a+2;b<face.Length;b++)
            {
                if(a==0&&b==face.Length-1)continue;
                var p=Project(face[a]);var q=Project(face[(a+1)%face.Length]);var r=Project(face[b]);var s=Project(face[(b+1)%face.Length]);
                if(Cross(q-p,r-p)*Cross(q-p,s-p)<-1e-10f&&Cross(s-r,p-r)*Cross(s-r,q-r)<-1e-10f){crossed=true;break;}
            }
            if(crossed)result.Add(new(MapDiagnosticSeverity.Error,$"Face {f} intersects itself.",Face:f));
        }
        if(!safe)return result;
        int orphans=mesh.Vertices.Count-used.Count;
        if(orphans>0)result.Add(new(MapDiagnosticSeverity.Info,$"{orphans} unused vertices.",Cleanup:true));
        int duplicates=mesh.Vertices.GroupBy(p=>(p[0],p[1],p[2])).Sum(g=>g.Count()-1);
        if(duplicates>0)result.Add(new(MapDiagnosticSeverity.Info,$"{duplicates} coincident vertices (may be intentional seams).",Cleanup:true));
        var topology=new MapMeshTopology(mesh);
        foreach(var edge in topology.Edges)
        {
            var uses=topology.Uses(edge);
            if(uses.Count>2)result.Add(new(MapDiagnosticSeverity.Warning,$"Edge {edge.A}–{edge.B} is non-manifold.",Edge:edge));
            if(uses.Count==2&&uses[0].From==uses[1].From)result.Add(new(MapDiagnosticSeverity.Warning,$"Edge {edge.A}–{edge.B} has inconsistent winding.",Edge:edge));
            var a=mesh.Vertices[edge.A];var b=mesh.Vertices[edge.B];
            if(Vector3.DistanceSquared(new(a[0],a[1],a[2]),new(b[0],b[1],b[2]))<1e-10f)result.Add(new(MapDiagnosticSeverity.Warning,$"Edge {edge.A}–{edge.B} is tiny.",Edge:edge));
        }
        int boundaries=topology.BoundaryEdges().Length;
        if(mesh.Solid&&boundaries>0)result.Add(new(MapDiagnosticSeverity.Warning,$"Collision mesh has {boundaries} open boundary edges."));
        return result;
    }
}
