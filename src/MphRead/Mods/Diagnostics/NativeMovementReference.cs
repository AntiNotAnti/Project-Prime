using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Diagnostics
{
    /// <summary>
    /// Collision-free native-cadence movement reference. This deliberately models
    /// only state whose 30 Hz ordering is understood: horizontal impulse/cap,
    /// horizontal damping, gravity, position integration and facing convergence.
    /// Collision/contact is excluded until the dedicated collision-shadow slice.
    /// </summary>
    internal readonly record struct NativeMovementReferenceState(
        Vector3 Position,
        Vector3 Velocity,
        Vector3 Facing);

    internal readonly record struct NativeMovementReferenceInput(
        Vector3 HorizontalImpulse,
        float HorizontalSpeedCap,
        float HorizontalMultiplier,
        bool ApplyGravity,
        float Gravity,
        float FacingCoefficient,
        Vector3 FacingTarget);

    internal static class NativeMovementReference
    {
        internal static NativeMovementReferenceState Step(
            in NativeMovementReferenceState state,
            in NativeMovementReferenceInput input)
        {
            if (!float.IsFinite(input.HorizontalSpeedCap) || input.HorizontalSpeedCap < 0)
                throw new ArgumentOutOfRangeException(nameof(input.HorizontalSpeedCap));
            if (!float.IsFinite(input.HorizontalMultiplier) || input.HorizontalMultiplier < 0)
                throw new ArgumentOutOfRangeException(nameof(input.HorizontalMultiplier));
            if (!float.IsFinite(input.Gravity))
                throw new ArgumentOutOfRangeException(nameof(input.Gravity));
            if (!float.IsFinite(input.FacingCoefficient)
                || input.FacingCoefficient < 0 || input.FacingCoefficient > 1)
                throw new ArgumentOutOfRangeException(nameof(input.FacingCoefficient));

            Vector3 velocity = state.Velocity;
            float horizontalBefore = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            velocity.X += input.HorizontalImpulse.X;
            velocity.Z += input.HorizontalImpulse.Z;
            float horizontalAfter = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
            if (input.HorizontalSpeedCap > 0 && horizontalAfter > horizontalBefore
                && horizontalAfter > input.HorizontalSpeedCap)
            {
                float target = horizontalBefore <= input.HorizontalSpeedCap
                    ? input.HorizontalSpeedCap
                    : horizontalBefore;
                float factor = target / horizontalAfter;
                velocity.X *= factor;
                velocity.Z *= factor;
            }

            velocity.X *= input.HorizontalMultiplier;
            velocity.Z *= input.HorizontalMultiplier;
            if (input.ApplyGravity)
            {
                velocity.Y += input.Gravity;
            }

            Vector3 position = state.Position + velocity;
            Vector3 facing = state.Facing;
            if (input.FacingCoefficient > 0)
            {
                facing += (input.FacingTarget - facing) * input.FacingCoefficient;
                if (facing.LengthSquared > 0)
                {
                    facing = facing.Normalized();
                }
            }

            return new NativeMovementReferenceState(position, velocity, facing);
        }
    }
}
