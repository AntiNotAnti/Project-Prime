using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

// Explicit opt-in ruleset. Existing Quake III captures retain their v2 contract.
internal static class TraversalMovementProfileV1
{
    internal const int ProfileVersion = 1;
    internal const float GroundSpeed = Quake3MovementProfileV2.GroundSpeed;
    internal const float AirTurnRadiansPerSecond = 1.8f;
    internal static MomentumTuning Tuning => new(10, 6, GroundSpeed * .3125f, 2, GroundSpeed);

    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input)
    {
        Vector3 result = MomentumMath.Step(velocity, input with { DesiredSpeed = GroundSpeed }, Tuning);
        if (!input.Grounded && input.MoveAxes.LengthSquared > 0 && input.DeltaSeconds > 0)
        {
            double forwardLength = Math.Sqrt((double)input.FlatForward.X * input.FlatForward.X
                + (double)input.FlatForward.Z * input.FlatForward.Z);
            double rightLength = Math.Sqrt((double)input.FlatRight.X * input.FlatRight.X
                + (double)input.FlatRight.Z * input.FlatRight.Z);
            double wishX = input.FlatRight.X / rightLength * input.MoveAxes.X
                + input.FlatForward.X / forwardLength * input.MoveAxes.Y;
            double wishZ = input.FlatRight.Z / rightLength * input.MoveAxes.X
                + input.FlatForward.Z / forwardLength * input.MoveAxes.Y;
            double speed = Math.Sqrt((double)result.X * result.X + (double)result.Z * result.Z);
            if ((wishX != 0 || wishZ != 0) && speed > 0)
            {
                double current = Math.Atan2(result.Z, result.X), target = Math.Atan2(wishZ, wishX);
                double angle = Math.IEEERemainder(target - current, Math.Tau);
                double turn = AirTurnRadiansPerSecond * (double)input.DeltaSeconds
                    * Math.Min(1, input.MoveAxes.Length);
                angle = current + Math.Clamp(angle, -turn, turn);
                // Steering changes direction only. Acceleration above is the
                // sole source of horizontal speed; external impulses survive.
                double x = Math.Cos(angle) * speed, z = Math.Sin(angle) * speed;
                if (Math.Abs(x) > float.MaxValue || Math.Abs(z) > float.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(velocity), "Steered result exceeds the finite float range.");
                result = new Vector3((float)x, result.Y, (float)z);
            }
        }
        return new(result, false, false);
    }

    internal static float RampLift(Vector3 velocity, Vector3 normal)
    {
        if (!Finite(velocity) || !Finite(normal)) return 0;
        // Walkable, genuinely sloped support only. The live adapter supplies
        // this tick's collision witness, never a grace-period ground flag.
        if (normal.Y <= .5f || normal.Y >= .99f || velocity.Y <= 0
            || velocity.X * normal.X + velocity.Z * normal.Z >= 0) return 0;
        return Math.Min(velocity.Y, new Vector2(velocity.X, velocity.Z).Length);
    }

    private static bool Finite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
}
