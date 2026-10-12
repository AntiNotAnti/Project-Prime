using System;

namespace MphRead.Entities;

/// <summary>
/// Bounded visual acquisition delays. Reaction tuning is a simulation policy,
/// not a multiplier applied to weapon damage, movement speed or target position.
/// </summary>
internal static class BotReactionPolicy
{
    internal static int AcquisitionFrames(int difficulty, bool reacquired)
    {
        int level = Math.Clamp(difficulty, 0, 3);
        return reacquired
            ? new[] { 12, 7, 5, 3 }[level]
            : new[] { 26, 16, 10, 7 }[level];
    }
}
