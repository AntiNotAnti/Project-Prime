using System;
using MphRead.Formats;
using MphRead.Metadata;

namespace MphRead.Entities;

/// <summary>
/// Bounded, explainable native-weapon value based on current visible movement.
/// This is a small preference adjustment; real ammo, damage, shot creation,
/// impact and hit tests still belong entirely to native weapon rules.
/// </summary>
internal static class BotWeaponTactics
{
    internal static float HitProbability(float distance, float lateralPerTick,
        float projectileSpeed, bool frozen)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(lateralPerTick)
            || !float.IsFinite(projectileSpeed))
            return 0;
        if (frozen) return .97f;
        float ticks = Math.Clamp(distance / Math.Max(.1f, projectileSpeed), 0f, 90f);
        float travel = MathF.Abs(lateralPerTick) * ticks;
        return Math.Clamp(.92f - travel * .12f, .20f, .92f);
    }

    internal static float EstimatedCombatValue(WeaponInfo weapon, BeamType beam,
        float distance, float lateralSpeed, bool frozen, int difficulty, bool balancedMode)
    {
        if (weapon == null || difficulty < 2 || !float.IsFinite(distance))
            return 0;
        float velocity = Math.Max(.1f, weapon.UnchargedSpeed / 4096f / 2f);
        float chance = HitProbability(distance, lateralSpeed, velocity, frozen);
        float nativeCooldown = Math.Max(1f, weapon.ShotCooldown * 2f);
        float nativeDamage = weapon.UnchargedDamage;
        if (balancedMode && beam == BeamType.Battlehammer)
            nativeDamage *= 3f; // Three native burst shots, not modified actual damage.

        // A conservative fraction of expected uncharged damage per firing
        // interval. Close-range/splash and affinity remain scored separately.
        float value = (nativeDamage / nativeCooldown) * chance * 5.5f;
        if (beam == BeamType.ShockCoil)
            value = Math.Min(value, 12f); // Continuous beam has its own cadence.
        return Math.Clamp(value, 0, 16f);
    }
}
