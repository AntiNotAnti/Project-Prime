using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MphRead.Mods.MapGen;

namespace MphRead.Mods.Import;

/// <summary>
/// Lowers user-source Echoes world geometry into the existing Project Prime
/// visual mesh format while retaining original collision in its independent
/// collision-only MapMesh channel. No source texture/package is distributed.
/// </summary>
public static class EchoesVisualLayer
{
    public static MapDefinition CreatePreview(MapDefinition collisionPreview,
        EchoesWorldGeometry world)
    {
        ArgumentNullException.ThrowIfNull(collisionPreview);
        ArgumentNullException.ThrowIfNull(world);
        if (!collisionPreview.LocalSourceOnly ||
            !collisionPreview.Name.StartsWith("LOCAL ECHOES ", StringComparison.Ordinal) ||
            collisionPreview.Geometry == null ||
            !collisionPreview.Geometry.OfType<MapMesh>().Any() ||
            collisionPreview.MapId == Guid.Empty)
            throw new InvalidDataException("Only a locally converted Echoes map can receive source visuals.");
        if (world.Positions.Length % 3 != 0 || world.TriangleVertices.Length % 3 != 0 ||
            world.TriangleCount is < 1 or > EchoesWorldGeometrySource.MaxTriangles ||
            world.PositionCount is < 3 or > EchoesWorldGeometrySource.MaxPositions)
            throw new InvalidDataException("Echoes render geometry shape exceeds limits.");

        MapDefinition result = MapProjectSerializer.Clone(collisionPreview);
        result.MapId = StableId("echoes/source-render/v1/" + collisionPreview.MapId.ToString("N"));
        result.Name = "VISUAL ECHOES " + collisionPreview.Name["LOCAL ECHOES ".Length..];
        MapValidator.RequireRuntimeName(result.Name);
        result.InGameName = (collisionPreview.InGameName ?? collisionPreview.Name)
            .Replace("(local blockout)", "(visual preview)", StringComparison.Ordinal);
        result.Description = "Locally imported Echoes world-render geometry and independent original collision. " +
            "Textures, interactable props, water and online play are not enabled.";
        foreach (MapMesh physics in result.Geometry.OfType<MapMesh>())
            physics.CollisionOnly = true;

        float[][] vertices = new float[world.PositionCount][];
        for (int i = 0; i < vertices.Length; i++)
        {
            int at = 3 * i;
            float[] v =
            {
                world.Positions[at] * EchoesLocalMapDraft.UnitsPerEchoesMetre,
                world.Positions[at + 2] * EchoesLocalMapDraft.UnitsPerEchoesMetre,
                -world.Positions[at + 1] * EchoesLocalMapDraft.UnitsPerEchoesMetre
            };
            if (v.Any(x => !float.IsFinite(x) || Math.Abs(x) >= 524288))
                throw new InvalidDataException("Echoes world visual positions exceed native map bounds.");
            vertices[i] = v;
        }

        var visualMeshes = new List<MapMesh>();
        int rendered = 0;
        float extent = 0;
        float[] min = { float.MaxValue, float.MaxValue, float.MaxValue };
        float[] max = { float.MinValue, float.MinValue, float.MinValue };
        for (int start = 0; start < world.TriangleCount; start += EchoesLocalMapDraft.MaxFacesPerMesh)
        {
            var mesh = new MapMesh
            {
                Id = StableId(result.MapId.ToString("N") + "/visible/" + start),
                Label = "Echoes source render surface " + start,
                Layer = "Echoes source visuals", Material = 0,
                Solid = false, CollisionOnly = false, Terrain = "Metal",
                Shade = 1
            };
            var remap = new Dictionary<int, int>();
            int end = Math.Min(start + EchoesLocalMapDraft.MaxFacesPerMesh,
                world.TriangleCount);
            for (int tri = start; tri < end; tri++)
            {
                int[] original =
                {
                    world.TriangleVertices[3 * tri],
                    world.TriangleVertices[3 * tri + 1],
                    world.TriangleVertices[3 * tri + 2]
                };
                if (original.Any(index => (uint)index >= vertices.Length))
                    throw new InvalidDataException("Echoes world visual surface references a missing vertex.");
                float[] a = vertices[original[0]], b = vertices[original[1]], c = vertices[original[2]];
                float ux = b[0] - a[0], uy = b[1] - a[1], uz = b[2] - a[2];
                float vx = c[0] - a[0], vy = c[1] - a[1], vz = c[2] - a[2];
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
                if (!float.IsFinite(nx * nx + ny * ny + nz * nz) ||
                    nx * nx + ny * ny + nz * nz < 1e-10f)
                    continue;
                var ring = new int[3];
                for (int j = 0; j < 3; j++)
                {
                    int source = original[j];
                    if (!remap.TryGetValue(source, out int local))
                    {
                        local = mesh.Vertices.Count;
                        mesh.Vertices.Add(vertices[source]);
                        remap.Add(source, local);
                        float[] v = vertices[source];
                        for (int k = 0; k < 3; k++)
                        {
                            min[k] = Math.Min(min[k], v[k]);
                            max[k] = Math.Max(max[k], v[k]);
                            extent = Math.Max(extent, Math.Abs(v[k]));
                        }
                    }
                    ring[j] = local;
                }
                mesh.Faces.Add(ring);
                rendered++;
            }
            if (mesh.Faces.Count > 0) visualMeshes.Add(mesh);
        }
        if (rendered == 0)
            throw new InvalidDataException("Echoes visual geometry contains no non-degenerate render faces.");

        result.Geometry.AddRange(visualMeshes);
        while (extent >= 8f * MathF.Pow(2, result.ScaleFactor))
        {
            if (++result.ScaleFactor > 16)
                throw new InvalidDataException("Echoes visual geometry exceeds native fixed-point scale.");
        }
        float dx = max[0] - min[0], dy = max[1] - min[1], dz = max[2] - min[2];
        result.FarClip = Math.Max(result.FarClip, Math.Clamp(
            MathF.Sqrt(dx * dx + dy * dy + dz * dz) * 2, 350f, 50000f));
        return result;
    }

    private static Guid StableId(string message)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(message));
        return new Guid(digest.AsSpan(0, 16));
    }
}
