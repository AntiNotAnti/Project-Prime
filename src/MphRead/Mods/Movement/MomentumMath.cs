using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

/// <summary>
/// Horizontal solver coefficients, not an approved gameplay profile. Rates are
/// per second; speeds use the same units/second as the supplied velocity.
/// All coefficients must be finite and nonnegative. Zero disables that term.
/// </summary>
internal readonly record struct MomentumTuning(
    float GroundAcceleration,
    float GroundFriction,
    float StopSpeed,
    float AirAcceleration,
    float AirWishSpeedCap);

/// <summary>
/// Signed strafe/forward axes and the corresponding world-space basis. Basis
/// vectors are projected onto X/Z and normalized; they must be orthogonal.
/// DesiredSpeed is the full-deflection speed before radial input scaling.
/// DeltaSeconds is simulation time, never presentation time in a runtime adapter.
/// </summary>
internal readonly record struct MomentumInput(
    Vector2 MoveAxes,
    Vector3 FlatForward,
    Vector3 FlatRight,
    float DesiredSpeed,
    bool Grounded,
    float DeltaSeconds);

/// <summary>
/// Independent, stateless horizontal acceleration/friction math. No position,
/// jump, gravity, contact, impulse classification or gameplay ownership lives
/// here. PR-01 deliberately has no runtime call site.
/// </summary>
internal static class MomentumMath
{
    // Basis error tolerance, not a gameplay tuning coefficient. Accept float
    // normalization noise while rejecting skew that would distort analog input.
    private const double BasisDotTolerance = 0.0001;

    /// <summary>
    /// Returns a new velocity, preserving Y exactly. Ground friction precedes
    /// acceleration. Air caps only the wish-direction projection, so existing
    /// perpendicular/externally supplied momentum is not magnitude-clamped.
    /// Invalid values or an unrepresentable result throw before any state changes.
    /// The valid path allocates no managed memory.
    /// </summary>
    internal static Vector3 Step(Vector3 velocity, in MomentumInput input, in MomentumTuning tuning)
    {
        ValidateVector(velocity, nameof(velocity));
        if (!float.IsFinite(input.MoveAxes.X) || !float.IsFinite(input.MoveAxes.Y))
            throw new ArgumentOutOfRangeException(nameof(input.MoveAxes));
        ValidateNonnegative(input.DesiredSpeed, nameof(input.DesiredSpeed));
        ValidateNonnegative(input.DeltaSeconds, nameof(input.DeltaSeconds));
        ValidateNonnegative(tuning.GroundAcceleration, nameof(tuning.GroundAcceleration));
        ValidateNonnegative(tuning.GroundFriction, nameof(tuning.GroundFriction));
        ValidateNonnegative(tuning.StopSpeed, nameof(tuning.StopSpeed));
        ValidateNonnegative(tuning.AirAcceleration, nameof(tuning.AirAcceleration));
        ValidateNonnegative(tuning.AirWishSpeedCap, nameof(tuning.AirWishSpeedCap));

        NormalizeBasis(input.FlatForward, nameof(input.FlatForward), out double fx, out double fz);
        NormalizeBasis(input.FlatRight, nameof(input.FlatRight), out double rx, out double rz);
        if (Math.Abs(fx * rx + fz * rz) > BasisDotTolerance)
            throw new ArgumentException("Planar forward and right must be orthogonal.", nameof(input));

        if (input.DeltaSeconds == 0)
            return velocity;

        // Double intermediates prevent overflow for finite float inputs, including
        // squared lengths and products of rate/speed/dt. Output remains Vector3.
        double vx = velocity.X, vz = velocity.Z;
        double dt = input.DeltaSeconds;
        if (input.Grounded && tuning.GroundFriction > 0)
        {
            double speed = Math.Sqrt(vx * vx + vz * vz);
            if (speed > 0)
            {
                double drop = Math.Max(speed, tuning.StopSpeed) * tuning.GroundFriction * dt;
                double retained = Math.Max(0, speed - drop) / speed;
                vx *= retained;
                vz *= retained;
            }
        }

        double x = input.MoveAxes.X, y = input.MoveAxes.Y;
        double magnitude = Math.Sqrt(x * x + y * y);
        if (magnitude > 0 && input.DesiredSpeed > 0)
        {
            // Normalize direction separately from magnitude; quarter-stick stays
            // quarter-speed. Digital diagonals/out-of-range axes saturate radially.
            double wishX = rx * (x / magnitude) + fx * (y / magnitude);
            double wishZ = rz * (x / magnitude) + fz * (y / magnitude);
            double wishLength = Math.Sqrt(wishX * wishX + wishZ * wishZ);
            wishX /= wishLength;
            wishZ /= wishLength;
            double wishSpeed = input.DesiredSpeed * Math.Min(1, magnitude);
            double target = input.Grounded ? wishSpeed : Math.Min(wishSpeed, tuning.AirWishSpeedCap);
            double available = Math.Max(0, target - (vx * wishX + vz * wishZ));
            double rate = input.Grounded ? tuning.GroundAcceleration : tuning.AirAcceleration;
            double increment = Math.Min(available, rate * wishSpeed * dt);
            vx += wishX * increment;
            vz += wishZ * increment;
        }

        // Very large but finite inputs may imply an output outside Vector3's
        // representable range. Reject rather than returning Infinity or silently
        // clipping an external impulse. No gameplay safety ceiling is chosen here.
        if (vx > float.MaxValue || vx < -float.MaxValue
            || vz > float.MaxValue || vz < -float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(velocity), "Horizontal result exceeds the finite float range.");
        return new Vector3((float)vx, velocity.Y, (float)vz);
    }

    private static void ValidateNonnegative(float value, string name)
    {
        if (!float.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateVector(Vector3 value, string name)
    {
        if (!float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z))
            throw new ArgumentOutOfRangeException(name);
    }

    private static void NormalizeBasis(Vector3 basis, string name, out double x, out double z)
    {
        ValidateVector(basis, name);
        double length = Math.Sqrt((double)basis.X * basis.X + (double)basis.Z * basis.Z);
        if (length == 0)
            throw new ArgumentException("Movement basis must have a nonzero planar component.", name);
        x = basis.X / length;
        z = basis.Z / length;
    }
}
