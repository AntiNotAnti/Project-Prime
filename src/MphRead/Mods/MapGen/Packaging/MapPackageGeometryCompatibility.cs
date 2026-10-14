using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Convert new *authoring-only* geometry metadata into the already supported
/// native MapMesh representation at the package boundary. Existing map
/// definitions without new geometry are not modified. This keeps the .map/JSON
/// studio master editable while .ppmap remains self-contained and does not
/// require new wire protocol, package reader or geometry discriminators.
/// </summary>
public static class MapPackageGeometryCompatibility
{
    public static bool LowerInPlace(MapDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        bool changed = false;
        for (int index = 0; index < definition.Geometry.Count; index++)
        {
            MapGeometry source = definition.Geometry[index];
            if (source is not MapBezierPatch && source.QuakeFaces == null)
                continue;
            if (source.Material < 0 || source.Material >= definition.Materials.Count)
                throw new InvalidDataException("Source material is missing during package geometry conversion.");

            IReadOnlyList<BuiltFace> compiled = GeometryCompiler.Compile(
                source, definition.Materials[source.Material].TexScale);
            if (compiled.Count is < 1 or > BezierPatchCompiler.MaximumTriangles
                || compiled.Sum(f => f.Points.Length) > 65535)
                throw new InvalidDataException("Authored geometry exceeds portable mesh budgets.");

            var mesh = new MapMesh
            {
                Id = source.Id, Label = source.Label, Layer = source.Layer,
                Material = source.Material,
                Hidden = source.Hidden, Locked = source.Locked,
                Solid = source.Solid, Damaging = source.Damaging,
                Terrain = source.Terrain, Shade = source.Shade,
                // Explicit vertex coordinates include source transforms.
                Transform = new MapTransform()
            };
            foreach (BuiltFace face in compiled)
            {
                if (face.Points.Length is < 3 or > 32
                    || face.Texcoords.Length != face.Points.Length
                    || face.Material < 0 || face.Material >= definition.Materials.Count)
                    throw new InvalidDataException("Invalid source face during portable mesh conversion.");
                int start = mesh.Vertices.Count;
                mesh.Faces.Add(Enumerable.Range(start,face.Points.Length).ToArray());
                mesh.FaceMaterials.Add(face.Material);
                mesh.FaceTexcoords.Add(face.Texcoords.Select(v => new[] { v.X, v.Y }).ToArray());
                foreach (var point in face.Points)
                    mesh.Vertices.Add(new[] { point.X, point.Y, point.Z });
            }
            IReadOnlyList<BuiltFace> portable = GeometryCompiler.Compile(mesh,
                definition.Materials[mesh.Material].TexScale);
            if (portable.Count != compiled.Count)
                throw new InvalidDataException("Portable mesh face count changed.");
            for (int faceIndex = 0; faceIndex < portable.Count; faceIndex++)
            {
                BuiltFace a = compiled[faceIndex], b = portable[faceIndex];
                if (a.Material != b.Material || a.Points.Length != b.Points.Length
                    || a.Points.Zip(b.Points,(x,y)=>(x-y).LengthSquared).Any(d=>d>0.000001f)
                    || a.Texcoords.Zip(b.Texcoords,(x,y)=>(x-y).LengthSquared).Any(d=>d>0.000001f))
                    throw new InvalidDataException("Portable mesh changed geometry, materials or UVs.");
            }
            definition.Geometry[index] = mesh;
            changed = true;
        }
        return changed;
    }
}
