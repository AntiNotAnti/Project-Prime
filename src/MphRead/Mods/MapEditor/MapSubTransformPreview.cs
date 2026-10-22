using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.MapEditor;

/// <summary>Detached positions for a single gesture. No document writes until Commit.</summary>
public sealed class MapSubTransformPreview
{
    private readonly MapMesh _source;
    private readonly Vector3[] _before;
    private readonly Vector3[] _world;
    private readonly Vector3[] _worldPositions;
    private MapViewportMesh? _cachedSource, _cachedPresentation;
    private readonly Matrix4x4 _inverse;
    private readonly Quaternion _orientation;
    public int[] Indices { get; }
    public Vector3[] Positions { get; }
    public Vector3 Pivot { get; }
    public Guid ObjectId => _source.Id;
    public MapSubTransformPreview(MapMesh mesh, MapSubSelection selection, string mode, string pivotMode, Vector3 cursor)
    {
        if (mesh.Locked) throw new InvalidOperationException("Unlock the mesh before editing it.");
        _source = mesh; Indices = selection.VertexIndices(mesh, mode);
        if (Indices.Length == 0) throw new InvalidOperationException("Select mesh elements first.");
        _before = mesh.Vertices.Select(p => new Vector3(p[0], p[1], p[2])).ToArray();
        Positions = (Vector3[])_before.Clone();
        var t = mesh.Transform; _orientation = new(t.Rotation[0], t.Rotation[1], t.Rotation[2], t.Rotation[3]);
        var matrix = Matrix4x4.CreateScale(t.Scale[0], t.Scale[1], t.Scale[2]) * Matrix4x4.CreateFromQuaternion(_orientation)
            * Matrix4x4.CreateTranslation(t.Position[0], t.Position[1], t.Position[2]);
        if (!Matrix4x4.Invert(matrix, out _inverse)) throw new InvalidOperationException("Cannot edit a mesh with a zero scale.");
        _world = _before.Select(p => Vector3.Transform(p, matrix)).ToArray();
        _worldPositions=(Vector3[])_world.Clone();
        IEnumerable<int> active = mode == "Face" && selection.ActiveFace >= 0 ? mesh.Faces[selection.ActiveFace]
            : mode == "Edge" && selection.ActiveEdge is { } edge ? new[] { edge.A, edge.B }
            : selection.ActiveVertex >= 0 ? new[] { selection.ActiveVertex } : Indices;
        Pivot = pivotMode == "World" ? Vector3.Zero : pivotMode == "Cursor" ? cursor
            : (pivotMode == "Active" ? active : Indices).Select(i => _world[i]).Aggregate(Vector3.Zero, (a,b) => a+b)
                / (pivotMode == "Active" ? active.Count() : Indices.Length);
    }
    public void Update(string tool, Vector3 move, float angle, float scale, bool local, Vector3 axis, Vector3 scaleAxes)
    {
        if (!float.IsFinite(angle) || !float.IsFinite(scale) || !Finite(move)) throw new ArgumentException("Transform must be finite.");
        Quaternion orientation = local ? _orientation : Quaternion.Identity;
        var matrix = tool == "Move" ? Matrix4x4.CreateTranslation(Vector3.Transform(move, orientation))
            : Matrix4x4.CreateTranslation(-Pivot) * (tool == "Rotate"
                ? Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Vector3.Transform(axis, orientation)), angle * MathF.PI / 180)
                : Matrix4x4.CreateFromQuaternion(Quaternion.Inverse(orientation)) * Matrix4x4.CreateScale(Vector3.One + scaleAxes * (scale - 1))
                    * Matrix4x4.CreateFromQuaternion(orientation)) * Matrix4x4.CreateTranslation(Pivot);
        _cachedPresentation=null;
        foreach (int i in Indices)
        {
            _worldPositions[i]=Vector3.Transform(_world[i],matrix);
            Positions[i] = Vector3.Transform(_worldPositions[i], _inverse);
            if (!Finite(Positions[i])) throw new ArgumentException("Transform produced non-finite coordinates.");
        }
    }
    public Vector3 World(int index) => _worldPositions[index];
    public MapViewportMesh Present(MapViewportMesh cached)
    {
        if(ReferenceEquals(cached,_cachedSource)&&_cachedPresentation!=null)return _cachedPresentation;
        _cachedSource=cached;
        IReadOnlyList<MapViewportFace> Faces(IReadOnlyList<MapViewportFace> faces) => faces.Select((face, f) =>
            f < _source.Faces.Count && face.Points.Length == _source.Faces[f].Length
                ? face with { Points = _source.Faces[f].Select(World).ToArray() } : face).ToArray();
        return _cachedPresentation=cached with { Faces = Faces(cached.Faces), CollisionFaces = cached.CollisionFaces == null ? null : Faces(cached.CollisionFaces) };
    }
    public void Commit(MapDocument document, string tool)
    {
        if (MapObjects.Find(document.Project.Definition, ObjectId)?.Value is not MapMesh current || !ReferenceEquals(current, _source)
            || current.Vertices.Count != _before.Length || current.Vertices.Where((p,i) => new Vector3(p[0],p[1],p[2]) != _before[i]).Any())
            throw new InvalidOperationException("Mesh changed during the gesture. Start the edit again.");
        var changed = Indices.Where(i => Positions[i] != _before[i]).ToArray();
        if (changed.Length > 0) document.History.Execute(new MapSubTransformCommand(document, ObjectId, tool, changed,
            changed.Select(i => _before[i]).ToArray(), changed.Select(i => Positions[i]).ToArray()));
    }
    private static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
}

public sealed class MapSubTransformCommand : IMapEditCommand
{
    private readonly MapDocument _document;
    private readonly Guid _id;
    private readonly int[] _indices;
    private readonly Vector3[] _before, _after;
    public string Label { get; }
    public long ApproximateBytes => 192L + _indices.Length * 28L;
    public MapDocumentChange Change { get; }
    public MapSubTransformCommand(MapDocument document, Guid id, string tool, int[] indices, Vector3[] before, Vector3[] after)
    {
        _document = document; _id = id; _indices = (int[])indices.Clone(); _before = (Vector3[])before.Clone(); _after = (Vector3[])after.Clone();
        Label = $"{tool} {indices.Length} vertices";
        Change = new(MapChangeDomain.Geometry | MapChangeDomain.Navigation, new[] { id });
    }
    public void Execute() => Apply(_after);
    public void Undo() => Apply(_before);
    private void Apply(Vector3[] positions)
    {
        var mesh = (MapMesh)(MapObjects.Find(_document.Project.Definition, _id)?.Value ?? throw new InvalidOperationException("Mesh is missing."));
        for (int i = 0; i < _indices.Length; i++) mesh.Vertices[_indices[i]] = new[] { positions[i].X, positions[i].Y, positions[i].Z };
    }
}
