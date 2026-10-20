using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Ground travel is solved in a horizontal heading with surface-speed magnitude,
// then redirected onto its supporting plane. This keeps the existing planar
// acceleration rules without repeatedly charging uphill motion for its ascent.
internal static class GroundSlopeMovement
{
    internal const float SlideGravity = 6;

    internal static bool Walkable(Vector3 normal) => float.IsFinite(normal.LengthSquared)
        && MathF.Abs(normal.LengthSquared - 1) < .002f && normal.Y > .5f;

    internal static Vector3 Flatten(Vector3 velocity)
    {
        Vector3 heading = velocity.WithY(0);
        return heading.LengthSquared > 1e-12f ? heading.Normalized() * velocity.Length : Vector3.Zero;
    }

    internal static Vector3 Follow(Vector3 heading, Vector3 normal)
    {
        if (!Walkable(normal) || !float.IsFinite(heading.LengthSquared))
            throw new ArgumentException("Ground travel requires a finite heading and walkable unit normal.");
        heading.Y = 0;
        float speed = heading.Length;
        Vector3 tangent = heading.WithY(-(heading.X * normal.X + heading.Z * normal.Z) / normal.Y);
        return tangent.LengthSquared > 1e-12f ? tangent.Normalized() * speed : Vector3.Zero;
    }

    internal static Vector3 Clip(Vector3 velocity, Vector3 normal)
    {
        float inward = Vector3.Dot(velocity, normal);
        if (inward >= 0) return velocity;
        Vector3 tangent = velocity - normal * inward;
        return tangent.LengthSquared > 1e-12f ? tangent.Normalized() * velocity.Length : Vector3.Zero;
    }

    // Once per fixed tick, never once per triangle. Friction remains in the
    // profile; this bounded tangent force gradually exchanges height and speed.
    internal static Vector3 Slide(Vector3 velocity, Vector3 normal, float seconds)
    {
        if (!Walkable(normal) || !float.IsFinite(seconds) || seconds < 0 || seconds > 1f / 60 + 1e-6f)
            throw new ArgumentException("Slide slope force requires one fixed tick and walkable support.");
        Vector3 gravity = -Vector3.UnitY * SlideGravity;
        return velocity + (gravity - normal * Vector3.Dot(gravity, normal)) * seconds;
    }
}
