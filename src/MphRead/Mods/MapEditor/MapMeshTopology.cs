using System;
using System.Collections.Generic;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

public readonly record struct MapEdge
{
    public int A { get; }
    public int B { get; }
    public MapEdge(int a, int b) { A = Math.Min(a, b); B = Math.Max(a, b); }
}

/// <summary>Transient adjacency; serialized meshes remain indexed polygons with corner UVs.</summary>
public sealed class MapMeshTopology
{
    public sealed record HalfEdge(int From, int To, int Face, int Corner, int Next);
    private readonly Dictionary<MapEdge, List<HalfEdge>> _edges = new();
    private readonly Dictionary<int, HashSet<int>> _vertices = new();
    private readonly MapEdge[][] _faces;
    public IReadOnlyList<HalfEdge> HalfEdges { get; }
    public IEnumerable<MapEdge> Edges => _edges.Keys.OrderBy(e => e.A).ThenBy(e => e.B);
    public MapMeshTopology(MapMesh mesh)
    {
        _faces = new MapEdge[mesh.Faces.Count][];
        var halfEdges = new List<HalfEdge>();
        for (int f = 0; f < mesh.Faces.Count; f++)
        {
            int[] face = mesh.Faces[f];
            _faces[f] = new MapEdge[face.Length];
            int first = halfEdges.Count;
            for (int c = 0; c < face.Length; c++)
            {
                int a = face[c], b = face[(c + 1) % face.Length];
                var edge = new MapEdge(a, b);
                var half = new HalfEdge(a, b, f, c, first + (c + 1) % face.Length);
                halfEdges.Add(half); _faces[f][c] = edge;
                if (!_edges.TryGetValue(edge, out var adjacent)) _edges[edge] = adjacent = new();
                adjacent.Add(half);
                if (!_vertices.TryGetValue(a, out var faces)) _vertices[a] = faces = new();
                faces.Add(f);
            }
        }
        HalfEdges = halfEdges.AsReadOnly();
    }
    public IReadOnlyList<HalfEdge> Uses(MapEdge edge) => _edges.TryGetValue(edge, out var uses) ? uses : Array.Empty<HalfEdge>();
    public int[] AdjacentFaces(MapEdge edge) => Uses(edge).Select(h => h.Face).Distinct().Order().ToArray();
    public IReadOnlyList<MapEdge> EdgesOfFace(int face) => _faces[face];
    public int[] FacesOfVertex(int vertex) => _vertices.TryGetValue(vertex, out var faces) ? faces.Order().ToArray() : Array.Empty<int>();
    public MapEdge[] BoundaryEdges() => Edges.Where(e => Uses(e).Count == 1).ToArray();
    public MapEdge[] NonManifoldEdges() => Edges.Where(e => Uses(e).Count > 2).ToArray();
    public int[] ConnectedFaces(int face)
    {
        if ((uint)face >= _faces.Length) throw new ArgumentOutOfRangeException(nameof(face));
        var seen = new HashSet<int> { face }; var queue = new Queue<int>(); queue.Enqueue(face);
        while (queue.TryDequeue(out int next))
            foreach (int neighbor in _faces[next].SelectMany(AdjacentFaces))
                if (seen.Add(neighbor)) queue.Enqueue(neighbor);
        return seen.Order().ToArray();
    }
    public int[][] FaceIslands()
    {
        var unseen = Enumerable.Range(0, _faces.Length).ToHashSet(); var result = new List<int[]>();
        while (unseen.Count > 0) { var island = ConnectedFaces(unseen.Min()); result.Add(island); unseen.ExceptWith(island); }
        return result.ToArray();
    }
    /// <summary>Only simple closed boundaries are fillable. Branching/non-manifold boundaries are excluded.</summary>
    public int[][] OpenBoundaries()
    {
        var boundary = BoundaryEdges().ToHashSet(); var result = new List<int[]>();
        var incidence = boundary.SelectMany(e => new[] { (v: e.A, e), (v: e.B, e) })
            .GroupBy(x => x.v).ToDictionary(g => g.Key, g => g.Select(x => x.e).ToArray());
        while (boundary.Count > 0)
        {
            var start = boundary.OrderBy(e => e.A).ThenBy(e => e.B).First();
            var use = Uses(start)[0];
            int first = use.To, current = use.From;
            var loop = new List<int> { first }; boundary.Remove(start);
            while (current != first && incidence[current].Length == 2)
            {
                loop.Add(current);
                var candidates = incidence[current].Where(boundary.Contains).ToArray();
                if (candidates.Length != 1) break;
                var next = candidates[0]; boundary.Remove(next); current = next.A == current ? next.B : next.A;
            }
            if (current == first && loop.Count >= 3) result.Add(loop.ToArray());
        }
        return result.ToArray();
    }
    /// <summary>Continue through opposite edges at regular quad vertices; stop at poles and boundaries.</summary>
    public MapEdge[] EdgeLoop(MapEdge start)
    {
        if (!_edges.ContainsKey(start)) return Array.Empty<MapEdge>();
        var seen = new HashSet<MapEdge> { start };
        void Walk(int vertex, MapEdge previous)
        {
            while (true)
            {
                int[] faces = FacesOfVertex(vertex);
                var incident = faces.SelectMany(EdgesOfFace).Where(e => e.A == vertex || e.B == vertex).Distinct().ToArray();
                if (incident.Length != 4 || faces.Any(f => _faces[f].Length != 4)) return;
                var candidates = incident.Where(e => e != previous && !AdjacentFaces(e).Intersect(AdjacentFaces(previous)).Any()).ToArray();
                if (candidates.Length != 1 || !seen.Add(candidates[0])) return;
                previous = candidates[0]; vertex = previous.A == vertex ? previous.B : previous.A;
            }
        }
        Walk(start.A, start); Walk(start.B, start);
        return seen.OrderBy(e => e.A).ThenBy(e => e.B).ToArray();
    }
    public MapEdge[][] EdgeLoops() => Edges.Select(EdgeLoop).GroupBy(loop => string.Join(";", loop)).Select(g => g.First()).ToArray();
}
