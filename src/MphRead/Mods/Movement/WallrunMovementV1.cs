using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// All constants use world units/second and fixed 60 Hz ticks. Contact and
// lifetime admission belong to the native controller, not this math helper.
internal static class WallrunMovementV1
{
    internal const int AirborneTicks = 45;
    internal const float GravityScale = .35f;
    internal const float MinimumTangentSpeed = TraversalMovementProfileV1.GroundSpeed * .5f;
    internal const float KickOutwardSpeed = 8;
    internal const float KickUpSpeed = 9;

    internal static Vector3 Kick(Vector3 velocity, Vector3 outward)
    {
        if (!float.IsFinite(velocity.LengthSquared) || !float.IsFinite(outward.LengthSquared)
            || outward.Y != 0 || MathF.Abs(outward.LengthSquared - 1) > .00001f) return velocity;
        float delta = MathF.Max(0, KickOutwardSpeed - Vector3.Dot(velocity, outward));
        Vector3 kicked = velocity + outward * delta;
        return new(kicked.X, MathF.Max(velocity.Y, KickUpSpeed), kicked.Z);
    }
}
