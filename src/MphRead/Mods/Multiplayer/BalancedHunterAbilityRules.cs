using System;

namespace MphRead.Mods.Multiplayer;

/// <summary>
/// Native Hunter ability refinements for Balanced Mode.
///
/// This layer intentionally has no dependency on Enhanced Hunters. It only
/// adjusts stock alt-form movement, timing, and native spawned entities while
/// Balanced Mode is active.
/// </summary>
internal static class BalancedHunterAbilityRules
{
    internal const float KandenAcquireRange = 6.5f;
    internal const float KandenLaunchSpeed = 0.36f;
    internal const float KandenSteeringFactor = 0.0625f; // stock 0.05 * 1.25
    internal const float KandenCooldownMultiplier = 0.85f;

    internal const float SpireClimbSpeedMultiplier = 1.20f;
    internal const int SpireLedgeGraceFrames = 9; // 150 ms at 60 Hz
    internal const float SpireClimbSteeringMultiplier = 1.15f;
    internal const float SpireCrestInwardSpeed = 0.115f;
    internal const float SpireCrestUpSpeed = 0.132f;

    internal const float NoxusStartupMultiplier = 0.75f;
    internal const float NoxusAttackSteeringMultiplier = 1.20f;

    internal const float WeavelAltMovementMultiplier = 1.15f;
    internal const float WeavelTransitionDamageMultiplier = 0.75f;

    internal static int KandenCooldownFrames(int stockFrames)
        => Math.Max(1, (int)MathF.Round(stockFrames * KandenCooldownMultiplier));

    internal static int NoxusStartupFrames(int stockFrames)
        => Math.Max(1, (int)MathF.Round(stockFrames * NoxusStartupMultiplier));

    internal static float RollTraction(Hunter hunter, bool spireClimbing,
        bool noxusAttacking, float stockTraction)
    {
        float multiplier = BalancedModeRules.HunterProfile(hunter).AltTractionMultiplier;
        if (hunter == Hunter.Spire && spireClimbing)
        {
            multiplier = Math.Max(multiplier, SpireClimbSteeringMultiplier);
        }
        else if (hunter == Hunter.Noxus && noxusAttacking)
        {
            multiplier = Math.Max(multiplier, NoxusAttackSteeringMultiplier);
        }
        return stockTraction * multiplier;
    }

    internal static float StrafeTraction(Hunter hunter, float stockTraction)
    {
        float multiplier = BalancedModeRules.HunterProfile(hunter).AltTractionMultiplier;
        if (hunter == Hunter.Weavel)
        {
            multiplier = Math.Max(multiplier, WeavelAltMovementMultiplier);
        }
        return stockTraction * multiplier;
    }

    // Base animation advancement is one frame every two simulation frames.
    // One extra frame every 10 sim frames = 20% faster; every 8 = 25% faster.
    internal static bool ExtraSpireAttackAnimationFrame(ulong frame)
        => frame != 0 && frame % 10 == 1;

    internal static bool ExtraWeavelTransitionAnimationFrame(ulong frame)
        => frame != 0 && frame % 8 == 1;

    internal static uint WeavelTransitionDamage(uint damage, bool headshot, bool transitioning)
    {
        if (damage == 0 || headshot || !transitioning)
        {
            return damage;
        }
        return (uint)Math.Max(1, (int)MathF.Round(damage * WeavelTransitionDamageMultiplier));
    }
}
