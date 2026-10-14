using System;
using System.Collections.Generic;
using System.Linq;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Quadratic 3x3 Bezier cells sharing control-point edges, using the same
/// subdivision convention as Q3Import.Tessellate. The authoring master stays
/// a control grid, while visible and solid faces share exactly these triangles.
/// </summary>
public static class BezierPatchCompiler
{
    public const int MaximumDimension = 17;
    public const int MaximumTessellation = 4;
    public const int MaximumTriangles = 2048;

    public static IReadOnlyList<BuiltFace> Compile(MapBezierPatch patch,
        float texScale, int materialOffset = 0)
    {
        ArgumentNullException.ThrowIfNull(patch);
        if (patch.Columns is < 3 or > MaximumDimension
            || patch.Rows is < 3 or > MaximumDimension
            || patch.Columns % 2 == 0 || patch.Rows % 2 == 0
            || patch.Tessellation is < 1 or > MaximumTessellation
            || patch.ControlPoints == null
            || patch.ControlPoints.Count != patch.Columns * patch.Rows
            || patch.ControlPoints.Any(p => p is not { Length: 5 }
                || p.Any(v => !float.IsFinite(v))
                || p.Take(3).Any(v => Math.Abs(v) >= 524288))
            || !float.IsFinite(texScale) || texScale <= 0 || texScale > 2048)
            throw new MapAuthoringException("FP-RADIANT-PATCH",
                "Bezier patches require odd 3-17 by 3-17 control grids of finite [x,y,z,s,t], tessellation 1-4.");

        int tiles = ((patch.Columns - 1) / 2) * ((patch.Rows - 1) / 2);
        int triangles = tiles * patch.Tessellation * patch.Tessellation * 2;
        if (triangles > MaximumTriangles)
            throw new MapAuthoringException("FP-RADIANT-PATCH",
                "Bezier tessellation exceeds the bounded triangle budget.");
        // Direct .map interop stores control points in room coordinates, so
        // ignoring an edited transform here would silently lose a change.
        MapTransform t = patch.Transform;
        if (t == null || !MapValidator.Vector(t.Position)
            || !MapValidator.Vector(t.Scale)
            || t.Rotation is not { Length: 4 }
            || t.Rotation.Any(x => !float.IsFinite(x))
            || t.Position.Any(x => x != 0)
            || t.Scale.Any(x => x != 1)
            || t.Rotation[0] != 0 || t.Rotation[1] != 0
            || t.Rotation[2] != 0 || t.Rotation[3] != 1
            || patch.QuakeFaces != null || patch.FaceUv.Count != 0
            || patch.Uv.Scale.Length != 2
            || patch.Uv.Scale.Any(x => x != 1)
            || patch.Uv.Offset.Any(x => x != 0)
            || patch.Uv.Rotation != 0)
            throw new MapAuthoringException("FP-RADIANT-PATCH",
                "Patch transforms and derived Quake face projections are unsupported; edit control points instead.");

        (Vector3 point, Vector2 uv) Control(int col, int row)
        {
            float[] cp = patch.ControlPoints[col * patch.Rows + row];
            return (new Vector3(cp[0], cp[1], cp[2]),
                new Vector2(cp[3] * texScale, cp[4] * texScale));
        }
        static (float, float, float) Weights(float value)
        {
            float remaining = 1 - value;
            return (remaining * remaining, 2 * remaining * value, value * value);
        }
        var result = new List<BuiltFace>(triangles);
        for (int row = 0; row + 2 < patch.Rows; row += 2)
        for (int col = 0; col + 2 < patch.Columns; col += 2)
        {
            int subdivisions = patch.Tessellation;
            var positions = new Vector3[subdivisions + 1, subdivisions + 1];
            var coords = new Vector2[subdivisions + 1, subdivisions + 1];
            for (int v = 0; v <= subdivisions; v++)
            {
                var (w0,w1,w2) = Weights(v / (float)subdivisions);
                for (int u = 0; u <= subdivisions; u++)
                {
                    var (u0,u1,u2) = Weights(u / (float)subdivisions);
                    Vector3 position = Vector3.Zero;
                    Vector2 uv = Vector2.Zero;
                    for (int dy = 0; dy < 3; dy++)
                    for (int dx = 0; dx < 3; dx++)
                    {
                        float weight = (dy == 0 ? w0 : dy == 1 ? w1 : w2)
                            * (dx == 0 ? u0 : dx == 1 ? u1 : u2);
                        var control = Control(col + dx, row + dy);
                        position += control.point * weight;
                        uv += control.uv * weight;
                    }
                    positions[v,u] = position;
                    coords[v,u] = uv;
                }
            }
            void Triangle((int v,int u) a,(int v,int u) b,(int v,int u) c)
            {
                Vector3[] points =
                {
                    positions[a.v,a.u],positions[b.v,b.u],positions[c.v,c.u]
                };
                Vector2[] texcoords =
                {
                    coords[a.v,a.u],coords[b.v,b.u],coords[c.v,c.u]
                };
                Vector3 normal = Vector3.Cross(points[1] - points[0],
                    points[2] - points[0]);
                if (!float.IsFinite(normal.LengthSquared)
                    || normal.LengthSquared < 1e-10f
                    || points.Any(p => !float.IsFinite(p.LengthSquared))
                    || texcoords.Any(uv => !float.IsFinite(uv.X)
                        || !float.IsFinite(uv.Y)
                        || MathF.Abs(uv.X) >= 2048 || MathF.Abs(uv.Y) >= 2048))
                    throw new MapAuthoringException("FP-RADIANT-PATCH",
                        "Patch produced a degenerate triangle or out-of-range texture coordinate.");
                normal.Normalize();
                result.Add(new BuiltFace(points,texcoords,normal,
                    patch.Material + materialOffset,
                    patch.Shade * (.7f + .3f * MathF.Max(0,normal.Y)))
                {
                    Damaging = patch.Damaging,
                    Terrain = Enum.TryParse<Terrain>(patch.Terrain,true,out var terrain)
                        ? terrain : Terrain.Metal
                });
            }
            for (int v = 0; v < subdivisions; v++)
            for (int u = 0; u < subdivisions; u++)
            {
                Triangle((v,u),(v,u+1),(v+1,u+1));
                Triangle((v,u),(v+1,u+1),(v+1,u));
            }
        }
        return result;
    }
}
