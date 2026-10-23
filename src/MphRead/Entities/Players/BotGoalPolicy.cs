using System;

namespace MphRead.Entities;

/// <summary>
/// Fair goal commitment: distant threats do not instantly cancel a scoring
/// route, but immediate combat still takes precedence.
/// </summary>
internal static class BotGoalPolicy
{
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
