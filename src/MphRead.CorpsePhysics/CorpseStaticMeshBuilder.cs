using System.Numerics;

namespace MphRead.CorpsePhysics;

public enum CorpseSurfaceResult { Skipped, Added, Rejected }

// Single-use copied-static-geometry builder. The owner must separately exclude
// dynamic/manual barriers and fence scene/connector changes. No engine query or
// source reference enters this class. Never silently truncate a nearby surface.
public sealed class CorpseStaticMeshBuilder
{
    public const int MaximumFaces = 8192, MaximumFaceVertices = 32;
    public const float MaximumPlaneError = .002f;
    private readonly StaticTriangle[] _triangles = new StaticTriangle[CorpseStaticWorld.MaximumTriangles];
    private readonly Vector3 _minimum, _maximum;
    private readonly int _owner = Environment.CurrentManagedThreadId;
    private int _count, _faces;
    private bool _failed, _sealed;
    private void Owner()
    { if(Environment.CurrentManagedThreadId!=_owner)throw new InvalidOperationException("Static capture owner thread changed."); }
    public bool Failed { get { Owner(); return _failed; } }
    public int VisitedFaces { get { Owner(); return _faces; } }
    public int StagedTriangles { get { Owner(); return _count; } }
    public CorpseStaticMeshBuilder(Vector3 center, float halfExtent)
    {
        if (!Limits.Position(center) || !float.IsFinite(halfExtent) || halfExtent is < 1 or > 16)
            throw new ArgumentException("A finite bounded static capture region is required.");
        _minimum = center - new Vector3(halfExtent); _maximum = center + new Vector3(halfExtent);
    }
    private CorpseSurfaceResult Reject()
    { _failed = true; _count = 0; Array.Clear(_triangles); return CorpseSurfaceResult.Rejected; }
    public CorpseSurfaceResult TryAddFace(ReadOnlySpan<Vector3> worldPoints, Vector4 worldPlane, bool ignorePlayers = false)
    {
        Owner();
        if (_failed || _sealed) return CorpseSurfaceResult.Rejected;
        if (++_faces > MaximumFaces) return Reject();
        // This flag comes from the trusted static source adapter, never a client
        // request. IgnoreBeams alone must not be translated into IgnorePlayers.
        if (ignorePlayers) return CorpseSurfaceResult.Skipped;
        if (worldPoints.Length is < 3 or > MaximumFaceVertices) return Reject();
        Vector3 min = worldPoints[0], max = min;
        foreach (var p in worldPoints)
        {
            if (!Limits.Position(p)) return Reject();
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
        }
        if (max.X < _minimum.X || min.X > _maximum.X || max.Y < _minimum.Y || min.Y > _maximum.Y
            || max.Z < _minimum.Z || min.Z > _maximum.Z) return CorpseSurfaceResult.Skipped;
        var normal = new Vector3(worldPlane.X, worldPlane.Y, worldPlane.Z);
        float lengthSquared = normal.LengthSquared();
        if (!Limits.Finite(normal) || !float.IsFinite(worldPlane.W) || lengthSquared is < .99f or > 1.01f) return Reject();
        float length = MathF.Sqrt(lengthSquared); normal /= length; float distance = worldPlane.W / length;
        foreach (var p in worldPoints)
            if (MathF.Abs(Vector3.Dot(normal, p) - distance) > MaximumPlaneError) return Reject();
        // Require a convex, consistently ordered polygon in its stored plane.
        // Testing every edge halfspace also rejects self-intersecting stars.
        float winding = 0;
        for (int i = 0; i < worldPoints.Length; i++)
        {
            var a = worldPoints[i]; var b = worldPoints[(i + 1) % worldPoints.Length]; var edge = b - a;
            if (edge.LengthSquared() is < 1e-10f or > 4096) return Reject();
            var next = worldPoints[(i + 2) % worldPoints.Length] - b;
            float turn = Vector3.Dot(Vector3.Cross(edge, next), normal);
            if (MathF.Abs(turn) > 1e-6f)
            {
                float sign = MathF.Sign(turn);
                if (winding != 0 && winding != sign) return Reject();
                winding = sign;
            }
        }
        if (winding == 0) return Reject();
        for (int i = 0; i < worldPoints.Length; i++)
        {
            var a = worldPoints[i]; var edge = worldPoints[(i + 1) % worldPoints.Length] - a;
            for (int j = 0; j < worldPoints.Length; j++)
                if (Vector3.Dot(Vector3.Cross(edge, worldPoints[j] - a), normal) * winding < -1e-5f) return Reject();
        }
        int needed = worldPoints.Length - 2;
        if (_count + needed > _triangles.Length) return Reject();
        Span<StaticTriangle> pending = stackalloc StaticTriangle[needed];
        for (int i = 0; i < needed; i++)
        {
            var a = worldPoints[0]; var b = worldPoints[i + 1]; var c = worldPoints[i + 2];
            var area = Vector3.Cross(b - a, c - a);
            if (Vector3.DistanceSquared(a, b) > 4096 || Vector3.DistanceSquared(b, c) > 4096 || Vector3.DistanceSquared(c, a) > 4096
                || area.LengthSquared() < .000001f
                // Plane tolerance alone permits a thin vertical/reversed fan
                // at an almost-collinear projected vertex. Require each fan's
                // signed projected area, not merely its three-dimensional size.
                || Vector3.Dot(area, normal) * winding < .001f) return Reject();
            // Output is always right-handed CCW toward the stored solid normal.
            // The BEPU boundary performs its separate clockwise conversion.
            pending[i] = winding > 0 ? new(a, b, c) : new(a, c, b);
        }
        pending.CopyTo(_triangles.AsSpan(_count)); _count += needed; return CorpseSurfaceResult.Added;
    }
    public bool TryBuild(out CorpseStaticWorld? world)
    {
        Owner();
        world = null;
        if (_failed || _sealed) return false;
        _sealed = true;
        if (_count == 0) return false;
        try { world = new(_triangles.AsSpan(0, _count)); return true; }
        catch (ArgumentException) { Reject(); return false; }
    }
}
