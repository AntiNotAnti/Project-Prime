using System;

namespace MphRead.Entities;

/// <summary>
/// Pure utility for short-horizon combat positioning. No learning, randomness,
/// or accesses to hidden enemy state are performed by the scoring function.
/// </summary>
internal static class BotPositionUtility
{
    internal static float Score(float currentDistance, float candidateDistance,
        float preferredDistance, bool lowHealth, bool isCover, float enemyFacing,
        float crowdPenalty, float routeCommitment)
    {
        if (!float.IsFinite(currentDistance) || !float.IsFinite(candidateDistance)
            || !float.IsFinite(preferredDistance) || !float.IsFinite(enemyFacing)
            || !float.IsFinite(crowdPenalty) || !float.IsFinite(routeCommitment))
        {
            return float.NegativeInfinity;
        }

        preferredDistance = MathF.Max(1f, preferredDistance);
        float oldError = MathF.Abs(currentDistance - preferredDistance);
        float nextError = MathF.Abs(candidateDistance - preferredDistance);
        float rangeBenefit = (oldError - nextError) * 2.7f;
        float score = rangeBenefit - Math.Clamp(crowdPenalty, 0, 20);

        if (lowHealth)
        {
            // Surviving and breaking LOS outrank preserving the ideal weapon
            // distance when the bot is seriously wounded.
            score += (candidateDistance - currentDistance) * 2.2f;
            if (isCover) score += 22;
        }
        else
        {
            // Negative dot: this candidate is behind an opponent's facing,
            // not directly in the center of their aim cone.
            score += Math.Clamp(-enemyFacing, -1f, 1f) * 7f;
            if (isCover) score -= 3;
        }

        return score + Math.Clamp(routeCommitment, 0, 5f);
    }
}
