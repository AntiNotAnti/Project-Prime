using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// world units and seconds only. The caller owns fixed-tick admission,
// accepted identity/nonce, native velocity conversion, gravity and collision.
// No scene, input, raycast, mutable state, Position or PrevPosition ownership.
internal static class GrappleMovementV1
{
    internal const float MinRange = 2f;
    internal const float MaxRange = 24f;
    internal const int MaxTicks = 180;
    internal const int TicksPerSecond = 60;
    internal const float InwardAcceleration = 12f;
    internal const float MaximumAssistPerTick = InwardAcceleration / TicksPerSecond;
    internal const float TautTolerance = .05f;
    internal const float MinimumDirectionDistance = .0001f;

    internal static bool Finite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    internal static bool ValidAcceptedLength(float length)
        => float.IsFinite(length) && length >= MinRange && length <= MaxRange;

    // elapsedTicks is zero for the first application, 179 for the last.
    // A false result cancels eligibility; the caller must not install result.
    // Position is an immutable value used only to compute radial direction.
    internal static bool TryConstrainVelocity(Vector3 position, Vector3 anchor,
        float acceptedLength, Vector3 velocityUnitsPerSecond, int elapsedTicks,
        out Vector3 resultUnitsPerSecond)
    {
        resultUnitsPerSecond = default;
        if (elapsedTicks < 0 || elapsedTicks >= MaxTicks
            || !Finite(position) || !Finite(anchor) || !Finite(velocityUnitsPerSecond)
            || !ValidAcceptedLength(acceptedLength)) return false;

        Vector3 radial = position - anchor;
        float squaredDistance = radial.LengthSquared;
        if (!float.IsFinite(squaredDistance)
            || squaredDistance <= MinimumDirectionDistance * MinimumDirectionDistance
            || squaredDistance > MaxRange * MaxRange) return false;
        float distance = MathF.Sqrt(squaredDistance);
        radial /= distance; // Unit vector pointing away from the anchor.
        float outwardSpeed = Vector3.Dot(velocityUnitsPerSecond, radial);
        if (!float.IsFinite(outwardSpeed)) return false;

        Vector3 constrained = velocityUnitsPerSecond;
        if (distance >= acceptedLength - TautTolerance && outwardSpeed > 0)
            constrained -= radial * outwardSpeed;

        // Assistance is bounded to exactly 12/60=.2 world units/second per tick.
        // Removing outward radial velocity is a dissipative constraint and can
        // remove more than .2; it never adds an outward/tangential impulse.
        // Preserve all incoming inward velocity, then add the bounded assist.
        constrained -= radial * MaximumAssistPerTick;
        if (!Finite(constrained)) return false;
        resultUnitsPerSecond = constrained;
        return true;
    }
}
