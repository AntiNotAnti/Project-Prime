using System;
using System.Collections.Generic;
using System.Numerics;

namespace MphRead.Mods.MapEditor;

public enum MapViewportDiagnosticMode { None, Terrain, Overdraw, TexelDensity, MaterialId }

/// <summary>A stable CPU mesh. Replaced only when this object's geometry changes.</summary>
public sealed record MapViewportMesh(Guid ObjectId, IReadOnlyList<MapViewportFace> Faces,
    IReadOnlyList<MapViewportFace>? CollisionFaces = null);

/// <summary>Editor presentation data, independent of the UI toolkit and graphics API.</summary>
public sealed record MapRenderFrame(MapViewportLayout Layout, MapViewportCamera Camera,
    IReadOnlyList<MapViewportMesh> Meshes, IReadOnlySet<Guid> Selection,
    IReadOnlyDictionary<Guid, Matrix4x4> PreviewTransforms, bool Wireframe, bool Collision)
{
    /// <summary>All current document meshes, including chunks culled from this picture.
    /// Visibility controls drawing; document membership controls resource lifetime.</summary>
    private IReadOnlyList<MapViewportMesh>? _residentMeshes;
    public IReadOnlyList<MapViewportMesh> ResidentMeshes
    {
        get => _residentMeshes ?? Meshes;
        init => _residentMeshes = value;
    }
    public IReadOnlyDictionary<(bool Imported, int Index), MapViewportMaterial> Materials { get; init; } = new Dictionary<(bool, int), MapViewportMaterial>();
    public bool UvChecker { get; init; }
    public string GridView { get; init; } = "Perspective";
    public float GridStep { get; init; } = 4;
    public bool LightingPreview { get; init; }
    public bool ShadowPreview { get; init; }
    public bool FogPreview { get; init; }
    public MapViewportDiagnosticMode DiagnosticMode { get; init; }
    public Vector3 Light1Vector { get; init; } = new(.3f,-1f,.2f);
    public Vector3 Light2Vector { get; init; } = new(-.3f,1f,-.2f);
    public Vector3 Light1Color { get; init; } = new(1f,28f/31,24f/31);
    public Vector3 Light2Color { get; init; } = new(10f/31,11f/31,16f/31);
    public Vector3 FogColor { get; init; } = new(8f/31,10f/31,16f/31);
    public bool FogEnabled { get; init; } = true;
    public int FogOffset { get; init; } = 65180;
    public int FogSlope { get; init; } = 5;
}

/// <summary>Retain uploaded editor meshes across visibility changes. Upload lazily,
/// and release resources only when their document mesh is removed or replaced.</summary>
public sealed class MapViewportMeshResources<TResource>
{
    private sealed record Entry(MapViewportMesh Source, TResource Resource);
    private readonly Dictionary<Guid, Entry> _entries = new();
    private readonly Dictionary<Guid, MapViewportMesh> _resident = new();
    private readonly List<Guid> _stale = new();
    public int Count => _entries.Count;
    public TResource this[Guid id] => _entries[id].Resource;

    public void Synchronize(MapRenderFrame frame, Func<MapViewportMesh, TResource> upload,
        Action<TResource> release)
    {
        try
        {
            foreach (var mesh in frame.ResidentMeshes) _resident.Add(mesh.ObjectId, mesh);
            foreach (var (id, entry) in _entries)
                if (!_resident.TryGetValue(id, out var source) || !ReferenceEquals(entry.Source, source))
                    _stale.Add(id);
            foreach (var id in _stale)
            {
                release(_entries[id].Resource);
                _entries.Remove(id);
            }
            foreach (var mesh in frame.Meshes)
            {
                if (!_resident.TryGetValue(mesh.ObjectId, out var source) || !ReferenceEquals(source, mesh))
                    throw new ArgumentException("Visible editor mesh is absent from the resident document meshes.", nameof(frame));
                if (!_entries.ContainsKey(mesh.ObjectId))
                    _entries.Add(mesh.ObjectId, new(mesh, upload(mesh)));
            }
        }
        finally
        {
            // Scratch collections must not keep replaced document geometry alive.
            _resident.Clear();
            _stale.Clear();
        }
    }

    public void Clear(Action<TResource> release)
    {
        foreach (var entry in _entries.Values) release(entry.Resource);
        _entries.Clear();
        _resident.Clear();
        _stale.Clear();
    }
}

public static class MapViewportGrid
{
    public static IEnumerable<(Vector3 A, Vector3 B)> Lines(string view, float step)
    {
        step = float.IsFinite(step) ? Math.Clamp(step, .125f, 1024) : 4;
        Vector3 Point(float a, float b) => view switch
        {
            "Front" => new(a,b,0), "Side" => new(0,b,a), _ => new(a,0,b)
        };
        float extent=64*step;
        for(int i=-64;i<=64;i++)
        {
            yield return (Point(i*step,-extent),Point(i*step,extent));
            yield return (Point(-extent,i*step),Point(extent,i*step));
        }
    }
}

public readonly record struct MapViewportCamera(Vector3 Position, Vector3 Target, bool Perspective)
{
    public (Vector3 Right, Vector3 Up, Vector3 Forward) Basis()
    {
        var direction = Target - Position;
        var forward = float.IsFinite(direction.LengthSquared()) && direction.LengthSquared() > 1e-10f
            ? Vector3.Normalize(direction) : -Vector3.UnitZ;
        var up = Math.Abs(forward.Y) > .99f ? Vector3.UnitZ : Vector3.UnitY;
        var right = Vector3.Normalize(Vector3.Cross(forward, up));
        return (right, Vector3.Cross(right, forward), forward);
    }
    public double FocalScale(MapViewportLayout layout) => Perspective
        ? Math.Min(layout.Width, layout.Height) * .9
        : Math.Min(layout.Width, layout.Height) / Math.Max(2, Vector3.Distance(Position, Target)) * 1.5;
    public (double X, double Y, double Depth)? Project(MapViewportLayout layout, Vector3 point)
    {
        if (!layout.IsValid) return null;
        var (right, up, forward) = Basis();
        var offset = point - Position;
        float depth = Vector3.Dot(offset, forward);
        if (depth < .05f) return null;
        double scale = FocalScale(layout) / (Perspective ? depth : 1);
        return (layout.Width / 2 + Vector3.Dot(offset, right) * scale,
            layout.Height / 2 - Vector3.Dot(offset, up) * scale, depth);
    }
    public (Vector3 Origin, Vector3 Direction) Ray(MapViewportLayout layout, double x, double y)
    {
        var (right, up, forward) = Basis();
        var offset = right * (float)((x - layout.Width / 2) / FocalScale(layout))
            - up * (float)((y - layout.Height / 2) / FocalScale(layout));
        return Perspective ? (Position, Vector3.Normalize(forward + offset)) : (Position + offset, forward);
    }
    public Matrix4x4 Projection(MapViewportLayout layout)
    {
        float vertical = (float)(layout.Height / FocalScale(layout));
        var projection = Perspective
            ? Matrix4x4.CreatePerspectiveFieldOfView(2 * MathF.Atan(vertical / 2), (float)layout.AspectRatio, .05f, 10000)
            : Matrix4x4.CreateOrthographic(vertical * (float)layout.AspectRatio, vertical, .05f, 10000);
        // The game's GL renderer uses clip depth -1..1; Numerics uses 0..1.
        projection.M33 = Perspective ? -(10000 + .05f) / (10000 - .05f) : -2 / (10000 - .05f);
        projection.M43 = Perspective ? -2 * 10000 * .05f / (10000 - .05f) : -(10000 + .05f) / (10000 - .05f);
        return projection;
    }
}

public readonly record struct MapPickHit(Guid ObjectId,int FaceIndex,int Material,int SourceMaterial,
    Vector3 Point,Vector3 Normal,float Distance);

public static class MapViewportPicking
{
    /// <summary>Pick actual triangles in world distance, including near-plane intersections.</summary>
    public static Guid Pick(MapRenderFrame frame, double x, double y)
        => PickHit(frame,x,y)?.ObjectId ?? Guid.Empty;

    public static MapPickHit? PickHit(MapRenderFrame frame,double x,double y,bool includeImported=true)
    {
        if (!frame.Layout.IsValid) return null;
        var (origin, direction) = frame.Camera.Ray(frame.Layout, x, y);
        float nearest = float.PositiveInfinity;
        MapPickHit? picked = null;
        foreach (var mesh in frame.Meshes)
        {
            if (!includeImported && mesh.ObjectId == Guid.Empty) continue;
            var transform = frame.PreviewTransforms.GetValueOrDefault(mesh.ObjectId, Matrix4x4.Identity);
            for(int faceIndex=0;faceIndex<mesh.Faces.Count;faceIndex++)
            {
                var face=mesh.Faces[faceIndex];
                if (frame.Collision && !face.Solid || face.Points.Length < 3) continue;
                var hit = PickFace(mesh.ObjectId, faceIndex, face, transform, origin, direction, out _);
                if (hit is not { } candidate || candidate.Distance >= nearest) continue;
                nearest = candidate.Distance;
                picked = candidate;
            }
        }
        return picked;
    }

    /// <summary>Canonical ray intersection for one cached face. Both full-scene
    /// fallback and the GPU ID path use this exact triangle/near-plane kernel.</summary>
    internal static MapPickHit? PickFace(Guid objectId, int faceIndex, MapViewportFace face,
        Matrix4x4 transform, Vector3 origin, Vector3 direction, out int trianglesTested)
    {
        trianglesTested = 0;
        if (face.Points.Length < 3) return null;
        float nearest = float.PositiveInfinity;
        MapPickHit? picked = null;
        var a = Vector3.Transform(face.Points[0], transform);
        for (int i = 1; i < face.Points.Length - 1; i++)
        {
            trianglesTested++;
            var b = Vector3.Transform(face.Points[i], transform);
            var c = Vector3.Transform(face.Points[i + 1], transform);
            var edge = b - a; var other = c - a; var cross = Vector3.Cross(direction, other);
            float determinant = Vector3.Dot(edge, cross);
            if (MathF.Abs(determinant) < 1e-7f) continue;
            var offset = origin - a;
            float u = Vector3.Dot(offset, cross) / determinant;
            if (u < 0 || u > 1) continue;
            var q = Vector3.Cross(offset, edge);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0 || u + v > 1) continue;
            float distance = Vector3.Dot(other, q) / determinant;
            if (distance < .05f || distance >= nearest) continue;
            Vector3 normal = Vector3.Cross(edge, other);
            normal = normal.LengthSquared() > 1e-10f ? Vector3.Normalize(normal) : Vector3.UnitY;
            nearest = distance;
            picked = new(objectId, faceIndex, face.Material, face.SourceMaterial,
                origin + direction * distance, normal, distance);
        }
        return picked;
    }

}
