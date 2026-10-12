using System;
using OpenTK.Mathematics;

namespace MphRead.Entities
{
    /// <summary>
    /// Deterministic, allocation-free rate shaping used by the authoritative bot motor.
    /// This changes requested inputs, never physics, hit detection, or player speed caps.
    /// </summary>
    internal static class BotMotorMath
    {
        internal static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (MathF.Abs(delta) <= maxDelta)
            {
                return target;
            }
            return current + MathF.CopySign(maxDelta, delta);
        }

        internal static float StepMoveAxis(float current, float desired,
            float accelerationPerTick, float brakingPerTick)
        {
            if (!float.IsFinite(current) || !float.IsFinite(desired)
                || !float.IsFinite(accelerationPerTick) || !float.IsFinite(brakingPerTick)
                || accelerationPerTick <= 0 || brakingPerTick <= 0)
            {
                return 0;
            }

            current = Math.Clamp(current, -1, 1);
            desired = Math.Clamp(desired, -1, 1);
            // Reversals always pass through a stop, instead of instantly jumping
            // from full-left to full-right digital traction.
            bool braking = current * desired < 0 || MathF.Abs(desired) < MathF.Abs(current);
            float amount = braking ? brakingPerTick : accelerationPerTick;
            float next = MoveTowards(current, desired, amount);
            return MathF.Abs(next) < 0.001f ? 0 : next;
        }

        internal static float SafePitchDegrees(Vector3 direction)
        {
            if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y)
                || !float.IsFinite(direction.Z) || direction.LengthSquared <= 0.00000001f)
            {
                return 0;
            }
            // The legacy hearing/position vectors are not unit length. Computing
            // acos(raw Y) caused NaN turn requests for ordinary height differences.
            float y = Math.Clamp(direction.Y / MathF.Sqrt(direction.LengthSquared), -1, 1);
            return MathF.Asin(y) * (180f / MathF.PI);
        }

        internal static float StepAimAxis(ref float angularVelocity, float requestedDelta,
            float maxDegreesPerTick, float accelerationDegreesPerTick)
        {
            if (!float.IsFinite(angularVelocity) || !float.IsFinite(requestedDelta)
                || !float.IsFinite(maxDegreesPerTick) || !float.IsFinite(accelerationDegreesPerTick)
                || maxDegreesPerTick <= 0 || accelerationDegreesPerTick <= 0)
            {
                angularVelocity = 0;
                return 0;
            }

            angularVelocity = Math.Clamp(angularVelocity,
                -maxDegreesPerTick, maxDegreesPerTick);

            // Brake as the aim error becomes small. Otherwise a smooth high-speed
            // flick oscillates around the target when the sign changes.
            float remaining = MathF.Abs(requestedDelta);
            float stoppingRate = MathF.Sqrt(2 * accelerationDegreesPerTick * remaining);
            float targetRate = MathF.CopySign(
                MathF.Min(maxDegreesPerTick, MathF.Min(remaining, stoppingRate)),
                requestedDelta);
            if (remaining < 0.0001f)
            {
                targetRate = 0;
            }
            angularVelocity = MoveTowards(angularVelocity, targetRate, accelerationDegreesPerTick);
            float output = Math.Clamp(angularVelocity, -remaining, remaining);
            if (MathF.Abs(output) < MathF.Abs(angularVelocity))
            {
                angularVelocity = output;
            }
            return output;
        }
    }
}
