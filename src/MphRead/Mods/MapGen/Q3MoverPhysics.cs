using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.MapGen;

/// <summary>
/// Fixed-60 Hz movement and collision math for one Quake brush model.
/// These structs are immutable; integration must apply the same frame and
/// activation stamp on authority, owner and replay before moving any body.
/// </summary>
internal readonly record struct Q3MoverPose(Vector3 Offset, float Degrees);

internal static class Q3MoverPhysics
{
    internal static Q3MoverPose At(in Q3MoverDescriptor mover, uint frame, uint activatedAt = 0)
    {
        if (frame < activatedAt) return default;
        uint relative = frame - activatedAt;
        return new(mover.ReferenceOffset(relative), mover.ReferenceAngleDegrees(relative));
    }

    private static bool Finite(Vector3 v) =>
        float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static Vector3 RotationAxis(in Q3MoverDescriptor mover)
    {
        if (mover.Kind != Q3MoverKind.Rotating && mover.Kind != Q3MoverKind.Pendulum)
            return Vector3.UnitY;
        return mover.Direction.LengthSquared > 0.00001f
            ? mover.Direction.Normalized() : Vector3.UnitY;
    }

    // Rodrigues' rotation, avoiding mutable model matrices and new allocations.
    private static Vector3 Rotate(Vector3 v, Vector3 axis, float degrees)
    {
        if (degrees == 0) return v;
        float radians = degrees * MathF.PI / 180f;
        (float sine, float cosine) = MathF.SinCos(radians);
        return v * cosine + Vector3.Cross(axis, v) * sine
            + axis * Vector3.Dot(axis, v) * (1f - cosine);
    }

    private static Vector3 Center(in Q3MoverDescriptor mover)
        => (mover.Minimum + mover.Maximum) * 0.5f;

    /// <summary>
    /// Apply the exact previous-to-next rigid transform to supported player
    /// feet. Radius is used for broad contact, not as a replacement for the
    /// native swept collision solver. Both biped and alt forms supply feet.
    /// </summary>
    internal static bool TryCarry(in Q3MoverDescriptor mover, in Q3MoverPose previous,
        in Q3MoverPose next, Vector3 feet, float radius, float footSnap, out Vector3 carried)
    {
        carried = feet;
        if (!Finite(feet) || !Finite(previous.Offset) || !Finite(next.Offset)
            || !float.IsFinite(previous.Degrees) || !float.IsFinite(next.Degrees)
            || !float.IsFinite(radius) || radius < 0 || radius > 8
            || !float.IsFinite(footSnap) || footSnap < 0 || footSnap > 1)
            return false;
        Vector3 pivot = Center(mover);
        Vector3 axis = RotationAxis(mover);
        Vector3 local = pivot + Rotate(feet - previous.Offset - pivot, axis, -previous.Degrees);
        if (local.X < mover.Minimum.X - radius || local.X > mover.Maximum.X + radius
            || local.Z < mover.Minimum.Z - radius || local.Z > mover.Maximum.Z + radius
            || local.Y < mover.Maximum.Y - footSnap || local.Y > mover.Maximum.Y + footSnap)
            return false;
        carried = pivot + Rotate(local - pivot, axis, next.Degrees) + next.Offset;
        return Finite(carried) && (carried - feet).LengthSquared <= 64f;
    }

    /// <summary>
    /// Moving AABB versus a point/sphere motion, measured in the model's
    /// previous local frame. The actual runtime collider must still use BSP
    /// brush planes for sloped/rotating meshes, not this conservative broadphase.
    /// Returns [0..1] entry time to avoid tunneling past a moving platform.
    /// </summary>
    internal static bool SweptAabb(in Q3MoverDescriptor mover,
        in Q3MoverPose previous, in Q3MoverPose next,
        Vector3 start, Vector3 destination, float radius, out float enter)
    {
        enter = 0f;
        if (!Finite(start) || !Finite(destination) || !Finite(previous.Offset)
            || !Finite(next.Offset) || !float.IsFinite(radius) || radius < 0 || radius > 8)
            return false;
        // Rotating movers need OBB/convex brush narrow-phase. Do not claim an
        // AABB sweep is authoritative for an angled, swept rotating mesh.
        if (mover.Kind is Q3MoverKind.Pendulum or Q3MoverKind.Rotating
            && (MathF.Abs(previous.Degrees) > 0.0001f || MathF.Abs(next.Degrees) > 0.0001f))
            return false;
        Vector3 relativeStart = start - previous.Offset;
        Vector3 relativeTravel = destination - start - (next.Offset - previous.Offset);
        float exit = 1f;
        for (int axis = 0; axis < 3; axis++)
        {
            float from = relativeStart[axis], move = relativeTravel[axis];
            float lower = mover.Minimum[axis] - radius, upper = mover.Maximum[axis] + radius;
            if (MathF.Abs(move) < 0.000001f)
            {
                if (from < lower || from > upper) return false;
                continue;
            }
            float t0 = (lower - from) / move, t1 = (upper - from) / move;
            enter = MathF.Max(enter, MathF.Min(t0, t1));
            exit = MathF.Min(exit, MathF.Max(t0, t1));
            if (enter > exit) return false;
        }
        return enter <= 1 && exit >= 0;
    }
}
