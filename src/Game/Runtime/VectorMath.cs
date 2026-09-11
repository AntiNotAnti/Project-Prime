using System;
using OpenTK.Mathematics;

namespace MphRead;

/// <summary>
/// Small deterministic vector guards shared by simulation and presentation.
/// A failed normalization never produces NaN or an arbitrary platform-dependent
/// direction; callers get an explicit failure or a stable fallback instead.
/// </summary>
internal static class VectorMath
{
    internal const float DefaultEpsilon = 1 / 4096f;

    internal static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static bool TryNormalize(Vector3 value, out Vector3 normalized,
        float epsilon = DefaultEpsilon)
    {
        normalized = Vector3.Zero;
        if (!IsFinite(value) || !float.IsFinite(epsilon) || epsilon <= 0)
        {
            return false;
        }
        float lengthSquared = value.LengthSquared;
        if (!float.IsFinite(lengthSquared) || lengthSquared <= epsilon * epsilon)
        {
            return false;
        }
        float inverseLength = 1 / MathF.Sqrt(lengthSquared);
        normalized = value * inverseLength;
        return IsFinite(normalized);
    }

    internal static Vector3 NormalizeOr(Vector3 value, Vector3 fallback,
        float epsilon = DefaultEpsilon)
    {
        if (TryNormalize(value, out Vector3 normalized, epsilon))
        {
            return normalized;
        }
        return TryNormalize(fallback, out normalized, epsilon) ? normalized : Vector3.UnitX;
    }

    internal static bool TryNormalizeHorizontal(Vector3 value, out Vector3 normalized,
        float epsilon = DefaultEpsilon)
    {
        normalized = Vector3.Zero;
        if (!IsFinite(value) || !float.IsFinite(epsilon) || epsilon <= 0)
        {
            return false;
        }
        float lengthSquared = value.X * value.X + value.Z * value.Z;
        if (!float.IsFinite(lengthSquared) || lengthSquared <= epsilon * epsilon)
        {
            return false;
        }
        float inverseLength = 1 / MathF.Sqrt(lengthSquared);
        normalized = new Vector3(value.X * inverseLength, 0, value.Z * inverseLength);
        return IsFinite(normalized);
    }

    internal static Vector3 NormalizeHorizontalOr(Vector3 value, Vector3 fallback,
        float epsilon = DefaultEpsilon)
    {
        if (TryNormalizeHorizontal(value, out Vector3 normalized, epsilon))
        {
            return normalized;
        }
        return TryNormalizeHorizontal(fallback, out normalized, epsilon)
            ? normalized : Vector3.UnitX;
    }

    /// <summary>
    /// Returns a stable unit vector perpendicular to <paramref name="axis"/>.
    /// The preferred basis is used when possible; near-parallel inputs choose a
    /// deterministic least-dependent world basis.
    /// </summary>
    internal static Vector3 Perpendicular(Vector3 axis, Vector3 preferred)
    {
        axis = NormalizeOr(axis, Vector3.UnitX);
        Vector3 candidate = Vector3.Cross(axis, NormalizeOr(preferred, Vector3.UnitY));
        if (TryNormalize(candidate, out Vector3 perpendicular))
        {
            return perpendicular;
        }
        Vector3 basis = MathF.Abs(axis.Y) < 0.70710677f ? Vector3.UnitY : Vector3.UnitX;
        candidate = Vector3.Cross(axis, basis);
        return NormalizeOr(candidate, Vector3.UnitZ);
    }

    /// <summary>
    /// Finds the closest point on a finite segment. Zero-length segments are
    /// treated as vertices so malformed collision data cannot divide by zero.
    /// </summary>
    internal static bool TryClosestPointOnSegment(Vector3 point, Vector3 start, Vector3 end,
        out Vector3 closest, out float distanceSquared, float epsilon = DefaultEpsilon)
    {
        closest = Vector3.Zero;
        distanceSquared = float.PositiveInfinity;
        if (!IsFinite(point) || !IsFinite(start) || !IsFinite(end))
        {
            return false;
        }
        Vector3 edge = end - start;
        float edgeLengthSquared = edge.LengthSquared;
        if (!float.IsFinite(edgeLengthSquared))
        {
            return false;
        }
        if (edgeLengthSquared <= epsilon * epsilon)
        {
            closest = start;
        }
        else
        {
            float t = Vector3.Dot(point - start, edge) / edgeLengthSquared;
            closest = start + edge * Math.Clamp(t, 0, 1);
        }
        Vector3 difference = point - closest;
        distanceSquared = difference.LengthSquared;
        return float.IsFinite(distanceSquared);
    }
}
