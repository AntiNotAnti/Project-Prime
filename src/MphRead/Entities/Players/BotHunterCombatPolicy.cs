using System;
using MphRead.Formats;

namespace MphRead.Entities;

/// <summary>
/// Deterministic per-Hunter combat decisions. Only currently visible targets
/// are supplied; this class never resolves hidden enemy positions or modifies
/// combat damage, mobility caps, cooldowns, or ammunition.
/// </summary>
internal enum BotHunterAltAction : byte
{
    None,
    Bomb,
    Lunge,
    Spin,
    Slam,
    Boost,
    HoldTurret
}

internal static class BotHunterCombatPolicy
{
    internal static bool ShouldEnterAlt(Hunter hunter, float distance, float healthFraction,
        bool targetVisible, bool grounded, bool navigationCommitted, bool targetFrozen,
        bool onObjective)
    {
        if (!targetVisible || !grounded || navigationCommitted
            || !float.IsFinite(distance) || !float.IsFinite(healthFraction))
        {
            return false;
        }
        healthFraction = Math.Clamp(healthFraction, 0f, 1f);
        return hunter switch
        {
            Hunter.Samus => distance >= 2.5f && distance <= 7f && healthFraction <= 0.37f,
            Hunter.Kanden => distance >= 1.8f && distance <= 5.5f && healthFraction >= 0.32f,
            Hunter.Trace => distance >= 1.5f && distance <= 4.5f && healthFraction >= 0.38f,
            Hunter.Sylux => distance >= 2f && distance <= 5.8f && healthFraction >= 0.35f,
            Hunter.Noxus => targetFrozen && distance <= 3.5f && healthFraction >= 0.40f,
            Hunter.Spire => distance <= 3.5f && healthFraction >= 0.50f,
            Hunter.Weavel => distance >= 4.5f && distance <= 11f
                && (healthFraction <= 0.60f || onObjective),
            _ => false
        };
    }

    internal static bool ShouldExitAlt(Hunter hunter, int controlledTicks,
        bool targetVisible, float distance)
    {
        if (controlledTicks < 24) return false;
        int maximum = hunter == Hunter.Weavel ? 108 : 78;
        if (controlledTicks >= maximum) return true;
        if (!targetVisible) return controlledTicks >= 40;
        if (!float.IsFinite(distance)) return true;
        float maximumCombatRange = hunter switch
        {
            Hunter.Weavel => 16f,
            Hunter.Samus => 14f,
            Hunter.Trace => 9f,
            _ => 10f
        };
        return distance > maximumCombatRange;
    }

    internal static BotHunterAltAction ChooseAltAction(Hunter hunter,
        float distance, bool targetVisible, bool targetFrozen)
    {
        if (!targetVisible || !float.IsFinite(distance) || distance < 0)
            return BotHunterAltAction.None;
        return hunter switch
        {
            Hunter.Samus when distance >= 6f && distance <= 13f => BotHunterAltAction.Boost,
            Hunter.Kanden when distance <= 4.5f => BotHunterAltAction.Bomb,
            Hunter.Trace when distance <= 3.8f => BotHunterAltAction.Lunge,
            Hunter.Sylux when distance <= 6f => BotHunterAltAction.Bomb,
            Hunter.Noxus when targetFrozen && distance <= 3f => BotHunterAltAction.Spin,
            Hunter.Spire when distance <= 3.4f => BotHunterAltAction.Slam,
            Hunter.Weavel when distance <= 3.4f => BotHunterAltAction.Lunge,
            Hunter.Weavel => BotHunterAltAction.HoldTurret,
            _ => BotHunterAltAction.None
        };
    }

    internal static bool WantsChargedAffinity(Hunter hunter, BeamType weapon,
        float distance, bool targetFrozen)
    {
        if (!float.IsFinite(distance)) return false;
        return hunter switch
        {
            Hunter.Samus => weapon == BeamType.Missile && distance >= 8f && distance <= 25f,
            Hunter.Kanden => weapon == BeamType.VoltDriver && distance >= 10f,
            Hunter.Noxus => weapon == BeamType.Judicator && !targetFrozen && distance >= 5f && distance <= 18f,
            Hunter.Spire => weapon == BeamType.Magmaul && distance >= 5f && distance <= 16f,
            _ => false
        };
    }
}
