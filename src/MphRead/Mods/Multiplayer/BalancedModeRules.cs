using System;

namespace MphRead.Mods.Multiplayer;

/// <summary>
/// Authoritative combat transforms used only while Balanced Mode is enabled.
/// Keep balance data here rather than scattering weapon-specific constants
/// through projectile collision code so later tuning remains inspectable.
/// </summary>
public readonly record struct BalancedHunterProfile(
    int SpawnHealthBonus,
    float IncomingDamageMultiplier,
    float AltTractionMultiplier,
    float AltSpeedCapMultiplier,
    float KnockbackMultiplier)
{
    public static BalancedHunterProfile Baseline => new(0, 1f, 1f, 1f, 1f);
}

public static class BalancedModeRules
{
    public const float MidRange = 12f;
    public const float FarRange = 24f;

    public static BalancedHunterProfile HunterProfile(Hunter hunter)
        => hunter switch
        {
            // Endurance + mobility. The health is spawn health only so the
            // global 199-HP ceiling and 200-damage Imperialist breakpoint stay intact.
            Hunter.Kanden => new(10, 1f, 1.12f, 1.08f, 1f),

            // Tank. Spire trades speed for the strongest body/splash durability
            // and substantially less displacement from explosive pressure.
            Hunter.Spire => new(0, 0.90f, 1.05f, 1.05f, 0.80f),

            // Duel/control. A smaller durability edge plus better alt mobility
            // without making Vhoscythe's freeze or raw attack damage stronger.
            Hunter.Noxus => new(0, 0.95f, 1.10f, 1.08f, 1f),

            // Bruiser. More health at the start of each life and a more useful
            // Halfturret movement state, while raw damage stays untouched.
            Hunter.Weavel => new(10, 1f, 1.10f, 1.08f, 1f),

            _ => BalancedHunterProfile.Baseline
        };

    public static int SpawnHealth(Hunter hunter, int baseHealth, int maxHealth)
        => Math.Min(maxHealth, baseHealth + HunterProfile(hunter).SpawnHealthBonus);

    public static uint ScaleIncomingDamage(Hunter hunter, uint damage, bool headshot)
    {
        if (damage == 0 || headshot)
        {
            return damage;
        }
        float multiplier = HunterProfile(hunter).IncomingDamageMultiplier;
        if (multiplier == 1f)
        {
            return damage;
        }
        return (uint)Math.Max(1, (int)MathF.Round(damage * multiplier));
    }

    public static float ScaleAltTraction(Hunter hunter, float traction)
        => traction * HunterProfile(hunter).AltTractionMultiplier;

    public static float ScaleAltSpeedCap(Hunter hunter, float speed)
        => speed * HunterProfile(hunter).AltSpeedCapMultiplier;

    public static float ScaleKnockback(Hunter hunter, float amount)
        => amount * HunterProfile(hunter).KnockbackMultiplier;

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

    public static bool HasProjectileSpeedTuning(BeamType beam)
        => beam is BeamType.VoltDriver or BeamType.Battlehammer
            or BeamType.Judicator or BeamType.Magmaul;

    public static float ProjectileSpeedMultiplier(BeamType beam, bool charged)
        => beam switch
        {
            // 20480 -> 24576 uncharged. Charged Volt gets the larger bump
            // (7168 -> 10240) so a committed long-range shot is not a slow orb.
            BeamType.VoltDriver => charged ? 10f / 7f : 1.20f,

            // Prediction weapons become more credible against modern movement
            // without approaching the Imperialist's near-hitscan identity.
            BeamType.Battlehammer => 1.25f,
            BeamType.Judicator => 1.25f,

            // Keep Magmaul dodgeable. Its reward comes mainly from the close
            // damage curve; this is only enough speed to reduce sluggishness.
            BeamType.Magmaul => 7680f / 6963f,

            _ => 1f
        };

    public static float ScaleProjectileSpeed(BeamType beam, bool charged, float speed)
        => speed * ProjectileSpeedMultiplier(beam, charged);

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
