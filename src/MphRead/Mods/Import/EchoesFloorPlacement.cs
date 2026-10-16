using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

/// <summary>
/// Source-collision-only floor query for local Echoes playtest spawn/pickup
/// placement. Mirrors Second Hunt's 0.3-unit elevated ray and 3-unit drop
/// window, but never consults imported visual meshes as collision.
/// Does not replace native scene collision or collision hitbox simulation.
/// </summary>
public static class EchoesFloorPlacement
{
    public static int SnapSpawnAndPickupFeet(MapDefinition definition, float maxDrop)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.LocalSourceOnly || definition.Geometry == null ||
            !float.IsFinite(maxDrop) || maxDrop <= 0 || maxDrop > 10)
            throw new InvalidDataException("Floor alignment requires a private Echoes map and bounded drop.");
        MapMesh[] floors = definition.Geometry.OfType<MapMesh>()
            .Where(mesh => mesh.CollisionOnly && mesh.Solid).ToArray();
        if (floors.Length == 0)
            throw new InvalidDataException("Echoes floor placement has no source collision geometry.");

        int moved = 0;
        foreach (MapSpawn spawn in definition.Spawns)
            if (Snap(floors, spawn.Position, maxDrop, out float[] aligned))
            { spawn.Position = aligned; moved++; }
        foreach (MapItem item in definition.Items)
            if (Snap(floors, item.Position, maxDrop, out float[] aligned))
            { item.Position = aligned; moved++; }
        return moved;
    }

    /// <summary>
    /// Choose the highest walkable collision triangle directly below the
    /// supplied feet within a short downward sweep. Never put a player on a
    /// vertical wall, ceiling, visual-only mesh or distant lower floor.
    /// </summary>
    public static bool Snap(IEnumerable<MapMesh> collisionMeshes, float[] feet,
        float maxDrop, out float[] snapped)
    {
        snapped = feet == null ? Array.Empty<float>() : (float[])feet.Clone();
        if (feet is not { Length: 3 } || !feet.All(float.IsFinite) ||
            !float.IsFinite(maxDrop) || maxDrop <= 0 || maxDrop > 10)
            throw new ArgumentException("Floor query needs finite feet and a bounded drop.");
        if (collisionMeshes == null) throw new ArgumentNullException(nameof(collisionMeshes));

        float x = feet[0], z = feet[2];
        float top = feet[1] + 0.3f, bottom = feet[1] - maxDrop;
        float best = float.NegativeInfinity;
        foreach (MapMesh mesh in collisionMeshes)
        {
            if (mesh == null || !mesh.CollisionOnly || !mesh.Solid) continue;
            foreach (int[] face in mesh.Faces)
            {
                if (face is not { Length: 3 }) continue;
                if (face.Any(index => (uint)index >= mesh.Vertices.Count)) continue;
                float[] a = mesh.Vertices[face[0]];
                float[] b = mesh.Vertices[face[1]];
                float[] c = mesh.Vertices[face[2]];
                if (a is not { Length: 3 } || b is not { Length: 3 } ||
                    c is not { Length: 3 } || !a.All(float.IsFinite) ||
                    !b.All(float.IsFinite) || !c.All(float.IsFinite)) continue;

                // Skip most surfaces by cheap projected bounding-box tests.
                if (x < MathF.Min(a[0], MathF.Min(b[0], c[0])) - 1e-4f ||
                    x > MathF.Max(a[0], MathF.Max(b[0], c[0])) + 1e-4f ||
                    z < MathF.Min(a[2], MathF.Min(b[2], c[2])) - 1e-4f ||
                    z > MathF.Max(a[2], MathF.Max(b[2], c[2])) + 1e-4f)
                    continue;

                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                float nx = uy * vz - uz * vy;
                float ny = uz * vx - ux * vz;
                float nz = ux * vy - uy * vx;
                float lengthSquared = nx * nx + ny * ny + nz * nz;
                if (!float.IsFinite(lengthSquared) || lengthSquared < 1e-10f ||
                    ny <= 0 || ny * ny < 0.49f * lengthSquared)
                    continue; // no up-facing walkable floor

                // XZ barycentrics are stable for walkable, not vertical, faces.
                float det = (b[2] - c[2]) * (a[0] - c[0]) +
                    (c[0] - b[0]) * (a[2] - c[2]);
                if (MathF.Abs(det) < 1e-9f) continue;
                float u = ((b[2] - c[2]) * (x - c[0]) +
                    (c[0] - b[0]) * (z - c[2])) / det;
                float v = ((c[2] - a[2]) * (x - c[0]) +
                    (a[0] - c[0]) * (z - c[2])) / det;
                float w = 1 - u - v;
                if (u < -1e-4f || v < -1e-4f || w < -1e-4f) continue;
                float y = u * a[1] + v * b[1] + w * c[1];
                if (float.IsFinite(y) && y <= top && y >= bottom && y > best) best = y;
            }
        }
        if (!float.IsFinite(best)) return false;
        snapped = new[] { x, best, z };
        return true;
    }
}
