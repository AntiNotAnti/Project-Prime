using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Caller supplies a current-tick static face contact and calls at most once per
// simulation tick, after native collision clipping. No temporal state or grace.
internal static class WallstrafeMovementV1
{
    internal const float Acceleration = 30f;
    internal const float StepSeconds = 1f / 60f;
    internal const float MaxTangentSpeed = TraversalMovementProfileV1.GroundSpeed * 1.5f;
    private const float Epsilon = .000001f;

    // Wish and outward normal must be horizontal unit vectors. Positive forward
    // plus nonzero strafe, into-wall wish, and nonzero tangent are all required.
    // Opposing tangent velocity receives no bonus: ordinary movement must first
    // turn it around. Existing over-cap speed is never clamped or accelerated.
    internal static Vector3 Step(Vector3 velocity, float forward, float strafe,
        Vector3 wish, Vector3 wallNormal, bool eligible, bool contact, bool corner)
    {
        if (!eligible || !contact || corner || !Finite(velocity) || !Finite(wish)
            || !Finite(wallNormal) || !float.IsFinite(forward) || !float.IsFinite(strafe)
            || forward <= 0 || forward > 1 || MathF.Abs(strafe) <= Epsilon
            || MathF.Abs(strafe) > 1 || wish.Y != 0 || wallNormal.Y != 0
            || MathF.Abs(wish.LengthSquared - 1) > .00001f
            || MathF.Abs(wallNormal.LengthSquared - 1) > .00001f)
            return velocity;

        // A perpendicular constructed from the normal avoids slope/Y impulses.
        Vector3 tangent = new(-wallNormal.Z, 0, wallNormal.X);
        tangent /= MathF.Sqrt(tangent.LengthSquared);
        float into = Vector3.Dot(wish, wallNormal);
        float along = Vector3.Dot(wish, tangent);
        if (into >= -Epsilon || MathF.Abs(along) <= Epsilon) return velocity;
        if (along < 0) tangent = -tangent;
        float speed = Vector3.Dot(velocity, tangent);
        if (speed < 0 || speed >= MaxTangentSpeed) return velocity;
        float delta = MathF.Min(Acceleration * StepSeconds, MaxTangentSpeed - speed);
        Vector3 result = velocity + tangent * delta;
        return new Vector3(result.X, velocity.Y, result.Z);
    }

    private static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
