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
        // No per-observation allocations in the 60-Hz bot hot path.
        return reacquired
            ? level switch { 0 => 12, 1 => 7, 2 => 5, _ => 3 }
            : level switch { 0 => 26, 1 => 16, 2 => 10, _ => 7 };
    }
}
