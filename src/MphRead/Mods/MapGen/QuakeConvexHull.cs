using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Strict, bounded half-space reconstruction for classic Quake three-point
/// brushes. Every source plane must contribute to a closed convex solid.
/// Imported angled brushes are native editable MapConvexBrush geometry,
/// not a permanent BSP mesh. Source projection/flags are validated by the
/// caller and are not silently approximated here.
/// </summary>
internal static class QuakeConvexHull
{
    private const float PlaneTolerance = 0.03f;
    private const float VertexToleranceSq = PlaneTolerance * PlaneTolerance;
    private sealed record Plane(Vector3 Normal, float Distance);

    public static MapGeometry FromQuakePlanes(IReadOnlyList<Vector3[]> sourceFaces, float units,
        bool legacyPrimeWinding = false)
    {
        if (sourceFaces.Count is < 4 or > 32)
            throw new InvalidOperationException("Convex Quake brush requires 4-32 planes.");
        if (!float.IsFinite(units) || units <= 0)
            throw new ArgumentOutOfRangeException(nameof(units));
        var planes = new List<Plane>(sourceFaces.Count);
        foreach (Vector3[] source in sourceFaces)
        {
            if (source.Length != 3 || source.Any(p =>
                !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z)
                || Math.Abs(p.X) > 65536 || Math.Abs(p.Y) > 65536 || Math.Abs(p.Z) > 65536))
                throw new InvalidOperationException("Invalid or out-of-budget Quake brush plane coordinates.");
            // Quake 3 PlaneFromPoints uses CrossProduct(d2, d1). The initial
            // Prime exporter used the opposite convention; only its explicit
            // v1 source header permits reading that historical format.
            Vector3 normal = legacyPrimeWinding
                ? Vector3.Cross(source[1] - source[0], source[2] - source[0])
                : Vector3.Cross(source[2] - source[0], source[1] - source[0]);
            if (!float.IsFinite(normal.LengthSquared()) || normal.LengthSquared() < 0.000001f)
                throw new InvalidOperationException("Degenerate Quake brush plane.");
            normal = Vector3.Normalize(normal);
            planes.Add(new Plane(normal, Vector3.Dot(normal, source[0])));
        }

        // A finite convex brush is the intersection of all its outward-facing
        // half spaces. Enumerating triples is bounded (at most 4,960).
        var corners = new List<Vector3>();
        for (int i = 0; i < planes.Count; i++)
        for (int j = i + 1; j < planes.Count; j++)
        for (int k = j + 1; k < planes.Count; k++)
        {
            Plane a = planes[i], b = planes[j], c = planes[k];
            Vector3 bc = Vector3.Cross(b.Normal, c.Normal);
            float denominator = Vector3.Dot(a.Normal, bc);
            if (MathF.Abs(denominator) < 0.000001f)
                continue;
            Vector3 point = (a.Distance * bc
                + b.Distance * Vector3.Cross(c.Normal, a.Normal)
                + c.Distance * Vector3.Cross(a.Normal, b.Normal)) / denominator;
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)
                || Math.Abs(point.X) > 65536 || Math.Abs(point.Y) > 65536 || Math.Abs(point.Z) > 65536
                || planes.Any(p => Vector3.Dot(p.Normal, point) > p.Distance + PlaneTolerance))
                continue;
            if (corners.All(p => Vector3.DistanceSquared(p, point) > VertexToleranceSq))
            {
                if (corners.Count >= 256)
                    throw new InvalidOperationException("Convex brush exceeds 256 unique corners.");
                corners.Add(point);
            }
        }
        if (corners.Count < 4)
            throw new InvalidOperationException("Brush planes do not enclose a 3D solid.");

        Vector3 center = corners.Aggregate(Vector3.Zero, (sum, p) => sum + p) / corners.Count;
        if (planes.Any(p => Vector3.Dot(p.Normal, center) >= p.Distance - 0.001f))
            throw new InvalidOperationException("Convex brush has no strictly interior center.");

        var polygons = new List<int[]>(planes.Count);
        foreach (Plane plane in planes)
        {
            int[] face = Enumerable.Range(0, corners.Count)
                .Where(index => MathF.Abs(Vector3.Dot(plane.Normal, corners[index])
                    - plane.Distance) <= PlaneTolerance).ToArray();
            if (face.Length is < 3 or > 32)
                throw new InvalidOperationException("Brush plane has fewer than three or more than 32 corners.");
            Vector3 faceCenter = face.Select(i => corners[i])
                .Aggregate(Vector3.Zero, (sum, p) => sum + p) / face.Length;
            Vector3 reference = MathF.Abs(plane.Normal.X) < 0.75f
                ? Vector3.UnitX : Vector3.UnitY;
            Vector3 u = Vector3.Normalize(Vector3.Cross(reference, plane.Normal));
            Vector3 v = Vector3.Cross(plane.Normal, u);
            face = face.OrderBy(index =>
            {
                Vector3 delta = corners[index] - faceCenter;
                return MathF.Atan2(Vector3.Dot(delta, v), Vector3.Dot(delta, u));
            }).ToArray();
            Vector3 normal = Vector3.Cross(corners[face[1]] - corners[face[0]],
                corners[face[2]] - corners[face[0]]);
            if (normal.LengthSquared() < 0.000001f
                || Vector3.Dot(normal, plane.Normal) <= 0)
                throw new InvalidOperationException("Degenerate or inverted convex face.");
            polygons.Add(face);
        }

        // Require exactly two faces on every undirected edge. Duplicate,
        // missing or inward-facing planes are refused rather than corrected.
        var edgeUse = new Dictionary<(int, int), int>();
        foreach (int[] face in polygons)
        for (int index = 0; index < face.Length; index++)
        {
            int a = face[index], b = face[(index + 1) % face.Length];
            if (a == b) throw new InvalidOperationException("Repeated convex face vertex.");
            var edge = (Math.Min(a, b), Math.Max(a, b));
            edgeUse[edge] = edgeUse.GetValueOrDefault(edge) + 1;
        }
        if (edgeUse.Count == 0 || edgeUse.Values.Any(count => count != 2))
            throw new InvalidOperationException("Brush planes are not a closed, nonduplicated convex hull.");

        // The Q3-to-Prime transform is a proper rotation (x,z,-y);
        // face winding is therefore preserved.
        var converted = corners.Select(p => new Vector3(
            p.X / units, p.Z / units, -p.Y / units)).ToArray();
        Vector3 lo = converted.Aggregate((a, b) => Vector3.Min(a, b));
        Vector3 hi = converted.Aggregate((a, b) => Vector3.Max(a, b));
        if (lo.X >= hi.X - 0.0001f || lo.Y >= hi.Y - 0.0001f
            || lo.Z >= hi.Z - 0.0001f)
            throw new InvalidOperationException("Brush has zero or negligible volume.");

        // Preserve the existing MapBox representation when all six planes
        // form a true axis-aligned box. Do not infer a box from arbitrary
        // points with coincident min/max bounds.
        bool axisBox = planes.Count == 6 && converted.Length == 8
            && polygons.All(f => f.Length == 4)
            && sourceFaces.All(f =>
                (f[0].X == f[1].X && f[1].X == f[2].X)
                || (f[0].Y == f[1].Y && f[1].Y == f[2].Y)
                || (f[0].Z == f[1].Z && f[1].Z == f[2].Z))
            && converted.All(p =>
                (Math.Abs(p.X - lo.X) < PlaneTolerance / units
                    || Math.Abs(p.X - hi.X) < PlaneTolerance / units)
                && (Math.Abs(p.Y - lo.Y) < PlaneTolerance / units
                    || Math.Abs(p.Y - hi.Y) < PlaneTolerance / units)
                && (Math.Abs(p.Z - lo.Z) < PlaneTolerance / units
                    || Math.Abs(p.Z - hi.Z) < PlaneTolerance / units));
        if (axisBox)
            return new MapBox
            {
                Transform = new MapTransform
                {
                    Position = new[] { (lo.X + hi.X) / 2,
                        (lo.Y + hi.Y) / 2, (lo.Z + hi.Z) / 2 },
                    Scale = new[] { hi.X - lo.X, hi.Y - lo.Y, hi.Z - lo.Z }
                }
            };

        return new MapConvexBrush
        {
            Vertices = converted.Select(p => new[] { p.X, p.Y, p.Z }).ToList(),
            Faces = polygons
        };
    }
}
