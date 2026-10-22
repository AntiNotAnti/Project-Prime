using System;
using OpenTK.Mathematics;

namespace MphRead.Mods.Movement;

internal enum MomentumContactPhase : byte
{
    Airborne,
    Grounded,
    Landing,
    LegalJump
}

internal readonly record struct MomentumProfileResult(Vector3 Velocity, bool SpeedLimited, bool IncomingOverCap);

/// <summary>
/// Immutable movement profile. Stock seven-Hunter calibration is identical;
/// native jump/ability/impulse ownership stays outside this horizontal policy.
/// Changes to these coefficients require a new profile version once recorded.
/// </summary>
internal static class MomentumProfileV1
{
    internal const int MomentumProfileVersion = 1;
    internal const float GroundSpeed = 983f / 4096 * 30;
    // Initial private safety budget, pending map/combat acceptance in PR-04/09.
    internal const float RetentionMultiplierCap = 1.5f;
    internal const float RetentionSpeedCap = GroundSpeed * RetentionMultiplierCap;
    internal static MomentumTuning Tuning => new(450f * 60 / 983,
        (1 - 2785f / 4096) * 30, 0, 450f * 60 / 983,
        GroundSpeed * (1 - 3481f / 4096));

    // No auto-hop, buffer, grace timer or parallel jump state. An accepted native
    // jump alone earns the legal-jump label; native collision owns landing flags.
    internal static MomentumContactPhase Contact(bool standing, bool standingPrevious,
        float verticalVelocity, bool nativeJumpAccepted)
    {
        if (!float.IsFinite(verticalVelocity)) throw new ArgumentOutOfRangeException(nameof(verticalVelocity));
        if (nativeJumpAccepted) return MomentumContactPhase.LegalJump;
        // Native slope collision can project horizontal motion into positive Y
        // while retaining Standing. Only the accepted native jump bypasses
        // friction on contact; active external impulses bypass the adapter.
        if (!standing) return MomentumContactPhase.Airborne;
        return standingPrevious ? MomentumContactPhase.Grounded : MomentumContactPhase.Landing;
    }

    internal static bool UsesGroundFriction(MomentumContactPhase contact) =>
        contact is MomentumContactPhase.Grounded or MomentumContactPhase.Landing;

    internal static MomentumProfileResult Step(Vector3 velocity, in MomentumInput input)
    {
        Vector3 result = MomentumMath.Step(velocity, input, Tuning);
        double beforeSquared = (double)velocity.X * velocity.X + (double)velocity.Z * velocity.Z;
        double regularCapSquared = (double)RetentionSpeedCap * RetentionSpeedCap;
        bool incomingOverCap = beforeSquared > regularCapSquared;
        // Active native impulses bypass this adapter. Residual incoming overspeed
        // is never flattened: only further steering-generated growth is limited.
        // Ordinary solver-only motion therefore cannot create overspeed itself.
        double allowedSquared = Math.Max(regularCapSquared, beforeSquared);
        double afterSquared = (double)result.X * result.X + (double)result.Z * result.Z;
        bool limited = afterSquared > allowedSquared;
        if (limited)
        {
            double scale = Math.Sqrt(allowedSquared / afterSquared);
            result = new Vector3(RoundInward(result.X * scale), velocity.Y, RoundInward(result.Z * scale));
            // Also cover the last double sqrt/division rounding bit. Otherwise
            // a float just outside the circle becomes next tick's incoming
            // overspeed budget and repeated air turns can ratchet it upward.
            if ((double)result.X * result.X + (double)result.Z * result.Z > allowedSquared)
            {
                if (MathF.Abs(result.X) >= MathF.Abs(result.Z)) result.X = TowardZero(result.X);
                else result.Z = TowardZero(result.Z);
            }
        }
        return new(result, limited, incomingOverCap);
    }

    private static float RoundInward(double value)
    {
        float rounded = (float)value;
        return Math.Abs(rounded) > Math.Abs(value) ? TowardZero(rounded) : rounded;
    }

    private static float TowardZero(float value) => value > 0 ? MathF.BitDecrement(value)
        : value < 0 ? MathF.BitIncrement(value) : value;
}
