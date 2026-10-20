using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

// Explicitly render-only source transport. No authored/collision tolerance is
// changed. Double intermediates avoid cancellation in tiny, translated source
// triangles; positions and corner order remain untouched. This computed normal
// is a native fallback normal, not a claim of original source lighting.
internal static class MapSourceVisualTriangle
{
    internal static bool Applies(MapMesh mesh, int corners) => mesh.SourceUvFaces != null
        && !mesh.Solid && !mesh.CollisionOnly && corners == 3;

    internal static bool TryNormal(Vector3 a, Vector3 b, Vector3 c, out Vector3 normal)
    {
        double ux = (double)b.X - a.X, uy = (double)b.Y - a.Y, uz = (double)b.Z - a.Z;
        double vx = (double)c.X - a.X, vy = (double)c.Y - a.Y, vz = (double)c.Z - a.Z;
        double x = uy * vz - uz * vy, y = uz * vx - ux * vz, z = ux * vy - uy * vx;
        double squared = x*x + y*y + z*z;
        normal = default;
        if (!double.IsFinite(squared) || squared <= 0) return false;
        double length = Math.Sqrt(squared);
        normal = new((float)(x / length), (float)(y / length), (float)(z / length));
        return float.IsFinite(normal.LengthSquared) && normal.LengthSquared > .99f;
    }
}
