using System;

namespace MphRead.Mods.Multiplayer;

/// <summary>
/// Authoritative combat transforms used only while Balanced Mode is enabled.
/// Keep balance data here rather than scattering weapon-specific constants
/// through projectile collision code so later tuning remains inspectable.
/// </summary>
public static class BalancedModeRules
{
    public const float MidRange = 12f;
    public const float FarRange = 24f;

    public static bool HasRangeDamageCurve(BeamType beam)
        => beam is BeamType.VoltDriver or BeamType.Magmaul;

    public static float RangeDamageMultiplier(BeamType beam, float distance)
    {
        distance = Math.Max(0, distance);
        return beam switch
        {
            // Volt is intentionally weakest in a point-blank scramble and
            // strongest when the projectile is earned across the arena.
            BeamType.VoltDriver => SmoothCurve(distance,
                close: 0.80f, middle: 1.00f, far: 1.20f),

            // Magmaul is the inverse: oppressive when a player commits to
            // close range, ordinary around mid range, and increasingly poor
            // as a safe long-distance spam tool.
            BeamType.Magmaul => SmoothCurve(distance,
                close: 1.20f, middle: 1.00f, far: 0.75f),

            _ => 1f
        };
    }

    public static float ScaleRangeDamage(BeamType beam, float damage, float distance)
    {
        if (damage <= 0 || !HasRangeDamageCurve(beam))
        {
            return damage;
        }
        return damage * RangeDamageMultiplier(beam, distance);
    }

    private static float SmoothCurve(float distance, float close, float middle, float far)
    {
        if (distance <= MidRange)
        {
            return Lerp(close, middle, SmoothStep(distance / MidRange));
        }
        if (distance >= FarRange)
        {
            return far;
        }
        return Lerp(middle, far, SmoothStep((distance - MidRange) / (FarRange - MidRange)));
    }

    private static float SmoothStep(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
