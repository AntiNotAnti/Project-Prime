using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public sealed class MapSubSelection
{
    public Guid ObjectId { get; set; }
    public HashSet<int> Faces { get; } = new();
    public HashSet<int> Vertices { get; } = new();
    public HashSet<MapEdge> Edges { get; } = new();
    public int ActiveFace { get; set; } = -1;
    private int _activeVertex=-1;
    private readonly List<int> _vertexOrder=new();
    public int ActiveVertex { get=>_activeVertex; set { _activeVertex=value;if(value>=0&&!_vertexOrder.Contains(value))_vertexOrder.Add(value); } }
    public int[] OrderedVertices => _vertexOrder.Where(Vertices.Contains).Concat(Vertices.Order()).Distinct().ToArray();
    public MapEdge? ActiveEdge { get; set; }
    public void Clear()
    { _vertexOrder.Clear(); Faces.Clear(); Vertices.Clear(); Edges.Clear(); ActiveFace = ActiveVertex = -1; ActiveEdge = null; ObjectId = Guid.Empty; }
    public void Bind(Guid id) { if (ObjectId != id) Clear(); ObjectId = id; }
    public int[] VertexIndices(MapMesh mesh, string mode) => (mode switch
    {
        "Face" => Faces.Where(f => (uint)f < mesh.Faces.Count).SelectMany(f => mesh.Faces[f]),
        "Edge" => Edges.SelectMany(e => new[] { e.A, e.B }),
        _ => Vertices
    }).Where(v => (uint)v < mesh.Vertices.Count).Distinct().Order().ToArray();
    public void Validate(MapMesh mesh)
    {
        if (ObjectId != mesh.Id) { Clear(); return; }
        Faces.RemoveWhere(f => (uint)f >= mesh.Faces.Count); Vertices.RemoveWhere(v => (uint)v >= mesh.Vertices.Count);
        var edges = new MapMeshTopology(mesh).Edges.ToHashSet(); Edges.IntersectWith(edges);
        if (!Faces.Contains(ActiveFace)) ActiveFace = Faces.Order().FirstOrDefault(-1);
        if (!Vertices.Contains(ActiveVertex)) ActiveVertex = Vertices.Order().FirstOrDefault(-1);
        if (ActiveEdge is not { } active || !Edges.Contains(active)) ActiveEdge = Edges.Count == 0 ? null : Edges.OrderBy(e => e.A).ThenBy(e => e.B).First();
    }
    public void SelectAll(MapMesh mesh, string mode)
    {
        Bind(mesh.Id);
        if (mode == "Face") Faces.UnionWith(Enumerable.Range(0, mesh.Faces.Count));
        else if (mode == "Edge") Edges.UnionWith(new MapMeshTopology(mesh).Edges);
        else Vertices.UnionWith(Enumerable.Range(0, mesh.Vertices.Count));
        Validate(mesh);
    }
    public void Linked(MapMesh mesh, string mode)
    {
        Bind(mesh.Id); var topology = new MapMeshTopology(mesh);
        int[] seeds = mode == "Face" ? Faces.ToArray() : VertexIndices(mesh, mode).SelectMany(topology.FacesOfVertex).Distinct().ToArray();
        var faces = seeds.SelectMany(topology.ConnectedFaces).Distinct().ToArray();
        if (mode == "Face") Faces.UnionWith(faces);
        else if (mode == "Edge") Edges.UnionWith(faces.SelectMany(topology.EdgesOfFace));
        else Vertices.UnionWith(faces.SelectMany(f => mesh.Faces[f]));
        Validate(mesh);
    }
    public void Resize(MapMesh mesh, string mode, bool grow)
    {
        var topology = new MapMeshTopology(mesh);
        void Change<T>(HashSet<T> selection, Func<T, IEnumerable<T>> neighbors) where T : notnull
        {
            var original = selection.ToHashSet();
            if (grow) selection.UnionWith(original.SelectMany(neighbors));
            else selection.RemoveWhere(item => neighbors(item).Any(n => !original.Contains(n)));
        }
        if (mode == "Face") Change(Faces, f => topology.EdgesOfFace(f).SelectMany(topology.AdjacentFaces));
        else if (mode == "Edge") Change(Edges, e => topology.FacesOfVertex(e.A).Concat(topology.FacesOfVertex(e.B)).SelectMany(topology.EdgesOfFace).Where(n => n.A == e.A || n.B == e.A || n.A == e.B || n.B == e.B));
        else Change(Vertices, v => topology.FacesOfVertex(v).SelectMany(topology.EdgesOfFace).Where(e => e.A == v || e.B == v).Select(e => e.A == v ? e.B : e.A));
        Validate(mesh);
    }
}
