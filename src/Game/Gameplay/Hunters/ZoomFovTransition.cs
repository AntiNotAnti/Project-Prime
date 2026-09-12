using System;

namespace MphRead.Entities;

/// <summary>
/// Keeps zoom transitions responsive at the fixed 60 Hz simulation rate while
/// the renderer interpolates the resulting samples at presentation cadence.
/// </summary>
internal static class ZoomFovTransition
{
    private const float ZoomInStepDegreesPerTick = 4;
    private const float LegacyReturnBlend = 0.25f;
    private const float SnapThresholdDegrees = 0.2f;

    private static readonly float ReturnBlendPerTick = 1 - MathF.Pow(
        1 - LegacyReturnBlend, SimTicks.LegacyHz / (float)SimTicks.Hz);

    public static float StepToward(float currentFov, float targetFov)
    {
        if (targetFov > currentFov)
        {
            return Math.Min(currentFov + ZoomInStepDegreesPerTick, targetFov);
        }
        if (targetFov < currentFov)
        {
            return Math.Max(currentFov - ZoomInStepDegreesPerTick, targetFov);
        }
        return currentFov;
    }

    public static float StepBackToNormal(float currentFov, float normalFov)
    {
        float difference = normalFov - currentFov;
        if (MathF.Abs(difference) < SnapThresholdDegrees)
        {
            return normalFov;
        }
        return currentFov + difference * ReturnBlendPerTick;
    }
}
