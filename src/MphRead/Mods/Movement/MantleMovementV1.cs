using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Independent conservative geometry helpers. The route prism encloses the
// complete native flat-ended standing cylinder, including between sweep spheres.
internal static class MantleMovementV1
{
    internal const float MaxRise = 1.25f, MaxTravel = 2.5f;
    internal const int Ticks = 24;
    internal const float LiftStep = .12f, AcrossStep = .14f, SettleStep = .08f;
    internal const float Clearance = .06f, Tolerance = 1f / 4096;

    internal static bool TriangleOverlapsBox(Vector3 a, Vector3 b, Vector3 c, Vector3 minimum, Vector3 maximum)
    {
        if (!Finite(a) || !Finite(b) || !Finite(c) || !Finite(minimum) || !Finite(maximum)
            || minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z) return true;
        Vector3 center = (minimum + maximum) * .5f, half = (maximum - minimum) * .5f;
        a -= center; b -= center; c -= center;
        Span<Vector3> edges = stackalloc Vector3[3] { b - a, c - b, a - c };
        if (Separates(Vector3.UnitX, a, b, c, half) || Separates(Vector3.UnitY, a, b, c, half)
            || Separates(Vector3.UnitZ, a, b, c, half)
            || Separates(Vector3.Cross(edges[0], edges[1]), a, b, c, half)) return false;
        foreach (Vector3 edge in edges)
            if (Separates(Vector3.Cross(edge, Vector3.UnitX), a, b, c, half)
                || Separates(Vector3.Cross(edge, Vector3.UnitY), a, b, c, half)
                || Separates(Vector3.Cross(edge, Vector3.UnitZ), a, b, c, half)) return false;
        return true;
    }

    private static bool Separates(Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 half)
    {
        float length = axis.Length;
        if (length < .0000001f) return false;
        float x = Vector3.Dot(a, axis), y = Vector3.Dot(b, axis), z = Vector3.Dot(c, axis);
        float radius = MathF.Abs(axis.X) * half.X + MathF.Abs(axis.Y) * half.Y + MathF.Abs(axis.Z) * half.Z;
        // Native quantization tolerance is only a boundary tolerance, never a
        // sampling interval or a radius reduction along an untested route.
        float tolerance = Tolerance * length;
        return MathF.Min(x, MathF.Min(y, z)) >= radius - tolerance
            || MathF.Max(x, MathF.Max(y, z)) <= -radius + tolerance;
    }

    internal static bool SupportsDisk(ReadOnlySpan<Vector3> vertices, Vector3 foot, float radius)
    {
        if (vertices.Length < 3 || !Finite(foot) || !float.IsFinite(radius) || radius <= 0) return false;
        float orientation = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 a = vertices[i], b = vertices[(i + 1) % vertices.Length];
            if (!Finite(a) || MathF.Abs(a.Y - foot.Y) > Tolerance) return false;
            Vector3 edge = b - a;
            float length = MathF.Sqrt(edge.X * edge.X + edge.Z * edge.Z);
            if (!float.IsFinite(length) || length <= Tolerance) return false;
            float cross = edge.X * (foot.Z - a.Z) - edge.Z * (foot.X - a.X);
            if (!float.IsFinite(cross) || !float.IsFinite(radius * length) || MathF.Abs(cross) < radius * length) return false;
            float sign = MathF.Sign(cross);
            if (orientation != 0 && sign != orientation) return false;
            orientation = sign;
            // Validate convexity instead of assuming an arbitrary face is a
            // convex landing. One face must contain the entire footprint.
            for (int j = 0; j < vertices.Length; j++)
            {
                Vector3 v = vertices[j];
                float side = sign * (edge.X * (v.Z - a.Z) - edge.Z * (v.X - a.X));
                if (!Finite(v) || !float.IsFinite(side) || side < -Tolerance * length)
                    return false;
            }
        }
        return true;
    }

    internal static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
