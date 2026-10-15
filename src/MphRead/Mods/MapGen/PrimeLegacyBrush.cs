using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>Lossless authored source adapter for the original axis-aligned brush list.
/// Unchanged source recovers the original brush; edited topology becomes an explicit mesh.</summary>
public static class PrimeLegacyBrush
{
    public static MapMesh ToMesh(MapBrush brush, List<MapMaterial> materials)
    {
        if (brush.Min?.Length != 3 || brush.Max?.Length != 3
            || brush.Min.Concat(brush.Max).Any(v => !float.IsFinite(v))
            || !float.IsFinite(brush.Shade) || brush.Material < 0 || brush.Material >= materials.Count)
            throw new InvalidDataException("Invalid original legacy brush.");
        var definition = new MapDefinition { Materials = materials, Brushes = new() { brush } };
        var built = new BuiltMap(definition);
        MapBuilder.AddAuthoredGeometry(built, definition);
        var faces = built.Faces;
        var mesh = new MapMesh
        {
            Id = brush.Id, Label = brush.Label ?? "Legacy brush", Material = brush.Material,
            Solid = brush.Solid, Damaging = brush.Damaging, Terrain = brush.Terrain ?? "Metal", Shade = brush.Shade
        };
        var vertices = new Dictionary<(float, float, float), int>();
        foreach (var face in faces)
        {
            var ring = new int[face.Points.Length];
            for (int i = 0; i < ring.Length; i++)
            {
                var p = face.Points[i];
                if (!vertices.TryGetValue((p.X, p.Y, p.Z), out int index))
                {
                    index = mesh.Vertices.Count;
                    vertices.Add((p.X, p.Y, p.Z), index);
                    mesh.Vertices.Add(new[] { p.X, p.Y, p.Z });
                }
                ring[i] = index;
            }
            mesh.Faces.Add(ring);
            mesh.FaceMaterials.Add(face.Material);
            mesh.FaceTexcoords.Add(face.Texcoords.Select(uv => new[] { uv.X, uv.Y }).ToArray());
        }
        return mesh;
    }
}
