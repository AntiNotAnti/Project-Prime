using System;

namespace MphRead.Entities;

/// <summary>
/// Fair goal commitment: distant threats do not instantly cancel a scoring
/// route, but immediate combat still takes precedence.
/// </summary>
internal enum BotGoalKind : byte
{
    Objective,
    Pickup,
    Escort
}

internal static class BotGoalPolicy
{
    /// <summary>
    /// One utility scale for map-level candidates. Combat interruptions are
    /// evaluated separately because they can preempt any selected plan.
    /// </summary>
    internal static float Utility(BotGoalKind kind, float baseScore,
        float healthFraction, bool currentlyCommitted, bool contested)
    {
        if (!float.IsFinite(baseScore) || !float.IsFinite(healthFraction))
            return float.NegativeInfinity;
        healthFraction = Math.Clamp(healthFraction, 0f, 1f);
        float bonus = kind switch
        {
            BotGoalKind.Objective => contested ? 18f : 8f,
            BotGoalKind.Escort => 5f,
            BotGoalKind.Pickup => healthFraction < 0.4f ? 12f : 0f,
            _ => 0f
        };
        if (currentlyCommitted) bonus += 8f;
        // A wounded bot must not abandon a genuine recovery opportunity to
        // chase a score zone unless the team's position is immediately contested.
        if (kind == BotGoalKind.Objective && healthFraction < 0.35f && !contested)
            bonus -= 40f;
        return baseScore + bonus;
    }

    internal static bool WantsObjective(bool objectiveAvailable,
        bool opponentVisible, float enemyDistance, float healthFraction)
    {
        if (!objectiveAvailable || !float.IsFinite(healthFraction)
            || healthFraction < 0.35f)
            return false;
        return !opponentVisible || (float.IsFinite(enemyDistance) && enemyDistance > 12f);
    }

    internal static bool KeepCommittedGoal(bool committed, bool objectiveGoal,
        bool visibleEnemy, float enemyDistance, float healthFraction)
    {
        if (!committed || !float.IsFinite(healthFraction))
            return false;
        healthFraction = Math.Clamp(healthFraction, 0f, 1f);
        if (objectiveGoal && healthFraction < 0.30f)
            return false;
        if (!visibleEnemy)
            return true;
        if (!float.IsFinite(enemyDistance))
            return false;
        // Pickups yield to any visible enemy; objectives only yield if
        // combat is immediately threatening the bot.
        return objectiveGoal && enemyDistance > 8f;
    }
}
